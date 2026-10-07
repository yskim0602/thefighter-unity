using UnityEngine;

namespace TheFighter
{
    public enum FlowState
    {
        Home,
        Offers,
        PreFight,
        Fighting,
        Result,
        Training,
        Records
    }

    /// Owns the career and decides which screen the player is on. The fight is driven through
    /// BoxingBootstrap.ApplySetup and read back through FightOutcome, so this is the only class
    /// that knows both halves exist.
    ///
    /// The ring is built once and stands behind the menus rather than being torn down - the
    /// camera keeps drifting over two idle fighters, which costs nothing and gives every screen a
    /// backdrop for free.
    public class GameFlow : MonoBehaviour
    {
        public BoxingBootstrap Ring;

        public CareerData Career { get; private set; }
        public FlowState State { get; private set; }
        public MatchOffer SignedOffer { get; private set; }
        public FightOutcome LastOutcome { get; private set; }
        public int LastPurse { get; private set; }
        /// Stage and record from before the last result, so the result screen can show what moved.
        public int PreviousStage { get; private set; }
        public bool PromotedLastFight { get; private set; }

        void Awake()
        {
            if (Ring == null)
            {
                Ring = GetComponent<BoxingBootstrap>();
            }
        }

        void Start()
        {
            Career = CareerSave.LoadOrCreate();
            EnsureOffers();
            Enter(FlowState.Home);
        }

        void Update()
        {
            // Only the fight gets watched, and only while we believe one is running: the director
            // sits in Finished whenever it is idle, which would otherwise read as a result.
            if (State != FlowState.Fighting)
            {
                return;
            }

            if (Ring != null && Ring.Director != null && Ring.Director.MatchOver)
            {
                FinishFight();
            }
        }

        // ------------------------------------------------------------------
        // Screens
        // ------------------------------------------------------------------

        public void Enter(FlowState next)
        {
            State = next;
            bool fighting = next == FlowState.Fighting;

            if (Ring == null)
            {
                return;
            }

            if (Ring.Hud != null)
            {
                Ring.Hud.Mode = fighting ? FightHud.HudMode.Full : FightHud.HudMode.Off;
            }

            // Menus need the pointer back, and a menu is not the place to be holding a hit stop.
            if (Ring.CameraRig != null)
            {
                Ring.CameraRig.SetMouseLook(fighting && !Application.isMobilePlatform);
            }
            if (!fighting)
            {
                Time.timeScale = 1f;
            }
        }

        // ------------------------------------------------------------------
        // Offers
        // ------------------------------------------------------------------

        /// Keeps a live batch on the table. Called after anything that moves the clock, since an
        /// offer expiring is how declining costs you something.
        public void EnsureOffers()
        {
            if (Career == null)
            {
                return;
            }

            MatchGenerator.PruneExpired(Career);
            if (Career.Offers.Count == 0)
            {
                MatchGenerator.Refresh(Career);
            }
        }

        public void Sign(MatchOffer offer)
        {
            if (offer == null)
            {
                return;
            }

            SignedOffer = offer;
            Enter(FlowState.PreFight);
        }

        public void DeclineAll()
        {
            Career.Offers.Clear();
            Career.Week += CareerConfig.DeclineAllWeeks;
            Career.DeclineStreak++;
            Career.Condition = Mathf.Min(CareerConfig.MaxCondition,
                Career.Condition + CareerConfig.ConditionPerRestWeek);

            EnsureOffers();
            Save();
        }

        // ------------------------------------------------------------------
        // The fight
        // ------------------------------------------------------------------

        public void StartSignedFight()
        {
            if (SignedOffer == null || Ring == null)
            {
                return;
            }

            PreviousStage = Career.Stage;
            Ring.ApplySetup(FightSetup.From(Career, SignedOffer));
            Enter(FlowState.Fighting);
        }

        void FinishFight()
        {
            FightOutcome outcome = FightOutcome.From(Ring.Director, SignedOffer.Rounds);
            LastOutcome = outcome;

            // Worked out before ApplyOutcome, which is what spends it.
            LastPurse = Career.PurseFor(SignedOffer, outcome);

            Career.ApplyOutcome(SignedOffer, outcome);
            PromotedLastFight = Career.Stage > PreviousStage;

            EnsureOffers();
            Save();
            Enter(FlowState.Result);
        }

        // ------------------------------------------------------------------
        // Weeks
        // ------------------------------------------------------------------

        public void Rest()
        {
            Career.Rest();
            EnsureOffers();
            Save();
        }

        public bool Train(TrainingFocus focus)
        {
            if (!TrainingPlan.Train(Career, focus))
            {
                return false;
            }

            EnsureOffers();
            Save();
            return true;
        }

        public void SetStance(Stance stance)
        {
            if (Ring != null && !Ring.SouthpawAvailable && stance == Stance.Southpaw)
            {
                return;
            }

            Career.Stance = stance;
            Save();
        }

        // ------------------------------------------------------------------

        public void Save()
        {
            CareerSave.Save(Career);
        }

        public void NewCareer()
        {
            CareerSave.Delete();
            Career = new CareerData();
            SignedOffer = null;
            LastOutcome = null;
            EnsureOffers();
            Save();
            Enter(FlowState.Home);
        }

        void OnApplicationPause(bool paused)
        {
            // Mobile does not promise a quit callback, so this is where a save actually has to
            // happen. Harmless on desktop.
            if (paused)
            {
                Save();
            }
        }

        void OnApplicationQuit()
        {
            Save();
        }
    }
}

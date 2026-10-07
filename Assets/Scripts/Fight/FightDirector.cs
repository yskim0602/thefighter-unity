using UnityEngine;

namespace TheFighter
{
    public enum MatchPhase
    {
        Opening,
        Round,
        Rest,
        Finished
    }

    /// Runs the match as a real bout rather than a death match: opening bell, rounds on a clock,
    /// a corner in between, and a verdict at the end that can be a knockout, a decision on three
    /// judges' cards, or a draw.
    ///
    /// Distance is a field, not a constant, because that is how the career will make a title fight
    /// feel different from your debut - see RoundRules.RoundsForStage.
    public class FightDirector : MonoBehaviour
    {
        public Fighter Player;
        public Fighter Enemy;
        public ImpactFeedback Feedback;

        [Header("Distance")]
        public int TotalRounds = RoundRules.DebutRounds;
        public float RoundSeconds = RoundRules.RoundSeconds;
        public float RestSeconds = RoundRules.RestSeconds;

        [Header("Who starts the fight")]
        /// Off when a career owns the flow - the scene stands ready and the bell waits for a
        /// signed offer. R also stops restarting, since in a career a fight happens once.
        public bool AutoStart = true;

        public MatchPhase Phase { get; private set; }
        public int CurrentRound { get; private set; }
        public float PhaseRemaining { get; private set; }
        public Scorecard Card { get; private set; }
        public MatchDecision Decision { get; private set; }
        public Fighter Winner { get; private set; }

        public bool MatchOver { get { return Phase == MatchPhase.Finished; } }
        public HitEvent LastHit { get; private set; }
        public float LastHitAge { get { return Time.unscaledTime - _lastHitTime; } }

        Vector3 _playerSpawn;
        Vector3 _enemySpawn;
        float _lastHitTime = -99f;

        void Start()
        {
            if (Player == null || Enemy == null)
            {
                return;
            }

            _playerSpawn = Player.transform.position;
            _enemySpawn = Enemy.transform.position;

            Subscribe(Player);
            Subscribe(Enemy);

            if (AutoStart)
            {
                StartMatch();
                return;
            }

            // Stand the fighters up and leave them idle until someone calls StartMatch.
            PlaceFighters();
            Player.FightActive = false;
            Enemy.FightActive = false;
            EnterPhase(MatchPhase.Finished, 0f);
        }

        void Subscribe(Fighter fighter)
        {
            fighter.Landed += OnLanded;
            fighter.Whiffed += OnWhiffed;
            fighter.Threw += OnThrew;
            fighter.KnockedOut += OnKnockedOut;
        }

        // ------------------------------------------------------------------
        // Match flow
        // ------------------------------------------------------------------

        public void StartMatch()
        {
            Time.timeScale = 1f;

            Card = new Scorecard();
            Card.NewMatch();

            CurrentRound = 0;
            Winner = null;
            Decision = new MatchDecision();

            PlaceFighters();
            Player.ResetForFight();
            Enemy.ResetForFight();
            Player.FightActive = false;
            Enemy.FightActive = false;

            EnterPhase(MatchPhase.Opening, RoundRules.OpeningSeconds);
        }

        void PlaceFighters()
        {
            Vector3 toEnemy = _enemySpawn - _playerSpawn;
            toEnemy.y = 0f;
            toEnemy.Normalize();

            Player.GetComponent<FighterMotor>().Teleport(_playerSpawn,
                Quaternion.LookRotation(toEnemy, Vector3.up));
            Enemy.GetComponent<FighterMotor>().Teleport(_enemySpawn,
                Quaternion.LookRotation(-toEnemy, Vector3.up));
        }

        void Update()
        {
            if (AutoStart && Input.GetKeyDown(KeyCode.R))
            {
                StartMatch();
                return;
            }

            if (Phase == MatchPhase.Finished)
            {
                return;
            }

            PhaseRemaining -= Time.deltaTime;
            if (PhaseRemaining > 0f)
            {
                return;
            }

            switch (Phase)
            {
                case MatchPhase.Opening:
                    BeginRound(1);
                    break;
                case MatchPhase.Round:
                    RingBell();
                    break;
                case MatchPhase.Rest:
                    BeginRound(CurrentRound + 1);
                    break;
            }
        }

        void EnterPhase(MatchPhase phase, float seconds)
        {
            Phase = phase;
            PhaseRemaining = seconds;
        }

        void BeginRound(int round)
        {
            CurrentRound = round;

            Player.BeginRound();
            Enemy.BeginRound();
            Player.FightActive = true;
            Enemy.FightActive = true;

            EnterPhase(MatchPhase.Round, RoundSeconds);
        }

        void RingBell()
        {
            Player.FightActive = false;
            Enemy.FightActive = false;

            Card.ScoreRound();

            if (CurrentRound >= TotalRounds)
            {
                FinishByDecision();
                return;
            }

            // The bell saves whoever was on the canvas; it still cost them the round.
            Player.RecoverInCorner(CurrentRound, TotalRounds);
            Enemy.RecoverInCorner(CurrentRound, TotalRounds);

            EnterPhase(MatchPhase.Rest, RestSeconds);
        }

        void FinishByDecision()
        {
            MatchDecision decision = Card.Decide();
            Decision = decision;
            Winner = decision.IsDraw ? null : (decision.PlayerWon ? Player : Enemy);
            Finish();
        }

        void Finish()
        {
            Phase = MatchPhase.Finished;
            PhaseRemaining = 0f;
            Player.FightActive = false;
            Enemy.FightActive = false;
        }

        // ------------------------------------------------------------------
        // Feeding the card
        // ------------------------------------------------------------------

        void OnLanded(HitEvent evt)
        {
            LastHit = evt;
            _lastHitTime = Time.unscaledTime;

            if (Feedback != null)
            {
                Feedback.Report(evt);
            }

            if (Phase != MatchPhase.Round || Card == null)
            {
                return;
            }

            Scorecard.Tally attacker = evt.Attacker == Player ? Card.Player : Card.Enemy;
            Scorecard.Tally defender = evt.Defender == Player ? Card.Player : Card.Enemy;

            switch (evt.Result)
            {
                case HitResult.Clean:
                    attacker.Landed++;
                    attacker.Damage += evt.Damage;
                    if (evt.CausedKnockdown)
                    {
                        attacker.KnockdownsScored++;
                        defender.KnockdownsSuffered++;
                    }
                    break;

                case HitResult.Blocked:
                    defender.Blocked++;
                    break;

                case HitResult.Dodged:
                    defender.Slipped++;
                    break;
            }
        }

        void OnThrew(Fighter fighter, PunchDefinition punch)
        {
            if (Phase != MatchPhase.Round || Card == null)
            {
                return;
            }

            Scorecard.Tally tally = fighter == Player ? Card.Player : Card.Enemy;
            tally.Thrown++;
        }

        void OnWhiffed(Fighter fighter, PunchDefinition punch)
        {
            if (Feedback != null)
            {
                Feedback.ReportWhiff(fighter, punch);
            }
        }

        void OnKnockedOut(Fighter fighter)
        {
            Winner = fighter == Player ? Enemy : Player;

            MatchDecision decision = new MatchDecision();
            decision.Kind = DecisionKind.Knockout;
            decision.PlayerWon = Winner == Player;
            Decision = decision;

            Finish();
        }
    }
}

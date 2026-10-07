namespace TheFighter
{
    /// What the fight hands back. Read off the director and the card once, at the final bell, so
    /// the career never has to reach into the fight's live state.
    public class FightOutcome
    {
        public bool PlayerWon;
        public bool Draw;
        public DecisionKind Kind;
        public int ScheduledRounds;
        public int EndedRound;

        public int KnockdownsScored;
        public int KnockdownsSuffered;
        public float DamageDealt;
        public float DamageTaken;
        public int PunchesThrown;
        public int PunchesLanded;

        public float Accuracy
        {
            get { return PunchesThrown > 0 ? PunchesLanded / (float)PunchesThrown : 0f; }
        }

        /// How the result gets written in the record book. A knockout that happened on the last
        /// scheduled round is still a knockout, so this reads the decision rather than the clock.
        public string Method
        {
            get
            {
                switch (Kind)
                {
                    case DecisionKind.Knockout:
                        return KnockdownsSuffered >= CombatTuning.MaxKnockdowns
                            || KnockdownsScored >= CombatTuning.MaxKnockdowns ? "TKO" : "KO";
                    case DecisionKind.Unanimous: return "UD";
                    case DecisionKind.Split: return "SD";
                    case DecisionKind.Majority: return "MD";
                    default: return "D";
                }
            }
        }

        /// Pulls the result out of a finished fight. The totals come from the card's running
        /// tallies plus the round in progress, because a knockout ends a round that never got
        /// scored and those punches still happened.
        public static FightOutcome From(FightDirector director, int scheduledRounds)
        {
            FightOutcome outcome = new FightOutcome();
            outcome.ScheduledRounds = scheduledRounds;
            outcome.EndedRound = UnityEngine.Mathf.Max(1, director.CurrentRound);
            outcome.Kind = director.Decision.Kind;
            outcome.Draw = director.Decision.IsDraw;
            outcome.PlayerWon = !outcome.Draw && director.Decision.PlayerWon;

            if (director.Card == null)
            {
                return outcome;
            }

            Scorecard.Tally player = director.Card.PlayerSoFar();
            Scorecard.Tally enemy = director.Card.EnemySoFar();

            outcome.PunchesThrown = player.Thrown;
            outcome.PunchesLanded = player.Landed;
            outcome.DamageDealt = player.Damage;
            outcome.DamageTaken = enemy.Damage;
            outcome.KnockdownsScored = player.KnockdownsScored;
            outcome.KnockdownsSuffered = player.KnockdownsSuffered;

            return outcome;
        }
    }
}

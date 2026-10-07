using UnityEngine;

namespace TheFighter
{
    /// Everything the fight needs to know, and nothing about the career it came from.
    ///
    /// This boundary is the point. A fight that reads career state cannot be tested on its own,
    /// and testing a fight on its own - press play, box, change a number, box again - is what has
    /// made every tuning pass so far possible. Hand the fight a setup and take an outcome back,
    /// and the two halves stay independently debuggable.
    [System.Serializable]
    public class FightSetup
    {
        public string PlayerName = "YOU";
        public FighterStats PlayerStats = new FighterStats();
        public Stance PlayerStance = Stance.Orthodox;

        public string OpponentName = "OPPONENT";
        public FighterStats OpponentStats = new FighterStats();
        public BoxingStyle OpponentStyle = BoxingStyle.BoxerPuncher;
        public Stance OpponentStance = Stance.Orthodox;
        [Range(0f, 1f)] public float OpponentSkill = 0.55f;

        public int Rounds = RoundRules.DebutRounds;
        public bool TitleFight;

        /// Builds the setup for a signed offer. Condition is folded into the player's stats here
        /// rather than carried into the fight as a separate dial: the fight already knows how to
        /// be worse at fighting with lower stats, and one mechanism beats two.
        public static FightSetup From(CareerData career, MatchOffer offer)
        {
            FightSetup setup = new FightSetup();

            setup.PlayerName = career.FighterName;
            setup.PlayerStats = career.Stats.Clone();
            setup.PlayerStats.Scale(CareerConfig.ConditionScale(career.Condition));
            setup.PlayerStance = career.Stance;

            setup.OpponentName = offer.Opponent.Name;
            setup.OpponentStats = offer.Opponent.Stats.Clone();
            setup.OpponentStyle = offer.Opponent.Style;
            setup.OpponentStance = offer.Opponent.Stance;
            setup.OpponentSkill = offer.Opponent.AiSkill;

            setup.Rounds = offer.Rounds;
            setup.TitleFight = offer.TitleFight;

            return setup;
        }
    }
}

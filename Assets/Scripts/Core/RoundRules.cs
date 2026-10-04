namespace TheFighter
{
    /// Round length, distance and judging. Real boxing's *structure* is what makes a fight feel
    /// like a fight - the bell, pacing across rounds, the corner, a card you can be losing on -
    /// so all of that is kept. Its *duration* is not: a 12 x 3 minute championship is 47 minutes,
    /// and nobody grinds a career out of that on a phone. So the clock is compressed and the
    /// structure is left alone.
    public static class RoundRules
    {
        // --- Clock ---------------------------------------------------------

        /// Half a real round. Long enough that pacing and gas are genuine decisions and the bell
        /// comes as a relief; short enough that no single round outstays its welcome.
        public const float RoundSeconds = 90f;

        /// The corner beat, down from a real minute. Enough to read the cards and breathe without
        /// dropping you out of the fight. Corner work and celebrations will live in this window.
        public const float RestSeconds = 12f;

        public const float OpeningSeconds = 2.5f;

        // --- Distance ------------------------------------------------------
        // Distance grows with the career, which is both how boxing works and how a player comes
        // to feel like a real fighter: your debut is over before you are tired, a title fight is
        // an ordeal you have to survive.
        //
        //   3 rounds -> ~4.9 min      6 rounds -> ~10.0 min
        //   4 rounds -> ~6.6 min      8 rounds -> ~13.4 min
        //   5 rounds -> ~8.3 min

        public const int DebutRounds = 3;
        public const int TitleRounds = 8;

        /// Career stage 1-10 -> rounds. Called by the career layer once it exists.
        public static int RoundsForStage(int stage)
        {
            if (stage <= 2) { return 3; }
            if (stage <= 4) { return 4; }
            if (stage <= 6) { return 5; }
            if (stage <= 8) { return 6; }
            return TitleRounds;
        }

        // --- Corner recovery -----------------------------------------------
        // You do not heal in boxing, but a fight that never recovers anything ends by TKO every
        // time and the cards never matter. So the corner gives a little back - and gives back
        // less the deeper the fight goes, which is what makes late rounds frightening.

        public const float RestStaminaRatio = 0.80f;
        public const float RestStaminaPerEndurance = 0.004f;
        public const float RestHealthRatio = 0.06f;
        public const float RestHealthPerEndurance = 0.0015f;
        /// How much of the corner's effect is lost by the final round.
        public const float RestHealthFade = 0.6f;
        /// However battered, you answer the bell with at least this much - otherwise a fighter who
        /// went down on the bell is knocked down again by the first jab of the next round.
        public const float CornerMinHealthRatio = 0.22f;

        // --- Judging -------------------------------------------------------
        // Ten-point must, three judges scoring independently. Each judge weights clean punching,
        // volume and defence slightly differently, which is what produces split decisions - and a
        // split decision is the most immersive thing a fight game can hand you.

        public const int JudgeCount = 3;
        /// Damage in a round that counts as thoroughly winning it.
        public const float ScoringDamageReference = 40f;
        /// Below this gap the judge calls the round even.
        public const float EvenRoundThreshold = 0.07f;
        public const int MinRoundScore = 6;
    }
}

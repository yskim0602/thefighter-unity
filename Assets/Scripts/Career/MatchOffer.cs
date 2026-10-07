namespace TheFighter
{
    /// A fight on the table. The three fields that matter are purse, difficulty and rounds, and
    /// they move together on purpose: the offer that pays is the offer that can end your streak.
    [System.Serializable]
    public class MatchOffer
    {
        public OpponentProfile Opponent = new OpponentProfile();
        public int Purse;
        public int Rounds;
        public int Stage;
        public int ExpiresWeek;
        public bool TitleFight;
        /// Opponent rating over yours at the moment the offer was made.
        public float Difficulty;
        public string Venue = "";

        public bool IsStepUp { get { return Difficulty >= CareerConfig.RiskyThreshold; } }
        public bool IsTuneUp { get { return Difficulty <= CareerConfig.SafeThreshold; } }

        public string TierName
        {
            get
            {
                if (TitleFight) { return "타이틀전"; }
                if (IsStepUp) { return "상위 랭커"; }
                if (IsTuneUp) { return "조정 경기"; }
                return "동급";
            }
        }

        /// What winning this one actually buys, which is the whole point of reading the offer.
        public string Reward
        {
            get
            {
                if (TitleFight) { return "챔피언"; }
                if (IsStepUp) { return "승격"; }
                if (IsTuneUp) { return "자금만"; }
                return "승격 진행";
            }
        }
    }
}

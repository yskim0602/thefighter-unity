namespace TheFighter
{
    /// One line of a career record. Kept flat and primitive so JsonUtility can round-trip it -
    /// it has no idea what a property or a dictionary is.
    [System.Serializable]
    public class FightRecord
    {
        public int Week;
        public int Stage;
        public string OpponentName = "";
        public string OpponentRecord = "";
        public bool Won;
        public bool Draw;
        public int ScheduledRounds;
        public int EndedRound;
        /// "KO", "TKO", "UD", "SD", "MD", "D" - stored rather than derived, because how a result
        /// is written down is part of the record and the rules behind it may change.
        public string Method = "";
        public int Purse;
        public bool TitleFight;

        public string Result
        {
            get { return Draw ? "D" : (Won ? "W" : "L"); }
        }

        public string Line
        {
            get
            {
                return Result + "  " + Method + " R" + EndedRound + "/" + ScheduledRounds
                    + "  vs " + OpponentName;
            }
        }
    }
}

using UnityEngine;

namespace TheFighter
{
    /// A generated opponent. Everything the fight needs to build them, plus the record and
    /// nickname the offer screen needs to make them feel like somebody rather than a stat block.
    [System.Serializable]
    public class OpponentProfile
    {
        public string Name = "";
        public string Nickname = "";
        public FighterStats Stats = new FighterStats();
        public BoxingStyle Style = BoxingStyle.BoxerPuncher;
        public Stance Stance = Stance.Orthodox;
        public int Wins;
        public int Losses;
        public int Draws;
        public int Knockouts;
        /// Feeds AIBrain.Skill. A late-career opponent reads the fight better, not just harder.
        [Range(0f, 1f)] public float AiSkill = 0.55f;

        public string Record
        {
            get { return Wins + "-" + Losses + (Draws > 0 ? "-" + Draws : ""); }
        }

        public string Display
        {
            get
            {
                return string.IsNullOrEmpty(Nickname)
                    ? Name
                    : Name + " \"" + Nickname + "\"";
            }
        }
    }
}

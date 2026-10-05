namespace TheFighter
{
    /// The four trainable numbers behind every fighter. Everything the fight actually reads
    /// (health pool, gas tank, footwork speed, punch damage, recovery) is derived from these,
    /// so the career layer only ever has to raise the four ints.
    [System.Serializable]
    public class FighterStats
    {
        public int Power = 10;
        public int Endurance = 10;
        public int Speed = 10;
        public int Skill = 10;

        public FighterStats() { }

        public FighterStats(int power, int endurance, int speed, int skill)
        {
            Power = power;
            Endurance = endurance;
            Speed = speed;
            Skill = skill;
        }

        public FighterStats Clone()
        {
            return new FighterStats(Power, Endurance, Speed, Skill);
        }

        public void Scale(float multiplier)
        {
            Power = UnityEngine.Mathf.RoundToInt(Power * multiplier);
            Endurance = UnityEngine.Mathf.RoundToInt(Endurance * multiplier);
            Speed = UnityEngine.Mathf.RoundToInt(Speed * multiplier);
            Skill = UnityEngine.Mathf.RoundToInt(Skill * multiplier);
        }

        // Each stat owns one readable thing, so who a fighter is comes across from how they fight:
        // Power lands harder, Endurance lasts longer, Speed is quicker, Skill is cleaner.

        /// Endurance decides how much you can take. The spread is wide on purpose - a tank and a
        /// glass cannon should carry visibly different bars, not the same bar draining faster.
        public float MaxHealth { get { return 70f + Endurance * 5.5f; } }
        public float MaxStamina { get { return 60f + Endurance * 2.5f + Skill * 1.5f; } }

        public float MoveSpeed { get { return 1.15f + Speed * 0.05f; } }
        public float PunchDamage { get { return 5f + Power * 0.85f; } }

        /// Arm length from the shoulder joint, in metres.
        public float Reach { get { return 0.70f + Skill * 0.0025f; } }

        /// Speed drives how fast the glove gets there: windup and strike.
        public float PunchSpeedScale { get { return 1f / (0.80f + Speed * 0.020f); } }

        /// Skill drives how fast you reset afterwards, which is what makes a technical fighter
        /// hard to punish rather than merely fast.
        public float RecoveryScale { get { return 1f / (0.85f + Skill * 0.015f); } }
    }
}

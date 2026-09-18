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

        public float MaxHealth { get { return 80f + Endurance * 4f; } }
        public float MaxStamina { get { return 70f + Endurance * 2f + Skill * 1.5f; } }
        public float MoveSpeed { get { return 2.0f + Speed * 0.06f; } }
        public float PunchDamage { get { return 6f + Power * 0.7f; } }

        /// Arm length from the shoulder joint, in metres.
        public float Reach { get { return 0.70f + Skill * 0.0025f; } }

        /// Scales every punch's windup/strike/recovery. Higher skill = crisper punches.
        public float TimingScale { get { return 1f / (0.82f + Skill * 0.018f); } }
    }
}

using UnityEngine;

namespace TheFighter
{
    /// Multipliers a style lays on top of the raw stats. Everything is 1.0 for a boxer-puncher,
    /// so a profile reads as "how far from the all-rounder is this".
    public struct StyleProfile
    {
        public float Attack;
        public float Footwork;
        public float Health;
        public float Stamina;
        public float Defense;
        public float Range;

        /// Centre-to-centre distance in metres this style tries to hold. Keep every one of these
        /// inside the style's own jab range or the AI will circle politely and never throw.
        public float PreferredDistance;

        /// How eagerly the style throws once it is in range, 0..1.
        public float Aggression;
    }

    public static class BoxingStyles
    {
        public static readonly BoxingStyle[] All =
        {
            BoxingStyle.OutBoxer,
            BoxingStyle.InFighter,
            BoxingStyle.Slugger,
            BoxingStyle.BoxerPuncher,
            BoxingStyle.CounterPuncher,
            BoxingStyle.PressureFighter
        };

        public static StyleProfile Profile(BoxingStyle style)
        {
            StyleProfile p = new StyleProfile();
            p.Attack = 1f;
            p.Footwork = 1f;
            p.Health = 1f;
            p.Stamina = 1f;
            p.Defense = 1f;
            p.Range = 1f;
            p.PreferredDistance = 1.05f;
            p.Aggression = 0.5f;

            switch (style)
            {
                case BoxingStyle.OutBoxer:
                    p.Attack = 0.85f; p.Footwork = 1.20f; p.Health = 0.90f;
                    p.Stamina = 1.05f; p.Defense = 1.00f; p.Range = 1.15f;
                    p.PreferredDistance = 1.20f; p.Aggression = 0.42f;
                    break;
                case BoxingStyle.InFighter:
                    p.Attack = 1.15f; p.Footwork = 1.00f; p.Health = 1.00f;
                    p.Stamina = 0.95f; p.Defense = 0.85f; p.Range = 0.85f;
                    p.PreferredDistance = 0.88f; p.Aggression = 0.72f;
                    break;
                case BoxingStyle.Slugger:
                    p.Attack = 1.35f; p.Footwork = 0.80f; p.Health = 1.10f;
                    p.Stamina = 0.85f; p.Defense = 0.90f; p.Range = 0.95f;
                    p.PreferredDistance = 1.02f; p.Aggression = 0.55f;
                    break;
                case BoxingStyle.BoxerPuncher:
                    p.PreferredDistance = 1.08f; p.Aggression = 0.52f;
                    break;
                case BoxingStyle.CounterPuncher:
                    p.Attack = 1.05f; p.Footwork = 1.00f; p.Health = 1.00f;
                    p.Stamina = 1.00f; p.Defense = 1.25f; p.Range = 1.05f;
                    p.PreferredDistance = 1.12f; p.Aggression = 0.38f;
                    break;
                case BoxingStyle.PressureFighter:
                    p.Attack = 1.00f; p.Footwork = 1.10f; p.Health = 1.15f;
                    p.Stamina = 0.85f; p.Defense = 0.95f; p.Range = 0.95f;
                    p.PreferredDistance = 0.92f; p.Aggression = 0.78f;
                    break;
            }

            return p;
        }

        public static string DisplayName(BoxingStyle style)
        {
            switch (style)
            {
                case BoxingStyle.OutBoxer: return "OUT-BOXER";
                case BoxingStyle.InFighter: return "IN-FIGHTER";
                case BoxingStyle.Slugger: return "SLUGGER";
                case BoxingStyle.BoxerPuncher: return "BOXER-PUNCHER";
                case BoxingStyle.CounterPuncher: return "COUNTER-PUNCHER";
                case BoxingStyle.PressureFighter: return "PRESSURE FIGHTER";
                default: return style.ToString();
            }
        }

        static readonly PunchKind[] JabHeavy = { PunchKind.Jab, PunchKind.Jab, PunchKind.Straight, PunchKind.Hook };
        static readonly PunchKind[] CloseRange = { PunchKind.Hook, PunchKind.Hook, PunchKind.Uppercut, PunchKind.Jab };
        static readonly PunchKind[] PowerHeavy = { PunchKind.Hook, PunchKind.Uppercut, PunchKind.Straight, PunchKind.Straight };
        static readonly PunchKind[] Balanced = { PunchKind.Jab, PunchKind.Straight, PunchKind.Hook, PunchKind.Uppercut };
        static readonly PunchKind[] CounterSet = { PunchKind.Straight, PunchKind.Straight, PunchKind.Jab, PunchKind.Uppercut };
        static readonly PunchKind[] VolumeSet = { PunchKind.Jab, PunchKind.Hook, PunchKind.Straight, PunchKind.Hook };

        public static PunchKind[] PreferredPunches(BoxingStyle style)
        {
            switch (style)
            {
                case BoxingStyle.OutBoxer: return JabHeavy;
                case BoxingStyle.InFighter: return CloseRange;
                case BoxingStyle.Slugger: return PowerHeavy;
                case BoxingStyle.CounterPuncher: return CounterSet;
                case BoxingStyle.PressureFighter: return VolumeSet;
                default: return Balanced;
            }
        }

        /// The stat shape each style is "made of", compared against the player's trained spread.
        static Vector4 IdealStatShape(BoxingStyle style)
        {
            switch (style)
            {
                case BoxingStyle.OutBoxer: return new Vector4(0.6f, 0.8f, 1.3f, 1.3f);
                case BoxingStyle.InFighter: return new Vector4(1.2f, 1.1f, 0.9f, 0.8f);
                case BoxingStyle.Slugger: return new Vector4(1.5f, 1.0f, 0.6f, 0.7f);
                case BoxingStyle.CounterPuncher: return new Vector4(0.9f, 0.9f, 1.0f, 1.4f);
                case BoxingStyle.PressureFighter: return new Vector4(1.0f, 1.4f, 1.1f, 0.7f);
                default: return new Vector4(1f, 1f, 1f, 1f);
            }
        }

        /// The player never picks a style: whatever they trained decides it. Cosine similarity
        /// means only the *shape* of the spread matters, not how high the numbers have climbed.
        public static BoxingStyle Infer(FighterStats stats)
        {
            Vector4 mine = new Vector4(stats.Power, stats.Endurance, stats.Speed, stats.Skill);
            if (mine.sqrMagnitude < 0.0001f)
            {
                return BoxingStyle.BoxerPuncher;
            }

            mine.Normalize();

            BoxingStyle best = BoxingStyle.BoxerPuncher;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < All.Length; i++)
            {
                Vector4 ideal = IdealStatShape(All[i]).normalized;
                float score = Vector4.Dot(mine, ideal);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = All[i];
                }
            }

            return best;
        }
    }
}

namespace TheFighter
{
    [System.Serializable]
    public class PunchDefinition
    {
        public PunchKind Kind;
        public string DisplayName;
        public HandRole Hand;

        public float DamageMultiplier;
        public float StaminaCost;

        public float WindupTime;
        public float StrikeTime;
        public float RecoveryTime;

        public float RangeMultiplier;
        public float GuardPierce;
        public float HitRadius;

        /// 0 = aims at the body, 1 = aims at the head. Only the AI uses this; the player aims with the mouse.
        public float AimBias;

        /// Sideways bulge of the trajectory, in metres. Positive swings outward before coming back in (hook).
        public float LateralArc;

        /// Vertical dip before the glove rises into the target, in metres (uppercut).
        public float RiseArc;

        public float TotalTime { get { return WindupTime + StrikeTime + RecoveryTime; } }
    }

    public static class PunchLibrary
    {
        public static readonly PunchKind[] All =
        {
            PunchKind.Jab,
            PunchKind.Straight,
            PunchKind.Hook,
            PunchKind.Uppercut
        };

        public static readonly PunchDefinition Jab = new PunchDefinition
        {
            Kind = PunchKind.Jab,
            DisplayName = "JAB",
            Hand = HandRole.Lead,
            DamageMultiplier = 0.55f,
            StaminaCost = 5f,
            WindupTime = 0.08f,
            StrikeTime = 0.12f,
            RecoveryTime = 0.18f,
            RangeMultiplier = 1.05f,
            GuardPierce = 0f,
            HitRadius = 0.095f,
            AimBias = 0.85f,
            LateralArc = 0f,
            RiseArc = 0f
        };

        public static readonly PunchDefinition Straight = new PunchDefinition
        {
            Kind = PunchKind.Straight,
            DisplayName = "STRAIGHT",
            Hand = HandRole.Rear,
            DamageMultiplier = 1.0f,
            StaminaCost = 9f,
            WindupTime = 0.15f,
            StrikeTime = 0.13f,
            RecoveryTime = 0.28f,
            RangeMultiplier = 1.15f,
            GuardPierce = 0.05f,
            HitRadius = 0.10f,
            AimBias = 0.8f,
            LateralArc = 0f,
            RiseArc = 0f
        };

        public static readonly PunchDefinition Hook = new PunchDefinition
        {
            Kind = PunchKind.Hook,
            DisplayName = "HOOK",
            Hand = HandRole.Lead,
            DamageMultiplier = 1.15f,
            StaminaCost = 11f,
            WindupTime = 0.20f,
            StrikeTime = 0.15f,
            RecoveryTime = 0.34f,
            RangeMultiplier = 0.85f,
            GuardPierce = 0.20f,
            HitRadius = 0.11f,
            AimBias = 0.35f,
            LateralArc = 0.55f,
            RiseArc = 0f
        };

        public static readonly PunchDefinition Uppercut = new PunchDefinition
        {
            Kind = PunchKind.Uppercut,
            DisplayName = "UPPERCUT",
            Hand = HandRole.Rear,
            DamageMultiplier = 1.35f,
            StaminaCost = 13f,
            // Shortened from 0.26/0.17/0.42. A Mixamo uppercut take winds up from the hip and
            // finishes over the shoulder, and at 0.85s the whole windmill was on screen. A
            // quicker punch shows 28% less of the clip *and* is 28% quicker, both at life speed.
            //
            // Trimming the window instead would have been the obvious move and it does not work:
            // the window is scrubbed across the whole punch, so fewer frames in the same seconds
            // is slow motion, not a smaller punch. The duration is the real lever.
            WindupTime = 0.18f,
            StrikeTime = 0.13f,
            RecoveryTime = 0.30f,
            RangeMultiplier = 0.75f,
            GuardPierce = 0.35f,
            HitRadius = 0.11f,
            AimBias = 0.95f,
            LateralArc = 0.1f,
            // Less vertical throw as well - the clip's arc was the other half of "too big".
            RiseArc = 0.3f
        };

        public static PunchDefinition Get(PunchKind kind)
        {
            switch (kind)
            {
                case PunchKind.Jab: return Jab;
                case PunchKind.Straight: return Straight;
                case PunchKind.Hook: return Hook;
                case PunchKind.Uppercut: return Uppercut;
                default: return Jab;
            }
        }
    }
}

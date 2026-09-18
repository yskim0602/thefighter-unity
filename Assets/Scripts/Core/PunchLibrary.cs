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
            WindupTime = 0.06f,
            StrikeTime = 0.09f,
            RecoveryTime = 0.13f,
            RangeMultiplier = 1.05f,
            GuardPierce = 0f,
            HitRadius = 0.17f,
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
            WindupTime = 0.11f,
            StrikeTime = 0.10f,
            RecoveryTime = 0.21f,
            RangeMultiplier = 1.15f,
            GuardPierce = 0.05f,
            HitRadius = 0.18f,
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
            WindupTime = 0.15f,
            StrikeTime = 0.11f,
            RecoveryTime = 0.25f,
            RangeMultiplier = 0.85f,
            GuardPierce = 0.20f,
            HitRadius = 0.20f,
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
            WindupTime = 0.19f,
            StrikeTime = 0.12f,
            RecoveryTime = 0.31f,
            RangeMultiplier = 0.75f,
            GuardPierce = 0.35f,
            HitRadius = 0.20f,
            AimBias = 0.95f,
            LateralArc = 0.1f,
            RiseArc = 0.45f
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

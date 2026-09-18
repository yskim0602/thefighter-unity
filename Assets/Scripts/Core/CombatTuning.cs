namespace TheFighter
{
    /// Every feel-related constant in the fight lives here, the way CareerConfig held the career
    /// numbers in the Godot build. Balance passes should only ever need to touch this file.
    public static class CombatTuning
    {
        // --- Stamina -------------------------------------------------------
        public const float StaminaRegenPerSecond = 13f;
        public const float GuardStaminaDrainPerSecond = 7f;
        public const float LowStaminaRatio = 0.30f;
        public const float LowStaminaRegenMultiplier = 0.5f;
        public const float ExhaustedDamageMultiplier = 0.55f;
        public const float ExhaustedTimingMultiplier = 1.6f;

        // Throwing punches back to back costs progressively more gas.
        public const float ComboResetTime = 1.2f;
        public const float ComboStaminaScaling = 0.25f;
        public const int ComboMaxStacks = 4;

        // --- Guard ---------------------------------------------------------
        public const float BlockDamageMultiplier = 0.22f;
        /// Body shots slip past a high guard far more easily than head shots.
        public const float BodyGuardLeakMultiplier = 2.1f;
        public const float GuardGaugeMax = 100f;
        public const float GuardGaugeDamageMultiplier = 1.4f;
        public const float GuardGaugeRegenPerSecond = 14f;
        public const float GuardBreakStunTime = 1.5f;

        // --- Counters ------------------------------------------------------
        /// Blocking within this many seconds of raising the guard is a "perfect" block.
        public const float PerfectBlockWindow = 0.18f;
        public const float CounterWindow = 0.7f;
        public const float CounterBonusMultiplier = 1.6f;
        /// Landing on someone still winding up their own punch.
        public const float PunishCounterMultiplier = 1.45f;

        // --- Dodge ---------------------------------------------------------
        public const float DodgeDuration = 0.28f;
        public const float DodgeCooldown = 0.75f;
        public const float DodgeSpeedMultiplier = 2.7f;
        public const float DodgeDamageReduction = 0.75f;
        public const float DodgeStaminaCost = 12f;

        // --- Hit reactions -------------------------------------------------
        public const float HeadStaggerChance = 0.4f;
        public const float StaggerTime = 0.35f;
        /// A body shot drains gas instead of reliably opening a stagger.
        public const float BodyStaminaDamageMultiplier = 0.9f;
        /// Metres per second of shove added per point of damage that gets through.
        public const float KnockbackPerDamage = 0.045f;

        // --- Knockdowns ----------------------------------------------------
        public const float KnockdownRecoveryTime = 3.0f;
        public const float GetUpHealthRatio = 0.35f;
        public const int MaxKnockdowns = 3;
        /// Seconds shaved off the count per punch-button mash.
        public const float MashRecoveryPerPress = 0.25f;

        // --- Impact feel ---------------------------------------------------
        public const float HitStopClean = 0.065f;
        public const float HitStopBlocked = 0.028f;
        public const float HitStopKnockdown = 0.18f;
        public const float HitStopTimeScale = 0.06f;

        public const float CameraShakeClean = 0.22f;
        public const float CameraShakeBlocked = 0.07f;
        public const float CameraShakeTaken = 0.38f;

        // --- Punch trajectory ----------------------------------------------
        /// How far "behind" the rest pose the glove loads during the windup, on the same
        /// normalised track the strike then runs 0 -> 1 along.
        public const float PunchLoadTrack = 0.18f;
        /// Hit tests only start once the glove is most of the way out, which keeps the first-person
        /// viewmodel pose from changing where a punch actually lands.
        public const float PunchHitTrackThreshold = 0.55f;

        // --- Ring ----------------------------------------------------------
        public const float RingHalfExtent = 3.1f;
        public const float MinFighterSeparation = 0.62f;
    }
}

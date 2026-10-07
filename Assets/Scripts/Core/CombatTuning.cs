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

        // --- Combos --------------------------------------------------------
        /// How far into a punch's recovery the next one may start. Only the other hand may cancel,
        /// so a one-two flows while a single glove cannot machine-gun - the whole difference
        /// between throwing punches and boxing.
        public const float ComboCancelFraction = 0.55f;
        /// Cutting your own recovery short costs extra gas.
        public const float ComboCancelStaminaMultiplier = 1.35f;

        // --- Guard ---------------------------------------------------------
        public const float BlockDamageMultiplier = 0.22f;
        /// Body shots slip past a high guard far more easily than head shots.
        public const float BodyGuardLeakMultiplier = 2.1f;
        public const float GuardGaugeMax = 100f;
        public const float GuardGaugeDamageMultiplier = 1.4f;
        public const float GuardGaugeRegenPerSecond = 14f;
        public const float GuardBreakStunTime = 1.5f;

        // Holding a guard that has nothing left in it.
        //
        // The gauge alone made the guard free until the moment it snapped: hold it all round and
        // the only cost was gas, which regenerates. A real fighter holding his hands up under fire
        // is taking the punishment through his arms and emptying his tank doing it - so once the
        // gauge is spent, insisting on the guard costs health directly and burns gas far faster.
        // The hands also do not come up, which is the other half of the answer: the gauge is a
        // resource, not a suggestion.
        public const float GuardSpentHealthPerSecond = 5f;
        public const float GuardSpentStaminaMultiplier = 2.6f;
        /// Below this much gauge the drain already starts climbing, so running it to zero is a
        /// decision you can feel coming rather than a cliff you fall off.
        public const float GuardStrainRatio = 0.3f;
        public const float GuardStrainMultiplier = 1.8f;

        // --- Weight ---------------------------------------------------------
        // Where the weight is, between the back foot and the front. Boxing's power comes from
        // moving it, not from the arm: a right hand thrown with the weight already forward has
        // nothing left to transfer, and a punch thrown while retreating has nothing behind it at
        // all. Nothing in the fight modelled this, which is why every punch hit the same whatever
        // the feet were doing.
        //
        // -1 is fully on the back foot, +1 fully on the front.
        /// An orthodox stance rests a little on the back foot, ready to drive off it.
        public const float WeightNeutral = -0.12f;
        /// How fast it settles back to neutral when nothing is asking it to move.
        public const float WeightSettlePerSecond = 1.8f;
        /// How far footwork carries it. Stepping in puts you on the front foot.
        public const float WeightFromMove = 0.55f;
        /// How hard a punch drives it forward. This is the punch.
        public const float WeightDrivePerSecond = 4f;

        /// How much of a punch's power is the weight transfer rather than the arm. The rear hand
        /// is almost all transfer; the jab is a range-finder and barely cares.
        public const float RearTransferShare = 0.6f;
        public const float LeadTransferShare = 0.18f;

        // --- Range quality ---------------------------------------------------
        // Landing is not the same as landing well. A straight smothered at chest range has no
        // room to extend, and one thrown at the very limit arrives with the arm out and the body
        // left behind. Each punch's own reach is already in EffectiveRange, so one fraction covers
        // every punch: at point blank a jab is smothered and an uppercut is exactly home, which
        // falls out of the uppercut's reach being shorter.
        /// Fraction of the punch's effective range where it lands hardest.
        public const float IdealRangeFraction = 0.82f;
        /// And below which it has no room to extend at all.
        ///
        /// Needed because fighters cannot stand closer than MinFighterSeparation, so measuring
        /// the smothered end from zero distance meant it was never reached - every punch kept
        /// 86% of its power at point blank and the mechanic did nothing. Measured as a fraction
        /// of each punch's own reach instead, which is what makes a short punch win on the inside
        /// and a long one win at range, with no special case per punch.
        public const float SmotherRangeFraction = 0.45f;
        /// What is left of a punch jammed up at the chest.
        public const float SmotheredPower = 0.45f;
        /// And of one thrown at the very edge of reach.
        public const float ReachingPower = 0.72f;

        // --- Ducking as a technique ----------------------------------------
        // Slip the jab, drop, and go to the body. That sequence is the first real *technique* in
        // the game rather than a button, so the numbers have to make it worth doing: the duck
        // tucks the head behind the gloves, and a short punch thrown from a low base is faster and
        // carries the legs behind it.
        /// How much of the head's guard leak a full duck closes - the chin is behind the gloves.
        public const float CrouchHeadCover = 0.45f;
        /// A body punch from a low base is a short punch. Below 1 is faster.
        public const float CrouchBodyPunchSpeed = 0.78f;
        /// And it has the legs under it.
        public const float CrouchBodyDamage = 1.2f;

        // --- Feints --------------------------------------------------------
        /// A feint is a windup with nothing behind it: it buys a reaction. Cheap, but not free,
        /// or it would simply be the best thing to do at all times.
        public const float FeintStaminaRatio = 0.35f;
        /// A feint's hand comes back faster than a real punch's, which is what lets it set one up.
        public const float FeintRecoveryRatio = 0.55f;

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

        // --- Where damage goes ---------------------------------------------
        // Boxing does not work like a health bar. Blood decides nothing; a few clean shots to the
        // chin decide everything, and a body attack buys you a man who cannot breathe in the
        // later rounds. So a punch does three separate things depending on where it lands, and
        // only one of them is the bar.
        //
        // Multiplying head damage into health instead was the obvious version and it does not
        // work: at 2.1x a clean straight takes a quarter of the pool, so every fight ends inside
        // the first round and the cards never matter.
        public const float HeadHealthMultiplier = 1.25f;
        public const float BodyHealthMultiplier = 0.8f;

        // --- Head trauma ---------------------------------------------------
        // The concussive load that actually ends fights, and the reason to put punches together.
        // It decays, so shots have to arrive in a burst: five clean straights inside a few
        // seconds puts a fresh fighter down, while the same five spread across a round only
        // grinds the bar. Landing them is the skill the whole game is about.
        public const float HeadTraumaPerDamage = 0.0135f;
        public const float HeadTraumaDecayPerSecond = 0.12f;
        /// A hurt fighter's chin goes first. This is what makes a man in trouble finishable.
        public const float HeadTraumaHurtMultiplier = 1.8f;
        /// You get up with your bell still ringing, so the second knockdown comes easier.
        public const float HeadTraumaAfterKnockdown = 0.35f;

        // --- Being hurt ----------------------------------------------------
        // Below half the bar a fighter comes apart in the ways a tired fighter really does: the
        // hands come down, the feet slow, the shots arrive late. None of it is a damage modifier -
        // it is the same guard and footwork numbers, worse.
        public const float HurtThresholdRatio = 0.5f;
        /// Where the fade is complete. Not zero, since zero is a knockdown anyway.
        public const float HurtFloorRatio = 0.08f;
        /// The hands stop coming all the way up, so more gets through the guard.
        public const float HurtGuardLeakMultiplier = 2.2f;
        public const float HurtGuardRegenMultiplier = 0.45f;
        public const float HurtMoveMultiplier = 0.72f;
        /// Windup, strike and recovery all stretch - the punches are late, not weaker.
        public const float HurtTimingMultiplier = 1.35f;

        // --- Hit descriptor ------------------------------------------------
        // What gameplay tells the animation system about a punch that landed, so it can pick a
        // reaction that matches instead of playing the one clip it has.
        /// How far off the centre line a contact has to be to count as a side shot rather than a
        /// straight one. A fraction of the forward distance, so it scales with how square the
        /// fighters are to each other.
        public const float SideHitRatio = 0.6f;
        /// Damage above this reads as a heavy shot. Near a clean straight from a mid-career
        /// fighter, so most jabs are light and most power punches are not.
        public const float HeavyHitDamage = 14f;

        // --- Hit reactions -------------------------------------------------
        public const float HeadStaggerChance = 0.4f;
        public const float StaggerTime = 0.35f;
        /// A body shot buys the gas tank rather than the bar. Raised when damage was split by
        /// zone: the body attack has to be worth throwing, and this is what pays for it.
        public const float BodyStaminaDamageMultiplier = 1.5f;
        /// Metres per second of shove added per point of damage that gets through.
        public const float KnockbackPerDamage = 0.085f;

        // --- Knockdowns ----------------------------------------------------
        public const float KnockdownRecoveryTime = 3.0f;
        public const float GetUpHealthRatio = 0.35f;
        /// Knockdowns in a single round that end it as a TKO. This is the three-knockdown rule as
        /// it really works - the count resets each round, so across a long fight knockdowns cost
        /// you the scorecard rather than the fight.
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
        /// Hit tests only start once the glove is on its way out, which keeps the first-person
        /// viewmodel pose from changing where a punch actually lands. Low enough that the swept
        /// test gets most of the glove's path: at close range the glove reaches the head early and
        /// finishes past it.
        public const float PunchHitTrackThreshold = 0.40f;

        // --- Hit volumes ---------------------------------------------------
        // The hurtboxes are the fighter's silhouette, not a generous bubble around it, and a
        // glove's hit radius is a glove. They used to be roughly twice this, which let a punch
        // land with its surface 9cm clear of the skin - a swing through open air that still made
        // a thud. Range is tuned against these numbers, so EffectiveRange reads them directly
        // rather than carrying its own fudge factor.
        public const float HeadHurtboxRadius = 0.13f;
        public const float HeadHurtboxHeight = 1.56f;
        public const float BodyHurtboxRadius = 0.20f;
        /// Belt to sternum. It used to run from the knees to the chin.
        public const float BodyHurtboxHeight = 0.56f;
        public const float BodyHurtboxCentre = 1.18f;

        // --- Reach ---------------------------------------------------------
        /// How far inside the geometric limit a fighter steps before throwing, so the punch lands
        /// solidly instead of at the very edge of the maths.
        public const float RangeBite = 0.06f;
        /// A punch thrown from beyond contact range carries the fighter in rather than pawing at
        /// the air - real boxers step into their shots, and without it a key press at the wrong
        /// moment just reads as the input being ignored. Only this far, though: a step is a step,
        /// not a lunge across the ring.
        public const float StepInReach = 0.45f;
        public const float StepInMax = 0.30f;

        // --- Head movement -------------------------------------------------
        // Leaning and ducking move the head hurtbox itself, so slipping a punch is a real miss
        // rather than a damage modifier. It costs a trickle of gas and some footwork.
        public const float LeanHeadOffset = 0.19f;
        public const float CrouchHeadOffset = 0.30f;
        public const float HeadMoveSpeed = 7f;
        /// How fast a stance may *change* while a punch is in the air. Low, because a punch
        /// commits your weight - but never zero, and the stance you already had is never dropped.
        /// Zeroing it during a punch is what made the fighter stand up to throw and sit back down.
        public const float HeadMoveDuringPunch = 0.35f;
        /// Ducking is aiming. At a full crouch the punch goes to the body, which is what boxing
        /// does and what the player means by bending at the waist - no separate aim input needed.
        public const float CrouchAimDrop = 1f;
        public const float HeadMoveStaminaDrainPerSecond = 3.5f;
        public const float CrouchMoveMultiplier = 0.55f;
        public const float LeanMoveMultiplier = 0.82f;

        // --- HUD -----------------------------------------------------------
        /// Health bars are drawn against this rather than normalised to each fighter, so an
        /// endurance tank visibly carries a longer bar than a quick one. Raise it as the career
        /// pushes stats higher.
        public const float HealthBarReference = 260f;
        /// Even the frailest fighter keeps a readable bar.
        public const float HealthBarMinFraction = 0.34f;

        // --- Ring ----------------------------------------------------------
        public const float RingHalfExtent = 3.1f;
        public const float MinFighterSeparation = 0.62f;
    }
}

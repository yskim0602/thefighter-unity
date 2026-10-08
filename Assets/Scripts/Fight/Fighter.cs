using UnityEngine;

namespace TheFighter
{
    /// Everything a boxer is made of: vitals, the punch state machine, guard, dodge, counters and
    /// the three-knockdown rule. It never decides *what* to do - a brain hands it a FighterIntent -
    /// so the player and the AI run through exactly the same code.
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(FighterMotor))]
    public class Fighter : MonoBehaviour
    {
        [Header("Identity")]
        public string FighterName = "FIGHTER";
        public Stance CurrentStance = Stance.Orthodox;
        public BoxingStyle Style = BoxingStyle.BoxerPuncher;
        public FighterStats Stats = new FighterStats();

        [Header("Pace")]
        /// 1 is the designed pace. Higher is slower and more deliberate - one dial for "the whole
        /// fight is too fast", applied to punch timings and footwork together so they stay in
        /// proportion. Live-tunable in play mode.
        [Range(0.5f, 2.5f)] public float Tempo = 1f;

        [Header("Rig")]
        public Transform EyeAnchor;
        /// Leaning and ducking move this, which is what makes head movement a real defence.
        public Transform HeadHurtbox;
        public FighterRig Rig;

        /// Installed by FighterAnimation when there is one: does a clip exist for this punch on
        /// this side? Left over right, as a bool, because the clip set knows nothing about stance.
        ///
        /// Gameplay asking animation a question is deliberate and narrow. A punch thrown with a
        /// hand that has no clip does not degrade gracefully - the other arm swings while the
        /// throwing arm hangs there - so for a punch that can legitimately come off either hand,
        /// which clips exist is part of what the fighter can do.
        public System.Func<PunchKind, bool, bool> HasPunchClip;
        public Renderer[] BodyRenderers;

        [Header("Match")]
        public Fighter Opponent;
        public bool FightActive;

        public event System.Action<HitEvent> Landed;
        public event System.Action<Fighter, PunchDefinition> Whiffed;
        /// Fires the moment a punch starts. Volume is a judging criterion, so the scorecard needs
        /// to count what was thrown, not only what landed.
        public event System.Action<Fighter, PunchDefinition> Threw;
        public event System.Action<Fighter> Downed;
        public event System.Action<Fighter> KnockedOut;

        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public float Stamina { get; private set; }
        public float MaxStamina { get; private set; }
        public float GuardGauge { get; private set; }
        /// Knockdowns in the current round - three of them is a TKO, as the three-knockdown rule
        /// actually works. Across the whole fight they only cost you points.
        public int Knockdowns { get; private set; }
        public int TotalKnockdowns { get; private set; }

        public ActionState State { get; private set; }
        public bool IsGuarding { get; private set; }
        public bool GuardBroken { get; private set; }
        public bool IsDodging { get; private set; }
        public float AimHeight { get; private set; }
        /// -1 leaning left, +1 right.
        public float LeanAmount { get; private set; }
        /// 0 standing, 1 fully ducked.
        public float CrouchAmount { get; private set; }

        public PunchDefinition ActivePunch { get; private set; }
        /// The windup is real; the punch is not. Nothing will be thrown, and nothing will land.
        public bool IsFeinting { get; private set; }
        public HandRole ActiveHand { get; private set; }
        /// -PunchLoadTrack while loading, 0 -> 1 as the glove extends. FighterRig turns this into a pose.
        public float PunchTrack { get; private set; }

        /// Monotonic 0 -> 1 across the whole punch: windup, strike, recovery. An animation clip
        /// needs this; PunchTrack is a pose curve and runs backwards on the way home.
        public float PunchProgress
        {
            get
            {
                if (ActivePunch == null)
                {
                    return 0f;
                }

                float p = _phaseDuration > 0f ? Mathf.Clamp01(_phaseTimer / _phaseDuration) : 1f;
                switch (State)
                {
                    case ActionState.Windup: return p * 0.30f;
                    case ActionState.Strike: return 0.30f + p * 0.30f;
                    case ActionState.Recovery: return 0.60f + p * 0.40f;
                    default: return 0f;
                }
            }
        }

        StyleProfile _profile;
        BoxingStyle _baseStyle = BoxingStyle.BoxerPuncher;
        IFighterBrain _brain;
        FighterMotor _motor;

        readonly Collider[] _overlap = new Collider[16];
        Vector3 _gloveTrail;
        bool _gloveTrailValid;

        float _phaseTimer;
        float _phaseDuration;
        bool _punchLanded;
        HandRole _lastHand = HandRole.Rear;
        float _recoveryFrom = 1f;
        float _guardHeldTime;
        float _guardBreakTimer;
        float _counterWindowTimer;
        float _dodgeTimer;
        float _dodgeCooldownTimer;
        float _staggerTimer;
        float _downTimer;
        float _comboTimer;
        int _comboStacks;
        Vector3 _headHurtboxBase;
        bool _headHurtboxCaptured;

        void Awake()
        {
            _profile = BoxingStyles.Profile(Style);
            AimHeight = 1f;
        }

        public FighterMotor Motor
        {
            get
            {
                if (_motor == null)
                {
                    _motor = GetComponent<FighterMotor>();
                }
                return _motor;
            }
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        public void Configure(string displayName, FighterStats stats, BoxingStyle style, Stance stance)
        {
            FighterName = displayName;
            Stats = stats;
            CurrentStance = stance;
            SetBaseStyle(style);
            ResetForFight();
        }

        /// The style a fighter came into the ring with. Only this one moves the health and gas pools.
        public void SetBaseStyle(BoxingStyle style)
        {
            _baseStyle = style;
            Style = style;
            _profile = BoxingStyles.Profile(style);
            MaxHealth = Stats.MaxHealth * _profile.Health;
            MaxStamina = Stats.MaxStamina * _profile.Stamina;
        }

        /// A tactical switch mid-fight. Changes how they fight, never how much they can take -
        /// otherwise the AI dropping into in-fighter mode would silently refill its own bar.
        public void SetActiveStyle(BoxingStyle style)
        {
            Style = style;
            _profile = BoxingStyles.Profile(style);
        }

        public BoxingStyle BaseStyle { get { return _baseStyle; } }
        public StyleProfile Profile { get { return _profile; } }

        public void SetBrain(IFighterBrain brain)
        {
            _brain = brain;
            if (_brain != null)
            {
                _brain.Attach(this, Opponent);
            }
        }

        public void ResetForFight()
        {
            Health = MaxHealth;
            Stamina = MaxStamina;
            GuardGauge = CombatTuning.GuardGaugeMax;
            HeadTrauma = 0f;
            HeadDamageTaken = 0f;
            BodyDamageTaken = 0f;
            HeadDamageBlocked = 0f;
            BodyDamageBlocked = 0f;
            LastHitDirection = HitDirection.Any;
            LastHitSeverity = HitSeverity.Any;
            Weight = CombatTuning.WeightNeutral;
            PunchTransfer = 1f;
            Knockdowns = 0;
            TotalKnockdowns = 0;
            State = ActionState.Free;
            ActivePunch = null;
            PunchTrack = 0f;
            AimHeight = 1f;
            IsGuarding = false;
            IsDodging = false;
            IsFeinting = false;
            _lastHand = HandRole.Rear;
            GuardBroken = false;
            _phaseTimer = 0f;
            _phaseDuration = 0f;
            _punchLanded = false;
            _guardHeldTime = 0f;
            _guardBreakTimer = 0f;
            _counterWindowTimer = 0f;
            _dodgeTimer = 0f;
            _dodgeCooldownTimer = 0f;
            _staggerTimer = 0f;
            _downTimer = 0f;
            _comboTimer = 0f;
            _comboStacks = 0;
            LeanAmount = 0f;
            CrouchAmount = 0f;
            Motor.Stop();
        }

        /// Answering the bell: the three-knockdown count starts fresh and nothing carries over
        /// from the last round except how hurt and how tired you are.
        public void BeginRound()
        {
            Knockdowns = 0;
            CancelPunch();
            State = ActionState.Free;
            IsGuarding = false;
            IsDodging = false;
            GuardBroken = false;
            _guardHeldTime = 0f;
            _guardBreakTimer = 0f;
            _counterWindowTimer = 0f;
            _dodgeTimer = 0f;
            _dodgeCooldownTimer = 0f;
            _staggerTimer = 0f;
            _downTimer = 0f;
            _comboTimer = 0f;
            _comboStacks = 0;
            LeanAmount = 0f;
            CrouchAmount = 0f;
            Motor.Stop();
        }

        /// A minute on the stool, compressed. Gas comes back most, damage barely - and the corner
        /// gets less out of you the deeper the fight goes, so the late rounds are where fights are
        /// actually lost. Endurance is what you are buying when you train it.
        public void RecoverInCorner(int roundsCompleted, int totalRounds)
        {
            float fade = totalRounds > 1
                ? 1f - RoundRules.RestHealthFade * ((float)roundsCompleted / totalRounds)
                : 1f;

            float staminaFloor = MaxStamina * Mathf.Clamp01(RoundRules.RestStaminaRatio
                + Stats.Endurance * RoundRules.RestStaminaPerEndurance);
            Stamina = Mathf.Max(Stamina, staminaFloor);

            float heal = MaxHealth * (RoundRules.RestHealthRatio
                + Stats.Endurance * RoundRules.RestHealthPerEndurance) * Mathf.Max(0.15f, fade);

            Health = Mathf.Min(MaxHealth,
                Mathf.Max(Health + heal, MaxHealth * RoundRules.CornerMinHealthRatio));

            GuardGauge = CombatTuning.GuardGaugeMax;

            // Caught by the bell on the canvas: the count stops and the corner gets you up.
            if (State == ActionState.Down)
            {
                State = ActionState.Free;
                _downTimer = 0f;
            }
        }

        public void SetFirstPerson(bool enabled)
        {
            if (Rig != null)
            {
                Rig.SetFirstPerson(enabled);
            }

            if (BodyRenderers != null)
            {
                for (int i = 0; i < BodyRenderers.Length; i++)
                {
                    if (BodyRenderers[i] != null)
                    {
                        BodyRenderers[i].enabled = !enabled;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Queries the brains and HUD read
        // ------------------------------------------------------------------

        public float HealthRatio { get { return MaxHealth > 0f ? Health / MaxHealth : 0f; } }

        /// Concussive load, 0 to 1. Reaching 1 is a knockdown. Decays, so it measures punches
        /// landed *together* rather than punches landed.
        public float HeadTrauma { get; private set; }

        /// Damage taken per region across the whole fight. Health is one pool and tells you
        /// nothing about where it went, but in boxing where it went is the story: a man who has
        /// been worked downstairs all night and a man who has been hit on the chin are in
        /// completely different trouble. Kept for the fight, not the round - that is the point.
        public float HeadDamageTaken { get; private set; }
        public float BodyDamageTaken { get; private set; }
        public float HeadDamageBlocked { get; private set; }
        public float BodyDamageBlocked { get; private set; }

        /// The last punch that landed, described so the animation layer can pick a reaction that
        /// matches it. Worked out from the contact point in this fighter's own frame, not from
        /// which hand threw it - a left hook from the man in front of you lands on your right.
        public HitDirection LastHitDirection { get; private set; }
        public HitSeverity LastHitSeverity { get; private set; }

        /// Where the weight is: -1 on the back foot, +1 on the front. Footwork moves it, a punch
        /// drives it forward, and it settles back to the stance when nothing is asking.
        public float Weight { get; private set; }

        /// How much transfer the punch in the air had available when it started. 1 means the
        /// weight was all the way back with everywhere to go; 0 means it was already forward and
        /// the punch is arm only. Read by the damage, and by PostureProbe.
        public float PunchTransfer { get; private set; }

        /// 0 while there is still something left, 1 when a fighter is out on his feet. Scales the
        /// guard, the footwork and the timing - every one of them for the worse.
        public float HurtFactor
        {
            get
            {
                return Mathf.Clamp01(Mathf.InverseLerp(CombatTuning.HurtThresholdRatio,
                    CombatTuning.HurtFloorRatio, HealthRatio));
            }
        }
        public float StaminaRatio { get { return MaxStamina > 0f ? Stamina / MaxStamina : 0f; } }
        public float GuardRatio { get { return GuardGauge / CombatTuning.GuardGaugeMax; } }
        public bool IsExhausted { get { return Stamina <= 1f; } }
        public bool IsFinished { get { return State == ActionState.KnockedOut; } }
        public bool CounterWindowOpen { get { return _counterWindowTimer > 0f; } }
        public bool CanAct { get { return State == ActionState.Free; } }
        /// No guard, no slip: the windup is the hole a punish counter goes through.
        public bool IsVulnerable { get { return State == ActionState.Windup; } }
        public int ComboStacks { get { return _comboStacks; } }

        public float DistanceTo(Fighter other)
        {
            if (other == null)
            {
                return float.MaxValue;
            }
            Vector3 a = transform.position;
            Vector3 b = other.transform.position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public float PunchReach(PunchDefinition punch)
        {
            return Stats.Reach * punch.RangeMultiplier * _profile.Range;
        }

        /// Centre-to-centre distance at which this punch still connects: how far the glove
        /// travels, plus the two radii that have to touch. Then a little closer still
        /// (CombatTuning.RangeBite), so the punch lands solidly rather than at the edge of the
        /// maths. Reads the hurtbox constants directly - a range that disagrees with the hit
        /// volumes is how you get fighters pawing at air, or standing inside each other.
        public float EffectiveRange(PunchDefinition punch)
        {
            return PunchReach(punch) + punch.HitRadius + CombatTuning.HeadHurtboxRadius
                - CombatTuning.RangeBite;
        }

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;

            // Decays whatever else is happening, including between rounds: the corner is where a
            // fighter clears his head.
            HeadTrauma = Mathf.Max(0f, HeadTrauma - CombatTuning.HeadTraumaDecayPerSecond * dt);

            if (!FightActive)
            {
                RefreshRig(dt);
                return;
            }

            FighterIntent intent = _brain != null ? _brain.Think(dt) : FighterIntent.Neutral();

            AdvanceTimers(dt);

            if (State == ActionState.KnockedOut)
            {
                RefreshRig(dt);
                return;
            }

            if (State == ActionState.Down)
            {
                if (intent.MashGetUp)
                {
                    _downTimer -= CombatTuning.MashRecoveryPerPress;
                }
                if (_downTimer <= 0f)
                {
                    GetUp();
                }
                RefreshRig(dt);
                return;
            }

            AimHeight = Mathf.Clamp01(intent.AimHeight);

            HandleGuard(intent, dt);
            HandleWeight(intent, dt);
            HandleHeadMovement(intent, dt);
            HandleDodge(intent);
            HandleMovement(intent, dt);

            if (Opponent != null)
            {
                Motor.FaceTowards(Opponent.transform.position, dt);
            }

            HandlePunchInput(intent);
            AdvancePunch(dt);
            RegenerateStamina(dt);
        }

        void AdvanceTimers(float dt)
        {
            if (_counterWindowTimer > 0f)
            {
                _counterWindowTimer -= dt;
            }

            if (_dodgeCooldownTimer > 0f)
            {
                _dodgeCooldownTimer -= dt;
            }

            if (IsDodging)
            {
                _dodgeTimer -= dt;
                if (_dodgeTimer <= 0f)
                {
                    IsDodging = false;
                }
            }

            if (GuardBroken)
            {
                _guardBreakTimer -= dt;
                if (_guardBreakTimer <= 0f)
                {
                    GuardBroken = false;
                }
            }

            if (State == ActionState.Staggered)
            {
                _staggerTimer -= dt;
                if (_staggerTimer <= 0f)
                {
                    State = ActionState.Free;
                }
            }

            if (State == ActionState.Down)
            {
                _downTimer -= dt;
            }

            if (_comboStacks > 0)
            {
                _comboTimer -= dt;
                if (_comboTimer <= 0f)
                {
                    _comboStacks = 0;
                }
            }
        }

        void HandleGuard(FighterIntent intent, float dt)
        {
            // Recovery counts. Bringing the hands back from a punch *is* raising the guard, and
            // locking it out until Free meant a straight (0.28s of recovery) could not be followed
            // by a block at all - an incoming jab lands in 0.20s. The counter after your own punch
            // was unblockable by construction, which is what "the guard does not come up" was.
            // Windup still commits you: once the hand goes, it goes.
            bool wants = intent.Guard
                && !GuardBroken
                && (State == ActionState.Free || State == ActionState.Recovery)
                && !IsDodging;

            // Insisting on a guard that has nothing left in it. The hands do not come up - that
            // is what GuardBroken means - but the arms are still out there taking it, and that
            // costs health rather than nothing. Without this the guard was free until it snapped,
            // since gas regenerates and health does not.
            if (intent.Guard && GuardBroken && !IsDodging && State != ActionState.Down
                && State != ActionState.KnockedOut)
            {
                Health = Mathf.Max(0f, Health - CombatTuning.GuardSpentHealthPerSecond * dt);
                Stamina = Mathf.Max(0f, Stamina - CombatTuning.GuardStaminaDrainPerSecond
                    * CombatTuning.GuardSpentStaminaMultiplier * dt);
            }

            if (wants)
            {
                if (!IsGuarding)
                {
                    IsGuarding = true;
                    _guardHeldTime = 0f;
                }
                else
                {
                    _guardHeldTime += dt;
                }

                // The strain climbs as the gauge empties, so running it down is a decision you can
                // feel arriving instead of a cliff edge.
                float strain = GuardRatio < CombatTuning.GuardStrainRatio
                    ? Mathf.Lerp(CombatTuning.GuardStrainMultiplier, 1f,
                        GuardRatio / Mathf.Max(0.0001f, CombatTuning.GuardStrainRatio))
                    : 1f;

                Stamina = Mathf.Max(0f,
                    Stamina - CombatTuning.GuardStaminaDrainPerSecond * strain * dt);
            }
            else
            {
                IsGuarding = false;
                _guardHeldTime = 0f;

                if (!GuardBroken)
                {
                    GuardGauge = Mathf.Min(CombatTuning.GuardGaugeMax,
                        GuardGauge + CombatTuning.GuardGaugeRegenPerSecond * dt
                            * Mathf.Lerp(1f, CombatTuning.HurtGuardRegenMultiplier, HurtFactor));
                }
            }
        }

        /// Slipping and ducking, which move the head hurtbox rather than granting a damage
        /// reduction - a punch aimed where your head was simply misses. Cheap in gas but not free,
        /// and it costs footwork, which is the trade that makes it a decision.
        /// Moves the weight. Three things ask for it and they are allowed to disagree, which is
        /// the point: stepping in while throwing a right hand is how you take the power out of
        /// your own punch.
        void HandleWeight(FighterIntent intent, float dt)
        {
            float target;
            float rate;

            if (ActivePunch != null && State != ActionState.Recovery)
            {
                // Driving off the back foot. This is the punch, not decoration on it.
                target = 1f;
                rate = CombatTuning.WeightDrivePerSecond;
            }
            else if (Motor != null && Mathf.Abs(Motor.LocalMove.y) > 0.05f)
            {
                float lean = Mathf.Clamp(Motor.LocalMove.y / Mathf.Max(0.1f, Stats.MoveSpeed), -1f, 1f);
                target = CombatTuning.WeightNeutral + lean * CombatTuning.WeightFromMove;
                rate = CombatTuning.WeightSettlePerSecond;
            }
            else
            {
                target = CombatTuning.WeightNeutral;
                rate = CombatTuning.WeightSettlePerSecond;
            }

            Weight = Mathf.MoveTowards(Weight, Mathf.Clamp(target, -1f, 1f), rate * dt);
        }

        void HandleHeadMovement(FighterIntent intent, float dt)
        {
            // Posture is a stance, not an action, so it has to survive a punch. This gate used to
            // require Free or Recovery, which meant every punch spent its windup and strike
            // driving the crouch toward zero - 0.20s at 7 per second is the entire crouch, so the
            // fighter stood up to throw and sat back down afterwards. That is the exact thing
            // CLAUDE.md forbids, and it was here rather than in any clip.
            bool floored = State == ActionState.Down || State == ActionState.KnockedOut;
            bool allowed = !floored && State != ActionState.Staggered && !IsDodging;

            float wantedLean = allowed ? Mathf.Clamp(intent.Lean, -1f, 1f) : 0f;
            float wantedCrouch = allowed && intent.Crouch ? 1f : 0f;

            // Holding a stance through a punch costs nothing, since the target is already where
            // you are. Changing one mid-punch is slow, because your weight is committed - so
            // letting go of the key rises out of the crouch rather than snapping upright.
            float rate = CombatTuning.HeadMoveSpeed
                * (ActivePunch != null ? CombatTuning.HeadMoveDuringPunch : 1f);

            LeanAmount = Mathf.MoveTowards(LeanAmount, wantedLean, rate * dt);
            CrouchAmount = Mathf.MoveTowards(CrouchAmount, wantedCrouch, rate * dt);

            // Bent at the waist, the punch goes downstairs. Decided here rather than by the
            // player aiming separately, and before HandlePunchInput runs, so the shot that leaves
            // is already a body shot.
            AimHeight = Mathf.Clamp01(AimHeight - CrouchAmount * CombatTuning.CrouchAimDrop);

            float effort = Mathf.Abs(LeanAmount) + CrouchAmount;
            if (effort > 0.05f)
            {
                Stamina = Mathf.Max(0f,
                    Stamina - CombatTuning.HeadMoveStaminaDrainPerSecond * effort * dt);
            }

            if (HeadHurtbox == null)
            {
                return;
            }

            if (!_headHurtboxCaptured)
            {
                // Lowered once by whatever bend the stance actually has, because the bootstrap
                // places this volume at a straight-legged height and a boxer is never straight-
                // legged. Leaving it up there puts the chin that gets hit a few centimetres above
                // the chin you can see, which is exactly the "punching empty air" the volumes
                // were shrunk to fix. Asked for rather than assumed: a fighter with no model has
                // no knees to bend, so there is nothing to subtract.
                CrouchPose stance = GetComponent<CrouchPose>();
                float bend = stance != null ? stance.RestDrop : 0f;
                _headHurtboxBase = HeadHurtbox.localPosition + new Vector3(0f, -bend, 0f);
                _headHurtboxCaptured = true;
            }

            HeadHurtbox.localPosition = _headHurtboxBase + new Vector3(
                LeanAmount * CombatTuning.LeanHeadOffset,
                -CrouchAmount * CombatTuning.CrouchHeadOffset,
                0f);
        }

        void HandleDodge(FighterIntent intent)
        {
            if (!intent.Dodge || IsDodging)
            {
                return;
            }
            if (State != ActionState.Free || _dodgeCooldownTimer > 0f)
            {
                return;
            }
            if (Stamina < CombatTuning.DodgeStaminaCost)
            {
                return;
            }

            IsDodging = true;
            IsGuarding = false;
            _dodgeTimer = CombatTuning.DodgeDuration;
            _dodgeCooldownTimer = CombatTuning.DodgeCooldown;
            Stamina = Mathf.Max(0f, Stamina - CombatTuning.DodgeStaminaCost);
        }

        void HandleMovement(FighterIntent intent, float dt)
        {
            Vector2 move = Vector2.ClampMagnitude(intent.Move, 1f);
            float speed = Stats.MoveSpeed * _profile.Footwork / Mathf.Max(0.1f, Tempo)
                * Mathf.Lerp(1f, CombatTuning.HurtMoveMultiplier, HurtFactor);

            if (CrouchAmount > 0.01f)
            {
                speed *= Mathf.Lerp(1f, CombatTuning.CrouchMoveMultiplier, CrouchAmount);
            }
            if (Mathf.Abs(LeanAmount) > 0.01f)
            {
                speed *= Mathf.Lerp(1f, CombatTuning.LeanMoveMultiplier, Mathf.Abs(LeanAmount));
            }

            if (IsDodging)
            {
                if (move.sqrMagnitude < 0.01f)
                {
                    move = new Vector2(0f, -1f);
                }
                speed *= CombatTuning.DodgeSpeedMultiplier;
            }
            else
            {
                if (IsGuarding)
                {
                    speed *= 0.55f;
                }
                if (ActivePunch != null)
                {
                    speed *= 0.35f;
                }
                if (State == ActionState.Staggered)
                {
                    speed *= 0.30f;
                }
                if (IsExhausted)
                {
                    speed *= 0.80f;
                }
            }

            Motor.Tick(move, speed, dt);
        }

        void HandlePunchInput(FighterIntent intent)
        {
            if (!intent.ThrowPunch || IsDodging)
            {
                return;
            }

            PunchDefinition punch = PunchLibrary.Get(intent.Punch);

            // Combos have to flow. The second punch of a one-two starts before the first hand is
            // all the way back - but only with the *other* hand, so one glove cannot machine-gun,
            // and it costs extra gas. That one rule is what turns single punches into boxing.
            HandRole hand = ResolveHand(punch, intent.Hand);

            // Compared against the hand actually in the air rather than the punch kind's default,
            // which now that a hook can come off either hand are not the same thing.
            bool cancelling = State == ActionState.Recovery
                && ActivePunch != null
                && hand != ActiveHand
                && _phaseTimer / _phaseDuration >= CombatTuning.ComboCancelFraction;

            if (State != ActionState.Free && !cancelling)
            {
                return;
            }

            float cost = punch.StaminaCost * (1f + _comboStacks * CombatTuning.ComboStaminaScaling);
            if (cancelling)
            {
                cost *= CombatTuning.ComboCancelStaminaMultiplier;
            }
            if (intent.Feint)
            {
                cost *= CombatTuning.FeintStaminaRatio;
            }

            Stamina = Mathf.Max(0f, Stamina - cost);
            _comboStacks = Mathf.Min(CombatTuning.ComboMaxStacks, _comboStacks + 1);
            _comboTimer = CombatTuning.ComboResetTime;

            IsGuarding = false;
            _guardHeldTime = 0f;
            ActivePunch = punch;
            ActiveHand = hand;
            _lastHand = hand;
            IsFeinting = intent.Feint;

            // How much weight there was left to move. Decided now, not when the punch lands -
            // a fighter who has already lunged in cannot get it back by the time the fist
            // arrives, and that is exactly the mistake the system exists to punish.
            PunchTransfer = Mathf.Clamp01((1f - Weight) * 0.5f);
            _punchLanded = false;
            EnterPhase(ActionState.Windup, punch.WindupTime * PunchSpeedScale());
            TrackGlove();
            StepIntoRange(punch);

            if (Threw != null)
            {
                Threw(this, punch);
            }
        }

        /// Which hand throws this one.
        ///
        /// Fixed for a jab or a straight. For a hook or an uppercut the input decides, because
        /// the player has a key per arm - and an explicit choice is never second-guessed, since
        /// stopping the hand from alternating under their fingers is the only reason two keys
        /// exist. Only when nobody asked (the AI, the touch pad) does it alternate on its own.
        HandRole ResolveHand(PunchDefinition punch, ClipSide chosen)
        {
            if (!punch.EitherHand)
            {
                return punch.Hand;
            }

            if (chosen != ClipSide.Auto && Rig != null)
            {
                bool wantLeft = chosen == ClipSide.Left;
                return Rig.IsLeftHand(HandRole.Lead) == wantLeft ? HandRole.Lead : HandRole.Rear;
            }

            bool chaining = ActivePunch != null || _comboTimer > 0f;
            HandRole wanted = chaining
                ? (_lastHand == HandRole.Lead ? HandRole.Rear : HandRole.Lead)
                : punch.Hand;

            // Only the clip set can veto the *inferred* hand. With both sides animated this never
            // fires; with one side animated - a single left uppercut, say - the alternation
            // settles on the left one instead of half of them swinging a still arm.
            if (ClipExistsFor(punch.Kind, wanted))
            {
                return wanted;
            }

            HandRole other = wanted == HandRole.Lead ? HandRole.Rear : HandRole.Lead;
            return ClipExistsFor(punch.Kind, other) ? other : wanted;
        }

        bool ClipExistsFor(PunchKind kind, HandRole hand)
        {
            if (HasPunchClip == null || Rig == null)
            {
                return true;
            }

            return HasPunchClip(kind, Rig.IsLeftHand(hand));
        }

        /// Speed owns the outgoing half of a punch.
        float PunchSpeedScale()
        {
            float scale = Stats.PunchSpeedScale * Mathf.Max(0.1f, Tempo)
                * Mathf.Lerp(1f, CombatTuning.HurtTimingMultiplier, HurtFactor);

            // Crouched and throwing downstairs: a short punch from a low base. Both conditions,
            // so ducking does not quietly speed up head shots too.
            float low = CrouchAmount * (1f - AimHeight);
            scale *= Mathf.Lerp(1f, CombatTuning.CrouchBodyPunchSpeed, Mathf.Clamp01(low));
            if (IsExhausted)
            {
                scale *= CombatTuning.ExhaustedTimingMultiplier;
            }
            return scale;
        }

        /// Skill owns getting the hand back, and the style's conditioning tightens it further.
        float RecoveryScale()
        {
            float scale = Stats.RecoveryScale * Mathf.Max(0.1f, Tempo)
                / Mathf.Max(0.5f, _profile.Stamina)
                * Mathf.Lerp(1f, CombatTuning.HurtTimingMultiplier, HurtFactor);
            if (IsExhausted)
            {
                scale *= CombatTuning.ExhaustedTimingMultiplier;
            }
            return scale;
        }

        void EnterPhase(ActionState state, float duration)
        {
            State = state;
            _phaseTimer = 0f;
            _phaseDuration = Mathf.Max(0.02f, duration);
        }

        void AdvancePunch(float dt)
        {
            if (ActivePunch == null)
            {
                PunchTrack = 0f;
                RefreshRig(dt);
                _gloveTrailValid = false;
                return;
            }

            _phaseTimer += dt;

            while (ActivePunch != null && _phaseTimer >= _phaseDuration)
            {
                float carry = _phaseTimer - _phaseDuration;

                if (State == ActionState.Windup)
                {
                    if (IsFeinting)
                    {
                        // No strike phase at all, so there is no hit test and no whiff - a feint
                        // cannot land and cannot miss. The hand comes back quickly, which is what
                        // lets it set up the punch that follows.
                        _recoveryFrom = PunchTrack;
                        EnterPhase(ActionState.Recovery, ActivePunch.RecoveryTime
                            * CombatTuning.FeintRecoveryRatio * RecoveryScale());
                        _phaseTimer = carry;
                        continue;
                    }

                    EnterPhase(ActionState.Strike, ActivePunch.StrikeTime * PunchSpeedScale());
                    _phaseTimer = carry;
                }
                else if (State == ActionState.Strike)
                {
                    // Never let a short punch skip past full extension without a hit test.
                    PunchTrack = 1f;
                    RefreshRig(0f);
                    if (!_punchLanded)
                    {
                        TryLand();
                    }
                    if (!_punchLanded && Whiffed != null)
                    {
                        Whiffed(this, ActivePunch);
                    }

                    _recoveryFrom = 1f;
                    EnterPhase(ActionState.Recovery, ActivePunch.RecoveryTime * RecoveryScale());
                    _phaseTimer = carry;
                }
                else
                {
                    EndPunch();
                }
            }

            if (ActivePunch == null)
            {
                PunchTrack = 0f;
                RefreshRig(dt);
                _gloveTrailValid = false;
                return;
            }

            float p = Mathf.Clamp01(_phaseTimer / _phaseDuration);
            PunchTrack = TrackForPhase(p);
            RefreshRig(dt);

            if (State == ActionState.Strike && !_punchLanded && PunchTrack >= CombatTuning.PunchHitTrackThreshold)
            {
                TryLand();

                // A fist stops when it hits something. Cutting the strike short on contact is what
                // makes the hit read as impact rather than the glove sailing through, and it hands
                // the puncher their guard back sooner - a small reward for landing.
                if (_punchLanded)
                {
                    _recoveryFrom = PunchTrack;
                    EnterPhase(ActionState.Recovery, ActivePunch.RecoveryTime * RecoveryScale());
                }
            }

            TrackGlove();
        }

        float TrackForPhase(float p)
        {
            switch (State)
            {
                case ActionState.Windup:
                    return -CombatTuning.PunchLoadTrack * (1f - (1f - p) * (1f - p));
                case ActionState.Strike:
                    // Snappy out: most of the travel happens in the first third of the window.
                    return Mathf.Lerp(-CombatTuning.PunchLoadTrack, 1f, Mathf.Pow(p, 0.55f));
                case ActionState.Recovery:
                    // From wherever the glove actually stopped, not always full extension.
                    return Mathf.Lerp(_recoveryFrom, 0f, p * p);
                default:
                    return 0f;
            }
        }

        void EndPunch()
        {
            ActivePunch = null;
            IsFeinting = false;
            PunchTrack = 0f;
            _phaseTimer = 0f;
            _phaseDuration = 0f;
            if (State == ActionState.Windup || State == ActionState.Strike || State == ActionState.Recovery)
            {
                State = ActionState.Free;
            }
        }

        void CancelPunch()
        {
            ActivePunch = null;
            PunchTrack = 0f;
            _phaseTimer = 0f;
            _phaseDuration = 0f;
        }

        void RefreshRig(float dt)
        {
            if (Rig != null)
            {
                Rig.Refresh(dt);
            }
        }

        // ------------------------------------------------------------------
        // Hitting and being hit
        // ------------------------------------------------------------------

        /// Remembers where the glove was, so the next frame's hit test can sweep from here.
        void TrackGlove()
        {
            if (Rig == null || ActivePunch == null)
            {
                _gloveTrailValid = false;
                return;
            }

            _gloveTrail = Rig.GetGloveWorldPosition(ActiveHand);
            _gloveTrailValid = true;
        }

        /// A punch thrown from a step too far out carries the fighter in instead of pawing at the
        /// air. Boxers step into their shots, and without this a press at the wrong moment reads
        /// as the input having been ignored - the punch plays, and nothing is there.
        ///
        /// Only closes a real shortfall, and never more than StepInMax: this is a step, not a
        /// lunge across the ring, and it must not become a way to cover ground for free.
        void StepIntoRange(PunchDefinition punch)
        {
            if (Opponent == null || Motor == null
                || Opponent.State == ActionState.Down || Opponent.State == ActionState.KnockedOut)
            {
                return;
            }

            Vector3 flat = Opponent.transform.position - transform.position;
            flat.y = 0f;
            float gap = flat.magnitude;
            if (gap < 0.0001f)
            {
                return;
            }

            float shortfall = gap - EffectiveRange(punch);
            if (shortfall <= 0.01f || shortfall > CombatTuning.StepInReach)
            {
                return;
            }

            Motor.AddStep(flat / gap, Mathf.Min(shortfall, CombatTuning.StepInMax));
        }

        void TryLand()
        {
            if (Opponent == null || Rig == null || ActivePunch == null)
            {
                return;
            }

            Vector3 point = Rig.GetGloveWorldPosition(ActiveHand);
            Vector3 from = _gloveTrailValid ? _gloveTrail : point;

            // Sweep the glove's path, not a point on it. Two reasons, and the hurtboxes are small
            // enough now that both bite: a strike crosses a good part of a head in one frame, and
            // at close range it finishes *behind* the head entirely. A point test calls the first
            // a miss by luck of timing and the second a miss outright, which is exactly the
            // "punched straight through him" complaint.
            int count = Physics.OverlapCapsuleNonAlloc(from, point, ActivePunch.HitRadius, _overlap,
                ~0, QueryTriggerInteraction.Collide);

            Hurtbox best = null;
            for (int i = 0; i < count; i++)
            {
                Hurtbox box = _overlap[i].GetComponent<Hurtbox>();
                if (box == null || box.Owner != Opponent)
                {
                    continue;
                }
                if (best == null || box.Zone == HitZone.Head)
                {
                    best = box;
                }
            }

            if (best == null)
            {
                return;
            }

            _punchLanded = true;
            ResolveHit(best, point);
        }

        void ResolveHit(Hurtbox hurtbox, Vector3 point)
        {
            PunchDefinition punch = ActivePunch;
            Fighter target = hurtbox.Owner;

            float damage = Stats.PunchDamage * _profile.Attack * punch.DamageMultiplier;
            if (IsExhausted)
            {
                damage *= CombatTuning.ExhaustedDamageMultiplier;
            }

            // The legs are behind a body shot thrown from a duck.
            if (hurtbox.Zone == HitZone.Body)
            {
                damage *= Mathf.Lerp(1f, CombatTuning.CrouchBodyDamage, CrouchAmount);
            }

            // What the feet contributed, and where it landed. Both are the difference between a
            // punch that connects and a punch that hurts.
            damage *= TransferPower(punch);
            damage *= RangePower(punch, DistanceTo(target));

            float counterMultiplier = 1f;
            if (_counterWindowTimer > 0f)
            {
                counterMultiplier = CombatTuning.CounterBonusMultiplier;
            }
            if (target.IsVulnerable && CombatTuning.PunishCounterMultiplier > counterMultiplier)
            {
                counterMultiplier = CombatTuning.PunishCounterMultiplier;
            }

            damage *= counterMultiplier;
            _counterWindowTimer = 0f;

            HitEvent evt = target.ReceivePunch(this, punch, hurtbox.Zone, damage, point, counterMultiplier > 1f);

            if (Landed != null)
            {
                Landed(evt);
            }
        }

        /// How much of this punch's power survived the state the feet were in. A rear hand is
        /// almost all weight transfer, so throwing one with the weight already forward guts it; a
        /// jab is a range-finder and barely notices.
        float TransferPower(PunchDefinition punch)
        {
            // The hand in the air, not the punch kind's default hand. A hook comes off either
            // one, and it is the rear hand that has the weight behind it - reading the definition
            // here would score every right hook as if it were a lead hook and quietly take two
            // thirds of its power away.
            float share = ActiveHand == HandRole.Rear
                ? CombatTuning.RearTransferShare
                : CombatTuning.LeadTransferShare;

            return Mathf.Lerp(1f - share, 1f, PunchTransfer);
        }

        /// And how much survived the distance. Peaks a little short of full extension, falls off
        /// hard when jammed up close and gently at the limit of reach - which is the difference
        /// between a punch and a push, and between a punch and a reach.
        float RangePower(PunchDefinition punch, float distance)
        {
            float range = EffectiveRange(punch);
            if (range <= 0.0001f)
            {
                return 1f;
            }

            float ideal = range * CombatTuning.IdealRangeFraction;
            float smothered = range * CombatTuning.SmotherRangeFraction;

            if (distance <= ideal)
            {
                // Jammed up with no room to extend, through to full leverage at the ideal.
                return Mathf.Lerp(CombatTuning.SmotheredPower, 1f,
                    Mathf.Clamp01((distance - smothered) / Mathf.Max(0.0001f, ideal - smothered)));
            }

            // Past it: reaching, with the body left behind.
            return Mathf.Lerp(1f, CombatTuning.ReachingPower,
                Mathf.Clamp01((distance - ideal) / Mathf.Max(0.0001f, range - ideal)));
        }

        /// Which way the punch came in, from where it touched. Lateral beats forward when the
        /// contact is well off the centre line, which is what separates a hook from a straight
        /// without having to ask what kind of punch it was - a wide straight should still rock you
        /// sideways, and a hook taken square should not.
        HitDirection Classify(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);

            if (Mathf.Abs(local.x) > Mathf.Abs(local.z) * CombatTuning.SideHitRatio)
            {
                return local.x >= 0f ? HitDirection.Right : HitDirection.Left;
            }

            return local.z >= 0f ? HitDirection.Front : HitDirection.Back;
        }

        public HitEvent ReceivePunch(Fighter attacker, PunchDefinition punch, HitZone zone,
            float rawDamage, Vector3 point, bool counter)
        {
            HitEvent evt = new HitEvent();
            evt.Attacker = attacker;
            evt.Defender = this;
            evt.Punch = punch;
            evt.Zone = zone;
            evt.Point = point;
            evt.Counter = counter;

            if (State == ActionState.Down || State == ActionState.KnockedOut)
            {
                evt.Result = HitResult.Dodged;
                return evt;
            }

            float damage = rawDamage;

            if (IsDodging)
            {
                damage *= 1f - CombatTuning.DodgeDamageReduction;
                evt.Result = HitResult.Dodged;
                OpenCounterWindow();
            }
            else if (IsGuarding && !GuardBroken)
            {
                float leak = CombatTuning.BlockDamageMultiplier / Mathf.Max(0.35f, _profile.Defense);
                if (zone == HitZone.Body)
                {
                    leak *= CombatTuning.BodyGuardLeakMultiplier;
                }

                // The hands do not come all the way up any more.
                leak *= Mathf.Lerp(1f, CombatTuning.HurtGuardLeakMultiplier, HurtFactor);

                // Bent at the waist behind a high guard, the chin is the hardest thing in boxing
                // to find. Head only - the ribs are what you have left open, which is the trade.
                if (zone == HitZone.Head)
                {
                    leak *= 1f - CrouchAmount * CombatTuning.CrouchHeadCover;
                }
                leak += punch.GuardPierce;

                if (_guardHeldTime <= CombatTuning.PerfectBlockWindow)
                {
                    leak *= 0.4f;
                    OpenCounterWindow();
                }

                damage *= Mathf.Clamp01(leak);

                GuardGauge -= rawDamage * CombatTuning.GuardGaugeDamageMultiplier;
                if (GuardGauge <= 0f)
                {
                    BreakGuard();
                }

                evt.Result = HitResult.Blocked;
            }
            else
            {
                evt.Result = HitResult.Clean;

                if (zone == HitZone.Head)
                {
                    // The chin, not the bar. A hurt fighter's goes sooner.
                    HeadTrauma += damage * CombatTuning.HeadTraumaPerDamage
                        * Mathf.Lerp(1f, CombatTuning.HeadTraumaHurtMultiplier, HurtFactor);

                    if (UnityEngine.Random.value < CombatTuning.HeadStaggerChance)
                    {
                        Stagger();
                    }
                }
                else
                {
                    Stamina = Mathf.Max(0f, Stamina - damage * CombatTuning.BodyStaminaDamageMultiplier);
                }
            }

            // Only now does any of it reach the bar, and how much depends on where it landed.
            // A guarded punch keeps its raw damage for the gauge above; this is what gets through.
            damage *= zone == HitZone.Head
                ? CombatTuning.HeadHealthMultiplier
                : CombatTuning.BodyHealthMultiplier;

            Health = Mathf.Max(0f, Health - damage);
            evt.Damage = damage;

            if (evt.Result == HitResult.Blocked)
            {
                if (zone == HitZone.Head) { HeadDamageBlocked += damage; }
                else { BodyDamageBlocked += damage; }
            }
            else if (evt.Result == HitResult.Clean)
            {
                if (zone == HitZone.Head) { HeadDamageTaken += damage; }
                else { BodyDamageTaken += damage; }
            }

            Vector3 push = transform.position - attacker.transform.position;
            push.y = 0f;
            if (push.sqrMagnitude > 0.0001f)
            {
                push.Normalize();
                Motor.AddImpulse(push * (damage * CombatTuning.KnockbackPerDamage));
            }

            LastHitDirection = Classify(point);
            LastHitSeverity = damage >= CombatTuning.HeavyHitDamage
                ? HitSeverity.Heavy : HitSeverity.Light;

            // A blocked punch still moves you, just far less than one that got through.
            if (Rig != null && evt.Result != HitResult.Dodged)
            {
                float reactionDamage = evt.Result == HitResult.Blocked ? damage * 2.2f : damage;
                Rig.PlayHitReaction(zone, reactionDamage, push);
            }

            // Two ways to go down, and they mean different things: the bar emptying is a fighter
            // worn out, the chin going is a fighter caught.
            if (Health <= 0f || HeadTrauma >= 1f)
            {
                evt.CausedKnockdown = true;
                HeadTrauma = CombatTuning.HeadTraumaAfterKnockdown;
                Knockdown();
            }

            return evt;
        }

        void OpenCounterWindow()
        {
            _counterWindowTimer = CombatTuning.CounterWindow;
        }

        void BreakGuard()
        {
            GuardGauge = 0f;
            GuardBroken = true;
            IsGuarding = false;
            _guardHeldTime = 0f;
            _guardBreakTimer = CombatTuning.GuardBreakStunTime;
        }

        void Stagger()
        {
            CancelPunch();
            IsGuarding = false;
            _guardHeldTime = 0f;
            State = ActionState.Staggered;
            _staggerTimer = CombatTuning.StaggerTime;
        }

        void Knockdown()
        {
            Knockdowns++;
            TotalKnockdowns++;
            CancelPunch();
            IsGuarding = false;
            IsDodging = false;
            GuardBroken = false;
            Health = 0f;
            Motor.Stop();

            if (Knockdowns >= CombatTuning.MaxKnockdowns)
            {
                State = ActionState.KnockedOut;
                if (KnockedOut != null)
                {
                    KnockedOut(this);
                }
            }
            else
            {
                State = ActionState.Down;
                _downTimer = CombatTuning.KnockdownRecoveryTime;
                if (Downed != null)
                {
                    Downed(this);
                }
            }
        }

        void GetUp()
        {
            State = ActionState.Free;
            Health = MaxHealth * CombatTuning.GetUpHealthRatio;
            Stamina = Mathf.Max(Stamina, MaxStamina * 0.45f);
            GuardGauge = CombatTuning.GuardGaugeMax;
            _comboStacks = 0;
            _downTimer = 0f;
        }

        public float DownCountRemaining { get { return Mathf.Max(0f, _downTimer); } }

        void RegenerateStamina(float dt)
        {
            if (IsGuarding)
            {
                return;
            }

            float rate = CombatTuning.StaminaRegenPerSecond * _profile.Stamina;
            if (StaminaRatio < CombatTuning.LowStaminaRatio)
            {
                rate *= CombatTuning.LowStaminaRegenMultiplier;
            }
            if (ActivePunch != null)
            {
                rate *= 0.35f;
            }

            Stamina = Mathf.Min(MaxStamina, Stamina + rate * dt);
        }
    }
}

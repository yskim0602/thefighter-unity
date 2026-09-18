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

        [Header("Rig")]
        public Transform EyeAnchor;
        public GloveRig Gloves;
        public Renderer[] BodyRenderers;

        [Header("Match")]
        public Fighter Opponent;
        public bool FightActive;

        public event System.Action<HitEvent> Landed;
        public event System.Action<Fighter, PunchDefinition> Whiffed;
        public event System.Action<Fighter> Downed;
        public event System.Action<Fighter> KnockedOut;

        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public float Stamina { get; private set; }
        public float MaxStamina { get; private set; }
        public float GuardGauge { get; private set; }
        public int Knockdowns { get; private set; }

        public ActionState State { get; private set; }
        public bool IsGuarding { get; private set; }
        public bool GuardBroken { get; private set; }
        public bool IsDodging { get; private set; }
        public float AimHeight { get; private set; }

        public PunchDefinition ActivePunch { get; private set; }
        public HandRole ActiveHand { get; private set; }
        /// -PunchLoadTrack while loading, 0 -> 1 as the glove extends. GloveRig turns this into a pose.
        public float PunchTrack { get; private set; }

        StyleProfile _profile;
        BoxingStyle _baseStyle = BoxingStyle.BoxerPuncher;
        IFighterBrain _brain;
        FighterMotor _motor;

        readonly Collider[] _overlap = new Collider[16];

        float _phaseTimer;
        float _phaseDuration;
        bool _punchLanded;
        float _guardHeldTime;
        float _guardBreakTimer;
        float _counterWindowTimer;
        float _dodgeTimer;
        float _dodgeCooldownTimer;
        float _staggerTimer;
        float _downTimer;
        float _comboTimer;
        int _comboStacks;

        void Awake()
        {
            _profile = BoxingStyles.Profile(Style);
            AimHeight = 1f;
        }

        FighterMotor Motor
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
            Knockdowns = 0;
            State = ActionState.Free;
            ActivePunch = null;
            PunchTrack = 0f;
            AimHeight = 1f;
            IsGuarding = false;
            IsDodging = false;
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
            Motor.Stop();
        }

        public void SetFirstPerson(bool enabled)
        {
            if (Gloves != null)
            {
                Gloves.SetFirstPerson(enabled);
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

        /// Centre-to-centre distance at which this punch still connects. Deliberately a touch
        /// short of the true geometric reach: the AI steps that little bit closer and its punches
        /// actually land, instead of pawing at the air from the edge of the maths.
        public float EffectiveRange(PunchDefinition punch)
        {
            return PunchReach(punch) + 0.05f + punch.HitRadius + 0.18f;
        }

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;

            if (!FightActive)
            {
                RefreshGloves(dt);
                return;
            }

            FighterIntent intent = _brain != null ? _brain.Think(dt) : FighterIntent.Neutral();

            AdvanceTimers(dt);

            if (State == ActionState.KnockedOut)
            {
                RefreshGloves(dt);
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
                RefreshGloves(dt);
                return;
            }

            AimHeight = Mathf.Clamp01(intent.AimHeight);

            HandleGuard(intent, dt);
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
            bool wants = intent.Guard
                && !GuardBroken
                && State == ActionState.Free
                && !IsDodging;

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

                Stamina = Mathf.Max(0f, Stamina - CombatTuning.GuardStaminaDrainPerSecond * dt);
            }
            else
            {
                IsGuarding = false;
                _guardHeldTime = 0f;

                if (!GuardBroken)
                {
                    GuardGauge = Mathf.Min(CombatTuning.GuardGaugeMax,
                        GuardGauge + CombatTuning.GuardGaugeRegenPerSecond * dt);
                }
            }
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
            float speed = Stats.MoveSpeed * _profile.Footwork;

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
            if (!intent.ThrowPunch || State != ActionState.Free || IsDodging)
            {
                return;
            }

            PunchDefinition punch = PunchLibrary.Get(intent.Punch);
            float cost = punch.StaminaCost * (1f + _comboStacks * CombatTuning.ComboStaminaScaling);

            Stamina = Mathf.Max(0f, Stamina - cost);
            _comboStacks = Mathf.Min(CombatTuning.ComboMaxStacks, _comboStacks + 1);
            _comboTimer = CombatTuning.ComboResetTime;

            IsGuarding = false;
            _guardHeldTime = 0f;
            ActivePunch = punch;
            ActiveHand = punch.Hand;
            _punchLanded = false;
            EnterPhase(ActionState.Windup, Mathf.Max(0.02f, punch.WindupTime * TimingScale()));
        }

        float TimingScale()
        {
            float scale = Stats.TimingScale;
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
                RefreshGloves(dt);
                return;
            }

            _phaseTimer += dt;

            while (ActivePunch != null && _phaseTimer >= _phaseDuration)
            {
                float carry = _phaseTimer - _phaseDuration;

                if (State == ActionState.Windup)
                {
                    EnterPhase(ActionState.Strike, ActivePunch.StrikeTime * TimingScale());
                    _phaseTimer = carry;
                }
                else if (State == ActionState.Strike)
                {
                    // Never let a short punch skip past full extension without a hit test.
                    PunchTrack = 1f;
                    RefreshGloves(0f);
                    if (!_punchLanded)
                    {
                        TryLand();
                    }
                    if (!_punchLanded && Whiffed != null)
                    {
                        Whiffed(this, ActivePunch);
                    }

                    float recovery = ActivePunch.RecoveryTime * TimingScale() / Mathf.Max(0.5f, _profile.Stamina);
                    EnterPhase(ActionState.Recovery, recovery);
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
                RefreshGloves(dt);
                return;
            }

            float p = Mathf.Clamp01(_phaseTimer / _phaseDuration);
            PunchTrack = TrackForPhase(p);
            RefreshGloves(dt);

            if (State == ActionState.Strike && !_punchLanded && PunchTrack >= CombatTuning.PunchHitTrackThreshold)
            {
                TryLand();
            }
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
                    return Mathf.Lerp(1f, 0f, p * p);
                default:
                    return 0f;
            }
        }

        void EndPunch()
        {
            ActivePunch = null;
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

        void RefreshGloves(float dt)
        {
            if (Gloves != null)
            {
                Gloves.Refresh(dt);
            }
        }

        // ------------------------------------------------------------------
        // Hitting and being hit
        // ------------------------------------------------------------------

        void TryLand()
        {
            if (Opponent == null || Gloves == null || ActivePunch == null)
            {
                return;
            }

            Vector3 point = Gloves.GetGloveWorldPosition(ActiveHand);
            int count = Physics.OverlapSphereNonAlloc(point, ActivePunch.HitRadius, _overlap,
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

            Health = Mathf.Max(0f, Health - damage);
            evt.Damage = damage;

            Vector3 push = transform.position - attacker.transform.position;
            push.y = 0f;
            if (push.sqrMagnitude > 0.0001f)
            {
                Motor.AddImpulse(push.normalized * (damage * CombatTuning.KnockbackPerDamage));
            }

            if (Health <= 0f)
            {
                evt.CausedKnockdown = true;
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

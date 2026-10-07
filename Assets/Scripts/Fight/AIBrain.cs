using UnityEngine;

namespace TheFighter
{
    /// The opponent. Built in two halves that run at different speeds, because a boxer's hands and
    /// a boxer's plan are not the same thing.
    ///
    /// **Reflex, every frame.** Raising the guard against an incoming punch is a reaction, not a
    /// decision. This used to be decided on the planning tick alone - every 0.22s against a punch
    /// that lands in 0.20s - so the AI could not react to anything and the player's attacks landed
    /// for free. Now a punch starting is seen the frame it starts, and answered after a delay set
    /// by skill: slow hands take a quarter of a second, good hands under a tenth.
    ///
    /// **Plan, on a varying interval.** What to throw, when to step in, when to lie. This is where
    /// the psychology lives, and it is the part that makes a fight feel like somebody is in there:
    ///
    /// - **Guard discipline.** Hands up by default whenever inside punching range. The cost is gas
    ///   and a guard that leaks to the body, which is what gives the player an answer rather than
    ///   a wall.
    /// - **Feints.** A windup with nothing behind it, thrown at a fighter who has shown they react
    ///   to windups. Draws the guard, then the real punch goes where the guard just left.
    /// - **Baiting.** Deliberately dropping the hands at a fighter who has shown they take the
    ///   invitation, with the counter already primed.
    /// - **Rhythm.** Flurries and lulls instead of a metronome. An AI that acts on a fixed beat is
    ///   readable within about three exchanges however good its choices are.
    /// - **Pattern punishment.** It learns what follows your jab and starts covering it.
    public class AIBrain : MonoBehaviour, IFighterBrain
    {
        [Header("Skill")]
        /// Drives reaction time, how often the reads are acted on, and how disciplined the guard
        /// is. The career sets it per opponent.
        [Range(0f, 1f)] public float Skill = 0.6f;

        [Header("Reflex")]
        /// Reaction to a punch starting, at Skill 0 and Skill 1. Human reaction to a visual cue is
        /// around 0.2s, so a fully skilled fighter here is deliberately superhuman - he is
        /// reading the shoulder, not waiting for the glove.
        public float ReactionSlow = 0.26f;
        public float ReactionFast = 0.07f;

        [Header("Planning")]
        public float PlanInterval = 0.22f;
        /// A flurry plans this much faster, a lull this much slower.
        public float BurstScale = 0.45f;
        public float LullScale = 2.1f;

        [Header("Guard discipline")]
        /// Below this much gas he stops holding a guard he does not need - which is the opening a
        /// body attack is supposed to buy.
        public float GuardStaminaFloor = 0.22f;
        /// How far outside his own jab range he still keeps the hands up.
        public float GuardRangeMargin = 0.3f;

        [Header("Psychology")]
        /// Chance of feinting instead of punching, before the read scales it. A fighter who never
        /// reacts cannot be drawn, so this ends up near zero against one.
        [Range(0f, 1f)] public float FeintChance = 0.3f;
        /// Chance of dropping the hands to invite a punch. Scaled by how much pressure he is
        /// under: baiting a fighter who will not come forward achieves nothing.
        [Range(0f, 1f)] public float BaitChance = 0.22f;
        public float BaitSeconds = 0.7f;

        [Header("Ring craft")]
        /// How long one intention lasts before it is reconsidered. Seconds, not frames: this is
        /// meant to read as a decision a fighter made, and anything shorter reads as jitter.
        public float ApproachSecondsMin = 1.3f;
        public float ApproachSecondsMax = 3.4f;
        /// How far past the centre an opponent has to drift before cutting the ring is worth
        /// doing, as a fraction of the ring's half extent. Cutting somebody off in open space
        /// achieves nothing.
        public float CutThreshold = 0.45f;
        /// And how close to the ropes the fighter has to be before getting himself out matters
        /// more than cornering anybody.
        public float EscapeThreshold = 0.62f;

        [Header("Style")]
        public float EarlyPhaseSeconds = 12f;
        public float LowHealthRatio = 0.35f;
        public float CounterReactionTime = 1.4f;

        Fighter _self;
        Fighter _opponent;
        readonly OpponentRead _read = new OpponentRead();

        float _planTimer;
        float _fightTime;
        float _reactionTimer;
        float _lastHealth;

        // Reflex
        float _reflexTimer;
        bool _reflexArmed;
        bool _answeredThisPunch;
        float _guardTimer;
        float _leanTimer;
        float _leanDirection = 1f;
        float _crouchTimer;
        bool _dodgeQueued;

        // Plan
        /// What the fighter is trying to do with the distance right now. The old controller had
        /// no equivalent - it held one number - and that is what made it feel glued.
        enum Approach
        {
            /// Close it down and stay there.
            Pressure,
            /// Sit at his own range and work.
            Hold,
            /// Step out, breathe, reset the exchange.
            Reset,
            /// Take the angle that shrinks the space the other man can escape into.
            Cut
        }

        Approach _approach = Approach.Hold;
        float _approachTimer;
        float _wantDistance = 1f;
        float _band = 0.3f;
        float _closeSpeed = 1f;
        float _circleAmount = 0.5f;

        float _circleTimer;
        float _circleDirection = 1f;
        float _aim = 1f;
        bool _punchQueued;
        bool _feintQueued;
        PunchKind _queuedPunch;
        float _baitTimer;
        int _burstLeft;
        float _rhythmScale = 1f;
        float _pressTimer;

        public OpponentRead Read { get { return _read; } }
        public bool Baiting { get { return _baitTimer > 0f; } }

        public void Attach(Fighter self, Fighter opponent)
        {
            _self = self;
            _opponent = opponent != null ? opponent : (self != null ? self.Opponent : null);
            _lastHealth = self != null ? self.Health : 0f;
            _fightTime = 0f;
            _reactionTimer = 0f;
            _approachTimer = 0f;
            _read.Reset();
        }

        // ------------------------------------------------------------------

        public FighterIntent Think(float deltaTime)
        {
            FighterIntent intent = FighterIntent.Neutral();

            if (_self == null)
            {
                return intent;
            }
            if (_opponent == null)
            {
                _opponent = _self.Opponent;
                if (_opponent == null)
                {
                    return intent;
                }
            }

            _fightTime += deltaTime;
            TrackDamage(deltaTime);
            UpdateStyle();
            _read.Observe(_opponent, deltaTime);

            if (_self.State == ActionState.Down)
            {
                intent.MashGetUp = Random.value < 0.3f;
                return intent;
            }
            if (_self.IsFinished || _opponent.IsFinished)
            {
                return intent;
            }

            Tick(ref _guardTimer, deltaTime);
            Tick(ref _leanTimer, deltaTime);
            Tick(ref _crouchTimer, deltaTime);
            Tick(ref _baitTimer, deltaTime);
            Tick(ref _pressTimer, deltaTime);

            Reflex(deltaTime);

            _circleTimer -= deltaTime;
            if (_circleTimer <= 0f)
            {
                _circleTimer = Random.Range(1.2f, 2.8f);
                _circleDirection = Random.value < 0.5f ? -1f : 1f;
            }

            _approachTimer -= deltaTime;
            if (_approachTimer <= 0f)
            {
                PickApproach();
            }

            _planTimer -= deltaTime;
            if (_planTimer <= 0f)
            {
                _planTimer = PlanInterval * _rhythmScale * Random.Range(0.8f, 1.25f);
                Plan();
            }

            intent.Move = ComputeMove();
            intent.Guard = WantsGuard();
            intent.Lean = _leanTimer > 0f ? _leanDirection : 0f;
            intent.Crouch = _crouchTimer > 0f;
            intent.AimHeight = _aim;

            if (_dodgeQueued)
            {
                intent.Dodge = true;
                _dodgeQueued = false;
            }
            if (_punchQueued)
            {
                intent.ThrowPunch = true;
                intent.Punch = _queuedPunch;
                intent.Feint = _feintQueued;
                _punchQueued = false;
                _feintQueued = false;
            }

            return intent;
        }

        static void Tick(ref float timer, float deltaTime)
        {
            if (timer > 0f)
            {
                timer -= deltaTime;
            }
        }

        // ------------------------------------------------------------------
        // Reflex - every frame, because the hands are not on a committee
        // ------------------------------------------------------------------

        void Reflex(float deltaTime)
        {
            bool incoming = _opponent.ActivePunch != null
                && _self.DistanceTo(_opponent)
                    <= _opponent.EffectiveRange(_opponent.ActivePunch) + 0.25f;

            if (!incoming)
            {
                _reflexArmed = false;
                _answeredThisPunch = false;
                return;
            }

            if (!_reflexArmed)
            {
                _reflexArmed = true;
                _reflexTimer = Mathf.Lerp(ReactionSlow, ReactionFast, Skill);
            }

            if (_answeredThisPunch)
            {
                return;
            }

            _reflexTimer -= deltaTime;
            if (_reflexTimer > 0f)
            {
                return;
            }

            _answeredThisPunch = true;

            // A fighter he is baiting gets no defence - the whole point was to be hit at, and
            // flinching out of it wastes the invitation.
            if (_baitTimer > 0f)
            {
                return;
            }

            // Missing a reaction is how skill reads as skill. A weak fighter simply does not get
            // his hands up in time, which is more honest than giving him a worse guard.
            if (Random.value > Mathf.Lerp(0.45f, 0.98f, Skill))
            {
                return;
            }

            Answer(_opponent.ActivePunch);
        }

        /// Picks a way out, weighted by where the punch is going rather than at random.
        void Answer(PunchDefinition punch)
        {
            bool high = _opponent.AimHeight >= 0.5f;
            float roll = Random.value;

            // Against the body the guard is the worst answer and stepping back the best, so the
            // weights invert rather than the AI just rolling the same dice.
            if (!high)
            {
                if (roll < 0.45f) { _crouchTimer = Random.Range(0.2f, 0.4f); return; }
                if (roll < 0.7f) { _dodgeQueued = true; return; }
                _guardTimer = Random.Range(0.25f, 0.45f);
                return;
            }

            if (roll < 0.52f && !_self.GuardBroken)
            {
                _guardTimer = Random.Range(0.3f, 0.6f);
                return;
            }
            if (roll < 0.72f)
            {
                _leanTimer = Random.Range(0.22f, 0.4f);
                // Lean away from the hand it is coming from, not at random.
                _leanDirection = punch.Hand == HandRole.Lead ? 1f : -1f;
                return;
            }
            if (roll < 0.88f)
            {
                _dodgeQueued = true;
                return;
            }
            _crouchTimer = Random.Range(0.2f, 0.35f);
        }

        /// Hands up by default inside range. The reason this is affordable is that it is not free:
        /// it drains gas and it leaks to the body, so the player's answer is to go downstairs.
        bool WantsGuard()
        {
            if (_self.GuardBroken || _baitTimer > 0f)
            {
                return false;
            }
            if (_guardTimer > 0f)
            {
                return true;
            }
            if (_self.StaminaRatio < GuardStaminaFloor)
            {
                return false;
            }

            // Not while committing to something of his own - but recovery is not committing, it
            // is the hands coming back, and that is exactly when the counter arrives. The player
            // got this fixed in HandleGuard; giving the AI less would be handing out a tool.
            if (_punchQueued
                || (_self.ActivePunch != null && _self.State != ActionState.Recovery))
            {
                return false;
            }

            float distance = _self.DistanceTo(_opponent);
            bool inRange = distance
                <= _opponent.EffectiveRange(PunchLibrary.Jab) + GuardRangeMargin;

            // Discipline is a skill. A novice drops his hands between exchanges.
            return inRange && Random.value < Mathf.Lerp(0.35f, 1f, Skill);
        }

        // ------------------------------------------------------------------
        // Plan - what to throw, and what to lie about
        // ------------------------------------------------------------------

        void Plan()
        {
            float distance = _self.DistanceTo(_opponent);
            StyleProfile profile = _self.Profile;

            UpdateRhythm();

            // A counter beats every other consideration, and a bait that worked is a counter.
            if (_self.CounterWindowOpen || _opponent.IsVulnerable)
            {
                if (distance <= _self.EffectiveRange(PunchLibrary.Straight) + 0.15f)
                {
                    _baitTimer = 0f;
                    Throw(PickCounterPunch(distance), false);
                    return;
                }
            }

            if (_baitTimer > 0f)
            {
                return;
            }

            // Pattern punishment: he knows what follows your jab, so he covers it before it comes.
            if (_opponent.ActivePunch == null && _read.AfterJabIsLive())
            {
                float confidence;
                PunchKind expected = _read.ExpectedAfterJab(out confidence);
                if (confidence > 0.55f && Random.value < Skill * confidence)
                {
                    PunchDefinition def = PunchLibrary.Get(expected);
                    _guardTimer = Mathf.Max(_guardTimer, def.TotalTime + 0.1f);
                }
            }

            if (distance > profile.PreferredDistance + 0.25f)
            {
                return;
            }

            if (_self.StaminaRatio < 0.2f && Random.value < 0.5f)
            {
                _guardTimer = Random.Range(0.3f, 0.8f);
                return;
            }

            // Baiting: only worth it against someone who comes forward, and only if there is gas
            // to punish with.
            if (TryBait(distance))
            {
                return;
            }

            if (distance <= _self.EffectiveRange(PunchLibrary.Jab)
                && Random.value < profile.Aggression)
            {
                // Feinting: only worth it against someone who reacts. The read decides, not a
                // dice roll - against a fighter who stands there and eats it, this is ~0.
                bool feint = _pressTimer <= 0f
                    && Random.value < FeintChance * _read.Reactiveness * Skill;

                Throw(feint ? PickFeint(distance) : PickStylePunch(distance), feint);
                return;
            }

            if (!_self.GuardBroken && Random.value < 0.3f)
            {
                _guardTimer = Random.Range(0.2f, 0.6f);
            }
        }

        bool TryBait(float distance)
        {
            if (_read.Pressure < 1.2f || _self.StaminaRatio < 0.45f)
            {
                return false;
            }
            if (distance > _self.EffectiveRange(PunchLibrary.Straight))
            {
                return false;
            }
            if (Random.value >= BaitChance * Skill)
            {
                return false;
            }

            // Hands down, feet planted, in range. Everything that happens next is on purpose.
            _baitTimer = BaitSeconds * Random.Range(0.8f, 1.3f);
            _guardTimer = 0f;
            return true;
        }

        /// Flurries and lulls. A fixed beat is readable within about three exchanges whatever the
        /// choices on it are, so the beat itself moves.
        void UpdateRhythm()
        {
            if (_burstLeft > 0)
            {
                _burstLeft--;
                return;
            }

            float roll = Random.value;
            if (roll < 0.22f)
            {
                _burstLeft = Random.Range(2, 5);
                _rhythmScale = BurstScale;
            }
            else if (roll < 0.38f)
            {
                _burstLeft = Random.Range(1, 3);
                _rhythmScale = LullScale;
            }
            else
            {
                _rhythmScale = 1f;
            }
        }

        // ------------------------------------------------------------------

        void TrackDamage(float deltaTime)
        {
            if (_reactionTimer > 0f)
            {
                _reactionTimer -= deltaTime;
            }
            if (_self.Health < _lastHealth - 0.01f)
            {
                _reactionTimer = CounterReactionTime;
            }
            _lastHealth = _self.Health;
        }

        void UpdateStyle()
        {
            BoxingStyle wanted = _self.BaseStyle;

            if (_fightTime < EarlyPhaseSeconds)
            {
                wanted = BoxingStyle.OutBoxer;
            }
            if (_self.HealthRatio < LowHealthRatio)
            {
                wanted = BoxingStyle.InFighter;
            }
            if (_reactionTimer > 0f)
            {
                wanted = BoxingStyle.CounterPuncher;
            }

            if (_self.Style != wanted)
            {
                _self.SetActiveStyle(wanted);
            }
        }

        /// Picks what to do with the distance for the next few seconds.
        ///
        /// The controller this replaced held a single preferred distance with a 0.30m deadband,
        /// which inside a 6.2m ring meant the entire fight happened in five percent of the
        /// available space - back away and it closed instantly, every time, so it read as being
        /// on a string rather than as being chased. A fighter decides *when* to come in; that is
        /// what this is.
        void PickApproach()
        {
            _approachTimer = Random.Range(ApproachSecondsMin, ApproachSecondsMax);

            float preferred = _self.Profile.PreferredDistance;
            float aggression = _self.Profile.Aggression;

            // Weights, not a decision tree, so the same situation does not always produce the same
            // fighter. Tired or hurt leans toward resetting; an aggressive style leans in.
            float tired = 1f - Mathf.Clamp01(_self.StaminaRatio);
            float hurt = _self.HurtFactor;

            float pressure = aggression * 1.2f * (1f - tired * 0.8f) * (1f - hurt * 0.7f);
            float hold = 0.9f;
            float reset = 0.35f + tired * 1.6f + hurt * 1.2f;
            float cut = Cornered(_opponent) ? aggression * 1.8f : 0.15f;

            float roll = Random.value * (pressure + hold + reset + cut);

            if (roll < pressure) { _approach = Approach.Pressure; }
            else if (roll < pressure + hold) { _approach = Approach.Hold; }
            else if (roll < pressure + hold + reset) { _approach = Approach.Reset; }
            else { _approach = Approach.Cut; }

            switch (_approach)
            {
                case Approach.Pressure:
                    // Inside his own jab, where the work gets done.
                    _wantDistance = preferred * 0.78f;
                    _band = 0.14f;
                    _closeSpeed = 1f;
                    _circleAmount = 0.3f;
                    break;

                case Approach.Reset:
                    // Out of range on purpose, and moving while he is there.
                    _wantDistance = preferred + Random.Range(0.7f, 1.4f);
                    _band = 0.35f;
                    _closeSpeed = 0.55f;
                    _circleAmount = 0.8f;
                    break;

                case Approach.Cut:
                    _wantDistance = preferred + 0.15f;
                    _band = 0.3f;
                    _closeSpeed = 0.75f;
                    _circleAmount = 1f;
                    break;

                default:
                    _wantDistance = preferred;
                    // A wide band is most of what stops it feeling glued: inside it he simply
                    // does not correct, so the distance drifts the way a real one does.
                    _band = 0.42f;
                    _closeSpeed = 0.7f;
                    _circleAmount = 0.55f;
                    break;
            }
        }

        Vector2 ComputeMove()
        {
            // While baiting he holds his ground rather than drifting out of the trap.
            if (_baitTimer > 0f)
            {
                return new Vector2(_circleDirection * 0.2f, 0f);
            }

            float distance = _self.DistanceTo(_opponent);

            float forward = 0f;
            if (distance > _wantDistance + _band)
            {
                forward = _closeSpeed;
            }
            else if (distance < _wantDistance - _band)
            {
                forward = -0.8f;
            }

            return new Vector2(Lateral() * _circleAmount, forward);
        }

        /// Which way to circle. Getting off the ropes beats cornering somebody, and cornering
        /// somebody beats drifting - so the three are checked in that order.
        float Lateral()
        {
            Vector3 centre = Vector3.zero;

            Vector3 mine = Flat(_self.transform.position - centre);
            if (mine.magnitude > CombatTuning.RingHalfExtent * EscapeThreshold)
            {
                // On the ropes himself: circle toward open floor rather than along them.
                return Side(-mine);
            }

            if (_approach == Approach.Cut || Cornered(_opponent))
            {
                // The space he can escape into is the way back to the middle, so taking that side
                // is what shrinks it. This is ring cutting, and it is the difference between
                // chasing a man and trapping one.
                Vector3 escape = Flat(centre - _opponent.transform.position);
                if (escape.sqrMagnitude > 0.01f)
                {
                    return Side(escape);
                }
            }

            return _circleDirection;
        }

        bool Cornered(Fighter fighter)
        {
            return Flat(fighter.transform.position).magnitude
                > CombatTuning.RingHalfExtent * CutThreshold;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// Turns a world direction into "circle left or right" in the fighter's own frame.
        float Side(Vector3 worldDirection)
        {
            float dot = Vector3.Dot(_self.transform.right, worldDirection.normalized);
            return Mathf.Abs(dot) < 0.15f ? _circleDirection : Mathf.Sign(dot);
        }

        PunchKind PickStylePunch(float distance)
        {
            PunchKind[] pool = BoxingStyles.PreferredPunches(_self.Style);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                PunchKind candidate = pool[Random.Range(0, pool.Length)];
                if (distance <= _self.EffectiveRange(PunchLibrary.Get(candidate)))
                {
                    return candidate;
                }
            }
            return PunchKind.Jab;
        }

        /// A feint wants the longest windup he can afford - that is the part being sold.
        PunchKind PickFeint(float distance)
        {
            if (distance <= _self.EffectiveRange(PunchLibrary.Hook) && Random.value < 0.5f)
            {
                return PunchKind.Hook;
            }
            return PunchKind.Straight;
        }

        PunchKind PickCounterPunch(float distance)
        {
            if (distance <= _self.EffectiveRange(PunchLibrary.Uppercut) && Random.value < 0.4f)
            {
                return PunchKind.Uppercut;
            }
            if (distance <= _self.EffectiveRange(PunchLibrary.Hook) && Random.value < 0.4f)
            {
                return PunchKind.Hook;
            }
            return PunchKind.Straight;
        }

        void Throw(PunchKind kind, bool feint)
        {
            _punchQueued = true;
            _feintQueued = feint;
            _queuedPunch = kind;

            PunchDefinition def = PunchLibrary.Get(kind);

            // Aim where the guard is not. A fighter who has been blocking high gets it downstairs,
            // which is the same lesson the player is meant to learn in the other direction.
            float bias = def.AimBias;
            if (Random.value < Skill)
            {
                bias += (0.5f - _read.HeadBias) * 0.8f;
            }
            bias = Mathf.Clamp01(bias + Random.Range(-0.2f, 0.2f));
            _aim = bias > 0.5f ? 1f : 0f;

            // After a feint, the real punch follows close behind - that is what the feint bought.
            // The lockout stops him feinting twice in a row, which fools nobody.
            if (feint)
            {
                _planTimer = def.WindupTime * 1.1f;
                _pressTimer = 1.4f;
            }
        }
    }
}

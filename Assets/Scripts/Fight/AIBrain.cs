using UnityEngine;

namespace TheFighter
{
    /// The opponent. It picks on a slow tick rather than every frame so its reactions feel human,
    /// and - like the Godot build - it drops whatever it was doing the moment a counter opens up.
    /// Its style is not a fixed job: it feels the fight out, presses when hurt, and switches to
    /// counter-punching right after eating one.
    public class AIBrain : MonoBehaviour, IFighterBrain
    {
        [Header("Rhythm")]
        public float DecisionInterval = 0.17f;

        [Header("Dynamic style")]
        public float EarlyPhaseSeconds = 12f;
        public float LowHealthRatio = 0.35f;
        public float CounterReactionTime = 1.4f;

        [Header("Difficulty")]
        [Range(0f, 1f)] public float Skill = 0.6f;

        Fighter _self;
        Fighter _opponent;

        float _decisionTimer;
        float _fightTime;
        float _reactionTimer;
        float _lastHealth;
        float _guardTimer;
        float _circleTimer;
        float _circleDirection = 1f;
        float _aim = 1f;
        bool _punchQueued;
        bool _dodgeQueued;
        PunchKind _queuedPunch = PunchKind.Jab;

        public void Attach(Fighter self, Fighter opponent)
        {
            _self = self;
            _opponent = opponent != null ? opponent : (self != null ? self.Opponent : null);
            _lastHealth = self != null ? self.Health : 0f;
            _fightTime = 0f;
            _reactionTimer = 0f;
        }

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

            if (_self.State == ActionState.Down)
            {
                intent.MashGetUp = Random.value < 0.3f;
                return intent;
            }
            if (_self.IsFinished || _opponent.IsFinished)
            {
                return intent;
            }

            if (_guardTimer > 0f)
            {
                _guardTimer -= deltaTime;
            }

            _circleTimer -= deltaTime;
            if (_circleTimer <= 0f)
            {
                _circleTimer = Random.Range(1.2f, 2.8f);
                _circleDirection = Random.value < 0.5f ? -1f : 1f;
            }

            _decisionTimer -= deltaTime;
            if (_decisionTimer <= 0f)
            {
                _decisionTimer = DecisionInterval * Random.Range(0.75f, 1.3f);
                Decide();
            }

            intent.Move = ComputeMove();
            intent.Guard = _guardTimer > 0f;
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
                _punchQueued = false;
            }

            return intent;
        }

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

        void Decide()
        {
            float distance = _self.DistanceTo(_opponent);
            StyleProfile profile = _self.Profile;

            // A counter beats every other consideration.
            if (_self.CounterWindowOpen || _opponent.IsVulnerable)
            {
                if (distance <= _self.EffectiveRange(PunchLibrary.Straight) + 0.15f)
                {
                    QueuePunch(PickCounterPunch(distance));
                    return;
                }
            }

            bool threatened = _opponent.ActivePunch != null
                && distance <= _opponent.EffectiveRange(_opponent.ActivePunch) + 0.2f;

            if (threatened && Random.value < Skill)
            {
                if (!_self.GuardBroken && Random.value < 0.65f)
                {
                    _guardTimer = Random.Range(0.25f, 0.5f);
                }
                else
                {
                    _dodgeQueued = true;
                }
                return;
            }

            if (distance > profile.PreferredDistance + 0.25f)
            {
                _guardTimer = 0f;
                return;
            }

            if (_self.StaminaRatio < 0.2f && Random.value < 0.5f)
            {
                _guardTimer = Random.Range(0.3f, 0.8f);
                return;
            }

            if (distance <= _self.EffectiveRange(PunchLibrary.Jab) && Random.value < profile.Aggression)
            {
                QueuePunch(PickStylePunch(distance));
                return;
            }

            if (!_self.GuardBroken && Random.value < 0.3f)
            {
                _guardTimer = Random.Range(0.2f, 0.6f);
            }
        }

        Vector2 ComputeMove()
        {
            float distance = _self.DistanceTo(_opponent);
            float preferred = _self.Profile.PreferredDistance;

            float forward = 0f;
            if (distance > preferred + 0.12f)
            {
                forward = 1f;
            }
            else if (distance < preferred - 0.18f)
            {
                forward = -0.7f;
            }

            return new Vector2(_circleDirection * 0.55f, forward);
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

        void QueuePunch(PunchKind kind)
        {
            _punchQueued = true;
            _queuedPunch = kind;

            PunchDefinition def = PunchLibrary.Get(kind);
            float bias = Mathf.Clamp01(def.AimBias + Random.Range(-0.35f, 0.35f));
            _aim = bias > 0.5f ? 1f : 0f;
        }
    }
}

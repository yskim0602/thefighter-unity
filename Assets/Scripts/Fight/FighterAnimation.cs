using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TheFighter
{
    /// Plays Humanoid clips on a real model without an Animator Controller.
    ///
    /// Three reasons it is built this way:
    ///
    /// 1. **Code owns the timing.** Every clip's time is set explicitly each frame - punches from
    ///    Fighter.PunchProgress, idle and footwork from an accumulated loop clock. Mixamo clip
    ///    lengths have nothing to do with our windup/strike/recovery numbers, and if the clips
    ///    drove the timing then every range, counter window and scorecard weight would shift.
    ///    This way the animation follows the fight instead of dictating it, and no clip needs its
    ///    Loop Time or length touched on import.
    ///
    /// 2. **No state machine to hand-author.** A controller graph with a dozen states and
    ///    transitions is tedious to build in the editor and impossible to review as a diff. Drag
    ///    clips into the slots below and that is the whole setup.
    ///
    /// 3. **It fails soft.** Empty slots are skipped, and if nothing is assigned or the graph
    ///    cannot be built, this component does nothing at all and the procedural capsule rig keeps
    ///    running. An animation bug must never cost us a playable fight.
    /// The clips, in one place. Lives on BoxingBootstrap so both fighters share a single set you
    /// drag once - the scene is built from code, so a component added at runtime has nowhere to
    /// hold Inspector references of its own.
    [System.Serializable]
    public class BoxerClipSet
    {
        [Header("Stance and footwork (looping)")]
        public AnimationClip Idle;
        public AnimationClip Guard;
        public AnimationClip StepForward;
        public AnimationClip StepBack;
        public AnimationClip StepSide;

        [Header("Punches (driven by Fighter.PunchProgress)")]
        public AnimationClip Jab;
        public AnimationClip Straight;
        public AnimationClip Hook;
        public AnimationClip Uppercut;

        [Header("Reactions")]
        public AnimationClip Slip;
        public AnimationClip Down;
    }

    public class FighterAnimation : MonoBehaviour
    {
        public Fighter Owner;
        public Animator ModelAnimator;
        public BoxerClipSet Clips;

        [Header("Blending")]
        /// Weights are damped rather than set outright. Without this the stance/footwork mix
        /// follows the AI's frame-to-frame jitter and the skeleton visibly shivers.
        public float StanceBlendSpeed = 9f;
        /// Punches have to land on their frame, so they are not damped nearly as much.
        public float ActionBlendSpeed = 36f;
        /// Below this much movement it is pure stance, above the next it is pure footwork. The gap
        /// between them is deliberately narrow: a long cross-fade averages two unrelated poses and
        /// that average is what reads as boneless.
        public float StepDeadzone = 0.12f;
        public float StepFullSpeed = 0.55f;

        [Header("Punch clips")]
        /// How much of a punch clip the punch maps onto. Mixamo punches include a long wind-up and
        /// return, and squeezing all of it into our 0.3s jab looks frantic; 0.5 uses the first half
        /// at half the speed. Our timing does not change either way - only which frames you see.
        [Range(0.1f, 1f)] public float PunchClipPortion = 0.6f;

        enum Slot
        {
            Idle,
            Guard,
            StepForward,
            StepBack,
            StepSide,
            Jab,
            Straight,
            Hook,
            Uppercut,
            Slip,
            Down,
            Count
        }

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable[] _playables;
        float[] _lengths;
        float[] _weights;
        float[] _smoothed;
        bool[] _assigned;
        bool _built;

        float _baseClock;
        float _slipClock;
        float _downClock;
        float _guardBlend;

        void Start()
        {
            Build();
        }

        void OnDestroy()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }

        AnimationClip ClipFor(Slot slot)
        {
            if (Clips == null)
            {
                return null;
            }

            switch (slot)
            {
                case Slot.Idle: return Clips.Idle;
                case Slot.Guard: return Clips.Guard;
                case Slot.StepForward: return Clips.StepForward;
                case Slot.StepBack: return Clips.StepBack;
                case Slot.StepSide: return Clips.StepSide;
                case Slot.Jab: return Clips.Jab;
                case Slot.Straight: return Clips.Straight;
                case Slot.Hook: return Clips.Hook;
                case Slot.Uppercut: return Clips.Uppercut;
                case Slot.Slip: return Clips.Slip;
                case Slot.Down: return Clips.Down;
                default: return null;
            }
        }

        void Build()
        {
            if (ModelAnimator == null || Owner == null || Clips == null)
            {
                return;
            }

            int count = (int)Slot.Count;
            _playables = new AnimationClipPlayable[count];
            _lengths = new float[count];
            _weights = new float[count];
            _smoothed = new float[count];
            _assigned = new bool[count];

            // Our motor owns position. Root motion on top of it would move everyone twice, which
            // also means nobody has to remember Mixamo's "In Place" checkbox.
            ModelAnimator.applyRootMotion = false;

            _graph = PlayableGraph.Create("FighterAnimation:" + Owner.FighterName);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Pose", ModelAnimator);
            _mixer = AnimationMixerPlayable.Create(_graph, count, true);
            output.SetSourcePlayable(_mixer);

            bool any = false;
            for (int i = 0; i < count; i++)
            {
                AnimationClip clip = ClipFor((Slot)i);
                if (clip == null)
                {
                    continue;
                }

                AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
                playable.SetApplyFootIK(false);
                // Paused, because we scrub the time ourselves every frame.
                playable.Pause();

                _graph.Connect(playable, 0, _mixer, i);
                _mixer.SetInputWeight(i, 0f);

                _playables[i] = playable;
                _lengths[i] = Mathf.Max(0.01f, clip.length);
                _assigned[i] = true;
                any = true;
            }

            if (!any)
            {
                _graph.Destroy();
                return;
            }

            _graph.Play();
            _built = true;
        }

        void LateUpdate()
        {
            if (!_built || Owner == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _baseClock += dt;

            for (int i = 0; i < _weights.Length; i++)
            {
                _weights[i] = 0f;
            }

            // Every looping clip advances every frame whatever its weight, so a clip fading out is
            // still moving. Freezing it mid-fade is half of what makes a blend look wrong.
            for (int i = 0; i < (int)Slot.Jab; i++)
            {
                if (_assigned[i])
                {
                    _playables[i].SetTime(Mathf.Repeat(_baseClock, _lengths[i]));
                }
            }

            int action = ResolveAction(dt);
            float actionWeight = 0f;

            if (action >= 0)
            {
                actionWeight = 1f;
                _weights[action] = 1f;
            }

            float baseWeight = 1f - actionWeight;
            if (baseWeight > 0.001f)
            {
                ApplyStanceAndFootwork(baseWeight, dt);
            }

            Commit(dt);
        }

        /// Returns the slot to play as a one-shot this frame, or -1 for none.
        int ResolveAction(float dt)
        {
            if (Owner.State == ActionState.Down || Owner.State == ActionState.KnockedOut)
            {
                _downClock += dt;
                return Scrub(Slot.Down, _downClock / _lengths[(int)Slot.Down]);
            }

            _downClock = 0f;

            PunchDefinition punch = Owner.ActivePunch;
            if (punch != null)
            {
                return Scrub(SlotForPunch(punch.Kind),
                    Owner.PunchProgress * Mathf.Clamp01(PunchClipPortion));
            }

            if (Owner.IsDodging)
            {
                _slipClock += dt;
                return Scrub(Slot.Slip, _slipClock / CombatTuning.DodgeDuration);
            }

            _slipClock = 0f;
            return -1;
        }

        /// Sets a clip's time from a 0-1 progress and returns its index, or -1 if unassigned so the
        /// caller falls through to the stance. Holding the last frame beats snapping to T-pose.
        int Scrub(Slot slot, float progress)
        {
            int index = (int)slot;
            if (!_assigned[index])
            {
                return -1;
            }

            _playables[index].SetTime(Mathf.Clamp01(progress) * _lengths[index]);
            return index;
        }

        static Slot SlotForPunch(PunchKind kind)
        {
            switch (kind)
            {
                case PunchKind.Jab: return Slot.Jab;
                case PunchKind.Straight: return Slot.Straight;
                case PunchKind.Hook: return Slot.Hook;
                default: return Slot.Uppercut;
            }
        }

        void ApplyStanceAndFootwork(float baseWeight, float dt)
        {
            Vector2 move = Vector2.zero;
            if (Owner.Motor != null)
            {
                move = Owner.Motor.LocalMove / Mathf.Max(0.1f, Owner.Stats.MoveSpeed);
                move = Vector2.ClampMagnitude(move, 1f);
            }

            bool guarding = Owner.IsGuarding && !Owner.GuardBroken;
            _guardBlend = Mathf.MoveTowards(_guardBlend, guarding ? 1f : 0f, StanceBlendSpeed * dt);

            int step = ResolveStep(move);
            float effort = step >= 0
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StepDeadzone, StepFullSpeed, move.magnitude))
                : 0f;

            float stanceWeight = baseWeight * (1f - effort);
            int guard = _assigned[(int)Slot.Guard] ? (int)Slot.Guard : -1;
            int idle = _assigned[(int)Slot.Idle] ? (int)Slot.Idle : -1;

            if (guard >= 0 && idle >= 0)
            {
                Loop(guard, stanceWeight * _guardBlend);
                Loop(idle, stanceWeight * (1f - _guardBlend));
            }
            else if (guard >= 0)
            {
                Loop(guard, stanceWeight);
            }
            else if (idle >= 0)
            {
                Loop(idle, stanceWeight);
            }

            if (step >= 0)
            {
                Loop(step, baseWeight * effort);
            }
        }

        /// Picks the footwork clip for the dominant axis, falling back to whichever clips exist -
        /// one side-step clip is enough to read as footwork in every direction.
        int ResolveStep(Vector2 move)
        {
            if (move.magnitude < 0.05f)
            {
                return -1;
            }

            if (Mathf.Abs(move.y) >= Mathf.Abs(move.x))
            {
                return move.y >= 0f
                    ? FirstAssigned(Slot.StepForward, Slot.StepSide, Slot.StepBack)
                    : FirstAssigned(Slot.StepBack, Slot.StepSide, Slot.StepForward);
            }

            return FirstAssigned(Slot.StepSide, Slot.StepForward, Slot.StepBack);
        }

        int FirstAssigned(Slot a, Slot b, Slot c)
        {
            if (_assigned[(int)a]) { return (int)a; }
            if (_assigned[(int)b]) { return (int)b; }
            if (_assigned[(int)c]) { return (int)c; }
            return -1;
        }

        void Loop(int index, float weight)
        {
            if (index < 0 || weight <= 0.0001f)
            {
                return;
            }
            _weights[index] += weight;
        }

        static bool IsAction(int index)
        {
            return index >= (int)Slot.Jab;
        }

        void Commit(float deltaTime)
        {
            float total = 0f;

            for (int i = 0; i < _weights.Length; i++)
            {
                if (!_assigned[i])
                {
                    continue;
                }

                float speed = IsAction(i) ? ActionBlendSpeed : StanceBlendSpeed;
                _smoothed[i] = Mathf.MoveTowards(_smoothed[i], _weights[i], speed * deltaTime);
                total += _smoothed[i];
            }

            // Never leave the mixer empty: an unweighted Humanoid snaps to T-pose.
            if (total <= 0.0001f && _assigned[(int)Slot.Idle])
            {
                _smoothed[(int)Slot.Idle] = 1f;
            }

            for (int i = 0; i < _smoothed.Length; i++)
            {
                if (_assigned[i])
                {
                    _mixer.SetInputWeight(i, _smoothed[i]);
                }
            }
        }
    }
}

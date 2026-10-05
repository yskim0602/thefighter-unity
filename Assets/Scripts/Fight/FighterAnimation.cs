using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TheFighter
{
    /// One clip, plus which slice of it to use. Actions take a list of these so the same punch can
    /// come out of a different animation each time - a jab that always looks identical is the
    /// fastest way to make a fighter read as a machine.
    [System.Serializable]
    public class ClipVariant
    {
        public AnimationClip Clip;
        /// Normalised start and end. Mixamo clips are often longer than our punch, or are whole
        /// combos - "Body Jab Cross" is a left jab then a right cross, so 0 to 0.5 is the jab and
        /// 0.45 to 1 is the cross. Trim here when a punch looks frantic; the fight's timing does
        /// not change either way, only which frames you see.
        public Vector2 Window = new Vector2(0f, 0.7f);
    }

    /// The clips, in one place. Lives on BoxingBootstrap so both fighters share a single set you
    /// drag once - the scene is built from code, so a component added at runtime has nowhere to
    /// hold Inspector references of its own.
    [System.Serializable]
    public class BoxerClipSet
    {
        [Header("Stance (looping)")]
        public AnimationClip Idle;
        public AnimationClip Guard;

        [Header("Footwork (looping) - all four blend, so diagonals use two at once")]
        public AnimationClip StepForward;
        public AnimationClip StepBack;
        /// Which way the clip *looks* like it is going, not which file it came from. A southpaw set
        /// built by mirroring an orthodox one has its side steps swapped, so the mirrored
        /// step-left clip belongs in StepRight here.
        public AnimationClip StepLeft;
        public AnimationClip StepRight;

        [Header("Punches (add as many variants as you like - one is picked at random)")]
        public ClipVariant[] Jab = new ClipVariant[0];
        public ClipVariant[] Straight = new ClipVariant[0];
        public ClipVariant[] Hook = new ClipVariant[0];
        public ClipVariant[] Uppercut = new ClipVariant[0];

        [Header("Reactions")]
        public ClipVariant[] Slip = new ClipVariant[0];
        public ClipVariant[] Down = new ClipVariant[0];

        public ClipVariant[] PunchVariants(PunchKind kind)
        {
            switch (kind)
            {
                case PunchKind.Jab: return Jab;
                case PunchKind.Straight: return Straight;
                case PunchKind.Hook: return Hook;
                default: return Uppercut;
            }
        }
    }

    /// Plays Humanoid clips on a real model without an Animator Controller.
    ///
    /// Three reasons it is built this way:
    ///
    /// 1. **Code owns the timing.** Every clip's time is set explicitly each frame - punches from
    ///    Fighter.PunchProgress, stance and footwork from an accumulated loop clock. Mixamo clip
    ///    lengths have nothing to do with our windup/strike/recovery numbers, and if the clips
    ///    drove the timing then every range, counter window and scorecard weight would shift.
    ///    This way the animation follows the fight instead of dictating it, and no clip needs its
    ///    Loop Time or length touched on import.
    ///
    /// 2. **No state machine to hand-author.** A controller graph with a dozen states and
    ///    transitions is tedious to build in the editor and impossible to review as a diff. Drag
    ///    clips into the slots and that is the whole setup.
    ///
    /// 3. **It fails soft.** Empty slots are skipped, and if nothing is assigned or the graph
    ///    cannot be built, this component does nothing at all and the procedural capsule rig keeps
    ///    running. An animation bug must never cost us a playable fight.
    public class FighterAnimation : MonoBehaviour
    {
        public Fighter Owner;
        public Animator ModelAnimator;

        /// A southpaw's lead hand is the right one, so hit detection reads the right wrist bone
        /// while an orthodox clip throws the left arm - the animation and the hitbox end up on
        /// opposite sides of the body. Mirrored clips are not cosmetic; they are what keeps the
        /// punch landing where it looks like it lands. If the southpaw set is empty the orthodox
        /// one is used, and Bootstrap stops handing anyone a southpaw stance.
        public BoxerClipSet OrthodoxClips;
        public BoxerClipSet SouthpawClips;

        [Header("Hip height")]
        /// Mixamo clips disagree about how high the hips sit, because each one's Root Transform
        /// Position (Y) is measured from a different reference. Switching from idle to a punch then
        /// pops the whole body upward, which reads as the fighter hopping on every shot. The proper
        /// fix is per-clip import settings - Root Transform Position (Y), Bake Into Pose on, Based
        /// Upon Feet - but that is one clip at a time across every file, so this is the
        /// one-checkbox version. Measured against the model's own transform, so leaning, ducking
        /// and the knockdown pose (which all live above it) still work.
        [Range(0f, 1f)] public float HipHeightLock = 1f;

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

        enum Pose
        {
            Idle,
            Guard,
            StepForward,
            StepBack,
            StepLeft,
            StepRight,
            Count
        }

        class Entry
        {
            public AnimationClip Clip;
            public Vector2 Window;
            public bool Looping;
            public AnimationClipPlayable Playable;
            public float Length;
            public float Target;
            public float Weight;
        }

        BoxerClipSet _clips;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        readonly List<Entry> _entries = new List<Entry>();
        readonly int[] _poses = new int[(int)Pose.Count];
        readonly List<int>[] _punches = new List<int>[4];
        readonly List<int> _slip = new List<int>();
        readonly List<int> _down = new List<int>();
        bool _built;

        Transform _hips;
        float _hipHeight;
        bool _hipCaptured;

        float _baseClock;
        float _slipClock;
        float _downClock;
        float _guardBlend;

        PunchKind _lastKind;
        bool _punchWasActive;
        float _lastProgress;
        int _punchVariant = -1;
        int _slipVariant = -1;
        int _downVariant = -1;

        /// Picked once, because a stance is chosen before the bell and never changes mid-fight.
        public static BoxerClipSet Resolve(Stance stance, BoxerClipSet orthodox, BoxerClipSet southpaw)
        {
            bool southpawReady = southpaw != null && southpaw.Idle != null;
            return stance == Stance.Southpaw && southpawReady ? southpaw : orthodox;
        }

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

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        void Build()
        {
            if (ModelAnimator == null || Owner == null)
            {
                return;
            }

            _clips = Resolve(Owner.CurrentStance, OrthodoxClips, SouthpawClips);
            if (_clips == null)
            {
                return;
            }

            for (int i = 0; i < _poses.Length; i++)
            {
                _poses[i] = -1;
            }
            for (int i = 0; i < _punches.Length; i++)
            {
                _punches[i] = new List<int>();
            }

            _poses[(int)Pose.Idle] = AddLoop(_clips.Idle);
            _poses[(int)Pose.Guard] = AddLoop(_clips.Guard);
            _poses[(int)Pose.StepForward] = AddLoop(_clips.StepForward);
            _poses[(int)Pose.StepBack] = AddLoop(_clips.StepBack);
            _poses[(int)Pose.StepLeft] = AddLoop(_clips.StepLeft);
            _poses[(int)Pose.StepRight] = AddLoop(_clips.StepRight);

            for (int i = 0; i < PunchLibrary.All.Length; i++)
            {
                PunchKind kind = PunchLibrary.All[i];
                AddVariants(_clips.PunchVariants(kind), _punches[(int)kind]);
            }
            AddVariants(_clips.Slip, _slip);
            AddVariants(_clips.Down, _down);

            if (_entries.Count == 0)
            {
                return;
            }

            // Our motor owns position. Root motion on top of it would move everyone twice, which
            // also means nobody has to remember Mixamo's "In Place" checkbox.
            ModelAnimator.applyRootMotion = false;

            _graph = PlayableGraph.Create("FighterAnimation:" + Owner.FighterName);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Pose", ModelAnimator);
            _mixer = AnimationMixerPlayable.Create(_graph, _entries.Count, true);
            output.SetSourcePlayable(_mixer);

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, entry.Clip);
                playable.SetApplyFootIK(false);
                // Paused, because we scrub the time ourselves every frame.
                playable.Pause();

                _graph.Connect(playable, 0, _mixer, i);
                _mixer.SetInputWeight(i, 0f);

                entry.Playable = playable;
                entry.Length = Mathf.Max(0.01f, entry.Clip.length);
            }

            _graph.Play();
            _built = true;
        }

        int AddLoop(AnimationClip clip)
        {
            if (clip == null)
            {
                return -1;
            }

            Entry entry = new Entry();
            entry.Clip = clip;
            entry.Window = new Vector2(0f, 1f);
            entry.Looping = true;
            _entries.Add(entry);
            return _entries.Count - 1;
        }

        void AddVariants(ClipVariant[] variants, List<int> into)
        {
            if (variants == null)
            {
                return;
            }

            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null || variants[i].Clip == null)
                {
                    continue;
                }

                Entry entry = new Entry();
                entry.Clip = variants[i].Clip;
                entry.Window = variants[i].Window;
                entry.Looping = false;
                _entries.Add(entry);
                into.Add(_entries.Count - 1);
            }
        }

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        void LateUpdate()
        {
            if (!_built || Owner == null)
            {
                return;
            }

            StabiliseHipHeight();

            float dt = Time.deltaTime;
            _baseClock += dt;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                entry.Target = 0f;

                // Loops advance whatever their weight, so a clip fading out keeps moving instead
                // of freezing mid-fade. Half of what makes a blend look wrong is a frozen pose.
                if (entry.Looping)
                {
                    entry.Playable.SetTime(Mathf.Repeat(_baseClock, entry.Length));
                }
            }

            int action = ResolveAction(dt);
            if (action >= 0)
            {
                _entries[action].Target = 1f;
            }
            else
            {
                ApplyStanceAndFootwork(dt);
            }

            Commit(dt);
        }

        /// Returns the entry to play as a one-shot this frame, or -1 for none.
        int ResolveAction(float deltaTime)
        {
            if (Owner.State == ActionState.Down || Owner.State == ActionState.KnockedOut)
            {
                _downClock += deltaTime;
                int entry = Pick(_down, ref _downVariant, _downClock > deltaTime);
                return Scrub(entry, entry >= 0 ? _downClock / _entries[entry].Length : 0f);
            }

            _downClock = 0f;
            _downVariant = -1;

            PunchDefinition punch = Owner.ActivePunch;
            if (punch != null)
            {
                float progress = Owner.PunchProgress;

                // A fresh punch gets a fresh variant. Progress running backwards catches a combo
                // cancel, where one punch replaces another without passing through null.
                bool fresh = !_punchWasActive || _lastKind != punch.Kind || progress + 0.01f < _lastProgress;
                _punchWasActive = true;
                _lastKind = punch.Kind;
                _lastProgress = progress;

                int entry = Pick(_punches[(int)punch.Kind], ref _punchVariant, !fresh);
                return Scrub(entry, progress);
            }

            _punchWasActive = false;
            _punchVariant = -1;

            if (Owner.IsDodging)
            {
                _slipClock += deltaTime;
                int entry = Pick(_slip, ref _slipVariant, _slipClock > deltaTime);
                return Scrub(entry, _slipClock / CombatTuning.DodgeDuration);
            }

            _slipClock = 0f;
            _slipVariant = -1;
            return -1;
        }

        /// Holds onto the variant already in play, or rolls a new one.
        int Pick(List<int> options, ref int held, bool keep)
        {
            if (options.Count == 0)
            {
                held = -1;
                return -1;
            }

            if (keep && held >= 0 && options.Contains(held))
            {
                return held;
            }

            held = options[Random.Range(0, options.Count)];
            return held;
        }

        /// Maps a 0-1 action progress onto the entry's window of its clip. Holding the last frame
        /// beats snapping to T-pose, so an out-of-range progress just clamps.
        int Scrub(int index, float progress)
        {
            if (index < 0)
            {
                return -1;
            }

            Entry entry = _entries[index];
            float start = Mathf.Clamp01(Mathf.Min(entry.Window.x, entry.Window.y));
            float end = Mathf.Clamp01(Mathf.Max(entry.Window.x, entry.Window.y));

            entry.Playable.SetTime(Mathf.Lerp(start, end, Mathf.Clamp01(progress)) * entry.Length);
            return index;
        }

        void ApplyStanceAndFootwork(float deltaTime)
        {
            Vector2 move = Vector2.zero;
            if (Owner.Motor != null)
            {
                move = Owner.Motor.LocalMove / Mathf.Max(0.1f, Owner.Stats.MoveSpeed);
                move = Vector2.ClampMagnitude(move, 1f);
            }

            bool guarding = Owner.IsGuarding && !Owner.GuardBroken;
            _guardBlend = Mathf.MoveTowards(_guardBlend, guarding ? 1f : 0f, StanceBlendSpeed * deltaTime);

            float effort = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(StepDeadzone, StepFullSpeed, move.magnitude));

            // Movement is opponent-relative - x circles, y steps in and out - so each axis maps
            // straight onto one clip. Splitting the effort between them by magnitude means a
            // diagonal plays forward and sideways together instead of snapping to whichever axis
            // happens to be larger, which is the whole point of having four clips rather than two.
            float lateral = Mathf.Abs(move.x);
            float depth = Mathf.Abs(move.y);
            float sum = lateral + depth;

            float lateralShare = 0f;
            float depthShare = 0f;
            if (sum > 0.0001f)
            {
                lateralShare = effort * lateral / sum;
                depthShare = effort * depth / sum;
            }

            // A missing clip gives its share back to the stance rather than to the other axis:
            // playing a forward step for a sideways slide puts the feet somewhere the body is not.
            int lateralClip = move.x >= 0f
                ? Fallback(Pose.StepRight, Pose.StepLeft)
                : Fallback(Pose.StepLeft, Pose.StepRight);
            int depthClip = move.y >= 0f
                ? Fallback(Pose.StepForward, Pose.StepBack)
                : Fallback(Pose.StepBack, Pose.StepForward);

            if (lateralClip < 0) { lateralShare = 0f; }
            if (depthClip < 0) { depthShare = 0f; }

            float stanceWeight = Mathf.Max(0f, 1f - lateralShare - depthShare);
            int guard = _poses[(int)Pose.Guard];
            int idle = _poses[(int)Pose.Idle];

            if (guard >= 0 && idle >= 0)
            {
                Add(guard, stanceWeight * _guardBlend);
                Add(idle, stanceWeight * (1f - _guardBlend));
            }
            else if (guard >= 0)
            {
                Add(guard, stanceWeight);
            }
            else
            {
                Add(idle, stanceWeight);
            }

            Add(lateralClip, lateralShare);
            Add(depthClip, depthShare);
        }

        /// The opposite-direction clip is a deliberate last resort: wrong-footed footwork still
        /// reads as footwork, where frozen legs over a sliding body reads as a bug. Only reached
        /// while a slot is still empty.
        int Fallback(Pose wanted, Pose other)
        {
            if (_poses[(int)wanted] >= 0) { return _poses[(int)wanted]; }
            return _poses[(int)other];
        }

        void Add(int index, float weight)
        {
            if (index < 0 || weight <= 0.0001f)
            {
                return;
            }
            _entries[index].Target += weight;
        }

        void Commit(float deltaTime)
        {
            float total = 0f;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                float speed = entry.Looping ? StanceBlendSpeed : ActionBlendSpeed;
                entry.Weight = Mathf.MoveTowards(entry.Weight, entry.Target, speed * deltaTime);
                total += entry.Weight;
            }

            // Never leave the mixer empty: an unweighted Humanoid snaps to T-pose.
            int idle = _poses[(int)Pose.Idle];
            if (total <= 0.0001f && idle >= 0)
            {
                _entries[idle].Weight = 1f;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                _mixer.SetInputWeight(i, _entries[i].Weight);
            }
        }

        /// Pulls the hips back to the height they sat at on the first frame, cancelling the
        /// per-clip disagreement without touching anything the clips meant to do horizontally.
        void StabiliseHipHeight()
        {
            if (HipHeightLock <= 0.001f || ModelAnimator == null)
            {
                return;
            }

            if (_hips == null)
            {
                _hips = ModelAnimator.GetBoneTransform(HumanBodyBones.Hips);
                if (_hips == null)
                {
                    return;
                }
            }

            Transform reference = ModelAnimator.transform;
            Vector3 local = reference.InverseTransformPoint(_hips.position);

            if (!_hipCaptured)
            {
                _hipHeight = local.y;
                _hipCaptured = true;
                return;
            }

            local.y = Mathf.Lerp(local.y, _hipHeight, HipHeightLock);
            _hips.position = reference.TransformPoint(local);
        }
    }
}

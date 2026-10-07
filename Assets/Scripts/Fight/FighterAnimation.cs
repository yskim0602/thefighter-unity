using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TheFighter
{
    /// One clip, plus how to play it. Every slot takes a list of these, so a slot can hold several
    /// different motions - the same punch comes out of a different animation each time, and the two
    /// fighters pick different idles.
    [System.Serializable]
    public class ClipVariant
    {
        public AnimationClip Clip;

        /// Which slice of the clip to use, 0 to 1. **Leave both at 0 and it is worked out for
        /// you**: the slice that plays at life speed inside the time the action actually has.
        ///
        /// That is almost always what you want, and it used to mean "the whole clip" - which for
        /// a Mixamo take is two to five times speed, a blur that happens to contain a punch. The
        /// code already knew the right answer (it was printing it in the fit log for someone to
        /// retype), so now it applies it.
        ///
        /// Set it by hand when the clip's useful part is not at the beginning. "Body Jab Cross" is
        /// a left jab then a right cross, so Jab takes 0 to 0.5 and Straight 0.45 to 1 of the one
        /// file. Loops are left whole, since a loop has no duration to fit.
        public Vector2 Window = Vector2.zero;

        /// Playback rate, for looping slots only - an action's rate comes from the fight, since
        /// code owns punch timing. For footwork this scales a rate that already tracks how fast
        /// the fighter is actually travelling. 0 reads as 1.
        public float Speed = 1f;

        [Header("When this one is chosen")]
        /// Leave both at Any and this clip serves every situation - which is what you want with
        /// one clip in the slot. Tag them and it is preferred when they match.
        ///
        /// This is how the big fight games do reactions: gameplay produces a hit descriptor and
        /// the animation system queries for the closest clip, rather than there being one slot per
        /// situation. Splitting slots instead would mean zone x direction x force = two dozen
        /// Inspector fields, and adding a new axis would mean doubling them again.
        public HitDirection Direction = HitDirection.Any;
        public HitSeverity Force = HitSeverity.Any;

        /// Routes this clip to the full-body layer instead of the masked upper-body one.
        ///
        /// **Costs the crouch.** A full-body punch overrides the legs, so it cannot be thrown from
        /// a low stance without standing the fighter up - which is the thing this project treats
        /// as broken. Only worth it for a clip whose leg drive really is the punch (a deep
        /// uppercut off the back foot), and only after watching it from a crouch.
        public bool FullBody;
    }

    /// The clips, in one place. Lives on BoxingBootstrap so both fighters share a single set you
    /// drag once - the scene is built from code, so a component added at runtime has nowhere to
    /// hold Inspector references of its own.
    [System.Serializable]
    public class BoxerClipSet
    {
        [Header("Stance (looping)")]
        /// The one slot that must not be empty: an unweighted Humanoid snaps to T-pose.
        public ClipVariant[] Idle = new ClipVariant[0];
        /// The ordinary high guard, hands at the cheeks. Leave empty to let ArmPose raise the
        /// gloves with IK instead.
        public ClipVariant[] Guard = new ClipVariant[0];
        /// Covering up against hooks - tight to the temples, elbows up. A high guard does nothing
        /// about a hook, which comes around it rather than through it, so this is a different
        /// pose rather than the same one held harder. Empty falls back to Guard, and ArmPose
        /// reshapes the arms either way.
        public ClipVariant[] GuardHook = new ClipVariant[0];
        /// Elbows dropped onto the ribs against body work. Empty falls back to Guard.
        public ClipVariant[] GuardBody = new ClipVariant[0];

        [Header("Footwork (looping) - all four blend, so diagonals use two at once")]
        public ClipVariant[] StepForward = new ClipVariant[0];
        public ClipVariant[] StepBack = new ClipVariant[0];
        /// Which way the clip *looks* like it is going, not which file it came from. A southpaw set
        /// built by mirroring an orthodox one has its side steps swapped, so the mirrored
        /// step-left clip belongs in StepRight here.
        public ClipVariant[] StepLeft = new ClipVariant[0];
        public ClipVariant[] StepRight = new ClipVariant[0];

        [Header("Punches - one variant is picked at random per punch")]
        public ClipVariant[] Jab = new ClipVariant[0];
        public ClipVariant[] Straight = new ClipVariant[0];
        public ClipVariant[] Hook = new ClipVariant[0];
        public ClipVariant[] Uppercut = new ClipVariant[0];

        [Header("Reactions")]
        public ClipVariant[] Slip = new ClipVariant[0];
        /// Taking one clean. Driven by FighterRig's recoil clock, so a clip and the procedural
        /// head snap stay on one timeline instead of each running a reaction of its own. Empty
        /// leaves the procedural reaction doing the whole job, as now.
        public ClipVariant[] HitHead = new ClipVariant[0];
        public ClipVariant[] HitBody = new ClipVariant[0];
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

        public static bool HasAny(ClipVariant[] variants)
        {
            if (variants == null)
            {
                return false;
            }
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].Clip != null)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// Plays Humanoid clips on a real model without an Animator Controller.
    ///
    /// **Three layers, not one clip.** This is the difference between a fighter and a mannequin
    /// playing animations. A boxer's feet never stop: he steps while he jabs, and the jab is his
    /// arms and torso over footwork that is still happening. Playing one clip over the whole body
    /// means every punch freezes the legs mid-stride and the fighter slides across the canvas on
    /// still feet - which no amount of better clips can fix, because the problem is that the legs
    /// are being told to stop.
    ///
    ///   layer 0  stance and footwork, full body, always running
    ///   layer 1  punches and slips, masked to the upper body - legs keep working underneath
    ///   layer 2  knockdowns, full body, overrides everything
    ///
    /// The mask is built in code, so there is no .mask asset to author in the editor.
    ///
    /// Beyond that:
    ///
    /// - **Code owns the timing.** Clip times are set explicitly each frame - punches from
    ///   Fighter.PunchProgress, footwork from distance actually travelled. Mixamo clip lengths
    ///   have nothing to do with our windup/strike/recovery numbers, and if the clips drove the
    ///   timing then every range, counter window and scorecard weight would shift. No clip needs
    ///   its Loop Time or length touched on import.
    ///
    /// - **No state machine to hand-author.** Drag clips into the slots and that is the setup.
    ///
    /// - **It fails soft.** Empty slots are skipped, and if nothing is assigned or the graph
    ///   cannot be built this component does nothing and the procedural capsule rig keeps running.
    ///   An animation bug must never cost us a playable fight.
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

        [Header("Upper-body layer")]
        /// Off, punches play over the whole body and the legs stop dead on every shot. Only worth
        /// turning off to see the difference.
        public bool MaskPunchesToUpperBody = true;
        /// The torso is left to the punch so the shot carries hip rotation - most of what makes a
        /// punch look like it has weight behind it. The cost is that the punch clip also drives
        /// hip *height*, which is what HipHeightLock then cancels.
        public bool PunchDrivesTorso = true;

        [Header("Footwork")]
        /// Footwork clips advance with the ground actually covered rather than on their own timer.
        /// A stride cycle running at a fixed rate under a body moving at a different speed is
        /// exactly what reads as ice-skating, and no clip is immune to it.
        public bool SyncStrideToSpeed = true;
        /// Playback rate at a standstill and at full speed. Never zero: feet that stop completely
        /// look broken, and a boxer is always shifting weight.
        public float StrideRateIdle = 0.55f;
        public float StrideRateFull = 1.35f;

        [Header("Hit reactions")]
        /// How long a hit clip is given, before severity scales it.
        ///
        /// Not the rig's recoil: that is 0.18-0.26s, tuned for a procedural head snap, and a
        /// Mixamo react clip is well over a second. Playing one on that clock runs it at six times
        /// speed, which reads as a twitch rather than as being hit. So the clip gets a clock of
        /// its own and the two run side by side at their own rates.
        public float HitReactionSeconds = 0.45f;
        /// A heavy shot gets longer, which is where most of the difference between eating a jab
        /// and eating a right hand comes from.
        public float HeavyHitScale = 1.35f;
        /// How much of the procedural recoil survives once hit clips are doing the work. Low, but
        /// not zero: the clip carries the reaction and this keeps the first frame sharp.
        [Range(0f, 1f)] public float ProceduralRecoilWithClips = 0.4f;

        [Header("Diagnostics")]
        /// Logs, once per fighter, how much clip each punch is being asked to show in the time it
        /// actually has. A three-second Mixamo take played across a 0.38s jab runs at 8x, and no
        /// amount of tuning elsewhere makes that read as a punch - it reads as a blur that happens
        /// to contain one. This says which clips to trim and roughly where to trim them to.
        public bool LogClipFit = true;

        [Header("Hip height")]
        /// Mixamo clips disagree about how high the hips sit, because each one's Root Transform
        /// Position (Y) is measured from a different reference. Blending from idle into a punch
        /// then pops the whole body upward, which reads as the fighter hopping on every shot.
        ///
        /// So the hips are pinned to the height the stance puts them at - but **only while an
        /// action is playing**, scaled by that action's weight. Pinning them the whole time would
        /// also cancel the vertical bob inside the idle and step cycles, and that bob is most of
        /// what makes a stance look alive and a step look like weight transferring. The reference
        /// height is learned from the stance rather than sampled on frame one.
        ///
        /// Measured against the model's own transform, so leaning, ducking and the knockdown pose
        /// (which all live above it) still work.
        [Range(0f, 1f)] public float HipHeightLock = 1f;

        [Header("Blending")]
        /// Weights are damped rather than set outright. Without this the stance/footwork mix
        /// follows the AI's frame-to-frame jitter and the skeleton visibly shivers.
        public float StanceBlendSpeed = 9f;
        /// Footwork gets its own, faster, because a step is an event rather than a mood. At the
        /// stance speed a short press spends its whole duration blending in and never arrives,
        /// which looks exactly like not stepping at all.
        public float FootworkBlendSpeed = 20f;
        /// Punches have to land on their frame, so they are not damped nearly as much.
        public float ActionBlendSpeed = 36f;
        /// Below this much movement it is pure stance, above the next it is pure footwork. The gap
        /// between them is deliberately narrow: a long cross-fade averages two unrelated poses and
        /// that average is what reads as boneless.
        public float StepDeadzone = 0.08f;
        public float StepFullSpeed = 0.38f;

        enum Pose
        {
            Idle,
            Guard,
            GuardBody,
            StepForward,
            StepBack,
            StepLeft,
            StepRight,
            Count
        }

        enum Layer
        {
            /// Stance and footwork. Full body, never stops.
            Base = 0,
            /// Punches and slips. Masked to the upper body.
            Upper = 1,
            /// Knockdowns. Full body, over the top.
            Full = 2
        }

        class Entry
        {
            public AnimationClip Clip;
            public Layer Layer;
            public HitDirection Direction;
            public HitSeverity Force;
            /// Whether the window was worked out rather than typed, so the log can say which.
            public bool AutoWindow;
            public float Start;
            public float End;
            public float Speed;
            public bool Looping;
            /// Footwork, as opposed to the stance loops - only these follow the ground covered.
            public bool Striding;
            public AnimationClipPlayable Playable;
            public int Port;
            public float Length;
            public float Target;
            public float Weight;
        }

        BoxerClipSet _clips;
        PlayableGraph _graph;
        AnimationLayerMixerPlayable _layers;
        readonly AnimationMixerPlayable[] _mixers = new AnimationMixerPlayable[3];
        readonly List<Entry> _entries = new List<Entry>();
        readonly int[] _poses = new int[(int)Pose.Count];
        readonly List<int>[] _punches = new List<int>[4];
        readonly List<int> _guardHook = new List<int>();
        readonly List<int> _slip = new List<int>();
        readonly List<int> _hitHead = new List<int>();
        readonly List<int> _hitBody = new List<int>();
        readonly List<int> _down = new List<int>();
        AvatarMask _upperMask;
        bool _built;

        /// ArmPose reads this and stands down: if a guard clip exists, IK gloves fighting an
        /// animated guard gives you neither.
        public bool GuardClipAssigned { get; private set; }

        // What the footwork layer decided this frame. "He sometimes just does not step" is a
        // sentence with at least four causes behind it - no clip, a weight that never arrived, a
        // diagonal splitting one step across two clips, or a stride rate near zero - and they are
        // indistinguishable by eye. PostureProbe reads these.
        public float FootworkInput { get; private set; }
        public float FootworkEffort { get; private set; }
        public float StrideRate { get { return _strideRate; } }
        public string ActiveStep { get; private set; }
        public float ActiveStepWeight { get; private set; }

        Transform _hips;
        float _hipHeight;
        bool _hipCaptured;

        float _stanceClock;
        float _strideClock;
        float _slipClock;
        float _downClock;
        float _guardBlend;
        HitDirection _lastGuardSide = HitDirection.Any;
        float _upperWeight;
        float _fullWeight;

        PunchKind _lastKind;
        bool _punchWasActive;
        float _lastProgress;
        int _punchVariant = -1;
        int _guardHookVariant = -1;
        bool _hitActive;
        float _hitClock;
        float _lastRecoil = -1f;
        HitZone _hitZone;
        int _slipVariant = -1;
        int _hitVariant = -1;
        int _downVariant = -1;

        /// Picked once, because a stance is chosen before the bell and never changes mid-fight.
        public static BoxerClipSet Resolve(Stance stance, BoxerClipSet orthodox, BoxerClipSet southpaw)
        {
            bool southpawReady = southpaw != null && BoxerClipSet.HasAny(southpaw.Idle);
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
            if (_upperMask != null)
            {
                Destroy(_upperMask);
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

            // A loop keeps one variant for the whole fight - swapping idles mid-round would pop.
            // Rolling it per fighter instead means the two boxers often stand differently, which
            // is free characterisation.
            _poses[(int)Pose.Idle] = AddLoop(_clips.Idle, false);
            _poses[(int)Pose.Guard] = AddLoop(_clips.Guard, false);
            // Every variant kept, not one rolled: which ear is covered has to follow the punch,
            // so this one is chosen per frame rather than per fight.
            AddVariants(_clips.GuardHook, _guardHook, Layer.Base);
            for (int i = 0; i < _guardHook.Count; i++)
            {
                _entries[_guardHook[i]].Looping = true;
            }
            _poses[(int)Pose.GuardBody] = AddLoop(_clips.GuardBody, false);
            _poses[(int)Pose.StepForward] = AddLoop(_clips.StepForward, true);
            _poses[(int)Pose.StepBack] = AddLoop(_clips.StepBack, true);
            _poses[(int)Pose.StepLeft] = AddLoop(_clips.StepLeft, true);
            _poses[(int)Pose.StepRight] = AddLoop(_clips.StepRight, true);

            GuardClipAssigned = _poses[(int)Pose.Guard] >= 0;

            for (int i = 0; i < PunchLibrary.All.Length; i++)
            {
                PunchKind kind = PunchLibrary.All[i];
                AddVariants(_clips.PunchVariants(kind), _punches[(int)kind], Layer.Upper,
                    PunchLibrary.Get(kind).TotalTime, false);
            }
            AddVariants(_clips.Slip, _slip, Layer.Upper, CombatTuning.DodgeDuration, false);
            AddVariants(_clips.HitHead, _hitHead, Layer.Upper, HitReactionSeconds, true);
            AddVariants(_clips.HitBody, _hitBody, Layer.Upper, HitReactionSeconds, true);
            // A knockdown runs on the clip's own length, so there is nothing to fit it to.
            AddVariants(_clips.Down, _down, Layer.Full);

            if (_entries.Count == 0)
            {
                return;
            }

            // Our motor owns position. Root motion on top of it would move everyone twice, which
            // also means nobody has to remember Mixamo's "In Place" checkbox.
            ModelAnimator.applyRootMotion = false;

            _graph = PlayableGraph.Create("FighterAnimation:" + Owner.FighterName);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Pose", ModelAnimator);

            _layers = AnimationLayerMixerPlayable.Create(_graph, _mixers.Length);
            output.SetSourcePlayable(_layers);

            for (int layer = 0; layer < _mixers.Length; layer++)
            {
                int count = CountOn((Layer)layer);
                _mixers[layer] = AnimationMixerPlayable.Create(_graph, Mathf.Max(1, count), true);
                _graph.Connect(_mixers[layer], 0, _layers, layer);
                _layers.SetLayerAdditive((uint)layer, false);
                _layers.SetInputWeight(layer, layer == 0 ? 1f : 0f);
            }

            if (MaskPunchesToUpperBody)
            {
                _upperMask = BuildUpperBodyMask();
                _layers.SetLayerMaskFromAvatarMask((uint)Layer.Upper, _upperMask);
            }

            int[] ports = new int[_mixers.Length];
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, entry.Clip);
                playable.SetApplyFootIK(false);
                // Paused, because we scrub the time ourselves every frame.
                playable.Pause();

                int layer = (int)entry.Layer;
                entry.Port = ports[layer]++;
                _graph.Connect(playable, 0, _mixers[layer], entry.Port);
                _mixers[layer].SetInputWeight(entry.Port, 0f);

                entry.Playable = playable;
            }

            _graph.Play();
            _built = true;

            // One reaction, not two. With clips in the slots the procedural snap becomes a sharp
            // first frame on top rather than a second, differently-timed reaction underneath.
            if (Owner.Rig != null && (_hitHead.Count > 0 || _hitBody.Count > 0))
            {
                Owner.Rig.RecoilWeight = Mathf.Clamp01(ProceduralRecoilWithClips);
            }

            if (LogClipFit)
            {
                ReportClipFit();
            }
        }

        /// A punch's window has to be shown inside the punch's own duration, and the ratio between
        /// them is the playback rate. Past about 2.5x the motion stops being readable, so this
        /// prints the rate and the window that would bring it back to 1x.
        void ReportClipFit()
        {
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.Append("FighterAnimation clip fit for ").Append(Owner.FighterName).Append(':');
            bool anyTight = false;

            for (int i = 0; i < PunchLibrary.All.Length; i++)
            {
                PunchKind kind = PunchLibrary.All[i];
                PunchDefinition punch = PunchLibrary.Get(kind);
                List<int> variants = _punches[(int)kind];

                if (variants.Count == 0)
                {
                    report.Append("\n  ").Append(punch.DisplayName).Append("  (no clip)");
                    continue;
                }

                for (int v = 0; v < variants.Count; v++)
                {
                    anyTight |= Line(report, punch.DisplayName, _entries[variants[v]],
                        Mathf.Max(0.01f, punch.TotalTime));
                }
            }

            anyTight |= ReportAction(report, "HIT HEAD", _hitHead,
                HitReactionSeconds * HeavyHitScale);
            anyTight |= ReportAction(report, "HIT BODY", _hitBody,
                HitReactionSeconds * HeavyHitScale);
            anyTight |= ReportAction(report, "SLIP", _slip, CombatTuning.DodgeDuration);
            anyTight |= ReportFootwork(report);

            if (anyTight)
            {
                report.Append("\n  Every flagged clip has a Window typed into it. Clearing that "
                    + "Window to 0,0 lets it be fitted automatically, which is what (auto) lines "
                    + "already are. TOO FAST is a blur; TOO SLOW is slow motion, which reads as "
                    + "the punch having no weight. Either way only which frames you see changes, "
                    + "never the fight's timing.");
                Debug.LogWarning(report.ToString());
            }
            else
            {
                Debug.Log(report.ToString());
            }
        }

        int CountOn(Layer layer)
        {
            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Layer == layer) { count++; }
            }
            return count;
        }

        /// The mask that lets the legs carry on. Built in code rather than as a .mask asset, so
        /// there is nothing to author in the editor and nothing to forget to assign.
        AvatarMask BuildUpperBodyMask()
        {
            AvatarMask mask = new AvatarMask();

            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            }

            // The legs and the root belong to the base layer. That is the whole point: footwork
            // keeps running under the punch, and position stays with the motor.
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);

            if (!PunchDrivesTorso)
            {
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
            }

            return mask;
        }

        /// Adds one randomly chosen variant from a looping slot, and returns its index.
        int AddLoop(ClipVariant[] variants, bool striding)
        {
            List<ClipVariant> usable = new List<ClipVariant>();
            if (variants != null)
            {
                for (int i = 0; i < variants.Length; i++)
                {
                    if (variants[i] != null && variants[i].Clip != null)
                    {
                        usable.Add(variants[i]);
                    }
                }
            }

            if (usable.Count == 0)
            {
                return -1;
            }

            // Collected first so an empty slot in the middle of the array does not skew the roll
            // toward whatever follows it.
            int index = Append(usable[Random.Range(0, usable.Count)], Layer.Base, true);
            _entries[index].Striding = striding;
            return index;
        }

        /// fitDuration is how long the action gets, so an untouched window can be sized to it.
        /// 0 means there is nothing to fit against - a loop.
        void AddVariants(ClipVariant[] variants, List<int> into, Layer layer,
            float fitDuration, bool scaleByForce)
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

                // Down is already full body; a punch gets there only by opting in.
                Layer actual = variants[i].FullBody ? Layer.Full : layer;
                into.Add(Append(variants[i], actual, false, fitDuration, scaleByForce));
            }
        }

        void AddVariants(ClipVariant[] variants, List<int> into, Layer layer)
        {
            AddVariants(variants, into, layer, 0f, false);
        }

        int Append(ClipVariant variant, Layer layer, bool looping)
        {
            return Append(variant, layer, looping, 0f, false);
        }

        int Append(ClipVariant variant, Layer layer, bool looping,
            float fitDuration, bool scaleByForce)
        {
            Entry entry = new Entry();
            entry.Clip = variant.Clip;
            entry.Layer = layer;
            entry.Looping = looping;
            entry.Length = Mathf.Max(0.01f, variant.Clip.length);
            // Tagged by hand if you tagged it; read off the file name if you did not. Mixamo
            // already says it - "Standing React Large From Left" is a direction and a severity in
            // the name - and asking somebody to retype that into two dropdowns per clip, for
            // dozens of clips, is how a system this size stops being used. An explicit tag always
            // wins, so this only ever fills in blanks.
            entry.Direction = variant.Direction != HitDirection.Any
                ? variant.Direction : DirectionFromName(entry.Clip.name);
            entry.Force = variant.Force != HitSeverity.Any
                ? variant.Force : ForceFromName(entry.Clip.name);

            // Unity fills a freshly grown array element with zeroes, so an untouched window is a
            // degenerate one. That used to mean "the whole clip"; it now means "fit it".
            float a = Mathf.Clamp01(Mathf.Min(variant.Window.x, variant.Window.y));
            float b = Mathf.Clamp01(Mathf.Max(variant.Window.x, variant.Window.y));

            if (b - a < 0.01f)
            {
                a = 0f;
                float have = fitDuration
                    * (scaleByForce && entry.Force == HitSeverity.Heavy ? HeavyHitScale : 1f);
                b = have > 0f ? Mathf.Clamp(have / entry.Length, 0.05f, 1f) : 1f;
                entry.AutoWindow = true;
            }

            entry.Start = a;
            entry.End = b;
            entry.Speed = variant.Speed <= 0.0001f ? 1f : variant.Speed;

            _entries.Add(entry);
            return _entries.Count - 1;
        }

        static HitDirection DirectionFromName(string name)
        {
            string lower = name.ToLowerInvariant();
            // Checked before "back", since "backward" contains it and a few Mixamo names use both.
            if (lower.Contains("left")) { return HitDirection.Left; }
            if (lower.Contains("right")) { return HitDirection.Right; }
            if (lower.Contains("front") || lower.Contains("forward")) { return HitDirection.Front; }
            if (lower.Contains("back") || lower.Contains("behind")) { return HitDirection.Back; }
            return HitDirection.Any;
        }

        static HitSeverity ForceFromName(string name)
        {
            string lower = name.ToLowerInvariant();

            // Our words first, Mixamo's second, and that order is the whole point. Mixamo ships
            // "Standing React Large From Left" and a set downloaded entirely from the Large
            // variants carries that word in every file - so checking it first read every clip as
            // Heavy, including the ones renamed to say Light. The word someone typed on purpose
            // has to beat the word that came with the download.
            if (lower.Contains("heavy")) { return HitSeverity.Heavy; }
            if (lower.Contains("light")) { return HitSeverity.Light; }
            if (lower.Contains("large")) { return HitSeverity.Heavy; }
            if (lower.Contains("small")) { return HitSeverity.Light; }
            return HitSeverity.Any;
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

            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].Target = 0f;
            }

            // The base layer runs every frame, punch or no punch. That is what keeps the feet
            // alive underneath a shot instead of parking them for its duration.
            ApplyStanceAndFootwork(dt);
            AdvanceLoops(dt);

            int upper = ResolveUpperAction(dt);
            int full = ResolveFullAction(dt);

            if (upper >= 0) { _entries[upper].Target = 1f; }
            if (full >= 0) { _entries[full].Target = 1f; }

            // A punch that opted into FullBody lands on the full layer, so the layer a frame's
            // action belongs to is read off the entry rather than assumed from which resolver
            // produced it.
            bool upperActive = upper >= 0 && _entries[upper].Layer == Layer.Upper;
            bool fullActive = full >= 0 || (upper >= 0 && _entries[upper].Layer == Layer.Full);

            _upperWeight = Mathf.MoveTowards(_upperWeight, upperActive ? 1f : 0f,
                ActionBlendSpeed * dt);
            _fullWeight = Mathf.MoveTowards(_fullWeight, fullActive ? 1f : 0f,
                ActionBlendSpeed * dt);

            Commit(dt);
        }

        /// Loops advance whatever their weight, so a clip fading out keeps moving instead of
        /// freezing mid-fade - half of what makes a blend look wrong is a frozen pose.
        void AdvanceLoops(float deltaTime)
        {
            _stanceClock += deltaTime;
            _strideClock += deltaTime * _strideRate;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (!entry.Looping)
                {
                    continue;
                }

                float span = (entry.End - entry.Start) * entry.Length;
                if (span <= 0.0001f)
                {
                    entry.Playable.SetTime(entry.Start * entry.Length);
                    continue;
                }

                float clock = entry.Striding ? _strideClock : _stanceClock;
                entry.Playable.SetTime(entry.Start * entry.Length
                    + Mathf.Repeat(clock * entry.Speed, span));
            }
        }

        float _strideRate = 1f;

        // ------------------------------------------------------------------

        /// Punches and slips: the upper-body layer.
        int ResolveUpperAction(float deltaTime)
        {
            if (Owner.State == ActionState.Down || Owner.State == ActionState.KnockedOut)
            {
                _punchWasActive = false;
                _punchVariant = -1;
                _slipClock = 0f;
                _slipVariant = -1;
                return -1;
            }

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

            // Taking one. The rig's recoil only says *that* a hit landed and which zone; the
            // clip then runs on its own clock, because the two want very different durations.
            if (Owner.Rig != null)
            {
                float recoil = Owner.Rig.RecoilProgress;

                // A new hit is the recoil appearing, or restarting while one is already running.
                bool fresh = recoil >= 0f && (_lastRecoil < 0f || recoil < _lastRecoil);
                _lastRecoil = recoil;

                if (fresh)
                {
                    _hitActive = true;
                    _hitClock = 0f;
                    _hitVariant = -1;
                    _hitZone = Owner.Rig.RecoilZone;
                }

                if (_hitActive)
                {
                    _hitClock += deltaTime;
                    float duration = HitReactionSeconds
                        * (Owner.LastHitSeverity == HitSeverity.Heavy ? HeavyHitScale : 1f);

                    if (_hitClock >= duration)
                    {
                        _hitActive = false;
                        _hitVariant = -1;
                    }
                    else
                    {
                        List<int> pool = _hitZone == HitZone.Head ? _hitHead : _hitBody;
                        int hit = Pick(pool, ref _hitVariant, _hitClock > deltaTime,
                            Owner.LastHitDirection, Owner.LastHitSeverity);
                        if (hit >= 0)
                        {
                            return Scrub(hit, _hitClock / duration);
                        }

                        // No clip for this zone - let the procedural reaction have it alone.
                        _hitActive = false;
                    }
                }
            }

            if (Owner.IsDodging)
            {
                _slipClock += deltaTime;
                HitDirection way = Owner.LeanAmount < -0.1f ? HitDirection.Left
                    : Owner.LeanAmount > 0.1f ? HitDirection.Right : HitDirection.Any;
                int entry = Pick(_slip, ref _slipVariant, _slipClock > deltaTime,
                    way, HitSeverity.Any);
                return Scrub(entry, _slipClock / CombatTuning.DodgeDuration);
            }

            _slipClock = 0f;
            _slipVariant = -1;
            return -1;
        }

        /// Knockdowns: full body, over the top of everything.
        int ResolveFullAction(float deltaTime)
        {
            if (Owner.State != ActionState.Down && Owner.State != ActionState.KnockedOut)
            {
                _downClock = 0f;
                _downVariant = -1;
                return -1;
            }

            _downClock += deltaTime;
            int entry = Pick(_down, ref _downVariant, _downClock > deltaTime,
                Owner.LastHitDirection, HitSeverity.Any);
            return Scrub(entry, entry >= 0 ? _downClock / _entries[entry].Length : 0f);
        }

        /// Holds onto the variant already in play, or rolls a new one.
        int Pick(List<int> options, ref int held, bool keep)
        {
            return Pick(options, ref held, keep, HitDirection.Any, HitSeverity.Any);
        }

        /// Picks the clip that best fits the situation, rather than any clip in the slot.
        ///
        /// Scoring rather than filtering, so the slot degrades instead of going empty: a clip
        /// tagged for exactly this direction and force beats one tagged for the direction alone,
        /// which beats an untagged one - but an untagged one is still used when nothing specific
        /// exists. A clip tagged for a *different* direction is never used for this one, since a
        /// fighter snapping right from a punch that came from the right is worse than no reaction.
        /// Ties are broken at random, which is where variety comes from.
        int Pick(List<int> options, ref int held, bool keep,
            HitDirection direction, HitSeverity force)
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

            int best = -1;
            int bestScore = -1;
            int tied = 0;

            for (int i = 0; i < options.Count; i++)
            {
                Entry entry = _entries[options[i]];
                int score = Score(entry, direction, force);
                if (score < 0)
                {
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = options[i];
                    tied = 1;
                }
                else if (score == bestScore)
                {
                    // Reservoir sampling, so an even spread over however many tie without
                    // collecting them into a list every time a punch lands.
                    tied++;
                    if (Random.Range(0, tied) == 0)
                    {
                        best = options[i];
                    }
                }
            }

            // Nothing matched even as a wildcard - better something than a T-pose.
            if (best < 0)
            {
                best = options[Random.Range(0, options.Count)];
            }

            held = best;
            return best;
        }

        static int Score(Entry entry, HitDirection direction, HitSeverity force)
        {
            int score = 0;

            if (entry.Direction != HitDirection.Any)
            {
                if (direction != HitDirection.Any && entry.Direction != direction)
                {
                    return -1;
                }
                score += entry.Direction == direction ? 2 : 0;
            }

            if (entry.Force != HitSeverity.Any)
            {
                if (force != HitSeverity.Any && entry.Force != force)
                {
                    return -1;
                }
                score += entry.Force == force ? 2 : 0;
            }

            return score;
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
            entry.Playable.SetTime(Mathf.Lerp(entry.Start, entry.End, Mathf.Clamp01(progress)) * entry.Length);
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

            // Stride rate from ground actually covered. A cycle running on its own timer under a
            // body travelling at a different speed is what reads as ice-skating, whatever the clip.
            _strideRate = SyncStrideToSpeed
                ? Mathf.Lerp(StrideRateIdle, StrideRateFull, move.magnitude)
                : 1f;

            bool guarding = Owner.IsGuarding && !Owner.GuardBroken;
            _guardBlend = Mathf.MoveTowards(_guardBlend, guarding ? 1f : 0f, StanceBlendSpeed * deltaTime);

            float effort = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(StepDeadzone, StepFullSpeed, move.magnitude));

            FootworkInput = move.magnitude;
            FootworkEffort = effort;

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

            int lateralClip = move.x >= 0f
                ? Fallback(Pose.StepRight, Pose.StepLeft)
                : Fallback(Pose.StepLeft, Pose.StepRight);
            int depthClip = move.y >= 0f
                ? Fallback(Pose.StepForward, Pose.StepBack)
                : Fallback(Pose.StepBack, Pose.StepForward);

            // A missing clip gives its share back to the stance rather than to the other axis:
            // playing a forward step for a sideways slide puts the feet somewhere the body is not.
            if (lateralClip < 0) { lateralShare = 0f; }
            if (depthClip < 0) { depthShare = 0f; }

            float stanceWeight = Mathf.Max(0f, 1f - lateralShare - depthShare);
            int guard = GuardPoseFor();
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

            // Whichever of the two is carrying more of the step, for the readout.
            int shown = depthShare >= lateralShare ? depthClip : lateralClip;
            ActiveStep = shown >= 0 ? _entries[shown].Clip.name : "(none)";
            ActiveStepWeight = shown >= 0 ? _entries[shown].Weight : 0f;
        }

        /// Which guard clip the situation calls for, falling back to the plain one. Reads the
        /// incoming punch exactly as ArmPose does, so the clip and the IK ask for the same shape
        /// rather than pulling the arms in two directions.
        int GuardPoseFor()
        {
            Fighter foe = Owner.Opponent;
            PunchDefinition incoming = foe != null ? foe.ActivePunch : null;
            int plain = _poses[(int)Pose.Guard];

            if (incoming == null)
            {
                return plain;
            }

            if (incoming.Kind == PunchKind.Hook && _guardHook.Count > 0)
            {
                // Cover the ear it is coming at. A left hook from him arrives on my right, which
                // is the same mirror ArmPose uses, so clip and IK ask for one shape.
                bool fromMyLeft = foe.Rig != null && !foe.Rig.IsLeftHand(foe.ActiveHand);
                HitDirection side = fromMyLeft ? HitDirection.Left : HitDirection.Right;

                // Held while this punch is in the air, so the cover does not re-roll mid-hook.
                bool keep = _guardHookVariant >= 0 && _lastGuardSide == side;
                _lastGuardSide = side;
                return Pick(_guardHook, ref _guardHookVariant, keep, side, HitSeverity.Any);
            }

            bool low = foe.AimHeight < 0.5f || incoming.Kind == PunchKind.Uppercut;
            if (low && _poses[(int)Pose.GuardBody] >= 0)
            {
                return _poses[(int)Pose.GuardBody];
            }

            return plain;
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
            float baseTotal = 0f;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                float speed = entry.Looping
                    ? (entry.Striding ? FootworkBlendSpeed : StanceBlendSpeed)
                    : ActionBlendSpeed;
                entry.Weight = Mathf.MoveTowards(entry.Weight, entry.Target, speed * deltaTime);

                if (entry.Layer == Layer.Base)
                {
                    baseTotal += entry.Weight;
                }
            }

            // Never leave the base layer empty: an unweighted Humanoid snaps to T-pose.
            int idle = _poses[(int)Pose.Idle];
            if (baseTotal <= 0.0001f && idle >= 0)
            {
                _entries[idle].Weight = 1f;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                _mixers[(int)entry.Layer].SetInputWeight(entry.Port, entry.Weight);
            }

            _layers.SetInputWeight((int)Layer.Base, 1f);
            _layers.SetInputWeight((int)Layer.Upper, _upperWeight);
            _layers.SetInputWeight((int)Layer.Full, _fullWeight);
        }

        /// Which footwork slots are empty, and said plainly - because the symptom does not look
        /// like a missing clip. A fighter walking with no step clip does not T-pose or error; he
        /// stands in his idle and slides, which reads as "the animation is broken" rather than as
        /// "there is no clip for walking forward".
        bool ReportFootwork(System.Text.StringBuilder report)
        {
            string[] names = { "STEP FWD", "STEP BACK", "STEP LEFT", "STEP RIGHT" };
            Pose[] slots = { Pose.StepForward, Pose.StepBack, Pose.StepLeft, Pose.StepRight };
            bool missing = false;

            for (int i = 0; i < slots.Length; i++)
            {
                int index = _poses[(int)slots[i]];
                if (index < 0)
                {
                    missing = true;
                    report.Append("\n  ").Append(names[i])
                        .Append("  (no clip - he will slide without stepping in this direction)");
                    continue;
                }

                report.Append("\n  ").Append(names[i])
                    .Append("  ").Append(_entries[index].Clip.name);
            }

            return missing;
        }

        /// Same fit check for the reaction slots, which have their own durations - and which is
        /// where the problem usually is, since a react clip is seconds long and a reaction is not.
        bool ReportAction(System.Text.StringBuilder report, string name, List<int> pool,
            float baseDuration)
        {
            if (pool.Count == 0)
            {
                report.Append("\n  ").Append(name).Append("  (no clip)");
                return false;
            }

            bool off = false;

            for (int i = 0; i < pool.Count; i++)
            {
                Entry entry = _entries[pool[i]];

                // A clip tagged Heavy is given the longer reaction, so it has to be measured
                // against that rather than against the base.
                float have = baseDuration
                    * (entry.Force == HitSeverity.Heavy ? HeavyHitScale : 1f);

                off |= Line(report, name, entry, have);
            }

            return off;
        }

        /// One clip's fit, and what window would put it at life speed.
        ///
        /// Flags both directions. Too fast is a blur; too slow is slow motion, which is just as
        /// wrong and much easier to miss, because a reaction in slow motion still looks like a
        /// reaction - it only feels weightless.
        bool Line(System.Text.StringBuilder report, string name, Entry entry, float have)
        {
            float shown = (entry.End - entry.Start) * entry.Length;
            float rate = shown / Mathf.Max(0.01f, have);
            float ideal = Mathf.Clamp01(have / entry.Length);

            report.Append("\n  ").Append(name)
                .Append("  ").Append(entry.Clip.name)
                .Append("  [").Append(entry.Direction).Append('/').Append(entry.Force)
                .Append(']')
                .Append("  clip ").Append(entry.Length.ToString("0.00")).Append('s')
                .Append("  window ").Append(entry.Start.ToString("0.00"))
                .Append('-').Append(entry.End.ToString("0.00"))
                .Append(entry.AutoWindow ? " (auto)" : " (set)")
                .Append("  -> ").Append(rate.ToString("0.0")).Append('x');

            if (rate > 1.8f || rate < 0.7f)
            {
                report.Append(rate > 1.8f ? "   TOO FAST" : "   TOO SLOW")
                    .Append(" - clear the Window to 0,0 and it becomes ")
                    .Append(entry.Start.ToString("0.00"))
                    .Append('-').Append(Mathf.Min(1f, entry.Start + ideal).ToString("0.00"));
                return true;
            }

            return false;
        }

        /// Pulls the hips back to the height the stance sits at, in proportion to how much of the
        /// pose is coming from an action clip. See HipHeightLock for why it is not applied all the
        /// time.
        void StabiliseHipHeight()
        {
            if (ModelAnimator == null)
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

            // Learn the resting height from the stance itself, while nothing is distorting it. A
            // slow running average rather than one sample, so the idle bob averages out instead of
            // pinning the hips to wherever frame one happened to catch them.
            float action = Mathf.Max(_upperWeight, _fullWeight);
            if (action < 0.01f)
            {
                _hipHeight = _hipCaptured
                    ? Mathf.Lerp(_hipHeight, local.y, 1f - Mathf.Exp(-0.8f * Time.deltaTime))
                    : local.y;
                _hipCaptured = true;
                return;
            }

            float strength = HipHeightLock * action;
            if (!_hipCaptured || strength <= 0.001f)
            {
                return;
            }

            local.y = Mathf.Lerp(local.y, _hipHeight, strength);
            _hips.position = reference.TransformPoint(local);
        }
    }
}

using UnityEngine;

namespace TheFighter
{
    /// Ducking bends the knees. Lowering the whole model instead - which is what the procedural
    /// pivot does, and what it looked like - sinks the feet through the canvas and reads as the
    /// fighter being pushed into the floor rather than slipping under a punch.
    ///
    /// So: note where the feet are, drop the hips, then solve both legs to put the feet back.
    /// Nothing below the ankles moves, which is the whole difference.
    ///
    /// Runs in LateUpdate after FighterAnimation and before ArmPose: the arms hang off the spine,
    /// so moving the hips after solving them would undo their work.
    public class CrouchPose : MonoBehaviour
    {
        public Fighter Owner;
        public Animator ModelAnimator;

        /// How far the hips drop at a full crouch, in metres.
        public float HipDrop = 0.20f;
        /// The torso folds forward a little as well, the way it does when you really duck.
        public float TorsoPitch = 9f;
        /// Knees bend forward. Flip if they hinge backwards on your rig.
        public float KneeForward = 1f;

        /// The bend that is there before any ducking at all. Set 0 to stand straight-legged.
        public float StanceBend = CombatTuning.StanceKneeBend;
        /// The ready stance's rocking: metres of travel, and cycles per second.
        public float Rhythm = CombatTuning.StanceRhythm;
        public float RhythmRate = CombatTuning.StanceRhythmRate;
        /// The small forward lean that comes with the stance, before any ducking.
        public float StancePitch = 2.5f;

        /// What was actually applied this frame, and what it rests at. PostureProbe reads these
        /// so its "hips are where the stance says" test knows about the stance bend.
        public float CurrentDrop { get; private set; }
        public float RestDrop { get { return StanceBend; } }

        float _phase;
        bool _resolved;

        void Awake()
        {
            // Two fighters rocking in lockstep looks like one puppet on two strings.
            _phase = Random.Range(0f, 10f);
        }
        Transform _hips;
        Transform _spine;
        Transform _leftUpper;
        Transform _leftLower;
        Transform _leftFoot;
        Transform _rightUpper;
        Transform _rightLower;
        Transform _rightFoot;

        void Resolve()
        {
            if (_resolved)
            {
                return;
            }
            _resolved = true;

            if (ModelAnimator == null || !ModelAnimator.isHuman)
            {
                return;
            }

            _hips = ModelAnimator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = ModelAnimator.GetBoneTransform(HumanBodyBones.Spine);
            _leftUpper = ModelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _leftLower = ModelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _leftFoot = ModelAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightUpper = ModelAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _rightLower = ModelAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rightFoot = ModelAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        void LateUpdate()
        {
            if (Owner == null)
            {
                return;
            }

            Resolve();

            float amount = Mathf.Clamp01(Owner.CrouchAmount);
            if (_hips == null || _leftFoot == null || _rightFoot == null)
            {
                CurrentDrop = 0f;
                return;
            }

            // Interpolated, not added. The stance bend and the duck are the same joint doing the
            // same thing by different amounts - adding them would duck 25cm and fold him over.
            float drop = Mathf.Lerp(StanceBend, HipDrop, amount);

            // The rocking fades out once the feet are working, because the step clips bring their
            // own motion and two rhythms at once is a limp. It also fades as he ducks: a man under
            // a punch holds still.
            float speed = Owner.Motor != null ? Owner.Motor.PlanarSpeed : 0f;
            float settled = (1f - Mathf.Clamp01(speed / 0.5f)) * (1f - amount);
            if (settled > 0.001f && Rhythm > 0f)
            {
                drop += Rhythm * settled
                    * Mathf.Sin((Time.time + _phase) * RhythmRate * Mathf.PI * 2f);
            }

            CurrentDrop = drop;

            bool floored = Owner.State == ActionState.Down
                || Owner.State == ActionState.KnockedOut;
            if (floored || Mathf.Abs(drop) < 0.0005f)
            {
                CurrentDrop = 0f;
                return;
            }

            // Where the clip put the feet. This is the one thing the crouch must not change.
            Vector3 leftPlanted = _leftFoot.position;
            Vector3 rightPlanted = _rightFoot.position;

            Transform root = Owner.transform;
            _hips.position -= root.up * drop;

            float pitch = Mathf.Lerp(StancePitch, TorsoPitch, amount);
            if (_spine != null && pitch != 0f)
            {
                _spine.rotation = Quaternion.AngleAxis(pitch, root.right) * _spine.rotation;
            }

            Vector3 knee = root.forward * KneeForward;
            TwoBoneIk.Solve(_leftUpper, _leftLower, _leftFoot, leftPlanted, knee, 1f);
            TwoBoneIk.Solve(_rightUpper, _rightLower, _rightFoot, rightPlanted, knee, 1f);
        }
    }
}

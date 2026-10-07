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

        bool _resolved;
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

            float amount = Owner.CrouchAmount;
            if (amount <= 0.001f || _hips == null || _leftFoot == null || _rightFoot == null)
            {
                return;
            }

            // Where the clip put the feet. This is the one thing the crouch must not change.
            Vector3 leftPlanted = _leftFoot.position;
            Vector3 rightPlanted = _rightFoot.position;

            Transform root = Owner.transform;
            _hips.position -= root.up * (HipDrop * amount);

            if (_spine != null && TorsoPitch != 0f)
            {
                _spine.rotation = Quaternion.AngleAxis(TorsoPitch * amount, root.right)
                    * _spine.rotation;
            }

            Vector3 knee = root.forward * KneeForward;
            TwoBoneIk.Solve(_leftUpper, _leftLower, _leftFoot, leftPlanted, knee, 1f);
            TwoBoneIk.Solve(_rightUpper, _rightLower, _rightFoot, rightPlanted, knee, 1f);
        }
    }
}

using UnityEngine;

namespace TheFighter
{
    /// A guard is just "both gloves beside the face", which is little enough that it does not need
    /// a clip - and Mixamo has almost no boxing guard clips anyway. So this solves the arms onto
    /// the cheeks directly, over whatever animation is playing, and blends in and out with the
    /// guard button.
    ///
    /// Two-bone IK done the convention-independent way: every rotation here comes from
    /// Quaternion.FromToRotation between where a bone currently points and where it should, so
    /// nothing depends on how the rig's bone axes happen to be oriented. Hard-coded Euler angles
    /// would be shorter and would break on the next model.
    ///
    /// Runs in LateUpdate, after the Animator has applied its pose - that is the whole reason it
    /// can override an animation without an IK pass or an Animator Controller.
    public class GuardPose : MonoBehaviour
    {
        public Fighter Owner;
        public Animator ModelAnimator;

        [Header("Where the gloves sit, relative to the head")]
        /// x is outward from the centre line, y up, z forward. Metres.
        public Vector3 HandOffset = new Vector3(0.105f, -0.03f, 0.15f);
        /// The lead hand carries a little further forward, as it does in a real stance.
        public float LeadForwardBias = 0.04f;

        [Header("Blend")]
        public float BlendSpeed = 9f;
        [Range(0f, 1f)] public float MaxWeight = 1f;

        [Header("Fix-ups")]
        /// Flip to -1 if the elbows bend the wrong way on your rig.
        public float ElbowBendSign = 1f;

        float _weight;
        bool _resolved;
        Transform _head;
        Transform _leftUpper;
        Transform _leftLower;
        Transform _leftHand;
        Transform _rightUpper;
        Transform _rightLower;
        Transform _rightHand;

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

            _head = ModelAnimator.GetBoneTransform(HumanBodyBones.Head);
            _leftUpper = ModelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leftLower = ModelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftHand = ModelAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightUpper = ModelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightLower = ModelAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightHand = ModelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        void LateUpdate()
        {
            if (Owner == null)
            {
                return;
            }

            Resolve();

            bool guarding = Owner.IsGuarding && !Owner.GuardBroken
                && Owner.State != ActionState.Down && Owner.State != ActionState.KnockedOut;

            _weight = Mathf.MoveTowards(_weight, guarding ? MaxWeight : 0f, BlendSpeed * Time.deltaTime);

            if (_weight <= 0.001f || _head == null)
            {
                return;
            }

            SolveArm(_leftUpper, _leftLower, _leftHand, TargetFor(true), -1f);
            SolveArm(_rightUpper, _rightLower, _rightHand, TargetFor(false), 1f);
        }

        Vector3 TargetFor(bool left)
        {
            Transform root = Owner.transform;
            bool isLead = (Owner.CurrentStance == Stance.Orthodox) == left;
            float forward = HandOffset.z + (isLead ? LeadForwardBias : 0f);

            return _head.position
                + root.right * (HandOffset.x * (left ? -1f : 1f))
                + root.up * HandOffset.y
                + root.forward * forward;
        }

        void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, float side)
        {
            if (upper == null || lower == null || hand == null)
            {
                return;
            }

            Quaternion upperBefore = upper.rotation;
            Quaternion lowerBefore = lower.rotation;

            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            if (upperLength < 0.0001f || lowerLength < 0.0001f)
            {
                return;
            }

            Vector3 toTarget = target - shoulder;
            float reach = toTarget.magnitude;
            if (reach < 0.0001f)
            {
                return;
            }

            // Keep the target inside what the arm can actually reach, and off the exact limits so
            // the triangle never degenerates.
            float distance = Mathf.Clamp(reach,
                Mathf.Abs(upperLength - lowerLength) + 0.01f,
                upperLength + lowerLength - 0.01f);

            Vector3 direction = toTarget / reach;
            Vector3 reachable = shoulder + direction * distance;

            // The elbow swings outward, away from the ribs.
            Vector3 bendAxis = Vector3.Cross(direction, Owner.transform.right * side * ElbowBendSign);
            if (bendAxis.sqrMagnitude < 0.0001f)
            {
                bendAxis = Owner.transform.up;
            }
            bendAxis.Normalize();

            float cosShoulder = Mathf.Clamp(
                (upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                / (2f * upperLength * distance), -1f, 1f);
            float shoulderAngle = Mathf.Acos(cosShoulder) * Mathf.Rad2Deg;

            // Point the whole arm at the target, swing the elbow out of that line by the triangle's
            // shoulder angle, then close the forearm so the hand arrives.
            upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, reachable - shoulder)
                * upper.rotation;
            upper.rotation = Quaternion.AngleAxis(shoulderAngle, bendAxis) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position,
                reachable - lower.position) * lower.rotation;

            upper.rotation = Quaternion.Slerp(upperBefore, upper.rotation, _weight);
            lower.rotation = Quaternion.Slerp(lowerBefore, lower.rotation, _weight);
        }
    }
}

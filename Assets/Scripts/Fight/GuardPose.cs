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
        /// x is outward from the centre line, y up, z forward. Metres. Measured from the head
        /// *bone*, which on a Mixamo rig sits around the jaw rather than at the middle of the
        /// face, so y is a little positive to reach the cheeks.
        public Vector3 HandOffset = new Vector3(0.11f, 0.02f, 0.13f);
        /// The lead hand carries a little further forward, as it does in a real stance.
        public float LeadForwardBias = 0.04f;

        [Header("Where the elbow goes")]
        /// Which way the elbow is pushed off the straight shoulder-to-glove line. x is outward
        /// from the body, y up, z forward - so this default is "down, slightly out, slightly
        /// back", which is a boxing guard: elbows hanging in front of the ribs.
        ///
        /// Two bones and a target leave the elbow free to sit anywhere on a circle, and this is
        /// what picks the spot. Pointing it outward instead gives the chicken-wing - arms rising
        /// to the sides with the elbows in a V - which is the one thing a guard must not look
        /// like, since the elbows are what cover the body.
        public Vector3 ElbowHint = new Vector3(0.3f, -1f, -0.25f);

        [Header("Blend")]
        /// How fast the hands come back to the cheeks.
        public float BlendSpeed = 9f;
        /// How fast an arm is released to throw. Much faster: a punch cannot wait for the guard
        /// to fade or it starts from the wrong place.
        public float ReleaseSpeed = 40f;
        [Range(0f, 1f)] public float MaxWeight = 1f;

        /// How much the hands are held at the cheeks even when the guard button is not down.
        ///
        /// This is the difference between a boxer and a mannequin holding a pose. Mixamo's
        /// shadowbox clips rest the hands low and loose, so between punches the fighter stands
        /// there with his chin out - and no clip fixes that, because the clip is where it comes
        /// from. A partial pull home costs nothing and reads as somebody who has been taught to
        /// keep his hands up.
        [Range(0f, 1f)] public float IdleGuardWeight = 0.45f;

        /// With a Guard clip assigned, an animated guard and a full IK guard fight each other and
        /// you get neither - so the clip takes the active guard and this layer drops back to the
        /// idle pull. Clear the Guard slot to hand the whole job to IK.
        public bool DeferToGuardClip = true;

        float _leftWeight;
        float _rightWeight;
        FighterAnimation _animation;
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

            _animation = GetComponent<FighterAnimation>();
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

            bool floored = Owner.State == ActionState.Down || Owner.State == ActionState.KnockedOut;
            bool guarding = Owner.IsGuarding && !Owner.GuardBroken && !floored;

            if (guarding && DeferToGuardClip && _animation != null && _animation.GuardClipAssigned)
            {
                guarding = false;
            }

            float target = floored ? 0f : (guarding ? MaxWeight : IdleGuardWeight);

            // The arm that is throwing has to be free; the other one stays home.
            bool punching = Owner.ActivePunch != null && !floored;
            bool leftThrowing = punching && Owner.Rig != null && Owner.Rig.IsLeftHand(Owner.ActiveHand);

            float leftTarget = punching && leftThrowing ? 0f : target;
            float rightTarget = punching && !leftThrowing ? 0f : target;

            _leftWeight = Step(_leftWeight, leftTarget);
            _rightWeight = Step(_rightWeight, rightTarget);

            if (_head == null)
            {
                return;
            }

            if (_leftWeight > 0.001f)
            {
                SolveArm(_leftUpper, _leftLower, _leftHand, TargetFor(true), -1f, _leftWeight);
            }
            if (_rightWeight > 0.001f)
            {
                SolveArm(_rightUpper, _rightLower, _rightHand, TargetFor(false), 1f, _rightWeight);
            }
        }

        /// Releasing is fast and returning is not, so a punch is never held up by its own guard
        /// fading out.
        float Step(float current, float target)
        {
            float speed = target < current ? ReleaseSpeed : BlendSpeed;
            return Mathf.MoveTowards(current, target, speed * Time.deltaTime);
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

        void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, float side,
            float weight)
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

            Transform root = Owner.transform;
            Vector3 hint = root.right * (ElbowHint.x * side)
                + root.up * ElbowHint.y
                + root.forward * ElbowHint.z;

            // Only the part of the hint lying across the shoulder-to-glove line can move the
            // elbow; anything along that line just points at the glove.
            Vector3 pole = hint - direction * Vector3.Dot(hint, direction);
            if (pole.sqrMagnitude < 0.0001f)
            {
                pole = -root.up - direction * Vector3.Dot(-root.up, direction);
                if (pole.sqrMagnitude < 0.0001f)
                {
                    pole = root.forward;
                }
            }
            pole.Normalize();

            // Rotating a vector v about Cross(v, pole) carries it toward pole - the derivative of
            // the rotation at zero is Cross(axis, v), which works out to exactly pole. Get this
            // cross product backwards and the elbow swings to the opposite side of the circle.
            Vector3 bendAxis = Vector3.Cross(direction, pole);
            if (bendAxis.sqrMagnitude < 0.0001f)
            {
                return;
            }
            bendAxis.Normalize();

            float cosShoulder = Mathf.Clamp(
                (upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                / (2f * upperLength * distance), -1f, 1f);
            float shoulderAngle = Mathf.Acos(cosShoulder) * Mathf.Rad2Deg;

            // Point the whole arm at the glove, swing the elbow off that line by the triangle's
            // shoulder angle, then close the forearm so the hand arrives.
            Quaternion upperSolved = Quaternion.AngleAxis(shoulderAngle, bendAxis)
                * Quaternion.FromToRotation(lower.position - shoulder, reachable - shoulder)
                * upper.rotation;
            upper.rotation = upperSolved;

            // Read after the upper arm moved: lower and hand have followed it, so this aims the
            // forearm from where the elbow now is.
            Quaternion lowerSolved = Quaternion.FromToRotation(hand.position - lower.position,
                reachable - lower.position) * lower.rotation;

            upper.rotation = Quaternion.Slerp(upperBefore, upperSolved, weight);
            lower.rotation = Quaternion.Slerp(lowerBefore, lowerSolved, weight);
        }
    }
}

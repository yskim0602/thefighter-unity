using UnityEngine;

namespace TheFighter
{
    /// Owns both arms, on top of whatever clip is playing. Two jobs, one component on purpose: a
    /// guard layer and an aim layer writing arm rotations in the same LateUpdate would fight each
    /// other, and the arm that is throwing is exactly the arm the guard wants to hold home.
    ///
    /// **Aim.** A punch clip throws wherever the animator pointed it when they recorded it - which
    /// is not at the fighter in front of you. That is why the left hand went off into empty space
    /// instead of at the opponent's face. So the clip supplies the motion and IK supplies the
    /// address: the hand is steered onto the opponent, blending in over the back half of the
    /// extension so the shot keeps the clip's arc instead of becoming a straight poke. It also
    /// makes the hit test honest, because the hitbox reads the same hand bone.
    ///
    /// **Guard.** Mixamo's shadowbox clips rest the hands low and loose, so between punches the
    /// fighter stands with his chin out. The hands are pulled partly home even with the guard
    /// button up, and only the throwing arm is released - a boxer who drops both hands to punch is
    /// a boxer who gets hit.
    ///
    /// Runs last in LateUpdate, after FighterAnimation and CrouchPose: the arms hang off the
    /// spine, so anything that moves the hips afterwards would undo this.
    public class ArmPose : MonoBehaviour
    {
        public Fighter Owner;
        public Animator ModelAnimator;

        [Header("Guard - where the gloves sit, relative to the head")]
        /// x is outward from the centre line, y up, z forward. Metres. Measured from the head
        /// *bone*, which on a Mixamo rig sits around the jaw rather than at the middle of the
        /// face, so y is a little positive to reach the cheeks.
        public Vector3 HandOffset = new Vector3(0.11f, 0.02f, 0.13f);
        /// The lead hand carries a little further forward, as it does in a real stance.
        public float LeadForwardBias = 0.04f;
        /// How much the hands are held at the cheeks even when the guard button is not down.
        [Range(0f, 1f)] public float IdleGuardWeight = 0.45f;
        [Range(0f, 1f)] public float MaxWeight = 1f;
        /// With a Guard clip assigned, an animated guard and a full IK guard fight each other and
        /// you get neither - so the clip leads and IK only assists. Clear the Guard slot to hand
        /// the whole job to IK.
        public bool DeferToGuardClip = true;
        /// How much IK still applies while a Guard clip is leading. Not zero: standing down
        /// completely leaves the guard entirely at the mercy of how good that clip happens to be,
        /// and "the guard does not really come up" is the one thing a boxing game cannot have.
        /// This guarantees the gloves reach the cheeks whatever the clip does.
        [Range(0f, 1f)] public float GuardClipAssist = 0.55f;

        [Header("Punch aim")]
        /// 0 leaves punches aimed wherever the clip aimed them. 1 drags the hand all the way onto
        /// the target, which lands every shot but straightens a hook into a jab. The default keeps
        /// most of the clip's shape while fixing the address.
        [Range(0f, 1f)] public float AimWeight = 0.8f;
        /// Where on the extension the aim starts blending in, on Fighter.PunchTrack. Early enough
        /// to steer the shot, late enough that the wind-up is still the clip's.
        public float AimStartTrack = 0.3f;
        /// How much of that aim a feint gets. Some, so it reads as a punch; not all, because a
        /// feint only works if it cannot be told from one in time.
        [Range(0f, 1f)] public float FeintAimWeight = 0.5f;

        [Header("Where the elbow goes")]
        /// Which way the elbow is pushed off the straight shoulder-to-glove line. x is outward
        /// from the body, y up, z forward - so this default is "down, slightly out, slightly
        /// back", which is a boxing guard: elbows hanging in front of the ribs. Pointing it
        /// outward gives the chicken-wing, and the elbows are what cover the body.
        public Vector3 GuardElbowHint = new Vector3(0.3f, -1f, -0.25f);
        /// Throwing, the elbow comes up behind the fist rather than hanging.
        public Vector3 PunchElbowHint = new Vector3(0.25f, -0.55f, -0.3f);

        [Header("Shoulder")]
        /// How much the shoulder turns into the punch before the arm is solved.
        ///
        /// Without this the reach comes entirely from straightening the elbow, and a punch that
        /// extends from the elbow outward is the "arms only" look however much the torso twists
        /// behind it. The shoulder is the last link of foot -> knee -> hip -> torso -> shoulder ->
        /// arm, and it is the one the clip is least likely to have aimed where we need it.
        ///
        /// Partial on purpose: all the way and the shoulder dislocates past what the mesh skins.
        [Range(0f, 1f)] public float ShoulderDrive = 0.35f;

        [Header("Blend")]
        public float BlendSpeed = 9f;
        /// Releasing an arm to throw is much faster than bringing it home: a punch cannot wait for
        /// its own guard to fade or it starts from the wrong place.
        public float ReleaseSpeed = 40f;

        float _leftWeight;
        float _rightWeight;
        bool _resolved;
        FighterAnimation _animation;
        Transform _head;
        Transform _leftShoulder;
        Transform _rightShoulder;
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
            // Optional in a Humanoid rig, so everything downstream tolerates null.
            _leftShoulder = ModelAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _rightShoulder = ModelAnimator.GetBoneTransform(HumanBodyBones.RightShoulder);
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
            if (_head == null)
            {
                return;
            }

            bool floored = Owner.State == ActionState.Down || Owner.State == ActionState.KnockedOut;
            bool guarding = Owner.IsGuarding && !Owner.GuardBroken && !floored;

            float full = MaxWeight;
            if (DeferToGuardClip && _animation != null && _animation.GuardClipAssigned)
            {
                full = Mathf.Min(MaxWeight, GuardClipAssist);
            }

            float guardTarget = floored ? 0f : (guarding ? full : IdleGuardWeight);

            // Aim only on the way out. On the way back the guard return takes the arm, which is
            // where it should be going anyway.
            bool aiming = !floored
                && Owner.ActivePunch != null
                && Owner.State != ActionState.Recovery
                && Owner.Opponent != null
                && AimWeight > 0.001f;

            // A feint has to read as a punch, so the arm is released and the shoulder turns - but
            // it is not steered onto the chin. A feint that tracks its target perfectly is a tell,
            // and the whole value of one is that it cannot be told apart in time.
            float aimScale = Owner.IsFeinting ? FeintAimWeight : 1f;

            bool leftThrowing = Owner.ActivePunch != null && Owner.Rig != null
                && Owner.Rig.IsLeftHand(Owner.ActiveHand);

            float aim = 0f;
            if (aiming)
            {
                float t = Mathf.InverseLerp(AimStartTrack, 1f, Owner.PunchTrack);
                aim = Mathf.SmoothStep(0f, 1f, t) * AimWeight * aimScale;
            }

            SolveSide(true, leftThrowing, aim, guardTarget, ref _leftWeight);
            SolveSide(false, !leftThrowing, aim, guardTarget, ref _rightWeight);
        }

        void SolveSide(bool left, bool throwing, float aim, float guardTarget, ref float weight)
        {
            Transform upper = left ? _leftUpper : _rightUpper;
            Transform lower = left ? _leftLower : _rightLower;
            Transform hand = left ? _leftHand : _rightHand;
            if (upper == null || lower == null || hand == null)
            {
                return;
            }

            float side = left ? -1f : 1f;

            if (throwing && aim > 0.001f)
            {
                // The throwing arm is aimed, not guarded, so its guard weight bleeds away rather
                // than snapping - otherwise the next punch would start from a half-blended pose.
                weight = Mathf.MoveTowards(weight, 0f, ReleaseSpeed * Time.deltaTime);

                // Shoulder first, so the arm is then solved from where the body put it. Solving
                // the arm and then turning the shoulder would drag the hand back off target.
                Transform shoulder = left ? _leftShoulder : _rightShoulder;
                DriveShoulder(shoulder, hand, AimTarget(upper.position), aim * ShoulderDrive);

                TwoBoneIk.Solve(upper, lower, hand, AimTarget(upper.position),
                    HintFor(PunchElbowHint, side), aim);
                return;
            }

            float target = throwing ? 0f : guardTarget;
            float speed = target < weight ? ReleaseSpeed : BlendSpeed;
            weight = Mathf.MoveTowards(weight, target, speed * Time.deltaTime);

            if (weight > 0.001f)
            {
                TwoBoneIk.Solve(upper, lower, hand, GuardTarget(left),
                    HintFor(GuardElbowHint, side), weight);
            }
        }

        /// Turns the shoulder a fraction of the way toward the target, so the reach starts at the
        /// body rather than at the elbow.
        void DriveShoulder(Transform shoulder, Transform hand, Vector3 target, float weight)
        {
            if (shoulder == null || weight <= 0.001f)
            {
                return;
            }

            Vector3 from = hand.position - shoulder.position;
            Vector3 to = target - shoulder.position;
            if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f)
            {
                return;
            }

            Quaternion before = shoulder.rotation;
            shoulder.rotation = Quaternion.Slerp(before,
                Quaternion.FromToRotation(from, to) * before, weight);
        }

        Vector3 HintFor(Vector3 hint, float side)
        {
            Transform root = Owner.transform;
            return root.right * (hint.x * side) + root.up * hint.y + root.forward * hint.z;
        }

        Vector3 GuardTarget(bool left)
        {
            Transform root = Owner.transform;
            bool isLead = (Owner.CurrentStance == Stance.Orthodox) == left;
            float forward = HandOffset.z + (isLead ? LeadForwardBias : 0f);

            return _head.position
                + root.right * (HandOffset.x * (left ? -1f : 1f))
                + root.up * HandOffset.y
                + root.forward * forward;
        }

        /// Where the wrist has to be for the *glove* to be on the target, since the solver places
        /// the hand bone and the fist carries on past it.
        Vector3 AimTarget(Vector3 shoulder)
        {
            Fighter foe = Owner.Opponent;

            Vector3 head = foe.HeadHurtbox != null
                ? foe.HeadHurtbox.position
                : foe.transform.position + Vector3.up * CombatTuning.HeadHurtboxHeight;
            Vector3 body = foe.transform.position + Vector3.up * CombatTuning.BodyHurtboxCentre;

            Vector3 target = Vector3.Lerp(body, head, Mathf.Clamp01(Owner.AimHeight));

            float knuckles = Owner.Rig != null ? Owner.Rig.HandReachOffset : 0.11f;
            Vector3 toTarget = target - shoulder;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return target;
            }

            return target - toTarget.normalized * knuckles;
        }
    }
}

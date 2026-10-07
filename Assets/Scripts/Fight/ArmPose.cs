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

        /// A guard is not one pose. Boxing has several, and which one you are in says what you
        /// are expecting - hands out front against straight punching, clamped to the ears against
        /// hooks, elbows dropped against the body. Picking the right one is most of what makes a
        /// defence read as a defence rather than as two arms held up.
        public enum GuardShape
        {
            /// Hands at the cheeks, slightly forward. The default, against straight punching.
            High,
            /// Tight to the temples with the elbows up, and the near hand clamped over the ear on
            /// the side the hook is coming from. A high guard does nothing about a hook, which
            /// arrives around it rather than through it.
            HookCover,
            /// Elbows dropped onto the ribs. Costs the head, which is the trade.
            BodyCover
        }

        [Header("Guard - where the gloves sit, relative to the head")]
        /// x is outward from the centre line, y up, z forward. Metres. Measured from the head
        /// *bone*, which on a Mixamo rig sits around the jaw rather than at the middle of the
        /// face, so y is a little positive to reach the cheeks.
        public Vector3 HandOffset = new Vector3(0.11f, 0.02f, 0.13f);
        /// The lead hand carries a little further forward, as it does in a real stance.
        public float LeadForwardBias = 0.04f;

        [Header("Guard - hook cover")]
        /// Against a hook: tighter in, higher, and hard against the head.
        public Vector3 HookHandOffset = new Vector3(0.085f, 0.07f, 0.045f);
        /// The glove on the side the hook comes from goes further still - over the ear.
        public Vector3 HookNearExtra = new Vector3(0.015f, 0.05f, -0.03f);
        /// Elbows come up in front of the face rather than hanging at the ribs.
        public Vector3 HookElbowHint = new Vector3(0.5f, -0.35f, 0.55f);

        [Header("Guard - body cover")]
        /// Elbows onto the ribs, hands still high enough to be worth something.
        public Vector3 BodyHandOffset = new Vector3(0.1f, -0.04f, 0.08f);
        public Vector3 BodyElbowHint = new Vector3(0.12f, -1f, 0.1f);
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
        [Range(0f, 1f)] public float AimWeight = 0.95f;
        /// Where on the extension the aim starts blending in, on Fighter.PunchTrack. Early enough
        /// to steer the shot, late enough that the wind-up is still the clip's.
        public float AimStartTrack = 0.2f;
        /// How much of that aim a feint gets. Some, so it reads as a punch; not all, because a
        /// feint only works if it cannot be told from one in time.
        [Range(0f, 1f)] public float FeintAimWeight = 0.5f;
        /// How fast the aim lets go when the strike ends.
        ///
        /// It used to simply stop: `aiming` goes false the frame the strike becomes recovery, so
        /// the weight fell from 0.95 to 0 between two frames and the arm snapped. On a slow punch
        /// that snap is the most visible thing in the animation - it was the uppercut flailing.
        /// Fast enough not to drag the hand along, slow enough to be a release.
        public float AimReleaseSpeed = 6f;

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

        float _aim;
        float _leftWeight;
        float _rightWeight;
        GuardShape _shape = GuardShape.High;
        float _hookSide;
        float _shapeHold;
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

            // Per side, and both require a punch to exist. This used to be computed as
            // `leftThrowing` and `!leftThrowing`, so with no punch at all the right arm was told
            // it was throwing - its guard weight was driven to zero and the right hand never came
            // up. That is why the guard only ever looked like one hand.
            bool punching = Owner.ActivePunch != null && !floored;
            bool leftIsActive = Owner.Rig != null && Owner.Rig.IsLeftHand(Owner.ActiveHand);
            bool leftThrowing = punching && leftIsActive;
            bool rightThrowing = punching && !leftIsActive;

            float wanted = 0f;
            if (aiming)
            {
                float t = Mathf.InverseLerp(AimStartTrack, 1f, Owner.PunchTrack);
                wanted = Mathf.SmoothStep(0f, 1f, t) * AimWeight * aimScale;
            }

            // Rising follows the punch exactly, because the fist has to arrive where it is aimed.
            // Falling is damped, because nothing has to arrive anywhere on the way back.
            _aim = wanted > _aim
                ? wanted
                : Mathf.MoveTowards(_aim, wanted, AimReleaseSpeed * Time.deltaTime);
            float aim = _aim;

            GuardShape shape = ChooseShape();

            SolveSide(true, leftThrowing, aim, guardTarget, shape, ref _leftWeight);
            SolveSide(false, rightThrowing, aim, guardTarget, shape, ref _rightWeight);
        }

        void SolveSide(bool left, bool throwing, float aim, float guardTarget, GuardShape shape,
            ref float weight)
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
                TwoBoneIk.Solve(upper, lower, hand, GuardTarget(left, shape),
                    HintFor(ElbowHintFor(shape), side), weight);
            }
        }

        /// Reads the incoming punch and picks the guard for it. Nothing random: the shape is a
        /// statement about what the fighter thinks is coming, so it follows the threat.
        GuardShape ChooseShape()
        {
            Fighter foe = Owner.Opponent;
            PunchDefinition incoming = foe != null ? foe.ActivePunch : null;

            if (incoming == null)
            {
                // Nothing in the air: hold the shape a moment rather than snapping back, so a
                // combination does not flicker the guard between poses.
                _shapeHold = Mathf.MoveTowards(_shapeHold, 0f, Time.deltaTime);
                return _shapeHold > 0f ? _shape : GuardShape.High;
            }

            _shapeHold = 0.35f;

            if (incoming.Kind == PunchKind.Hook)
            {
                _shape = GuardShape.HookCover;
                // Which ear to cover: the hand it is thrown with decides, mirrored for a southpaw
                // since his lead is the other arm.
                bool fromMyLeft = foe.Rig != null && !foe.Rig.IsLeftHand(foe.ActiveHand);
                _hookSide = fromMyLeft ? -1f : 1f;
                return _shape;
            }

            if (foe.AimHeight < 0.5f || incoming.Kind == PunchKind.Uppercut)
            {
                _shape = GuardShape.BodyCover;
                return _shape;
            }

            _shape = GuardShape.High;
            return _shape;
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

        Vector3 ElbowHintFor(GuardShape shape)
        {
            switch (shape)
            {
                case GuardShape.HookCover: return HookElbowHint;
                case GuardShape.BodyCover: return BodyElbowHint;
                default: return GuardElbowHint;
            }
        }

        Vector3 GuardTarget(bool left, GuardShape shape)
        {
            Transform root = Owner.transform;
            float side = left ? -1f : 1f;
            Vector3 offset;

            switch (shape)
            {
                case GuardShape.HookCover:
                    offset = HookHandOffset;
                    // The glove on the side the hook is coming from clamps over the ear.
                    if (Mathf.Approximately(_hookSide, side))
                    {
                        offset += HookNearExtra;
                    }
                    break;

                case GuardShape.BodyCover:
                    offset = BodyHandOffset;
                    break;

                default:
                    offset = HandOffset;
                    bool isLead = (Owner.CurrentStance == Stance.Orthodox) == left;
                    offset.z += isLead ? LeadForwardBias : 0f;
                    break;
            }

            return _head.position
                + root.right * (offset.x * side)
                + root.up * offset.y
                + root.forward * offset.z;
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

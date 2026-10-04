using UnityEngine;

namespace TheFighter
{
    /// The procedural animation layer: where the gloves rest, the path a punch travels, how the
    /// body commits to throwing it, how it recoils from taking one, and how it folds on a
    /// knockdown. One component owns the whole body pose so nothing fights over a transform.
    ///
    /// The fighter refreshes it every frame *before* hit tests, so the glove you see is literally
    /// the thing that does the hitting.
    ///
    /// First person gets its own glove rest poses. In the Godot build the gloves sat at body
    /// height and swallowed most of the screen, so here they idle low at the bottom edge and only
    /// come up to frame the view when the guard goes up. Punches ignore the rest pose past the
    /// halfway mark and converge onto the real trajectory, so first and third person land
    /// identically.
    ///
    /// Everything here is placeholder motion for capsules. When real models arrive this becomes
    /// AnimationPlayer calls and the combat code above it does not change.
    public class FighterRig : MonoBehaviour
    {
        [Header("References")]
        public Fighter Owner;
        /// Parent of the whole visible body. Leaning, recoil and the knockdown pose move this one
        /// transform, so the gloves and head come along for free.
        public Transform BodyPivot;
        public Transform Head;
        public Transform LeftGlove;
        public Transform RightGlove;

        [Header("Third person rest poses (right-hand values, mirrored by stance)")]
        public Vector3 ThirdPersonIdleLead = new Vector3(0.24f, 1.32f, 0.34f);
        public Vector3 ThirdPersonIdleRear = new Vector3(0.28f, 1.26f, 0.16f);
        public Vector3 ThirdPersonGuardLead = new Vector3(0.15f, 1.50f, 0.30f);
        public Vector3 ThirdPersonGuardRear = new Vector3(0.19f, 1.47f, 0.22f);

        [Header("First person rest poses - idle sits low, guard lifts into frame")]
        public Vector3 FirstPersonIdleLead = new Vector3(0.20f, 1.41f, 0.50f);
        public Vector3 FirstPersonIdleRear = new Vector3(0.27f, 1.38f, 0.44f);
        public Vector3 FirstPersonGuardLead = new Vector3(0.14f, 1.56f, 0.42f);
        public Vector3 FirstPersonGuardRear = new Vector3(0.18f, 1.52f, 0.38f);
        public float FirstPersonGloveScale = 0.62f;
        public Vector3 DownGlovePose = new Vector3(0.30f, 0.98f, 0.26f);

        [Header("Trajectory")]
        public Vector3 ShoulderLocal = new Vector3(0.20f, 1.38f, 0.05f);
        public float BodyTargetHeight = 1.02f;
        public float HeadTargetHeight = 1.54f;
        public float RestResponsiveness = 16f;

        [Header("Throwing a punch - body commitment")]
        /// Centimetres the body drives forward behind a fully extended punch.
        public float PunchLunge = 0.06f;
        /// Degrees the shoulders rotate through the punch.
        public float PunchTorsoYaw = 9f;
        public float PunchLean = 4f;

        [Header("Taking a punch - recoil")]
        /// Damage that produces a full-strength recoil.
        public float RecoilDamageReference = 18f;
        public float RecoilSeconds = 0.20f;
        public float StaggerLean = 9f;

        [Header("Knockdown")]
        public Vector3 DownPosition = new Vector3(0f, -0.72f, -0.08f);
        public Vector3 DownRotation = new Vector3(24f, 0f, 10f);
        public float DownFallSeconds = 0.30f;
        public float DownRiseSeconds = 0.45f;

        bool _firstPerson;
        bool _captured;
        Vector3 _leftBaseScale = Vector3.one;
        Vector3 _rightBaseScale = Vector3.one;
        Vector3 _pivotBasePosition;
        Quaternion _pivotBaseRotation = Quaternion.identity;
        Vector3 _headBasePosition;
        Quaternion _headBaseRotation = Quaternion.identity;

        float _downBlend;
        float _recoilTimer;
        float _recoilDuration;
        Vector3 _recoilPush;
        Vector3 _recoilAngles;
        Vector3 _headRecoilPush;
        Vector3 _headRecoilAngles;

        /// The rig is wired up after AddComponent, so Awake is too early to read the base pose.
        void Capture()
        {
            if (_captured || LeftGlove == null || RightGlove == null)
            {
                return;
            }

            _leftBaseScale = LeftGlove.localScale;
            _rightBaseScale = RightGlove.localScale;

            if (BodyPivot != null)
            {
                _pivotBasePosition = BodyPivot.localPosition;
                _pivotBaseRotation = BodyPivot.localRotation;
            }
            if (Head != null)
            {
                _headBasePosition = Head.localPosition;
                _headBaseRotation = Head.localRotation;
            }

            _captured = true;
        }

        // ------------------------------------------------------------------
        // View mode
        // ------------------------------------------------------------------

        public bool FirstPerson
        {
            get { return _firstPerson; }
        }

        public void SetFirstPerson(bool enabled)
        {
            _firstPerson = enabled;
            ApplyScale();
        }

        void ApplyScale()
        {
            Capture();
            float s = _firstPerson ? FirstPersonGloveScale : 1f;
            if (LeftGlove != null)
            {
                LeftGlove.localScale = _leftBaseScale * s;
            }
            if (RightGlove != null)
            {
                RightGlove.localScale = _rightBaseScale * s;
            }
        }

        // ------------------------------------------------------------------
        // Hands
        // ------------------------------------------------------------------

        public bool LeadIsLeftHand
        {
            get { return Owner == null || Owner.CurrentStance == Stance.Orthodox; }
        }

        bool IsLeftHand(HandRole role)
        {
            return role == HandRole.Lead ? LeadIsLeftHand : !LeadIsLeftHand;
        }

        public Transform GloveFor(HandRole role)
        {
            return IsLeftHand(role) ? LeftGlove : RightGlove;
        }

        /// Orthodox and southpaw are the same numbers with the x sign flipped, so a stance switch
        /// never needs a second set of poses.
        public float SideSign(HandRole role)
        {
            return IsLeftHand(role) ? -1f : 1f;
        }

        public Vector3 GetGloveWorldPosition(HandRole role)
        {
            Transform glove = GloveFor(role);
            return glove != null ? glove.position : transform.position;
        }

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        public void Refresh(float deltaTime)
        {
            if (Owner == null)
            {
                return;
            }

            Capture();
            UpdateBody(deltaTime);
            UpdateHand(HandRole.Lead, deltaTime);
            UpdateHand(HandRole.Rear, deltaTime);
        }

        void UpdateHand(HandRole role, float deltaTime)
        {
            Transform glove = GloveFor(role);
            if (glove == null)
            {
                return;
            }

            Vector3 rest = RestLocal(role);
            PunchDefinition punch = Owner.ActivePunch;

            if (punch != null && Owner.ActiveHand == role)
            {
                glove.localPosition = PunchLocal(role, rest, punch, Owner.PunchTrack);
            }
            else
            {
                glove.localPosition = Vector3.Lerp(glove.localPosition, rest,
                    1f - Mathf.Exp(-RestResponsiveness * deltaTime));
            }
        }

        Vector3 RestLocal(HandRole role)
        {
            float sign = SideSign(role);

            if (Owner.State == ActionState.Down || Owner.State == ActionState.KnockedOut)
            {
                return new Vector3(sign * DownGlovePose.x, DownGlovePose.y, DownGlovePose.z);
            }

            bool guarding = Owner.IsGuarding && !Owner.GuardBroken;
            bool lead = role == HandRole.Lead;

            Vector3 pose;
            if (_firstPerson)
            {
                pose = guarding
                    ? (lead ? FirstPersonGuardLead : FirstPersonGuardRear)
                    : (lead ? FirstPersonIdleLead : FirstPersonIdleRear);
            }
            else
            {
                pose = guarding
                    ? (lead ? ThirdPersonGuardLead : ThirdPersonGuardRear)
                    : (lead ? ThirdPersonIdleLead : ThirdPersonIdleRear);
            }

            pose.x *= sign;
            return pose;
        }

        Vector3 PunchLocal(HandRole role, Vector3 rest, PunchDefinition punch, float track)
        {
            float sign = SideSign(role);

            if (track <= 0f)
            {
                float load = Mathf.Clamp01(-track / CombatTuning.PunchLoadTrack);
                Vector3 loaded = rest + new Vector3(sign * 0.05f, -punch.RiseArc * 0.35f, -0.13f);
                return Vector3.Lerp(rest, loaded, load);
            }

            Vector3 target = TargetLocal(punch, sign);
            Vector3 mid = (rest + target) * 0.5f;
            Vector3 control = mid + new Vector3(sign * punch.LateralArc, -punch.RiseArc, 0f);
            return QuadraticBezier(rest, control, target, Mathf.Clamp01(track));
        }

        Vector3 TargetLocal(PunchDefinition punch, float sign)
        {
            float y = Mathf.Lerp(BodyTargetHeight, HeadTargetHeight, Mathf.Clamp01(Owner.AimHeight));
            float forward = ShoulderLocal.z + Owner.PunchReach(punch);
            return new Vector3(sign * 0.04f, y, forward);
        }

        // ------------------------------------------------------------------
        // Body pose: commitment, recoil, knockdown
        // ------------------------------------------------------------------

        void UpdateBody(float deltaTime)
        {
            if (BodyPivot == null)
            {
                return;
            }

            bool downed = Owner.State == ActionState.Down || Owner.State == ActionState.KnockedOut;
            _downBlend = Mathf.MoveTowards(_downBlend, downed ? 1f : 0f,
                deltaTime / Mathf.Max(0.01f, downed ? DownFallSeconds : DownRiseSeconds));

            if (_recoilTimer > 0f)
            {
                _recoilTimer -= deltaTime;
            }
            float recoil = _recoilDuration > 0f
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_recoilTimer / _recoilDuration))
                : 0f;

            Vector3 position = Vector3.zero;
            Vector3 angles = Vector3.zero;

            PunchDefinition punch = Owner.ActivePunch;
            if (punch != null)
            {
                // Negative track is the load: the body coils back before it drives forward.
                float commit = Mathf.Clamp01(Owner.PunchTrack)
                    - Mathf.Clamp01(-Owner.PunchTrack / CombatTuning.PunchLoadTrack) * 0.6f;
                float sign = SideSign(Owner.ActiveHand);

                position.z += commit * PunchLunge;
                angles.y -= sign * commit * PunchTorsoYaw;
                angles.x += commit * PunchLean - punch.RiseArc * commit * 7f;
            }

            if (Owner.State == ActionState.Staggered)
            {
                angles.x -= StaggerLean;
                position.z -= 0.04f;
            }

            position += _recoilPush * recoil;
            angles += _recoilAngles * recoil;

            position = Vector3.Lerp(position, DownPosition, _downBlend);
            angles = Vector3.Lerp(angles, DownRotation, _downBlend);

            BodyPivot.localPosition = _pivotBasePosition + position;
            BodyPivot.localRotation = _pivotBaseRotation * Quaternion.Euler(angles);

            if (Head != null)
            {
                float headFade = recoil * (1f - _downBlend);
                Head.localPosition = _headBasePosition + _headRecoilPush * headFade;
                Head.localRotation = _headBaseRotation * Quaternion.Euler(_headRecoilAngles * headFade);
            }
        }

        /// A punch landing has to move the person it landed on, or it reads as a glove passing
        /// through them. A head shot snaps the head back and across; a body shot folds them over it.
        public void PlayHitReaction(HitZone zone, float damage, Vector3 worldPushDirection)
        {
            Capture();

            float strength = Mathf.Clamp01(damage / Mathf.Max(1f, RecoilDamageReference));
            if (strength <= 0.01f)
            {
                return;
            }

            Vector3 local = worldPushDirection.sqrMagnitude > 0.0001f
                ? transform.InverseTransformDirection(worldPushDirection.normalized)
                : Vector3.back;

            if (zone == HitZone.Head)
            {
                _recoilPush = new Vector3(local.x * 0.04f, 0.015f, local.z * 0.05f) * strength;
                _recoilAngles = new Vector3(-7f, local.x * 6f, -local.x * 4f) * strength;
                _headRecoilPush = new Vector3(local.x * 0.07f, 0.02f, local.z * 0.09f) * strength;
                _headRecoilAngles = new Vector3(-22f, local.x * 16f, -local.x * 10f) * strength;
            }
            else
            {
                _recoilPush = new Vector3(local.x * 0.03f, -0.05f, local.z * 0.045f) * strength;
                _recoilAngles = new Vector3(13f, local.x * 4f, 0f) * strength;
                _headRecoilPush = new Vector3(0f, -0.03f, 0.04f) * strength;
                _headRecoilAngles = new Vector3(10f, 0f, 0f) * strength;
            }

            _recoilDuration = RecoilSeconds * (0.7f + strength * 0.6f);
            _recoilTimer = _recoilDuration;
        }

        static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inv = 1f - t;
            return inv * inv * a + 2f * inv * t * b + t * t * c;
        }
    }
}

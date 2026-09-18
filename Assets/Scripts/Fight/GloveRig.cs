using UnityEngine;

namespace TheFighter
{
    /// Drives both gloves: where they rest, how they mirror for southpaw, and the path each punch
    /// travels. The fighter refreshes it every frame *before* hit tests, so the glove you see is
    /// literally the thing that does the hitting.
    ///
    /// First person gets its own rest poses. In the Godot build the gloves sat at body height and
    /// swallowed most of the screen, so here they idle low at the bottom edge and only come up to
    /// frame the view when the guard goes up. Punches ignore the rest pose past the halfway mark
    /// and converge onto the real trajectory, so first and third person land identically.
    public class GloveRig : MonoBehaviour
    {
        [Header("References")]
        public Fighter Owner;
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

        [Header("Trajectory")]
        public Vector3 ShoulderLocal = new Vector3(0.20f, 1.38f, 0.05f);
        public float BodyTargetHeight = 1.02f;
        public float HeadTargetHeight = 1.54f;
        public float RestResponsiveness = 16f;

        bool _firstPerson;
        bool _scalesCaptured;
        Vector3 _leftBaseScale = Vector3.one;
        Vector3 _rightBaseScale = Vector3.one;

        /// The rig is wired up after AddComponent, so Awake is too early to read the gloves' scale.
        void CaptureScales()
        {
            if (_scalesCaptured || LeftGlove == null || RightGlove == null)
            {
                return;
            }
            _leftBaseScale = LeftGlove.localScale;
            _rightBaseScale = RightGlove.localScale;
            _scalesCaptured = true;
        }

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
            CaptureScales();
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

        public void Refresh(float deltaTime)
        {
            if (Owner == null)
            {
                return;
            }

            CaptureScales();
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
                return new Vector3(sign * 0.34f, 0.26f, 0.14f);
            }

            bool guarding = Owner.IsGuarding && !Owner.GuardBroken;
            bool lead = role == HandRole.Lead;

            Vector3 pose;
            if (_firstPerson)
            {
                if (guarding)
                {
                    pose = lead ? FirstPersonGuardLead : FirstPersonGuardRear;
                }
                else
                {
                    pose = lead ? FirstPersonIdleLead : FirstPersonIdleRear;
                }
            }
            else
            {
                if (guarding)
                {
                    pose = lead ? ThirdPersonGuardLead : ThirdPersonGuardRear;
                }
                else
                {
                    pose = lead ? ThirdPersonIdleLead : ThirdPersonIdleRear;
                }
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

        static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inv = 1f - t;
            return inv * inv * a + 2f * inv * t * b + t * t * c;
        }
    }
}

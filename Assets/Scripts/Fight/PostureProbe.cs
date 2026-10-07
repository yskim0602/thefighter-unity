using UnityEngine;

namespace TheFighter
{
    /// Measures the posture failures CLAUDE.md forbids, live, as numbers.
    ///
    /// Every one of them - standing up on a punch, the height popping, feet floating or sinking,
    /// the body snapping to origin, no weight transfer - is something you catch by *looking* and
    /// then cannot describe precisely enough to fix. So this reads them off the skeleton instead:
    /// press P during a fight, run the ten tests, and read what actually happened.
    ///
    /// It also keeps the worst value seen since the last reset, because the bad frame is usually
    /// one frame in the middle of a punch and it is gone before you can look at it.
    ///
    /// Debug only. Nothing here affects the fight, and it is safe to leave off.
    public class PostureProbe : MonoBehaviour
    {
        public Fighter Target;
        public KeyCode Toggle = KeyCode.P;
        /// Not O: that is the uppercut.
        public KeyCode Reset = KeyCode.F1;
        public bool Visible;

        [Header("Thresholds (metres / degrees)")]
        /// How far the hips may sit from where the current stance says they should.
        public float HipDriftWarn = 0.03f;
        public float HipDriftFail = 0.06f;
        /// How far an ankle may stray from its resting height.
        public float FootDriftWarn = 0.02f;
        public float FootDriftFail = 0.05f;
        /// A jump this big in one frame is a snap, not motion.
        public float SnapWarn = 0.04f;
        /// Pelvis and chest are *supposed* to diverge on a punch - that is the torso turning. Only
        /// an absurd gap means the layers are fighting rather than connected.
        public float TwistFail = 55f;
        /// Below this much commitment a punch had no body behind it.
        public float CommitmentFloor = 0.05f;

        Animator _animator;
        CrouchPose _crouch;
        FighterAnimation _animation;
        Transform _hips;
        Transform _chest;
        Transform _leftFoot;
        Transform _rightFoot;
        bool _resolved;

        // Learned while standing still and upright: the only honest reference for "where should
        // this bone be", since it depends on the model and the idle clip.
        float _stanceHip;
        float _stanceAnkle;
        bool _learned;

        float _hipDrift;
        float _footDrift;
        float _snap;
        float _twist;
        Vector3 _lastHipLocal;
        bool _hasLastHip;

        // Per-punch
        bool _punchActive;
        float _crouchAtStart;
        float _crouchLost;
        float _peakCommit;
        float _peakLunge;
        /// How far the glove was from where it was aimed, at the moment of furthest extension.
        /// "The punch goes off to the left" is a sentence; this is the distance.
        float _aimError = -1f;
        float _worstAim;
        Vector3 _pivotAtStart;

        // Worst since reset
        float _worstHip;
        float _worstFoot;
        float _worstSnap;
        float _worstCrouchLost;
        int _punchesWithoutDrive;
        int _punchesSeen;

        float _scale = 1f;
        GUIStyle _mono;

        void Resolve()
        {
            if (_resolved)
            {
                return;
            }
            _resolved = true;

            if (Target == null)
            {
                return;
            }

            _crouch = Target.GetComponent<CrouchPose>();
            _animation = Target.GetComponent<FighterAnimation>();
            _animator = Target.GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman)
            {
                return;
            }

            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            if (_chest == null)
            {
                _chest = _animator.GetBoneTransform(HumanBodyBones.Spine);
            }
            _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        void Update()
        {
            if (Input.GetKeyDown(Toggle))
            {
                Visible = !Visible;
            }
            if (Input.GetKeyDown(Reset))
            {
                ResetWorst();
            }
        }

        /// Sampled after everything that poses the skeleton, which is the only frame position that
        /// sees what the player sees.
        void LateUpdate()
        {
            Resolve();
            if (Target == null || _hips == null)
            {
                return;
            }

            Transform root = Target.transform;
            Vector3 hipLocal = root.InverseTransformPoint(_hips.position);

            bool punching = Target.ActivePunch != null;
            bool still = Target.Motor == null || Target.Motor.PlanarSpeed < 0.08f;
            bool upright = Target.CrouchAmount < 0.02f && Mathf.Abs(Target.LeanAmount) < 0.02f;
            bool floored = Target.State == ActionState.Down || Target.State == ActionState.KnockedOut;

            // Learn the reference only from a fighter standing quietly and upright. Anything else
            // and the reference would absorb the very drift we are trying to measure.
            if (!punching && !floored && still && upright)
            {
                float ankle = AnkleHeight(root);
                _stanceHip = _learned ? Mathf.Lerp(_stanceHip, hipLocal.y, 0.05f) : hipLocal.y;
                _stanceAnkle = _learned ? Mathf.Lerp(_stanceAnkle, ankle, 0.05f) : ankle;
                _learned = true;
            }

            if (!_learned || floored)
            {
                _hasLastHip = false;
                return;
            }

            // Where the hips *should* be: the stance, lowered by exactly as much as the player is
            // ducking. Anything else is the punch overriding the posture.
            float drop = _crouch != null ? _crouch.HipDrop : 0.2f;
            float expected = _stanceHip - Target.CrouchAmount * drop;
            _hipDrift = hipLocal.y - expected;

            _footDrift = AnkleHeight(root) - _stanceAnkle;

            _snap = _hasLastHip ? (hipLocal - _lastHipLocal).magnitude : 0f;
            _lastHipLocal = hipLocal;
            _hasLastHip = true;

            _twist = TwistDegrees(root);

            TrackPunch(punching, root);

            _worstHip = Mathf.Max(_worstHip, Mathf.Abs(_hipDrift));
            _worstFoot = Mathf.Max(_worstFoot, Mathf.Abs(_footDrift));
            _worstSnap = Mathf.Max(_worstSnap, _snap);
        }

        void TrackPunch(bool punching, Transform root)
        {
            if (punching && !_punchActive)
            {
                _punchActive = true;
                _crouchAtStart = Target.CrouchAmount;
                _crouchLost = 0f;
                _peakCommit = 0f;
                _peakLunge = 0f;
                _pivotAtStart = Target.Rig != null && Target.Rig.BodyPivot != null
                    ? Target.Rig.BodyPivot.localPosition : Vector3.zero;
                _aimError = -1f;
                _punchesSeen++;
            }

            if (punching)
            {
                // Only counts as lost if the player is still asking for it - releasing the key
                // mid-punch is them standing up, not the animation doing it.
                _crouchLost = Mathf.Max(_crouchLost, _crouchAtStart - Target.CrouchAmount);

                if (Target.Rig != null)
                {
                    // Sampled at full extension, where the aim is meant to have won.
                    if (Target.PunchTrack > 0.85f && Target.Opponent != null)
                    {
                        Vector3 glove = Target.Rig.GetGloveWorldPosition(Target.ActiveHand);
                        Vector3 aimed = Target.Opponent.HeadHurtbox != null
                            ? Target.Opponent.HeadHurtbox.position
                            : Target.Opponent.transform.position
                                + Vector3.up * CombatTuning.HeadHurtboxHeight;
                        Vector3 low = Target.Opponent.transform.position
                            + Vector3.up * CombatTuning.BodyHurtboxCentre;
                        aimed = Vector3.Lerp(low, aimed, Mathf.Clamp01(Target.AimHeight));

                        float error = Vector3.Distance(glove, aimed);
                        _aimError = _aimError < 0f ? error : Mathf.Min(_aimError, error);
                    }

                    _peakCommit = Mathf.Max(_peakCommit, Target.Rig.Commitment);
                    if (Target.Rig.BodyPivot != null)
                    {
                        _peakLunge = Mathf.Max(_peakLunge,
                            (Target.Rig.BodyPivot.localPosition - _pivotAtStart).magnitude);
                    }
                }
                return;
            }

            if (!_punchActive)
            {
                return;
            }

            _punchActive = false;
            _worstCrouchLost = Mathf.Max(_worstCrouchLost, _crouchLost);
            if (_aimError >= 0f)
            {
                _worstAim = Mathf.Max(_worstAim, _aimError);
            }
            if (_peakCommit < CommitmentFloor || _peakLunge < 0.001f)
            {
                _punchesWithoutDrive++;
            }
        }

        float AnkleHeight(Transform root)
        {
            float left = _leftFoot != null ? root.InverseTransformPoint(_leftFoot.position).y : 0f;
            float right = _rightFoot != null ? root.InverseTransformPoint(_rightFoot.position).y : 0f;
            if (_leftFoot == null && _rightFoot == null)
            {
                return 0f;
            }
            if (_leftFoot == null) { return right; }
            if (_rightFoot == null) { return left; }
            return Mathf.Min(left, right);
        }

        /// How far the chest has turned past the pelvis, around the fighter's own up axis.
        float TwistDegrees(Transform root)
        {
            if (_chest == null)
            {
                return 0f;
            }

            Vector3 up = root.up;
            Vector3 pelvis = Vector3.ProjectOnPlane(_hips.forward, up);
            Vector3 chest = Vector3.ProjectOnPlane(_chest.forward, up);
            if (pelvis.sqrMagnitude < 0.0001f || chest.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Vector3.Angle(pelvis, chest);
        }

        public void ResetWorst()
        {
            _worstHip = 0f;
            _worstFoot = 0f;
            _worstSnap = 0f;
            _worstCrouchLost = 0f;
            _worstAim = 0f;
            _punchesWithoutDrive = 0;
            _punchesSeen = 0;
        }

        // ------------------------------------------------------------------

        void OnGUI()
        {
            if (!Visible || Target == null)
            {
                return;
            }

            _scale = Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            if (_mono == null)
            {
                _mono = new GUIStyle();
                // Columns only line up in a monospace face. Left null if none of these are
                // installed, which falls back to the skin font - ugly, still readable.
                _mono.font = FindMono();
                _mono.normal.textColor = Color.white;
            }
            _mono.fontSize = Mathf.RoundToInt(13 * _scale);

            float w = 430f * _scale;
            float h = 320f * _scale;
            Rect area = new Rect(Screen.width - w - 12f * _scale,
                Screen.height - h - 12f * _scale, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = area.x + 10f * _scale;
            float y = area.y + 8f * _scale;
            float line = 16f * _scale;

            Write(x, ref y, line, "POSTURE PROBE   " + Target.FighterName
                + "   P hide / F1 reset", Color.white);

            if (!_learned)
            {
                Write(x, ref y, line, "learning the stance - stand still and upright a moment",
                    new Color(0.7f, 0.75f, 0.8f));
                return;
            }

            y += 4f * _scale;
            Write(x, ref y, line, string.Format("state    crouch {0:0.00}  lean {1:+0.00;-0.00}  {2}",
                Target.CrouchAmount, Target.LeanAmount,
                Target.ActivePunch != null
                    ? Target.ActivePunch.DisplayName + " " + Target.PunchTrack.ToString("0.00")
                    : "-"), new Color(0.75f, 0.8f, 0.86f));

            y += 4f * _scale;
            Row(x, ref y, line, "hips", _hipDrift, HipDriftWarn, HipDriftFail, "m",
                "stands up / height pops");
            Row(x, ref y, line, "feet", _footDrift, FootDriftWarn, FootDriftFail, "m",
                "floats / sinks");
            Row(x, ref y, line, "snap", _snap, SnapWarn, SnapWarn * 2f, "m",
                "body jumps to origin");

            Color twistColor = _twist > TwistFail
                ? new Color(1f, 0.45f, 0.4f) : new Color(0.55f, 0.85f, 0.6f);
            Write(x, ref y, line, string.Format("twist   {0,7:0.0} deg   pelvis vs chest", _twist),
                twistColor);

            y += 6f * _scale;

            // Four different causes look identical from outside: no clip, a weight that never
            // arrived, a diagonal split across two clips, or a stride near standstill.
            if (_animation != null)
            {
                bool stepping = _animation.ActiveStepWeight > 0.25f;
                bool moving = Target.Motor != null && Target.Motor.PlanarSpeed > 0.15f;
                Write(x, ref y, line, string.Format(
                    "feet    in {0:0.00}  effort {1:0.00}  stride {2:0.00}x  w {3:0.00}  {4}",
                    _animation.FootworkInput, _animation.FootworkEffort,
                    _animation.StrideRate, _animation.ActiveStepWeight,
                    _animation.ActiveStep),
                    moving && !stepping ? new Color(1f, 0.45f, 0.4f)
                        : new Color(0.55f, 0.85f, 0.6f));

                if (moving && !stepping)
                {
                    Write(x, ref y, line, "        MOVING WITHOUT STEPPING", new Color(1f, 0.45f, 0.4f));
                }
            }

            bool droveLast = _peakCommit >= CommitmentFloor;
            Write(x, ref y, line, string.Format("drive   commit {0:0.00}  lunge {1:0.000}m   {2}",
                _peakCommit, _peakLunge, droveLast ? "OK" : "NO WEIGHT TRANSFER"),
                droveLast ? new Color(0.55f, 0.85f, 0.6f) : new Color(1f, 0.45f, 0.4f));

            // Contact needs the glove within about 0.23m of the head centre, so that is the line
            // between "aimed at him" and "aimed past him".
            bool aimed = _aimError >= 0f && _aimError < 0.25f;
            Write(x, ref y, line, string.Format("aim     {0}   {1}",
                _aimError < 0f ? "  --  " : _aimError.ToString("0.000") + "m",
                _aimError < 0f ? "no punch yet" : (aimed ? "on target" : "MISSING THE TARGET")),
                _aimError < 0f ? new Color(0.65f, 0.7f, 0.75f)
                    : aimed ? new Color(0.55f, 0.85f, 0.6f) : new Color(1f, 0.45f, 0.4f));

            Write(x, ref y, line, string.Format("crouch  held {0:0.00} -> lost {1:0.00}   {2}",
                _crouchAtStart, _crouchLost, _crouchLost > 0.05f ? "POSTURE OVERRIDDEN" : "HELD"),
                _crouchLost > 0.05f ? new Color(1f, 0.45f, 0.4f) : new Color(0.55f, 0.85f, 0.6f));

            y += 8f * _scale;
            Write(x, ref y, line, "--- worst since reset ---", new Color(0.65f, 0.7f, 0.75f));
            Write(x, ref y, line, string.Format("hip {0:0.000}m   foot {1:0.000}m   snap {2:0.000}m",
                _worstHip, _worstFoot, _worstSnap), Worst());
            Write(x, ref y, line, string.Format("crouch lost {0:0.00}   punches with no drive {1}/{2}",
                _worstCrouchLost, _punchesWithoutDrive, _punchesSeen), Worst());
            Write(x, ref y, line, string.Format("worst aim error {0:0.000}m", _worstAim), Worst());
        }

        static Font FindMono()
        {
            string[] wanted = { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New", "Monaco" };
            string[] installed = Font.GetOSInstalledFontNames();
            if (installed == null)
            {
                return null;
            }

            for (int w = 0; w < wanted.Length; w++)
            {
                for (int i = 0; i < installed.Length; i++)
                {
                    if (string.Equals(installed[i], wanted[w], System.StringComparison.OrdinalIgnoreCase))
                    {
                        return Font.CreateDynamicFontFromOSFont(installed[i], 13);
                    }
                }
            }

            return null;
        }

        Color Worst()
        {
            bool bad = _worstHip > HipDriftFail || _worstFoot > FootDriftFail
                || _worstCrouchLost > 0.05f || _punchesWithoutDrive > 0;
            return bad ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 0.85f, 0.65f);
        }

        void Row(float x, ref float y, float line, string name, float value,
            float warn, float fail, string unit, string meaning)
        {
            float magnitude = Mathf.Abs(value);
            Color color = magnitude > fail
                ? new Color(1f, 0.45f, 0.4f)
                : magnitude > warn
                    ? new Color(1f, 0.82f, 0.4f)
                    : new Color(0.55f, 0.85f, 0.6f);

            string verdict = magnitude > fail ? "FAIL" : magnitude > warn ? "warn" : "ok";
            Write(x, ref y, line,
                string.Format("{0,-6} {1,7:+0.000;-0.000}{2}  {3,-4}  {4}",
                    name, value, unit, verdict, meaning), color);
        }

        void Write(float x, ref float y, float line, string text, Color color)
        {
            _mono.normal.textColor = color;
            GUI.Label(new Rect(x, y, 420f * _scale, line), text, _mono);
            y += line;
        }
    }
}

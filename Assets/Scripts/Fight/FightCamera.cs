using UnityEngine;

namespace TheFighter
{
    /// The default view is a broadcast camera: it sits ringside behind the player and drifts
    /// slowly along an arc, the way a real operator repositions, instead of locking to one angle.
    /// It breathes in when the fighters close and shoves in on a knockdown.
    ///
    /// The arc is deliberately bounded rather than a full orbit. Footwork is expressed relative to
    /// the opponent, so if the camera swung behind the enemy the player's "circle left" would read
    /// as right on screen. Staying on one side keeps the controls honest - which is also what
    /// ringside cameras do.
    ///
    /// First person stays available on V for testing, and keeps the mouse-pitch aim the keyboard
    /// brain reads. Touch supplies its own aim, so nothing here is required on a phone.
    public class FightCamera : MonoBehaviour
    {
        public enum CameraMode
        {
            Broadcast,
            FirstPerson
        }

        public Camera View;
        public Fighter Player;
        public Fighter Enemy;

        [Header("View")]
        public CameraMode Mode = CameraMode.Broadcast;
        public float FieldOfView = 58f;

        /// Whether the camera holds its angle or wanders.
        ///
        /// Wandering looks like television and plays like a fight with the floor tilting. Footwork
        /// is opponent-relative, so "circle left" is always left in the fighter's own frame - but
        /// if the camera has drifted 22 degrees since you last pressed it, that key now points
        /// somewhere else on screen. The arc swung through 22 degrees of travel, which is enough
        /// that muscle memory cannot form. That is a control problem wearing a cinematography
        /// costume, so Stable is the default and Roaming is for replays and highlights.
        public enum Framing
        {
            /// One angle for the fight, with only enough breath to not look frozen.
            Stable,
            /// The wandering ringside operator. Good to watch, bad to play.
            Roaming
        }

        [Header("Framing")]
        public Framing Shot = Framing.Stable;
        /// The angle Stable holds, off straight-behind-the-player. Far enough round that the
        /// player's own back does not hide the opponent - see OrbitMin.
        public float StableAngle = 27f;
        /// How much it still breathes, in degrees. Small on purpose: this is the difference
        /// between a camera that is alive and a camera that moves the controls.
        public float StableDrift = 2.5f;
        public float StableDriftRate = 0.17f;

        [Header("Broadcast arc")]
        /// How far off straight-behind-the-player the camera wanders. It stays on one side of the
        /// ring for a whole fight, the way a ringside operator does.
        ///
        /// OrbitMin is the important one: at a small angle the camera, the player and the opponent
        /// are nearly collinear, so the line to the opponent's head runs straight through the back
        /// of the player's own head. Height alone cannot fix that - you would have to climb high
        /// enough to look at the tops of their heads. Stepping sideways fixes it at any height,
        /// so the arc simply never passes through centre.
        public float OrbitMin = 22f;
        public float OrbitRange = 44f;
        public float OrbitSpeed = 7f;
        public float OrbitDwell = 2.2f;
        public float NearDistance = 2.45f;
        public float FarDistance = 3.95f;
        public float Height = 2.2f;
        public float HeightDrift = 0.18f;
        public float LookHeight = 1.45f;
        /// 0 frames the pair evenly, 1 centres the opponent. Biased toward the opponent because
        /// that is where the punches you have to read are coming from; your own fighter can sit
        /// nearer the edge of frame.
        [Range(0f, 1f)] public float OpponentBias = 0.5f;
        public float PositionSmoothTime = 0.45f;
        public float RotationSmoothing = 6f;

        [Header("Mouse (keyboard play only)")]
        public bool MouseLook = true;
        public float Sensitivity = 2.4f;
        public float MinPitch = -28f;
        public float MaxPitch = 14f;
        public float MaxYawOffset = 26f;

        [Header("Feel")]
        public float ShakeDecay = 7f;
        public float KickDecay = 9f;
        public float PushDecay = 1.8f;

        float _pitch;
        float _yaw;
        float _shake;
        float _fovKick;
        float _push;

        float _orbit;
        float _orbitSide = 1f;
        float _orbitTarget;
        float _orbitVelocity;
        float _dwellTimer;
        float _heightPhase;
        float _driftPhase;
        Vector3 _positionVelocity;

        public bool FirstPerson
        {
            get { return Mode == CameraMode.FirstPerson; }
        }

        /// 0 = the punch goes to the body, 1 = to the head. Look down to dig to the body.
        /// Only the keyboard brain reads this; TouchBrain decides aim from where you tap.
        public float AimHeight
        {
            get { return Mathf.InverseLerp(-20f, -3f, _pitch); }
        }

        void Awake()
        {
            if (View == null)
            {
                View = GetComponent<Camera>();
            }
            if (View != null)
            {
                View.nearClipPlane = 0.04f;
                View.fieldOfView = FieldOfView;
            }

            _heightPhase = Random.value * 10f;
            _orbitSide = Random.value < 0.5f ? -1f : 1f;
            _orbit = _orbitSide * (Shot == Framing.Stable
                ? StableAngle : (OrbitMin + OrbitRange) * 0.5f);
            _driftPhase = Random.value * 10f;
            PickOrbitTarget();
        }

        void Start()
        {
            ApplyMode();
            if (MouseLook)
            {
                LockCursor(true);
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (Input.GetKeyDown(KeyCode.V))
            {
                Mode = Mode == CameraMode.Broadcast ? CameraMode.FirstPerson : CameraMode.Broadcast;
                ApplyMode();
            }

            if (MouseLook)
            {
                UpdateMouse();
            }

            _shake = Mathf.MoveTowards(_shake, 0f, ShakeDecay * dt);
            _fovKick = Mathf.MoveTowards(_fovKick, 0f, KickDecay * dt);
            _push = Mathf.MoveTowards(_push, 0f, PushDecay * dt);

            UpdateOrbit(dt);
        }

        void UpdateMouse()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                LockCursor(false);
            }
            else if (!CursorLocked && Input.GetMouseButtonDown(0))
            {
                LockCursor(true);
            }

            if (!CursorLocked)
            {
                return;
            }

            _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * Sensitivity, MinPitch, MaxPitch);
            _yaw = Mathf.Clamp(_yaw + Input.GetAxisRaw("Mouse X") * Sensitivity, -MaxYawOffset, MaxYawOffset);
            _yaw = Mathf.MoveTowards(_yaw, 0f, 22f * Time.unscaledDeltaTime);
        }

        void UpdateOrbit(float dt)
        {
            _heightPhase += dt * 0.35f;

            if (Shot == Framing.Stable)
            {
                _driftPhase += dt * StableDriftRate * Mathf.PI * 2f;
                _orbitTarget = _orbitSide * (StableAngle + Mathf.Sin(_driftPhase) * StableDrift);
                _orbit = Mathf.SmoothDamp(_orbit, _orbitTarget, ref _orbitVelocity, 0.8f,
                    OrbitSpeed, dt);
                return;
            }

            _dwellTimer -= dt;
            _orbit = Mathf.SmoothDamp(_orbit, _orbitTarget, ref _orbitVelocity, 1.6f, OrbitSpeed, dt);

            if (_dwellTimer <= 0f && Mathf.Abs(_orbit - _orbitTarget) < 2.5f)
            {
                PickOrbitTarget();
            }
        }

        void PickOrbitTarget()
        {
            // One side for the whole fight, and never nearer centre than OrbitMin. Crossing over
            // would both put the player back in front of the camera and flip the controls on
            // screen: footwork is opponent-relative, so from behind the opponent "circle left"
            // would read as right.
            float low = Mathf.Min(OrbitMin, OrbitRange);
            float high = Mathf.Max(OrbitMin, OrbitRange);
            float span = high - low;

            // Always move somewhere meaningfully different, so the drift never stalls mid-arc.
            float pick = Random.Range(low, high);
            if (span > 0.01f && Mathf.Abs(pick - Mathf.Abs(_orbit)) < span * 0.4f)
            {
                pick = Mathf.Abs(_orbit) - low < span * 0.5f
                    ? Random.Range(low + span * 0.5f, high)
                    : Random.Range(low, low + span * 0.5f);
            }

            _orbitTarget = _orbitSide * pick;
            _dwellTimer = OrbitDwell * Random.Range(0.7f, 1.5f);
        }

        void LateUpdate()
        {
            if (View == null || Player == null)
            {
                return;
            }

            if (Mode == CameraMode.FirstPerson)
            {
                PlaceFirstPerson();
            }
            else
            {
                PlaceBroadcast();
            }

            if (_shake > 0.0001f)
            {
                View.transform.position += Random.insideUnitSphere * _shake * 0.12f;
            }

            View.fieldOfView = FieldOfView + _fovKick;
        }

        void PlaceFirstPerson()
        {
            Transform eye = Player.EyeAnchor != null ? Player.EyeAnchor : Player.transform;
            View.transform.position = eye.position;
            View.transform.rotation = Player.transform.rotation * Quaternion.Euler(-_pitch, _yaw, 0f);
            _positionVelocity = Vector3.zero;
        }

        void PlaceBroadcast()
        {
            Vector3 playerPos = Player.transform.position;
            Vector3 enemyPos = Enemy != null ? Enemy.transform.position : playerPos + Player.transform.forward * 1.6f;

            Vector3 mid = (playerPos + enemyPos) * 0.5f;
            Vector3 framed = Vector3.Lerp(mid, enemyPos, OpponentBias * 0.5f);

            Vector3 toEnemy = enemyPos - playerPos;
            toEnemy.y = 0f;
            float separation = toEnemy.magnitude;
            if (separation < 0.0001f)
            {
                toEnemy = Player.transform.forward;
                separation = 1f;
            }
            toEnemy /= Mathf.Max(0.0001f, separation);

            float distance = Mathf.Lerp(NearDistance, FarDistance,
                Mathf.InverseLerp(0.7f, 2.2f, separation)) - _push;
            distance = Mathf.Max(1.4f, distance);

            Vector3 behind = Quaternion.AngleAxis(_orbit, Vector3.up) * -toEnemy;
            float height = Height + Mathf.Sin(_heightPhase) * HeightDrift - _push * 0.25f;

            Vector3 target = mid + behind * distance + Vector3.up * Mathf.Max(0.6f, height);
            View.transform.position = Vector3.SmoothDamp(View.transform.position, target,
                ref _positionVelocity, PositionSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

            Vector3 look = framed + Vector3.up * LookHeight;
            Fighter down = DownedFighter();
            if (down != null)
            {
                look = Vector3.Lerp(look, down.transform.position + Vector3.up * 0.5f, 0.6f);
            }

            Quaternion wanted = Quaternion.LookRotation(look - View.transform.position, Vector3.up);
            View.transform.rotation = Quaternion.Slerp(View.transform.rotation, wanted,
                1f - Mathf.Exp(-RotationSmoothing * Time.unscaledDeltaTime));
        }

        Fighter DownedFighter()
        {
            if (Player != null && (Player.State == ActionState.Down || Player.State == ActionState.KnockedOut))
            {
                return Player;
            }
            if (Enemy != null && (Enemy.State == ActionState.Down || Enemy.State == ActionState.KnockedOut))
            {
                return Enemy;
            }
            return null;
        }

        void ApplyMode()
        {
            if (Player != null)
            {
                Player.SetFirstPerson(Mode == CameraMode.FirstPerson);
            }
        }

        public void Shake(float amount)
        {
            _shake = Mathf.Max(_shake, amount);
        }

        public void Kick(float degrees)
        {
            _fovKick = Mathf.Max(_fovKick, degrees);
        }

        /// Menus need the pointer back, so this both stops reading the mouse and releases the
        /// cursor - setting MouseLook alone leaves it captured.
        public void SetMouseLook(bool enabled)
        {
            MouseLook = enabled;
            LockCursor(enabled);
        }

        /// Shoves the broadcast camera in closer for a moment - knockdowns, big counters.
        public void PushIn(float metres)
        {
            _push = Mathf.Max(_push, metres);
        }

        static bool CursorLocked
        {
            get { return Cursor.lockState == CursorLockMode.Locked; }
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}

using UnityEngine;

namespace TheFighter
{
    /// First person is the default view - it is the one the prototype exists to test. The body
    /// always squares up to the opponent on its own, so the mouse only moves the *head*: pitch
    /// picks head or body as your target, yaw is a small clamped lean that never fights the
    /// auto-facing.
    public class FightCamera : MonoBehaviour
    {
        public Camera View;
        public Fighter Player;
        public Fighter Enemy;

        [Header("View")]
        public bool FirstPerson = true;
        public float FieldOfView = 62f;
        public float ThirdPersonDistance = 3.4f;
        public float ThirdPersonHeight = 2.05f;

        [Header("Mouse")]
        public float Sensitivity = 2.4f;
        public float MinPitch = -28f;
        public float MaxPitch = 14f;
        public float MaxYawOffset = 26f;

        [Header("Feel")]
        public float ShakeDecay = 7f;
        public float KickDecay = 9f;

        float _pitch;
        float _yaw;
        float _shake;
        float _fovKick;

        /// 0 = the punch goes to the body, 1 = to the head. Look down to dig to the body.
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
        }

        void Start()
        {
            ApplyViewMode();
            LockCursor(true);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.V))
            {
                FirstPerson = !FirstPerson;
                ApplyViewMode();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                LockCursor(false);
            }
            else if (!CursorLocked && Input.GetMouseButtonDown(0))
            {
                LockCursor(true);
            }

            if (CursorLocked)
            {
                _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * Sensitivity, MinPitch, MaxPitch);
                _yaw = Mathf.Clamp(_yaw + Input.GetAxisRaw("Mouse X") * Sensitivity, -MaxYawOffset, MaxYawOffset);
                _yaw = Mathf.MoveTowards(_yaw, 0f, 22f * Time.unscaledDeltaTime);
            }

            _shake = Mathf.MoveTowards(_shake, 0f, ShakeDecay * Time.unscaledDeltaTime);
            _fovKick = Mathf.MoveTowards(_fovKick, 0f, KickDecay * Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            if (View == null || Player == null)
            {
                return;
            }

            if (FirstPerson)
            {
                PlaceFirstPerson();
            }
            else
            {
                PlaceThirdPerson();
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
        }

        void PlaceThirdPerson()
        {
            Vector3 playerPos = Player.transform.position;
            Vector3 enemyPos = Enemy != null ? Enemy.transform.position : playerPos + Player.transform.forward * 2f;

            Vector3 mid = (playerPos + enemyPos) * 0.5f;
            Vector3 away = playerPos - enemyPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = -Player.transform.forward;
            }
            away.Normalize();

            Vector3 target = mid + away * ThirdPersonDistance + Vector3.up * ThirdPersonHeight;
            View.transform.position = Vector3.Lerp(View.transform.position, target,
                1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));
            View.transform.rotation = Quaternion.LookRotation((mid + Vector3.up * 1.15f) - View.transform.position);
        }

        void ApplyViewMode()
        {
            if (Player != null)
            {
                Player.SetFirstPerson(FirstPerson);
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

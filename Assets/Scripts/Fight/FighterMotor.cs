using UnityEngine;

namespace TheFighter
{
    /// Footwork only. The fighter always squares up to the opponent, so movement is expressed
    /// relative to that facing: y steps in and out of range, x circles. First and third person
    /// therefore control identically, which is what made the Godot first-person view confusing.
    [RequireComponent(typeof(CharacterController))]
    public class FighterMotor : MonoBehaviour
    {
        public float TurnSpeed = 12f;
        public float Acceleration = 14f;

        CharacterController _controller;
        Vector3 _velocity;
        Vector3 _impulse;
        float _verticalSpeed;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public void FaceTowards(Vector3 worldPoint, float deltaTime)
        {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0004f)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-TurnSpeed * deltaTime));
        }

        public void Tick(Vector2 move, float speed, float deltaTime)
        {
            Vector3 wanted = (transform.right * move.x + transform.forward * move.y) * speed;
            _velocity = Vector3.Lerp(_velocity, wanted, 1f - Mathf.Exp(-Acceleration * deltaTime));

            _impulse = Vector3.Lerp(_impulse, Vector3.zero, 1f - Mathf.Exp(-6f * deltaTime));

            if (_controller.isGrounded && _verticalSpeed < 0f)
            {
                _verticalSpeed = -2f;
            }
            _verticalSpeed += Physics.gravity.y * deltaTime;

            Vector3 delta = (_velocity + _impulse) * deltaTime;
            delta.y = _verticalSpeed * deltaTime;
            _controller.Move(delta);

            ClampToRing();
        }

        public void AddImpulse(Vector3 impulse)
        {
            _impulse += impulse;
        }

        public void Stop()
        {
            _velocity = Vector3.zero;
            _impulse = Vector3.zero;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Stop();
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
        }

        void ClampToRing()
        {
            float limit = CombatTuning.RingHalfExtent;
            Vector3 p = transform.position;
            float clampedX = Mathf.Clamp(p.x, -limit, limit);
            float clampedZ = Mathf.Clamp(p.z, -limit, limit);
            if (!Mathf.Approximately(clampedX, p.x) || !Mathf.Approximately(clampedZ, p.z))
            {
                transform.position = new Vector3(clampedX, p.y, clampedZ);
            }
        }
    }
}

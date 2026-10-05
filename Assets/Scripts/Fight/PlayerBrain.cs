using UnityEngine;

namespace TheFighter
{
    /// Turns the keyboard and mouse into the same FighterIntent the AI produces. Movement is
    /// relative to the opponent, so W always means "step in" whichever view you are in.
    public class PlayerBrain : MonoBehaviour, IFighterBrain
    {
        public FightCamera CameraRig;

        Fighter _self;

        public void Attach(Fighter self, Fighter opponent)
        {
            _self = self;
        }

        public FighterIntent Think(float deltaTime)
        {
            FighterIntent intent = FighterIntent.Neutral();

            if (_self != null && _self.IsFinished)
            {
                return intent;
            }

            float x = 0f;
            float y = 0f;
            if (Input.GetKey(KeyCode.W)) { y += 1f; }
            if (Input.GetKey(KeyCode.S)) { y -= 1f; }
            if (Input.GetKey(KeyCode.D)) { x += 1f; }
            if (Input.GetKey(KeyCode.A)) { x -= 1f; }
            intent.Move = new Vector2(x, y);

            intent.Guard = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.L);
            intent.Dodge = Input.GetKeyDown(KeyCode.Space);

            // Head movement, held rather than tapped: Q and E lean, C ducks.
            float lean = 0f;
            if (Input.GetKey(KeyCode.Q)) { lean -= 1f; }
            if (Input.GetKey(KeyCode.E)) { lean += 1f; }
            intent.Lean = lean;
            intent.Crouch = Input.GetKey(KeyCode.C);
            intent.AimHeight = CameraRig != null ? CameraRig.AimHeight : 1f;

            PunchKind kind;
            if (TryReadPunch(out kind))
            {
                intent.ThrowPunch = true;
                intent.Punch = kind;
                intent.MashGetUp = true;
            }

            return intent;
        }

        static bool TryReadPunch(out PunchKind kind)
        {
            bool mouseUsable = Cursor.lockState == CursorLockMode.Locked;

            if (Input.GetKeyDown(KeyCode.J) || (mouseUsable && Input.GetMouseButtonDown(0)))
            {
                kind = PunchKind.Jab;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.K) || (mouseUsable && Input.GetMouseButtonDown(1)))
            {
                kind = PunchKind.Straight;
                return true;
            }
            // Q and E used to alias hook and uppercut; they lean now.
            if (Input.GetKeyDown(KeyCode.I))
            {
                kind = PunchKind.Hook;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.O))
            {
                kind = PunchKind.Uppercut;
                return true;
            }

            kind = PunchKind.Jab;
            return false;
        }
    }
}

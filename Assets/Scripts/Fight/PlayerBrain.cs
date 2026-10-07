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
            ClipSide hand;
            if (TryReadPunch(out kind, out hand))
            {
                intent.ThrowPunch = true;
                intent.Punch = kind;
                intent.Hand = hand;
                intent.MashGetUp = true;
            }

            return intent;
        }

        /// Six punches on two columns, laid out the way the hands are: the left column is the
        /// left arm, the right column the right arm, and the row is the punch.
        ///
        ///     U  I     left hook      right hook
        ///     J  K     jab            straight
        ///     N  M     left uppercut  right uppercut
        ///
        /// The hooks and uppercuts name an *arm*, not a role, so the key means the same glove in
        /// both stances. The jab and the straight are one hand each by definition - the jab is
        /// the lead hand, which is the right one in southpaw - so they stay roles and stay on the
        /// mouse buttons.
        static bool TryReadPunch(out PunchKind kind, out ClipSide hand)
        {
            bool mouseUsable = Cursor.lockState == CursorLockMode.Locked;

            if (Input.GetKeyDown(KeyCode.J) || (mouseUsable && Input.GetMouseButtonDown(0)))
            {
                kind = PunchKind.Jab;
                hand = ClipSide.Auto;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.K) || (mouseUsable && Input.GetMouseButtonDown(1)))
            {
                kind = PunchKind.Straight;
                hand = ClipSide.Auto;
                return true;
            }

            if (Input.GetKeyDown(KeyCode.U))
            {
                kind = PunchKind.Hook;
                hand = ClipSide.Left;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.I))
            {
                kind = PunchKind.Hook;
                hand = ClipSide.Right;
                return true;
            }

            if (Input.GetKeyDown(KeyCode.N))
            {
                kind = PunchKind.Uppercut;
                hand = ClipSide.Left;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.O))
            {
                kind = PunchKind.Uppercut;
                hand = ClipSide.Right;
                return true;
            }

            kind = PunchKind.Jab;
            hand = ClipSide.Auto;
            return false;
        }
    }
}

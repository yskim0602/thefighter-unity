using UnityEngine;

namespace TheFighter
{
    /// What a brain wants to do this frame. Keeping it a plain struct is the whole point of the
    /// Unity rebuild: the player and the AI feed identical data into identical fight code, so a
    /// fighter can swap brains at runtime (AI vs AI balance runs, replays, sparring modes).
    public struct FighterIntent
    {
        /// x = circle left/right, y = step in/out. Always relative to the opponent.
        public Vector2 Move;
        public bool Guard;
        public bool Dodge;
        public bool ThrowPunch;
        public PunchKind Punch;
        /// 0 = aiming at the body, 1 = aiming at the head.
        public float AimHeight;
        /// One pulse per button press while grounded, to beat the count.
        public bool MashGetUp;

        public static FighterIntent Neutral()
        {
            FighterIntent intent = new FighterIntent();
            intent.AimHeight = 1f;
            return intent;
        }
    }

    public interface IFighterBrain
    {
        void Attach(Fighter self, Fighter opponent);
        FighterIntent Think(float deltaTime);
    }
}

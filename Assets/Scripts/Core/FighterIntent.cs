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
        /// Which arm, for a punch that has one of each (hook, uppercut). A *side*, not a
        /// Lead/Rear role, because this comes from a key the player associates with an arm -
        /// and in southpaw the left hook is the rear hook. Auto leaves the choice to the fight,
        /// which is what the AI and the touch pad use.
        public ClipSide Hand;
        /// Throws the windup and nothing else. The hand never comes, so all it buys is whatever
        /// the other fighter does about it - which is the whole idea.
        public bool Feint;
        /// 0 = aiming at the body, 1 = aiming at the head.
        public float AimHeight;
        /// Head movement. -1 leans left, +1 right. Moves the head hurtbox, not the feet.
        public float Lean;
        /// Ducking. Also moves the head hurtbox, so it really does take you under a punch.
        public bool Crouch;
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

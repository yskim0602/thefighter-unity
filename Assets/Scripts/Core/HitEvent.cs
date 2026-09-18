using UnityEngine;

namespace TheFighter
{
    /// One resolved punch. Feedback (hit stop, shake, sound) and the HUD all read this instead of
    /// reaching back into the fighters.
    public struct HitEvent
    {
        public Fighter Attacker;
        public Fighter Defender;
        public PunchDefinition Punch;
        public HitZone Zone;
        public HitResult Result;
        public Vector3 Point;
        public float Damage;
        public bool Counter;
        public bool CausedKnockdown;
    }
}

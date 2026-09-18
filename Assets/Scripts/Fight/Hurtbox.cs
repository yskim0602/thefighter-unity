using UnityEngine;

namespace TheFighter
{
    /// A trigger volume that says "this part of this fighter can be hit, and it counts as head/body".
    /// Punches resolve by overlapping the glove against these, so where you aim is where you land -
    /// the Godot build rolled a dice for head vs body instead.
    public class Hurtbox : MonoBehaviour
    {
        public Fighter Owner;
        public HitZone Zone;
    }
}

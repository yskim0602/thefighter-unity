using UnityEngine;

namespace TheFighter
{
    /// A trigger volume that says "this part of this fighter can be hit, and it counts as head/body".
    /// Punches resolve by sweeping the glove against these, so where you aim is where you land -
    /// the Godot build rolled a dice for head vs body instead.
    ///
    /// These are the fighter's silhouette, deliberately: a volume bigger than the body is a punch
    /// that lands in open air. CombatTuning holds the sizes and Fighter.EffectiveRange reads the
    /// same constants, so range and contact cannot drift apart.
    public class Hurtbox : MonoBehaviour
    {
        public Fighter Owner;
        public HitZone Zone;

        /// Draws the real volume in the Scene view, during play too. The sizes only matter relative
        /// to the model standing inside them, and that is not something you can check by reading a
        /// number - you have to see whether the box is wearing the fighter or swallowing him.
        void OnDrawGizmos()
        {
            Gizmos.color = Zone == HitZone.Head
                ? new Color(1f, 0.45f, 0.2f, 0.75f)
                : new Color(0.3f, 0.7f, 1f, 0.6f);

            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere != null)
            {
                Gizmos.DrawWireSphere(transform.TransformPoint(sphere.center),
                    sphere.radius * transform.lossyScale.x);
                return;
            }

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                return;
            }

            // Gizmos cannot draw a capsule, so the two caps and the span between them stand in.
            float scale = transform.lossyScale.x;
            float radius = capsule.radius * scale;
            float half = Mathf.Max(0f, capsule.height * 0.5f * scale - radius);
            Vector3 axis = capsule.direction == 0 ? transform.right
                : capsule.direction == 1 ? transform.up : transform.forward;
            Vector3 centre = transform.TransformPoint(capsule.center);

            Gizmos.DrawWireSphere(centre + axis * half, radius);
            Gizmos.DrawWireSphere(centre - axis * half, radius);
            Gizmos.DrawLine(centre + axis * half, centre - axis * half);
        }
    }
}

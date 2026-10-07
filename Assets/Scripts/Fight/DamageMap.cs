using UnityEngine;

namespace TheFighter
{
    /// Shows where damage has gone, on the body it went into. Test tooling, toggled with B.
    ///
    /// One health bar says how much is left and nothing about where it went, but in boxing where
    /// it went is the whole story: a man worked downstairs all night and a man caught on the chin
    /// are in completely different trouble, and the bar reads the same for both. Until a real
    /// model can show that as swelling and a closed eye, this stands in for it.
    ///
    /// Markers sit on the hurtboxes themselves, so they also say plainly whether the volumes are
    /// where you think they are.
    public class DamageMap : MonoBehaviour
    {
        public Fighter Player;
        public Fighter Enemy;
        public Camera View;
        public KeyCode Toggle = KeyCode.B;
        public bool Visible;

        /// Damage at which a region is drawn fully red. Not the health pool: this is "how beaten
        /// up does this look", and a head takes far less than a body before it tells.
        public float HeadReference = 70f;
        public float BodyReference = 120f;

        GUIStyle _label;
        float _scale = 1f;

        void Update()
        {
            if (Input.GetKeyDown(Toggle))
            {
                Visible = !Visible;
            }
        }

        void OnGUI()
        {
            if (!Visible)
            {
                return;
            }

            if (View == null)
            {
                View = Camera.main;
                if (View == null)
                {
                    return;
                }
            }

            _scale = Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            if (_label == null)
            {
                _label = new GUIStyle();
                _label.fontStyle = FontStyle.Bold;
                _label.alignment = TextAnchor.MiddleCenter;
            }
            _label.fontSize = Mathf.RoundToInt(12 * _scale);

            Draw(Player);
            Draw(Enemy);
        }

        void Draw(Fighter fighter)
        {
            if (fighter == null)
            {
                return;
            }

            // The head marker rides the hurtbox, so leaning and ducking move it too - which is
            // also a free check that head movement is really moving the thing that gets hit.
            Vector3 head = fighter.HeadHurtbox != null
                ? fighter.HeadHurtbox.position
                : fighter.transform.position + Vector3.up * CombatTuning.HeadHurtboxHeight;
            Vector3 body = fighter.transform.position
                + Vector3.up * CombatTuning.BodyHurtboxCentre;

            Mark(head, fighter.HeadDamageTaken, HeadReference, fighter.HeadDamageBlocked,
                "HEAD", fighter.HeadTrauma);
            Mark(body, fighter.BodyDamageTaken, BodyReference, fighter.BodyDamageBlocked,
                "BODY", -1f);
        }

        void Mark(Vector3 worldPoint, float taken, float reference, float blocked,
            string name, float trauma)
        {
            Vector3 screen = View.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f)
            {
                return;
            }

            float y = Screen.height - screen.y;
            float wear = Mathf.Clamp01(taken / Mathf.Max(1f, reference));

            // White through amber to red. The ring grows with the damage as well as reddening, so
            // it still reads at a glance on a small screen or in a recording.
            Color colour = wear < 0.5f
                ? Color.Lerp(new Color(1f, 1f, 1f, 0.5f), new Color(1f, 0.78f, 0.2f, 0.8f), wear * 2f)
                : Color.Lerp(new Color(1f, 0.78f, 0.2f, 0.8f), new Color(1f, 0.2f, 0.18f, 0.95f),
                    (wear - 0.5f) * 2f);

            float radius = (9f + wear * 16f) * _scale;
            Ring(new Vector2(screen.x, y), radius, colour);

            string text = name + " " + taken.ToString("0");
            if (blocked > 1f)
            {
                text += "  (blk " + blocked.ToString("0") + ")";
            }
            if (trauma >= 0f)
            {
                text += "\nCHIN " + Mathf.RoundToInt(trauma * 100f) + "%";
            }

            _label.normal.textColor = colour;
            GUI.Label(new Rect(screen.x - 70f * _scale, y + radius, 140f * _scale, 34f * _scale),
                text, _label);
        }

        /// Four arcs rather than a texture, because the HUD is still IMGUI and a ring drawn from
        /// quads needs no asset.
        void Ring(Vector2 centre, float radius, Color colour)
        {
            GUI.color = colour;

            const int segments = 20;
            float thickness = Mathf.Max(2f, 2f * _scale);

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector2 point = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                GUI.DrawTexture(new Rect(point.x - thickness * 0.5f, point.y - thickness * 0.5f,
                    thickness, thickness), Texture2D.whiteTexture);
            }

            GUI.color = Color.white;
        }
    }
}

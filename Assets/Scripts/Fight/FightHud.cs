using UnityEngine;

namespace TheFighter
{
    /// A throwaway IMGUI readout so the prototype can be tuned without building any real UI.
    /// Deliberately ASCII only - Unity's built-in GUI font has no Korean glyphs, and the real
    /// screens come later with their own font asset.
    public class FightHud : MonoBehaviour
    {
        public FightDirector Director;
        public FightCamera CameraRig;

        static readonly Color HealthColor = new Color(0.86f, 0.22f, 0.22f);
        static readonly Color StaminaColor = new Color(0.95f, 0.76f, 0.22f);
        static readonly Color GuardColor = new Color(0.32f, 0.62f, 0.95f);

        GUIStyle _label;
        GUIStyle _title;
        GUIStyle _mid;
        GUIStyle _banner;

        void BuildStyles()
        {
            if (_label != null)
            {
                return;
            }

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 13;
            _label.normal.textColor = Color.white;

            _title = new GUIStyle(_label);
            _title.fontSize = 17;
            _title.fontStyle = FontStyle.Bold;

            _mid = new GUIStyle(_label);
            _mid.fontSize = 20;
            _mid.fontStyle = FontStyle.Bold;
            _mid.alignment = TextAnchor.MiddleCenter;

            _banner = new GUIStyle(_label);
            _banner.fontSize = 34;
            _banner.fontStyle = FontStyle.Bold;
            _banner.alignment = TextAnchor.MiddleCenter;
        }

        void OnGUI()
        {
            if (Director == null || Director.Player == null || Director.Enemy == null)
            {
                return;
            }

            BuildStyles();

            DrawFighterPanel(new Rect(18f, 16f, 330f, 120f), Director.Player);
            DrawFighterPanel(new Rect(Screen.width - 348f, 16f, 330f, 120f), Director.Enemy);

            DrawCrosshair();
            DrawHitReadout();
            DrawCount();
            DrawResult();
            DrawControls();
        }

        void DrawFighterPanel(Rect area, Fighter fighter)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = area.x + 10f;
            float y = area.y + 8f;
            float w = area.width - 20f;

            GUI.Label(new Rect(x, y, w, 22f), fighter.FighterName, _title);
            y += 22f;

            string meta = BoxingStyles.DisplayName(fighter.Style)
                + "  /  " + (fighter.CurrentStance == Stance.Orthodox ? "ORTHODOX" : "SOUTHPAW")
                + "  /  DOWN " + fighter.Knockdowns + "/" + CombatTuning.MaxKnockdowns;
            GUI.Label(new Rect(x, y, w, 18f), meta, _label);
            y += 20f;

            Bar(new Rect(x, y, w, 12f), fighter.HealthRatio, HealthColor);
            y += 16f;
            Bar(new Rect(x, y, w, 8f), fighter.StaminaRatio, StaminaColor);
            y += 12f;
            Bar(new Rect(x, y, w, 6f), fighter.GuardRatio, GuardColor);
            y += 12f;

            GUI.Label(new Rect(x, y, w, 18f), StateLabel(fighter), _label);
        }

        static string StateLabel(Fighter fighter)
        {
            if (fighter.State == ActionState.KnockedOut)
            {
                return "KNOCKED OUT";
            }
            if (fighter.State == ActionState.Down)
            {
                return "DOWN - count " + fighter.DownCountRemaining.ToString("0.0");
            }
            if (fighter.GuardBroken)
            {
                return "GUARD BROKEN";
            }
            if (fighter.IsDodging)
            {
                return "SLIP";
            }
            if (fighter.State == ActionState.Staggered)
            {
                return "STAGGERED";
            }
            if (fighter.ActivePunch != null)
            {
                return fighter.ActivePunch.DisplayName + " - " + fighter.State.ToString().ToUpper();
            }
            if (fighter.IsGuarding)
            {
                return "GUARD UP";
            }
            if (fighter.CounterWindowOpen)
            {
                return "COUNTER READY";
            }
            if (fighter.IsExhausted)
            {
                return "OUT OF GAS";
            }
            return "READY";
        }

        void DrawCrosshair()
        {
            if (CameraRig == null || !CameraRig.FirstPerson)
            {
                return;
            }

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string aim = CameraRig.AimHeight > 0.5f ? "HEAD" : "BODY";
            GUI.Label(new Rect(cx + 14f, cy - 10f, 80f, 20f), aim, _label);
        }

        void DrawHitReadout()
        {
            float age = Director.LastHitAge;
            if (age > 0.9f || Director.LastHit.Punch == null)
            {
                return;
            }

            HitEvent evt = Director.LastHit;
            Color color = evt.Result == HitResult.Clean ? new Color(1f, 0.85f, 0.3f)
                : evt.Result == HitResult.Blocked ? new Color(0.6f, 0.8f, 1f)
                : new Color(0.8f, 0.8f, 0.8f);

            string text = (evt.Counter ? "COUNTER " : "")
                + evt.Punch.DisplayName + " -> " + evt.Zone.ToString().ToUpper()
                + "  " + evt.Result.ToString().ToUpper()
                + "  " + evt.Damage.ToString("0");

            GUIStyle style = new GUIStyle(_title);
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = new Color(color.r, color.g, color.b, 1f - age / 0.9f);

            GUI.Label(new Rect(0f, Screen.height * 0.5f + 44f, Screen.width, 26f), text, style);
        }

        void DrawCount()
        {
            Fighter down = Director.Player.State == ActionState.Down ? Director.Player
                : Director.Enemy.State == ActionState.Down ? Director.Enemy : null;
            if (down == null)
            {
                return;
            }

            string text = down.FighterName + " IS DOWN   " + down.DownCountRemaining.ToString("0.0");
            if (down == Director.Player)
            {
                text += "   -  mash a punch key to beat the count";
            }

            GUI.Label(new Rect(0f, Screen.height * 0.34f, Screen.width, 30f), text, _mid);
        }

        void DrawResult()
        {
            if (!Director.MatchOver || Director.Winner == null)
            {
                return;
            }

            bool playerWon = Director.Winner == Director.Player;
            GUIStyle style = new GUIStyle(_banner);
            style.normal.textColor = playerWon ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 0.4f, 0.4f);

            GUI.Label(new Rect(0f, Screen.height * 0.40f, Screen.width, 50f),
                playerWon ? "WIN BY KO" : "LOSE BY KO", style);
            GUI.Label(new Rect(0f, Screen.height * 0.40f + 54f, Screen.width, 26f),
                "press R for another fight", _mid);
        }

        void DrawControls()
        {
            const string line1 = "WASD step / circle     J or LMB jab     K or RMB straight     I or Q hook     O or E uppercut";
            const string line2 = "Shift (hold) guard     Space slip     mouse aims (look down = body)     V view     R restart     Esc free cursor";

            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0f, Screen.height - 44f, Screen.width, 44f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(18f, Screen.height - 40f, Screen.width - 36f, 18f), line1, _label);
            GUI.Label(new Rect(18f, Screen.height - 22f, Screen.width - 36f, 18f), line2, _label);
        }

        static void Bar(Rect rect, float fill, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}

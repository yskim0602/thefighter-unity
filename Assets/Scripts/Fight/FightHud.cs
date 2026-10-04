using UnityEngine;

namespace TheFighter
{
    /// A tuning readout, not the real UI. IMGUI needs no prefabs or font assets, which is what
    /// makes it right for a balance-and-feel build and wrong for shipping - it allocates every
    /// frame. The real screens arrive with the career layer.
    ///
    /// Health bars are drawn to scale against a shared reference rather than normalised per
    /// fighter: an endurance tank carries a visibly longer bar than a quick one, so you can read
    /// who is built how at a glance instead of inferring it from how fast the bar moves.
    ///
    /// Everything is ASCII - Unity's built-in GUI font has no Korean glyphs.
    public class FightHud : MonoBehaviour
    {
        public FightDirector Director;
        public FightCamera CameraRig;
        public TouchBrain Touch;
        public ImpactFeedback Feedback;

        public bool ShowTouchOverlay = true;

        static readonly Color HealthColor = new Color(0.86f, 0.22f, 0.22f);
        static readonly Color StaminaColor = new Color(0.95f, 0.76f, 0.22f);
        static readonly Color GuardColor = new Color(0.32f, 0.62f, 0.95f);
        static readonly Color ZoneColor = new Color(1f, 1f, 1f, 0.16f);

        GUIStyle _label;
        GUIStyle _small;
        GUIStyle _title;
        GUIStyle _mid;
        GUIStyle _banner;

        float _scale = 1f;
        float _builtScale;

        void BuildStyles()
        {
            if (_label != null && Mathf.Approximately(_builtScale, _scale))
            {
                return;
            }
            _builtScale = _scale;

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = Mathf.RoundToInt(13f * _scale);
            _label.normal.textColor = Color.white;

            _small = new GUIStyle(_label);
            _small.fontSize = Mathf.RoundToInt(11f * _scale);
            _small.normal.textColor = new Color(0.78f, 0.80f, 0.84f);

            _title = new GUIStyle(_label);
            _title.fontSize = Mathf.RoundToInt(17f * _scale);
            _title.fontStyle = FontStyle.Bold;

            _mid = new GUIStyle(_label);
            _mid.fontSize = Mathf.RoundToInt(20f * _scale);
            _mid.fontStyle = FontStyle.Bold;
            _mid.alignment = TextAnchor.MiddleCenter;

            _banner = new GUIStyle(_label);
            _banner.fontSize = Mathf.RoundToInt(34f * _scale);
            _banner.fontStyle = FontStyle.Bold;
            _banner.alignment = TextAnchor.MiddleCenter;
        }

        void OnGUI()
        {
            if (Director == null || Director.Player == null || Director.Enemy == null)
            {
                return;
            }

            // Phones are 2-3x the pixel density of a monitor; without this every label is unreadable.
            _scale = Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            BuildStyles();

            DrawDamageFlash();

            float panelWidth = 330f * _scale;
            float panelHeight = 146f * _scale;
            float margin = 16f * _scale;

            DrawFighterPanel(new Rect(margin, margin, panelWidth, panelHeight), Director.Player);
            DrawFighterPanel(new Rect(Screen.width - panelWidth - margin, margin, panelWidth, panelHeight),
                Director.Enemy);

            DrawRoundBanner();
            DrawTouchZones();
            DrawCrosshair();
            DrawHitReadout();
            DrawCount();
            DrawResult();
            DrawControls();
        }

        /// In a ringside view the camera is not on your face, so a hit you took has to be felt some
        /// other way. A red wash over the whole screen is the cheapest honest way to say "that was
        /// you". Replace with a vignette shader when there is a render pipeline to put it in.
        void DrawDamageFlash()
        {
            if (Feedback == null || Feedback.DamageFlash <= 0.001f)
            {
                return;
            }

            GUI.color = new Color(0.75f, 0.05f, 0.05f, Mathf.Clamp01(Feedback.DamageFlash) * 0.42f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------
        // Round clock and cards
        // ------------------------------------------------------------------

        void DrawRoundBanner()
        {
            if (Director.Phase == MatchPhase.Finished)
            {
                return;
            }

            float w = 280f * _scale;
            float h = 56f * _scale;
            Rect area = new Rect((Screen.width - w) * 0.5f, 0f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            string top;
            string bottom;

            switch (Director.Phase)
            {
                case MatchPhase.Opening:
                    top = "ROUND 1";
                    bottom = "SECONDS OUT";
                    break;
                case MatchPhase.Rest:
                    top = "REST  " + Mathf.Max(0, Mathf.CeilToInt(Director.PhaseRemaining));
                    bottom = "ROUND " + (Director.CurrentRound + 1) + " NEXT";
                    break;
                default:
                    top = Clock(Director.PhaseRemaining);
                    bottom = "ROUND " + Director.CurrentRound + " / " + Director.TotalRounds;
                    break;
            }

            GUI.Label(new Rect(area.x, area.y + 4f * _scale, area.width, 28f * _scale), top, _mid);

            GUIStyle sub = new GUIStyle(_small);
            sub.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(area.x, area.y + 32f * _scale, area.width, 18f * _scale), bottom, sub);

            if (Director.Phase == MatchPhase.Rest)
            {
                DrawScorecard(area.yMax + 8f * _scale);
            }
        }

        /// Three cards, read out the way a ring announcer would. Being behind on them with one
        /// round to go is the whole point of having them.
        void DrawScorecard(float top)
        {
            Scorecard card = Director.Card;
            if (card == null || card.RoundsScored == 0)
            {
                return;
            }

            float w = 280f * _scale;
            float rowHeight = 20f * _scale;
            float h = rowHeight * (card.JudgeCount + 1) + 10f * _scale;
            Rect area = new Rect((Screen.width - w) * 0.5f, top, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle row = new GUIStyle(_small);
            row.alignment = TextAnchor.MiddleCenter;

            float y = area.y + 5f * _scale;
            GUI.Label(new Rect(area.x, y, area.width, rowHeight),
                "AFTER " + card.RoundsScored + " ROUND" + (card.RoundsScored == 1 ? "" : "S"), row);
            y += rowHeight;

            for (int i = 0; i < card.JudgeCount; i++)
            {
                int player = card.PlayerTotal(i);
                int enemy = card.EnemyTotal(i);

                GUIStyle style = new GUIStyle(row);
                style.normal.textColor = player > enemy ? new Color(0.5f, 1f, 0.6f)
                    : enemy > player ? new Color(1f, 0.55f, 0.55f)
                    : Color.white;

                GUI.Label(new Rect(area.x, y, area.width, rowHeight),
                    card.JudgeAt(i).Name + "    " + player + " - " + enemy, style);
                y += rowHeight;
            }
        }

        static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        // ------------------------------------------------------------------
        // Fighter panels
        // ------------------------------------------------------------------

        void DrawFighterPanel(Rect area, Fighter fighter)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float pad = 10f * _scale;
            float x = area.x + pad;
            float y = area.y + 8f * _scale;
            float w = area.width - pad * 2f;

            GUI.Label(new Rect(x, y, w, 22f * _scale), fighter.FighterName, _title);
            y += 22f * _scale;

            string meta = BoxingStyles.DisplayName(fighter.Style)
                + "  /  " + (fighter.CurrentStance == Stance.Orthodox ? "ORTHODOX" : "SOUTHPAW")
                + "  /  DOWN " + fighter.Knockdowns + "/" + CombatTuning.MaxKnockdowns;
            GUI.Label(new Rect(x, y, w, 16f * _scale), meta, _small);
            y += 18f * _scale;

            // The bar's *length* is the fighter's pool; its fill is what is left of it.
            float pool = Mathf.Clamp(fighter.MaxHealth / CombatTuning.HealthBarReference,
                CombatTuning.HealthBarMinFraction, 1f);

            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(new Rect(x, y, w, 13f * _scale), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Bar(new Rect(x, y, w * pool, 13f * _scale), fighter.HealthRatio, HealthColor);
            y += 15f * _scale;

            GUI.Label(new Rect(x, y, w, 14f * _scale),
                "HP " + fighter.Health.ToString("0") + " / " + fighter.MaxHealth.ToString("0"), _small);
            y += 16f * _scale;

            Bar(new Rect(x, y, w * 0.82f, 8f * _scale), fighter.StaminaRatio, StaminaColor);
            y += 11f * _scale;
            Bar(new Rect(x, y, w * 0.82f, 6f * _scale), fighter.GuardRatio, GuardColor);
            y += 11f * _scale;

            FighterStats s = fighter.Stats;
            string stats = "PWR " + s.Power + "   END " + s.Endurance
                + "   SPD " + s.Speed + "   SKL " + s.Skill;
            GUI.Label(new Rect(x, y, w, 14f * _scale), stats, _small);
            y += 15f * _scale;

            GUI.Label(new Rect(x, y, w, 18f * _scale), StateLabel(fighter), _label);
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

        // ------------------------------------------------------------------
        // Touch overlay
        // ------------------------------------------------------------------

        void DrawTouchZones()
        {
            if (Touch == null || !ShowTouchOverlay)
            {
                return;
            }

            Rect move = ToGui(Touch.MoveZone);
            Rect punch = ToGui(Touch.PunchZone);

            Outline(move, ZoneColor);
            Outline(punch, ZoneColor);

            GUI.Label(new Rect(move.x + 8f * _scale, move.yMax - 22f * _scale, move.width, 20f * _scale),
                "MOVE / CIRCLE", _small);

            // The head/body split and the lead/rear split, drawn where the thumb actually needs them.
            float bodyLine = punch.yMax - Touch.BodyBandTop * punch.height;
            float headLine = punch.yMax - Touch.HeadBandBottom * punch.height;
            GUI.color = ZoneColor;
            GUI.DrawTexture(new Rect(punch.x, bodyLine, punch.width, 1f * _scale), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(punch.x, headLine, punch.width, 1f * _scale), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(punch.center.x, punch.y, 1f * _scale, punch.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(punch.x + 8f * _scale, punch.y + 6f * _scale, punch.width, 20f * _scale),
                "HEAD", _small);
            GUI.Label(new Rect(punch.x + 8f * _scale, punch.yMax - 22f * _scale, punch.width, 20f * _scale),
                "BODY", _small);
            GUI.Label(new Rect(punch.x + punch.width * 0.22f, punch.center.y, punch.width, 20f * _scale),
                "LEAD", _small);
            GUI.Label(new Rect(punch.x + punch.width * 0.70f, punch.center.y, punch.width, 20f * _scale),
                "REAR", _small);

            if (Touch.StickActive)
            {
                Vector2 origin = ToGui(Touch.StickOrigin);
                Vector2 tip = ToGui(Touch.StickTip);
                Dot(origin, 16f * _scale, new Color(1f, 1f, 1f, 0.35f));
                Dot(tip, 22f * _scale, new Color(1f, 1f, 1f, 0.6f));
            }

            if (Touch.GuardActive)
            {
                GUI.color = new Color(0.32f, 0.62f, 0.95f, 0.22f);
                GUI.DrawTexture(punch, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(Touch.LastGesture))
            {
                GUI.Label(new Rect(punch.x, punch.y - 24f * _scale, punch.width, 22f * _scale),
                    Touch.LastGesture, _label);
            }
        }

        void DrawCrosshair()
        {
            if (CameraRig == null || !CameraRig.FirstPerson || Touch != null)
            {
                return;
            }

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            Dot(new Vector2(cx, cy), 4f * _scale, new Color(1f, 1f, 1f, 0.55f));

            GUI.Label(new Rect(cx + 14f * _scale, cy - 10f * _scale, 80f * _scale, 20f * _scale),
                CameraRig.AimHeight > 0.5f ? "HEAD" : "BODY", _label);
        }

        // ------------------------------------------------------------------
        // Match readouts
        // ------------------------------------------------------------------

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

            GUI.Label(new Rect(0f, Screen.height * 0.22f, Screen.width, 26f * _scale), text, style);
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
                text += Touch != null ? "   -  tap to beat the count" : "   -  mash a punch key";
            }

            GUI.Label(new Rect(0f, Screen.height * 0.32f, Screen.width, 30f * _scale), text, _mid);
        }

        void DrawResult()
        {
            if (!Director.MatchOver)
            {
                return;
            }

            MatchDecision decision = Director.Decision;
            bool playerWon = !decision.IsDraw && Director.Winner == Director.Player;

            GUIStyle headline = new GUIStyle(_mid);
            headline.normal.textColor = new Color(0.95f, 0.9f, 0.6f);

            GUIStyle verdict = new GUIStyle(_banner);
            verdict.normal.textColor = decision.IsDraw ? Color.white
                : playerWon ? new Color(0.4f, 1f, 0.55f)
                : new Color(1f, 0.4f, 0.4f);

            float y = Screen.height * 0.30f;
            GUI.Label(new Rect(0f, y, Screen.width, 28f * _scale), decision.Headline, headline);
            y += 30f * _scale;

            string text = decision.IsDraw ? "DRAW" : (playerWon ? "YOU WIN" : "YOU LOSE");
            GUI.Label(new Rect(0f, y, Screen.width, 50f * _scale), text, verdict);
            y += 52f * _scale;

            // A knockout does not go to the cards.
            if (decision.Kind != DecisionKind.Knockout)
            {
                DrawScorecard(y);
                y += 24f * _scale * (RoundRules.JudgeCount + 1) + 18f * _scale;
            }

            GUI.Label(new Rect(0f, y, Screen.width, 26f * _scale), "press R for another fight", _mid);
        }

        void DrawControls()
        {
            string line1;
            string line2;

            if (Touch != null)
            {
                line1 = "LEFT thumb: drag to step in / out / circle";
                line2 = "RIGHT thumb: tap = jab (left) or straight (right), high = head low = body"
                    + "     swipe up = uppercut, side = hook, down = slip, hold = guard";
            }
            else
            {
                line1 = "WASD step / circle     J or LMB jab     K or RMB straight     I or Q hook     O or E uppercut";
                line2 = "Shift (hold) guard     Space slip     mouse aims (look down = body)     V view     R restart     Esc free cursor";
            }

            float h = 44f * _scale;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0f, Screen.height - h, Screen.width, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(18f * _scale, Screen.height - h + 4f * _scale, Screen.width, 18f * _scale),
                line1, _label);
            GUI.Label(new Rect(18f * _scale, Screen.height - h + 23f * _scale, Screen.width, 18f * _scale),
                line2, _label);
        }

        // ------------------------------------------------------------------
        // Primitives. GUI space is top-left origin; touches are bottom-left.
        // ------------------------------------------------------------------

        static Rect ToGui(Rect normalised)
        {
            float w = normalised.width * Screen.width;
            float h = normalised.height * Screen.height;
            float x = normalised.xMin * Screen.width;
            float y = Screen.height - normalised.yMax * Screen.height;
            return new Rect(x, y, w, h);
        }

        static Vector2 ToGui(Vector2 screenPoint)
        {
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        static void Dot(Vector2 centre, float size, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void Outline(Rect rect, Color color)
        {
            float t = Mathf.Max(1f, 1f * _scale);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - t, rect.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, t, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - t, rect.y, t, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
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

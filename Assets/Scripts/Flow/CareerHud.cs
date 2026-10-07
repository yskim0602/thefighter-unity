using UnityEngine;

namespace TheFighter
{
    /// The career screens, in IMGUI, on purpose and temporarily.
    ///
    /// This exists to answer one question before any of the real interface gets built: **is the
    /// loop any fun?** Offer, sign, fight, result, train, repeat. If it is not, every pixel spent
    /// on a proper UI would have been spent on top of a career nobody wants to play - so the
    /// cheapest possible screens come first and get thrown away in step 7.4.
    ///
    /// Everything here is the throwaway half: per-frame allocations, no layout system, no
    /// transitions. None of it should be copied into the real UI.
    public class CareerHud : MonoBehaviour
    {
        public GameFlow Flow;

        float _scale = 1f;
        bool _styled;
        GUIStyle _title;
        GUIStyle _head;
        GUIStyle _body;
        GUIStyle _dim;
        GUIStyle _button;
        GUIStyle _box;
        Font _font;
        bool _fontSearched;
        bool _editingName;
        string _nameBuffer = "";

        void Awake()
        {
            if (Flow == null)
            {
                Flow = GetComponent<GameFlow>();
            }
        }

        /// Unity's built-in GUI font has no Hangul, so Korean labels come out as boxes. Borrowing
        /// an OS font is a one-liner and makes the prototype readable; the real UI gets a proper
        /// TextMeshPro asset in 7.3, which is the only way to ship this.
        Font FindKoreanFont()
        {
            string[] wanted =
            {
                "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "NanumGothic",
                "Nanum Gothic", "Noto Sans CJK KR", "Noto Sans KR", "Gulim", "Dotum"
            };

            string[] installed = Font.GetOSInstalledFontNames();
            if (installed == null)
            {
                return null;
            }

            for (int w = 0; w < wanted.Length; w++)
            {
                for (int i = 0; i < installed.Length; i++)
                {
                    if (string.Equals(installed[i], wanted[w], System.StringComparison.OrdinalIgnoreCase))
                    {
                        return Font.CreateDynamicFontFromOSFont(installed[i], 18);
                    }
                }
            }

            return null;
        }

        void BuildStyles()
        {
            if (!_fontSearched)
            {
                _fontSearched = true;
                _font = FindKoreanFont();
                if (_font == null)
                {
                    Debug.LogWarning("CareerHud: no Korean OS font found, so labels may render as "
                        + "boxes. Harmless - the real UI uses a TextMeshPro font asset.");
                }
            }

            if (_styled)
            {
                return;
            }
            _styled = true;

            _title = Make(30, FontStyle.Bold, new Color(1f, 0.92f, 0.78f));
            _head = Make(19, FontStyle.Bold, new Color(0.96f, 0.96f, 0.96f));
            _body = Make(16, FontStyle.Normal, new Color(0.88f, 0.88f, 0.90f));
            _dim = Make(14, FontStyle.Normal, new Color(0.62f, 0.64f, 0.68f));

            _button = new GUIStyle(GUI.skin.button);
            _button.fontSize = Mathf.RoundToInt(16 * _scale);
            _button.fontStyle = FontStyle.Bold;
            if (_font != null) { _button.font = _font; }

            _box = new GUIStyle(GUI.skin.box);
            _box.fontSize = Mathf.RoundToInt(15 * _scale);
            if (_font != null) { _box.font = _font; }
        }

        GUIStyle Make(int size, FontStyle weight, Color colour)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = Mathf.RoundToInt(size * _scale);
            style.fontStyle = weight;
            style.normal.textColor = colour;
            style.wordWrap = true;
            if (_font != null) { style.font = _font; }
            return style;
        }

        void OnGUI()
        {
            if (Flow == null || Flow.Career == null || Flow.State == FlowState.Fighting)
            {
                return;
            }

            float wanted = Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            if (!Mathf.Approximately(wanted, _scale))
            {
                _scale = wanted;
                _styled = false;
            }
            BuildStyles();

            float w = Mathf.Min(Screen.width - 40f, 760f * _scale);
            float h = Screen.height - 40f;
            Rect area = new Rect((Screen.width - w) * 0.5f, 20f, w, h);

            GUI.color = new Color(0.05f, 0.05f, 0.07f, 0.88f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(area.x + 18f * _scale, area.y + 14f * _scale,
                area.width - 36f * _scale, area.height - 28f * _scale));

            switch (Flow.State)
            {
                case FlowState.Home: DrawHome(); break;
                case FlowState.Offers: DrawOffers(); break;
                case FlowState.PreFight: DrawPreFight(); break;
                case FlowState.Result: DrawResult(); break;
                case FlowState.Training: DrawTraining(); break;
                case FlowState.Records: DrawRecords(); break;
            }

            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------

        void DrawHome()
        {
            CareerData c = Flow.Career;

            GUILayout.Label("THE FIGHTER", _title);
            Gap(6);

            if (_editingName)
            {
                GUILayout.BeginHorizontal();
                _nameBuffer = GUILayout.TextField(_nameBuffer, 16, GUILayout.Width(220f * _scale));
                if (GUILayout.Button("확인", _button, GUILayout.Width(70f * _scale)))
                {
                    string trimmed = _nameBuffer.Trim();
                    if (trimmed.Length > 0) { c.FighterName = trimmed; }
                    _editingName = false;
                    Flow.Save();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(c.FighterName + (c.IsChampion ? "  ★ 챔피언" : ""), _head);
                if (GUILayout.Button("이름 변경", _button, GUILayout.Width(100f * _scale)))
                {
                    _nameBuffer = c.FighterName;
                    _editingName = true;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("전적 " + c.Record + "  (KO " + c.KnockoutWins + ")"
                + "     스테이지 " + c.Stage + "/" + CareerConfig.MaxStage
                + "     " + BoxingStyles.DisplayName(c.Style), _body);
            GUILayout.Label("자금 ₩" + c.Money.ToString("N0")
                + "     " + c.Week + "주차"
                + "     승격 진행 " + c.StageProgress + "/" + CareerConfig.EvenWinsPerStage, _body);

            Gap(8);
            DrawBar("컨디션", c.Condition / (float)CareerConfig.MaxCondition,
                c.Condition + " / " + CareerConfig.MaxCondition,
                c.Condition < CareerConfig.ConditionWarning
                    ? new Color(0.85f, 0.45f, 0.2f) : new Color(0.35f, 0.75f, 0.45f));
            GUILayout.Label("컨디션이 낮으면 그 경기 동안 스탯이 깎입니다 (최저 "
                + Mathf.RoundToInt(CareerConfig.MinConditionScale * 100f) + "%)", _dim);

            Gap(10);
            DrawStats(c.Stats, "스탯");

            Gap(14);
            if (GUILayout.Button("경기 제의 (" + Flow.Career.Offers.Count + ")", _button, Tall()))
            {
                Flow.EnsureOffers();
                Flow.Enter(FlowState.Offers);
            }
            if (GUILayout.Button("훈련 / 휴식", _button, Tall()))
            {
                Flow.Enter(FlowState.Training);
            }
            if (GUILayout.Button("전적 기록", _button, Tall()))
            {
                Flow.Enter(FlowState.Records);
            }

            Gap(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("스탠스: " + (c.Stance == Stance.Orthodox ? "오소독스" : "사우스포"),
                _button, GUILayout.Width(200f * _scale)))
            {
                Flow.SetStance(c.Stance == Stance.Orthodox ? Stance.Southpaw : Stance.Orthodox);
            }
            if (GUILayout.Button("새 커리어", _button, GUILayout.Width(120f * _scale)))
            {
                Flow.NewCareer();
            }
            GUILayout.EndHorizontal();

            if (Flow.Ring != null && !Flow.Ring.SouthpawAvailable)
            {
                GUILayout.Label("사우스포는 미러 클립 세트가 들어오면 열립니다.", _dim);
            }
        }

        // ------------------------------------------------------------------

        void DrawOffers()
        {
            GUILayout.Label("경기 제의", _title);
            GUILayout.Label(Flow.Career.Week + "주차 · 제의는 " + CareerConfig.OfferWeeks
                + "주 안에 사라집니다", _dim);
            Gap(8);

            for (int i = 0; i < Flow.Career.Offers.Count; i++)
            {
                DrawOffer(Flow.Career.Offers[i]);
                Gap(6);
            }

            Gap(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전부 거절 (" + CareerConfig.DeclineAllWeeks + "주 경과)",
                _button, GUILayout.Width(260f * _scale)))
            {
                Flow.DeclineAll();
            }
            if (GUILayout.Button("뒤로", _button, GUILayout.Width(100f * _scale)))
            {
                Flow.Enter(FlowState.Home);
            }
            GUILayout.EndHorizontal();

            if (Flow.Career.DeclineStreak >= CareerConfig.DeclineStreakBeforePenalty)
            {
                GUILayout.Label("계속 거절하고 있습니다. 프로모터가 눈치를 봅니다.", _dim);
            }
        }

        void DrawOffer(MatchOffer offer)
        {
            if (offer == null)
            {
                return;
            }

            OpponentProfile o = offer.Opponent;
            GUILayout.BeginVertical(_box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("[" + offer.TierName + "]  " + o.Display, _head);
            GUILayout.FlexibleSpace();
            GUILayout.Label("₩" + offer.Purse.ToString("N0"), _head);
            GUILayout.EndHorizontal();

            GUILayout.Label(o.Record + " (KO " + o.Knockouts + ")"
                + "   " + BoxingStyles.DisplayName(o.Style)
                + "   " + (o.Stance == Stance.Orthodox ? "오소독스" : "사우스포")
                + "   레이팅 " + CareerConfig.Rating(o.Stats).ToString("F0")
                + " (나 " + Flow.Career.Rating.ToString("F0") + ")", _body);
            GUILayout.Label(offer.Rounds + "라운드 · " + offer.Venue
                + " · 승리 시: " + offer.Reward
                + " · 기한 " + offer.ExpiresWeek + "주차", _dim);

            if (GUILayout.Button("계약", _button, GUILayout.Height(30f * _scale)))
            {
                Flow.Sign(offer);
            }

            GUILayout.EndVertical();
        }

        // ------------------------------------------------------------------

        void DrawPreFight()
        {
            MatchOffer offer = Flow.SignedOffer;
            if (offer == null)
            {
                Flow.Enter(FlowState.Offers);
                return;
            }

            CareerData c = Flow.Career;
            GUILayout.Label("계 량", _title);
            GUILayout.Label(offer.Venue + " · " + offer.Rounds + "라운드"
                + (offer.TitleFight ? " · 타이틀전" : ""), _dim);
            Gap(10);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(_box);
            GUILayout.Label(c.FighterName, _head);
            GUILayout.Label(c.Record + "   " + BoxingStyles.DisplayName(c.Style), _body);
            GUILayout.Label("레이팅 " + c.Rating.ToString("F0"), _body);
            DrawStats(c.Stats, "");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_box);
            GUILayout.Label(offer.Opponent.Display, _head);
            GUILayout.Label(offer.Opponent.Record + "   "
                + BoxingStyles.DisplayName(offer.Opponent.Style), _body);
            GUILayout.Label("레이팅 " + CareerConfig.Rating(offer.Opponent.Stats).ToString("F0"), _body);
            DrawStats(offer.Opponent.Stats, "");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            Gap(10);
            float scale = CareerConfig.ConditionScale(c.Condition);
            if (c.Condition < CareerConfig.ConditionWarning)
            {
                GUILayout.Label("⚠ 컨디션 " + c.Condition + " — 이 경기에서 스탯이 "
                    + Mathf.RoundToInt(scale * 100f) + "%로 적용됩니다. 휴식을 권합니다.", _head);
            }
            else
            {
                GUILayout.Label("컨디션 " + c.Condition + " — 스탯 "
                    + Mathf.RoundToInt(scale * 100f) + "% 적용", _dim);
            }

            Gap(10);
            if (GUILayout.Button("경기 시작", _button, GUILayout.Height(40f * _scale)))
            {
                Flow.StartSignedFight();
            }
            if (GUILayout.Button("뒤로", _button, Tall()))
            {
                Flow.Enter(FlowState.Offers);
            }
        }

        // ------------------------------------------------------------------

        void DrawResult()
        {
            FightOutcome outcome = Flow.LastOutcome;
            if (outcome == null)
            {
                Flow.Enter(FlowState.Home);
                return;
            }

            CareerData c = Flow.Career;

            GUILayout.Label(outcome.Draw ? "무승부" : (outcome.PlayerWon ? "승리" : "패배"), _title);
            GUILayout.Label(outcome.Method + " · " + outcome.EndedRound + "/"
                + outcome.ScheduledRounds + "라운드", _head);
            Gap(8);

            GUILayout.Label("유효타 " + outcome.PunchesLanded + "/" + outcome.PunchesThrown
                + " (" + Mathf.RoundToInt(outcome.Accuracy * 100f) + "%)"
                + "   준 데미지 " + outcome.DamageDealt.ToString("F0")
                + "   받은 데미지 " + outcome.DamageTaken.ToString("F0"), _body);
            if (outcome.KnockdownsScored > 0 || outcome.KnockdownsSuffered > 0)
            {
                GUILayout.Label("다운  기록 " + outcome.KnockdownsScored
                    + " · 당함 " + outcome.KnockdownsSuffered, _body);
            }

            Gap(8);
            DrawJudges();

            Gap(8);
            GUILayout.Label("파이트머니 ₩" + Flow.LastPurse.ToString("N0")
                + "   →   보유 ₩" + c.Money.ToString("N0"), _head);
            GUILayout.Label("전적 " + c.Record + "   컨디션 " + c.Condition, _body);

            if (Flow.PromotedLastFight)
            {
                GUILayout.Label("★ 스테이지 " + Flow.PreviousStage + " → " + c.Stage
                    + "   다음 경기는 " + c.Rounds + "라운드", _head);
            }
            if (c.IsChampion)
            {
                GUILayout.Label("★ 챔피언 (방어 " + c.TitleDefences + "회)", _head);
            }

            Gap(12);
            if (GUILayout.Button("계속", _button, GUILayout.Height(40f * _scale)))
            {
                Flow.Enter(FlowState.Home);
            }
        }

        void DrawJudges()
        {
            if (Flow.Ring == null || Flow.Ring.Director == null)
            {
                return;
            }

            Scorecard card = Flow.Ring.Director.Card;
            if (card == null || card.RoundsScored == 0)
            {
                return;
            }

            for (int i = 0; i < card.JudgeCount; i++)
            {
                GUILayout.Label(card.JudgeAt(i).Name + "   "
                    + card.PlayerTotal(i) + " - " + card.EnemyTotal(i), _body);
            }
        }

        // ------------------------------------------------------------------

        void DrawTraining()
        {
            CareerData c = Flow.Career;

            GUILayout.Label("훈련", _title);
            GUILayout.Label("자금 ₩" + c.Money.ToString("N0") + "   " + c.Week + "주차"
                + "   컨디션 " + c.Condition, _dim);
            GUILayout.Label("스타일은 고르지 않습니다 — 훈련한 결과가 "
                + BoxingStyles.DisplayName(c.Style) + "입니다.", _dim);
            Gap(10);

            for (int i = 0; i < 4; i++)
            {
                TrainingFocus focus = (TrainingFocus)i;
                int value = c.StatValue(focus);
                int cost = TrainingPlan.Cost(c, focus);
                string reason;
                bool can = TrainingPlan.CanTrain(c, focus, out reason);

                GUILayout.BeginHorizontal(_box);
                GUILayout.BeginVertical();
                GUILayout.Label(TrainingPlan.DisplayName(focus) + "  " + value
                    + " / " + CareerConfig.MaxStat, _head);
                GUILayout.Label(TrainingPlan.Effect(focus)
                    + (can ? "" : "   — " + reason), _dim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();

                GUI.enabled = can;
                if (GUILayout.Button("₩" + cost.ToString("N0") + " · 1주",
                    _button, GUILayout.Width(170f * _scale), GUILayout.Height(38f * _scale)))
                {
                    Flow.Train(focus);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                Gap(4);
            }

            Gap(10);
            if (GUILayout.Button("휴식 (1주, 컨디션 +"
                + CareerConfig.ConditionPerRestWeek + ")", _button, GUILayout.Height(38f * _scale)))
            {
                Flow.Rest();
            }
            if (GUILayout.Button("뒤로", _button, Tall()))
            {
                Flow.Enter(FlowState.Home);
            }
        }

        // ------------------------------------------------------------------

        void DrawRecords()
        {
            CareerData c = Flow.Career;

            GUILayout.Label("전적 기록", _title);
            GUILayout.Label(c.Record + "   KO승 " + c.KnockoutWins
                + " · KO패 " + c.KnockoutLosses, _body);
            Gap(8);

            if (c.History.Count == 0)
            {
                GUILayout.Label("아직 경기가 없습니다.", _dim);
            }

            // Newest first, and only as many as fit - this screen gets a real scroll view later.
            int shown = 0;
            for (int i = c.History.Count - 1; i >= 0 && shown < 12; i--, shown++)
            {
                FightRecord r = c.History[i];
                GUILayout.Label(r.Week + "주  " + r.Line
                    + (r.TitleFight ? "  ★" : "")
                    + "   ₩" + r.Purse.ToString("N0"), _body);
            }

            Gap(12);
            if (GUILayout.Button("뒤로", _button, Tall()))
            {
                Flow.Enter(FlowState.Home);
            }
        }

        // ------------------------------------------------------------------

        void DrawStats(FighterStats stats, string heading)
        {
            if (!string.IsNullOrEmpty(heading))
            {
                GUILayout.Label(heading, _head);
            }

            GUILayout.Label("파워 " + stats.Power
                + "   체력 " + stats.Endurance
                + "   스피드 " + stats.Speed
                + "   기술 " + stats.Skill, _body);
            GUILayout.Label("체력바 " + stats.MaxHealth.ToString("F0")
                + " · 스태미나 " + stats.MaxStamina.ToString("F0")
                + " · 데미지 " + stats.PunchDamage.ToString("F1"), _dim);
        }

        void DrawBar(string label, float fill, string value, Color colour)
        {
            GUILayout.Label(label + "   " + value, _body);

            Rect row = GUILayoutUtility.GetRect(1f, 14f * _scale);
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
            GUI.color = colour;
            GUI.DrawTexture(new Rect(row.x, row.y, row.width * Mathf.Clamp01(fill), row.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void Gap(float pixels)
        {
            GUILayout.Space(pixels * _scale);
        }

        GUILayoutOption Tall()
        {
            return GUILayout.Height(34f * _scale);
        }
    }
}

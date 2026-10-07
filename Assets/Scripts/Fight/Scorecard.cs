using System.Collections.Generic;
using UnityEngine;

namespace TheFighter
{
    public enum DecisionKind
    {
        Knockout,
        Unanimous,
        Split,
        Majority,
        Draw
    }

    public struct MatchDecision
    {
        public DecisionKind Kind;
        public bool PlayerWon;
        public bool IsDraw;

        public string Headline
        {
            get
            {
                switch (Kind)
                {
                    case DecisionKind.Knockout: return "KNOCKOUT";
                    case DecisionKind.Unanimous: return "UNANIMOUS DECISION";
                    case DecisionKind.Split: return "SPLIT DECISION";
                    case DecisionKind.Majority: return "MAJORITY DECISION";
                    default: return "DRAW";
                }
            }
        }
    }

    /// Ten-point must, three independent judges.
    ///
    /// Each judge gets its own weighting for clean punching, volume and defence, plus a little
    /// noise, drawn fresh for every fight. That is the whole trick: identical fights score
    /// differently on different cards, close rounds genuinely swing, and you get split decisions
    /// instead of an arithmetic verdict. Being two rounds down with one to go is the feeling this
    /// exists to produce.
    public class Scorecard
    {
        public class Tally
        {
            public int Thrown;
            public int Landed;
            public int Blocked;
            public int Slipped;
            public int KnockdownsScored;
            public int KnockdownsSuffered;
            public float Damage;

            public void Add(Tally other)
            {
                Thrown += other.Thrown;
                Landed += other.Landed;
                Blocked += other.Blocked;
                Slipped += other.Slipped;
                KnockdownsScored += other.KnockdownsScored;
                KnockdownsSuffered += other.KnockdownsSuffered;
                Damage += other.Damage;
            }

            public void Clear()
            {
                Thrown = 0;
                Landed = 0;
                Blocked = 0;
                Slipped = 0;
                KnockdownsScored = 0;
                KnockdownsSuffered = 0;
                Damage = 0f;
            }
        }

        public struct Judge
        {
            public string Name;
            public float Clean;
            public float Volume;
            public float Defense;
            public float Knockdown;
            public float Noise;
        }

        /// This round's running stats. The director feeds these; ScoreRound consumes and clears.
        public readonly Tally Player = new Tally();
        public readonly Tally Enemy = new Tally();

        /// Running totals for the whole fight. ScoreRound clears the per-round tallies the judges
        /// work from, so without these the career would have nothing to report once the final
        /// bell rang.
        public readonly Tally PlayerTotals = new Tally();
        public readonly Tally EnemyTotals = new Tally();

        Judge[] _judges;
        readonly List<int[]> _playerRounds = new List<int[]>();
        readonly List<int[]> _enemyRounds = new List<int[]>();

        public int JudgeCount { get { return _judges != null ? _judges.Length : 0; } }
        public int RoundsScored { get { return _playerRounds.Count; } }

        public Judge JudgeAt(int index)
        {
            return _judges[index];
        }

        public void NewMatch()
        {
            _judges = new Judge[RoundRules.JudgeCount];
            for (int i = 0; i < _judges.Length; i++)
            {
                _judges[i] = MakeJudge("JUDGE " + (char)('A' + i));
            }

            _playerRounds.Clear();
            _enemyRounds.Clear();
            Player.Clear();
            Enemy.Clear();
            PlayerTotals.Clear();
            EnemyTotals.Clear();
        }

        /// Fight totals including the round in progress. A knockout ends a round that never got
        /// scored, and those punches still happened.
        public Tally PlayerSoFar() { return Combined(PlayerTotals, Player); }
        public Tally EnemySoFar() { return Combined(EnemyTotals, Enemy); }

        static Tally Combined(Tally banked, Tally live)
        {
            Tally total = new Tally();
            total.Add(banked);
            total.Add(live);
            return total;
        }

        static Judge MakeJudge(string name)
        {
            Judge judge = new Judge();
            judge.Name = name;
            judge.Clean = Random.Range(0.80f, 1.25f);
            judge.Volume = Random.Range(0.15f, 0.45f);
            judge.Defense = Random.Range(0.08f, 0.30f);
            judge.Knockdown = Random.Range(2.5f, 4.0f);
            judge.Noise = Random.Range(0.02f, 0.10f);
            return judge;
        }

        public void ScoreRound()
        {
            if (_judges == null)
            {
                NewMatch();
            }

            int[] playerScores = new int[_judges.Length];
            int[] enemyScores = new int[_judges.Length];

            for (int i = 0; i < _judges.Length; i++)
            {
                Judge judge = _judges[i];

                float player = Raw(judge, Player) + Random.Range(-judge.Noise, judge.Noise);
                float enemy = Raw(judge, Enemy) + Random.Range(-judge.Noise, judge.Noise);

                int playerScore;
                int enemyScore;

                if (Mathf.Abs(player - enemy) < RoundRules.EvenRoundThreshold)
                {
                    playerScore = 10;
                    enemyScore = 10;
                }
                else if (player > enemy)
                {
                    playerScore = 10;
                    enemyScore = 9;
                }
                else
                {
                    playerScore = 9;
                    enemyScore = 10;
                }

                // A knockdown is a point off the card, which is what turns 10-9 into 10-8.
                playerScore -= Player.KnockdownsSuffered;
                enemyScore -= Enemy.KnockdownsSuffered;

                playerScores[i] = Mathf.Clamp(playerScore, RoundRules.MinRoundScore, 10);
                enemyScores[i] = Mathf.Clamp(enemyScore, RoundRules.MinRoundScore, 10);
            }

            _playerRounds.Add(playerScores);
            _enemyRounds.Add(enemyScores);

            PlayerTotals.Add(Player);
            EnemyTotals.Add(Enemy);
            Player.Clear();
            Enemy.Clear();
        }

        static float Raw(Judge judge, Tally tally)
        {
            return judge.Clean * (tally.Damage / RoundRules.ScoringDamageReference)
                + judge.Volume * (tally.Landed / 10f)
                + judge.Defense * ((tally.Blocked + tally.Slipped) / 10f)
                + judge.Knockdown * tally.KnockdownsScored;
        }

        public int PlayerTotal(int judge)
        {
            return Total(_playerRounds, judge);
        }

        public int EnemyTotal(int judge)
        {
            return Total(_enemyRounds, judge);
        }

        static int Total(List<int[]> rounds, int judge)
        {
            int sum = 0;
            for (int i = 0; i < rounds.Count; i++)
            {
                sum += rounds[i][judge];
            }
            return sum;
        }

        public int PlayerRoundScore(int round, int judge)
        {
            return _playerRounds[round][judge];
        }

        public int EnemyRoundScore(int round, int judge)
        {
            return _enemyRounds[round][judge];
        }

        public MatchDecision Decide()
        {
            MatchDecision decision = new MatchDecision();

            int playerCards = 0;
            int enemyCards = 0;
            int evenCards = 0;

            for (int i = 0; i < JudgeCount; i++)
            {
                int player = PlayerTotal(i);
                int enemy = EnemyTotal(i);

                if (player > enemy)
                {
                    playerCards++;
                }
                else if (enemy > player)
                {
                    enemyCards++;
                }
                else
                {
                    evenCards++;
                }
            }

            if (playerCards == enemyCards)
            {
                decision.Kind = DecisionKind.Draw;
                decision.IsDraw = true;
                return decision;
            }

            decision.PlayerWon = playerCards > enemyCards;
            int winnerCards = decision.PlayerWon ? playerCards : enemyCards;
            int loserCards = decision.PlayerWon ? enemyCards : playerCards;

            if (winnerCards == JudgeCount)
            {
                decision.Kind = DecisionKind.Unanimous;
            }
            else if (loserCards > 0)
            {
                decision.Kind = DecisionKind.Split;
            }
            else
            {
                // Won on some cards, drew the rest.
                decision.Kind = evenCards > 0 ? DecisionKind.Majority : DecisionKind.Unanimous;
            }

            return decision;
        }
    }
}

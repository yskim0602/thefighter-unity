using UnityEngine;

namespace TheFighter
{
    /// Owns the match: places the fighters, routes every landed punch to the feedback layer, and
    /// ends things on a knockout. The career layer will later hand it an accepted offer and read
    /// the result back out; for now it just restarts on R.
    public class FightDirector : MonoBehaviour
    {
        public Fighter Player;
        public Fighter Enemy;
        public ImpactFeedback Feedback;

        public bool MatchOver { get; private set; }
        public Fighter Winner { get; private set; }
        public float FightTime { get; private set; }
        public HitEvent LastHit { get; private set; }
        public float LastHitAge { get { return Time.unscaledTime - _lastHitTime; } }

        Vector3 _playerSpawn;
        Vector3 _enemySpawn;
        float _lastHitTime = -99f;

        void Start()
        {
            if (Player == null || Enemy == null)
            {
                return;
            }

            _playerSpawn = Player.transform.position;
            _enemySpawn = Enemy.transform.position;

            Subscribe(Player);
            Subscribe(Enemy);

            StartFight();
        }

        void Update()
        {
            if (!MatchOver)
            {
                FightTime += Time.deltaTime;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                StartFight();
            }
        }

        void Subscribe(Fighter fighter)
        {
            fighter.Landed += OnLanded;
            fighter.Whiffed += OnWhiffed;
            fighter.KnockedOut += OnKnockedOut;
        }

        public void StartFight()
        {
            MatchOver = false;
            Winner = null;
            FightTime = 0f;
            Time.timeScale = 1f;

            Vector3 toEnemy = _enemySpawn - _playerSpawn;
            toEnemy.y = 0f;
            toEnemy.Normalize();

            Player.GetComponent<FighterMotor>().Teleport(_playerSpawn, Quaternion.LookRotation(toEnemy, Vector3.up));
            Enemy.GetComponent<FighterMotor>().Teleport(_enemySpawn, Quaternion.LookRotation(-toEnemy, Vector3.up));

            Player.ResetForFight();
            Enemy.ResetForFight();

            Player.FightActive = true;
            Enemy.FightActive = true;
        }

        void OnLanded(HitEvent evt)
        {
            LastHit = evt;
            _lastHitTime = Time.unscaledTime;

            if (Feedback != null)
            {
                Feedback.Report(evt);
            }
        }

        void OnWhiffed(Fighter fighter, PunchDefinition punch)
        {
            if (Feedback != null)
            {
                Feedback.ReportWhiff(fighter, punch);
            }
        }

        void OnKnockedOut(Fighter fighter)
        {
            MatchOver = true;
            Winner = fighter == Player ? Enemy : Player;
            Player.FightActive = false;
            Enemy.FightActive = false;
        }
    }
}

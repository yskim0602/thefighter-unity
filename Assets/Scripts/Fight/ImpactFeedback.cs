using UnityEngine;

namespace TheFighter
{
    /// Impact is most of what makes a punch feel like a punch, and none of it needs art: a short
    /// freeze on contact, a camera jolt, and a thud synthesised at runtime so the prototype ships
    /// with no audio assets at all.
    [RequireComponent(typeof(AudioSource))]
    public class ImpactFeedback : MonoBehaviour
    {
        public FightCamera CameraRig;
        public Fighter Player;

        AudioSource _source;
        AudioClip _clean;
        AudioClip _blocked;
        AudioClip _whiff;
        AudioClip _down;
        float _hitStop;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            _clean = BuildImpact("hit_clean", 0.16f, 118f, 0.55f, 26f);
            _blocked = BuildImpact("hit_block", 0.10f, 240f, 0.75f, 42f);
            _whiff = BuildImpact("whiff", 0.13f, 620f, 0.95f, 24f);
            _down = BuildImpact("down", 0.42f, 62f, 0.35f, 9f);
        }

        void Update()
        {
            if (_hitStop > 0f)
            {
                _hitStop -= Time.unscaledDeltaTime;
                if (_hitStop <= 0f)
                {
                    Time.timeScale = 1f;
                }
            }
        }

        void OnDisable()
        {
            if (_hitStop > 0f)
            {
                _hitStop = 0f;
                Time.timeScale = 1f;
            }
        }

        public void Report(HitEvent evt)
        {
            bool playerTookIt = Player != null && evt.Defender == Player;
            float weight = Mathf.Clamp01(evt.Damage / 26f);

            switch (evt.Result)
            {
                case HitResult.Clean:
                    Freeze(evt.CausedKnockdown ? CombatTuning.HitStopKnockdown : CombatTuning.HitStopClean);
                    Play(evt.CausedKnockdown ? _down : _clean, 0.75f + weight * 0.35f);
                    Shake((playerTookIt ? CombatTuning.CameraShakeTaken : CombatTuning.CameraShakeClean)
                        * (0.6f + weight) * (evt.Counter ? 1.35f : 1f));
                    if (playerTookIt && CameraRig != null)
                    {
                        CameraRig.Kick(4f + evt.Damage * 0.22f);
                    }
                    break;

                case HitResult.Blocked:
                    Freeze(CombatTuning.HitStopBlocked);
                    Play(_blocked, 0.55f + weight * 0.3f);
                    Shake(CombatTuning.CameraShakeBlocked * (0.6f + weight));
                    break;

                case HitResult.Dodged:
                    Play(_whiff, 0.4f);
                    break;
            }
        }

        public void ReportWhiff(Fighter fighter, PunchDefinition punch)
        {
            Play(_whiff, 0.32f);
        }

        void Freeze(float seconds)
        {
            if (seconds <= _hitStop)
            {
                return;
            }
            _hitStop = seconds;
            Time.timeScale = CombatTuning.HitStopTimeScale;
        }

        void Shake(float amount)
        {
            if (CameraRig != null)
            {
                CameraRig.Shake(amount);
            }
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip == null || _source == null)
            {
                return;
            }
            _source.pitch = Random.Range(0.92f, 1.08f);
            _source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// A decaying tone mixed with noise. Low and boomy reads as a clean shot, high and dry as
        /// leather on a glove.
        static AudioClip BuildImpact(string name, float duration, float frequency, float noise, float decay)
        {
            const int rate = 44100;
            int samples = Mathf.Max(16, Mathf.CeilToInt(rate * duration));
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / rate;
                float envelope = Mathf.Exp(-decay * t);
                // The tone slides down as it decays, which is what gives it a "thump" rather than a beep.
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t * (1f - 0.35f * t / Mathf.Max(0.001f, duration)));
                float grain = Random.value * 2f - 1f;
                data[i] = (tone * (1f - noise) + grain * noise) * envelope * 0.85f;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

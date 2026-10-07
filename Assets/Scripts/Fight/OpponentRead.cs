using UnityEngine;

namespace TheFighter
{
    /// What the AI has noticed about the fighter in front of it.
    ///
    /// This is the difference between an opponent and an obstacle. A bot that rolls dice plays the
    /// same fight every time and the player learns nothing; a bot that *notices* rewards the
    /// player for changing something, which is the only reason to keep playing a fight game.
    ///
    /// Everything is an exponential average, so it leans on the last few exchanges rather than the
    /// whole fight - a player who switches to the body in round four should be read as a body
    /// puncher by round five, not averaged back into who they were at the first bell.
    public class OpponentRead
    {
        /// 0 = everything to the body, 1 = everything upstairs. Starts neutral.
        public float HeadBias = 0.5f;
        /// Punches started per second, smoothed. High means pressure, which is what baiting wants.
        public float Pressure;
        /// How much of the time they hold a guard, and how often they slip. Between them they say
        /// whether a feint is worth throwing - a fighter who reacts to nothing cannot be drawn.
        public float Blocks;
        public float Slips;
        /// How often the punch after a jab is each kind. This is what lets the AI learn a one-two
        /// and start covering the straight before it arrives.
        public readonly float[] AfterJab = new float[4];

        PunchKind _last;
        bool _hasLast;
        float _sinceLast;
        bool _punchWasActive;

        public float Reactiveness { get { return Mathf.Clamp01(Blocks + Slips); } }

        /// Whether enough one-twos have been seen to be worth acting on at all.
        public bool AfterJabIsLive()
        {
            float total = 0f;
            for (int i = 0; i < AfterJab.Length; i++)
            {
                total += AfterJab[i];
            }
            return total >= 2f;
        }

        /// The kind most likely to follow a jab, and how sure we are.
        public PunchKind ExpectedAfterJab(out float confidence)
        {
            int best = 0;
            float total = 0f;
            for (int i = 0; i < AfterJab.Length; i++)
            {
                total += AfterJab[i];
                if (AfterJab[i] > AfterJab[best])
                {
                    best = i;
                }
            }

            confidence = total > 0.0001f ? AfterJab[best] / total : 0f;
            return (PunchKind)best;
        }

        public void Observe(Fighter them, float deltaTime)
        {
            if (them == null || deltaTime <= 0f)
            {
                return;
            }

            _sinceLast += deltaTime;

            // Sampled on the transition into a punch, not per frame - otherwise a slow punch would
            // count for more than a fast one just by being on screen longer.
            bool punching = them.ActivePunch != null;
            if (punching && !_punchWasActive)
            {
                Note(them);
            }
            _punchWasActive = punching;

            // Guard and slip are states rather than events, so these are time-weighted.
            float blend = 1f - Mathf.Exp(-deltaTime * 0.6f);
            Blocks = Mathf.Lerp(Blocks, them.IsGuarding ? 1f : 0f, blend);
            Slips = Mathf.Lerp(Slips, them.IsDodging ? 1f : 0f, blend);
            Pressure = Mathf.Lerp(Pressure, 0f, 1f - Mathf.Exp(-deltaTime * 0.35f));
        }

        void Note(Fighter them)
        {
            PunchKind kind = them.ActivePunch.Kind;

            // A feint is still information: it is what they want you to think is coming.
            HeadBias = Mathf.Lerp(HeadBias, them.AimHeight >= 0.5f ? 1f : 0f, 0.25f);
            Pressure += 1f;

            // Only count a follow-up that actually followed - a punch eight seconds later is a
            // new exchange, not the second half of a combination.
            if (_hasLast && _last == PunchKind.Jab && _sinceLast < 1.1f)
            {
                for (int i = 0; i < AfterJab.Length; i++)
                {
                    AfterJab[i] *= 0.88f;
                }
                AfterJab[(int)kind] += 1f;
            }

            _last = kind;
            _hasLast = true;
            _sinceLast = 0f;
        }

        public void Reset()
        {
            HeadBias = 0.5f;
            Pressure = 0f;
            Blocks = 0f;
            Slips = 0f;
            for (int i = 0; i < AfterJab.Length; i++)
            {
                AfterJab[i] = 0f;
            }
            _hasLast = false;
            _punchWasActive = false;
            _sinceLast = 0f;
        }
    }
}

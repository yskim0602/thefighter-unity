using UnityEngine;

namespace TheFighter
{
    public enum TrainingFocus
    {
        Power,
        Endurance,
        Speed,
        Skill
    }

    /// Training spends the two things a career is short of - money and weeks - for one stat point.
    /// Each point already banked makes the next dearer, so specialising is a decision with a
    /// payoff rather than a detour on the way to maxing everything out. And because the player's
    /// style is inferred from their stats, training is also how you choose what kind of fighter
    /// you are becoming, without ever picking from a list.
    public static class TrainingPlan
    {
        public static string DisplayName(TrainingFocus focus)
        {
            switch (focus)
            {
                case TrainingFocus.Power: return "파워";
                case TrainingFocus.Endurance: return "체력";
                case TrainingFocus.Speed: return "스피드";
                default: return "기술";
            }
        }

        /// What this stat buys in the ring, in the player's words rather than the code's.
        public static string Effect(TrainingFocus focus)
        {
            switch (focus)
            {
                case TrainingFocus.Power: return "펀치 데미지";
                case TrainingFocus.Endurance: return "체력바 + 스태미나";
                case TrainingFocus.Speed: return "발놀림 + 펀치 속도";
                default: return "리치 + 후딜 회복";
            }
        }

        public static int Cost(CareerData career, TrainingFocus focus)
        {
            return CareerConfig.TrainingCost(career.StatValue(focus));
        }

        public static bool CanTrain(CareerData career, TrainingFocus focus, out string reason)
        {
            if (career.StatValue(focus) >= CareerConfig.MaxStat)
            {
                reason = "이미 최대치입니다";
                return false;
            }

            if (career.Money < Cost(career, focus))
            {
                reason = "자금이 부족합니다";
                return false;
            }

            reason = "";
            return true;
        }

        /// Spends the money and the week. Condition drops, which is the quiet cost: train right up
        /// to a fight and you will take it tired.
        public static bool Train(CareerData career, TrainingFocus focus)
        {
            string reason;
            if (!CanTrain(career, focus, out reason))
            {
                return false;
            }

            career.Money -= Cost(career, focus);
            career.RaiseStat(focus);
            career.Week += CareerConfig.TrainingWeeks;
            career.Condition = Mathf.Clamp(
                career.Condition + CareerConfig.ConditionPerTrainingWeek,
                0, CareerConfig.MaxCondition);

            return true;
        }
    }
}

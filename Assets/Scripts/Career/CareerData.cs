using System.Collections.Generic;
using UnityEngine;

namespace TheFighter
{
    /// The save file, and the rules that move it forward. Flat serializable fields throughout:
    /// JsonUtility ignores properties and has never heard of a dictionary, and a save format that
    /// quietly drops half its fields is worse than no save at all.
    [System.Serializable]
    public class CareerData
    {
        /// Bumped whenever a field's meaning changes, so an old save is detected rather than
        /// loaded into the wrong shape.
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;

        public string FighterName = "YOU";
        public FighterStats Stats = new FighterStats(
            CareerConfig.StartingStat, CareerConfig.StartingStat,
            CareerConfig.StartingStat, CareerConfig.StartingStat);
        public Stance Stance = Stance.Orthodox;

        public int Stage = CareerConfig.StartingStage;
        public int Money = CareerConfig.StartingMoney;
        public int Week = 1;
        public int Condition = CareerConfig.MaxCondition;

        public int Wins;
        public int Losses;
        public int Draws;
        public int KnockoutWins;
        public int KnockoutLosses;
        public bool IsChampion;

        /// Even-level wins banked toward the next stage. A step-up win promotes outright, so this
        /// is the slow road rather than the only one.
        public int StageProgress;
        public int WinStreak;
        public int LossStreak;
        public int TitleDefences;

        public int DeclineStreak;

        public List<FightRecord> History = new List<FightRecord>();
        public List<MatchOffer> Offers = new List<MatchOffer>();
        /// -1 means "no batch has been generated yet", which is not the same as week 0.
        public int OffersWeek = -1;

        // ------------------------------------------------------------------
        // Reading
        // ------------------------------------------------------------------

        public string Record
        {
            get { return Wins + "-" + Losses + (Draws > 0 ? "-" + Draws : ""); }
        }

        public float Rating { get { return CareerConfig.Rating(Stats); } }

        /// The player never picks a style - whatever they trained decides it. Keeping it derived
        /// means the character screen cannot lie about who you have become.
        public BoxingStyle Style { get { return BoxingStyles.Infer(Stats); } }

        public int Rounds { get { return RoundRules.RoundsForStage(Stage); } }

        public bool TitleEligible
        {
            get { return Stage >= CareerConfig.TitleStage && StageProgress >= CareerConfig.TitleWinsRequired; }
        }

        // ------------------------------------------------------------------
        // Spending weeks
        // ------------------------------------------------------------------

        public void Rest()
        {
            Week++;
            Condition = Mathf.Min(CareerConfig.MaxCondition,
                Condition + CareerConfig.ConditionPerRestWeek);
        }

        public int StatValue(TrainingFocus focus)
        {
            switch (focus)
            {
                case TrainingFocus.Power: return Stats.Power;
                case TrainingFocus.Endurance: return Stats.Endurance;
                case TrainingFocus.Speed: return Stats.Speed;
                default: return Stats.Skill;
            }
        }

        public void RaiseStat(TrainingFocus focus)
        {
            switch (focus)
            {
                case TrainingFocus.Power: Stats.Power++; break;
                case TrainingFocus.Endurance: Stats.Endurance++; break;
                case TrainingFocus.Speed: Stats.Speed++; break;
                default: Stats.Skill++; break;
            }
        }

        // ------------------------------------------------------------------
        // Results
        // ------------------------------------------------------------------

        /// Folds a finished fight into the career: record, money, condition, standing, history.
        /// All of it here rather than spread across the screens, so there is one place to read
        /// when a result does something surprising.
        public void ApplyOutcome(MatchOffer offer, FightOutcome outcome)
        {
            bool knockout = outcome.Kind == DecisionKind.Knockout;
            // Captured before ApplyStanding, which is what promotes - the record has to say where
            // the fight happened, not where winning it moved you to.
            int foughtAtStage = Stage;

            if (outcome.Draw)
            {
                Draws++;
                WinStreak = 0;
                LossStreak = 0;
            }
            else if (outcome.PlayerWon)
            {
                Wins++;
                WinStreak++;
                LossStreak = 0;
                if (knockout) { KnockoutWins++; }
            }
            else
            {
                Losses++;
                LossStreak++;
                WinStreak = 0;
                if (knockout) { KnockoutLosses++; }
            }

            int purse = PurseFor(offer, outcome);
            Money += purse;

            Condition = Mathf.Clamp(
                Condition + CareerConfig.ConditionPerFight
                    + Mathf.RoundToInt(outcome.DamageTaken * CareerConfig.ConditionPerDamageTaken),
                0, CareerConfig.MaxCondition);

            ApplyStanding(offer, outcome);

            FightRecord record = new FightRecord();
            record.Week = Week;
            record.Stage = foughtAtStage;
            record.OpponentName = offer.Opponent.Name;
            record.OpponentRecord = offer.Opponent.Record;
            record.Won = outcome.PlayerWon;
            record.Draw = outcome.Draw;
            record.ScheduledRounds = outcome.ScheduledRounds;
            record.EndedRound = outcome.EndedRound;
            record.Method = outcome.Method;
            record.Purse = purse;
            record.TitleFight = offer.TitleFight;
            History.Add(record);

            Week++;
            Offers.Clear();
            OffersWeek = -1;
            DeclineStreak = 0;
        }

        public int PurseFor(MatchOffer offer, FightOutcome outcome)
        {
            float ratio = outcome.Draw
                ? CareerConfig.DrawPurseRatio
                : (outcome.PlayerWon ? 1f : CareerConfig.LossPurseRatio);

            if (outcome.PlayerWon && outcome.Kind == DecisionKind.Knockout)
            {
                ratio += CareerConfig.KnockoutBonus;
            }

            return Mathf.RoundToInt(offer.Purse * ratio);
        }

        /// Promotion comes from beating someone above you. Winning a tune-up pays and nothing
        /// else - otherwise the safe choice would also be the fast choice and the risk decision
        /// the whole career is built on would evaporate.
        void ApplyStanding(MatchOffer offer, FightOutcome outcome)
        {
            if (outcome.Draw)
            {
                return;
            }

            if (!outcome.PlayerWon)
            {
                StageProgress = 0;
                if (LossStreak >= CareerConfig.LossesBeforeDemotion && Stage > 1)
                {
                    Stage--;
                    LossStreak = 0;
                }
                if (offer.TitleFight)
                {
                    IsChampion = false;
                }
                return;
            }

            if (offer.TitleFight)
            {
                if (IsChampion) { TitleDefences++; }
                IsChampion = true;
                return;
            }

            if (offer.IsStepUp)
            {
                Promote();
                return;
            }

            if (offer.IsTuneUp)
            {
                return;
            }

            StageProgress++;
            if (StageProgress >= CareerConfig.EvenWinsPerStage)
            {
                Promote();
            }
        }

        void Promote()
        {
            if (Stage < CareerConfig.MaxStage)
            {
                Stage++;
                StageProgress = 0;
                return;
            }

            // Already at the top: wins here bank toward a title shot instead.
            StageProgress++;
        }
    }
}

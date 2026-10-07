namespace TheFighter
{
    /// Every career curve in one place, the way CombatTuning holds the fight's feel. A career is a
    /// sequence of risk decisions, and these numbers are what makes a decision a decision: if the
    /// safe fight paid the same as the hard one there would be nothing to choose.
    public static class CareerConfig
    {
        // --- Starting out --------------------------------------------------
        public const int StartingStage = 1;
        public const int MaxStage = 10;
        public const int StartingStat = 10;
        public const int MaxStat = 25;
        public const int StartingMoney = 2500;
        /// Opponents are allowed past the player's own cap, or a maxed-out fighter at the top of
        /// the game could never be offered a step up again.
        public const int MaxOpponentStat = 30;

        // --- Difficulty tiers ----------------------------------------------
        // An offer's difficulty is the opponent's rating over yours. Three tiers come in every
        // batch so the choice is always the same shape: take the money, take the step up, or take
        // the tune-up.
        public const float SafeTier = 0.84f;
        public const float EvenTier = 1.0f;
        public const float RiskyTier = 1.18f;
        /// Spread inside a tier, so two "even" offers are not identical.
        public const float TierVariance = 0.06f;
        /// Above this the offer counts as a step up, below it as a tune-up.
        public const float RiskyThreshold = 1.05f;
        public const float SafeThreshold = 0.95f;

        // --- Purse ---------------------------------------------------------
        public const int BasePurse = 1200;
        /// Per stage. Compounding is what makes the climb feel worth it.
        public const float PurseStageGrowth = 1.4f;
        /// A hard fight pays for being hard - and pays *steeply*, which is the other half of the
        /// risk decision. A linear weight here made the step-up worth about 25% more than the
        /// tune-up, which is not enough to make anyone think twice; the exponent takes it to
        /// roughly double.
        public const float PurseDifficultyExponent = 2.2f;
        public const float PurseFloor = 0.15f;
        /// You are paid for showing up, win or lose - just much less.
        public const float LossPurseRatio = 0.4f;
        public const float DrawPurseRatio = 0.7f;
        /// A knockout win is a highlight reel, and highlight reels sell tickets.
        public const float KnockoutBonus = 0.25f;
        public const float TitlePurseMultiplier = 2.5f;

        // --- Advancement ---------------------------------------------------
        // Promotion comes from beating someone above you, not from accumulating wins against
        // nobody. That one rule is what stops the safe choice from being the right choice.
        public const int EvenWinsPerStage = 2;
        /// Three in a row and the phone stops ringing at your level. Two was as easy as the two
        /// wins promotion needs, which made the even-fight path a random walk that never arrived.
        public const int LossesBeforeDemotion = 3;
        /// A title is on the line at the top, and only once you are established there.
        public const int TitleStage = MaxStage;
        public const int TitleWinsRequired = 2;

        // --- Offers --------------------------------------------------------
        public const int OffersPerBatch = 3;
        public const int OfferWeeks = 4;
        /// Turning everything down still costs you the week, and promoters notice.
        public const int DeclineAllWeeks = 1;
        public const int DeclineStreakBeforePenalty = 3;

        // --- Condition -----------------------------------------------------
        // Condition is the reason to ever turn a fight down. It is not health - you always start
        // a fight on full health - it is how ready you are, and a fight taken tired is a fight
        // fought with worse stats.
        public const int MaxCondition = 100;
        public const int ConditionPerRestWeek = 22;
        public const int ConditionPerTrainingWeek = -9;
        /// Base cost of a fight, before damage.
        public const int ConditionPerFight = -18;
        public const float ConditionPerDamageTaken = -0.22f;
        /// Condition scales the player's stats for a fight: full is 1.0, empty is this.
        public const float MinConditionScale = 0.72f;
        /// Below this the career screen warns you before you sign.
        public const int ConditionWarning = 65;

        // --- Training ------------------------------------------------------
        public const int BaseTrainingCost = 400;
        /// Each point already banked makes the next one dearer, so a specialist is a real choice
        /// rather than a stop on the way to maxing everything.
        public const float TrainingCostPerPoint = 0.38f;
        public const int TrainingWeeks = 1;

        public static int TrainingCost(int currentValue)
        {
            int over = currentValue - StartingStat;
            float scale = 1f + UnityEngine.Mathf.Max(0, over) * TrainingCostPerPoint;
            return UnityEngine.Mathf.RoundToInt(BaseTrainingCost * scale);
        }

        /// One number per fighter, so an offer's difficulty is comparable across styles. The sum
        /// rather than anything cleverer: all four stats buy roughly a fight's worth of advantage.
        public static float Rating(FighterStats stats)
        {
            return stats.Power + stats.Endurance + stats.Speed + stats.Skill;
        }

        // --- Opposition ----------------------------------------------------
        /// What a fighter at this stage is worth. **The stage sets the opposition, not the
        /// player's own rating** - that distinction is the whole growth curve.
        ///
        /// Scaling opponents off the player instead rubber-bands the career: train all you like
        /// and the next opponent trains with you, so an even fight stays a coin flip forever and
        /// the only thing training buys is a bigger number on a screen. Simulated over 400 weeks
        /// that way, a player taking even fights never got past stage 3 - promotion needs two
        /// wins before a loss and demotion needs a losing streak, and at a permanent 50% those
        /// cancel out.
        ///
        /// Anchored just under the player's starting 40, and reaching about where a fighter who
        /// trains steadily actually ends up - not where a theoretically maxed one would. Training
        /// hard turns an even fight at your own stage into one you should win and leaves the step
        /// up as the real test; neglecting it makes a tune-up dangerous.
        ///
        /// Measured by simulating 16 careers per strategy against a crude rating-gap win model.
        /// The slope is what that measurement was for: at 6.2 the ladder outran the player and
        /// even a champion finished 49-50, while at 4.6 the straightforward path comes out around
        /// 37-31 over roughly 70 fights and 200 weeks. Training remained strictly required at
        /// every slope tried - a career that never trains stalls around stage 3 in all of them.
        ///
        /// First-pass numbers. The win model behind them ignores player skill entirely, so real
        /// play will move these - they are a starting point, not a balance.
        public const float StageRatingBase = 38f;
        public const float StageRatingPerStage = 4.6f;

        public static float StageRating(int stage)
        {
            return StageRatingBase + UnityEngine.Mathf.Max(0, stage - 1) * StageRatingPerStage;
        }

        /// What an offer pays before the win/loss ratio is applied.
        public static int Purse(int stage, float difficulty, bool title)
        {
            float stageScale = UnityEngine.Mathf.Pow(PurseStageGrowth, UnityEngine.Mathf.Max(0, stage - 1));
            float riskScale = PurseFloor
                + UnityEngine.Mathf.Pow(UnityEngine.Mathf.Max(0.1f, difficulty), PurseDifficultyExponent);
            float purse = BasePurse * stageScale * riskScale;
            if (title)
            {
                purse *= TitlePurseMultiplier;
            }
            return UnityEngine.Mathf.RoundToInt(purse / 10f) * 10;
        }

        public static float ConditionScale(int condition)
        {
            float t = UnityEngine.Mathf.Clamp01(condition / (float)MaxCondition);
            return UnityEngine.Mathf.Lerp(MinConditionScale, 1f, t);
        }
    }
}

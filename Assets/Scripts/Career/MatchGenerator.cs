using System.Collections.Generic;
using UnityEngine;

namespace TheFighter
{
    /// Makes the offers on the table. Every batch holds the same three-way choice - a tune-up, an
    /// even fight and a step up - because that is the decision the career is built on. The purse
    /// moves with the difficulty, so there is never an offer that is simply better than another.
    public static class MatchGenerator
    {
        static readonly string[] FirstNames =
        {
            "Marcus", "Dmitri", "Rafael", "Tyrone", "Kenji", "Omar", "Luis", "Viktor",
            "Terrence", "Hugo", "Nikolai", "Joaquin", "Darnell", "Ivan", "Emeka", "Sergio",
            "Kwame", "Andres", "Malik", "Bruno", "Takeshi", "Carlos", "Dion", "Pavel"
        };

        static readonly string[] LastNames =
        {
            "Okafor", "Volkov", "Santos", "Whitaker", "Tanaka", "Haddad", "Mendoza", "Petrov",
            "Boyd", "Ferreira", "Kuznetsov", "Reyes", "Hayes", "Dragomir", "Adeyemi", "Castillo",
            "Mensah", "Navarro", "Jackson", "Moreira", "Yamada", "Ortega", "Walsh", "Novak"
        };

        static readonly string[] Nicknames =
        {
            "The Hammer", "Ghost", "Iron", "El Toro", "Thunder", "Cobra", "The Wall", "Razor",
            "Hurricane", "The Surgeon", "Bulldog", "Lightning", "The Machine", "Stone Hands",
            "Shadow", "The Butcher", "Silk", "Mongoose"
        };

        /// Venues climb with the stage, which is most of how a career's shape comes across without
        /// a word of narration.
        static readonly string[] SmallVenues =
        {
            "지역 체육관", "시민회관", "소규모 아레나", "클럽 대회"
        };

        static readonly string[] BigVenues =
        {
            "국제 아레나", "중앙 경기장", "돔 스타디움", "메인 이벤트 홀"
        };

        /// Builds a fresh batch and stamps it onto the career. Clears whatever was there: an offer
        /// you did not take is gone, which is what makes declining cost something.
        public static void Refresh(CareerData career)
        {
            career.Offers.Clear();

            bool title = career.TitleEligible;
            float[] tiers = { CareerConfig.SafeTier, CareerConfig.EvenTier, CareerConfig.RiskyTier };

            for (int i = 0; i < tiers.Length && i < CareerConfig.OffersPerBatch; i++)
            {
                float difficulty = tiers[i]
                    + Random.Range(-CareerConfig.TierVariance, CareerConfig.TierVariance);

                // Only the hardest offer in a batch can be for the belt. A title shot you can
                // dodge by taking the easy fight next to it is not a title shot.
                bool forTitle = title && i == tiers.Length - 1;
                career.Offers.Add(BuildOffer(career, difficulty, forTitle));
            }

            career.OffersWeek = career.Week;
        }

        public static MatchOffer BuildOffer(CareerData career, float difficulty, bool title)
        {
            MatchOffer offer = new MatchOffer();
            offer.Difficulty = difficulty;
            offer.Stage = career.Stage;
            offer.Rounds = title ? RoundRules.TitleRounds : RoundRules.RoundsForStage(career.Stage);
            offer.TitleFight = title;
            offer.Purse = CareerConfig.Purse(career.Stage, difficulty, title);
            offer.ExpiresWeek = career.Week + CareerConfig.OfferWeeks;
            offer.Venue = Venue(career.Stage, title);
            offer.Opponent = BuildOpponent(career, difficulty, title);
            return offer;
        }

        public static OpponentProfile BuildOpponent(CareerData career, float difficulty, bool title)
        {
            OpponentProfile profile = new OpponentProfile();

            profile.Name = FirstNames[Random.Range(0, FirstNames.Length)]
                + " " + LastNames[Random.Range(0, LastNames.Length)];

            // A nickname is a promoter's doing, so the further up the card the likelier it is.
            if (title || Random.value < 0.25f + career.Stage * 0.04f)
            {
                profile.Nickname = Nicknames[Random.Range(0, Nicknames.Length)];
            }

            // Off the stage, never off the player's own rating - see CareerConfig.StageRating.
            profile.Stats = BuildStats(CareerConfig.StageRating(career.Stage) * difficulty);

            // Billed as whatever they will actually fight like. Inferring it from the finished
            // stats rather than storing the style we aimed for means the offer screen cannot
            // promise a counter-puncher and deliver a slugger.
            profile.Style = BoxingStyles.Infer(profile.Stats);

            profile.Stance = Random.value < 0.22f ? Stance.Southpaw : Stance.Orthodox;

            float skill = Mathf.Lerp(0.42f, 0.86f, (career.Stage - 1) / 9f)
                + (difficulty - 1f) * 0.35f;
            profile.AiSkill = Mathf.Clamp01(skill + Random.Range(-0.04f, 0.04f));

            BuildRecord(profile, career.Stage, difficulty, title);
            return profile;
        }

        /// Distributes a target rating across the four stats along one style's shape, so the
        /// opponent is lopsided the way a real fighter is instead of being four equal numbers.
        static FighterStats BuildStats(float targetTotal)
        {
            BoxingStyle shapeStyle = BoxingStyles.All[Random.Range(0, BoxingStyles.All.Length)];
            Vector4 shape = BoxingStyles.IdealStatShape(shapeStyle);

            float sum = shape.x + shape.y + shape.z + shape.w;
            if (sum < 0.0001f)
            {
                shape = new Vector4(1f, 1f, 1f, 1f);
                sum = 4f;
            }

            float total = Mathf.Max(16f, targetTotal);
            FighterStats stats = new FighterStats(
                Portion(total, shape.x, sum),
                Portion(total, shape.y, sum),
                Portion(total, shape.z, sum),
                Portion(total, shape.w, sum));

            return stats;
        }

        static int Portion(float total, float weight, float sum)
        {
            float value = total * (weight / sum) * Random.Range(0.92f, 1.08f);
            return Mathf.Clamp(Mathf.RoundToInt(value), 5, CareerConfig.MaxOpponentStat);
        }

        /// A record that reads like the opponent's standing. A step-up is somebody with wins; a
        /// tune-up is somebody who has been losing, and the player can see which they are signing.
        static void BuildRecord(OpponentProfile profile, int stage, float difficulty, bool title)
        {
            int fights = Mathf.Max(2, Mathf.RoundToInt(stage * 2.2f * Random.Range(0.7f, 1.4f)));
            float winRate = Mathf.Clamp(0.42f + (difficulty - 1f) * 1.5f, 0.2f, 0.92f);
            if (title)
            {
                winRate = Mathf.Max(winRate, 0.85f);
                fights = Mathf.Max(fights, 14);
            }

            profile.Wins = Mathf.RoundToInt(fights * winRate);
            profile.Draws = Random.value < 0.25f ? 1 : 0;
            profile.Losses = Mathf.Max(0, fights - profile.Wins - profile.Draws);
            profile.Knockouts = Mathf.RoundToInt(profile.Wins * Random.Range(0.25f, 0.7f));
        }

        static string Venue(int stage, bool title)
        {
            if (title)
            {
                return "타이틀 — " + BigVenues[Random.Range(0, BigVenues.Length)];
            }
            string[] pool = stage >= 6 ? BigVenues : SmallVenues;
            return pool[Random.Range(0, pool.Length)];
        }

        /// Drops offers whose deadline has passed, and reports whether anything is still live.
        public static bool PruneExpired(CareerData career)
        {
            for (int i = career.Offers.Count - 1; i >= 0; i--)
            {
                if (career.Offers[i] == null || career.Offers[i].ExpiresWeek <= career.Week)
                {
                    career.Offers.RemoveAt(i);
                }
            }
            return career.Offers.Count > 0;
        }
    }
}

using System;

namespace Game.Core
{
    // What a bot does about the pollution (S8-A8, ADR-245). Pure: it knows only what a player knows (region states, the warning
    // clock, its own place). Reaction: when the region the bot stands in is warned, it waits a delay by difficulty (easy longest,
    // hard shortest, fixed per bot by its seed) and then leaves; it always leaves when the front is close or the region is lost.
    public static class BotPollutionPlan
    {
        // Longest reaction delay after a warning began, seconds, by difficulty (0..max drawn per bot).
        public static float MaxReactionSeconds(BotDifficulty level) => level == BotDifficulty.Easy ? 45f : level == BotDifficulty.Hard ? 10f : 25f;

        public static float ReactionSeconds(BotDifficulty level, int botSeed) =>
            (float)(new Random(unchecked(botSeed * 131 + 7)).NextDouble() * MaxReactionSeconds(level));

        // True when the bot should head out of its region now. `sinceWarning` = seconds since the region's warning began,
        // `secondsToConfirm` = seconds until it is contaminated (large when not warned), `doomed` = the front is within reach.
        public static bool ShouldLeave(PollutionState own, float sinceWarning, float secondsToConfirm, bool doomed, BotDifficulty level, int botSeed)
        {
            if (own == PollutionState.Contaminated || doomed) return true;
            if (own != PollutionState.Warning) return false;
            // Whatever the delay, the last stretch before the confirmation is for walking out.
            const float MinimumWalkSeconds = 40f;
            return secondsToConfirm <= MinimumWalkSeconds || sinceWarning >= ReactionSeconds(level, botSeed);
        }

        // The final region is known to a player once this many regions are warned or contaminated: ceil(2/3 of the regions) (M7).
        public static int KnownFinalThreshold(int regionCount) => Math.Max(1, (int)Math.Ceiling(regionCount * 2.0 / 3.0));

        public static bool KnowsFinal(int warnedOrContaminated, int regionCount) => warnedOrContaminated >= KnownFinalThreshold(regionCount);
    }
}

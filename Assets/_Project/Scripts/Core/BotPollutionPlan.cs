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

        // Whatever the delay, at least the last stretch before the confirmation is for walking out.
        public const float MinimumWalkSeconds = 40f;
        // A nav path runs longer than the straight line, and doors, a fight on the way or a stall cost time (ADR-533).
        public const float PathFactor = 1.4f, SpareSeconds = 15f;

        // Seconds a bot needs to cross `metersToBorder` (straight line) at `speed` m/s and be out before the region ends: both
        // endings start at the confirmation, the gas from the lowest ground and the bombardment anywhere (ADR-484), so being
        // over the border by then is safe from either. Never under MinimumWalkSeconds.
        public static float WalkOutSeconds(float metersToBorder, float speed) =>
            Math.Max(MinimumWalkSeconds, metersToBorder * PathFactor / Math.Max(0.5f, speed) + SpareSeconds);

        // Metres along the straight line from (fromX, fromZ) toward (toX, toZ) until `inside` is false (the border of the
        // region it stands in), sampled every `step` metres; the whole distance when it never leaves.
        public static float MetersToBorder(Func<float, float, bool> inside, float fromX, float fromZ, float toX, float toZ, float step = 10f)
        {
            float dx = toX - fromX, dz = toZ - fromZ, len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f) return 0f;
            for (float d = step; d < len; d += step)
                if (!inside(fromX + dx * d / len, fromZ + dz * d / len)) return d;
            return len;
        }

        // True when the bot should head out of its region now. `sinceWarning` = seconds since the region's warning began,
        // `secondsToConfirm` = seconds until it is contaminated (large when not warned), `doomed` = the front is within reach,
        // `walkOutSeconds` = WalkOutSeconds of the way out: whatever the reaction delay, it leaves when that is all that is left.
        public static bool ShouldLeave(PollutionState own, float sinceWarning, float secondsToConfirm, bool doomed, BotDifficulty level, int botSeed,
            float walkOutSeconds = MinimumWalkSeconds)
        {
            if (own == PollutionState.Contaminated || doomed) return true;
            if (own != PollutionState.Warning) return false;
            return secondsToConfirm <= Math.Max(MinimumWalkSeconds, walkOutSeconds) || sinceWarning >= ReactionSeconds(level, botSeed);
        }

        // Chance that a bot with a key and a warning on its region uses the key (S8-A9 KeyUseChance; the dial that tunes the key
        // dropout toward the simulation's 25-28%). Fixed per bot.
        public static float KeyUseChance(BotDifficulty level) => level == BotDifficulty.Easy ? 0.5f : level == BotDifficulty.Hard ? 1f : 0.8f;

        public static bool WantsKey(BotDifficulty level, int botSeed) =>
            new Random(unchecked(botSeed * 197 + 3)).NextDouble() < KeyUseChance(level);

        // The final region is known to a player once this many regions are warned or contaminated: ceil(2/3 of the regions) (M7).
        public static int KnownFinalThreshold(int regionCount) => Math.Max(1, (int)Math.Ceiling(regionCount * 2.0 / 3.0));

        public static bool KnowsFinal(int warnedOrContaminated, int regionCount) => warnedOrContaminated >= KnownFinalThreshold(regionCount);
    }
}

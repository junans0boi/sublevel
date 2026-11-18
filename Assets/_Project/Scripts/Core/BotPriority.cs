using System;

namespace Game.Core
{
    // What a bot is after, highest first (ADR-312, #376). The controller and the brain apply the order; this table is the one place it is written.
    public enum BotGoal { Escape = 0, Combat = 1, Farm = 2, Investigate = 3, Gather = 4, Travel = 5 }

    public struct PriorityInput
    {
        public bool EscapeUrgent;    // the pollution or the lockdown is on it (ZoneGoal urgent)
        public bool Combat;          // an enemy in sight, a retreat or a heal going on
        public bool Farming;         // still kitting itself out (BotFarm)
        public bool NoiseHeard;      // a noise it has not looked into yet
        public bool Investigates;    // this bot goes to noises (BotPriority.Investigates)
        public bool GatherGoal;      // the lockdown wants it in the final region (not urgent)
    }

    public static class BotPriority
    {
        // Escape the pollution/lockdown > fight, retreat, heal > finish farming > look into a noise > gather in the final region > walk between towns.
        public static BotGoal Choose(in PriorityInput i)
        {
            if (i.EscapeUrgent) return BotGoal.Escape;
            if (i.Combat) return BotGoal.Combat;
            if (i.Farming) return BotGoal.Farm;
            if (i.NoiseHeard && i.Investigates) return BotGoal.Investigate;
            if (i.GatherGoal) return BotGoal.Gather;
            return BotGoal.Travel;
        }

        // A bot's own fixed number in [0, 1) from its seed: it goes to noises when the number is under the level's chance (so the share of
        // bots that go converges on the chance: 0.4 easy, 0.7 normal, 1.0 hard).
        public static float Roll(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u + 0x9E3779B9u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        public static bool Investigates(float roll, float chance) => roll < chance;
    }
}

using System;

namespace Game.Core
{
    // How bots use what they carry (#148, ADR-116): which throwable at which target, the launch pitch that lands it,
    // which pickups they still want. Pure, so EditMode tests can pin the numbers.
    public static class BotItems
    {
        // Launch pitch (degrees above the horizontal, the flat arc) for a throw at speed `speed` to land `distance`
        // metres away and `rise` metres above the hand. False when the target is out of reach.
        public static bool TryLaunchPitch(float distance, float rise, float speed, float gravity, out float pitchDeg)
        {
            pitchDeg = 0f;
            if (distance < 0.5f || speed <= 0f || gravity <= 0f) return false;
            float v2 = speed * speed;
            float disc = v2 * v2 - gravity * (gravity * distance * distance + 2f * rise * v2);
            if (disc < 0f) return false;
            float tan = (v2 - (float)Math.Sqrt(disc)) / (gravity * distance);
            pitchDeg = (float)(Math.Atan(tan) * 180.0 / Math.PI);
            return true;
        }

        // The throwable to throw at an enemy `distance` away, or null. `roll` (0..1) breaks ties between a flash and a
        // grenade. A fire bottle only for someone standing still.
        public static ThrowableKind? PickThrow(float distance, bool targetStill, int frag, int flash, int molotov, float roll, BotRules r)
        {
            bool canMolotov = molotov > 0 && targetStill && distance >= r.MolotovMinMeters && distance <= r.MolotovMaxMeters;
            bool canFlash = flash > 0 && distance >= r.FlashMinMeters && distance <= r.FlashMaxMeters;
            bool canFrag = frag > 0 && distance >= r.FragMinMeters && distance <= r.FragMaxMeters;
            if (canMolotov) return ThrowableKind.Molotov;
            if (canFlash && (!canFrag || roll < 0.5f)) return ThrowableKind.Flash;
            if (canFrag) return ThrowableKind.Frag;
            return null;
        }

        // Does a bot with these counts still want this pickup? (Weapons, ammo and medkits are BotBrain.WantsLoot.)
        public static bool WantsLoot(LootKind kind, int bandages, int firstAids, int drinks, int pills, int syringes,
            int frags, int smokes, int flashes, int molotovs, BotRules r)
        {
            switch (kind)
            {
                case LootKind.Bandage: return bandages + firstAids < r.HealItemsWanted;
                case LootKind.FirstAidKit: return bandages + firstAids < r.HealItemsWanted;
                case LootKind.Drink: return drinks < r.BoostWanted;
                case LootKind.Pill: return pills < r.BoostWanted;
                case LootKind.Syringe: return syringes < r.BoostWanted;
                case LootKind.Grenade: return frags < r.ThrowablesWanted;
                case LootKind.Smoke: return smokes < r.ThrowablesWanted;
                case LootKind.Flash: return flashes < r.ThrowablesWanted;
                case LootKind.Molotov: return molotovs < r.ThrowablesWanted;
                default: return false;
            }
        }

        // Out of sight, should the bot drink/inject a boost item now?
        public static bool WantsBoost(float healthFraction, double now, double lastEnemySeenAt, int boostItems, bool busy, BotRules r) =>
            boostItems > 0 && !busy && (healthFraction < r.BoostBelowFraction || now - lastEnemySeenAt < r.BoostThreatSeconds);
    }
}

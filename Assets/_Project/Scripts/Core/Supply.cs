using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The facility supply crate (#144, ADR-113): when a lockdown stage begins, an elevator in a still-safe region
    // sends a crate down, marked by a coloured signal smoke. High-tier loot from a table of its own.
    [Serializable]
    public class SupplyRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static SupplyRules Current = new SupplyRules();

        public int FirstStage = 2;           // the first lockdown stage (1 = first region to close) that sends a crate
        public int EveryStages = 2;          // then every this many stages
        public int ItemCount = 6;            // what a crate holds (the first four are the guaranteed picks)
        public float DescentSeconds = 6f;    // the crate is on its way down this long and cannot be opened yet
        public float OpenSeconds = 3f;       // F held this long next to it opens it
        public float ReachMeters = 3f;       // horizontal reach for opening
        public float SmokeSeconds = 150f;    // the signal smoke lasts this long (the crate stays until opened)
        public float DropHeightMeters = 45f; // where the crate starts its descent, above the landing spot
        public float RingRadius = 1.4f;      // metres from the crate to each item once opened
        public int BotWeight = 6;            // how many times the crate counts among the places bots roam to
    }

    // Pure rules of the supply crate.
    public static class SupplyPlan
    {
        // Does the start of lockdown stage `stage` (1-based) send a crate?
        public static bool DropAt(int stage, SupplyRules rules)
        {
            if (stage < 1 || stage < rules.FirstStage) return false;
            return rules.EveryStages <= 1 || (stage - rules.FirstStage) % rules.EveryStages == 0;
        }

        // The region a crate comes down in: one that is still safe, picked by the roll. -1 when none is left.
        public static int PickRegion(IReadOnlyList<ZoneState> states, Random rng)
        {
            int safe = 0;
            for (int i = 0; i < states.Count; i++) if (states[i] == ZoneState.Safe) safe++;
            if (safe == 0) return -1;
            int pick = rng.Next(safe);
            for (int i = 0; i < states.Count; i++)
                if (states[i] == ZoneState.Safe && pick-- == 0) return i;
            return -1;
        }

        // What the crate holds: a gun, armour or a backpack, a boost and an attachment are guaranteed (when the
        // table has them), the rest is rolled from the whole table. Same seed, same contents.
        public static List<LootEntry> Contents(LootTable table, int count, int seed)
        {
            var result = new List<LootEntry>(count);
            var rng = new Random(seed);
            Func<LootEntry, bool>[] picks =
            {
                e => e.Kind == LootKind.Weapon,
                e => e.Kind == LootKind.Helmet || e.Kind == LootKind.Vest || e.Kind == LootKind.Backpack,
                e => e.Kind == LootKind.Drink || e.Kind == LootKind.Pill || e.Kind == LootKind.Syringe,
                e => e.Kind == LootKind.Optic,
            };
            foreach (var wanted in picks)
            {
                if (result.Count >= count) break;
                var e = Roll(table, rng, wanted);
                if (e != null) result.Add(e);
            }
            while (result.Count < count)
            {
                var e = Roll(table, rng, null);
                if (e == null) break;
                result.Add(e);
            }
            return result;
        }

        static LootEntry Roll(LootTable table, Random rng, Func<LootEntry, bool> filter)
        {
            float total = 0f;
            foreach (var e in table.Entries) if (filter == null || filter(e)) total += e.Weight;
            if (total <= 0f) return null;
            double pick = rng.NextDouble() * total;
            LootEntry last = null;
            foreach (var e in table.Entries)
            {
                if (filter != null && !filter(e)) continue;
                last = e;
                pick -= e.Weight;
                if (pick < 0) return e;
            }
            return last;
        }

        // Opening progress 0..1 after `dt` more seconds: grows while F is held in reach, drops back otherwise.
        public static float Hold(float progress, bool held, bool inReach, float dt, float openSeconds) =>
            held && inReach ? Math.Min(1f, progress + dt / Math.Max(0.05f, openSeconds)) : 0f;
    }
}

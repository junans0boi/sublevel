using System;

namespace Game.Core
{
    // Bot names (S8-A5, ADR-245): invented nicknames only (no real person, brand or streamer). A match seed shuffles the pool
    // once, so bot i gets the i-th shuffled name: unique inside a match and the same on every call. Past the pool a number is added.
    public static class BotNames
    {
        public static readonly string[] Pool =
        {
            "하늘", "루나", "노을", "새벽", "여울", "바람", "솔잎", "마루", "다온", "보리", "구름", "이슬", "달빛", "서리", "도담", "가람",
            "Ash", "Birch", "Cinder", "Dusk", "Ember", "Fjord", "Gale", "Haze", "Iris", "Juniper", "Kestrel", "Lumen", "Moss", "Nimbus",
            "Onyx", "Pebble", "Quill", "Raven", "Sable", "Thistle", "Umber", "Vesper", "Willow", "Yarrow", "Zephyr", "Alder", "Basalt", "Cobalt",
            "Drift", "Echo", "Flint", "Glint", "Harbor", "Indigo", "Jade", "Kite", "Larch", "Marlin", "Nettle", "Opal", "Pine", "Quartz",
            "Reed", "Slate", "Tern", "Ultra", "Vale", "Wren",
        };

        public static string For(int matchSeed, int index)
        {
            if (index < 0) index = 0;
            int n = Pool.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var rng = new Random(unchecked(matchSeed * 7919 + 13));
            for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
            string name = Pool[order[index % n]];
            return index < n ? name : name + (index / n + 1);
        }
    }

    // Bot difficulty mix (S8-A4, ADR-245): the setting is the centre of a mix, each bot gets its own level from the match seed
    // and its number, fixed for the match. Shares are percent easy/normal/hard.
    public static class BotMix
    {
        public static void Shares(BotDifficulty setting, out int easy, out int normal, out int hard)
        {
            switch (setting)
            {
                case BotDifficulty.Easy: easy = 50; normal = 40; hard = 10; break;
                case BotDifficulty.Hard: easy = 10; normal = 40; hard = 50; break;
                default: easy = 20; normal = 60; hard = 20; break;
            }
        }

        // Statistics runs (--perf-level): every bot has exactly the set level instead of the mix.
        public static bool Pure;

        public static BotDifficulty For(BotDifficulty setting, int matchSeed, int index)
        {
            if (Pure) return setting;
            Shares(setting, out int easy, out int normal, out _);
            int roll = new Random(unchecked(matchSeed * 31 + index * 7919 + 5)).Next(100);
            return roll < easy ? BotDifficulty.Easy : roll < easy + normal ? BotDifficulty.Normal : BotDifficulty.Hard;
        }
    }
}

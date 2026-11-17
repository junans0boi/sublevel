using System.Diagnostics;

namespace Game.Net
{
    // Per-frame stopwatches of the bots' work (ADR-292), summed over all bots in a frame and sampled by ServerPerfProbe.
    // Off unless the probe runs (--perf), so a normal match pays nothing.
    public static class BotPerf
    {
        public const int Ai = 0, Sense = 1, Loot = 2, Doors = 3, Path = 4, Slots = 5;
        public static readonly string[] Names = { "bot_ai", "sense", "loot", "doors", "path" };
        public static bool Enabled;
        static readonly long[] Ticks = new long[Slots];

        public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0L;
        public static void End(int slot, long begun) { if (begun != 0L) Ticks[slot] += Stopwatch.GetTimestamp() - begun; }

        // Milliseconds gathered in `slot` since the last take.
        public static float TakeMs(int slot)
        {
            float ms = Ticks[slot] * 1000f / Stopwatch.Frequency;
            Ticks[slot] = 0L;
            return ms;
        }
    }
}

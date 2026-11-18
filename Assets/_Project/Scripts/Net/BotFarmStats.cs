using System.Collections.Generic;

namespace Game.Net
{
    // What the bots' farming (ADR-294) achieved, for the perf log: when each bot first held a gun (seconds after its drop) and
    // how many are farming. Server only; the match director's server start clears it.
    public static class BotFarmStats
    {
        public static readonly List<float> FirstGunSeconds = new List<float>();
        public static int Farming;
        public static float FirstStartAt = -1f;   // Time.time of the first bot's drop (deaths are counted from it)
        public static int Bots;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() { FirstGunSeconds.Clear(); Farming = 0; FirstStartAt = -1f; Bots = 0; }

        // Median of the first-gun times so far (-1 when none).
        public static float MedianFirstGun()
        {
            if (FirstGunSeconds.Count == 0) return -1f;
            var sorted = new List<float>(FirstGunSeconds);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }
    }
}

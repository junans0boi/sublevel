using System.Collections.Generic;

namespace Game.Net
{
    // What the bots achieved in a match, per difficulty level (0 easy, 1 normal, 2 hard), for the 80-player statistics (ADR-313, #377).
    // Server only; the match start clears it. The bots register themselves; ServerPerfProbe writes the numbers.
    public static class BotMatchStats
    {
        public const int Levels = 3;
        public static readonly int[] Bots = new int[Levels], Kills = new int[Levels], Deaths = new int[Levels], StalledBots = new int[Levels];
        public static readonly List<float>[] FirstGun = { new List<float>(), new List<float>(), new List<float>() };
        public static int Engagements, FirstEngages, FirstEngagesArmed;
        public static float EngageSeconds;
        public static readonly List<float> DeathSeconds = new List<float>();   // of bots, since the drop
        static readonly Dictionary<int, int> LevelOfId = new Dictionary<int, int>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            for (int i = 0; i < Levels; i++) { Bots[i] = Kills[i] = Deaths[i] = StalledBots[i] = 0; FirstGun[i].Clear(); }
            Engagements = FirstEngages = FirstEngagesArmed = 0;
            EngageSeconds = 0f;
            LevelOfId.Clear();
            DeathSeconds.Clear();
        }

        public static void Register(int objectId, int level) { LevelOfId[objectId] = level; Bots[level]++; }

        public static bool TryLevel(int objectId, out int level) => LevelOfId.TryGetValue(objectId, out level);

        // A bot died: the one who shot it (when a bot) and the bot itself are credited per level.
        public static void Died(int victimId, int attackerId)
        {
            if (LevelOfId.TryGetValue(victimId, out int v)) { Deaths[v]++; if (BotFarmStats.FirstStartAt >= 0f) DeathSeconds.Add(UnityEngine.Time.time - BotFarmStats.FirstStartAt); }
            if (attackerId >= 0 && attackerId != victimId && LevelOfId.TryGetValue(attackerId, out int a)) Kills[a]++;
        }
    }
}

using Game.Core;
using UnityEngine;

namespace Game.Net
{
    public enum GuardKind { Move, Teleport, Fire, Slot, Item, Distance, Vehicle, Parkour, Rate, Arg }

    // Counts and logs what the server refused from a client (#170, ADR-142). The log is one line per kind every
    // GuardRules.LogIntervalSeconds, so a flood cannot fill it. Counters are per play session (tests and the net HUD read them).
    public static class ServerGuard
    {
        static readonly int[] Counts = new int[System.Enum.GetValues(typeof(GuardKind)).Length];
        static readonly float[] LoggedAt = new float[Counts.Length];
        static readonly int[] Muted = new int[Counts.Length];

        public static int Total { get; private set; }
        public static int Count(GuardKind kind) => Counts[(int)kind];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStats()
        {
            Total = 0;
            System.Array.Clear(Counts, 0, Counts.Length);
            System.Array.Clear(Muted, 0, Muted.Length);
            for (int i = 0; i < LoggedAt.Length; i++) LoggedAt[i] = float.NegativeInfinity;
        }

        // Always returns false so a refusal reads `return ServerGuard.Refuse(...)`.
        public static bool Refuse(GuardKind kind, Combatant who, string detail)
        {
            int k = (int)kind;
            Counts[k]++;
            Total++;
            float now = Time.realtimeSinceStartup;
            if (now - LoggedAt[k] < GameData.Rules.Guard.LogIntervalSeconds) { Muted[k]++; return false; }
            Debug.LogWarning($"[ServerGuard] refused {kind} from {(who != null ? who.name : "?")}: {detail}" + (Muted[k] > 0 ? $" (+{Muted[k]} more)" : ""));
            LoggedAt[k] = now;
            Muted[k] = 0;
            return false;
        }

        public static string Summary()
        {
            var s = "";
            for (int i = 0; i < Counts.Length; i++)
                if (Counts[i] > 0) s += (s.Length > 0 ? " " : "") + (GuardKind)i + " " + Counts[i];
            return s;
        }
    }
}

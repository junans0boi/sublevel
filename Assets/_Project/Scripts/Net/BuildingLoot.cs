using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The server's building loot index (ADR-291): built once when the match starts from the world plan, the spawn
    // points and the doors the door system made. Null on a scene without the generated map.
    public static class BuildingLoot
    {
        public static BuildingLootIndex Index { get; private set; }
        public static double BuildMilliseconds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Index = null;

        public static void Build(Transform[] spawns)
        {
            Index = null;
            var doors = DoorSystem.Instance;
            var plan = doors != null ? doors.Plan : null;
            if (plan == null || spawns == null || spawns.Length == 0) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var xs = new float[spawns.Length];
            var zs = new float[spawns.Length];
            for (int i = 0; i < spawns.Length; i++) { xs[i] = spawns[i].position.x; zs[i] = spawns[i].position.z; }
            var index = new BuildingLootIndex(plan.Lots, xs, zs);
            foreach (var door in doors.ExteriorDoors) index.AddDoor(door.Lot, door.Position.x, door.Position.y, door.Position.z);
            Index = index;
            BuildMilliseconds = watch.Elapsed.TotalMilliseconds;
            var histogram = new int[8];
            for (int b = 0; b < index.BuildingCount; b++) histogram[System.Math.Min(7, index.SpawnsOf(b).Count)]++;
            Debug.Log($"[BuildingLoot] spawns per building 0..7+: {string.Join(" ", histogram)}; {index.BuildingCount} buildings, {spawns.Length} spawns, unassigned {index.Unassigned}, ambiguous {index.Ambiguous}, {BuildMilliseconds:F1} ms (the plan itself comes from the door system)");
        }
    }
}

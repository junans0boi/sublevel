using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.AI
{
    // The pickups near a point, for the bots' eyes (ADR-290). The bots used to walk every item in the world on every
    // sense; items only appear and vanish, so one shared snapshot (rebuilt when the item count changes or every
    // RefreshSeconds) and a hash answer "what lies within 25 m" with a handful. Candidates come back in LootItem.All
    // order, so the pick is the same as with the full scan.
    public static class BotLootGrid
    {
        public const float RefreshSeconds = 0.5f;
        public const float Margin = 1f;   // slack for an item that moved since the snapshot

        static readonly PointGrid Grid = new PointGrid(25f);
        static readonly List<LootItem> Snapshot = new List<LootItem>();
        static readonly List<int> Hits = new List<int>();
        static float[] _xs = new float[0], _zs = new float[0];
        static float _at = float.NegativeInfinity;
        static int _count = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Snapshot.Clear(); _at = float.NegativeInfinity; _count = -1; }

        // Fills `result` (cleared first) with the items that may lie within `radius` of `at`.
        public static void Near(Vector3 at, float radius, List<LootItem> result)
        {
            var all = LootItem.All;
            float now = Time.time;
            if (all.Count != _count || now - _at >= RefreshSeconds) Rebuild(all, now);
            Grid.Query(at.x, at.z, radius + Margin, Hits);
            result.Clear();
            for (int i = 0; i < Hits.Count; i++) result.Add(Snapshot[Hits[i]]);
        }

        static void Rebuild(List<LootItem> all, float now)
        {
            _at = now;
            _count = all.Count;
            Snapshot.Clear();
            if (_xs.Length < all.Count) { _xs = new float[all.Count * 2]; _zs = new float[all.Count * 2]; }
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var item = all[i];
                if (item == null) continue;
                var p = item.transform.position;
                _xs[n] = p.x; _zs[n] = p.z; n++;
                Snapshot.Add(item);
            }
            Grid.Rebuild(_xs, _zs, n);
        }
    }
}

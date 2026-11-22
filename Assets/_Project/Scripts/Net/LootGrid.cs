using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The pickups near a point (ADR-290; was BotLootGrid). Walking every item in the world on every look was the
    // bots' sense spike and the local player's per-frame F search (#484); items only appear and vanish, so one shared
    // snapshot (rebuilt when the item set changes or every RefreshSeconds) and a hash answer "what lies within 25 m"
    // with a handful. Candidates come back in LootItem.All order, so the pick is the same as with the full scan.
    public static class LootGrid
    {
        public const float RefreshSeconds = 0.5f;
        public const float Margin = 1f;   // slack for an item that moved since the snapshot

        static readonly PointGrid Grid = new PointGrid(25f);
        static readonly List<LootItem> Snapshot = new List<LootItem>();
        static readonly List<int> Hits = new List<int>();
        static float[] _xs = new float[0], _zs = new float[0];
        static float _at = float.NegativeInfinity;
        static int _version = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Snapshot.Clear(); _at = float.NegativeInfinity; _version = -1; }

        // Fills `result` (cleared first) with the items that may lie within `radius` of `at`.
        public static void Near(Vector3 at, float radius, List<LootItem> result)
        {
            var all = LootItem.All;
            float now = Time.time;
            if (LootItem.Version != _version || now - _at >= RefreshSeconds || now < _at) Rebuild(all, now);
            Grid.Query(at.x, at.z, radius + Margin, Hits);
            result.Clear();
            for (int i = 0; i < Hits.Count; i++) result.Add(Snapshot[Hits[i]]);
        }

        static void Rebuild(List<LootItem> all, float now)
        {
            _at = now;
            _version = LootItem.Version;
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

using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Counts the floors and walls between a sound and the ear with one ray (ADR-251). The numbers become a gain and a
    // low-pass cutoff in SoundOcclusion. People, vehicles and doors don't count (doors have their own rule, Doors.HeardMeters).
    public static class SoundOcclusionProbe
    {
        static readonly RaycastHit[] Hits = new RaycastHit[16];
        public static readonly SoundOcclusion.Budget Budget = new SoundOcclusion.Budget();

        public static Occlusion Between(Vector3 source, Vector3 ear, bool throughStairs = false)
        {
            var d = ear - source;
            float length = d.magnitude;
            if (length < 1.5f) return Occlusion.Open;
            int n = Physics.RaycastNonAlloc(source, d / length, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (n == 0) return Occlusion.Open;
            System.Array.Sort(Hits, 0, n, HitOrder.Instance);
            int floors = 0, walls = 0;
            float last = -10f;
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c is TerrainCollider || c.GetComponentInParent<Combatant>() != null || c.GetComponentInParent<Vehicle>() != null || c.GetComponentInParent<Door>() != null) continue;
                if (Hits[i].distance - last < SoundOcclusion.MergeMeters) continue;
                last = Hits[i].distance;
                if (SoundOcclusion.IsFloor(Hits[i].normal.y)) floors++; else walls++;
            }
            return SoundOcclusion.Of(floors, walls, throughStairs);
        }

        sealed class HitOrder : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitOrder Instance = new HitOrder();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}

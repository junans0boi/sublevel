using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // ADR-276: what the player's eyes can take. A pickup is a candidate only when it is in reach and on this storey
    // (Interact rules), lies in front (view cone), and nothing solid is between the eye and it (walls, floors, ceilings,
    // furniture, closed doors). The client lists these; the server asks the same question again (Combatant.PickupInReach).
    public static class PickupSight
    {
        // A pickup's point sits this high over the floor it lies on (WorldBuilder.LootLift, drops: transform + 0.3).
        public const float PivotLift = 0.3f;
        const float EndPad = 0.04f;
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        // Nothing solid between `eye` and the pickup (one of two points on it: low and at its point).
        public static bool Clear(Vector3 eye, Transform item, Transform self)
        {
            var rules = InteractRules.Current;
            var pivot = item.position;
            return ClearTo(eye, pivot + Vector3.down * (PivotLift - rules.SightLiftMeters), item, self)
                || ClearTo(eye, pivot + Vector3.down * (PivotLift - rules.SightTopMeters), item, self);
        }

        static bool ClearTo(Vector3 eye, Vector3 target, Transform item, Transform self)
        {
            var to = target - eye;
            float distance = to.magnitude - EndPad;
            if (distance <= 0.01f) return true;
            int n = Physics.RaycastNonAlloc(eye, to / (distance + EndPad), Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c.transform.IsChildOf(item) || (self != null && c.transform.IsChildOf(self))) continue;
                // Bodies, vehicles and thrown things move: only the fixed world hides a pickup.
                if (c.attachedRigidbody != null || c.GetComponentInParent<Combatant>() != null) continue;
                return false;
            }
            return true;
        }

        // Server and client: the pickup in reach (with `slack` for a late body), on this storey, in sight. No view cone.
        public static bool Reachable(Vector3 feet, Vector3 eye, Transform item, Transform self, float slack)
        {
            var rules = InteractRules.Current;
            var p = item.position;
            float reach = rules.ReachMeters + slack;
            if ((p - feet).sqrMagnitude > reach * reach) return false;
            if (!Interact.SameFloor(feet.y, p.y - PivotLift + rules.SightLiftMeters, rules)) return false;
            return Clear(eye, item, self);
        }

        static readonly List<LootItem> Near = new List<LootItem>();

        // Client: every pickup F could take from here looking along `look`, with its bearing off the view (degrees).
        // `items` and `bearings` are filled in step; `skip` pickups (just asked for) are left out.
        public static void Gather(Combatant me, Vector3 look, List<LootItem> items, List<float> bearings, System.Func<LootItem, bool> skip = null)
        {
            items.Clear();
            bearings.Clear();
            if (me == null) return;
            var rules = InteractRules.Current;
            var feet = me.transform.position;
            var eye = me.EyePosition;
            LootGrid.Near(feet, rules.ReachMeters, Near); // #484: a handful from the hash, not all ~1,600 every frame
            foreach (var item in Near)
            {
                if (item == null || !item.IsSpawned || (skip != null && skip(item))) continue;
                var p = item.transform.position;
                float floorY = p.y - PivotLift + rules.SightLiftMeters;
                if (!Interact.Candidate(feet.x, feet.y, feet.z, eye.x, eye.z, look.x, look.z, p.x, floorY, p.z, rules, out float bearing)) continue;
                if (!Clear(eye, item.transform, me.transform)) continue;
                items.Add(item);
                bearings.Add(bearing);
            }
        }
    }
}

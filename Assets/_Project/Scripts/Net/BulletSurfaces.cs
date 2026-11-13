using System;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Marks a collider's bullet surface where its place in the world does not tell (props, tests).
    public class BulletSurfaceTag : MonoBehaviour
    {
        public BulletSurface Surface = BulletSurface.Concrete;
    }

    // What a shot hit, from the collider alone (ADR-114). Anything unknown is concrete: it stops the bullet, as before.
    public static class BulletSurfaces
    {
        public static BulletSurface Of(Collider c)
        {
            if (c is TerrainCollider) return BulletSurface.Ground;
            var tag = c.GetComponentInParent<BulletSurfaceTag>();
            if (tag != null) return tag.Surface;
            if (c.GetComponentInParent<Combatant>() != null) return BulletSurface.Body;
            if (c.GetComponentInParent<Vehicle>() != null) return BulletSurface.Vehicle;
            if (c.GetComponentInParent<Door>() != null) return BulletSurface.Wood;
            // Building boxes sit on a "Colliders" child of "Lot03_House" / "Station01" (as CombatantAudio.SurfaceAt reads them).
            var root = c.transform.parent;
            if (c.name != "Colliders" || root == null) return BulletSurface.Concrete;
            if (root.name.StartsWith("Station", StringComparison.Ordinal)) return BulletSurface.Concrete;
            if (!Enum.TryParse(root.name.Substring(root.name.LastIndexOf('_') + 1), out BuildingKind kind)) return BulletSurface.Concrete;
            // shortcut: every box of a building is its main material (wooden houses, sheet-metal sheds and halls); props and roofs are not told apart.
            return kind == BuildingKind.House ? BulletSurface.Wood : kind == BuildingKind.ElevatorStation ? BulletSurface.Concrete : BulletSurface.Metal;
        }

        // Metres of `hit.collider` along the shot, or infinity when the far side cannot be found within `limit`.
        public static float Thickness(in RaycastHit hit, Vector3 dir, float limit, out Vector3 exit)
        {
            exit = hit.point;
            var from = hit.point + dir * (limit + 0.02f);
            if (!hit.collider.Raycast(new Ray(from, -dir), out var back, limit + 0.02f)) return float.PositiveInfinity;
            exit = back.point;
            return Vector3.Distance(hit.point, back.point);
        }
    }
}

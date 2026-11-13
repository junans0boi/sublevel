using System;
using UnityEngine;

namespace Game.Gameplay
{
    // The one marker the local player puts on the full map. Shared so the minimap and the HUD can draw it too.
    // Y is the height of the player when it was placed; the map only uses X and Z.
    public static class MapMarker
    {
        public static bool Has { get; private set; }
        public static Vector3 Position { get; private set; }
        public static event Action Changed;

        public static void Set(Vector3 position)
        {
            Position = position;
            Has = true;
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (!Has) return;
            Has = false;
            Changed?.Invoke();
        }

        // Horizontal distance in metres from a point to the marker; 0 when there is none.
        public static float DistanceFrom(Vector3 from) =>
            Has ? Game.Core.MapProjection.Distance(from.x, from.z, Position.x, Position.z) : 0f;

        // Compass bearing (0 = north, 90 = east) from a point to the marker.
        public static float BearingFrom(Vector3 from) =>
            Has ? Game.Core.MapProjection.BearingDeg(from.x, from.z, Position.x, Position.z) : 0f;
    }
}

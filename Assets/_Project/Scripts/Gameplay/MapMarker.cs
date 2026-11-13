using System;
using UnityEngine;

namespace Game.Gameplay
{
    // The one marker the local player puts down: a click on the full map or a ping at the crosshair (middle mouse,
    // ADR-065 (6)). Shared so the compass strip, the minimap and the in-world icon draw the same point.
    // Y is a ground height: the hit point of a ping, or the terrain under a map click.
    //
    // Team pings are out of scope (single player), but the marker already carries its owner and colour so a
    // per-player set can replace this one without changing what the views read.
    public static class MapMarker
    {
        public const int LocalOwner = 0;
        // Walking this close (horizontal metres) to the marker clears it.
        public const float ReachMeters = 10f;

        public static bool Has { get; private set; }
        public static Vector3 Position { get; private set; }
        public static int OwnerId { get; private set; } = LocalOwner;
        public static Color Colour { get; private set; } = UiKit.Accent;
        // Bumped on every change, so views that poll can tell a new marker from the one they placed.
        public static int Version { get; private set; }
        public static event Action Changed;

        // Cleared on arrival only after the player has been farther than ReachMeters from it, so a marker put down
        // right next to the player does not vanish the same frame.
        static bool _armed;

        public static void Set(Vector3 position) => Set(position, LocalOwner, UiKit.Accent);

        public static void Set(Vector3 position, int ownerId, Color colour)
        {
            Position = position;
            OwnerId = ownerId;
            Colour = colour;
            Has = true;
            _armed = false;
            Version++;
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (!Has) return;
            Has = false;
            _armed = false;
            Version++;
            Changed?.Invoke();
        }

        // Called once a frame with the local player's position: clears the marker when it is reached.
        // Returns true when it was cleared by this call.
        public static bool TickReach(Vector3 playerPosition)
        {
            if (!Has) return false;
            if (DistanceFrom(playerPosition) > ReachMeters)
            {
                _armed = true;
                return false;
            }
            if (!_armed) return false;
            Clear();
            return true;
        }

        // Horizontal distance in metres from a point to the marker; 0 when there is none.
        public static float DistanceFrom(Vector3 from) =>
            Has ? Game.Core.MapProjection.Distance(from.x, from.z, Position.x, Position.z) : 0f;

        // Compass bearing (0 = north, 90 = east) from a point to the marker.
        public static float BearingFrom(Vector3 from) =>
            Has ? Game.Core.MapProjection.BearingDeg(from.x, from.z, Position.x, Position.z) : 0f;
    }

    // "235m" labels for the marker views, each built once and kept: walking toward a marker revisits the same
    // values, so the compass and the in-world icon never allocate a label twice.
    public static class MapMarkerText
    {
        const int Cached = 2048; // past the island's diagonal
        static readonly string[] Labels = new string[Cached];

        public static string Metres(int metres)
        {
            if (metres < 0) metres = 0;
            if (metres >= Cached) return metres + "m";
            return Labels[metres] ??= metres + "m";
        }
    }
}

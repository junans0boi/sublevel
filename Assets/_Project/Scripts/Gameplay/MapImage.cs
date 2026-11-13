using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // The baked world map picture (Editor/MapBaker.cs writes Resources/Map/world_map.png) and the world <-> picture
    // conversion the full map, the minimap and the HUD share. The projection is Game.Core.MapProjection, the same one
    // the baker uses: uv (0, 0) is the south-west corner of the island, u runs east and v north.
    public static class MapImage
    {
        public const string ResourcePath = "Map/world_map";
        public const float DefaultSize = 1024f;

        static Texture2D _texture;

        // The picture, or null when it has not been baked yet.
        public static Texture2D Get()
        {
            if (_texture == null) _texture = Resources.Load<Texture2D>(ResourcePath);
            return _texture;
        }

        // Side of the square map in metres: the current world's, or the default one.
        public static float WorldSize => WorldInfo.Current != null && WorldInfo.Current.Size > 0f ? WorldInfo.Current.Size : DefaultSize;

        // Not clamped: points off the map give values outside 0..1.
        public static Vector2 WorldToUv(Vector3 world)
        {
            MapProjection.WorldToUv(WorldSize, world.x, world.z, out float u, out float v);
            return new Vector2(u, v);
        }

        // On the ground plane (y = 0).
        public static Vector3 UvToWorld(Vector2 uv)
        {
            MapProjection.UvToWorld(WorldSize, uv.x, uv.y, out float x, out float z);
            return new Vector3(x, 0f, z);
        }
    }
}

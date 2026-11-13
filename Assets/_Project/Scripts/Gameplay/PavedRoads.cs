using UnityEngine;

namespace Game.Gameplay
{
    // Which ground is asphalt. The road strips of the generated World have no colliders (the ground under a road
    // is the terrain, painted dirt), so the footstep lookup cannot learn "road" from a raycast; instead the
    // "Asphalt*" meshes are rasterised once into a 1 m grid of the map and looked up by position.
    public static class PavedRoads
    {
        const float Cell = 1f;
        static WorldInfo _owner;
        static bool[] _paved;
        static int _cells;

        public static bool Contains(Vector3 point)
        {
            var world = WorldInfo.Current;
            if (world == null) return false;
            if (world != _owner) Build(world);
            int x = Mathf.FloorToInt(point.x / Cell), z = Mathf.FloorToInt(point.z / Cell);
            return x >= 0 && z >= 0 && x < _cells && z < _cells && _paved[z * _cells + x];
        }

        static void Build(WorldInfo world)
        {
            _owner = world;
            _cells = Mathf.Max(1, Mathf.CeilToInt(world.Size / Cell));
            _paved = new bool[_cells * _cells];
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || !filter.gameObject.name.StartsWith("Asphalt", System.StringComparison.Ordinal)) continue;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var to = filter.transform;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                    Fill(to.TransformPoint(vertices[triangles[i]]), to.TransformPoint(vertices[triangles[i + 1]]), to.TransformPoint(vertices[triangles[i + 2]]));
            }
        }

        // Marks every grid cell whose centre lies inside the triangle seen from above.
        static void Fill(Vector3 a, Vector3 b, Vector3 c)
        {
            float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) / Cell)), x1 = Mathf.Min(_cells - 1, Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.z, b.z, c.z) / Cell)), z1 = Mathf.Min(_cells - 1, Mathf.FloorToInt(Mathf.Max(a.z, b.z, c.z) / Cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = (x + 0.5f) * Cell, pz = (z + 0.5f) * Cell;
                    float w1 = ((px - a.x) * (c.z - a.z) - (c.x - a.x) * (pz - a.z)) / area;
                    float w2 = ((b.x - a.x) * (pz - a.z) - (px - a.x) * (b.z - a.z)) / area;
                    if (w1 >= -0.02f && w2 >= -0.02f && w1 + w2 <= 1.02f) _paved[z * _cells + x] = true;
                }
        }
    }
}

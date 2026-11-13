using Game.Net;
using UnityEngine;

namespace Game.Tests
{
    // A basin of water for tests and review shots (ADR-112): a floor, four walls and a WaterVolume `depth` metres deep.
    // The map has no swimmable water yet (see the follow-up issue), so this is where swimming gets exercised.
    public static class TestPool
    {
        public static GameObject Create(Vector3 centre, float size, float depth, out WaterVolume water)
        {
            var root = new GameObject("TestPool");
            root.transform.position = centre;
            Box(root.transform, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(size + 2f, 1f, size + 2f));
            float wall = depth + 1f;
            foreach (var s in new[] { 1f, -1f })
            {
                Box(root.transform, "WallX", new Vector3(s * (size / 2f + 0.5f), wall / 2f, 0f), new Vector3(1f, wall, size + 2f));
                Box(root.transform, "WallZ", new Vector3(0f, wall / 2f, s * (size / 2f + 0.5f)), new Vector3(size + 2f, wall, 1f));
            }
            water = WaterVolume.Create(root.transform, new Vector2(centre.x, centre.z), new Vector2(size, size), centre.y + depth, true);
            return root;
        }

        static void Box(Transform parent, string name, Vector3 local, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
        }
    }
}

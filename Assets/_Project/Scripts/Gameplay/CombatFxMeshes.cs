using UnityEngine;

namespace Game.Gameplay
{
    // Meshes for shot effects, built once and shared. Their soft edges come from vertex colours (opaque in the
    // middle, clear at the rim), which the particle shader multiplies in, so no glow texture is needed.
    public static class CombatFxMeshes
    {
        const int DiscSegments = 12;

        static Mesh _disc, _muzzleFlash;

        // A disc of diameter 1 in the XY plane.
        public static Mesh Disc
        {
            get
            {
                if (_disc == null) _disc = Build("FxDisc", false);
                return _disc;
            }
        }

        // A disc of diameter 1 seen from the front, plus two crossed spikes along +Z that show from the side.
        public static Mesh MuzzleFlash
        {
            get
            {
                if (_muzzleFlash == null) _muzzleFlash = Build("FxMuzzleFlash", true);
                return _muzzleFlash;
            }
        }

        static Mesh Build(string name, bool spikes)
        {
            int vertexCount = 1 + DiscSegments + (spikes ? 10 : 0);
            int indexCount = DiscSegments * 3 + (spikes ? 24 : 0);
            var vertices = new Vector3[vertexCount];
            var colors = new Color32[vertexCount];
            var triangles = new int[indexCount];
            var solid = new Color32(255, 255, 255, 255);
            var clear = new Color32(255, 255, 255, 0);

            vertices[0] = Vector3.zero;
            colors[0] = solid;
            for (int i = 0; i < DiscSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / DiscSegments;
                vertices[1 + i] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);
                colors[1 + i] = clear;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + i;
                triangles[i * 3 + 2] = 1 + (i + 1) % DiscSegments;
            }

            if (spikes)
            {
                int v = 1 + DiscSegments, t = DiscSegments * 3;
                AddSpike(vertices, colors, triangles, ref v, ref t, Vector3.right, solid, clear);
                AddSpike(vertices, colors, triangles, ref v, ref t, Vector3.up, solid, clear);
            }

            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        // A long diamond in the plane of `side` and +Z: bright at its core, clear at the edges and the tip.
        static void AddSpike(Vector3[] vertices, Color32[] colors, int[] triangles, ref int v, ref int t, Vector3 side, Color32 solid, Color32 clear)
        {
            int core = v, tail = v + 1, left = v + 2, tip = v + 3, right = v + 4;
            vertices[core] = new Vector3(0f, 0f, 0.3f); colors[core] = solid;
            vertices[tail] = new Vector3(0f, 0f, -0.15f); colors[tail] = clear;
            vertices[left] = -side * 0.3f + new Vector3(0f, 0f, 0.25f); colors[left] = clear;
            vertices[tip] = new Vector3(0f, 0f, 1.6f); colors[tip] = clear;
            vertices[right] = side * 0.3f + new Vector3(0f, 0f, 0.25f); colors[right] = clear;
            v += 5;
            triangles[t++] = core; triangles[t++] = tail; triangles[t++] = left;
            triangles[t++] = core; triangles[t++] = left; triangles[t++] = tip;
            triangles[t++] = core; triangles[t++] = tip; triangles[t++] = right;
            triangles[t++] = core; triangles[t++] = right; triangles[t++] = tail;
        }
    }
}

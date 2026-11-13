using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    // Melee weapon models built in code (ADR-073): plain generic tools, no model asset, no markings or logos.
    // Model space, in metres: the origin is where the hand grips, +y runs along the handle to the head, +z is the
    // side that strikes (a blade's edge, the bottom of the pan, the crowbar's hook), +x is the flat side.
    // Three sub-meshes: Metal (bare steel), Grip (handle: wood, rubber or coating) and Paint (a painted part).
    // Built once per weapon and kept for the session. Used in the hand (MeleeModel), on the ground and for icons.
    public static class MeleeMeshes
    {
        public const int Metal = 0, Grip = 1, Paint = 2, SubMeshCount = 3;
        public const int Machete = 21, Crowbar = 22, Sickle = 23, Pan = 24;
        // The melee weapon ids that have a model, in weapons.json order.
        public static readonly int[] Ids = { Machete, Crowbar, Sickle, Pan };

        static readonly Dictionary<int, Mesh> Models = new Dictionary<int, Mesh>();

        public static bool Has(int weaponId) => System.Array.IndexOf(Ids, weaponId) >= 0;

        // Null for an id without a model.
        public static Mesh Model(int weaponId)
        {
            if (!Has(weaponId)) return null;
            if (Models.TryGetValue(weaponId, out var mesh) && mesh != null) return mesh;
            var d = new Draft();
            switch (weaponId)
            {
                case Machete: BuildMachete(d); break;
                case Crowbar: BuildCrowbar(d); break;
                case Sickle: BuildSickle(d); break;
                default: BuildPan(d); break;
            }
            mesh = d.ToMesh("Melee" + weaponId);
            Models[weaponId] = mesh;
            return mesh;
        }

        // A 45 cm blade with a straight spine and a slightly bellied edge, a small guard and a round handle.
        static void BuildMachete(Draft d)
        {
            d.Tube(new Vector3(0f, -0.075f, 0f), new Vector3(0f, 0.07f, 0f), 0.016f, Grip, 10);
            d.Box(new Vector3(0f, 0.074f, 0.008f), new Vector3(0.018f, 0.008f, 0.06f), Metal);
            // Sections from the guard to the tip: spine point and edge point (z, y).
            var back = new[] { new Vector2(-0.012f, 0.078f), new Vector2(-0.012f, 0.30f), new Vector2(-0.012f, 0.45f), new Vector2(-0.004f, 0.51f), new Vector2(0.022f, 0.535f) };
            var edge = new[] { new Vector2(0.034f, 0.078f), new Vector2(0.042f, 0.30f), new Vector2(0.047f, 0.45f), new Vector2(0.04f, 0.51f), new Vector2(0.024f, 0.534f) };
            d.Blade(back, edge, 0.0025f, 0.0006f, Metal);
        }

        // A 65 cm hex-ish steel bar, painted, with the hook bent forward at the top and a flat bent pry end below.
        static void BuildCrowbar(Draft d)
        {
            const float r = 0.011f;
            d.Tube(new Vector3(0f, -0.12f, 0f), new Vector3(0f, 0.43f, 0f), r, Paint, 6);
            // The hook: an arc bending toward +z and back down, ending in a split claw.
            Vector3 prev = new Vector3(0f, 0.43f, 0f);
            const int steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.PI * i / steps; // 0..180 degrees over the top
                var p = new Vector3(0f, 0.43f + Mathf.Sin(a) * 0.055f, 0.055f - Mathf.Cos(a) * 0.055f);
                d.Tube(prev, p, r, i < steps ? Paint : Metal, 6);
                prev = p;
            }
            d.Tube(prev, prev + new Vector3(0f, -0.035f, 0.004f), r * 0.8f, Metal, 6);
            // The pry end: a short flattened bend toward -z.
            d.Box(new Vector3(0f, -0.135f, -0.008f), new Vector3(0.022f, 0.03f, 0.012f), Metal);
        }

        // A short wooden handle and a crescent blade curving forward over the top, sharp on the inside.
        static void BuildSickle(Draft d)
        {
            d.Tube(new Vector3(0f, -0.07f, 0f), new Vector3(0f, 0.085f, 0f), 0.015f, Grip, 10);
            d.Tube(new Vector3(0f, 0.085f, 0f), new Vector3(0f, 0.1f, 0f), 0.017f, Metal, 10);
            var centre = new Vector2(0.085f, 0.17f);
            const int sections = 9;
            var outer = new Vector2[sections];
            var inner = new Vector2[sections];
            for (int i = 0; i < sections; i++)
            {
                float t = i / (float)(sections - 1);
                float a = Mathf.Lerp(200f, 15f, t) * Mathf.Deg2Rad;
                const float rOut = 0.115f;
                float width = Mathf.Lerp(0.03f, 0.002f, t * t);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                outer[i] = centre + dir * rOut;
                inner[i] = centre + dir * (rOut - width);
            }
            d.Blade(outer, inner, 0.002f, 0.0005f, Metal);
        }

        // A 24 cm round pan facing +z with a flat handle; the cooking side (toward -z) is a dark coating.
        static void BuildPan(Draft d)
        {
            d.Box(new Vector3(0f, 0.01f, -0.015f), new Vector3(0.024f, 0.2f, 0.012f), Grip);
            d.Tube(new Vector3(0f, 0.11f, -0.015f), new Vector3(0f, 0.13f, -0.015f), 0.008f, Metal, 8);
            var centre = new Vector3(0f, 0.25f, 0f);
            d.Tube(centre + new Vector3(0f, 0f, -0.035f), centre, 0.12f, Metal, 24);
            d.Disc(centre + new Vector3(0f, 0f, -0.0365f), 0.106f, Vector3.back, Grip, 24);
        }

        internal sealed class Draft
        {
            const float UvPerMetre = 8f;
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int>[] _t = { new List<int>(), new List<int>(), new List<int>() };

            // A flat quad a-b-c-d (in order round its edge), turned to face the side `facing` points to.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, int sub)
            {
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-16f) n = Vector3.Cross(c - a, d - a);
                if (n.sqrMagnitude < 1e-16f) return;
                if (Vector3.Dot(n, facing) < 0f)
                {
                    (b, d) = (d, b);
                    n = -n;
                }
                n.Normalize();
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) _n.Add(n);
                _uv.Add(Uv(a, n)); _uv.Add(Uv(b, n)); _uv.Add(Uv(c, n)); _uv.Add(Uv(d, n));
                var t = _t[sub];
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            static Vector2 Uv(Vector3 p, Vector3 n)
            {
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                if (ax >= ay && ax >= az) return new Vector2(p.z, p.y) * UvPerMetre;
                if (ay >= az) return new Vector2(p.x, p.z) * UvPerMetre;
                return new Vector2(p.x, p.y) * UvPerMetre;
            }

            public void Box(Vector3 centre, Vector3 size, int sub)
            {
                var h = size * 0.5f;
                Vector3 P(float x, float y, float z) => centre + new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right, sub);
                Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left, sub);
                Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up, sub);
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down, sub);
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward, sub);
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), Vector3.back, sub);
            }

            // A closed cylinder of `segments` flat sides from a to b.
            public void Tube(Vector3 a, Vector3 b, float radius, int sub, int segments)
            {
                var axis = b - a;
                if (axis.sqrMagnitude < 1e-12f) return;
                var dir = axis.normalized;
                var side = Vector3.Cross(dir, Mathf.Abs(dir.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
                var up = Vector3.Cross(side, dir);
                float step = Mathf.PI * 2f / segments;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s * step, a1 = (s + 1) * step, am = (a0 + a1) * 0.5f;
                    var r0 = (side * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
                    var r1 = (side * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
                    Quad(a + r0, a + r1, b + r1, b + r0, side * Mathf.Cos(am) + up * Mathf.Sin(am), sub);
                    Quad(a, a, a + r1, a + r0, -dir, sub);
                    Quad(b, b, b + r1, b + r0, dir, sub);
                }
            }

            // A flat round face looking along `facing`.
            public void Disc(Vector3 centre, float radius, Vector3 facing, int sub, int segments)
            {
                var dir = facing.normalized;
                var side = Vector3.Cross(dir, Mathf.Abs(dir.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
                var up = Vector3.Cross(side, dir);
                float step = Mathf.PI * 2f / segments;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s * step, a1 = (s + 1) * step;
                    Quad(centre, centre, centre + (side * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius,
                        centre + (side * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius, dir, sub);
                }
            }

            // A flat blade in the y-z plane through matching sections: `back[i]` on the thick side, `edge[i]` on the
            // sharp side (both as (z, y)). Half thickness `backHalf` at the back thinning to `edgeHalf` at the edge.
            public void Blade(Vector2[] back, Vector2[] edge, float backHalf, float edgeHalf, int sub)
            {
                Vector3 B(int i, float s) => new Vector3(s * backHalf, back[i].y, back[i].x);
                Vector3 E(int i, float s) => new Vector3(s * edgeHalf, edge[i].y, edge[i].x);
                int last = back.Length - 1;
                for (int i = 0; i < last; i++)
                {
                    Quad(B(i, 1), E(i, 1), E(i + 1, 1), B(i + 1, 1), Vector3.right, sub);
                    Quad(B(i, -1), E(i, -1), E(i + 1, -1), B(i + 1, -1), Vector3.left, sub);
                    var outward = B(i, 0) + B(i + 1, 0) - E(i, 0) - E(i + 1, 0);
                    Quad(B(i, 1), B(i + 1, 1), B(i + 1, -1), B(i, -1), outward, sub);
                    Quad(E(i, 1), E(i + 1, 1), E(i + 1, -1), E(i, -1), -outward, sub);
                }
                Quad(B(0, 1), E(0, 1), E(0, -1), B(0, -1), B(0, 0) + E(0, 0) - B(1, 0) - E(1, 0), sub);
                Quad(B(last, 1), E(last, 1), E(last, -1), B(last, -1), B(last, 0) + E(last, 0) - B(last - 1, 0) - E(last - 1, 0), sub);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.subMeshCount = SubMeshCount;
                for (int s = 0; s < SubMeshCount; s++) mesh.SetTriangles(_t[s], s);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}

using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Low-poly models of the worn helmet, vest and backpack (3 tiers each), built in code (no downloads), life size in the
    // character's frame: metres, +y up, +z forward. Sub-meshes: Body (the tier's military tone), Dark (webbing, straps,
    // liners) and Accent (pouches, mid tone). Tier 1 light khaki, 2 olive, 3 black; heavier tiers add parts.
    // Origins: helmet = centre of the brim line; vest = chest centre; backpack = bottom centre of the back panel
    // (the pack hangs toward -z). Both the worn visuals (ArmorVisual, BackpackVisual) and the ground pickups use them.
    public static class WornGearMeshes
    {
        public const int Body = 0, Dark = 1, Accent = 2;
        // Reference sizes the visuals scale from (a 1.8 m adult).
        public const float HeadRise = 0.215f, ShoulderWidth = 0.37f;
        static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();
        // Tier 3 "black" is a dark grey so it still separates from a black bodysuit; the Accent lines carry the edges.
        static readonly Color[] Tones = { new Color(0.50f, 0.46f, 0.34f), new Color(0.30f, 0.34f, 0.22f), new Color(0.31f, 0.32f, 0.33f) };
        static readonly Color DarkColor = new Color(0.09f, 0.09f, 0.10f);

        public static void Colors(LootKind kind, int tier, Color[] into)
        {
            var tone = Tones[Mathf.Clamp(tier, 1, 3) - 1];
            into[Body] = tone;
            into[Dark] = DarkColor;
            into[Accent] = Color.Lerp(tone, new Color(0.82f, 0.79f, 0.68f), 0.62f); // edge highlight lines and pockets: clearly lighter than the shell
        }

        public static bool Handles(LootKind kind) => kind == LootKind.Helmet || kind == LootKind.Vest || kind == LootKind.Backpack;

        // Worn model: ONE sub-mesh whose UVs point into a 3-colour palette (Body, Dark, Accent), so the three pieces of a
        // character cost one draw call each and every piece of a tier shares one material (Palette).
        public static Mesh Model(LootKind kind, int tier)
        {
            tier = Mathf.Clamp(tier, 1, 3);
            int key = (int)kind * 10 + tier + 5000;
            if (Cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var raw = Raw(kind, tier);
            if (raw == null) return null;
            mesh = Object.Instantiate(raw);
            mesh.name = "Worn" + kind + tier;
            var uv = new Vector2[mesh.vertexCount]; var all = new List<int>();
            for (int sub = 0; sub < raw.subMeshCount; sub++)
                foreach (int idx in raw.GetTriangles(sub)) { uv[idx] = new Vector2((sub + 0.5f) / 3f, 0.5f); all.Add(idx); }
            mesh.uv = uv;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(all, 0);
            Cache[key] = mesh;
            return mesh;
        }

        static readonly Dictionary<int, Material> Palettes = new Dictionary<int, Material>();

        // The shared material of a tier: a 3x1 point-sampled palette (Body, Dark, Accent) on URP Lit.
        public static Material Palette(int tier)
        {
            tier = Mathf.Clamp(tier, 1, 3);
            if (Palettes.TryGetValue(tier, out var m) && m != null) return m;
            var c = new Color[3]; Colors(LootKind.Helmet, tier, c);
            var tex = new Texture2D(3, 1, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "WornGearPalette" + tier };
            tex.SetPixels(c); tex.Apply();
            m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "WornGear" + tier };
            m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex); m.SetFloat("_Smoothness", 0.2f);
            Palettes[tier] = m;
            return m;
        }

        static Mesh Raw(LootKind kind, int tier)
        {
            int key = (int)kind * 10 + tier;
            if (Cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var d = new MeleeMeshes.Draft();
            switch (kind)
            {
                case LootKind.Helmet: BuildHelmet(d, tier); break;
                case LootKind.Vest: BuildVest(d, tier); break;
                case LootKind.Backpack: BuildBackpack(d, tier); break;
                default: return null;
            }
            mesh = d.ToMesh("WornRaw" + kind + tier);
            Cache[key] = mesh;
            return mesh;
        }

        // The same model as a pickup lying on the floor (a little larger than life so it reads at a distance).
        public static Mesh Ground(LootKind kind, int tier)
        {
            tier = Mathf.Clamp(tier, 1, 3);
            int key = 1000 + (int)kind * 10 + tier;
            if (Cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var worn = Raw(kind, tier); // three sub-meshes, tinted by LootView like the other pickups
            // Helmet rests on its brim; the vest lies on its back (front up); the pack stands on its bottom, panel toward -z.
            Quaternion turn = kind == LootKind.Vest ? Quaternion.Euler(-90f, 0f, 0f) : kind == LootKind.Backpack ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            // Larger than life, but the longest side stays within the pickup size limit (LootMeshTests: 0.6 m).
            var wb = worn.bounds; float longest = Mathf.Max(wb.size.x, wb.size.y, wb.size.z);
            float scale = Mathf.Min(kind == LootKind.Helmet ? 1.25f : 1.1f, 0.55f / longest);
            mesh = Object.Instantiate(worn);
            mesh.name = "Ground" + kind + tier;
            var v = mesh.vertices; var n = mesh.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = turn * v[i] * scale; n[i] = turn * n[i]; }
            mesh.vertices = v; mesh.normals = n;
            mesh.RecalculateBounds();
            float lift = -mesh.bounds.min.y;
            for (int i = 0; i < v.Length; i++) v[i] += new Vector3(0f, lift, 0f);
            mesh.vertices = v;
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Cache[key] = mesh;
            return mesh;
        }

        // Where and how big the three pieces sit on a roster character (CharacterDef sockets). Null when the character has no
        // CharacterDef (the old Swat), which keeps its hand-tuned constants in ArmorVisual/BackpackVisual.
        public struct Fit
        {
            public Transform Head, Torso;
            public Vector3 HelmetBase, VestCentre, PackBottom; // world positions
            public float HeadScale, TorsoScale;
        }

        public static bool TryFit(Transform root, out Fit fit)
        {
            fit = default;
            var def = root.GetComponentInChildren<CharacterDef>(true);
            if (def == null || def.Sockets.Head == null || def.Sockets.UpperChest == null) return false;
            var s = def.Sockets; var fwd = root.forward; var up = root.up;
            fit.Head = s.Head; fit.Torso = s.UpperChest;
            fit.HeadScale = Mathf.Clamp((s.HeadSocket.position.y - s.Head.position.y) / HeadRise, 0.9f, 1.15f);
            float shoulders = Vector3.Distance(s.LeftUpperArm.position, s.RightUpperArm.position);
            fit.TorsoScale = Mathf.Clamp(shoulders / ShoulderWidth, 0.85f, 1.2f);
            // Brim 0.08 below the crown: over the brow, clear of the eyes; centred a little forward of the skull-base bone.
            fit.HelmetBase = new Vector3(s.Head.position.x, s.HeadSocket.position.y - 0.08f * fit.HeadScale, s.Head.position.z) + fwd * (0.02f * fit.HeadScale);
            fit.VestCentre = s.UpperChest.position - up * (0.02f * fit.TorsoScale) + fwd * (0.01f * fit.TorsoScale);
            fit.PackBottom = s.UpperChest.position - up * (0.30f * fit.TorsoScale) - fwd * (0.115f * fit.TorsoScale);
            return true;
        }

        // ---- Helmet: a dome (superellipse rings) with a brim and an edge highlight line, chin strap; tier 2 adds ear guards and
        // mounts, tier 3 a curved face shield with a dark visor band and one highlight line.
        static void BuildHelmet(MeleeMeshes.Draft d, int tier)
        {
            const float rx = 0.093f, rz = 0.108f;
            float h = tier == 1 ? 0.135f : 0.125f;
            var rings = new List<Ring>
            {
                new Ring(-0.004f, rx + 0.014f, rz + 0.014f, 0f), // brim
                new Ring(h * 0.20f, rx, rz, 0f),
                new Ring(h * 0.45f, rx * 0.95f, rz * 0.95f, 0f),
                new Ring(h * 0.70f, rx * 0.80f, rz * 0.80f, 0f),
                new Ring(h * 0.90f, rx * 0.52f, rz * 0.52f, 0f),
                new Ring(h, rx * 0.20f, rz * 0.20f, 0f),
            };
            Loft(d, rings, 12, 2.4f, Body, true, false);
            d.Disc(new Vector3(0f, -0.003f, 0f), rx + 0.01f, Vector3.down, Dark, 12); // liner seen from below
            Band(d, 0.0f, 0.009f, rx + 0.0165f, rz + 0.0165f, 0f, 360f, 12, Accent);   // brim edge highlight
            // Chin strap: two straps down the cheeks and a band under the chin.
            foreach (float s in new[] { -1f, 1f })
                d.Box(new Vector3(s * (rx - 0.004f), -0.055f, 0.03f), new Vector3(0.012f, 0.11f, 0.02f), Dark);
            d.Box(new Vector3(0f, -0.108f, 0.045f), new Vector3(rx * 1.5f, 0.012f, 0.02f), Dark);
            if (tier == 1)
            {
                d.Box(new Vector3(0f, 0.012f, -rz - 0.012f), new Vector3(0.07f, 0.014f, 0.02f), Dark); // rear net clip
                return;
            }
            // Tier 2+: ear guards, side rails, a front mount and a rear battery pouch.
            foreach (float s in new[] { -1f, 1f })
            {
                d.Box(new Vector3(s * (rx + 0.004f), -0.035f, 0.005f), new Vector3(0.018f, 0.085f, 0.11f), Body);
                d.Box(new Vector3(s * (rx * 0.97f), h * 0.50f, 0.0f), new Vector3(0.012f, 0.018f, 0.09f), Dark);
            }
            d.Box(new Vector3(0f, h * 0.62f, rz * 0.90f), new Vector3(0.045f, 0.04f, 0.035f), Dark); // front mount
            d.Box(new Vector3(0f, h * 0.30f, -rz * 0.92f), new Vector3(0.075f, 0.05f, 0.03f), Dark); // rear pouch
            d.Box(new Vector3(0f, -0.04f, -rz * 0.85f), new Vector3(0.12f, 0.07f, 0.02f), Body); // neck drop
            if (tier == 2) return;
            // Tier 3: a curved shield round the face (part of a cylinder, like the skull), a dark visor band cut into it and
            // a highlight line along its top edge; a curved jaw guard below.
            const float sr = 0.012f;
            Shell(d, -0.035f, 0.085f, rx + sr, rz + sr, -60f, 60f, 8, 0.012f, Body);
            Band(d, -0.026f, 0.034f, rx + sr + 0.0145f, rz + sr + 0.0145f, -52f, 52f, 8, Dark);  // visor
            Band(d, 0.0f, 0.012f, rx + sr + 0.0145f, rz + sr + 0.0145f, -60f, 60f, 8, Accent);    // top-edge highlight
            Shell(d, -0.1f, 0.045f, rx + sr, rz + sr, -80f, 80f, 4, 0.012f, Body);
        }

        // ---- Vest: curved front and back plates (parts of a cylinder round the torso) with a thin outline where the
        // plate window is, cummerbund, shoulder straps; tier 2 adds pouches, tier 3 side plates and pads.
        // Highlight lines (Accent, a lighter tone) trace the plate edges so dark tiers do not melt into a dark body.
        static void BuildVest(MeleeMeshes.Draft d, int tier)
        {
            const float rx = 0.178f, rz = 0.122f; // outside the torso (a chest ~0.165 x 0.11)
            float ph = tier == 1 ? 0.30f : 0.34f;
            const float th = 0.016f;
            Shell(d, 0f, ph, rx, rz, -62f, 62f, 10, th, Body);                  // front plate
            Shell(d, 0f, ph, rx, rz, 118f, 242f, 8, th, Body);                 // back plate
            Shell(d, -0.13f, 0.12f, rx - 0.004f, rz - 0.004f, 62f, 118f, 2, 0.012f, Dark);   // cummerbund, left side
            Shell(d, -0.13f, 0.12f, rx - 0.004f, rz - 0.004f, 242f, 298f, 2, 0.012f, Dark);  // right side
            float k = th + 0.003f;
            Band(d, ph * 0.5f - 0.008f, 0.014f, rx + k, rz + k, -62f, 62f, 10, Accent);       // front top edge
            Band(d, -ph * 0.5f + 0.008f, 0.014f, rx + k, rz + k, -62f, 62f, 10, Accent);      // front bottom edge
            // Plate window: a thin outline, not a filled rectangle.
            float wy = ph * 0.5f - 0.05f, wh = ph - 0.11f, wk = th + 0.002f;
            Band(d, wy - 0.003f, 0.011f, rx + wk, rz + wk, -26f, 26f, 4, Accent);
            Band(d, wy - wh, 0.011f, rx + wk, rz + wk, -26f, 26f, 4, Accent);
            Band(d, wy - wh * 0.5f - 0.003f, wh, rx + wk, rz + wk, -28.5f, -24.5f, 1, Accent);
            Band(d, wy - wh * 0.5f - 0.003f, wh, rx + wk, rz + wk, 24.5f, 28.5f, 1, Accent);
            Band(d, ph * 0.5f - 0.008f, 0.014f, rx + k, rz + k, 118f, 242f, 8, Accent);      // back top edge
            foreach (float s in new[] { -1f, 1f })                              // shoulder straps over the top
            {
                d.Box(new Vector3(s * 0.095f, ph * 0.5f + 0.012f, 0f), new Vector3(0.055f, 0.016f, rz * 2f + 0.02f), Dark);
                d.Box(new Vector3(s * 0.095f, ph * 0.5f - 0.03f, rz + 0.012f), new Vector3(0.05f, 0.06f, 0.012f), Dark);
            }
            if (tier == 1) return;
            // Tier 2+: three magazine pouches on the lower front, a chest rig strip, a drag handle behind.
            for (int i = -1; i <= 1; i++)
                d.Box(new Vector3(i * 0.065f, -ph * 0.5f + 0.045f, rz + 0.03f), new Vector3(0.055f, 0.09f, 0.04f), Dark);
            d.Box(new Vector3(0f, ph * 0.5f - 0.015f, -rz - 0.02f), new Vector3(0.09f, 0.03f, 0.016f), Dark);
            if (tier == 2) return;
            // Tier 3: side plates, shoulder pads and a collar guard.
            foreach (float s in new[] { -1f, 1f })
            {
                Shell(d, 0.02f, 0.2f, rx + 0.012f, rz + 0.012f, s > 0 ? 70f : 250f, s > 0 ? 110f : 290f, 2, 0.014f, Body);
                d.Box(new Vector3(s * 0.185f, ph * 0.5f + 0.01f, 0f), new Vector3(0.09f, 0.03f, 0.13f), Body);
            }
            d.Box(new Vector3(0f, ph * 0.5f + 0.02f, rz * 0.3f), new Vector3(0.12f, 0.035f, 0.08f), Body);
        }

        // ---- Backpack: a rounded pack (soft superellipse) with a domed lid, a front pocket, side pockets and flat straps;
        // bigger and heavier per tier. Nothing sticks out as a box: the old side strap boxes are gone.
        static void BuildBackpack(MeleeMeshes.Draft d, int tier)
        {
            float w = 0.24f + 0.035f * tier, h = 0.26f + 0.055f * tier, l = 0.11f + 0.035f * tier;
            float rx = w * 0.5f, zc = -l * 0.5f; // the back panel is the plane z = 0
            var rings = new List<Ring>
            {
                new Ring(0f, rx * 0.80f, l * 0.42f, zc), new Ring(h * 0.05f, rx * 0.95f, l * 0.49f, zc),
                new Ring(h * 0.45f, rx, l * 0.5f, zc), new Ring(h * 0.82f, rx * 0.97f, l * 0.49f, zc),
                new Ring(h * 0.95f, rx * 0.84f, l * 0.44f, zc), new Ring(h, rx * 0.62f, l * 0.36f, zc),
            };
            Loft(d, rings, 10, 2.6f, Body, true, true);
            // Domed lid in the dark tone with a highlight line along its lower edge and a strap over the front.
            var lid = new List<Ring> { new Ring(h * 0.72f, rx * 1.03f, l * 0.53f, zc), new Ring(h * 0.88f, rx * 1.04f, l * 0.52f, zc), new Ring(h * 0.98f, rx * 0.90f, l * 0.46f, zc), new Ring(h * 1.05f, rx * 0.6f, l * 0.34f, zc) };
            Loft(d, lid, 10, 2.6f, Dark, true, false);
            Band(d, h * 0.725f, 0.012f, rx * 1.045f, l * 0.535f, 0f, 360f, 10, Accent, zc); // lid edge highlight (centred on the pack)
            d.Box(new Vector3(0f, h * 0.60f, zc - l * 0.5f - 0.004f), new Vector3(0.045f, h * 0.30f, 0.01f), Dark); // strap down the front
            d.Box(new Vector3(0f, h + 0.01f, zc), new Vector3(0.07f, 0.016f, 0.02f), Dark);                         // carry loop
            // Front pocket, rounded.
            var pocket = new List<Ring> { new Ring(h * 0.08f, rx * 0.70f, l * 0.18f, zc - l * 0.5f), new Ring(h * 0.30f, rx * 0.76f, l * 0.21f, zc - l * 0.53f), new Ring(h * 0.40f, rx * 0.62f, l * 0.16f, zc - l * 0.5f) };
            Loft(d, pocket, 8, 2.6f, Body, true, true);
            // Flat shoulder straps on the back panel.
            foreach (float s in new[] { -1f, 1f })
                d.Box(new Vector3(s * rx * 0.52f, h * 0.55f, 0.012f), new Vector3(0.05f, h * 0.85f, 0.022f), Dark);
            if (tier == 1) return;
            // Tier 2+: two rounded side pockets set flush against the body.
            foreach (float s in new[] { -1f, 1f })
            {
                var side = new List<Ring> { new Ring(h * 0.05f, 0.024f, l * 0.30f, zc), new Ring(h * 0.26f, 0.030f, l * 0.35f, zc), new Ring(h * 0.34f, 0.022f, l * 0.26f, zc) };
                Loft(d, side, 6, 2.6f, Body, true, true, s * (rx + 0.004f));
            }
            if (tier == 2) return;
            // Tier 3: a rolled mat on top and a second front pouch.
            d.Tube(new Vector3(-rx * 0.8f, h + 0.06f, zc), new Vector3(rx * 0.8f, h + 0.06f, zc), 0.045f, Body, 8);
            var pouch = new List<Ring> { new Ring(h * 0.50f, rx * 0.58f, l * 0.12f, zc - l * 0.55f), new Ring(h * 0.66f, rx * 0.6f, l * 0.14f, zc - l * 0.57f), new Ring(h * 0.74f, rx * 0.5f, l * 0.10f, zc - l * 0.55f) };
            Loft(d, pouch, 6, 2.6f, Body, true, true);
        }

        // ---- Shape helpers ----
        struct Ring
        {
            public float Y, Rx, Rz, Cz;
            public Ring(float y, float rx, float rz, float cz) { Y = y; Rx = rx; Rz = rz; Cz = cz; }
        }

        // Rings of a superellipse |x/rx|^n + |z/rz|^n = 1 (n = 2 ellipse, larger = boxier), joined by quads; optional caps.
        static void Loft(MeleeMeshes.Draft d, List<Ring> rings, int segs, float n, int sub, bool capTop, bool capBottom, float cx = 0f)
        {
            Vector3 P(Ring r, int i)
            {
                float t = i * Mathf.PI * 2f / segs, c = Mathf.Cos(t), s = Mathf.Sin(t);
                float e = 2f / n;
                return new Vector3(cx + r.Rx * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e), r.Y, r.Cz + r.Rz * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e));
            }
            for (int j = 0; j < rings.Count - 1; j++)
                for (int i = 0; i < segs; i++)
                {
                    Vector3 a = P(rings[j], i), b = P(rings[j], i + 1), c = P(rings[j + 1], i + 1), e = P(rings[j + 1], i);
                    var mid = (a + b + c + e) * 0.25f;
                    var facing = new Vector3(mid.x - cx, 0f, mid.z - rings[j].Cz);
                    d.Quad(a, b, c, e, facing, sub);
                }
            void Cap(Ring r, Vector3 up)
            {
                var centre = new Vector3(cx, r.Y, r.Cz);
                for (int i = 0; i < segs; i++) d.Quad(centre, centre, P(r, i + 1), P(r, i), up, sub);
            }
            if (capTop) Cap(rings[rings.Count - 1], Vector3.up);
            if (capBottom) Cap(rings[0], Vector3.down);
        }

        // A curved plate: part of a cylinder (outer skin plus the top and bottom rims), angles in degrees from +z.
        static void Shell(MeleeMeshes.Draft d, float y, float h, float rx, float rz, float a0, float a1, int segs, float thick, int sub)
        {
            Vector3 P(float deg, float yy, float k)
            {
                float t = deg * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(t) * (rx + k), yy, Mathf.Cos(t) * (rz + k));
            }
            float yb = y - h * 0.5f, yt = y + h * 0.5f;
            for (int i = 0; i < segs; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)segs), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)segs), tm = (t0 + t1) * 0.5f * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Sin(tm), 0f, Mathf.Cos(tm));
                d.Quad(P(t0, yb, thick), P(t1, yb, thick), P(t1, yt, thick), P(t0, yt, thick), outward, sub);
                d.Quad(P(t0, yt, 0), P(t1, yt, 0), P(t1, yt, thick), P(t0, yt, thick), Vector3.up, sub);
                d.Quad(P(t0, yb, 0), P(t1, yb, 0), P(t1, yb, thick), P(t0, yb, thick), Vector3.down, sub);
            }
        }

        // A thin one-sided strip on a cylinder surface (an outline or highlight line), radii already include its offset.
        static void Band(MeleeMeshes.Draft d, float y, float h, float rx, float rz, float a0, float a1, int segs, int sub, float cz = 0f)
        {
            for (int i = 0; i < segs; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)segs) * Mathf.Deg2Rad, t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)segs) * Mathf.Deg2Rad, tm = (t0 + t1) * 0.5f;
                Vector3 P(float t, float yy) => new Vector3(Mathf.Sin(t) * rx, yy, cz + Mathf.Cos(t) * rz);
                d.Quad(P(t0, y - h * 0.5f), P(t1, y - h * 0.5f), P(t1, y + h * 0.5f), P(t0, y + h * 0.5f), new Vector3(Mathf.Sin(tm), 0f, Mathf.Cos(tm)), sub);
            }
        }
    }
}

using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // The pictures of the hit effects, painted in code once (ADR-470): no texture asset, nothing to license.
    // Atlas of 4 x 2 cells: concrete hole with cracks, splintered wood, bright-rimmed metal hole, dirt crater,
    // cracked glass, a blood splat, a blood spatter streak (long side = +u), a round blood pool.
    public static class ImpactFxTextures
    {
        public const int CellConcrete = 0, CellWood = 1, CellMetal = 2, CellDirt = 3, CellGlass = 4, CellSplat = 5, CellSpatter = 6, CellPool = 7;
        const int Cols = 4, Rows = 2, CellPx = 128;
        // The holes are painted smaller than their cell, so a decal is drawn this much bigger than the old mark disc.
        public const float MarkScale = 2.6f;

        static Texture2D _atlas, _dot, _chunk;
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static int MarkCell(BulletSurface s)
        {
            switch (s)
            {
                case BulletSurface.Wood: return CellWood;
                case BulletSurface.Metal: case BulletSurface.Vehicle: return CellMetal;
                case BulletSurface.Ground: return CellDirt;
                case BulletSurface.Glass: return CellGlass;
                default: return CellConcrete;
            }
        }

        public static Rect CellRect(int cell)
        {
            int cx = cell % Cols, cy = cell / Cols;
            return new Rect((float)cx / Cols, (float)cy / Rows, 1f / Cols, 1f / Rows);
        }

        public static Material DecalMaterial(Material source) => WithTexture(source, Atlas, "_Decals");
        public static Material DotMaterial(Material source) => WithTexture(source, Dot, "_Dots");
        public static Material ChunkMaterial(Material source) => WithTexture(source, Chunk, "_Chunks");
        public static Material SparkMaterial(Material source) => WithTexture(source, Dot, "_Sparks");

        static Material WithTexture(Material source, Texture2D texture, string suffix)
        {
            var m = new Material(source) { name = source.name + suffix };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, texture);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, Color.white); // the picture and the vertex colour carry the tint
            return m;
        }

        public static Texture2D Atlas => _atlas != null ? _atlas : (_atlas = PaintAtlas());
        static Texture2D Dot => _dot != null ? _dot : (_dot = Paint(32, (u, v) => { float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)); return new Color(1f, 1f, 1f, a * a * (3f - 2f * a)); }));
        static Texture2D Chunk => _chunk != null ? _chunk : (_chunk = Paint(32, ChunkPixel));

        static Texture2D NewTexture(int w, int h, string name) =>
            new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };

        static Texture2D Paint(int size, System.Func<float, float, Color> pixel)
        {
            var tex = NewTexture(size, size, "FxSprite");
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = pixel((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        // A jagged five-sided chip with a lit edge.
        static Color ChunkPixel(float u, float v)
        {
            float ang = Mathf.Atan2(v, u), r = Mathf.Sqrt(u * u + v * v);
            float edge = 0.62f + 0.22f * Mathf.Sin(ang * 5f + 0.7f) * Mathf.Sin(ang * 2f + 1.9f);
            if (r > edge) return Color.clear;
            float shade = 0.75f + 0.25f * (u - v) * 0.7f;
            return new Color(shade, shade, shade, 1f);
        }

        static Texture2D PaintAtlas()
        {
            int w = Cols * CellPx, h = Rows * CellPx;
            var tex = NewTexture(w, h, "ImpactDecals");
            var px = new Color[w * h];
            for (int cell = 0; cell < Cols * Rows; cell++)
            {
                var rng = new System.Random(1009 + cell * 7919);
                var p = CellParams(rng);
                int ox = (cell % Cols) * CellPx, oy = (cell / Cols) * CellPx;
                for (int y = 0; y < CellPx; y++)
                    for (int x = 0; x < CellPx; x++)
                    {
                        float u = (x + 0.5f) / CellPx * 2f - 1f, v = (y + 0.5f) / CellPx * 2f - 1f;
                        var c = CellPixel(cell, u, v, p);
                        // The outer rim of every cell stays clear, so mip levels do not bleed into the neighbours.
                        float rim = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                        if (rim > 0.94f) c.a = 0f;
                        px[(oy + y) * w + ox + x] = c;
                    }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        // Per cell: twelve random numbers (crack angles, droplet places).
        static float[] CellParams(System.Random rng)
        {
            var p = new float[36];
            for (int i = 0; i < p.Length; i++) p[i] = (float)rng.NextDouble();
            return p;
        }

        static float Hash(float x, float y)
        {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        // Smooth value noise in [0, 1].
        static float Noise(float x, float y)
        {
            float ix = Mathf.Floor(x), iy = Mathf.Floor(y), fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1f, iy), c = Hash(ix, iy + 1f), d = Hash(ix + 1f, iy + 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Smooth(float edge0, float edge1, float x) { float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0)); return t * t * (3f - 2f * t); }

        // Distance from (u, v) to the ray from the centre at angle `a` (only its first `length`).
        static float CrackDistance(float u, float v, float a, float length, out float along)
        {
            float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
            along = u * dx + v * dy;
            if (along < 0f || along > length) return 9f;
            // A crack wanders a little.
            float side = -u * dy + v * dx + 0.03f * Mathf.Sin(along * 23f + a * 5f);
            return Mathf.Abs(side);
        }

        static Color Over(Color under, Color top)
        {
            float a = top.a + under.a * (1f - top.a);
            if (a <= 1e-5f) return Color.clear;
            var rgb = (new Vector3(top.r, top.g, top.b) * top.a + new Vector3(under.r, under.g, under.b) * under.a * (1f - top.a)) / a;
            return new Color(rgb.x, rgb.y, rgb.z, a);
        }

        static Color CellPixel(int cell, float u, float v, float[] p)
        {
            float r = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
            float n = Noise(u * 9f + cell * 13f, v * 9f);
            var c = Color.clear;
            switch (cell)
            {
                case CellConcrete:
                {
                    for (int k = 0; k < 6; k++)
                    {
                        float len = 0.45f + 0.4f * p[k + 6];
                        float d = CrackDistance(u, v, p[k] * Mathf.PI * 2f, len, out float along);
                        float width = 0.022f * (1f - along / len) + 0.004f;
                        if (d < width) c = Over(c, new Color(0.09f, 0.09f, 0.09f, 0.85f * (1f - 0.6f * along / len)));
                    }
                    float crater = 0.30f + 0.08f * Noise(ang * 2.5f + 3f, 1f);
                    if (r < crater) c = Over(c, new Color(0.30f + 0.1f * n, 0.30f + 0.1f * n, 0.29f + 0.1f * n, 0.9f * (1f - Smooth(crater - 0.06f, crater, r))));
                    if (r < 0.15f) c = Over(c, new Color(0.04f, 0.04f, 0.04f, 1f - Smooth(0.12f, 0.15f, r)));
                    break;
                }
                case CellWood:
                {
                    // Fibres torn out along v, light fresh wood, around a dark hole.
                    float tear = 0.62f + 0.12f * p[0];
                    float half = 0.13f * (1f - Mathf.Abs(v) / tear) + 0.05f * Noise(v * 18f, 2f) + 0.03f * Noise(u * 40f, v * 4f);
                    if (Mathf.Abs(v) < tear && Mathf.Abs(u) < half)
                        c = Over(c, new Color(0.72f + 0.1f * n, 0.56f + 0.08f * n, 0.36f + 0.05f * n, 0.95f));
                    float grain = Mathf.Abs(Mathf.Sin(u * 60f + Noise(u * 3f, v * 3f) * 4f));
                    if (Mathf.Abs(v) < tear * 0.8f && Mathf.Abs(u) < half && grain > 0.85f) c = Over(c, new Color(0.45f, 0.32f, 0.18f, 0.6f));
                    float e = (u / 0.11f) * (u / 0.11f) + (v / 0.16f) * (v / 0.16f);
                    if (e < 1f) c = Over(c, new Color(0.06f, 0.04f, 0.02f, 1f - Smooth(0.75f, 1f, e)));
                    break;
                }
                case CellMetal:
                {
                    if (r < 0.34f) c = Over(c, new Color(0.18f, 0.18f, 0.19f, 0.45f * (1f - Smooth(0.2f, 0.34f, r))));
                    if (r < 0.2f) c = Over(c, new Color(0.78f + 0.1f * n, 0.78f + 0.1f * n, 0.8f + 0.1f * n, 1f - Smooth(0.17f, 0.2f, r)));
                    if (r < 0.11f) c = Over(c, new Color(0.03f, 0.03f, 0.03f, 1f - Smooth(0.09f, 0.11f, r)));
                    break;
                }
                case CellDirt:
                {
                    float blot = 0.5f + 0.15f * (Noise(ang * 2f + 7f, 3f) - 0.5f) * 2f;
                    if (r < blot) c = Over(c, new Color(0.14f + 0.06f * n, 0.10f + 0.04f * n, 0.06f + 0.03f * n, 0.8f * (1f - Smooth(blot * 0.5f, blot, r))));
                    if (r < 0.12f) c = Over(c, new Color(0.05f, 0.04f, 0.03f, 1f - Smooth(0.09f, 0.12f, r)));
                    break;
                }
                case CellGlass:
                {
                    for (int k = 0; k < 9; k++)
                    {
                        float a = (k + p[k] * 0.6f) / 9f * Mathf.PI * 2f;
                        float len = 0.6f + 0.3f * p[k + 9];
                        if (CrackDistance(u, v, a, len, out float along) < 0.009f + 0.008f * (1f - along / len))
                            c = Over(c, new Color(0.95f, 0.97f, 1f, 0.8f * (1f - 0.5f * along / len)));
                    }
                    foreach (float ring in new[] { 0.28f, 0.52f })
                        if (Mathf.Abs(r - ring - 0.03f * Mathf.Sin(ang * 7f)) < 0.01f && Noise(ang * 3f + ring * 10f, 5f) > 0.35f)
                            c = Over(c, new Color(0.95f, 0.97f, 1f, 0.7f));
                    if (r < 0.07f) c = Over(c, new Color(0.08f, 0.08f, 0.09f, 1f - Smooth(0.05f, 0.07f, r)));
                    break;
                }
                case CellSplat:
                {
                    float blob = 0.36f + 0.16f * (Noise(ang * 2.2f + 11f, 1f) - 0.5f) * 2f;
                    float a = r < blob ? 1f - Smooth(blob - 0.04f, blob, r) : 0f;
                    for (int k = 0; k < 12; k++)
                    {
                        float da = p[k] * Mathf.PI * 2f, dr = 0.45f + 0.42f * p[k + 12], size = 0.025f + 0.05f * p[k + 24];
                        float dx = u - Mathf.Cos(da) * dr, dy = v - Mathf.Sin(da) * dr;
                        float dd = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dd < size) a = Mathf.Max(a, 1f - Smooth(size * 0.7f, size, dd));
                    }
                    if (a > 0f) c = new Color(0.30f - 0.08f * n, 0.015f, 0.015f, 0.92f * a);
                    break;
                }
                case CellSpatter:
                {
                    // A blob on the left with drops thrown to the right, each stretched along +u with a tail.
                    float a = 0f;
                    float bx = u + 0.45f, blob = 0.26f + 0.07f * (Noise(ang * 3f, 4f) - 0.5f);
                    float br = Mathf.Sqrt(bx * bx + v * v);
                    if (br < blob) a = 1f - Smooth(blob - 0.04f, blob, br);
                    for (int k = 0; k < 14; k++)
                    {
                        float cx = -0.25f + 1.1f * p[k], cy = (p[k + 14] - 0.5f) * (0.25f + 0.6f * p[k]);
                        float size = (0.07f - 0.045f * p[k]) * (0.6f + 0.6f * p[k + 20]);
                        float dx = (u - cx) / 2.6f, dy = v - cy;
                        if (dx < 0f) dx *= 2.4f; // the tail points back toward where it came from
                        float dd = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dd < size) a = Mathf.Max(a, 1f - Smooth(size * 0.7f, size, dd));
                    }
                    if (a > 0f) c = new Color(0.34f - 0.08f * n, 0.02f, 0.02f, 0.9f * a);
                    break;
                }
                case CellPool:
                {
                    float edge = 0.62f + 0.12f * (Noise(ang * 1.6f + 5f, 2f) - 0.5f) * 2f;
                    if (r < edge)
                    {
                        float a = 1f - Smooth(edge - 0.05f, edge, r);
                        float dark = Mathf.Lerp(0.16f, 0.30f, r / edge);
                        c = new Color(dark, 0.01f, 0.01f, 0.88f * a);
                    }
                    break;
                }
            }
            return c;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

// Our own trees, bushes and grass (ADR-058 (9), ADR-070, #71). CC0 game-ready semi-realistic trees do not exist at a
// weight we can draw (ADR-052 (7)), so: trunks and branches are tapered tubes built here and wrapped in scanned bark
// (ambientCG Bark012, Bark014); leaves are cards whose textures are composed here from scanned leaf, sprig and blade
// atlases (ambientCG LeafSet005, LeafSet019, Foliage006); the far LOD is a billboard painted the same way. Nothing is
// rendered to a texture, so it all runs under -nographics. Every derived texture is written under Prefabs/World.
public static class WorldVegetation
{
    const string FoliageArt = "Assets/_Project/Art/Foliage";
    const string BarkArt = "Assets/_Project/Art/Materials";
    const string BroadleafBark = "Bark012", ConiferBark = "Bark014";
    const string LeafAtlas = "LeafSet005", SprigAtlas = "LeafSet019", BladeAtlas = "Foliage006";
    const float BarkTile = 1.6f;     // metres one repeat of the bark covers round and up the trunk

    // Colours the cards are multiplied by, tuned against the terrain's grass layer and the overcast light.
    static readonly Color BroadleafTint = new Color(0.86f, 0.92f, 0.80f);
    static readonly Color ConiferTint = new Color(0.78f, 0.86f, 0.80f);
    static readonly Color BushTint = new Color(0.74f, 0.82f, 0.68f);
    static readonly Color GrassTint = new Color(0.66f, 0.74f, 0.52f);
    static readonly Color BarkTint = new Color(0.82f, 0.80f, 0.78f);
    const float LeafCutoff = 0.5f;

    // What the builder needs: one prototype per (kind, variant) and the grass meshes.
    public sealed class Kit
    {
        public VegetationRenderer.Prototype[] Prototypes;
        public int[] TrunkOf;               // per prototype: index into TrunkRadius / TrunkHeight (-1 for bushes)
        public float[] TrunkRadius, TrunkHeight;
        public Mesh GrassShort, GrassTall;
        public Material Grass, Flowers;
        public Mesh FlowerClump;

        public int Prototype(PlantKind kind, int variant) => (int)kind * Vegetation.Variants + Mathf.Clamp(variant, 0, Vegetation.Variants - 1);
    }

    public static Kit Build(WorldMeshes.Store store)
    {
        var leaf = Source.Load(LeafAtlas);
        var sprig = Source.Load(SprigAtlas);
        var blade = Source.Load(BladeAtlas);
        var cluster = ComposeLeafCluster(leaf);
        var branch = ComposeConiferBranch(sprig);
        var grass = ComposeGrass(blade);
        var broadleafBoard = ComposeBroadleafBillboard(cluster);
        var coniferBoard = ComposeConiferBillboard(branch);

        var leafTex = Save("Leaves_Broadleaf", cluster);
        var branchTex = Save("Leaves_Conifer", branch);
        var grassTex = Save("Grass_Clump", grass);
        var flowerTex = Save("Flowers", ComposeFlowers(blade));
        var broadleafBoardTex = Save("Billboard_Broadleaf", broadleafBoard);
        var coniferBoardTex = Save("Billboard_Conifer", coniferBoard);

        var broadleafBark = Bark("Broadleaf", BroadleafBark, 14f, 0.28f);
        var coniferBark = Bark("Conifer", ConiferBark, 18f, 0.22f);
        var broadleafLeaves = Foliage("World_Leaves_Broadleaf", leafTex, BroadleafTint, 14f, 0.28f, 0.05f);
        var coniferLeaves = Foliage("World_Leaves_Conifer", branchTex, ConiferTint, 18f, 0.22f, 0.025f);
        var bushLeaves = Foliage("World_Leaves_Bush", leafTex, BushTint, 1.9f, 0.07f, 0.04f);
        var broadleafBillboard = Foliage("World_Billboard_Broadleaf", broadleafBoardTex, BroadleafTint, 14f, 0.28f, 0f);
        var coniferBillboard = Foliage("World_Billboard_Conifer", coniferBoardTex, ConiferTint, 18f, 0.22f, 0f);

        var kit = new Kit
        {
            Prototypes = new VegetationRenderer.Prototype[3 * Vegetation.Variants],
            TrunkOf = new int[3 * Vegetation.Variants],
            TrunkRadius = new float[3 * Vegetation.Variants],
            TrunkHeight = new float[3 * Vegetation.Variants],
            Grass = Foliage("World_Grass", grassTex, GrassTint, 0.9f, 0.22f, 0.012f),
            Flowers = Foliage("World_Flowers", flowerTex, Color.white, 0.8f, 0.25f, 0.012f),
        };
        for (int v = 0; v < Vegetation.Variants; v++)
        {
            int b = kit.Prototype(PlantKind.Broadleaf, v);
            kit.Prototypes[b] = BroadleafTree(v, store, broadleafBark, broadleafLeaves, broadleafBillboard, out kit.TrunkRadius[b], out kit.TrunkHeight[b]);
            kit.TrunkOf[b] = b;
            int c = kit.Prototype(PlantKind.Conifer, v);
            kit.Prototypes[c] = ConiferTree(v, store, coniferBark, coniferLeaves, coniferBillboard, out kit.TrunkRadius[c], out kit.TrunkHeight[c]);
            kit.TrunkOf[c] = c;
            int s = kit.Prototype(PlantKind.Bush, v);
            kit.Prototypes[s] = Bush(v, store, bushLeaves);
            kit.TrunkOf[s] = -1;
        }
        kit.GrassShort = store.Add(GrassClump("Grass_Short", 0.95f, 0.5f));
        kit.GrassTall = store.Add(GrassClump("Grass_Reeds", 0.8f, 1.15f));
        kit.FlowerClump = store.Add(FlowerClump("Flower_Clump", 0.9f, 0.62f));
        return kit;
    }

    // ---- source atlases ----

    // A scanned atlas read straight from disk (the imported textures are not readable), with its sprites: the
    // separate leaves, sprigs or blades found as connected opaque regions of the opacity map.
    sealed class Source
    {
        public int W, H;
        public Color[] Color;
        public float[] Alpha;
        public List<RectInt> Sprites;

        public static Source Load(string id)
        {
            var color = LoadFile($"{FoliageArt}/{id}/{id}_color.jpg") ?? throw new FileNotFoundException("colour map missing for " + id);
            var opacity = LoadFile($"{FoliageArt}/{id}/{id}_opacity.jpg") ?? throw new FileNotFoundException("opacity map missing for " + id);
            if (color.width != opacity.width || color.height != opacity.height) throw new Exception(id + ": colour and opacity differ in size");
            var src = new Source { W = color.width, H = color.height, Color = color.GetPixels() };
            var o = opacity.GetPixels();
            src.Alpha = new float[o.Length];
            for (int i = 0; i < o.Length; i++) src.Alpha[i] = o[i].r;
            Object.DestroyImmediate(color);
            Object.DestroyImmediate(opacity);
            src.Sprites = FindSprites(src);
            if (src.Sprites.Count == 0) throw new Exception(id + ": no sprites found in the opacity map");
            return src;
        }

        // Bilinear colour (premultiplied by nothing) and alpha at a pixel position.
        public void Sample(float x, float y, out Color c, out float a)
        {
            x = Mathf.Clamp(x - 0.5f, 0f, W - 1.001f);
            y = Mathf.Clamp(y - 0.5f, 0f, H - 1.001f);
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            int i00 = y0 * W + x0, i10 = i00 + 1, i01 = i00 + W, i11 = i01 + 1;
            float w00 = (1f - fx) * (1f - fy), w10 = fx * (1f - fy), w01 = (1f - fx) * fy, w11 = fx * fy;
            a = Alpha[i00] * w00 + Alpha[i10] * w10 + Alpha[i01] * w01 + Alpha[i11] * w11;
            c = Color[i00] * w00 + Color[i10] * w10 + Color[i01] * w01 + Color[i11] * w11;
        }
    }

    static Texture2D LoadFile(string path)
    {
        if (!File.Exists(path)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        if (!tex.LoadImage(File.ReadAllBytes(path)))
        {
            Object.DestroyImmediate(tex);
            return null;
        }
        return tex;
    }

    // Connected opaque regions on a 4-pixel grid, largest first, ignoring specks.
    static List<RectInt> FindSprites(Source src)
    {
        const int block = 4;
        int gw = src.W / block, gh = src.H / block;
        var solid = new bool[gw * gh];
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                bool any = false;
                for (int y = 0; y < block && !any; y++)
                    for (int x = 0; x < block && !any; x++)
                        any = src.Alpha[(gy * block + y) * src.W + gx * block + x] > 0.5f;
                solid[gy * gw + gx] = any;
            }
        var seen = new bool[solid.Length];
        var found = new List<(RectInt rect, int blocks)>();
        var stack = new Stack<int>();
        for (int start = 0; start < solid.Length; start++)
        {
            if (!solid[start] || seen[start]) continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, blocks = 0;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % gw, y = i / gw;
                blocks++;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                        int n = ny * gw + nx;
                        if (!solid[n] || seen[n]) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
            }
            if (blocks < 40) continue;
            int px = Mathf.Max(0, minX * block - 2), py = Mathf.Max(0, minY * block - 2);
            int pw = Mathf.Min(src.W, (maxX + 1) * block + 2) - px, ph = Mathf.Min(src.H, (maxY + 1) * block + 2) - py;
            found.Add((new RectInt(px, py, pw, ph), blocks));
        }
        found.Sort((a, b) => b.blocks.CompareTo(a.blocks));
        var rects = new List<RectInt>();
        foreach (var f in found) rects.Add(f.rect);
        return rects;
    }

    // ---- composing card textures ----

    // An RGBA image being painted, premultiplied while it is built. Row 0 is the bottom (as in a Texture2D).
    sealed class Canvas
    {
        public readonly int W, H;
        public readonly Color[] Px;

        public Canvas(int w, int h)
        {
            W = w;
            H = h;
            Px = new Color[w * h];
        }

        // Draws a sprite of `src` so the sprite point `pivot` (pixels of the source) lands on `at`, scaled by `scale`
        // (canvas pixels per source pixel) and turned by `degrees`; `flipX` mirrors the sprite first.
        public void Stamp(Source src, RectInt rect, Vector2 pivot, Vector2 at, float scale, float degrees, Color tint, bool flipX = false)
        {
            float rad = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            var corners = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax) };
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var corner in corners)
            {
                var d = (corner - pivot) * scale;
                if (flipX) d.x = -d.x;
                var p = at + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    // Back into the source: unturn, unscale, unflip.
                    float dx = x + 0.5f - at.x, dy = y + 0.5f - at.y;
                    float sx = (dx * cos + dy * sin) / scale, sy = (-dx * sin + dy * cos) / scale;
                    if (flipX) sx = -sx;
                    sx += pivot.x;
                    sy += pivot.y;
                    if (sx < rect.xMin || sy < rect.yMin || sx >= rect.xMax || sy >= rect.yMax) continue;
                    src.Sample(sx, sy, out var c, out float a);
                    if (a <= 0.004f) continue;
                    ref var d = ref Px[y * W + x];
                    d.r = c.r * tint.r * a + d.r * (1f - a);
                    d.g = c.g * tint.g * a + d.g * (1f - a);
                    d.b = c.b * tint.b * a + d.b * (1f - a);
                    d.a = a + d.a * (1f - a);
                }
            }
        }

        // Same, with another canvas as the source (billboards are painted from the finished cards).
        public void Stamp(Canvas src, Vector2 at, float scale, float degrees, Color tint, bool flipX = false)
        {
            var source = src.AsSource();
            Stamp(source, new RectInt(0, 0, src.W, src.H), new Vector2(src.W * 0.5f, src.H * 0.5f), at, scale, degrees, tint, flipX);
        }

        Source _asSource;

        Source AsSource()
        {
            if (_asSource != null) return _asSource;
            var color = new Color[Px.Length];
            var alpha = new float[Px.Length];
            for (int i = 0; i < Px.Length; i++) { color[i] = Px[i]; color[i].a = 1f; alpha[i] = Px[i].a; }
            return _asSource = new Source { W = W, H = H, Color = color, Alpha = alpha };
        }

        // One premultiplied "over" pixel.
        public void Over(int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || a <= 0f) return;
            ref var d = ref Px[y * W + x];
            d.r = c.r * a + d.r * (1f - a);
            d.g = c.g * a + d.g * (1f - a);
            d.b = c.b * a + d.b * (1f - a);
            d.a = a + d.a * (1f - a);
        }

        public void Disc(float cx, float cy, float radius, Color c)
        {
            for (int y = Mathf.FloorToInt(cy - radius - 1f); y <= Mathf.CeilToInt(cy + radius + 1f); y++)
                for (int x = Mathf.FloorToInt(cx - radius - 1f); x <= Mathf.CeilToInt(cx + radius + 1f); x++)
                    Over(x, y, c, Mathf.Clamp01(radius - Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) + 0.5f));
        }

        // A filled rectangle of flat colour (trunks on billboards).
        public void Fill(Rect area, Color color)
        {
            for (int y = Mathf.Max(0, (int)area.yMin); y < Mathf.Min(H, (int)area.yMax); y++)
                for (int x = Mathf.Max(0, (int)area.xMin); x < Mathf.Min(W, (int)area.xMax); x++)
                    Px[y * W + x] = color;
        }

        // Back from premultiplied, darkened toward the bottom by `shade` (the inside of a crown, the foot of the
        // grass), and the colour bled out under transparent pixels so mipmaps do not pick up a dark fringe.
        public void Finish(float shadeBottom, float shadeTop)
        {
            Color sum = Color.clear;
            int n = 0;
            for (int y = 0; y < H; y++)
            {
                float shade = Mathf.Lerp(shadeBottom, shadeTop, y / (float)(H - 1));
                for (int x = 0; x < W; x++)
                {
                    ref var p = ref Px[y * W + x];
                    if (p.a > 0.004f)
                    {
                        p.r = p.r / p.a * shade; p.g = p.g / p.a * shade; p.b = p.b / p.a * shade;
                        if (p.a > 0.5f) { sum += p; n++; }
                    }
                }
            }
            var mean = n > 0 ? sum / n : new Color(0.3f, 0.4f, 0.2f);
            for (int i = 0; i < Px.Length; i++)
                if (Px[i].a <= 0.004f) Px[i] = new Color(mean.r, mean.g, mean.b, 0f);
            _asSource = null;
        }
    }

    static Vector2 Centre(RectInt r) => new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

    // Broadleaf leaf card: a round cluster of leaves pointing out from its middle, darker leaves underneath.
    static Canvas ComposeLeafCluster(Source leaf)
    {
        const int size = 512;
        var canvas = new Canvas(size, size);
        var rng = new Random(511);
        var centre = new Vector2(size * 0.5f, size * 0.5f);
        const int count = 78;
        for (int i = 0; i < count; i++)
        {
            int sprite = rng.Next(leaf.Sprites.Count);
            var rect = leaf.Sprites[sprite];
            float r = Mathf.Sqrt((float)rng.NextDouble()) * size * 0.34f;
            float angle = (float)rng.NextDouble() * 360f;
            var at = centre + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * r;
            float length = Mathf.Max(rect.width, rect.height);
            float target = (sprite == 0 ? 190f : 105f) * (0.8f + 0.4f * (float)rng.NextDouble());
            float light = Mathf.Lerp(0.55f, 1.05f, i / (float)(count - 1)) * (0.92f + 0.16f * (float)rng.NextDouble());
            var tint = new Color(light * (0.95f + 0.1f * (float)rng.NextDouble()), light, light * 0.95f);
            // The sprite's long axis along the way out from the middle.
            float along = rect.width >= rect.height ? 0f : 90f;
            canvas.Stamp(leaf, rect, Centre(rect), at, target / length, angle - along + (float)(rng.NextDouble() - 0.5) * 70f, tint, rng.Next(2) == 0);
        }
        canvas.Finish(0.85f, 1f);
        return canvas;
    }

    // Conifer branch card: sprigs fanned along a twig, the base at u = 0 (the trunk end), the tip at u = 1.
    // In LeafSet019 the woody end of every sprig is on its right, so the sprigs are mirrored.
    static Canvas ComposeConiferBranch(Source sprig)
    {
        const int w = 512, h = 256;
        var canvas = new Canvas(w, h);
        var rng = new Random(1019);
        canvas.Fill(new Rect(0f, h * 0.5f - 3f, 90f, 6f), new Color(0.24f, 0.18f, 0.13f, 1f));
        var sprites = sprig.Sprites;
        var main = sprites[0];
        // Pivot of a sprig: the middle of its woody (right) end.
        Vector2 Base(RectInt r) => new Vector2(r.xMax - 2f, r.y + r.height * 0.5f);
        for (int i = 0; i < 9; i++)
        {
            var rect = sprites[rng.Next(sprites.Count)];
            float t = i / 8f;
            float startX = Mathf.Lerp(20f, 300f, t);
            float length = Mathf.Lerp(330f, 190f, t) * (0.85f + 0.3f * (float)rng.NextDouble());
            float side = i % 2 == 0 ? 1f : -1f;
            float angle = side * Mathf.Lerp(26f, 14f, t) * (0.7f + 0.6f * (float)rng.NextDouble());
            float light = 0.72f + 0.25f * (float)rng.NextDouble();
            canvas.Stamp(sprig, rect, Base(rect), new Vector2(startX, h * 0.5f), length / rect.width, angle, new Color(light, light, light), flipX: true);
        }
        canvas.Stamp(sprig, main, Base(main), new Vector2(8f, h * 0.5f), 495f / main.width, 0f, Color.white, flipX: true);
        canvas.Finish(0.9f, 1f);
        return canvas;
    }

    // Grass card: blades standing on the bottom edge, leaning a little, darker at the foot.
    static Canvas ComposeGrass(Source blade)
    {
        const int w = 512, h = 256;
        var canvas = new Canvas(w, h);
        var rng = new Random(606);
        // Upright blades (taller than wide); the curled ones of the atlas read as litter on a card.
        var upright = blade.Sprites.FindAll(r => r.height > r.width * 2);
        if (upright.Count == 0) upright = blade.Sprites;
        for (int i = 0; i < 84; i++)
        {
            var rect = upright[rng.Next(upright.Count)];
            float x = 16f + (float)rng.NextDouble() * (w - 32f);
            float height = h * (0.45f + 0.5f * (float)rng.NextDouble());
            float lean = (x - w * 0.5f) / (w * 0.5f) * 14f + (float)(rng.NextDouble() - 0.5) * 16f;
            float light = 0.75f + 0.3f * (float)rng.NextDouble();
            var tint = new Color(light * (0.95f + 0.12f * (float)rng.NextDouble()), light, light * 0.9f);
            // Pivot: the foot of the blade (bottom of its rectangle; row 0 is the bottom).
            canvas.Stamp(blade, rect, new Vector2(rect.x + rect.width * 0.5f, rect.yMin + 2f), new Vector2(x, 0f), height / rect.height, -lean, tint, rng.Next(2) == 0);
        }
        canvas.Finish(0.55f, 1.05f);
        return canvas;
    }

    // Billboards: left half the tree from the side (trunk at the bottom middle), right half from above.
    const int BoardSize = 256;

    static Canvas ComposeBroadleafBillboard(Canvas cluster)
    {
        var canvas = new Canvas(BoardSize * 2, BoardSize);
        var rng = new Random(77);
        float cx = BoardSize * 0.5f;
        canvas.Fill(new Rect(cx - 5f, 0f, 10f, BoardSize * 0.5f), new Color(0.22f, 0.18f, 0.14f, 1f));
        // Crown: an ellipse of clusters, the lower ones darker.
        for (int i = 0; i < 46; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble());
            var at = new Vector2(cx + Mathf.Cos(a) * r * BoardSize * 0.36f, BoardSize * 0.6f + Mathf.Sin(a) * r * BoardSize * 0.3f);
            float light = Mathf.Lerp(0.62f, 1f, (at.y - BoardSize * 0.3f) / (BoardSize * 0.6f)) * (0.9f + 0.15f * (float)rng.NextDouble());
            canvas.Stamp(cluster, at, 0.24f, (float)rng.NextDouble() * 360f, new Color(light, light, light));
        }
        for (int i = 0; i < 40; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble());
            var at = new Vector2(BoardSize + cx + Mathf.Cos(a) * r * BoardSize * 0.34f, BoardSize * 0.5f + Mathf.Sin(a) * r * BoardSize * 0.34f);
            float light = 0.75f + 0.3f * (1f - r);
            canvas.Stamp(cluster, at, 0.24f, (float)rng.NextDouble() * 360f, new Color(light, light, light));
        }
        canvas.Finish(1f, 1f);
        return canvas;
    }

    static Canvas ComposeConiferBillboard(Canvas branch)
    {
        var canvas = new Canvas(BoardSize * 2, BoardSize);
        var rng = new Random(78);
        float cx = BoardSize * 0.5f;
        canvas.Fill(new Rect(cx - 3f, 0f, 6f, BoardSize * 0.95f), new Color(0.2f, 0.15f, 0.12f, 1f));
        // Tiers from the bottom of the crown to the tip, each a branch out to either side, drooping.
        const int tiers = 14;
        for (int t = 0; t < tiers; t++)
        {
            float f = t / (float)(tiers - 1);
            float y = Mathf.Lerp(BoardSize * 0.17f, BoardSize * 0.95f, f);
            float length = Mathf.Lerp(BoardSize * 0.44f, BoardSize * 0.06f, f);
            float light = Mathf.Lerp(0.7f, 1f, f);
            for (int side = -1; side <= 1; side += 2)
            {
                float droop = 14f + 8f * (float)rng.NextDouble();
                // A branch card is twice as long as wide; its middle goes half a length out from the trunk.
                var at = new Vector2(cx + side * length * 0.5f, y - Mathf.Sin(droop * Mathf.Deg2Rad) * length * 0.5f);
                canvas.Stamp(branch, at, length / branch.W, side > 0 ? -droop : droop, new Color(light, light, light), side < 0);
            }
        }
        for (int i = 0; i < 16; i++)
        {
            float angle = i * 22.5f + (float)rng.NextDouble() * 10f;
            var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            float length = BoardSize * 0.42f;
            canvas.Stamp(branch, new Vector2(BoardSize + cx, BoardSize * 0.5f) + dir * length * 0.5f, length / branch.W, angle, Color.white);
        }
        canvas.Finish(1f, 1f);
        return canvas;
    }

    static Texture2D Save(string name, Canvas canvas)
    {
        var path = $"{WorldMaterials.TextureDir}/{name}.png";
        var pixels = new Color32[canvas.Px.Length];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = canvas.Px[i];
        var tex = new Texture2D(canvas.W, canvas.H, TextureFormat.RGBA32, false, false);
        tex.SetPixels32(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        // Alpha-tested cards keep their coverage in the small mips instead of thinning to nothing far away.
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = LeafCutoff;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return SceneLook.Load<Texture2D>(path);
    }

    // ---- materials ----

    static Material Asset(string name)
    {
        var path = $"{WorldMaterials.MaterialDir}/{name}.mat";
        var shader = Shader.Find("Game/WorldFoliage") ?? throw new FileNotFoundException("Game/WorldFoliage shader not found");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader) mat.shader = shader;
        // Drawn with Graphics.RenderMeshInstanced.
        mat.enableInstancing = true;
        return mat;
    }

    // Bark: URP Lit with the scan's colour and normal and a mask packed from its roughness and occlusion.
    static Material Bark(string name, string id, float windHeight, float bend)
    {
        var maskPath = $"{WorldMaterials.TextureDir}/{id}_mask.png";
        var rough = LoadFile($"{BarkArt}/{id}/{id}_roughness.jpg") ?? throw new FileNotFoundException("roughness map missing for " + id);
        var ao = LoadFile($"{BarkArt}/{id}/{id}_ao.jpg");
        var r = rough.GetPixels32();
        var a = ao != null && ao.width == rough.width && ao.height == rough.height ? ao.GetPixels32() : null;
        var packed = new Color32[r.Length];
        for (int i = 0; i < r.Length; i++) packed[i] = new Color32(0, a != null ? a[i].r : (byte)255, 128, (byte)(255 - r[i].r));
        WorldMaterials.WritePng(maskPath, rough.width, rough.height, packed);
        Object.DestroyImmediate(rough);
        if (ao != null) Object.DestroyImmediate(ao);
        var mask = WorldMaterials.ImportTexture(maskPath, true, false, TextureWrapMode.Repeat, true);

        var mat = Asset("World_Bark_" + name);
        mat.SetTexture("_BaseMap", SceneLook.Load<Texture2D>($"{BarkArt}/{id}/{id}_color.jpg"));
        mat.SetTexture("_BumpMap", SceneLook.Load<Texture2D>($"{BarkArt}/{id}/{id}_normal.jpg"));
        mat.SetTexture("_MaskMap", mask);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_UseNormal", 1f);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_AlphaClip", 0f);
        mat.DisableKeyword("_MATTE");
        mat.SetFloat("_Matte", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Back);
        mat.SetFloat("_Smoothness", 0.6f);
        mat.SetFloat("_BumpScale", 1f);
        mat.SetColor("_BaseColor", BarkTint);
        mat.renderQueue = (int)RenderQueue.Geometry;
        mat.SetOverrideTag("RenderType", "Opaque");
        Wind(mat, windHeight, bend, 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void Wind(Material mat, float height, float bend, float flutter)
    {
        mat.SetFloat("_WindHeight", height);
        mat.SetFloat("_WindBend", bend);
        mat.SetFloat("_Flutter", flutter);
    }

    // Leaves, needles, grass and billboards: alpha-tested, both faces, no highlights (a waxy sheen reads as plastic).
    static Material Foliage(string name, Texture2D texture, Color tint, float windHeight, float bend, float flutter)
    {
        var mat = Asset(name);
        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", LeafCutoff);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_MATTE");
        mat.SetFloat("_Matte", 1f);
        mat.DisableKeyword("_NORMALMAP");
        mat.SetFloat("_UseNormal", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.doubleSidedGI = true;
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        mat.SetFloat("_Smoothness", 0.1f);
        Wind(mat, windHeight, bend, flutter);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---- meshes ----

    // Vertices of one mesh with any number of submeshes (0 bark, 1 leaves).
    sealed class MeshBuilder
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int>[] _tris;

        public MeshBuilder(int submeshes)
        {
            _tris = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) _tris[i] = new List<int>();
        }

        // A tapered tube through `spine` with `radius` at each point; u round the tube, v up it, in bark repeats.
        public void Tube(int submesh, IReadOnlyList<Vector3> spine, IReadOnlyList<float> radius, int sides, bool cap)
        {
            int first = _v.Count;
            float climbed = 0f;
            for (int i = 0; i < spine.Count; i++)
            {
                var along = (i + 1 < spine.Count ? spine[i + 1] - spine[i] : spine[i] - spine[i - 1]).normalized;
                if (i > 0 && i + 1 < spine.Count) along = (spine[i + 1] - spine[i - 1]).normalized;
                var side = Vector3.Cross(along, Mathf.Abs(along.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
                var other = Vector3.Cross(side, along);
                if (i > 0) climbed += Vector3.Distance(spine[i], spine[i - 1]);
                float around = 2f * Mathf.PI * radius[0] / BarkTile;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    var outward = side * Mathf.Cos(a) + other * Mathf.Sin(a);
                    _v.Add(spine[i] + outward * radius[i]);
                    _n.Add(outward);
                    _uv.Add(new Vector2(s / (float)sides * Mathf.Max(1f, Mathf.Round(around)), climbed / BarkTile));
                }
            }
            int ring = sides + 1;
            for (int i = 0; i + 1 < spine.Count; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = first + i * ring + s, b = a + 1, c = a + ring, d = c + 1;
                    _tris[submesh].AddRange(new[] { a, c, b, b, c, d });
                }
            if (cap)
            {
                // Close the tip with a point so a broken-off end does not show the inside.
                int tip = _v.Count;
                var last = spine[spine.Count - 1];
                var dir = (last - spine[spine.Count - 2]).normalized;
                _v.Add(last + dir * radius[radius.Count - 1]);
                _n.Add(dir);
                _uv.Add(new Vector2(0.5f, (climbed + radius[radius.Count - 1]) / BarkTile));
                int top = first + (spine.Count - 1) * ring;
                for (int s = 0; s < sides; s++) _tris[submesh].AddRange(new[] { top + s, tip, top + s + 1 });
            }
        }

        // A card: `origin` is the middle of its bottom edge (u = uvMin.x .. uvMax.x along `right`, v up along `up`).
        // Vertex normals are given per corner (crowns light like a ball, grass like the ground).
        public void Card(int submesh, Vector3 origin, Vector3 right, Vector3 up, float width, float height, Rect uv, Func<Vector3, Vector3> normal)
        {
            int first = _v.Count;
            var r = right.normalized * (width * 0.5f);
            var u = up.normalized * height;
            var corners = new[] { origin - r, origin + r, origin - r + u, origin + r + u };
            var uvs = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax) };
            for (int i = 0; i < 4; i++)
            {
                _v.Add(corners[i]);
                _n.Add(normal(corners[i]).normalized);
                _uv.Add(uvs[i]);
            }
            _tris[submesh].AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = _tris.Length;
            for (int i = 0; i < _tris.Length; i++) mesh.SetTriangles(_tris[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }

    // One entry per Vegetation.Variants: a squat wide oak, a slim young tree, a tall old one and so on.
    static readonly float[] BroadleafHeight = { 9.5f, 11.7f, 13.9f, 8.2f, 12.6f };
    static readonly float[] BroadleafCrown = { 0.32f, 0.35f, 0.38f, 0.42f, 0.30f };
    static readonly float[] ConiferHeight = { 12f, 15f, 18f, 10f, 16.5f };
    static readonly float[] BushRadius = { 0.85f, 1.05f, 1.25f, 0.7f, 1.5f };
    static readonly float[] BushHeight = { 1.1f, 1.35f, 1.6f, 0.8f, 1.15f };

    static readonly Rect FullCard = new Rect(0f, 0f, 1f, 1f);
    static readonly Rect BoardSide = new Rect(0f, 0f, 0.5f, 1f), BoardTop = new Rect(0.5f, 0f, 0.5f, 1f);

    static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    static Vector3 RandomDirection(Random rng)
    {
        float y = Range(rng, -1f, 1f), a = Range(rng, 0f, Mathf.PI * 2f), r = Mathf.Sqrt(1f - y * y);
        return new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
    }

    // Normals of crown cards: out from the middle of the crown, tipped toward the sky, so a crown lights like a
    // soft ball instead of a heap of flat cards.
    static Func<Vector3, Vector3> Crown(Vector3 centre) => p => (p - centre).normalized + Vector3.up * 0.5f;

    // A billboard: two crossed upright cards with the side view and one flat card with the view from above.
    static void Billboard(MeshBuilder mb, float width, float height, float topY, float topSize)
    {
        Func<Vector3, Vector3> soft = p => Vector3.up + new Vector3(p.x, 0f, p.z).normalized * 0.6f;
        mb.Card(0, Vector3.zero, Vector3.right, Vector3.up, width, height, BoardSide, soft);
        mb.Card(0, Vector3.zero, Vector3.forward, Vector3.up, width, height, BoardSide, soft);
        mb.Card(0, new Vector3(0f, topY, -topSize * 0.5f), Vector3.right, Vector3.forward, topSize, topSize, BoardTop, _ => Vector3.up);
    }

    static VegetationRenderer.Lod Lod(Mesh mesh, bool shadows, params Material[] materials) =>
        new VegetationRenderer.Lod { Mesh = mesh, Materials = materials, CastShadows = shadows };

    // Broadleaf: a leaning trunk that forks into a few rising limbs under a rounded crown of leaf cards.
    static VegetationRenderer.Prototype BroadleafTree(int variant, WorldMeshes.Store store, Material bark, Material leaves, Material board,
        out float trunkRadius, out float trunkHeight)
    {
        var rng = new Random(4100 + variant);
        float height = BroadleafHeight[variant] + Range(rng, -0.5f, 0.5f);
        float crownY = height * 0.62f, crownRx = height * BroadleafCrown[variant], crownRy = height * 0.3f;
        trunkRadius = 0.2f + 0.035f * height * 0.5f;
        trunkHeight = height * 0.48f;
        float limbRadius = trunkRadius;
        var lean = new Vector3(Range(rng, -0.3f, 0.3f), 0f, Range(rng, -0.3f, 0.3f));
        var trunk = new List<Vector3>();
        var radius = new List<float>();
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            // From below the ground, so a trunk on a slope shows no gap on its downhill side.
            trunk.Add(new Vector3(0f, Mathf.Lerp(-0.6f, trunkHeight, t), 0f) + lean * t * t);
            radius.Add(Mathf.Lerp(trunkRadius * 1.15f, trunkRadius * 0.6f, t));
        }
        var top = trunk[trunk.Count - 1];
        var crown = new Vector3(top.x, crownY, top.z);
        int limbs = 4 + rng.Next(2);
        var limbEnds = new List<Vector3>();
        var limbSpines = new List<List<Vector3>>();
        for (int b = 0; b < limbs; b++)
        {
            float around = (b + Range(rng, -0.25f, 0.25f)) / limbs * Mathf.PI * 2f;
            float rise = Range(rng, 40f, 58f) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(around) * Mathf.Cos(rise), Mathf.Sin(rise), Mathf.Sin(around) * Mathf.Cos(rise));
            float length = crownRx * Range(rng, 0.65f, 0.85f);
            var start = Vector3.Lerp(trunk[2], top, Range(rng, 0.3f, 1f));
            var spine = new List<Vector3> { start, start + dir * length * 0.5f + Vector3.up * 0.3f, start + dir * length + Vector3.up * 0.5f };
            limbSpines.Add(spine);
            limbEnds.Add(spine[2]);
        }

        Mesh Make(int lod)
        {
            var mb = new MeshBuilder(2);
            mb.Tube(0, trunk, radius, lod == 0 ? 8 : 6, false);
            int limbCount = lod == 0 ? limbs : 2;
            for (int b = 0; b < limbCount; b++)
                mb.Tube(0, limbSpines[b], new[] { limbRadius * 0.55f, limbRadius * 0.35f, limbRadius * 0.15f }, lod == 0 ? 5 : 4, true);
            var cards = new Random(4200 + variant);
            int count = lod == 0 ? 64 : 24;
            for (int i = 0; i < count; i++)
            {
                // Most cards hug the limb ends, the rest fill the shell of the crown.
                Vector3 at;
                if (i % 5 < 3)
                {
                    var end = limbEnds[i % limbEnds.Count];
                    at = end + RandomDirection(cards) * Range(cards, 0.3f, 1.6f);
                }
                else
                {
                    var d = RandomDirection(cards);
                    at = crown + Vector3.Scale(d, new Vector3(crownRx, crownRy, crownRx)) * Range(cards, 0.55f, 0.95f);
                }
                float size = (lod == 0 ? Range(cards, 1.9f, 2.7f) : Range(cards, 2.8f, 3.6f)) * (height / 11f);
                var facing = (at - crown).normalized;
                if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
                facing = (facing + RandomDirection(cards) * 0.5f).normalized;
                var right = Vector3.Cross(facing, RandomDirection(cards)).normalized;
                var up = Vector3.Cross(right, facing).normalized;
                mb.Card(1, at - up * (size * 0.5f), right, up, size, size, FullCard, Crown(crown));
            }
            return store.Add(mb.ToMesh($"Tree_Broadleaf{variant}_LOD{lod}"));
        }

        var boardBuilder = new MeshBuilder(1);
        Billboard(boardBuilder, crownRx * 2.25f, height * 1.04f, crownY, crownRx * 2.1f);
        return new VegetationRenderer.Prototype
        {
            Name = "Broadleaf" + variant,
            Kind = PlantKind.Broadleaf,
            Height = height,
            Lods = new[]
            {
                Lod(Make(0), true, bark, leaves),
                Lod(Make(1), true, bark, leaves),
                Lod(store.Add(boardBuilder.ToMesh($"Tree_Broadleaf{variant}_Billboard")), false, board),
            },
        };
    }

    // Conifer: a straight trunk carrying whorls of drooping branch cards that shorten toward the top.
    static VegetationRenderer.Prototype ConiferTree(int variant, WorldMeshes.Store store, Material bark, Material leaves, Material board,
        out float trunkRadius, out float trunkHeight)
    {
        var rng = new Random(5100 + variant);
        float height = ConiferHeight[variant] + Range(rng, -0.6f, 0.6f);
        float spread = height * Range(rng, 0.22f, 0.26f);
        trunkRadius = 0.18f + 0.012f * height;
        trunkHeight = height;
        var trunk = new List<Vector3>();
        var radius = new List<float>();
        var lean = new Vector3(Range(rng, -0.15f, 0.15f), 0f, Range(rng, -0.15f, 0.15f));
        for (int i = 0; i <= 5; i++)
        {
            float t = i / 5f;
            trunk.Add(new Vector3(0f, Mathf.Lerp(-0.6f, height, t), 0f) + lean * t);
            radius.Add(Mathf.Lerp(trunkRadius * 1.1f, 0.04f, t));
        }

        Mesh Make(int lod)
        {
            var mb = new MeshBuilder(2);
            mb.Tube(0, trunk, radius, lod == 0 ? 7 : 5, false);
            var cards = new Random(5200 + variant);
            int tiers = lod == 0 ? 11 : 6, perTier = lod == 0 ? 6 : 4;
            float bottom = height * 0.16f;
            float turn = cards.Next(360);
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);
                float y = Mathf.Lerp(bottom, height * 0.93f, f);
                float length = Mathf.Lerp(spread, spread * 0.12f, Mathf.Pow(f, 0.9f)) + 0.35f;
                var at = new Vector3(lean.x * y / height, y, lean.z * y / height);
                turn += 137.5f;
                for (int k = 0; k < perTier; k++)
                {
                    float around = (turn + k * 360f / perTier + Range(cards, -12f, 12f)) * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Cos(around), 0f, Mathf.Sin(around));
                    float droop = Range(cards, 12f, 26f) * Mathf.Deg2Rad;
                    var along = (outward * Mathf.Cos(droop) - Vector3.up * Mathf.Sin(droop)).normalized;
                    var across = Vector3.Cross(Vector3.up, outward).normalized;
                    // Roll the card about its branch so the tiers have depth from every side.
                    float roll = Range(cards, -35f, 35f);
                    across = Quaternion.AngleAxis(roll, along) * across;
                    AddBranchCard(mb, at, across, along, length * 0.55f, length, outward);
                }
            }
            // The leader: a small tuft of crossed cards at the tip.
            for (int k = 0; k < (lod == 0 ? 3 : 2); k++)
            {
                float around = k * 60f * Mathf.Deg2Rad;
                var side = new Vector3(Mathf.Cos(around), 0f, Mathf.Sin(around));
                AddBranchCard(mb, new Vector3(lean.x, height * 0.86f, lean.z), side, Vector3.up, 0.9f, height * 0.16f, Vector3.up);
            }
            return store.Add(mb.ToMesh($"Tree_Conifer{variant}_LOD{lod}"));
        }

        var boardBuilder = new MeshBuilder(1);
        Billboard(boardBuilder, spread * 2.2f, height * 1.02f, height * 0.4f, spread * 2f);
        return new VegetationRenderer.Prototype
        {
            Name = "Conifer" + variant,
            Kind = PlantKind.Conifer,
            Height = height,
            Lods = new[]
            {
                Lod(Make(0), true, bark, leaves),
                Lod(Make(1), true, bark, leaves),
                Lod(store.Add(boardBuilder.ToMesh($"Tree_Conifer{variant}_Billboard")), false, board),
            },
        };
    }

    // A branch card from `root` out along `along`, `width` across it. The card texture's u runs along the branch
    // (base 0, tip 1), so Card's "right" is the branch and its "up" runs across: the origin is the middle of the edge
    // on one side of the branch.
    static void AddBranchCard(MeshBuilder mb, Vector3 root, Vector3 across, Vector3 along, float width, float length, Vector3 outward)
    {
        var origin = root + along * (length * 0.5f) - across * (width * 0.5f);
        Func<Vector3, Vector3> normal = p => outward + Vector3.up * 0.6f;
        mb.Card(1, origin, along, across, length, width, FullCard, normal);
    }

    // Bush: a low mound of leaf cards, no trunk, one LOD (dropped beyond Vegetation.BushDistance).
    static VegetationRenderer.Prototype Bush(int variant, WorldMeshes.Store store, Material leaves)
    {
        var rng = new Random(6100 + variant);
        float radius = BushRadius[variant], height = BushHeight[variant];
        var centre = new Vector3(0f, height * 0.45f, 0f);
        var mb = new MeshBuilder(1);
        for (int i = 0; i < 16; i++)
        {
            var d = RandomDirection(rng);
            d.y = Mathf.Abs(d.y) * 0.8f;
            var at = centre + Vector3.Scale(d, new Vector3(radius, height * 0.5f, radius)) * Range(rng, 0.3f, 0.8f);
            float size = Range(rng, 1.0f, 1.5f) * (radius / 0.9f);
            var facing = ((at - centre).normalized + RandomDirection(rng) * 0.6f).normalized;
            var right = Vector3.Cross(facing, RandomDirection(rng)).normalized;
            var up = Vector3.Cross(right, facing).normalized;
            mb.Card(0, at - up * (size * 0.5f), right, up, size, size, FullCard, Crown(centre - Vector3.up * 0.3f));
        }
        return new VegetationRenderer.Prototype
        {
            Name = "Bush" + variant,
            Kind = PlantKind.Bush,
            Height = height,
            Lods = new[] { Lod(store.Add(mb.ToMesh($"Bush{variant}")), false, leaves) },
        };
    }

    // Flowers: three columns of one atlas (white, yellow, violet), each a bunch of stems with blooms. Painted here
    // from the grass blade sprites and plain discs: nothing is downloaded (the blooms are our own shapes).
    static Canvas ComposeFlowers(Source blade)
    {
        const int w = 384, h = 256, column = w / 3;
        var canvas = new Canvas(w, h);
        var rng = new Random(909);
        var bloom = new[] { new Color(0.94f, 0.94f, 0.9f), new Color(0.95f, 0.78f, 0.18f), new Color(0.62f, 0.42f, 0.82f) };
        var upright = blade.Sprites.FindAll(r => r.height > r.width * 2);
        if (upright.Count == 0) upright = blade.Sprites;
        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < 16; i++)
            {
                var rect = upright[rng.Next(upright.Count)];
                float x = c * column + 10f + (float)rng.NextDouble() * (column - 20f);
                float light = 0.7f + 0.3f * (float)rng.NextDouble();
                canvas.Stamp(blade, rect, new Vector2(rect.x + rect.width * 0.5f, rect.yMin + 2f), new Vector2(x, 0f), h * (0.3f + 0.25f * (float)rng.NextDouble()) / rect.height,
                    (float)(rng.NextDouble() - 0.5) * 24f, new Color(light, light, light * 0.9f), rng.Next(2) == 0);
            }
            for (int i = 0; i < 5; i++)
            {
                float x = c * column + 16f + (float)rng.NextDouble() * (column - 32f), top = h * (0.62f + 0.3f * (float)rng.NextDouble());
                var stem = new Color(0.24f, 0.42f, 0.16f);
                for (int y = 0; y < (int)top; y++)
                    for (int dx = 0; dx < 2; dx++) canvas.Over((int)(x + dx + Mathf.Sin(y * 0.05f + i) * 2f), y, stem, 1f);
                float radius = 5.5f + 2.5f * (float)rng.NextDouble();
                canvas.Disc(x, top, radius, bloom[c] * (0.85f + 0.2f * (float)rng.NextDouble()));
                canvas.Disc(x, top, radius * 0.35f, new Color(0.85f, 0.62f, 0.1f));
            }
        }
        canvas.Finish(0.6f, 1.05f);
        return canvas;
    }

    // Three crossed cards, one slice of the flower atlas each, so a clump carries all three colours.
    static Mesh FlowerClump(string name, float width, float height)
    {
        var mb = new MeshBuilder(1);
        for (int k = 0; k < 3; k++)
        {
            float a = k * 60f * Mathf.Deg2Rad;
            mb.Card(0, Vector3.zero, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), Vector3.up, width, height, new Rect(k / 3f, 0f, 1f / 3f, 1f), _ => Vector3.up);
        }
        return mb.ToMesh(name);
    }

    // Grass clump: three upright cards crossing at 60 degrees. Normals point up so the clump lights like the
    // ground it stands on instead of flashing as the camera turns round it.
    static Mesh GrassClump(string name, float width, float height)
    {
        var mb = new MeshBuilder(1);
        for (int k = 0; k < 3; k++)
        {
            float a = k * 60f * Mathf.Deg2Rad;
            mb.Card(0, Vector3.zero, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), Vector3.up, width, height, FullCard, _ => Vector3.up);
        }
        return mb.ToMesh(name);
    }
}

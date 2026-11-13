using System;
using System.Diagnostics;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Renders the world map picture (PUBG_GAP 4-4 batch R) from the same WorldPlan the world builder uses: shaded relief
// from the heights, sea and shallows, roads, building footprints, region borders with gaps at the passes. Plain
// pixel maths, no graphics device. Run: tools/unity.sh run MapBaker.Bake
// The picture is code-made (no outside asset), 2048 x 2048 over the square island; MapProjection ties pixels to
// world metres for the baker and for the runtime MapImage / MapScreen alike.
public static class MapBaker
{
    public const string OutputPath = "Assets/_Project/Resources/Map/world_map.png";

    const int N = MapProjection.PictureSize;
    const int Grid = 1024;                  // analysis grid: one sample per metre on the default 1024 m map

    // "Field terminal" palette: muted, dark, one amber accent.
    static readonly Color32 SeaDeep = new Color32(15, 24, 33, 255);
    static readonly Color32 SeaShallow = new Color32(32, 52, 62, 255);
    static readonly Color32 Sand = new Color32(122, 112, 86, 255);
    static readonly Color32 Mountain = new Color32(88, 92, 82, 255);
    static readonly Color32 Wetland = new Color32(58, 80, 70, 255);
    static readonly Color32 Town = new Color32(94, 96, 68, 255);
    static readonly Color32 Factory = new Color32(108, 92, 66, 255);
    static readonly Color32 Casing = new Color32(16, 19, 22, 255);
    static readonly Color32 RoadTrunk = new Color32(214, 208, 186, 255);
    static readonly Color32 RoadSpur = new Color32(176, 171, 156, 255);
    static readonly Color32 RoadPath = new Color32(134, 130, 118, 255);
    static readonly Color32 BuildingFill = new Color32(160, 156, 142, 255);
    static readonly Color32 HallFill = new Color32(138, 142, 144, 255);
    static readonly Color32 Amber = new Color32(245, 163, 38, 255);
    static readonly Color32 Border = new Color32(214, 190, 140, 255);

    [MenuItem("Tools/Map/Bake World Map")]
    public static void Bake()
    {
        var timer = Stopwatch.StartNew();
        var plan = WorldGenerator.Generate(LoadSpec());
        var pixels = Render(plan);
        long rendered = timer.ElapsedMilliseconds;

        var texture = new Texture2D(N, N, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        byte[] png = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllBytes(OutputPath, png);
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = N;
        importer.isReadable = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
        Debug.Log($"[MapBaker] {OutputPath}: {N}x{N}, {png.Length / 1024} KB, render {rendered} ms, total {timer.ElapsedMilliseconds} ms " +
                  $"({plan.Roads.Count} roads, {plan.Lots.Count} buildings, {plan.Elevators.Count} elevators, {plan.Passes.Count} passes)");
    }

    // Same spec the world builder uses.
    static WorldSpec LoadSpec()
    {
        var text = Resources.Load<TextAsset>("GameData/world");
        if (text != null)
        {
            var spec = JsonUtility.FromJson<WorldSpec>(text.text);
            if (spec != null && spec.Regions != null && spec.Regions.Length > 0) return spec;
        }
        return WorldSpec.CreateDefault();
    }

    // Row 0 is the south edge (Texture2D order); see MapProjection.
    public static Color32[] Render(WorldPlan plan)
    {
        float size = plan.Size, sea = plan.SeaLevel, cell = size / Grid;
        int nodes = Grid + 1;
        var height = new float[nodes * nodes];
        var border = new float[nodes * nodes];
        var region = new sbyte[nodes * nodes];
        for (int j = 0; j < nodes; j++)
            for (int i = 0; i < nodes; i++)
            {
                float x = i * cell, z = j * cell;
                int k = j * nodes + i;
                height[k] = plan.Height(x, z);
                int r = plan.RegionAt(x, z);
                region[k] = (sbyte)r;
                border[k] = r >= 0 ? Math.Min(plan.BorderDistance(x, z), 99f) : 99f;
            }

        // Hillshade (light from the north-west, 45 degrees up, relief exaggerated) and slope per node.
        var shade = new float[nodes * nodes];
        var slope = new float[nodes * nodes];
        const float azimuth = 315f * Mathf.Deg2Rad, altitude = 45f * Mathf.Deg2Rad, exaggeration = 2f;
        float lx = Mathf.Cos(altitude) * Mathf.Sin(azimuth), ly = Mathf.Sin(altitude), lz = Mathf.Cos(altitude) * Mathf.Cos(azimuth);
        for (int j = 0; j < nodes; j++)
            for (int i = 0; i < nodes; i++)
            {
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, Grid), j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, Grid);
                float gx = (height[j * nodes + i1] - height[j * nodes + i0]) / ((i1 - i0) * cell);
                float gz = (height[j1 * nodes + i] - height[j0 * nodes + i]) / ((j1 - j0) * cell);
                float nx = -gx * exaggeration, nz = -gz * exaggeration;
                float inv = 1f / Mathf.Sqrt(nx * nx + 1f + nz * nz);
                shade[j * nodes + i] = (nx * lx + ly + nz * lz) * inv;
                slope[j * nodes + i] = Mathf.Sqrt(gx * gx + gz * gz);
            }

        var land = new[] { Mountain, Wetland, Town, Factory };
        var pixels = new Color32[N * N];
        float pixelMetres = size / N;
        for (int py = 0; py < N; py++)
        {
            float z = (py + 0.5f) * pixelMetres;
            float fz = z / cell;
            int j = Math.Min((int)fz, Grid - 1);
            float tz = fz - j;
            for (int px = 0; px < N; px++)
            {
                float x = (px + 0.5f) * pixelMetres;
                float fx = x / cell;
                int i = Math.Min((int)fx, Grid - 1);
                float tx = fx - i;
                int k00 = j * nodes + i, k10 = k00 + 1, k01 = k00 + nodes, k11 = k01 + 1;
                float h = Bilerp(height, k00, k10, k01, k11, tx, tz);
                float s = Bilerp(shade, k00, k10, k01, k11, tx, tz);
                float g = Bilerp(slope, k00, k10, k01, k11, tx, tz);

                float depth = sea - h;
                float coverage = Mathf.Clamp01(0.5f - depth / (Math.Max(g, 0.02f) * pixelMetres * 1.0f));
                var colour = ToVec(Mix(SeaDeep, SeaShallow, 1f - Smooth(depth / 8f)));
                if (depth > 0f && depth < 2f) colour = Vector3.Lerp(colour, ToVec(SeaShallow) * 1.25f, 1f - depth / 2f);

                if (coverage > 0f)
                {
                    int kr = (tx > 0.5f ? k10 : k00) + (tz > 0.5f ? nodes : 0);
                    int r = region[kr];
                    int kind = r >= 0 && r < plan.Regions.Count ? (int)plan.Regions[r].Kind : (int)RegionKind.Town;
                    var ground = ToVec(land[kind]);
                    float above = Mathf.Clamp01((h - sea) / 70f);
                    ground *= 0.86f + 0.34f * above;
                    float beach = 1f - Smooth((h - sea) / 1.6f);
                    ground = Vector3.Lerp(ground, ToVec(Sand), 0.8f * beach);
                    ground *= Mathf.Clamp(1f + (s - 0.7071f) * 1.5f, 0.45f, 1.45f);
                    // Contour lines every 10 m, only where the ground is sloped enough for them to read as lines.
                    if (g > 0.08f)
                    {
                        float d = Math.Abs(h - Mathf.Round(h / 10f) * 10f) / g;
                        ground *= 1f - 0.13f * Mathf.Clamp01(1.2f - d / (pixelMetres * 1.2f));
                    }
                    colour = Vector3.Lerp(colour, ground, coverage);
                }

                // Region border ridge line, with gaps at the passes.
                float bd = Bilerp(border, k00, k10, k01, k11, tx, tz);
                if (bd < 1.1f && coverage > 0.5f && !InPass(plan, x, z))
                    colour = Vector3.Lerp(colour, ToVec(Border), 0.5f * Mathf.Clamp01(1.5f - bd / 0.7f));

                pixels[py * N + px] = ToColor(colour);
            }
        }

        // Roads: dark casing first, then the light surface, so junctions stay clean.
        foreach (var road in plan.Roads) DrawRoad(pixels, plan, road, true);
        foreach (var road in plan.Roads) DrawRoad(pixels, plan, road, false);

        foreach (var lot in plan.Lots)
        {
            if (lot.Kind == BuildingKind.ElevatorStation) continue;
            bool hall = lot.Kind == BuildingKind.Warehouse || lot.Kind == BuildingKind.FactoryHall;
            DrawFootprint(pixels, plan, lot, hall ? HallFill : BuildingFill);
        }
        foreach (var elevator in plan.Elevators)
        {
            var station = new BuildingLot
            {
                Kind = BuildingKind.ElevatorStation, X = elevator.X, Z = elevator.Z, YawDeg = elevator.FacingDeg,
                SizeX = WorldElevator.StationWidth, SizeZ = WorldElevator.StationDepth,
            };
            DrawFootprint(pixels, plan, station, Amber);
        }
        return pixels;
    }

    static bool InPass(WorldPlan plan, float x, float z)
    {
        foreach (var pass in plan.Passes)
        {
            float dx = x - pass.X, dz = z - pass.Z, reach = pass.Width * 0.5f + 3f;
            if (dx * dx + dz * dz < reach * reach) return true;
        }
        return false;
    }

    static void DrawRoad(Color32[] pixels, WorldPlan plan, WorldRoad road, bool casing)
    {
        float toPx = N / plan.Size;
        float half = Math.Max(road.Width * 0.5f * toPx, 0.9f);
        var colour = casing ? Casing : road.Kind == RoadKind.Trunk ? RoadTrunk : road.Kind == RoadKind.Spur ? RoadSpur : RoadPath;
        float radius = casing ? half + 0.9f : half;
        float alpha = casing ? 0.8f : 1f;
        for (int i = 0; i + 1 < road.X.Length; i++)
            Capsule(pixels, road.X[i] * toPx, road.Z[i] * toPx, road.X[i + 1] * toPx, road.Z[i + 1] * toPx, radius, colour, alpha);
    }

    // Anti-aliased line with round ends, in pixel coordinates.
    static void Capsule(Color32[] pixels, float ax, float ay, float bx, float by, float radius, Color32 colour, float alpha)
    {
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - radius - 1f)), x1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(ax, bx) + radius + 1f));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - radius - 1f)), y1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(ay, by) + radius + 1f));
        float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f - ax, py = y + 0.5f - ay;
                float t = len2 > 1e-6f ? Mathf.Clamp01((px * dx + py * dy) / len2) : 0f;
                float ex = px - dx * t, ey = py - dy * t;
                float d = Mathf.Sqrt(ex * ex + ey * ey);
                float cover = Mathf.Clamp01(radius + 0.5f - d) * alpha;
                if (cover > 0f) Blend(pixels, y * N + x, colour, cover);
            }
    }

    // A rotated footprint: dark outline ring, then the fill inside it.
    static void DrawFootprint(Color32[] pixels, WorldPlan plan, BuildingLot lot, Color32 fill)
    {
        float toPx = N / plan.Size, pixelMetres = plan.Size / N;
        float reach = 0.5f * Mathf.Sqrt(lot.SizeX * lot.SizeX + lot.SizeZ * lot.SizeZ) + 2f;
        int x0 = Math.Max(0, (int)((lot.X - reach) * toPx)), x1 = Math.Min(N - 1, (int)((lot.X + reach) * toPx) + 1);
        int y0 = Math.Max(0, (int)((lot.Z - reach) * toPx)), y1 = Math.Min(N - 1, (int)((lot.Z + reach) * toPx) + 1);
        const float rim = 0.9f;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                lot.ToLocal((x + 0.5f) * pixelMetres, (y + 0.5f) * pixelMetres, out float right, out float forward);
                float inside = Math.Min(lot.SizeX * 0.5f - Math.Abs(right), lot.SizeZ * 0.5f - Math.Abs(forward)); // metres, > 0 inside
                float outer = Mathf.Clamp01(0.5f + inside / pixelMetres);
                if (outer <= 0f) continue;
                Blend(pixels, y * N + x, Casing, outer * 0.9f);
                float inner = Mathf.Clamp01(0.5f + (inside - rim) / pixelMetres);
                if (inner > 0f) Blend(pixels, y * N + x, fill, inner);
            }
    }

    static void Blend(Color32[] pixels, int index, Color32 colour, float alpha)
    {
        var p = pixels[index];
        pixels[index] = new Color32(
            (byte)(p.r + (colour.r - p.r) * alpha + 0.5f),
            (byte)(p.g + (colour.g - p.g) * alpha + 0.5f),
            (byte)(p.b + (colour.b - p.b) * alpha + 0.5f), 255);
    }

    static float Bilerp(float[] a, int k00, int k10, int k01, int k11, float tx, float tz) =>
        Mathf.Lerp(Mathf.Lerp(a[k00], a[k10], tx), Mathf.Lerp(a[k01], a[k11], tx), tz);

    static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    static Vector3 ToVec(Color32 c) => new Vector3(c.r, c.g, c.b);

    static Color32 Mix(Color32 a, Color32 b, float t) =>
        new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), 255);

    static Color32 ToColor(Vector3 v) =>
        new Color32((byte)Mathf.Clamp(v.x + 0.5f, 0f, 255f), (byte)Mathf.Clamp(v.y + 0.5f, 0f, 255f), (byte)Mathf.Clamp(v.z + 0.5f, 0f, 255f), 255);
}

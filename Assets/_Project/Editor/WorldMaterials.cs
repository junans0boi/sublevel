using System.Collections.Generic;
using System.Linq;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;

// Materials, terrain layers and generated textures of the outdoor map (ADR-052). The source maps are the CC0 sets
// under Art/ (ASSET_LEDGER A-0007..A-0023); everything derived from them is written under Prefabs/World.
public static class WorldMaterials
{
    public const string GeneratedDir = "Assets/_Project/Prefabs/World";
    public const string TextureDir = GeneratedDir + "/Textures";
    public const string MaterialDir = GeneratedDir + "/Materials";
    public const string LayerDir = GeneratedDir + "/Terrain";
    const string ArtMaterials = "Assets/_Project/Art/Materials";
    const string ArtModels = "Assets/_Project/Art/Models";
    const string WaterShader = "Game/WorldWater";
    const string TerrainShader = "Game/WorldTerrain"; // URP Terrain/Lit with a side-projected rock layer

    // A material and the size in metres one repeat of its texture covers (some sets are twice as wide as high).
    public struct Surface
    {
        public Material Material;
        public Vector2 Tile;
    }

    static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

    // Sets no table row picks (rock and mud layers, painted metal); the rest come from materials.json (ADR-185).
    static readonly string[] OwnSets = { "Rock051", "Ground054", "PaintedMetal014" };
    static readonly string[] TableIds = { "grass", "dirt_road", "asphalt", "wall_brick", "wall_plaster", "roof_tin", "roof_tile", "floor_wood", "pavement_alt", "metal_rust", "pavement", "school_wall", "floor_lino" };
    static string[] SurfaceSets => OwnSets.Concat(TableIds.Select(Src)).Distinct().ToArray();

    static MaterialTable _table;
    static MaterialTable Table => _table ?? (_table = JsonUtility.FromJson<MaterialTable>(File.ReadAllText(TablePath)));
    const string TablePath = "Assets/_Project/Data/Resources/GameData/materials.json";

    static MaterialEntry Entry(string tableId)
    {
        var entry = System.Array.Find(Table.materials, e => e.id == tableId);
        if (entry == null) throw new KeyNotFoundException("materials.json has no material " + tableId);
        return entry;
    }

    // The set a table row uses: its picked source once that is imported under Art/Materials, else the legacy ambientCG set.
    static string Src(string tableId)
    {
        var entry = Entry(tableId);
        return File.Exists($"{ArtMaterials}/{entry.source}/{entry.source}_color.jpg") ? entry.source : entry.fallback;
    }

    // The table's tint is a brightness match against the legacy set the pick replaces (the Poly Haven sets are darker),
    // multiplied into every material that uses the pick (one luminance factor in linear space, so the set keeps its own hue).
    static Color Match(string resolved)
    {
        var entry = System.Array.Find(Table.materials, e => e.source == resolved);
        return entry != null && entry.tint != null && entry.tint.Length == 3 ? new Color(entry.tint[0], entry.tint[1], entry.tint[2]) : Color.white;
    }

    // A square Poly Haven set replaces a 2:1 ambientCG one: keep the intended width, take the height from the set's shape.
    static float AspectOf(string resolved)
    {
        var entry = System.Array.Find(Table.materials, e => e.source == resolved);
        return entry != null && entry.tile != null && entry.tile.Length == 2 ? entry.tile[1] / entry.tile[0] : 0f;
    }

    static readonly Dictionary<string, float> MetalScale = new Dictionary<string, float>
    {
        { "CorrugatedSteel005", 0.15f }, { "rusty_corrugated_iron", 0.15f }, { "rusty_metal_sheet", 0.35f }, { "Metal022", 0.35f }, { "PaintedMetal014", 0.35f },
    };

    // (folder, file prefix) of the rock models.
    public static readonly (string Folder, string File)[] RockSets =
    {
        ("Rock07", "rock_07"), ("Rock09", "rock_09"), ("RockMossSet01", "rock_moss_set_01"),
    };

    // Five tints each (#207): sand, sky blue, ochre, sage and rose plaster; red, dark and weathered brick; green, rust and grey steel;
    // terracotta, brown, slate and moss roofs. A street no longer reads as one grey house repeated.
    static readonly Color[] BrickTints = { Color.white, new Color(0.84f, 0.80f, 0.78f), new Color(1f, 0.90f, 0.82f), new Color(0.74f, 0.66f, 0.62f), new Color(1f, 0.78f, 0.70f) };
    static readonly Color[] PlasterTints = { new Color(0.90f, 0.88f, 0.82f), new Color(0.76f, 0.81f, 0.84f), new Color(0.88f, 0.79f, 0.66f), new Color(0.74f, 0.82f, 0.72f), new Color(0.88f, 0.74f, 0.72f) };
    static readonly Color[] SteelTints = { new Color(1.0f, 1.0f, 1.0f), new Color(0.84f, 0.89f, 0.85f), new Color(0.95f, 0.85f, 0.78f), new Color(0.70f, 0.80f, 0.74f), new Color(0.78f, 0.82f, 0.90f) };
    static readonly Color[] RoofTints = { Color.white, new Color(0.80f, 0.74f, 0.70f), new Color(0.70f, 0.72f, 0.76f), new Color(1f, 0.72f, 0.60f), new Color(0.66f, 0.76f, 0.66f) };
    static readonly Color[] AccentTints =
    {
        new Color(0.62f, 0.22f, 0.20f), new Color(0.22f, 0.42f, 0.46f), new Color(0.78f, 0.60f, 0.22f), new Color(0.30f, 0.42f, 0.28f), new Color(0.26f, 0.32f, 0.52f),
    };

    // The scanned rocks are warm brown; pulled toward the grey of the cliffs they lie under.
    static readonly Color RockTint = new Color(0.82f, 0.88f, 0.92f);

    public const int Variants = 5;

    // Packs the masks every material needs. Call once before any material is asked for.
    public static void Prepare()
    {
        Directory.CreateDirectory(TextureDir);
        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(LayerDir);
        Cache.Clear();
        foreach (var id in SurfaceSets) PackSurfaceMask(id);
        foreach (var (folder, file) in RockSets) PackRockMask(folder, file);
        AssetDatabase.Refresh();
        foreach (var id in SurfaceSets) ImportTexture(MaskPath(id), true, false, TextureWrapMode.Repeat, true);
        foreach (var (_, file) in RockSets) ImportTexture(MaskPath(file), true, false, TextureWrapMode.Repeat, true);
    }

    static string MaskPath(string id) => $"{TextureDir}/{id}_mask.png";

    // One mask for both URP Lit (metallic R, smoothness A, occlusion G) and the terrain shader's mask map
    // (R metallic, G occlusion, B height, A smoothness): ambientCG ships these as separate maps.
    static void PackSurfaceMask(string id)
    {
        var outPath = MaskPath(id);
        if (File.Exists(outPath)) return;
        if (File.Exists($"{ArtMaterials}/{id}/{id}_arm.jpg")) { PackArm($"{ArtMaterials}/{id}/{id}_arm.jpg", outPath, MetalScale.TryGetValue(id, out float armScale) ? armScale : 1f); return; }
        var rough = LoadFile($"{ArtMaterials}/{id}/{id}_roughness.jpg");
        if (rough == null) throw new FileNotFoundException("roughness map missing for " + id);
        var ao = LoadFile($"{ArtMaterials}/{id}/{id}_ao.jpg");
        var metal = LoadFile($"{ArtMaterials}/{id}/{id}_metallic.jpg");
        var r = rough.GetPixels32();
        var a = SameSize(ao, rough) ? ao.GetPixels32() : null;
        var m = SameSize(metal, rough) ? metal.GetPixels32() : null;
        // Bare metal mirrors the dim overcast sky and comes out nearly black; cladding here is treated as coated sheet.
        float metalScale = MetalScale.TryGetValue(id, out float scale) ? scale : 1f;
        var packed = new Color32[r.Length];
        for (int i = 0; i < r.Length; i++)
            packed[i] = new Color32(m != null ? (byte)(m[i].r * metalScale) : (byte)0, a != null ? a[i].r : (byte)255, 128, (byte)(255 - r[i].r));
        WritePng(outPath, rough.width, rough.height, packed);
        Destroy(rough, ao, metal);
    }

    // Poly Haven packs AO, roughness and metalness into the R, G and B of one "arm" map.
    static void PackRockMask(string folder, string file)
    {
        var outPath = MaskPath(file);
        if (File.Exists(outPath)) return;
        PackArm($"{ArtModels}/{folder}/{file}_arm.jpg", outPath);
    }

    static void PackArm(string armPath, string outPath, float metalScale = 1f)
    {
        var arm = LoadFile(armPath);
        if (arm == null) throw new FileNotFoundException("arm map missing: " + armPath);
        var p = arm.GetPixels32();
        var packed = new Color32[p.Length];
        for (int i = 0; i < p.Length; i++)
            packed[i] = new Color32((byte)(p[i].b * metalScale), p[i].r, 128, (byte)(255 - p[i].g));
        WritePng(outPath, arm.width, arm.height, packed);
        Destroy(arm);
    }

    static bool SameSize(Texture2D tex, Texture2D like) => tex != null && tex.width == like.width && tex.height == like.height;

    static void Destroy(params Texture2D[] textures)
    {
        foreach (var tex in textures)
            if (tex != null) Object.DestroyImmediate(tex);
    }

    // Reads an image file straight from disk: the imported textures under Art/ are not readable.
    static Texture2D LoadFile(string path)
    {
        if (!File.Exists(path)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!tex.LoadImage(File.ReadAllBytes(path)))
        {
            Object.DestroyImmediate(tex);
            return null;
        }
        return tex;
    }

    public static void WritePng(string path, int width, int height, Color32[] pixels)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // AssetImportRules only covers Art/, so generated textures get their import settings here.
    public static Texture2D ImportTexture(string path, bool linear, bool normalMap, TextureWrapMode wrap, bool compressed)
    {
        AssetDatabase.ImportAsset(path);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new FileNotFoundException("generated texture was not imported: " + path);
        var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        var compression = compressed ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
        bool dirty = false;
        if (importer.textureType != type) { importer.textureType = type; dirty = true; }
        if (!normalMap && importer.sRGBTexture == linear) { importer.sRGBTexture = !linear; dirty = true; }
        if (importer.wrapMode != wrap) { importer.wrapMode = wrap; dirty = true; }
        if (importer.textureCompression != compression) { importer.textureCompression = compression; dirty = true; }
        if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; dirty = true; }
        if (importer.isReadable) { importer.isReadable = false; dirty = true; }
        if (dirty) importer.SaveAndReimport();
        return SceneLook.Load<Texture2D>(path);
    }

    static Material Asset(string name, string shaderName)
    {
        var path = $"{MaterialDir}/{name}.mat";
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new FileNotFoundException("shader not found: " + shaderName);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader) mat.shader = shader;
        return mat;
    }

    // URP Lit from a colour, normal and packed mask map; the same recipe as SceneLook's test box materials.
    static Material Lit(string name, string colorPath, string normalPath, string maskPath, Color tint, float smoothness, float bumpScale)
    {
        if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = Asset(name, "Universal Render Pipeline/Lit");
        var mask = SceneLook.Load<Texture2D>(maskPath);
        mat.SetTexture("_BaseMap", SceneLook.Load<Texture2D>(colorPath));
        mat.SetTexture("_BumpMap", SceneLook.Load<Texture2D>(normalPath));
        mat.SetTexture("_MetallicGlossMap", mask);
        mat.SetTexture("_OcclusionMap", mask);
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_BumpScale", bumpScale);
        mat.SetFloat("_OcclusionStrength", 1f);
        mat.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(mat);
        Cache[name] = mat;
        return mat;
    }

    static Surface Set(string name, string id, Color tint, float tileX, float tileY, float smoothness = 1f, float bumpScale = 1f) => new Surface
    {
        Material = Lit("World_" + name, $"{ArtMaterials}/{id}/{id}_color.jpg", $"{ArtMaterials}/{id}/{id}_normal.jpg", MaskPath(id), tint * Match(id), smoothness, bumpScale),
        Tile = new Vector2(tileX, AspectOf(id) > 0f ? tileX * AspectOf(id) : tileY),
    };

    static int Wrap(int variant) => ((variant % Variants) + Variants) % Variants;

    // Tinted variants so a street is not one colour. Bricks, plaster and planks are 2:1 sets.
    public static Surface Brick(int variant) => Set("Brick" + Wrap(variant), Src("wall_brick"), BrickTints[Wrap(variant)], 2.4f, 1.2f);
    public static Surface Plaster(int variant) => Set("Plaster" + Wrap(variant), "PaintedPlaster017", PlasterTints[Wrap(variant)], 3f, 1.5f);
    // Interior walls and the plastered ceilings under floors: plain light paint, no texture. A scanned plaster map shows its
    // stains as black blotches on a ceiling lit only by ambient (S1 captures of the 3-storey blocks).
    public static Surface PlasterInner
    {
        get
        {
            var mat = Asset("World_PlasterInner", "Universal Render Pipeline/Lit");
            foreach (var slot in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" }) mat.SetTexture(slot, null);
            foreach (var keyword in new[] { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP" }) mat.DisableKeyword(keyword);
            mat.SetColor("_BaseColor", new Color(0.84f, 0.82f, 0.77f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.1f);
            // A ceiling faces the ground's dark ambient and only the lamps' small spots reach it: a faint self-glow keeps it a light paint.
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.22f, 0.21f, 0.19f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);
            return new Surface { Material = mat, Tile = new Vector2(3f, 1.5f) };
        }
    }
    public static Surface Steel(int variant) => Set("Steel" + Wrap(variant), Src("roof_tin"), SteelTints[Wrap(variant)], 2f, 2f, smoothness: 0.5f);
    public static Surface SteelRoof => Set("SteelRoof", Src("roof_tin"), new Color(0.80f, 0.82f, 0.84f), 2f, 2f, smoothness: 0.5f);
    public static Surface RoofTiles(int variant) => Set("RoofTiles" + Wrap(variant), Src("roof_tile"), RoofTints[Wrap(variant)], 2f, 2f);
    // Town detail (#207): painted accents for shutters, awnings, signs and mailboxes, trim around openings, fences, wires, bins.
    public static Surface Accent(int variant) => Set("Accent" + Wrap(variant), "PaintedMetal014", AccentTints[Wrap(variant)], 1.5f, 1.5f, smoothness: 0.5f);
    public static Surface Trim(bool light) => light
        ? Set("TrimLight", Src("wall_plaster"), new Color(0.90f, 0.89f, 0.85f), 3f, 1.5f)
        : Set("TrimDark", Src("floor_wood"), new Color(0.42f, 0.32f, 0.24f), 1.2f, 0.6f);
    public static Surface FenceWhite => Set("FenceWhite", Src("floor_wood"), new Color(0.88f, 0.85f, 0.78f), 1.4f, 0.7f);
    public static Surface Wire => Set("Wire", "PaintedMetal014", new Color(0.07f, 0.07f, 0.08f), 1f, 1f, smoothness: 0.3f);
    public static Surface BinGreen => Set("BinGreen", "PaintedMetal014", new Color(0.20f, 0.40f, 0.30f), 1f, 1f, smoothness: 0.4f);
    public static Surface Planks => Set("Planks", Src("floor_wood"), Color.white, 2.4f, 1.2f);
    public static Surface PlanksDark => Set("PlanksDark", Src("floor_wood"), new Color(0.72f, 0.64f, 0.55f), 1.6f, 0.8f);
    public static Surface Shelving => Set("Shelving", Src("floor_wood"), new Color(0.62f, 0.60f, 0.56f), 1.2f, 0.6f);
    public static Surface Crate => Set("Crate", Src("floor_wood"), new Color(0.92f, 0.86f, 0.74f), 1.2f, 0.6f);
    public static Surface Bedding => Set("Bedding", Src("wall_plaster"), new Color(0.50f, 0.56f, 0.62f), 3f, 1.5f, smoothness: 0.4f, bumpScale: 0.5f);
    // Furniture parts (ADR-147): the same texture sets as the rest of the world, tinted so a room is not one colour.
    public static Surface Decor(DecorMat mat)
    {
        switch (mat)
        {
            case DecorMat.Wood: return Set("DecorWood", Src("floor_wood"), new Color(0.78f, 0.62f, 0.46f), 0.9f, 0.45f);
            case DecorMat.WoodDark: return Set("DecorWoodDark", Src("floor_wood"), new Color(0.50f, 0.36f, 0.26f), 0.9f, 0.45f);
            case DecorMat.Fabric: return Set("DecorFabric", Src("wall_plaster"), new Color(0.36f, 0.45f, 0.55f), 1.2f, 0.6f, smoothness: 0.2f, bumpScale: 0.4f);
            case DecorMat.Fabric2: return Set("DecorFabric2", Src("wall_plaster"), new Color(0.62f, 0.30f, 0.26f), 1.2f, 0.6f, smoothness: 0.2f, bumpScale: 0.4f);
            case DecorMat.Metal: return Set("DecorMetal", Src("metal_rust"), new Color(0.62f, 0.64f, 0.66f), 0.8f, 0.8f, smoothness: 0.85f);
            case DecorMat.Paint: return Set("DecorPaint", "PaintedMetal014", new Color(0.42f, 0.55f, 0.50f), 1f, 1f);
            case DecorMat.Warn: return Set("DecorWarn", "PaintedMetal014", new Color(0.95f, 0.72f, 0.12f), 1f, 1f);
            case DecorMat.Ceramic: return Set("DecorCeramic", Src("wall_plaster"), new Color(0.93f, 0.93f, 0.90f), 2f, 1f, smoothness: 0.9f, bumpScale: 0.2f);
            case DecorMat.Screen: return Set("DecorScreen", "PaintedMetal014", new Color(0.05f, 0.055f, 0.06f), 1f, 1f, smoothness: 1f, bumpScale: 0f);
            case DecorMat.Crate: return Set("DecorCrate", Src("floor_wood"), new Color(0.88f, 0.76f, 0.58f), 0.8f, 0.4f);
            default:
                {
                    var lamp = Set("DecorLamp", Src("wall_plaster"), new Color(1f, 0.93f, 0.8f), 1f, 1f);
                    lamp.Material.EnableKeyword("_EMISSION");
                    lamp.Material.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.5f) * 2.4f);
                    lamp.Material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    EditorUtility.SetDirty(lamp.Material);
                    return lamp;
                }
        }
    }

    // Table picks the map track's buildings and sidewalks take (ADR-184): public pavement, school walls, indoor linoleum.
    public static Surface Pavement => Set("Pavement", Src("pavement"), Color.white, 1.8f, 1.8f);
    public static Surface SchoolWall => Set("SchoolWall", Src("school_wall"), Color.white, 3f, 3f);
    public static Surface FloorLino => Set("FloorLino", Src("floor_lino"), Color.white, 2f, 2f);
    public static Surface Concrete => Set("Concrete", Src("pavement_alt"), Color.white, 3f, 3f);
    public static Surface ConcreteDark => Set("ConcreteDark", Src("pavement_alt"), new Color(0.74f, 0.74f, 0.72f), 3f, 3f);
    // Elevator shaft walls (ADR-065 (1)): the same concrete, much darker, so the shaft reads as underground under
    // the sky's ambient light.
    public static Surface ShaftConcrete => Set("ShaftConcrete", Src("pavement_alt"), new Color(0.40f, 0.40f, 0.39f), 2f, 2f);
    public static Surface PaintedMetal => Set("PaintedMetal", "PaintedMetal014", new Color(0.60f, 0.66f, 0.68f), 1.5f, 1.5f);
    public static Surface RustyMetal => Set("RustyMetal", Src("metal_rust"), new Color(0.55f, 0.60f, 0.62f), 1.5f, 1.5f, smoothness: 0.8f);
    public static Surface Asphalt => Set("Asphalt", Src("asphalt"), new Color(0.82f, 0.82f, 0.82f), 3f, 3f);
    public static Surface DirtRoad => Set("DirtRoad", Src("dirt_road"), new Color(0.88f, 0.86f, 0.82f), 5f, 2.5f);

    // Road strips (Game/WorldRoad): ragged alpha edge, wear and paint on top of the same scanned sets as the old Lit roads.
    public static Material Road(bool paved)
    {
        var mat = Asset(paved ? "World_RoadPaved" : "World_RoadDirt", "Game/WorldRoad");
        var surface = paved ? Asphalt : DirtRoad;
        var id = Src(paved ? "asphalt" : "dirt_road");
        mat.SetTexture("_BaseMap", SceneLook.Load<Texture2D>($"{ArtMaterials}/{id}/{id}_color.jpg"));
        mat.SetTexture("_BumpMap", SceneLook.Load<Texture2D>($"{ArtMaterials}/{id}/{id}_normal.jpg"));
        mat.SetTexture("_MaskMap", SceneLook.Load<Texture2D>(MaskPath(id)));
        // Dirt matches the terrain's own dirt layer (red earth, ADR-284) so a track does not glow against its verge.
        mat.SetColor("_BaseColor", paved ? surface.Material.GetColor("_BaseColor") : new Color(0.86f, 0.64f, 0.52f) * Match(id));
        mat.SetFloat("_Smoothness", paved ? 0.55f : 0.25f);
        mat.SetFloat("_Paved", paved ? 1f : 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 100;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static Material Rock(int set)
    {
        var (folder, file) = RockSets[set];
        var dir = $"{ArtModels}/{folder}/{file}";
        return Lit("World_" + folder, dir + "_color.jpg", dir + "_normal.jpg", MaskPath(file), RockTint, 0.5f, 1f);
    }

    public static string RockModelPath(int set) => $"{ArtModels}/{RockSets[set].Folder}/{RockSets[set].File}.fbx";

    // ---- terrain ----

    // Order matches WorldPlan.Splat: grass, dirt/gravel, rock, sand/mud. Tile sizes are large enough that the
    // repeat does not show from eye height (Ground081 is a 2:1 set).
    const float RockTile = 6f; // the same as ROCK_TILE in WorldTerrainPasses.hlsl

    public static TerrainLayer[] TerrainLayers() => new[]
    {
        Layer("Grass", Src("grass"), new Vector2(7f, 7f), 0.12f, new Color(0.46f, 0.58f, 0.31f)),   // olive base (x Match);  WorldTerrainPasses tints it toward dry yellow by area (ADR-284)
        Layer("Dirt", Src("dirt_road"), new Vector2(4.5f, 2.25f), 0.15f, new Color(0.60f, 0.37f, 0.30f)),   // red earth (ADR-284)
        Layer("Rock", "Rock051", new Vector2(RockTile, RockTile), 0.2f, new Color(0.50f, 0.50f, 0.49f)),
        Layer("Mud", "Ground054", new Vector2(8.6f, 8.6f), 0.3f, new Color(0.30f, 0.30f, 0.26f)),
    };

    static TerrainLayer Layer(string name, string id, Vector2 tileSize, float fallbackSmoothness, Color tint)
    {
        var path = $"{LayerDir}/World_{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = SceneLook.Load<Texture2D>($"{ArtMaterials}/{id}/{id}_color.jpg");
        layer.normalMapTexture = SceneLook.Load<Texture2D>($"{ArtMaterials}/{id}/{id}_normal.jpg");
        layer.maskMapTexture = null;
        var aspect = AspectOf(id);
        layer.tileSize = aspect > 0f ? new Vector2(tileSize.x, tileSize.x * aspect) : tileSize;
        layer.tileOffset = Vector2.zero;
        layer.normalScale = 1f;
        layer.diffuseRemapMin = new Vector4(0f, 0f, 0f, 0f);
        var matched = tint * Match(id);
        layer.diffuseRemapMax = new Vector4(matched.r, matched.g, matched.b, 1f);
        // Ground is never glossy: the scanned roughness reads as wet under the fill light.
        layer.maskMapRemapMin = new Vector4(0f, 0f, 0f, 0f);
        layer.maskMapRemapMax = new Vector4(0f, 1f, 1f, 0.45f);
        // Used only if the mask map is not read (the shader's _MASKMAP keyword off).
        layer.metallic = 0f;
        layer.smoothness = fallbackSmoothness * 0.5f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    // Our own copy of the URP terrain material so the per-pixel normal and mask map keywords are certain to be on
    // (they are shader features: the inspector normally sets them).
    // The sliding elevator door: plain concrete-grey URP Lit, no texture.
    public static Material Door()
    {
        var mat = Asset("World_ElevatorDoor", "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", new Color(0.42f, 0.44f, 0.46f));
        mat.SetFloat("_Metallic", 0.3f);
        mat.SetFloat("_Smoothness", 0.35f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static Material DoorStripe()
    {
        var mat = Asset("World_ElevatorDoorStripe", "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", new Color(0.86f, 0.58f, 0.12f));
        mat.SetFloat("_Smoothness", 0.3f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static Material DoorSeam()
    {
        var mat = Asset("World_ElevatorDoorSeam", "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f));
        mat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Lamps in the elevator shaft and the car ceiling: warm white, emissive (the post volume's bloom gives the glow;
    // the real light comes from a few point lights the builder places).
    public static Material ShaftLamp()
    {
        var mat = Asset("World_ShaftLamp", "Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.78f));
        mat.SetFloat("_Smoothness", 0.6f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1f, 0.82f, 0.58f) * 3f);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static Material Terrain()
    {
        var mat = Asset("World_Terrain", TerrainShader);
        mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
        mat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
        // No mask maps: four more samples on every pixel of ground for occlusion nobody sees (the layers' own
        // smoothness and metallic values are used instead).
        mat.DisableKeyword("_MASKMAP");
        mat.SetFloat("_EnableHeightBlend", 0f);
        mat.DisableKeyword("_TERRAIN_BLEND_HEIGHT");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---- water ----

    public static Material Water(Texture2D normals, Texture2D depth, float mapSize, float depthRange)
    {
        // A saved material keeps its own copy of every value; made afresh so the shader's defaults are the look.
        AssetDatabase.DeleteAsset($"{MaterialDir}/World_Water.mat");
        var mat = Asset("World_Water", WaterShader);
        mat.SetTexture("_NormalMap", normals);
        mat.SetTexture("_DepthMap", depth);
        mat.SetFloat("_MapSize", mapSize);
        mat.SetFloat("_DepthRange", depthRange);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Tileable wave normals made here from summed sine waves with whole-number frequencies (so the texture wraps):
    // no downloaded water texture exists in CC0 that we trust (ADR-052).
    public static Texture2D WaterNormals(int seed)
    {
        const int size = 512;
        const int waves = 28;
        var path = TextureDir + "/WaterNormals.png";
        var rng = new System.Random(seed);
        var kx = new float[waves];
        var ky = new float[waves];
        var amp = new float[waves];
        var phase = new float[waves];
        for (int w = 0; w < waves; w++)
        {
            // Frequencies from 2 to 24 cycles across the tile, in every direction; higher ones are weaker.
            float freq = 2f + 22f * (float)rng.NextDouble();
            float angle = (float)(rng.NextDouble() * System.Math.PI * 2.0);
            int fx = Mathf.RoundToInt(freq * Mathf.Cos(angle)), fy = Mathf.RoundToInt(freq * Mathf.Sin(angle));
            if (fx == 0 && fy == 0) fx = 3;
            kx[w] = fx * 2f * Mathf.PI;
            ky[w] = fy * 2f * Mathf.PI;
            amp[w] = 1f / Mathf.Sqrt(fx * fx + fy * fy);
            phase[w] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
        }

        // The summed slopes come to about 20 at their steepest; this keeps the steepest waves near 25 degrees
        // before the shader's own strength.
        const float slopeScale = 0.02f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float dx = 0f, dy = 0f;
                for (int w = 0; w < waves; w++)
                {
                    float c = Mathf.Cos(kx[w] * u + ky[w] * v + phase[w]) * amp[w];
                    dx += c * kx[w];
                    dy += c * ky[w];
                }
                var n = new Vector3(-dx * slopeScale, -dy * slopeScale, 1f).normalized;
                pixels[y * size + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 0, 255), 255);
            }
        }
        WritePng(path, size, size, pixels);
        return ImportTexture(path, true, true, TextureWrapMode.Repeat, true);
    }

    // Water depth over the whole map for the water shader: R = depth / depthRange, clamped. Not compressed:
    // block compression would band the shoreline fade.
    public static Texture2D WaterDepth(Color32[] pixels, int size)
    {
        var path = TextureDir + "/WaterDepth.png";
        WritePng(path, size, size, pixels);
        return ImportTexture(path, true, false, TextureWrapMode.Clamp, false);
    }
}

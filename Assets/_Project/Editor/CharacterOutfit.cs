using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// S3 outfit baker: rasterises per-triangle body regions (from each vertex's dominant Humanoid bone) into UV space,
// then paints a recoloured 1024 albedo per variant, so skin stays skin and clothes change.
//  - UBC (bare mannequin): top/bottom get a fabric colour, hands gloves, feet boots, head/neck keep the skin texture.
//  - Mixamo civilians (skin and clothes share one atlas): only clothing regions change; inside them skin-like pixels
//    are kept and the rest is hue-shifted (low-saturation cloth is tinted instead).
public static class CharacterOutfit
{
    public enum Region : byte { Skin, Hands, Top, Bottom, Feet, None = 255 }
    public const int Size = 1024;
    public const string TexDir = CharacterRoster.PrefabDir + "/Textures";

    public static readonly (string name, Color top, Color bottom)[] UbcOutfits =
    {
        ("Olive", new Color(0.30f, 0.34f, 0.22f), new Color(0.24f, 0.26f, 0.18f)),
        ("Charcoal", new Color(0.20f, 0.21f, 0.23f), new Color(0.13f, 0.14f, 0.15f)),
        ("Navy", new Color(0.14f, 0.20f, 0.32f), new Color(0.10f, 0.12f, 0.18f)),
        ("Coyote", new Color(0.48f, 0.40f, 0.28f), new Color(0.34f, 0.30f, 0.22f)),
    };
    public static readonly (string name, float hue, float sat)[] CivilianShifts = { ("Moss", 0.30f, 1f), ("Dusk", 0.78f, 0.9f), ("Rust", 0.04f, 1.1f) };

    static Region RegionOf(HumanBodyBones b)
    {
        if (b == HumanBodyBones.LeftHand || b == HumanBodyBones.RightHand || (b >= HumanBodyBones.LeftThumbProximal && b <= HumanBodyBones.RightLittleDistal)) return Region.Hands;
        switch (b)
        {
            case HumanBodyBones.Head: case HumanBodyBones.Neck: case HumanBodyBones.Jaw: case HumanBodyBones.LeftEye: case HumanBodyBones.RightEye: return Region.Skin;
            case HumanBodyBones.LeftFoot: case HumanBodyBones.RightFoot: case HumanBodyBones.LeftToes: case HumanBodyBones.RightToes: return Region.Feet;
            case HumanBodyBones.Hips: case HumanBodyBones.LeftUpperLeg: case HumanBodyBones.RightUpperLeg: case HumanBodyBones.LeftLowerLeg: case HumanBodyBones.RightLowerLeg: return Region.Bottom;
            default: return Region.Top;
        }
    }

    // Region of each vertex of `r`'s mesh (dominant bone, walking up to the nearest Humanoid bone).
    public static Region[] VertexRegions(SkinnedMeshRenderer r, Animator anim, out HumanBodyBones[] bones)
    {
        var map = new Dictionary<Transform, HumanBodyBones>();
        for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++) { var t = anim.GetBoneTransform(b); if (t != null) map[t] = b; }
        var m = r.sharedMesh; var w = m.boneWeights; var rb = r.bones;
        var res = new Region[m.vertexCount]; bones = new HumanBodyBones[m.vertexCount];
        for (int v = 0; v < res.Length; v++)
        {
            var t = rb[w[v].boneIndex0]; HumanBodyBones hb = HumanBodyBones.Hips;
            while (t != null && !map.TryGetValue(t, out hb)) t = t.parent;
            bones[v] = hb; res[v] = RegionOf(hb);
        }
        return res;
    }

    static void Raster(byte[] map, SkinnedMeshRenderer r, int submesh, Region[] vr)
    {
        var uv = r.sharedMesh.uv; var tris = r.sharedMesh.GetTriangles(submesh);
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            var cnt = new int[8]; cnt[(int)vr[a]]++; cnt[(int)vr[b]]++; cnt[(int)vr[c]]++;
            byte reg = (byte)System.Array.IndexOf(cnt, cnt.Max());
            Vector2 pa = uv[a] * Size, pb = uv[b] * Size, pc = uv[c] * Size;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x, pc.x))), x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x, pc.x)));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y, pc.y))), y1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y, pc.y)));
            float den = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
            if (Mathf.Abs(den) < 1e-6f) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float l1 = ((pb.y - pc.y) * (px - pc.x) + (pc.x - pb.x) * (py - pc.y)) / den;
                    float l2 = ((pc.y - pa.y) * (px - pc.x) + (pa.x - pc.x) * (py - pc.y)) / den;
                    float l3 = 1 - l1 - l2;
                    const float e = -0.02f; // slight overdraw so island edges have no gaps
                    if (l1 >= e && l2 >= e && l3 >= e) map[y * Size + x] = reg;
                }
        }
    }

    static byte[] Dilate(byte[] map)
    {
        for (int pass = 0; pass < 4; pass++) // dilate into the gutter
        {
            var next = (byte[])map.Clone();
            for (int y = 1; y < Size - 1; y++)
                for (int x = 1; x < Size - 1; x++)
                {
                    int i = y * Size + x; if (map[i] != 255) continue;
                    foreach (var n in new[] { i - 1, i + 1, i - Size, i + Size }) if (map[n] != 255) { next[i] = map[n]; break; }
                }
            map = next;
        }
        return map;
    }

    static Color[] Load(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = new Color[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) px[y * Size + x] = tex.GetPixelBilinear((x + 0.5f) / Size, (y + 0.5f) / Size);
        Object.DestroyImmediate(tex);
        return px;
    }

    static Texture2D Save(Color[] px, string name, bool srgb = true)
    {
        Directory.CreateDirectory(TexDir);
        var path = $"{TexDir}/{name}.png";
        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti.maxTextureSize != Size) { ti.maxTextureSize = Size; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Returns variant name -> baked albedo for the model's body material; also fills the stats line.
    public static Dictionary<string, Texture2D> Bake(CharacterRoster.Entry e, IList<(SkinnedMeshRenderer r, int sm)> parts, Animator anim, string srcPath, string srcPathDark, out string stats)
    {
        var map = Enumerable.Repeat((byte)Region.None, Size * Size).ToArray();
        foreach (var (r, sm) in parts) Raster(map, r, sm, VertexRegions(r, anim, out _));
        map = Dilate(map);
        var counts = new int[6]; foreach (var b in map) if (b < 5) counts[b]++;
        stats = $"{e.Id}: UV regions skin {counts[0] * 100f / map.Length:F1}% hands {counts[1] * 100f / map.Length:F1}% top {counts[2] * 100f / map.Length:F1}% bottom {counts[3] * 100f / map.Length:F1}% feet {counts[4] * 100f / map.Length:F1}% (of 1024^2 texels)";
        var result = new Dictionary<string, Texture2D>();
        if (!e.Mixamo)
        {
            foreach (var skin in new[] { ("Light", srcPath), ("Dark", srcPathDark) })
            {
                var src = Load(skin.Item2);
                for (int o = 0; o < UbcOutfits.Length; o++)
                {
                    if (skin.Item1 == "Dark" && o >= 4) continue;
                    var px = new Color[src.Length]; var (name, top, bot) = UbcOutfits[o];
                    for (int i = 0; i < px.Length; i++)
                    {
                        var p = src[i]; float lum = p.grayscale; float shade = Mathf.Lerp(0.72f, 1.18f, Mathf.Clamp01(lum * 1.4f));
                        switch ((Region)map[i])
                        {
                            case Region.Top: px[i] = top * shade; break;
                            case Region.Bottom: px[i] = bot * shade; break;
                            case Region.Hands: px[i] = new Color(0.10f, 0.10f, 0.11f) * shade; break; // gloves
                            case Region.Feet: px[i] = new Color(0.07f, 0.06f, 0.05f) * shade; break;  // boots
                            default: px[i] = p; break;
                        }
                        px[i].a = 1;
                    }
                    var key = $"{skin.Item1}_{name}";
                    result[key] = Save(px, $"{e.Id}_{key}");
                }
            }
        }
        else
        {
            var src = Load(srcPath); int changed = 0;
            foreach (var (name, hue, sat) in CivilianShifts)
            {
                var px = (Color[])src.Clone();
                for (int i = 0; i < px.Length; i++)
                {
                    var reg = (Region)map[i];
                    if (reg == Region.Skin || reg == Region.Hands || reg == Region.None) continue;
                    Color.RGBToHSV(src[i], out float h, out float s, out float v);
                    bool skinLike = h < 0.14f && s > 0.15f && s < 0.75f && v > 0.25f;
                    if (skinLike) continue;
                    px[i] = s < 0.2f ? Color.Lerp(src[i], Color.HSVToRGB(hue, 0.45f, Mathf.Clamp01(v + 0.1f)), 0.6f) : Color.HSVToRGB((h + hue) % 1f, Mathf.Clamp01(s * sat), v);
                    changed++;
                }
                result[name] = Save(px, $"{e.Id}_{name}");
                stats += $" | {name}: {changed * 100f / px.Length / 1f:F1}% texels recoloured";
                changed = 0;
            }
        }
        return result;
    }
}

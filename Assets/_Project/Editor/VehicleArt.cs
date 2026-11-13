using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Textures the vehicle models need besides their own (#89), all drawn here in code (no AI art):
// - an original Korean-style number plate (white, black border, "12가 3456" in plain strokes), replacing the models'
//   US plate sheets (state names and "DMV" are real-world marks);
// - the edited Goblin albedo: the "NEW YORK / EMPIRE STATE" plate and the author's web address painted over with
//   that plate, and the model's opacity map moved into the alpha channel (cut-out grilles).
// The Goblin source sheet is read from the gitignored AssetStaging folder; the edited copy is what the project keeps.
public static class VehicleArt
{
    public static string PlatePath => VehicleModels.OutDir + "/Plate_KR.png";
    public const string GoblinAlbedoPath = VehicleModels.ArtDir + "/GoblinV12/Goblin2017_albedo_edit.png";
    const string GoblinStaging = "AssetStaging/vehicles/fictional-supercar-v12-goblin/textures/";
    const int PlateWidth = 512, PlateHeight = 256;

    // Plate sheet of the Goblin albedo in pixels from the top-left (4096 x 2048 sheet): the whole frame with the
    // address under it, and the plate face inside.
    static readonly RectInt GoblinFrame = new RectInt(616, 1552, 322, 206);
    static readonly RectInt GoblinFace = new RectInt(636, 1586, 282, 122);

    public static Texture2D Plate()
    {
        if (!File.Exists(PlatePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlatePath));
            var tex = DrawPlate(PlateWidth, PlateHeight);
            File.WriteAllBytes(PlatePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PlatePath);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(PlatePath);
    }

    // White face, black border, "12가 3456". Strokes on a grid: each glyph is a list of segments in a 4 x 7 box.
    public static Texture2D DrawPlate(int w, int h)
    {
        var px = new Color32[w * h];
        var white = new Color32(236, 236, 230, 255);
        var black = new Color32(18, 18, 20, 255);
        int border = Mathf.Max(3, h / 28);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x < border || y < border || x >= w - border || y >= h - border;
                px[y * w + x] = edge ? black : white;
            }
        // Text: 8 glyphs (a gap counts as half a glyph) centred.
        const string text = "12가 3456";
        float glyphH = h * 0.56f, glyphW = glyphH * 0.52f, gap = glyphW * 0.32f, stroke = glyphH * 0.11f;
        float total = 0f;
        foreach (char c in text) total += (c == ' ' ? glyphW * 0.5f : glyphW) + gap;
        total -= gap;
        float cx = (w - total) * 0.5f, baseY = (h - glyphH) * 0.5f;
        foreach (char c in text)
        {
            if (c == ' ') { cx += glyphW * 0.5f + gap; continue; }
            foreach (var seg in Glyph(c))
            {
                var a = new Vector2(cx + seg.x / 4f * glyphW, baseY + seg.y / 7f * glyphH);
                var b = new Vector2(cx + seg.z / 4f * glyphW, baseY + seg.w / 7f * glyphH);
                Stroke(px, w, h, a, b, stroke * 0.5f, black);
            }
            cx += glyphW + gap;
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // Segments (x0, y0, x1, y1) on a 4 x 7 box, y up.
    static IEnumerable<Vector4> Glyph(char c)
    {
        Vector4 S(float x0, float y0, float x1, float y1) => new Vector4(x0, y0, x1, y1);
        switch (c)
        {
            case '0': return new[] { S(0, 0, 4, 0), S(4, 0, 4, 7), S(4, 7, 0, 7), S(0, 7, 0, 0) };
            case '1': return new[] { S(2.4f, 0, 2.4f, 7), S(2.4f, 7, 1, 5.8f), S(1, 0, 4, 0) };
            case '2': return new[] { S(0, 7, 4, 7), S(4, 7, 4, 3.5f), S(4, 3.5f, 0, 3.5f), S(0, 3.5f, 0, 0), S(0, 0, 4, 0) };
            case '3': return new[] { S(0, 7, 4, 7), S(4, 7, 4, 0), S(4, 0, 0, 0), S(1, 3.5f, 4, 3.5f) };
            case '4': return new[] { S(0, 7, 0, 3), S(0, 3, 4, 3), S(3, 7, 3, 0) };
            case '5': return new[] { S(4, 7, 0, 7), S(0, 7, 0, 3.7f), S(0, 3.7f, 4, 3.7f), S(4, 3.7f, 4, 0), S(4, 0, 0, 0) };
            case '6': return new[] { S(4, 7, 0, 7), S(0, 7, 0, 0), S(0, 0, 4, 0), S(4, 0, 4, 3.5f), S(4, 3.5f, 0, 3.5f) };
            case '7': return new[] { S(0, 7, 4, 7), S(4, 7, 1.5f, 0) };
            case '8': return new[] { S(0, 0, 4, 0), S(4, 0, 4, 7), S(4, 7, 0, 7), S(0, 7, 0, 0), S(0, 3.5f, 4, 3.5f) };
            case '9': return new[] { S(4, 3.5f, 0, 3.5f), S(0, 3.5f, 0, 7), S(0, 7, 4, 7), S(4, 7, 4, 0), S(4, 0, 0, 0) };
            // 가: ㄱ (a top stroke turning down) and ㅏ (an upright with a short stroke out to the right).
            case '가': return new[] { S(0, 6, 2.1f, 6), S(2.1f, 6, 1.0f, 0.8f), S(3.1f, 7, 3.1f, 0), S(3.1f, 3.6f, 4.2f, 3.6f) };
            default: return new Vector4[0];
        }
    }

    // A round-capped line of radius `r` (anti-aliased by one pixel).
    static void Stroke(Color32[] px, int w, int h, Vector2 a, Vector2 b, float r, Color32 ink)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - r - 2)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + r + 2));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - r - 2)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + r + 2));
        var ab = b - a;
        float len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                float d = Vector2.Distance(p, a + ab * t);
                float cover = Mathf.Clamp01(r + 0.5f - d);
                if (cover <= 0f) continue;
                int i = y * w + x;
                px[i] = Color32.Lerp(px[i], ink, cover);
            }
    }

    // The edited Goblin albedo (made once from the staging sheet; afterwards the kept copy is used).
    public static Texture2D GoblinAlbedo(bool remake = false)
    {
        if (remake || !File.Exists(GoblinAlbedoPath))
        {
            var albedoFile = GoblinStaging + "Goblin2017_albedo.png";
            var opacityFile = GoblinStaging + "Goblin2017_opacity.png";
            if (!File.Exists(albedoFile)) throw new FileNotFoundException("Goblin source sheet missing (AssetStaging is not in the repository)", albedoFile);
            var albedo = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            albedo.LoadImage(File.ReadAllBytes(albedoFile));
            var opacity = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (File.Exists(opacityFile)) opacity.LoadImage(File.ReadAllBytes(opacityFile));
            int w = albedo.width, h = albedo.height;
            var px = albedo.GetPixels32();
            if (opacity.width == w && opacity.height == h)
            {
                var op = opacity.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i].a = op[i].r;
            }
            // The frame and address: plain dark; then the plate face.
            var frame = new Color32(14, 14, 15, 255);
            Fill(px, w, h, GoblinFrame, (x, y) => frame);
            var plate = DrawPlate(GoblinFace.width * 2, GoblinFace.height * 2);
            Fill(px, w, h, GoblinFace, (x, y) => plate.GetPixelBilinear((x + 0.5f) / GoblinFace.width, 1f - (y + 0.5f) / GoblinFace.height));
            Object.DestroyImmediate(plate);
            albedo.SetPixels32(px);
            albedo.Apply();
            File.WriteAllBytes(GoblinAlbedoPath, albedo.EncodeToPNG());
            Object.DestroyImmediate(albedo);
            Object.DestroyImmediate(opacity);
            AssetDatabase.ImportAsset(GoblinAlbedoPath);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GoblinAlbedoPath);
    }

    // `rect` in pixels from the top-left; `color(x, y)` gets the position inside it from its top-left.
    static void Fill(Color32[] px, int w, int h, RectInt rect, System.Func<int, int, Color> color)
    {
        for (int y = 0; y < rect.height; y++)
            for (int x = 0; x < rect.width; x++)
            {
                int ix = rect.x + x, iyTop = rect.y + y;
                if (ix < 0 || ix >= w || iyTop < 0 || iyTop >= h) continue;
                int i = (h - 1 - iyTop) * w + ix; // texture rows run bottom to top
                var c = (Color32)color(x, y);
                c.a = 255;
                px[i] = c;
            }
    }
}

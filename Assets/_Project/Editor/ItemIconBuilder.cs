using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Generates the inventory item icons into Assets/_Project/Resources/Icons/<key>.png (ADR-054):
//  - weapons: the project's CC0 weapon models rendered from the side onto a transparent background
//  - ammo, medkit, bandage, first-aid kit, armour, grenade, smoke, optics: flat glyphs drawn from signed-distance shapes, supersampled
// Run: tools/unity.sh run-gfx ItemIconBuilder.Build   (exits the editor with 0 on success, 1 on failure)
public static class ItemIconBuilder
{
    public const string OutDir = "Assets/_Project/Resources/Icons";
    const int WeaponW = 256, WeaponH = 128, WeaponSuper = 2;
    const int GlyphSize = 256, GlyphSamples = 4; // 4x4 samples per pixel

    public static void Build()
    {
        int code = 0;
        try
        {
            Directory.CreateDirectory(OutDir);
            BuildWeapons();
            BuildGlyphs();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[ItemIconBuilder] done: " + OutDir);
        }
        catch (Exception e)
        {
            Debug.LogError("[ItemIconBuilder] failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    // ---------------------------------------------------------------- weapons

    static void BuildWeapons()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var oldAmbientMode = RenderSettings.ambientMode;
        var oldAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.46f);

        var camGo = new GameObject("IconCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 20f;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.aspect = WeaponW / (float)WeaponH;

        var key = MakeLight("Key", new Vector3(-0.55f, -0.75f, 0.35f), new Color(1f, 0.96f, 0.9f), 1.15f);
        var fill = MakeLight("Fill", new Vector3(-0.6f, -0.15f, -0.7f), new Color(0.75f, 0.85f, 1f), 0.55f);

        try
        {
            foreach (var weapon in CharacterKit.Weapons)
            {
                var go = CharacterKit.InstantiateWeapon(weapon);
                go.transform.position = Vector3.zero;
                var b = SceneLook.WorldBounds(go);
                // Side view: camera on +X looking toward -X, so the muzzle (+Z) points to the right of the image.
                camGo.transform.position = b.center + new Vector3(5f, 0f, 0f);
                camGo.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                const float margin = 1.16f; // ~8% each side
                float halfH = b.size.y * margin * 0.5f;
                float halfW = b.size.z * margin * 0.5f;
                cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect);
                var pixels = RenderTransparent(cam, WeaponW * WeaponSuper, WeaponH * WeaponSuper, WeaponSuper);
                Save(pixels, WeaponW, WeaponH, "weapon_" + weapon.Id);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(key);
            UnityEngine.Object.DestroyImmediate(fill);
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbient;
        }
    }

    static GameObject MakeLight(string name, Vector3 direction, Color color, float intensity)
    {
        var go = new GameObject(name);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        go.transform.rotation = Quaternion.LookRotation(direction.normalized);
        return go;
    }

    // Renders the scene over black and over white; alpha follows from how much the white background shows through.
    // The black render is already premultiplied against black, so it doubles as the premultiplied colour.
    // The result is box-downsampled by `factor` and returned as straight-alpha Color32 (row 0 = bottom).
    static Color32[] RenderTransparent(Camera cam, int w, int h, int factor)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        Color[] onBlack = Capture(cam, rt, Color.black);
        Color[] onWhite = Capture(cam, rt, Color.white);
        cam.targetTexture = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);

        int ow = w / factor, oh = h / factor;
        var result = new Color32[ow * oh];
        float inv = 1f / (factor * factor);
        for (int y = 0; y < oh; y++)
        {
            for (int x = 0; x < ow; x++)
            {
                float r = 0, g = 0, bl = 0, a = 0;
                for (int sy = 0; sy < factor; sy++)
                {
                    for (int sx = 0; sx < factor; sx++)
                    {
                        int i = (y * factor + sy) * w + (x * factor + sx);
                        Color k = onBlack[i], wh = onWhite[i];
                        float alpha = 1f - ((wh.r - k.r) + (wh.g - k.g) + (wh.b - k.b)) / 3f;
                        alpha = Mathf.Clamp01(alpha);
                        r += k.r; g += k.g; bl += k.b; a += alpha;
                    }
                }
                r *= inv; g *= inv; bl *= inv; a *= inv;
                if (a < 0.004f) { result[y * ow + x] = new Color32(0, 0, 0, 0); continue; }
                result[y * ow + x] = new Color(Mathf.Clamp01(r / a), Mathf.Clamp01(g / a), Mathf.Clamp01(bl / a), a);
            }
        }
        return result;
    }

    static Color[] Capture(Camera cam, RenderTexture rt, Color background)
    {
        cam.backgroundColor = background;
        cam.targetTexture = rt;
        // The first render after a scene change can come out with stale lighting, so draw twice and keep the second.
        cam.Render();
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        var pixels = tex.GetPixels();
        UnityEngine.Object.DestroyImmediate(tex);
        return pixels;
    }

    // ---------------------------------------------------------------- output

    static void Save(Color32[] pixels, int w, int h, string key)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        string path = OutDir + "/" + key + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new Exception("No importer for " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        Debug.Log("[ItemIconBuilder] wrote " + path);
    }

    // ---------------------------------------------------------------- glyphs

    // Per-sample painter: draw code calls Fill with a signed distance (negative inside) and a colour.
    sealed class Painter
    {
        public float X, Y;
        public Color Result; // straight colour, A is 0 or 1 per sample
        const float Outline = 3.5f;

        public void Rotate(float degrees, float cx = 128f, float cy = 128f)
        {
            float rad = -degrees * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            float dx = X - cx, dy = Y - cy;
            X = cx + dx * c - dy * s;
            Y = cy + dx * s + dy * c;
        }

        // shade: brightness multiplier for a cheap cylinder look; outline darkens the rim.
        public void Fill(float d, Color color, float shade = 1f, bool outline = true)
        {
            if (d > 0f) return;
            var rgb = new Color(Mathf.Clamp01(color.r * shade), Mathf.Clamp01(color.g * shade), Mathf.Clamp01(color.b * shade), 1f);
            if (outline && d > -Outline) rgb = new Color(rgb.r * 0.5f, rgb.g * 0.5f, rgb.b * 0.5f, 1f);
            Result = rgb;
        }

        public void Fill(bool inside, Color color) { if (inside) Result = color; }
    }

    static float Circle(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

    static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        float k = Mathf.Sqrt(((x - cx) / rx) * ((x - cx) / rx) + ((y - cy) / ry) * ((y - cy) / ry));
        return (k - 1f) * Mathf.Min(rx, ry);
    }

    static float RRect(float x, float y, float cx, float cy, float hw, float hh, float r)
    {
        float qx = Mathf.Abs(x - cx) - (hw - r), qy = Mathf.Abs(y - cy) - (hh - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static float Box(float x, float y, float cx, float cy, float hw, float hh) => RRect(x, y, cx, cy, hw, hh, 0f);

    // Distance-like value for a vertical profile shape: half width hw at height y between ya and yb.
    static float Profile(float dx, float y, float ya, float yb, float hw)
    {
        return -Mathf.Min(hw - Mathf.Abs(dx), Mathf.Min(y - ya, yb - y));
    }

    static float Shade(float dx, float hw) => Mathf.Lerp(1.18f, 0.8f, Mathf.Clamp01((dx / Mathf.Max(hw, 1f) + 1f) * 0.5f));

    static readonly Color Brass = new Color(0.86f, 0.68f, 0.30f);
    static readonly Color Copper = new Color(0.80f, 0.50f, 0.32f);
    static readonly Color Steel = new Color(0.74f, 0.77f, 0.82f);
    static readonly Color ShellRed = new Color(0.78f, 0.22f, 0.20f);
    static readonly Color Olive = new Color(0.45f, 0.53f, 0.42f);
    static readonly Color Slate = new Color(0.46f, 0.52f, 0.62f);
    static readonly Color Pip = new Color(0.96f, 0.82f, 0.38f);

    // One rifle/pistol cartridge standing on the local vertical axis through x = 128, base at yBase.
    // caseHw/neckHw: case half widths (equal for a straight case); shoulder is where the taper happens.
    static void Cartridge(Painter p, float yBase, float caseH, float caseHw, float neckHw, float shoulderH, float bulletH, float bulletHw, Color bullet)
    {
        float dx = p.X - 128f;
        float yCase0 = yBase + 12f;                  // above the rim
        float yCase1 = yCase0 + caseH;               // case mouth
        float yTip = yCase1 + bulletH;
        // bullet: flat base inside the case mouth, ogive tip
        if (p.Y > yCase1 - 4f && p.Y < yTip)
        {
            float t = Mathf.Clamp01((p.Y - (yCase1 - 4f)) / (yTip - yCase1 + 4f));
            float hw = bulletHw * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t * t));
            p.Fill(Profile(dx, p.Y, yCase1 - 4f, yTip, hw), bullet, Shade(dx, hw));
        }
        // brass case with an optional neck taper in its upper part
        if (p.Y >= yCase0 && p.Y <= yCase1)
        {
            float taperStart = yCase1 - shoulderH;
            float hw = p.Y >= taperStart && shoulderH > 0f ? Mathf.Lerp(caseHw, neckHw, (p.Y - taperStart) / shoulderH) : caseHw;
            p.Fill(Profile(dx, p.Y, yCase0, yCase1, hw), Brass, Shade(dx, hw));
        }
        // rim at the base with a groove line
        p.Fill(Profile(dx, p.Y, yBase, yCase0 + 2f, caseHw + 5f), Brass * 0.92f, Shade(dx, caseHw + 5f));
        p.Fill(Box(p.X, p.Y, 128f, yCase0 + 1f, caseHw + 2f, 1.6f), new Color(0.5f, 0.38f, 0.14f), 1f, false);
    }

    static void DrawAmmo9mm(Painter p)
    {
        p.Rotate(32f);
        Cartridge(p, 62f, 70f, 24f, 24f, 0f, 36f, 21f, Copper);
    }

    static void DrawAmmo556(Painter p)
    {
        p.Rotate(32f);
        Cartridge(p, 30f, 105f, 22f, 12.5f, 30f, 60f, 12.5f, Steel);
    }

    static void DrawAmmo762(Painter p)
    {
        p.Rotate(32f);
        Cartridge(p, 24f, 118f, 25f, 15f, 30f, 62f, 15f, Copper);
    }

    static void DrawAmmo12ga(Painter p)
    {
        p.Rotate(28f);
        float dx = p.X - 128f;
        const float hw = 38f;
        float yBase = 44f, yBrass = yBase + 46f, yTop = 206f;
        // red hull
        p.Fill(Profile(dx, p.Y, yBrass - 2f, yTop, hw), ShellRed, Shade(dx, hw));
        // crimped top: darker end disc and fold lines
        if (p.Y > yTop - 14f) p.Fill(Profile(dx, p.Y, yTop - 14f, yTop, hw - 3f), ShellRed * 0.7f, Shade(dx, hw), false);
        p.Fill(Box(p.X, p.Y, 128f, yTop - 8f, hw - 8f, 1.5f), new Color(0.35f, 0.08f, 0.07f), 1f, false);
        // brass base with a wider rim
        p.Fill(Profile(dx, p.Y, yBase, yBrass, hw), Brass, Shade(dx, hw));
        p.Fill(Profile(dx, p.Y, yBase - 2f, yBase + 10f, hw + 5f), Brass * 0.9f, Shade(dx, hw + 5f));
        p.Fill(Box(p.X, p.Y, 128f, yBrass - 3f, hw - 2f, 1.6f), new Color(0.5f, 0.38f, 0.14f), 1f, false);
    }

    static void DrawMedkit(Painter p)
    {
        var plate = new Color(0.30f, 0.52f, 0.42f);
        // carry handle: an open rounded bar above the box
        float handleOuter = RRect(p.X, p.Y, 128f, 186f, 46f, 22f, 10f);
        float handleInner = RRect(p.X, p.Y, 128f, 183f, 30f, 10f, 5f);
        p.Fill(Mathf.Max(handleOuter, -handleInner), Slate * 0.85f, 1f);
        // box
        p.Fill(RRect(p.X, p.Y, 128f, 112f, 100f, 70f, 20f), plate, Shade(p.X - 128f, 100f) * 0.5f + 0.5f);
        // latch strip
        p.Fill(Box(p.X, p.Y, 128f, 160f, 98f, 3f), plate * 0.6f, 1f, false);
        // white cross
        float cross = Mathf.Min(Box(p.X, p.Y, 128f, 108f, 46f, 15f), Box(p.X, p.Y, 128f, 108f, 15f, 46f));
        p.Fill(cross, new Color(0.96f, 0.97f, 0.96f), 1f, false);
    }

    static void DrawHelmet(Painter p, int tier)
    {
        float dome = Mathf.Max(Circle(p.X, p.Y, 128f, 108f, 76f), -(p.Y - 100f));
        float brim = RRect(p.X, p.Y, 128f, 98f, 102f, 9f, 8f);
        p.Fill(Mathf.Min(dome, brim), Olive, Shade(p.X - 128f, 100f) * 0.5f + 0.5f);
        // centre ridge and a lighter rim line
        p.Fill(Box(p.X, p.Y, 128f, 150f, 6f, 28f) < 0f && dome < -3f, Olive * 0.62f);
        Pips(p, tier, 128f, 52f, 40f, 11f);
    }

    static void DrawVest(Painter p, int tier)
    {
        float body = Box(p.X, p.Y, 128f, 108f, 66f, 72f);
        float shoulders = Mathf.Min(Box(p.X, p.Y, 84f, 172f, 20f, 28f), Box(p.X, p.Y, 172f, 172f, 20f, 28f));
        float d = Mathf.Min(body, shoulders);
        d = Mathf.Max(d, -Circle(p.X, p.Y, 128f, 202f, 30f));                  // neck
        d = Mathf.Max(d, -Circle(p.X, p.Y, 36f, 148f, 36f));                    // arm holes
        d = Mathf.Max(d, -Circle(p.X, p.Y, 220f, 148f, 36f));
        d = Mathf.Max(d, 30f - p.Y);                                         // flat bottom
        p.Fill(d, Slate, Shade(p.X - 128f, 90f) * 0.5f + 0.5f);
        // plate pouch with tier bars
        float pouch = RRect(p.X, p.Y, 128f, 100f, 40f, 44f, 8f);
        if (pouch < 0f && d < -3f) p.Fill(pouch, Slate * 0.62f, 1f, false);
        for (int i = 0; i < tier; i++)
        {
            float cy = 100f + (i - (tier - 1) * 0.5f) * 22f;
            p.Fill(RRect(p.X, p.Y, 128f, cy, 26f, 5f, 3f), Pip, 1f, false);
        }
    }

    static void Pips(Painter p, int count, float cx, float cy, float spacing, float r)
    {
        for (int i = 0; i < count; i++)
            p.Fill(Circle(p.X, p.Y, cx + (i - (count - 1) * 0.5f) * spacing, cy, r), Pip, 1f, true);
    }

    static void DrawGrenade(Painter p)
    {
        var body = new Color(0.40f, 0.48f, 0.34f);
        float ell = Ellipse(p.X, p.Y, 124f, 98f, 60f, 70f);
        p.Fill(ell, body, Shade(p.X - 124f, 60f));
        // grooves clipped to the body
        if (ell < -4f)
        {
            var dark = body * 0.6f;
            if (Mathf.Abs(p.Y - 70f) < 2.5f || Mathf.Abs(p.Y - 100f) < 2.5f || Mathf.Abs(p.Y - 130f) < 2.5f || Mathf.Abs(p.X - 124f) < 2.5f)
                p.Fill(true, new Color(dark.r, dark.g, dark.b, 1f));
        }
        // neck / fuse head
        p.Fill(RRect(p.X, p.Y, 124f, 172f, 22f, 15f, 5f), Steel * 0.85f);
        // spoon lever: along the top, then down the right side of the body
        p.Fill(Mathf.Min(Box(p.X, p.Y, 156f, 190f, 40f, 6f), Box(p.X, p.Y, 192f, 150f, 6f, 46f)), Steel);
        // pin and ring
        p.Fill(Box(p.X, p.Y, 98f, 176f, 16f, 3f), Steel);
        float ring = Mathf.Abs(Circle(p.X, p.Y, 70f, 190f, 22f)) - 4.5f;
        p.Fill(ring, Steel, 1f, false);
    }

    static readonly Color OpticBody = new Color(0.30f, 0.32f, 0.37f);
    static readonly Color Glass = new Color(0.32f, 0.52f, 0.68f);
    static readonly Color Ember = new Color(0.95f, 0.30f, 0.22f);

    // Red dot (ring = false) and holographic (ring = true) sights seen from the side-front: a housing with a window.
    static void DrawSight(Painter p, bool ring)
    {
        p.Fill(RRect(p.X, p.Y, 128f, 64f, 78f, 12f, 5f), OpticBody * 0.8f);          // rail foot
        p.Fill(RRect(p.X, p.Y, 128f, 118f, 66f, 54f, 14f), OpticBody, Shade(p.X - 128f, 66f) * 0.5f + 0.5f);
        p.Fill(RRect(p.X, p.Y, 128f, 120f, 48f, 38f, 8f), Glass * 0.55f, 1f, false);  // window
        if (!ring)
        {
            p.Fill(Circle(p.X, p.Y, 128f, 120f, 9f), Ember, 1f, false);
            return;
        }
        float ringD = Mathf.Abs(Circle(p.X, p.Y, 128f, 120f, 26f)) - 3.5f;
        p.Fill(Mathf.Min(ringD, Circle(p.X, p.Y, 128f, 120f, 4.5f)), Pip, 1f, false);
        p.Fill(Mathf.Min(Box(p.X, p.Y, 128f, 156f, 2.5f, 6f), Box(p.X, p.Y, 128f, 84f, 2.5f, 6f)), Pip, 1f, false);
    }

    // A magnifying scope lying along the icon: the tube grows with the zoom, with one to three amber bands.
    static void DrawScope(Painter p, int zoom)
    {
        float hl = Mathf.Min(100f, 58f + 5f * zoom);
        float ht = 21f + 1.3f * zoom;
        int bands = zoom <= 3 ? 1 : zoom <= 6 ? 2 : 3;
        float cx = 128f, cy = 118f;
        // mounts under the tube
        p.Fill(Mathf.Min(Box(p.X, p.Y, cx - hl * 0.5f, cy - ht - 8f, 9f, 10f), Box(p.X, p.Y, cx + hl * 0.5f, cy - ht - 8f, 9f, 10f)), OpticBody * 0.8f);
        // tube, bells
        p.Fill(RRect(p.X, p.Y, cx, cy, hl, ht, 8f), OpticBody, Shade(p.Y - cy, ht) * 0.5f + 0.5f);
        p.Fill(RRect(p.X, p.Y, cx + hl - 6f, cy, 20f, ht * 1.32f, 7f), OpticBody * 0.85f, Shade(p.Y - cy, ht * 1.3f) * 0.5f + 0.5f);
        p.Fill(RRect(p.X, p.Y, cx - hl + 4f, cy, 14f, ht * 1.14f, 6f), OpticBody * 0.85f, Shade(p.Y - cy, ht * 1.1f) * 0.5f + 0.5f);
        // lens on the front bell
        p.Fill(Ellipse(p.X, p.Y, cx + hl + 6f, cy, 7f, ht * 1.05f), Glass, 1f, false);
        // amber bands
        for (int i = 0; i < bands; i++)
            p.Fill(Box(p.X, p.Y, cx - hl * 0.42f + i * 18f, cy, 4f, ht - 4f), Pip, 1f, false);
        // turret knob on top
        p.Fill(RRect(p.X, p.Y, cx + hl * 0.05f, cy + ht + 8f, 11f, 9f, 3f), OpticBody * 0.9f);
    }

    // A rolled bandage seen from the side: cream roll with wound layers, a loose tail and a red stripe.
    static void DrawBandage(Painter p)
    {
        var cloth = new Color(0.93f, 0.90f, 0.82f);
        // loose tail running off to the right along the floor
        p.Fill(Box(p.X, p.Y, 182f, 66f, 54f, 7f), cloth * 0.92f, 1f);
        // the roll: a disc from the front with rings
        float roll = Circle(p.X, p.Y, 106f, 112f, 68f);
        p.Fill(roll, cloth, Shade(p.X - 106f, 68f) * 0.5f + 0.5f);
        if (roll < -4f)
        {
            float r = Circle(p.X, p.Y, 106f, 112f, 0f);
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(r - (24f + i * 17f)) < 1.8f) p.Fill(true, new Color(0.62f, 0.58f, 0.5f, 1f));
            p.Fill(Circle(p.X, p.Y, 106f, 112f, 12f), new Color(0.35f, 0.32f, 0.28f), 1f, false);
        }
        // red stripe across the roll
        if (roll < -4f) p.Fill(Box(p.X, p.Y, 106f, 112f, 66f, 6f) < 0f && Mathf.Abs(p.Y - 112f) < 6f, new Color(0.82f, 0.2f, 0.18f, 1f));
    }

    // A small first-aid pouch: a soft red case with a zip line and a white cross.
    static void DrawFirstAid(Painter p)
    {
        var red = new Color(0.78f, 0.22f, 0.20f);
        p.Fill(RRect(p.X, p.Y, 128f, 112f, 92f, 62f, 26f), red, Shade(p.X - 128f, 92f) * 0.5f + 0.5f);
        // zip line along the top
        p.Fill(Box(p.X, p.Y, 128f, 152f, 80f, 2.5f), red * 0.55f, 1f, false);
        // pull tab
        p.Fill(RRect(p.X, p.Y, 196f, 152f, 14f, 8f, 4f), Steel, 1f, false);
        float cross = Mathf.Min(Box(p.X, p.Y, 128f, 104f, 38f, 12f), Box(p.X, p.Y, 128f, 104f, 12f, 38f));
        p.Fill(cross, new Color(0.96f, 0.97f, 0.96f), 1f, false);
    }

    // A smoke canister: grey cylinder with a collar, a top lever and a puff of smoke above.
    static void DrawSmoke(Painter p)
    {
        var body = new Color(0.55f, 0.58f, 0.60f);
        float dx = p.X - 112f;
        p.Fill(Profile(dx, p.Y, 24f, 140f, 44f), body, Shade(dx, 44f));
        // collar and top cap
        p.Fill(Profile(dx, p.Y, 132f, 156f, 30f), Steel * 0.85f, Shade(dx, 30f));
        // coloured band
        p.Fill(Box(p.X, p.Y, 112f, 86f, 42f, 10f), new Color(0.88f, 0.62f, 0.18f), 1f, false);
        // spoon lever
        p.Fill(Mathf.Min(Box(p.X, p.Y, 146f, 164f, 34f, 5f), Box(p.X, p.Y, 176f, 130f, 5f, 38f)), Steel);
        // the puff: overlapping pale discs
        float puff = Mathf.Min(Circle(p.X, p.Y, 84f, 200f, 24f), Mathf.Min(Circle(p.X, p.Y, 118f, 214f, 28f), Circle(p.X, p.Y, 156f, 200f, 22f)));
        p.Fill(puff, new Color(0.86f, 0.88f, 0.90f), 1f);
    }

    static void BuildGlyphs()
    {
        DrawIcon("ammo_9mm", DrawAmmo9mm);
        DrawIcon("ammo_556", DrawAmmo556);
        DrawIcon("ammo_762", DrawAmmo762);
        DrawIcon("ammo_12ga", DrawAmmo12ga);
        DrawIcon("medkit", DrawMedkit);
        DrawIcon("grenade", DrawGrenade);
        DrawIcon("optic_reddot", p => DrawSight(p, false));
        DrawIcon("optic_holo", p => DrawSight(p, true));
        foreach (int zoom in new[] { 2, 3, 4, 6, 8, 16 })
        {
            int z = zoom;
            DrawIcon("optic_" + z + "x", p => DrawScope(p, z));
        }
        DrawIcon("bandage", DrawBandage);
        DrawIcon("firstaid", DrawFirstAid);
        DrawIcon("smoke", DrawSmoke);
        for (int tier = 1; tier <= 3; tier++)
        {
            int t = tier;
            DrawIcon("helmet_" + t, p => DrawHelmet(p, t));
            DrawIcon("vest_" + t, p => DrawVest(p, t));
        }
    }

    static void DrawIcon(string key, Action<Painter> draw)
    {
        var pixels = new Color32[GlyphSize * GlyphSize];
        var painter = new Painter();
        float inv = 1f / (GlyphSamples * GlyphSamples);
        for (int y = 0; y < GlyphSize; y++)
        {
            for (int x = 0; x < GlyphSize; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < GlyphSamples; sy++)
                {
                    for (int sx = 0; sx < GlyphSamples; sx++)
                    {
                        painter.X = x + (sx + 0.5f) / GlyphSamples;
                        painter.Y = y + (sy + 0.5f) / GlyphSamples;
                        painter.Result = new Color(0f, 0f, 0f, 0f);
                        draw(painter);
                        if (painter.Result.a > 0f) { r += painter.Result.r; g += painter.Result.g; b += painter.Result.b; a += 1f; }
                    }
                }
                if (a <= 0f) { pixels[y * GlyphSize + x] = new Color32(0, 0, 0, 0); continue; }
                pixels[y * GlyphSize + x] = new Color(r / a, g / a, b / a, a * inv);
            }
        }
        Save(pixels, GlyphSize, GlyphSize, key);
    }
}

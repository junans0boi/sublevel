using System.IO;
using UnityEditor;
using UnityEngine;

// Icons of the boost items and the extra throwables (ADR-099/105/110): flat glyphs from signed-distance shapes, like the
// other items, no markings. tools/unity.sh run ItemIconBuilder.BuildItemsOnly -> Resources/Icons/{drink,pill,syringe,...}.png
public static partial class ItemIconBuilder
{
    public static void BuildItemsOnly()
    {
        int code = 0;
        try
        {
            Directory.CreateDirectory(OutDir);
            DrawItems();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ItemIconBuilder] items failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static void DrawItems()
    {
        DrawIcon("drink", DrawDrink);
        DrawIcon("pill", DrawPill);
        DrawIcon("syringe", DrawSyringe);
        DrawIcon("flash", DrawFlash);
        DrawIcon("molotov", DrawMolotov);
    }

    // A drink can: teal body, a yellow band, steel rims and a pull tab.
    static void DrawDrink(Painter p)
    {
        var teal = new Color(0.14f, 0.58f, 0.62f);
        float dx = p.X - 128f;
        p.Fill(RRect(p.X, p.Y, 128f, 108f, 46f, 84f, 10f), teal, Shade(dx, 46f));
        p.Fill(Box(p.X, p.Y, 128f, 108f, 46f, 18f), new Color(0.95f, 0.80f, 0.20f), 1f, false);
        p.Fill(Box(p.X, p.Y, 128f, 108f, 46f, 5f), new Color(0.12f, 0.12f, 0.13f), 1f, false);
        p.Fill(RRect(p.X, p.Y, 128f, 196f, 40f, 8f, 4f), Steel, Shade(dx, 40f));
        p.Fill(RRect(p.X, p.Y, 128f, 22f, 40f, 8f, 4f), Steel * 0.9f, Shade(dx, 40f));
        p.Fill(Mathf.Max(RRect(p.X, p.Y, 138f, 207f, 18f, 6f, 3f), -RRect(p.X, p.Y, 144f, 207f, 6f, 2.5f, 1f)), Steel * 0.8f, 1f);
    }

    // A pill bottle: amber, a dark cap with ridges, a pale label and one capsule on it.
    static void DrawPill(Painter p)
    {
        var amber = new Color(0.86f, 0.52f, 0.14f);
        float dx = p.X - 128f;
        p.Fill(RRect(p.X, p.Y, 128f, 98f, 54f, 68f, 16f), amber, Shade(dx, 54f));
        p.Fill(RRect(p.X, p.Y, 128f, 188f, 60f, 24f, 8f), new Color(0.20f, 0.20f, 0.22f), Shade(dx, 60f));
        if (Mathf.Abs(Mathf.Repeat(p.X, 16f) - 8f) < 1.5f && Mathf.Abs(p.Y - 188f) < 20f && Mathf.Abs(dx) < 56f) p.Fill(true, new Color(0.34f, 0.34f, 0.36f));
        p.Fill(RRect(p.X, p.Y, 128f, 94f, 54f, 34f, 4f), new Color(0.93f, 0.93f, 0.90f), 1f, false);
        float capsule = RRect(p.X, p.Y, 128f, 94f, 30f, 11f, 11f);
        p.Fill(capsule, amber, 1f, true);
        p.Fill(Mathf.Max(capsule, p.X - 128f), new Color(0.96f, 0.97f, 0.96f), 1f, false);
    }

    // A flash grenade: a pale steel can with a yellow band, a lever, and a burst of rays above it.
    static void DrawFlash(Painter p)
    {
        var steel = new Color(0.78f, 0.80f, 0.84f);
        float dx = p.X - 112f;
        p.Fill(Profile(dx, p.Y, 24f, 138f, 42f), steel, Shade(dx, 42f));
        p.Fill(Profile(dx, p.Y, 130f, 152f, 28f), new Color(0.22f, 0.23f, 0.25f), Shade(dx, 28f));
        p.Fill(Box(p.X, p.Y, 112f, 96f, 40f, 9f), new Color(0.96f, 0.84f, 0.25f), 1f, false);
        p.Fill(Box(p.X, p.Y, 112f, 70f, 40f, 4f), new Color(0.96f, 0.84f, 0.25f), 1f, false);
        p.Fill(Mathf.Min(Box(p.X, p.Y, 146f, 160f, 34f, 5f), Box(p.X, p.Y, 176f, 130f, 5f, 38f)), Steel);
        // rays: two crossed bars and a bright core
        float ray = Mathf.Min(Box(p.X, p.Y, 112f, 206f, 6f, 26f), Box(p.X, p.Y, 112f, 206f, 26f, 6f));
        p.Fill(ray, new Color(1f, 0.97f, 0.7f), 1f, false);
        p.Fill(Circle(p.X, p.Y, 112f, 206f, 11f), Color.white, 1f, false);
    }

    // A fire bottle: green glass with a pale label, a long neck, a rag in the mouth and a flame above it.
    static void DrawMolotov(Painter p)
    {
        var glass = new Color(0.22f, 0.44f, 0.26f);
        float dx = p.X - 128f;
        float body = RRect(p.X, p.Y, 128f, 82f, 42f, 58f, 16f);
        float neck = Box(p.X, p.Y, 128f, 148f, 14f, 30f);
        p.Fill(Mathf.Min(body, neck), glass, Shade(dx, 42f));
        p.Fill(Box(p.X, p.Y, 128f, 84f, 42f, 20f), new Color(0.88f, 0.84f, 0.68f), 1f, false); // label
        p.Fill(RRect(p.X, p.Y, 128f, 184f, 11f, 9f, 3f), new Color(0.82f, 0.74f, 0.55f), 1f);   // rag
        // flame: an orange drop with a yellow core
        float drop = Mathf.Min(Circle(p.X, p.Y, 128f, 212f, 17f), RRect(p.X, p.Y, 128f, 226f, 8f, 20f, 8f));
        p.Fill(drop, new Color(0.96f, 0.45f, 0.10f), 1f);
        p.Fill(Circle(p.X, p.Y, 128f, 210f, 8f), new Color(1f, 0.88f, 0.35f), 1f, false);
    }

    // An injector: a clear barrel with a red fill, plunger with a thumb rest and finger flange, hub and needle.
    static void DrawSyringe(Painter p)
    {
        float dx = p.X - 128f;
        p.Fill(Box(p.X, p.Y, 128f, 26f, 3f, 20f), Steel, 1f, false);                      // needle
        p.Fill(RRect(p.X, p.Y, 128f, 56f, 13f, 10f, 3f), new Color(0.22f, 0.23f, 0.25f), Shade(dx, 13f)); // hub
        p.Fill(RRect(p.X, p.Y, 128f, 126f, 24f, 62f, 8f), new Color(0.82f, 0.88f, 0.90f), Shade(dx, 24f));  // barrel
        p.Fill(RRect(p.X, p.Y, 128f, 108f, 15f, 42f, 5f), new Color(0.78f, 0.18f, 0.20f), Shade(dx, 15f), false); // fluid
        p.Fill(RRect(p.X, p.Y, 128f, 186f, 42f, 7f, 3f), new Color(0.22f, 0.23f, 0.25f), 1f);            // finger flange
        p.Fill(Box(p.X, p.Y, 128f, 208f, 5f, 18f), Steel, 1f, false);                                      // plunger rod
        p.Fill(RRect(p.X, p.Y, 128f, 228f, 26f, 7f, 3f), new Color(0.22f, 0.23f, 0.25f), 1f);            // thumb rest
    }
}

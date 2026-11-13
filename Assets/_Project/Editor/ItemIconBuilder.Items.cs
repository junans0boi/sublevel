using System.IO;
using UnityEditor;
using UnityEngine;

// Icons of the boost items and the extra throwables (ADR-099/102/103): flat glyphs from signed-distance shapes, like the
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

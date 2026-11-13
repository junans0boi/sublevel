using System.IO;
using UnityEditor;
using UnityEngine;

// Backpack icons (ADR-091): a pack seen from the front, the colour of its tier (as LootMeshes) and one pale bar per
// tier on the front pocket, same 256x256 glyph frame as the armour.
// tools/unity.sh run ItemIconBuilder.BuildBackpacksOnly -> Resources/Icons/backpack_<tier>.png (the full Build makes them too)
public static partial class ItemIconBuilder
{
    static readonly Color[] BackpackColors = { new Color(0.66f, 0.60f, 0.42f), new Color(0.42f, 0.46f, 0.28f), new Color(0.20f, 0.27f, 0.18f) };

    // Only the backpack icons, so the others are not rewritten.
    public static void BuildBackpacksOnly()
    {
        int code = 0;
        try
        {
            Directory.CreateDirectory(OutDir);
            DrawBackpacks();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ItemIconBuilder] backpacks failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static void DrawBackpacks()
    {
        for (int tier = 1; tier <= 3; tier++)
        {
            int t = tier;
            DrawIcon("backpack_" + t, p => DrawBackpack(p, t));
        }
    }

    static void DrawBackpack(Painter p, int tier)
    {
        var cloth = BackpackColors[tier - 1];
        var dark = new Color(0.16f, 0.16f, 0.17f);
        // carry loop on top: a bar with a hole
        p.Fill(Mathf.Max(RRect(p.X, p.Y, 128f, 212f, 22f, 14f, 7f), -RRect(p.X, p.Y, 128f, 210f, 12f, 6f, 3f)), dark, 1f);
        // side straps behind the body
        p.Fill(Mathf.Min(RRect(p.X, p.Y, 56f, 110f, 10f, 64f, 5f), RRect(p.X, p.Y, 200f, 110f, 10f, 64f, 5f)), dark, 1f);
        // body: rounded at the top
        float body = RRect(p.X, p.Y, 128f, 112f, 70f, 90f, 34f);
        p.Fill(body, cloth, Shade(p.X - 128f, 70f) * 0.5f + 0.5f);
        // lid flap
        float lid = RRect(p.X, p.Y, 128f, 168f, 64f, 34f, 26f);
        if (body < -3f) p.Fill(Mathf.Max(lid, 136f - p.Y), cloth * 0.72f, 1f, false);
        if (body < -3f && Mathf.Abs(p.Y - 136f) < 2.5f && lid < 0f) p.Fill(true, cloth * 0.5f);
        // lid strap down the middle
        p.Fill(RRect(p.X, p.Y, 128f, 134f, 7f, 22f, 3f), dark, 1f, false);
        // front pocket with a zip line and the tier bars
        float pocket = RRect(p.X, p.Y, 128f, 74f, 50f, 40f, 12f);
        p.Fill(pocket, cloth * 0.86f, 1f);
        if (pocket < -3f && Mathf.Abs(p.Y - 104f) < 2f) p.Fill(true, dark);
        for (int i = 0; i < tier; i++)
        {
            float cy = 72f + (i - (tier - 1) * 0.5f) * 18f;
            p.Fill(RRect(p.X, p.Y, 128f, cy, 28f, 5f, 3f), Pip, 1f, false);
        }
    }
}

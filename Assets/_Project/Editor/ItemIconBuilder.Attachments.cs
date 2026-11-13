using System.IO;
using UnityEditor;
using UnityEngine;

// Attachment icons (ADR-100): grips and the stock, flat glyphs drawn from signed-distance shapes like the armour and
// backpacks (256x256, no markings). Same silhouettes as the ground models in LootMeshes.
// tools/unity.sh run ItemIconBuilder.BuildAttachmentsOnly -> Resources/Icons/att_*.png (the full Build makes them too)
public static partial class ItemIconBuilder
{
    static readonly Color Polymer = new Color(0.58f, 0.63f, 0.70f);
    static readonly Color PolymerDark = new Color(0.30f, 0.33f, 0.38f);

    public static void BuildAttachmentsOnly()
    {
        int code = 0;
        try
        {
            Directory.CreateDirectory(OutDir);
            DrawAttachments();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ItemIconBuilder] attachments failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static void DrawAttachments()
    {
        DrawIcon("att_grip_v", p => { DrawRailFoot(p, 128f, 226f); DrawGripBody(p, 128f, 218f, 84f, 40f); });
        DrawIcon("att_grip_a", p =>
        {
            DrawRailFoot(p, 90f, 226f);
            // the grip leans forward (to the right) off the rail
            p.Rotate(34f, 90f, 218f);
            DrawGripBody(p, 90f, 218f, 78f, 40f);
        });
        DrawIcon("att_grip_c", p =>
        {
            DrawRailFoot(p, 90f, 226f);
            // a curved grip: a swept post, thin at the rail and fat at the hand
            float d = 999f;
            for (int i = 0; i <= 36; i++)
            {
                float t = i / 36f, ang = Mathf.Lerp(Mathf.PI * 0.95f, Mathf.PI * 0.25f, t);
                d = Mathf.Min(d, Circle(p.X, p.Y, 150f + Mathf.Cos(ang) * 64f, 214f - Mathf.Sin(ang) * 118f + 8f, Mathf.Lerp(14f, 24f, t)));
            }
            p.Fill(d, Polymer, 1f);
        });
        DrawIcon("att_stock", DrawStock);
    }

    // The clamp that sits on the rail: a flat block with two bolts.
    static void DrawRailFoot(Painter p, float cx, float cy)
    {
        p.Fill(RRect(p.X, p.Y, cx, cy, 52f, 13f, 5f), PolymerDark, 1f);
        p.Fill(Circle(p.X, p.Y, cx - 34f, cy, 4.5f), Steel, 1f, false);
        p.Fill(Circle(p.X, p.Y, cx + 34f, cy, 4.5f), Steel, 1f, false);
    }

    // A vertical post hanging from (cx, top), `length` long and `width` wide, with finger grooves and a flared base.
    static void DrawGripBody(Painter p, float cx, float top, float length, float width)
    {
        float bottom = top - 2f * length;
        float neck = RRect(p.X, p.Y, cx, top - 12f, 14f, 12f, 4f);
        p.Fill(neck, PolymerDark, 1f);
        float body = RRect(p.X, p.Y, cx, (top + bottom) * 0.5f - 8f, width * 0.5f, length - 10f, 16f);
        p.Fill(body, Polymer, Shade(p.X - cx, width * 0.5f) * 0.5f + 0.5f);
        if (body < -4f)
            for (int g = 0; g < 4; g++) p.Fill(Mathf.Abs(p.Y - (top - 40f - g * 16f)) < 2.2f, PolymerDark);
        p.Fill(RRect(p.X, p.Y, cx, bottom + 8f, width * 0.5f + 5f, 8f, 5f), PolymerDark, 1f);
    }

    // A stock seen from the side: tube from the receiver at the right to the butt plate at the left, a cheek riser on top.
    static void DrawStock(Painter p)
    {
        // the receiver stub the tube comes out of
        p.Fill(RRect(p.X, p.Y, 214f, 132f, 30f, 28f, 8f), PolymerDark, 1f);
        // the tube: tapers slightly toward the butt
        float tube = Profile(p.Y - Mathf.Lerp(126f, 124f, Mathf.Clamp01((214f - p.X) / 150f)), p.X, 64f, 200f, 20f - Mathf.Clamp01((214f - p.X) / 150f) * 4f);
        p.Fill(tube, Polymer, Shade(p.Y - 126f, 20f) * 0.5f + 0.5f);
        // cheek riser
        p.Fill(RRect(p.X, p.Y, 124f, 152f, 46f, 9f, 6f), Polymer, 1.08f);
        // butt plate: a tall curved plate with a pad
        float plate = RRect(p.X, p.Y, 58f, 120f, 12f, 54f, 10f);
        p.Fill(plate, PolymerDark, 1f);
        p.Fill(RRect(p.X, p.Y, 52f, 120f, 5f, 46f, 4f), Pip, 0.8f, false);
    }
}

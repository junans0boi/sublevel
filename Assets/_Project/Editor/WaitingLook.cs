using UnityEditor;
using UnityEngine;

// The waiting island's own materials (ADR-495, 준환 r5.2: the lift and the hall should look refined): dark graphite steel for the
// frame, tower, shaft, rails and landing pad, light architectural concrete for the walls, pale metal for the dome, brushed stainless
// for the lift car and the call panels, tread plate for the car floor, amber for the wall band and the jump board. Kept apart from
// WorldMaterials so the town keeps its own look. They live in Resources so WaitingHud can put them on an island baked before this
// change (WaitingHud.Dress). Textures: CC0 sets already in the ledger (A-0001 Concrete034, A-0003 MetalPlates006, A-0006 Metal032).
// Plain metallic/smoothness values, no masks: the hall's warm point lights make a masked metal read black.
public static class WaitingLook
{
    const string Dir = "Assets/_Project/Resources/WaitingLook";
    const string Tex = "Assets/_Project/Art/Materials";

    public static Material Steel => Make("WaitingSteel", "Metal032", new Color(0.30f, 0.32f, 0.35f), 0.45f, 0.55f, 1f);
    public static Material Panel => Make("WaitingPanel", "Concrete034", new Color(0.82f, 0.82f, 0.80f), 0f, 0.25f, 0.6f);
    public static Material Dome => Make("WaitingDome", "Metal032", new Color(0.62f, 0.64f, 0.67f), 0.3f, 0.4f, 0.6f);
    public static Material Stainless => Make("WaitingStainless", "Metal032", new Color(0.86f, 0.88f, 0.90f), 0.6f, 0.75f, 0.5f);
    public static Material Tread => Make("WaitingTread", "MetalPlates006", new Color(0.75f, 0.76f, 0.78f), 0.5f, 0.5f, 0.5f);
    public static Material Accent => Make("WaitingAccent", "Metal032", new Color(0.95f, 0.62f, 0.18f), 0.2f, 0.5f, 0.4f);   // the wall band and the jump board: the call buttons' amber

    // Batchmode: tools/unity.sh run WaitingLook.Build
    public static void Build()
    {
        _ = Steel; _ = Panel; _ = Dome; _ = Stainless; _ = Tread; _ = Accent;
        AssetDatabase.SaveAssets();
        Debug.Log("[WaitingLook] materials in " + Dir);
    }

    static Material Make(string name, string id, Color tint, float metallic, float smoothness, float bump)
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Project/Resources", "WaitingLook");
        var path = $"{Dir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetTexture("_BaseMap", SceneLook.Load<Texture2D>($"{Tex}/{id}/{id}_color.jpg"));
        mat.SetTexture("_BumpMap", SceneLook.Load<Texture2D>($"{Tex}/{id}/{id}_normal.jpg"));
        mat.EnableKeyword("_NORMALMAP");
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_BumpScale", bump);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}

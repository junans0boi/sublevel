using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static EditorFields;

// Window glass (ADR-065 (4)): some of the window holes get a pane. A pane is a thin see-through box on the Ignore
// Raycast layer that blocks movement until a shot or a body going through the window breaks it (GlassPane).
// Called from PlaceBuilding once a building's boxes are placed; ids count up over the whole world, in build order.
public static partial class WorldBuilder
{
    const int GlassPercent = 45;           // share of windows glazed
    const float GlassThickness = 0.03f;
    const float GlassInset = 0.02f;        // smaller than the hole on every side so it never touches the wall

    static int _nextGlassId;

    static void ResetGlass() => _nextGlassId = 0;

    static void AddWindowGlass(Transform building, BuildingPlan plan, int seed)
    {
        if (plan.Windows.Count == 0) return;
        Material material = null;
        for (int i = 0; i < plan.Windows.Count; i++)
        {
            var w = plan.Windows[i];
            if (Hash(seed, 31000 + i) % 100 >= GlassPercent) continue;
            if (w.Width <= 2f * GlassInset || w.Height <= 2f * GlassInset) continue;
            if (material == null) material = GlassMaterial();
            var pane = GameObject.CreatePrimitive(PrimitiveType.Cube); // its box collider stops movement
            pane.name = "Glass_" + w.Name;
            pane.transform.SetParent(building, false);
            pane.transform.localPosition = new Vector3(w.X, w.Y + w.Height * 0.5f, w.Z);
            pane.transform.localRotation = Quaternion.Euler(0f, w.FacingDeg, 0f); // forward = out of the wall
            pane.transform.localScale = new Vector3(w.Width - 2f * GlassInset, w.Height - 2f * GlassInset, GlassThickness);
            pane.isStatic = false; // hidden when it breaks
            pane.layer = GlassPane.Layer;
            var renderer = pane.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var component = pane.AddComponent<GlassPane>();
            int id = _nextGlassId++;
            Set(component, "_id", p => p.intValue = id);
        }
    }

    // Faintly blue-green, glossy and see-through. The transparent setup is done by hand (the material inspector
    // normally applies the blend mode), the same recipe as SceneLook.OpticWindow.
    static Material GlassMaterial()
    {
        var path = $"{WorldMaterials.MaterialDir}/World_Glass.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", new Color(0.62f, 0.78f, 0.80f, 0.22f));
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.93f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}

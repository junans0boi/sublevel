using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Turns on URP post-processing for the project: renderer post data, a light SSAO pass and the shared
// volume profile (tonemapping, mild grading, mild vignette). Safe to run repeatedly.
// Run headless: tools/unity.sh run RenderSetup.Run
public static class RenderSetup
{
    public const string ProfilePath = "Assets/_Project/Settings/Game_PostProcess.asset";
    const string RendererPath = "Assets/_Project/Settings/Game_URP_Renderer.asset";
    const string PostDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

    public static void Run()
    {
        Apply();
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    public static VolumeProfile Apply()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null) throw new System.Exception("renderer data missing: " + RendererPath);
        var postData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostDataPath);
        if (postData == null) throw new System.Exception("URP PostProcessData missing: " + PostDataPath);
        renderer.postProcessData = postData;
        ConfigureSsao(renderer);
        EditorUtility.SetDirty(renderer);

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        Component<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
        var grading = Component<ColorAdjustments>(profile);
        grading.postExposure.Override(0.25f);
        grading.contrast.Override(12f);
        grading.saturation.Override(-10f);
        grading.colorFilter.Override(new Color(0.97f, 0.99f, 1f));
        var vignette = Component<Vignette>(profile);
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.45f);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    static T Component<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) return component;
        component = profile.Add<T>();
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    // Light SSAO configuration: half resolution, depth-only, applied after opaques.
    static void ConfigureSsao(UniversalRendererData renderer)
    {
        var ssao = renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
        if (ssao == null)
        {
            ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "ScreenSpaceAmbientOcclusion";
            AssetDatabase.AddObjectToAsset(ssao, renderer);
            renderer.rendererFeatures.Add(ssao);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            // The renderer keeps a parallel list of sub-asset ids to recover lost feature references.
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId))
            {
                var data = new SerializedObject(renderer);
                var map = data.FindProperty("m_RendererFeatureMap");
                map.arraySize = renderer.rendererFeatures.Count;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        var so = new SerializedObject(ssao);
        Prop(so, "Downsample").boolValue = true;
        Prop(so, "AfterOpaque").boolValue = true;
        Prop(so, "Source").intValue = 0;        // Depth
        Prop(so, "NormalSamples").intValue = 2; // High: fewer smudges on small props when normals come from depth
        Prop(so, "Samples").intValue = 1;       // Medium (8 samples); Low leaves visible grain
        Prop(so, "BlurQuality").intValue = 0;   // High (bilateral)
        Prop(so, "Intensity").floatValue = 0.4f;
        Prop(so, "DirectLightingStrength").floatValue = 0.25f;
        Prop(so, "Radius").floatValue = 0.12f;
        Prop(so, "Falloff").floatValue = 50f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static SerializedProperty Prop(SerializedObject so, string name)
    {
        var prop = so.FindProperty("m_Settings." + name);
        if (prop == null) throw new System.Exception("SSAO setting not found: " + name);
        return prop;
    }
}

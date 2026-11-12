using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class Setup
{
    const string SettingsDir = "Assets/_Project/Settings";
    const string PipelinePath = SettingsDir + "/Game_URP.asset";

    public static void ConfigureProject()
    {
        Directory.CreateDirectory(SettingsDir);

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, SettingsDir + "/Game_URP_Renderer.asset");
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
        }
        GraphicsSettings.defaultRenderPipeline = pipeline;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }

        PlayerSettings.companyName = "STEADY TO VIVID STUDIO";
        PlayerSettings.productName = "Sublevel";
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;

        // Input System only (takes effect after editor restart).
        var player = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
        var handler = player.FindProperty("activeInputHandler");
        if (handler != null) { handler.intValue = 1; player.ApplyModifiedPropertiesWithoutUndo(); }

        AssetDatabase.SaveAssets();
        Debug.Log("[Setup] ConfigureProject done. URP=" + PipelinePath);
        EditorApplication.Exit(0);
    }
}

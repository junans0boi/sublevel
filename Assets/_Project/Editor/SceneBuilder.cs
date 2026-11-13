using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SceneBuilder
{
    public const string BootstrapPath = "Assets/_Project/Scenes/Bootstrap.unity";

    // Build order: the first scene is what a player build starts in (`-scene <name>` picks another, see StartScene).
    // The outdoor map comes first (ADR-050); until it has been generated the test box is the start scene.
    static readonly string[] BuildOrder =
    {
        "Assets/_Project/Scenes/MainMenu.unity",
        "Assets/_Project/Scenes/World.unity",
        "Assets/_Project/Scenes/TestBox.unity",
        BootstrapPath,
        "Assets/_Project/Scenes/ZoneD.unity",
    };

    // The one place that decides which scenes ship. Scenes that have not been generated yet are skipped.
    public static void RegisterBuildScenes()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
        foreach (var path in BuildOrder)
            if (File.Exists(path)) scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    public static void BuildBootstrap()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BootstrapPath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(5, 1, 5);

        EditorSceneManager.SaveScene(scene, BootstrapPath);
        RegisterBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("[SceneBuilder] Saved " + BootstrapPath);
        EditorApplication.Exit(0);
    }
}

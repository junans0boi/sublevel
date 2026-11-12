using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SceneBuilder
{
    public const string BootstrapPath = "Assets/_Project/Scenes/Bootstrap.unity";

    public static void BuildBootstrap()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BootstrapPath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(5, 1, 5);

        EditorSceneManager.SaveScene(scene, BootstrapPath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapPath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[SceneBuilder] Saved " + BootstrapPath);
        EditorApplication.Exit(0);
    }
}

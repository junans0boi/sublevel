using System.IO;
using System.Linq;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Generates the main menu scene: a camera on a dark background, an audio listener and the MainMenu component (the UI
// is built in code). It is registered first in the build so a normal launch starts here.
// Run headless: tools/unity.sh run MainMenuBuilder.Build
public static class MainMenuBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/MainMenu.unity";

    public static void Build()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.04f, 0.05f, 1f);
        camera.cullingMask = -1;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 10f;
        cameraObject.AddComponent<AudioListener>();

        var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
        LobbyStageBuilder.Build(camera); // the lobby's 3D elevator and party (ADR-247)
        var profile = RenderSetup.Apply();
        SceneLook.EnablePost(camera);
        SceneLook.AddPostVolume(profile);

        EditorSceneManager.SaveScene(scene, ScenePath);
        RegisterFirst();
        AssetDatabase.SaveAssets();
        Debug.Log("[MainMenuBuilder] Saved " + ScenePath);
        EditorApplication.Exit(0);
    }

    // SceneBuilder owns the build list; this keeps MainMenu first even while SceneBuilder.BuildOrder does not name it
    // (once it does, this is a no-op).
    static void RegisterFirst()
    {
        SceneBuilder.RegisterBuildScenes();
        var scenes = EditorBuildSettings.scenes.ToList();
        scenes.RemoveAll(s => s.path == ScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}

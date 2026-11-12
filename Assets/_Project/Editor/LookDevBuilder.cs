using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the M1.5 look-check scene (one character, one weapon, a few PBR materials, HDRI sky)
// and renders screenshots to Logs/lookdev (ADR-035).
// Run with a graphics device: tools/unity.sh run-gfx LookDevBuilder.Build
public static class LookDevBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/LookDev.unity";
    const string OutDir = "Logs/lookdev";

    public static void Build()
    {
        Directory.CreateDirectory(OutDir);
        SceneLook.PrepareTextures();
        var postProfile = RenderSetup.Apply();
        CharacterKit.Prepare();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneLook.SetupSky();
        SceneLook.AddLights();
        SceneLook.AddPostVolume(postProfile);

        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20), SceneLook.Floor, 2.5f);
        SceneLook.WorldBox("BackWall", null, new Vector3(0, 1.5f, 4f), new Vector3(12, 3, 0.3f), SceneLook.Wall, 3f);
        SceneLook.WorldBox("SideWall", null, new Vector3(-4, 1.5f, 1f), new Vector3(0.3f, 3, 6), SceneLook.Panel, 2.5f);
        SceneLook.WorldBox("Pillar", null, new Vector3(-2.2f, 1.5f, 3.4f), new Vector3(0.6f, 3, 0.6f), SceneLook.Plates, 0.25f);
        SceneLook.WorldBox("CrateA", null, new Vector3(1.6f, 0.5f, 0.6f), Vector3.one, SceneLook.Plates, 0.25f);
        SceneLook.WorldBox("CrateB", null, new Vector3(2.6f, 0.35f, 1.6f), new Vector3(1.4f, 0.7f, 1.0f), SceneLook.Accent, 1.2f);

        var stand = new GameObject("Character").transform;
        stand.rotation = Quaternion.Euler(0, 120f, 0);
        var character = CharacterKit.AddTo(stand);
        character.WeaponModels[3].SetActive(true);

        var display = CharacterKit.InstantiateWeapon(CharacterKit.Weapons[2]);
        display.name = "WeaponDisplay";
        display.transform.rotation = Quaternion.Euler(0, 90, 0) * display.transform.rotation;
        var bounds = SceneLook.WorldBounds(display);
        display.transform.position += new Vector3(1.6f, 1.0f, 0.6f) - bounds.center + Vector3.up * bounds.extents.y;

        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 50;
        SceneLook.EnablePost(cam);

        var wideFrom = new Vector3(2.5f, 1.6f, -5.5f);
        var wideAt = new Vector3(0.3f, 1.1f, 0.5f);
        SceneLook.Shot(cam, OutDir + "/lookdev_wide.png", wideFrom, wideAt);
        SceneLook.Shot(cam, OutDir + "/lookdev_character.png", new Vector3(0.6f, 1.45f, -2.4f), new Vector3(0, 1.1f, 0));
        SceneLook.Shot(cam, OutDir + "/lookdev_weapon.png", new Vector3(1.9f, 1.45f, -0.5f), SceneLook.WorldBounds(display).center);
        cam.transform.position = wideFrom;
        cam.transform.LookAt(wideAt);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[LookDevBuilder] Saved " + ScenePath + " and screenshots in " + OutDir);
        EditorApplication.Exit(0);
    }
}

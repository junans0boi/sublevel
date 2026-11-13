using Game.Gameplay;
using UnityEditor.SceneManagement;
using UnityEngine;

// Melee weapon icons (ADR-073): the code-built models (MeleeMeshes) from the side like the guns, the handle on the
// left and the head on the right, same 256x128 frame, lighting and transparent background as the gun icons.
public static partial class ItemIconBuilder
{
    static void BuildMelee()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var oldAmbientMode = RenderSettings.ambientMode;
        var oldAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.46f, 0.47f, 0.50f);

        var camGo = new GameObject("IconCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 20f;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.aspect = WeaponW / (float)WeaponH;

        var key = MakeLight("Key", new Vector3(-0.55f, -0.75f, 0.35f), new Color(1f, 0.96f, 0.9f), 1.2f);
        var fill = MakeLight("Fill", new Vector3(-0.6f, -0.15f, -0.7f), new Color(0.75f, 0.85f, 1f), 0.6f);
        var materials = MeleeKit.Materials;
        try
        {
            foreach (int id in MeleeMeshes.Ids)
            {
                var go = MeleeKit.Instantiate(id, materials);
                go.transform.rotation = MeleeKit.IconRotation(id);
                var b = SceneLook.WorldBounds(go);
                // Camera on +X looking toward -X: +Z is the right of the image.
                camGo.transform.position = b.center + new Vector3(5f, 0f, 0f);
                camGo.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                const float margin = 1.16f;
                float halfH = b.size.y * margin * 0.5f;
                float halfW = b.size.z * margin * 0.5f;
                cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect);
                var pixels = RenderTransparent(cam, WeaponW * WeaponSuper, WeaponH * WeaponSuper, WeaponSuper);
                Save(pixels, WeaponW, WeaponH, "weapon_" + id);
                Object.DestroyImmediate(go);
            }
        }
        finally
        {
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(key);
            Object.DestroyImmediate(fill);
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbient;
        }
    }
}

using Game.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Gameplay
{
    // What the tree and grass renderers share (ADR-070): which cameras they draw for, the graphics level, and
    // instanced draws in chunks. They draw from RenderPipelineManager.beginCameraRendering, once per camera, so a
    // scope camera with a narrow field of view gets its own levels of detail and an overlay camera gets nothing.
    public static class VegetationDraw
    {
        // Instances per draw call (the constant-buffer limit of instanced draws).
        public const int Chunk = 1023;

        // 0 low, 1 medium, 2 high: from the quality preset, or the view-distance row under 사용자 설정.
        public static int Level => Vegetation.Level(GameSettings.GetIndex(SettingId.QualityPreset), GameSettings.GetIndex(SettingId.ViewDistance));

        public static bool Draws(Camera cam, int layer)
        {
            if (cam == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return false;
            if ((cam.cullingMask & (1 << layer)) == 0) return false;
            // Overlay cameras (the scope eyepiece) draw only their own layer on top of the base camera's picture.
            return !cam.TryGetComponent<UniversalAdditionalCameraData>(out var data) || data.renderType != CameraRenderType.Overlay;
        }

        // How far the camera draws vegetation: its far clip, or the end of the linear fog if that comes first
        // (beyond it everything is fog colour anyway).
        public static float Reach(Camera cam)
        {
            float far = cam.farClipPlane;
            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogEndDistance > 0f)
                far = Mathf.Min(far, RenderSettings.fogEndDistance + 20f);
            return far;
        }

        public static void Draw(in RenderParams rp, Mesh mesh, int submesh, Matrix4x4[] matrices, int count)
        {
            for (int start = 0; start < count; start += Chunk)
                Graphics.RenderMeshInstanced(rp, mesh, submesh, matrices, Mathf.Min(Chunk, count - start), start);
        }

        public static void Append(ref Matrix4x4[] into, ref int count, in Matrix4x4 m)
        {
            if (count == into.Length) System.Array.Resize(ref into, Mathf.Max(64, into.Length * 2));
            into[count++] = m;
        }
    }
}

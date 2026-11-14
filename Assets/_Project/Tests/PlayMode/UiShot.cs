using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Game.Tests
{
    // Saves the screen, overlay UI included, at a chosen size: batchmode has no game view, so every overlay canvas
    // is drawn through a camera into a texture for one frame and put back.
    public static class UiShot
    {
        // `whileShown` runs with the UI laid out at that size (CanvasScaler updates a frame later).
        public static IEnumerator Save(string path, int width, int height, Action whileShown = null)
        {
            var go = new GameObject("UiShotCamera");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) { cam.CopyFrom(main); cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation); } // CopyFrom keeps the settings, not the place (#172: shots of a player's view)
            else cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            // Higher sorting order nearer the camera: overlay order is not kept between camera canvases otherwise.
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Array.Sort(canvases, (a, b) => b.sortingOrder.CompareTo(a.sortingOrder));
            float distance = cam.nearClipPlane + 0.05f;
            var changed = new System.Collections.Generic.List<Canvas>();
            foreach (var c in canvases)
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = distance;
                distance += 0.01f;
                changed.Add(c);
            }
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply();
            RenderTexture.active = prev;
            whileShown?.Invoke();
            foreach (var c in changed)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.worldCamera = null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, shot.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(shot);
            UnityEngine.Object.Destroy(go);
        }
    }
}

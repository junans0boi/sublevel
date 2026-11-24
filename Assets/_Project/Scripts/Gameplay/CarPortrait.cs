using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Gameplay
{
    // The race room's car pictures (#514): the race's own car prefab (LobbyStage.CarPrefabs, set by the builder) painted the way the race
    // paints it (Vehicle.PaintedMaterial), seen 3/4 from the front right under a neutral two-light studio on a transparent background.
    // Rendered once per (model, paint) far below the world on a layer no other camera draws, by a camera that renders only on demand,
    // then kept as a texture. Null when the scene has no car prefabs (the caller draws its flat silhouette instead).
    public static class CarPortrait
    {
        public const int Width = 640, Height = 312;   // the silhouettes' 160:78
        const int Layer = 31;
        static readonly Vector3 Spot = new Vector3(0f, -5000f, 0f);
        static readonly Dictionary<(string, int), Texture2D> Cache = new Dictionary<(string, int), Texture2D>();
        static Sprite _shadow;

        public static int Rendered => Cache.Count;

        // `paint` is a VehicleColors index (VehicleColors.None = the model's own colour).
        public static Texture2D Get(string model, int paint)
        {
            if (Cache.TryGetValue((model, paint), out var tex) && tex != null) return tex;
            var prefab = Prefab(model);
            if (prefab == null) return null;
            return Cache[(model, paint)] = Render(prefab, paint);
        }

        static GameObject Prefab(string model)
        {
            var stage = Object.FindAnyObjectByType<LobbyStage>(FindObjectsInactive.Include);
            if (stage == null || stage.CarPrefabs == null) return null;
            foreach (var p in stage.CarPrefabs) if (p != null && p.name == model) return p;
            return null;
        }

        static Texture2D Render(GameObject prefab, int paint)
        {
            // A render-only copy: the prefab's visible meshes with the race's paint, none of its network or physics behaviours.
            var root = new GameObject("CarPortrait") { layer = Layer };
            root.transform.position = Spot;
            var bounds = new Bounds(Spot, Vector3.zero);
            bool any = false;
            foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (!mr.enabled || mf == null || mf.sharedMesh == null || !ActiveUnder(mr.transform, prefab.transform)) continue;
                var part = new GameObject(mr.name) { layer = Layer };
                part.transform.SetParent(root.transform, false);
                var m = prefab.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = part.AddComponent<MeshRenderer>();
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Vehicle.PaintedMaterial(mats[i], paint);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
            }

            // A soft contact shadow on the ground under the car.
            var shadow = new GameObject("Shadow") { layer = Layer };
            shadow.transform.SetParent(root.transform, false);
            shadow.transform.position = new Vector3(bounds.center.x, bounds.min.y + 0.01f, bounds.center.z);
            shadow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localScale = new Vector3(bounds.size.x * 1.25f, bounds.size.z * 1.12f, 1f);
            var sr = shadow.AddComponent<SpriteRenderer>();
            sr.sprite = ShadowSprite();
            sr.color = new Color(0f, 0f, 0f, 0.55f);

            // 3/4 from the front right (the front shows on the right, as the silhouettes face), a little above.
            var camGo = new GameObject("CarPortraitCamera") { layer = Layer };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.allowHDR = false;
            cam.allowMSAA = false;   // the pipeline's sample count would have to match the target's: 2x supersampling instead
            var rot = Quaternion.Euler(14f, 180f + 38f, 0f);   // looking back at the car's front-right corner
            camGo.transform.rotation = rot;
            // Fit: every bounds corner (and the shadow's) inside the frame with a small margin, centred.
            Vector3 c = bounds.center, e = bounds.extents;
            var inv = Quaternion.Inverse(rot);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            void Take(Vector3 world)
            {
                var v = inv * (world - c);
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            for (int i = 0; i < 8; i++) Take(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            var sb = sr.bounds;
            for (int i = 0; i < 4; i++) Take(new Vector3((i & 1) == 0 ? sb.min.x : sb.max.x, sb.min.y, (i & 2) == 0 ? sb.min.z : sb.max.z));
            float aspect = Width / (float)Height, margin = 1.06f;
            cam.aspect = aspect;
            cam.orthographicSize = Mathf.Max((maxY - minY) / 2f, (maxX - minX) / 2f / aspect) * margin;
            camGo.transform.position = c + rot * new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, -30f);
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 60f;

            // Studio light: a warm-neutral key from the camera's upper left, a cool fill from the right, a flat grey ambient, no fog.
            var key = AddLight(camGo.transform, "Key", rot * Quaternion.Euler(28f, 32f, 0f), 1.25f, new Color(1f, 0.97f, 0.92f));
            var fill = AddLight(camGo.transform, "Fill", rot * Quaternion.Euler(8f, -55f, 0f), 0.45f, new Color(0.85f, 0.9f, 1f));
            var others = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l.type == LightType.Directional && l != key && l != fill) { l.enabled = false; others.Add(l); }
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            var sun = RenderSettings.sun;
            bool fog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.47f);
            RenderSettings.sun = key;
            RenderSettings.fog = false;

            const int W2 = Width * 2, H2 = Height * 2;
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(W2, H2, RenderTextureFormat.ARGB32, 24) { msaaSamples = 1, sRGB = true });
            cam.targetTexture = rt;
            try { cam.Render(); }
            finally
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.sun = sun;
                RenderSettings.fog = fog;
                foreach (var l in others) l.enabled = true;
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var big = new Texture2D(W2, H2, TextureFormat.RGBA32, false);
            big.ReadPixels(new Rect(0, 0, W2, H2), 0, 0, false);
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            // 2x2 box down to the portrait. The background is clear, so colours are averaged by coverage (straight alpha, no dark fringe).
            var src = big.GetPixels32();
            Object.Destroy(big);
            var px = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        var q = src[(y * 2 + (k >> 1)) * W2 + x * 2 + (k & 1)];
                        r += q.r * q.a; g += q.g * q.a; b += q.b * q.a; a += q.a;
                    }
                    px[y * Width + x] = a == 0 ? new Color32(0, 0, 0, 0) : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / 4));
                }
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, true) { name = "CarPortrait_" + prefab.name + "_" + paint, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true, true);

            root.SetActive(false);
            camGo.SetActive(false);
            Object.Destroy(root);
            Object.Destroy(camGo);
            return tex;
        }

        static Light AddLight(Transform parent, string name, Quaternion rotation, float intensity, Color color)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.rotation = rotation;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.color = color;
            l.shadows = LightShadows.None;
            return l;
        }

        static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        // A soft round shadow: dark in the middle, gone at the rim.
        static Sprite ShadowSprite()
        {
            if (_shadow != null) return _shadow;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 1.6f);
                }
            tex.SetPixels(px);
            tex.Apply();
            return _shadow = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        }
    }
}

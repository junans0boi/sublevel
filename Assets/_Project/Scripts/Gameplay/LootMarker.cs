using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // ADR-277: a coloured ring on the floor under every pickup, so a dark gun on a grey counter or a small box on
    // wood is found at a glance. It grows with distance (stays readable across a room), shows within ShowMeters, and
    // is bigger and pulsing on the one F would take. Code-built (no art, no prefab change): LootView adds it.
    public sealed class LootMarker : MonoBehaviour
    {
        const float ShowMeters = 28f, NearSize = 0.5f, FarSize = 2.2f, CurrentScale = 1.3f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Mesh _quad, _upright;
        static Camera _camera;

        Transform _ring, _dot;
        MeshRenderer _renderer, _dotRenderer;
        Color _tint = Color.white;
        Vector3 _ringAt;  // pickups do not move (no NetworkTransform): the ring's place, kept off the transform
        bool _shown = true;
        // #484: one LateUpdate walks every ring instead of ~1,600 behaviours each fetching the camera and the player.
        static readonly System.Collections.Generic.List<LootMarker> Live = new System.Collections.Generic.List<LootMarker>();
        static Ticker _ticker;
        // #452 (ADR-547): one shared material per sprite and colour (5 tints x current or not) instead of a property block,
        // which took every ring and glint out of the SRP Batcher (~7 SetPass with 16 pickups in view).
        static readonly System.Collections.Generic.Dictionary<(FxKit.Sprite, Color), Material> Tinted = new System.Collections.Generic.Dictionary<(FxKit.Sprite, Color), Material>();

        static Material TintedMaterial(FxKit.Sprite sprite, Color c)
        {
            if (Tinted.TryGetValue((sprite, c), out var m)) return m;
            m = new Material(FxKit.Material(sprite, false));
            m.SetColor(BaseColorId, c);
            return Tinted[(sprite, c)] = m;
        }

        // Flat 1 m square, facing up.
        static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "LootMarkerQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
            _quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            return _quad;
        }

        // The same 1 m square standing up (XY), for the glint that faces the viewer.
        static Mesh Upright()
        {
            if (_upright != null) return _upright;
            _upright = new Mesh { name = "LootMarkerUpright" };
            _upright.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
            _upright.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _upright.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _upright.RecalculateBounds();
            return _upright;
        }

        // The ring's colour by what lies here: guns orange, ammo yellow, healing green, armour and packs blue, the rest white.
        public static Color TintOf(LootKind kind)
        {
            switch (kind)
            {
                case LootKind.Weapon: return new Color(1f, 0.55f, 0.12f);
                case LootKind.Ammo: return new Color(1f, 0.9f, 0.25f);
                case LootKind.Medkit: case LootKind.FirstAidKit: case LootKind.Bandage: case LootKind.Drink: case LootKind.Pill: case LootKind.Syringe: case LootKind.Antidote:
                    return new Color(0.35f, 0.95f, 0.45f);
                case LootKind.Helmet: case LootKind.Vest: case LootKind.Backpack: return new Color(0.45f, 0.75f, 1f);
                default: return new Color(0.95f, 0.95f, 0.95f);
            }
        }

        public void Setup(LootKind kind, Vector3 floorPoint)
        {
            if (_ring == null)
            {
                var go = new GameObject("LootMarker");
                go.layer = gameObject.layer;
                _ring = go.transform;
                _ring.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                _renderer = go.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = FxKit.Material(FxKit.Sprite.Ring, false);
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                // A soft dot over it that always faces the viewer: the floor ring is a sliver from far and low, the dot is not.
                var d = new GameObject("LootGlint");
                d.layer = gameObject.layer;
                _dot = d.transform;
                _dot.SetParent(transform, false);
                d.AddComponent<MeshFilter>().sharedMesh = Upright();
                _dotRenderer = d.AddComponent<MeshRenderer>();
                _dotRenderer.sharedMaterial = FxKit.Material(FxKit.Sprite.Dot, false);
                _dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _dotRenderer.receiveShadows = false;
            }
            _tint = TintOf(kind);
            _ringAt = floorPoint + Vector3.up * 0.015f;
            _ring.position = _ringAt;
            _ring.rotation = Quaternion.identity;
            _dot.position = floorPoint + Vector3.up * 0.4f;
        }

        void OnEnable()
        {
            Live.Add(this);
            if (_ticker != null) return;
            _ticker = new GameObject("LootMarkers").AddComponent<Ticker>();
            DontDestroyOnLoad(_ticker.gameObject); // outlives scene swaps; idle while no ring is live
        }

        void OnDisable() => Live.Remove(this);

        sealed class Ticker : MonoBehaviour
        {
            void LateUpdate()
            {
                if (Live.Count == 0) return;
                if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
                if (_camera == null) return;
                var viewer = _camera.transform.position;
                var player = PlayerController.Local;
                var nearby = player != null ? player.NearbyLoot : null;
                var current = nearby != null ? nearby.gameObject : null;
                for (int i = 0; i < Live.Count; i++)
                {
                    var m = Live[i];
                    if (m._ring == null) continue;
                    // Far and already hidden: nothing to touch (most of the ~1,600 on any frame).
                    if (!m._shown && (viewer - m._ringAt).sqrMagnitude >= ShowMeters * ShowMeters) continue;
                    m.Tick(viewer, current != null && ReferenceEquals(m.gameObject, current));
                }
            }

            void OnDestroy() { if (_ticker == this) _ticker = null; }
        }

        // Size, colour and visibility for a viewer at `viewer` (also called by the editor captures, which run no LateUpdate).
        public void Tick(Vector3 viewer, bool current)
        {
            float distance = Vector3.Distance(viewer, _ringAt);
            bool show = distance < ShowMeters;
            if (_shown != show || _renderer.enabled != show) { _renderer.enabled = show; _dotRenderer.enabled = show; _shown = show; }
            if (!show) return;
            float size = Mathf.Lerp(NearSize, FarSize, distance / ShowMeters);
            float pulse = current ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
            _ring.localScale = Vector3.one * (size * (current ? CurrentScale + 0.12f * pulse : 1f));
            var c = _tint;
            c.a = current ? 1f : 0.9f;
            var ring = TintedMaterial(FxKit.Sprite.Ring, c);
            if (_renderer.sharedMaterial != ring) _renderer.sharedMaterial = ring;
            // The glint: 0.2 m near, growing with distance so it keeps about the same size on screen from 5 m on.
            var toViewer = viewer - _dot.position;
            if (toViewer.sqrMagnitude > 1e-4f) _dot.rotation = Quaternion.LookRotation(-toViewer);
            _dot.localScale = Vector3.one * Mathf.Max(0.2f, 0.05f * distance) * (current ? 1.3f : 1f);
            c.a = current ? 1f : 0.85f;
            var dot = TintedMaterial(FxKit.Sprite.Dot, c);
            if (_dotRenderer.sharedMaterial != dot) _dotRenderer.sharedMaterial = dot;
        }
    }
}

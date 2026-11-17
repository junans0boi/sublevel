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
        const float ShowMeters = 28f, NearSize = 0.5f, FarSize = 1.4f, CurrentScale = 1.3f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Mesh _quad;
        static Camera _camera;

        Transform _ring;
        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        Color _tint = Color.white;

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
                _block = new MaterialPropertyBlock();
            }
            _tint = TintOf(kind);
            _ring.position = floorPoint + Vector3.up * 0.015f;
            _ring.rotation = Quaternion.identity;
        }

        void LateUpdate()
        {
            if (_ring == null) return;
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera == null) return;
            var player = PlayerController.Local;
            Tick(_camera.transform.position, player != null && player.NearbyLoot != null && player.NearbyLoot.gameObject == gameObject);
        }

        // Size, colour and visibility for a viewer at `viewer` (also called by the editor captures, which run no LateUpdate).
        public void Tick(Vector3 viewer, bool current)
        {
            float distance = Vector3.Distance(viewer, _ring.position);
            bool show = distance < ShowMeters;
            if (_renderer.enabled != show) _renderer.enabled = show;
            if (!show) return;
            float size = Mathf.Lerp(NearSize, FarSize, distance / ShowMeters);
            float pulse = current ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
            _ring.localScale = Vector3.one * (size * (current ? CurrentScale + 0.12f * pulse : 1f));
            var c = _tint;
            c.a = current ? 1f : 0.8f;
            _block.SetColor(BaseColorId, c);
            _renderer.SetPropertyBlock(_block);
        }
    }
}

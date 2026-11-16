using Game.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The live character of the inventory screen (#179): a copy of the player's own model, bones and worn pieces
    // (helmet, vest, backpack) included, stands far below the world on its own layer and is drawn by a camera into a
    // RenderTexture the screen shows. Copied when the screen opens and again whenever what is worn changes; the
    // scripts, colliders and held weapons are left out. Drag on the picture to turn the character.
    public sealed class InventoryPreview : MonoBehaviour
    {
        const int PreviewLayer = 30;
        static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);
        const int TextureWidth = 454, TextureHeight = 950;
        const float DefaultYaw = 22f, CameraDistance = 3.55f, CameraHeight = 0.93f, FieldOfView = 30f;

        Camera _camera;
        RenderTexture _texture;
        GameObject _clone;
        GameObject _lights;
        float _yaw = DefaultYaw;
        int _key = -1;

        public RenderTexture Texture => _texture;
        public bool HasModel => _clone != null;
        // Drawn parts of the copy, for tests and captures.
        public int RendererCount { get; private set; }
        public Camera PreviewCamera => _camera;

        public static InventoryPreview Create(Transform parent)
        {
            var go = new GameObject("InventoryPreview");
            go.transform.SetParent(parent, false);
            return go.AddComponent<InventoryPreview>();
        }

        void Awake()
        {
            _texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "InventoryPreview" };
            _camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            _camera.transform.SetParent(transform, false);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.fieldOfView = FieldOfView;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 12f;
            _camera.allowHDR = false;
            _camera.targetTexture = _texture;
            _camera.enabled = false;
            var data = _camera.GetUniversalAdditionalCameraData();
            if (data != null) { data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None; }
            PlaceCamera();
        }

        void OnDestroy()
        {
            Clear();
            if (_texture != null) _texture.Release();
            if (_lights != null) Destroy(_lights);
        }

        void PlaceCamera()
        {
            _camera.transform.position = Stage + new Vector3(0f, CameraHeight, CameraDistance);
            _camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
        }

        // Opens: copy the model, light it, draw every frame.
        public void Show(Combatant me)
        {
            _yaw = DefaultYaw;
            Rebuild(me);
            _camera.enabled = true;
        }

        public void Hide()
        {
            if (_camera != null) _camera.enabled = false;
            Clear();
            if (_lights != null) { Destroy(_lights); _lights = null; }
        }

        // Draws once now (tests and captures; a batch mode editor does not always run the camera on its own).
        public void RenderNow()
        {
            if (_camera == null || _clone == null) return;
            _camera.Render();
        }

        public void Spin(float degrees)
        {
            _yaw += degrees;
            if (_clone != null) _clone.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        // Copies the model again when the worn pieces changed (the key is whatever the caller says identifies them).
        public void Refresh(Combatant me, int key)
        {
            if (key == _key && _clone != null) return;
            Rebuild(me);
        }

        void Rebuild(Combatant me)
        {
            Clear();
            if (me == null) return;
            var animator = me.GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            // Under an inactive holder nothing of the copy wakes up before its scripts are removed.
            var holder = new GameObject("PreviewHolder");
            holder.SetActive(false);
            var clone = Instantiate(animator.gameObject, holder.transform);
            Strip(clone);
            clone.transform.SetParent(null, false);
            clone.transform.SetPositionAndRotation(Stage, Quaternion.Euler(0f, _yaw, 0f));
            clone.transform.localScale = Vector3.one;
            SetLayer(clone.transform, PreviewLayer);
            Destroy(holder);
            clone.SetActive(true);
            _clone = clone;
            RendererCount = ShowWornPieces(clone);
            var anim = clone.GetComponent<Animator>();
            if (anim != null) { anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.Update(0f); }
            EnsureLights();
            _key = KeyOf(me);
        }

        public static int KeyOf(Combatant me)
        {
            var gear = me != null ? me.GetComponent<CombatantGear>() : null;
            int key = gear != null ? gear.HelmetTier.Value * 16 + gear.VestTier.Value * 4 : 0;
            return key * 4 + InventoryBackend.BackpackTier(me);
        }

        void Clear()
        {
            if (_clone != null) Destroy(_clone);
            _clone = null;
            RendererCount = 0;
            _key = -1;
        }

        // Only the model itself, no behaviours or physics.
        static void Strip(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
            foreach (var light in root.GetComponentsInChildren<Light>(true)) DestroyImmediate(light);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        // The own eye view hides the body and the pieces: show them all, but not what is in the hand, and a mount only
        // when it holds a mesh (a piece that is not worn has none). Returns how many renderers are on.
        static int ShowWornPieces(GameObject clone)
        {
            var hand = BackpackVisual.Deep(clone.transform, "Wrist.R");
            int shown = 0;
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                bool inHand = hand != null && renderer.transform.IsChildOf(hand);
                bool on;
                if (inHand) on = false;
                else if (renderer is SkinnedMeshRenderer) on = true;
                else if (renderer is MeshRenderer)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    string n = renderer.name;
                    on = (n == "Helmet" || n == "Vest" || n == "Backpack") && filter != null && filter.sharedMesh != null;
                }
                else on = false;
                renderer.enabled = on;
                if (on)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    shown++;
                }
            }
            return shown;
        }

        // The scene's own light may be dim (night): three small lights around the character, far from the world.
        void EnsureLights()
        {
            if (_lights != null) return;
            _lights = new GameObject("PreviewLights");
            AddLight("Key", new Vector3(-1.6f, 2.3f, 2.4f), new Color(1f, 0.93f, 0.84f), 9f);
            AddLight("Fill", new Vector3(2.2f, 1.2f, 2.0f), new Color(0.62f, 0.74f, 1f), 4.5f);
            AddLight("Rim", new Vector3(0.6f, 2.0f, -2.2f), new Color(0.9f, 0.95f, 1f), 7f);
        }

        void AddLight(string name, Vector3 offset, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_lights.transform, false);
            go.transform.position = Stage + offset;
            go.layer = PreviewLayer;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 9f;
            light.shadows = LightShadows.None;
        }
    }

    // Dragging sideways over the picture turns the character.
    internal sealed class InventoryPreviewDrag : MonoBehaviour, IDragHandler
    {
        public InventoryPreview Preview;

        public void OnDrag(PointerEventData eventData)
        {
            if (Preview != null) Preview.Spin(-eventData.delta.x * 0.45f);
        }
    }
}

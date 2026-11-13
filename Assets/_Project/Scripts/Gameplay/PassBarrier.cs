using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Game.Gameplay
{
    // The barrier across one pass between two regions (ADR-058 (7)). It stands when either region is in lockdown or
    // beyond: bodies and bots stop at it, bullets, sight lines and the camera pass through (it sits on the built-in
    // Ignore Raycast layer, like the invisible sea wall). A player with a key next to it presses F to open it for
    // LockdownRules.KeyOpenSeconds. Runs on every side from what ZoneDirector knows; the server's copy is what
    // matters for the rules, the others are for the player's own body and the picture.
    public class PassBarrier : MonoBehaviour
    {
        public const int IgnoreRaycastLayer = 2;
        public const float Thickness = 2f;
        public const float Height = 12f;            // spans well below and above the pass floor (the ground slopes up the sides)
        public const float FloorDrop = 3f;          // the box starts this far below the pass floor
        public const float VisualHeight = 9f;       // the picture rises to this above the pass floor
        const float RiseSecondsFull = 1.5f;
        const string MaterialPath = "Lockdown/BarrierMaterial"; // optional: without it the barrier has no picture

        [SerializeField] int _pass;

        // The pass the local player could open right now (set each frame by that barrier), and the frame it was set.
        public static int PromptPass { get; private set; } = -1;
        public static int PromptFrame { get; private set; } = -1;
        public static bool PromptActive => PromptPass >= 0 && PromptFrame >= Time.frameCount - 1;

        static Mesh _boxMesh;

        BoxCollider _box;
        NavMeshObstacle _obstacle;
        Transform _visual;
        ZoneDirector _director;
        bool _blocking;
        float _rise;

        public int PassIndex => _pass;
        // Collider on: the barrier stops bodies right now.
        public bool Blocking => _blocking;
        public BoxCollider Box => _box;
        // 0..1: how far the picture has risen.
        public float Rise => _rise;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PromptPass = -1;
            PromptFrame = -1;
            _boxMesh = null;
        }

        // Puts a barrier on every pass of the world next to each match, so nothing in the scene refers to them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ZoneDirector.Started -= Install;
            ZoneDirector.Started += Install;
        }

        static void Install(ZoneDirector director)
        {
            var world = WorldInfo.Current;
            if (director == null || world == null || director.GetComponentInChildren<PassBarrier>() != null) return;
            var parent = new GameObject("PassBarriers").transform;
            // Under the director so they go away with the scene.
            parent.SetParent(director.transform, false);
            for (int i = 0; i < world.Passes.Count; i++) Create(parent, world.Passes[i], i, Resources.Load<Material>(MaterialPath));
        }

        // One barrier across a pass: collider on the Ignore Raycast layer, a navmesh obstacle for bots, and (when a
        // material is given) a rising slab as the picture.
        public static PassBarrier Create(Transform parent, WorldInfo.Pass pass, int index, Material material = null)
        {
            var go = new GameObject("PassBarrier" + index);
            go.layer = IgnoreRaycastLayer;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pass.Position, Quaternion.Euler(0f, pass.YawDeg, 0f)); // local +Z = the way through
            float width = pass.Width + 2f * WorldPass.Shoulder;
            var size = new Vector3(width, Height, Thickness);
            var center = new Vector3(0f, Height * 0.5f - FloorDrop, 0f);

            var barrier = go.AddComponent<PassBarrier>();
            barrier._pass = index;
            barrier._box = go.AddComponent<BoxCollider>();
            barrier._box.size = size;
            barrier._box.center = center;
            barrier._box.enabled = false;

            barrier._obstacle = go.AddComponent<NavMeshObstacle>();
            barrier._obstacle.shape = NavMeshObstacleShape.Box;
            barrier._obstacle.size = size;
            barrier._obstacle.center = center;
            barrier._obstacle.carving = true;
            barrier._obstacle.enabled = false;

            if (material != null)
            {
                var visual = new GameObject("Picture");
                visual.layer = IgnoreRaycastLayer;
                visual.transform.SetParent(go.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = BoxMesh();
                var renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                barrier._visual = visual.transform;
                barrier.ApplyRise();
            }
            return barrier;
        }

        void Awake()
        {
            _box = _box != null ? _box : GetComponent<BoxCollider>();
            _obstacle = _obstacle != null ? _obstacle : GetComponent<NavMeshObstacle>();
        }

        void Update()
        {
            if (_director == null) _director = ZoneDirector.Instance;
            if (_director == null) return;

            bool blocking = _director.IsPassBlocking(_pass);
            if (blocking != _blocking)
            {
                _blocking = blocking;
                if (_box != null) _box.enabled = blocking;
                if (_obstacle != null) _obstacle.enabled = blocking;
            }

            float target = _director.IsPassLocked(_pass) && !_director.IsPassOpen(_pass) ? 1f : 0f;
            if (_rise != target)
            {
                _rise = Mathf.MoveTowards(_rise, target, Time.deltaTime / RiseSecondsFull);
                ApplyRise();
            }

            if (_blocking) CheckKeyUse();
        }

        void ApplyRise()
        {
            if (_visual == null || _box == null) return;
            float h = Mathf.Max(0.01f, VisualHeight * _rise);
            _visual.localScale = new Vector3(_box.size.x, h, Thickness);
            _visual.localPosition = new Vector3(0f, h * 0.5f, 0f);
            if (_visual.gameObject.activeSelf != _rise > 0f) _visual.gameObject.SetActive(_rise > 0f);
        }

        // The local player next to a locked barrier with a key: F opens it.
        void CheckKeyUse()
        {
            var me = Combatant.LocalPlayer;
            var keys = KeyHolder.Local;
            if (me == null || keys == null || me.Dead.Value || keys.Keys.Value <= 0) return;
            if (_director.PassDistance(_pass, me.transform.position) > _director.Rules.PassInteractRange) return;
            PromptPass = _pass;
            PromptFrame = Time.frameCount;
            var keyboard = Keyboard.current;
            if (UiFocus.Current == UiScreen.None && keyboard != null && keyboard.fKey.wasPressedThisFrame) keys.CmdUseKey(_pass);
        }

        // A unit cube (24 vertices, flat normals), built once.
        static Mesh BoxMesh()
        {
            if (_boxMesh != null) return _boxMesh;
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uv = new Vector2[24];
            var triangles = new int[36];
            var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                var n = axes[f];
                var u = Vector3.Cross(n, Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up).normalized;
                var v = Vector3.Cross(n, u);
                int b = f * 4;
                vertices[b] = (n - u - v) * 0.5f; vertices[b + 1] = (n + u - v) * 0.5f;
                vertices[b + 2] = (n + u + v) * 0.5f; vertices[b + 3] = (n - u + v) * 0.5f;
                for (int k = 0; k < 4; k++) normals[b + k] = n;
                uv[b] = new Vector2(0f, 0f); uv[b + 1] = new Vector2(1f, 0f); uv[b + 2] = new Vector2(1f, 1f); uv[b + 3] = new Vector2(0f, 1f);
                int t = f * 6;
                // Unity draws the side a triangle's (second - first) x (third - first) points out of.
                triangles[t] = b; triangles[t + 1] = b + 1; triangles[t + 2] = b + 2;
                triangles[t + 3] = b; triangles[t + 4] = b + 2; triangles[t + 5] = b + 3;
            }
            _boxMesh = new Mesh { name = "PassBarrierBox", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            _boxMesh.RecalculateBounds();
            return _boxMesh;
        }
    }
}

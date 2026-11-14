using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // What the player sees and does with the supply crates (#144, ADR-113): the crate coming down on a line, the
    // coloured signal smoke over it (the smoke grenade's puffs and material, a tall column), the hold-F opening with
    // its prompt. Follows SupplyDirector's static events (which the director raises on remote clients too, ADR-131).
    // Installs itself under the director on the first crate; nothing in the scenes refers to it.
    public class SupplyView : MonoBehaviour
    {
        const int Puffs = 16;
        const float PlumeHeight = 40f, RiseSpeed = 0.1f, CrateWidth = 1.1f, CrateHeight = 0.75f;
        static readonly Color SmokeColor = new Color(1f, 0.36f, 0.05f, 0.85f);
        static readonly Color BodyColor = new Color(0.2f, 0.23f, 0.26f), LidColor = new Color(0.95f, 0.5f, 0.1f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        class Visual
        {
            public SupplyCrate Crate;
            public Transform Root;
            public MeshRenderer[] Smoke;
            public float[] Phase;
        }

        public static SupplyView Instance { get; private set; }

        readonly List<Visual> _visuals = new List<Visual>();
        MaterialPropertyBlock _block;
        Material _smokeMaterial, _bodyMaterial, _lidMaterial;
        Canvas _canvas;
        Text _prompt;
        RectTransform _fill;
        float _progress;
        int _opened;

        // For tests and captures.
        public int CrateCount => _visuals.Count;
        public int SmokePuffsOn
        {
            get
            {
                int n = 0;
                foreach (var v in _visuals) if (v.Smoke != null) foreach (var p in v.Smoke) if (p != null && p.enabled) n++;
                return n;
            }
        }
        public float Progress => _progress;
        public string Prompt => _prompt != null && _canvas.gameObject.activeSelf ? _prompt.text : "";
        public int Opened => _opened;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SupplyDirector.Dropped -= OnDropped;
            SupplyDirector.Dropped += OnDropped;
        }

        static void OnDropped(SupplyCrate crate)
        {
            var director = SupplyDirector.Instance;
            if (director == null) return;
            var view = director.GetComponentInChildren<SupplyView>();
            if (view == null)
            {
                var go = new GameObject("SupplyView");
                go.transform.SetParent(director.transform, false);
                view = go.AddComponent<SupplyView>();
            }
            view.Add(crate);
        }

        void Awake()
        {
            Instance = this;
            _block = new MaterialPropertyBlock();
        }

        void OnEnable() => SupplyDirector.Opened += OnOpened;
        void OnDisable() => SupplyDirector.Opened -= OnOpened;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var v in _visuals) if (v.Root != null) Destroy(v.Root.gameObject);
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void Add(SupplyCrate crate)
        {
            var root = new GameObject("SupplyCrate " + crate.Id).transform;
            root.SetParent(transform, false);
            Slab(root, "Body", new Vector3(CrateWidth, CrateHeight, CrateWidth * 0.75f), CrateHeight * 0.5f, BodyMaterial());
            Slab(root, "Lid", new Vector3(CrateWidth * 1.04f, 0.1f, CrateWidth * 0.8f), CrateHeight + 0.05f, LidMaterial());
            _visuals.Add(new Visual { Crate = crate, Root = root, Phase = new float[Puffs] });
            Place(_visuals[_visuals.Count - 1]);
        }

        void OnOpened(SupplyCrate crate)
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i].Crate != crate) continue;
                Destroy(_visuals[i].Root.gameObject);
                _visuals.RemoveAt(i);
                _opened++;
                return;
            }
        }

        static void Slab(Transform parent, string name, Vector3 size, float y, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            var box = cube.GetComponent<Collider>();
            box.enabled = false; // only to be seen: it never blocks movement or shots
            Destroy(box);
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = new Vector3(0f, y, 0f);
            cube.transform.localScale = size;
            if (material != null) cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Material Lit(string name, Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            var m = new Material(shader) { name = name };
            m.SetColor(BaseColorId, colour);
            return m;
        }

        Material BodyMaterial() => _bodyMaterial != null ? _bodyMaterial : _bodyMaterial = Lit("SupplyBody", BodyColor);
        Material LidMaterial() => _lidMaterial != null ? _lidMaterial : _lidMaterial = Lit("SupplyLid", LidColor);

        void Update()
        {
            for (int i = 0; i < _visuals.Count; i++) Place(_visuals[i]);
            UpdatePrompt();
        }

        // The crate on its line down (it lands linearly) and the smoke column, camera-facing discs rising and fading.
        void Place(Visual v)
        {
            var crate = v.Crate;
            var rules = SupplyRules.Current;
            float fall = Mathf.Clamp01((Time.time - crate.DroppedAt) / Mathf.Max(0.1f, rules.DescentSeconds));
            v.Root.position = crate.Position + Vector3.up * (rules.DropHeightMeters * (1f - fall));

            float age = Time.time - crate.DroppedAt;
            bool smoking = age < rules.SmokeSeconds;
            if (v.Smoke == null) BuildSmoke(v);
            if (v.Smoke == null) return;
            var camera = PlayerController.Local != null && PlayerController.Local.Camera != null ? PlayerController.Local.Camera.transform : null;
            var rotation = camera != null ? camera.rotation : Quaternion.identity;
            float radius = SmokeRules.Current.Radius;
            float fade = Mathf.Clamp01((rules.SmokeSeconds - age) / 5f) * Mathf.Clamp01(age / 1.5f);
            for (int p = 0; p < Puffs; p++)
            {
                var puff = v.Smoke[p];
                puff.enabled = smoking;
                if (!smoking) continue;
                float phase = Mathf.Repeat(Time.time * RiseSpeed + p / (float)Puffs, 1f);
                float h = phase * PlumeHeight;
                var sway = new Vector3(Mathf.Sin(p * 2.3f + Time.time * 0.4f), 0f, Mathf.Cos(p * 1.7f + Time.time * 0.35f)) * (phase * radius * 0.5f);
                puff.transform.SetPositionAndRotation(crate.Position + Vector3.up * (h + 0.6f) + sway, rotation);
                puff.transform.localScale = Vector3.one * (radius * Mathf.Lerp(0.5f, 1.5f, phase));
                var colour = SmokeColor;
                colour.a *= Mathf.Sqrt(Mathf.Sin(phase * Mathf.PI)) * fade;
                _block.SetColor(BaseColorId, colour);
                puff.SetPropertyBlock(_block);
            }
        }

        // Needs the smoke grenade's material, which hangs on the player's ThrowableView; tried again each frame until it is there.
        void BuildSmoke(Visual v)
        {
            if (_smokeMaterial == null)
            {
                var player = Combatant.LocalPlayer;
                var view = player != null ? player.GetComponent<ThrowableView>() : null;
                _smokeMaterial = view != null ? view.CloudMaterial : null;
            }
            if (_smokeMaterial == null) return;
            v.Smoke = new MeshRenderer[Puffs];
            for (int p = 0; p < Puffs; p++)
            {
                var go = new GameObject("SignalPuff");
                go.transform.SetParent(v.Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = CombatFxMeshes.Disc;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _smokeMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                v.Smoke[p] = r;
            }
        }

        // ---- Opening: F held ----

        // Called every frame by the player: true while the key belongs to the crate (held a moment next to a landed one).
        public static bool HandleInteract(Combatant me, bool held, float dt)
        {
            var view = Instance;
            var director = SupplyDirector.Instance;
            if (view == null || director == null || me == null || me.Dead.Value) { if (view != null) view._progress = 0f; return false; }
            var crate = director.NearestOpenable(me.transform.position);
            float seconds = SupplyRules.Current.OpenSeconds;
            float before = view._progress;
            view._progress = crate == null ? 0f : SupplyPlan.Hold(before, held, true, dt, seconds);
            if (crate != null && view._progress >= 1f)
            {
                director.RequestOpen(crate, me); // a remote client asks the server
                view._progress = 0f;
                return true;
            }
            // A tap (no progress yet) still picks things up; a hold is the crate's.
            return view._progress > 0.05f;
        }

        void UpdatePrompt()
        {
            var me = Combatant.LocalPlayer;
            var director = SupplyDirector.Instance;
            SupplyCrate near = null;
            if (me != null && director != null && !me.Dead.Value)
                foreach (var c in director.Crates) if (director.InReach(c, me.transform.position)) { near = c; break; }
            if (near == null)
            {
                if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                return;
            }
            if (_canvas == null) BuildPrompt();
            _canvas.gameObject.SetActive(true);
            bool landed = Time.time >= near.LandsAt;
            _prompt.text = landed ? "F 길게  보급 상자 열기" : "보급 상자 내려오는 중";
            _fill.anchorMax = new Vector2(landed ? _progress : 0f, 1f);
        }

        void BuildPrompt()
        {
            _canvas = UiKit.Canvas("SupplyPrompt", 60, false);
            _canvas.transform.SetParent(transform, false);
            var root = _canvas.transform;
            var centre = new Vector2(0.5f, 0.5f);
            var bar = UiKit.Rect("Bar", root, centre, centre, new Vector2(0f, -150f), new Vector2(360f, 10f));
            UiKit.Box(bar, new Color(0f, 0f, 0f, 0.6f));
            _fill = UiKit.Fill("Fill", bar);
            UiKit.Box(_fill, UiKit.Accent);
            var text = UiKit.Rect("Text", root, centre, centre, new Vector2(0f, -120f), new Vector2(600f, 36f));
            _prompt = UiKit.Label(text, "", 26, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }
}

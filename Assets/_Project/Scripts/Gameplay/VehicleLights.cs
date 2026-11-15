using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // The lamps of a car or motorcycle (#202, ADR-155), on every side from replicated state:
    //  - headlamps (a pair; one on a motorcycle): a glow on the lens, a real spot light and a faint beam cone so the
    //    light shows in the dark air; on by themselves at dusk / night / rain / fog while the engine runs, L toggles;
    //  - tail lamps (dim) and brake lamps (bright, while slowing down hard or on the handbrake), reverse lamps,
    //    and amber indicators that blink while the wheels are held over at low speed (cars).
    // The glow discs are placed on the body mesh's own surface (raycast from the front / rear at the lamp's height
    // and side), so they follow every model without per-model data. Real lights are a budget: only the nearest
    // MaxSpotVehicles vehicles with lamps lit get spot lights, the rest keep the glow. Added at run time by
    // VehicleAudio (the vehicle prefabs have it): nothing in the prefabs changes. Runs after Vehicle placed things.
    [DefaultExecutionOrder(310)]
    public class VehicleLights : MonoBehaviour
    {
        public const int MaxSpotVehicles = 3;
        const float SpotRange = 34f, SpotAngle = 64f, SpotInner = 24f, SpotIntensity = 90f;
        const float ConeLength = 15f, ConeRadius = 3.6f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color HeadColor = new Color(1f, 0.93f, 0.78f), TailColor = new Color(1f, 0.06f, 0.03f),
            ReverseColor = new Color(0.9f, 0.95f, 1f), IndicatorColor = new Color(1f, 0.55f, 0.05f);

        static readonly List<VehicleLights> All = new List<VehicleLights>();
        static float _rankedAt = -10f;
        static Mesh _cone;

        enum Kind { Head, Tail, Brake, Reverse, Indicator }

        sealed class Lamp
        {
            public Kind Kind;
            public int Side;                 // -1 left, +1 right (vehicle's own left)
            public bool Rear;
            public Transform Root;
            public MeshRenderer[] Renderers;
            public float Shown = -1f;
        }

        Vehicle _vehicle;
        VehicleHorn _switch;
        VehicleDamage _damage;
        VehicleDamageVisual _visual;
        Material _glow;
        MaterialPropertyBlock _block;
        readonly List<Lamp> _lamps = new List<Lamp>();
        readonly List<Light> _spots = new List<Light>();
        readonly List<MeshRenderer> _cones = new List<MeshRenderer>();
        bool _built, _noGlow;
        bool _lit, _braking, _reversing, _spotsAllowed;
        int _indicator;
        float _brakeUntil;

        // For tests and captures.
        public bool Lit => _lit;
        public bool Braking => _braking;
        public bool Reversing => _reversing;
        public int Indicator => _indicator;
        public int LampCount => _lamps.Count;
        public int SpotsOn { get { int n = 0; foreach (var l in _spots) if (l != null && l.enabled) n++; return n; } }
        public int SpotCount => _spots.Count;
        public static int SpotVehicles { get { int n = 0; foreach (var v in All) if (v != null && v.SpotsOn > 0) n++; return n; } }
        public byte Mode => _switch != null ? _switch.Lights.Value : VehicleFxRules.LightsAuto;
        // Tests force the day / night read (null = the scene's sky).
        public static float? AmbientOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); AmbientOverride = null; _cone = null; _rankedAt = -10f; }

        void Awake()
        {
            _vehicle = GetComponent<Vehicle>();
            _switch = GetComponent<VehicleHorn>();
            _damage = GetComponent<VehicleDamage>();
            _visual = GetComponent<VehicleDamageVisual>();
            _block = new MaterialPropertyBlock();
        }

        void OnEnable() => All.Add(this);

        void OnDisable() => All.Remove(this);

        static bool Auto(out float dark01)
        {
            var env = EnvironmentView.Current;
            float ambient = AmbientOverride ?? (env != null ? env.Look.Ambient : 1f);
            dark01 = 1f - Mathf.Clamp01(ambient);
            bool dim = env != null && env.Weather != WeatherKind.Clear && AmbientOverride == null;
            return VehicleFxRules.AutoLights(ambient, dim);
        }

        // What a press of L asks the server for.
        public byte NextMode() => VehicleFxRules.ToggleMode(Mode, Auto(out _), _vehicle.EngineOn.Value);

        void LateUpdate()
        {
            if (!_built)
            {
                if (_visual == null || _visual.BodyShell == null) return;
                Build();
            }
            bool auto = Auto(out float dark);
            bool engine = _vehicle.EngineOn.Value && _damage.CurrentStage < VehicleDamageStage.Burning;
            _lit = VehicleFxRules.LightsLit(Mode, auto, engine);
            float speed = _vehicle.Speed, accel = _vehicle.ShownAccel;
            if (engine && VehicleFxRules.Braking(speed, accel, false)) _brakeUntil = Time.time + 0.25f;
            _braking = engine && Time.time < _brakeUntil;
            _reversing = engine && VehicleFxRules.Reversing(speed);
            _indicator = engine && !_vehicle.IsMotorcycle ? VehicleFxRules.Indicator(_vehicle.SteerShown, speed) : 0;
            bool blink = VehicleFxRules.BlinkOn(Time.time);
            // By day the glow is faint (a bright disc on bright paint looks like a decal); the dark brings it up.
            float strength = Mathf.Lerp(0.55f, 1f, dark);
            foreach (var lamp in _lamps)
            {
                float v = 0f;
                switch (lamp.Kind)
                {
                    case Kind.Head: v = _lit ? 1f : 0f; break;
                    case Kind.Tail: v = _lit ? 0.35f : 0f; break;
                    case Kind.Brake: v = _braking ? 1.2f : 0f; break;
                    case Kind.Reverse: v = _reversing ? 0.9f : 0f; break;
                    case Kind.Indicator: v = _indicator == lamp.Side && blink ? 1f : 0f; break;
                }
                Show(lamp, v * (lamp.Kind == Kind.Indicator ? 1f : strength));
            }
            // Real lights and the beam cones, within the budget.
            Rank();
            foreach (var l in _spots) if (l != null) l.enabled = _lit && _spotsAllowed;
            bool cones = _lit && dark > 0.2f;
            foreach (var c in _cones)
            {
                if (c == null) continue;
                c.enabled = cones;
                if (cones)
                {
                    c.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, new Color(HeadColor.r, HeadColor.g, HeadColor.b, 0.32f * dark));
                    c.SetPropertyBlock(_block);
                }
            }
        }

        // The vehicles with lit lamps nearest the camera get the real lights (re-ranked ~4x a second).
        static void Rank()
        {
            if (Time.time - _rankedAt < 0.25f) return;
            _rankedAt = Time.time;
            var cam = PlayerController.Local != null ? PlayerController.Local.Camera : null;
            var from = cam != null ? cam.transform.position : Vector3.zero;
            var lit = new List<VehicleLights>();
            foreach (var v in All) if (v != null && v._built && v._lit) lit.Add(v);
            var d = new float[lit.Count];
            for (int i = 0; i < d.Length; i++) d[i] = (lit[i].transform.position - from).sqrMagnitude;
            var allowed = VehicleFxRules.NearestAllowed(d, MaxSpotVehicles);
            foreach (var v in All) if (v != null) v._spotsAllowed = false;
            for (int i = 0; i < lit.Count; i++) lit[i]._spotsAllowed = allowed[i];
        }

        void Show(Lamp lamp, float v)
        {
            if (Mathf.Approximately(v, lamp.Shown)) return;
            lamp.Shown = v;
            foreach (var r in lamp.Renderers)
            {
                r.enabled = v > 0.01f;
                if (!r.enabled) continue;
                var c = lamp.Kind switch { Kind.Head => HeadColor, Kind.Reverse => ReverseColor, Kind.Indicator => IndicatorColor, _ => TailColor };
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, new Color(c.r * v, c.g * v, c.b * v, Mathf.Clamp01(v)));
                r.SetPropertyBlock(_block);
            }
        }

        // ---- Building ----

        void Build()
        {
            _built = true;
            var view = FindAnyObjectByType<ThrowableView>();
            _glow = view != null ? view.GlowMaterial : null;
            _noGlow = _glow == null;
            var shell = _visual.BodyShell;
            var b = _damage.LocalBounds();
            bool bike = _vehicle.IsMotorcycle;
            var headRoot = new GameObject("Headlamps").transform;
            headRoot.SetParent(transform, false);
            foreach (int side in bike ? new[] { 0 } : new[] { -1, 1 })
            {
                // Where the model's own lamp lenses are (its "Lights" material), else a guess on the body's surface.
                if (!Cluster(shell, b, true, side, bike, out var c)) c = Guess(shell, b, true, side, bike);
                AddLamp(Kind.Head, side, false, c.At, c.Normal, c.Size);
                AddSpot(headRoot, c.At + Vector3.forward * 0.15f);
                AddCone(headRoot, c.At + Vector3.forward * 0.1f);
                if (!bike) AddLamp(Kind.Indicator, side, false, c.Outer, c.Normal, 0.14f);
            }
            foreach (int side in bike ? new[] { 0 } : new[] { -1, 1 })
            {
                if (!Cluster(shell, b, false, side, bike, out var c)) c = Guess(shell, b, false, side, bike);
                AddLamp(Kind.Tail, side, true, c.At, c.Normal, c.Size * 0.9f);
                AddLamp(Kind.Brake, side, true, c.At, c.Normal, c.Size * 1.5f);
                if (bike) continue;
                AddLamp(Kind.Reverse, side, true, Vector3.Lerp(c.At, new Vector3(0f, c.At.y, c.At.z), 0.45f), c.Normal, c.Size * 0.6f);
                AddLamp(Kind.Indicator, side, true, c.Outer, c.Normal, 0.14f);
            }
        }

        struct Spot
        {
            public Vector3 At, Normal, Outer;
            public float Size;
        }

        // The lamp lenses of one end and side, from the vertices of the body's lamp material ("...Lights"): their
        // centre, the most outward point and a size. False when the model has no such material or nothing on that end.
        bool Cluster(VehicleDamageVisual.Shell shell, Bounds b, bool front, int side, bool bike, out Spot spot)
        {
            spot = default;
            var mf = shell.Filter;
            var mr = mf.GetComponent<MeshRenderer>();
            var mesh = mf.sharedMesh;
            if (mr == null || mesh == null || !mesh.isReadable) return false;
            var mats = mr.sharedMaterials;
            var verts = mesh.vertices;
            Vector3 sum = Vector3.zero;
            int count = 0;
            float zMost = front ? float.MinValue : float.MaxValue, xOut = 0f, minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int m = 0; m < mats.Length && m < mesh.subMeshCount; m++)
            {
                if (mats[m] == null || mats[m].name.IndexOf("Lights", System.StringComparison.OrdinalIgnoreCase) < 0 || mats[m].name.EndsWith("_Glass")) continue;
                foreach (int vi in mesh.GetTriangles(m))
                {
                    var p = transform.InverseTransformPoint(mf.transform.TransformPoint(verts[vi]));
                    bool end = front ? p.z > b.center.z + b.size.z * 0.3f : p.z < b.center.z - b.size.z * 0.3f;
                    if (!end) continue;
                    if (bike ? Mathf.Abs(p.x) > b.size.x * 0.3f : (side < 0 ? p.x >= 0f : p.x <= 0f)) continue;
                    sum += p; count++;
                    zMost = front ? Mathf.Max(zMost, p.z) : Mathf.Min(zMost, p.z);
                    if (Mathf.Abs(p.x) > Mathf.Abs(xOut)) xOut = p.x;
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
            }
            if (count < 6) return false;
            var c = sum / count;
            var n = front ? Vector3.forward : Vector3.back;
            float width = maxX - minX, height = maxY - minY;
            spot = new Spot
            {
                At = new Vector3(c.x, c.y, zMost) + n * 0.015f,
                Normal = n,
                Outer = new Vector3(xOut * 0.97f, c.y + height * 0.25f, zMost) + n * 0.015f,
                Size = Mathf.Clamp(Mathf.Max(width, height) * 1.1f, 0.18f, 0.5f),
            };
            return true;
        }

        Spot Guess(VehicleDamageVisual.Shell shell, Bounds b, bool front, int side, bool bike)
        {
            float x = bike ? 0f : side * b.extents.x * (front ? 0.62f : 0.68f);
            float y = b.min.y + b.size.y * (front ? (bike ? 0.55f : 0.40f) : 0.50f);
            float z = front ? b.max.z + 0.6f : b.min.z - 0.6f;
            var n = front ? Vector3.forward : Vector3.back;
            var spot = new Spot { At = new Vector3(x, y, front ? b.max.z : b.min.z), Normal = n, Size = 0.3f };
            if (Surface(shell, new Vector3(x, y, z), -n, out var p, out var sn)) { spot.At = p; spot.Normal = sn; }
            spot.Outer = spot.At + new Vector3(side * 0.15f, 0.06f, 0f);
            return spot;
        }

        // The body surface met by a ray along vehicle-local `dir` from `from`; point and normal (vehicle space, offset out).
        bool Surface(VehicleDamageVisual.Shell shell, Vector3 from, Vector3 dir, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            var o = shell.FromVehicle(transform, from);
            var d = shell.DirFromVehicle(transform, dir);
            if (!shell.Ray(shell.SolidTris, o, d, 6f, out float t, out var n)) return false;
            var world = shell.Filter.transform;
            point = transform.InverseTransformPoint(world.TransformPoint(o + d * t));
            normal = transform.InverseTransformDirection(world.TransformDirection(n)).normalized;
            point += normal * 0.012f;
            return true;
        }

        void AddLamp(Kind kind, int side, bool rear, Vector3 at, Vector3 normal, float size)
        {
            var lamp = new Lamp { Kind = kind, Side = side, Rear = rear, Renderers = new MeshRenderer[0] };
            var root = new GameObject(kind + "Lamp").transform;
            root.SetParent(transform, false);
            root.localPosition = at;
            root.localRotation = Quaternion.LookRotation(normal);
            root.localScale = Vector3.one * size;
            lamp.Root = root;
            if (!_noGlow)
            {
                var list = new List<MeshRenderer>();
                for (int s = 0; s < 1; s++) // the material is double sided
                {
                    var go = new GameObject("Glow");
                    go.transform.SetParent(root, false);
                    go.transform.localRotation = Quaternion.Euler(0f, s * 180f, 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = CombatFxMeshes.Disc;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = _glow;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = false;
                    list.Add(r);
                }
                lamp.Renderers = list.ToArray();
            }
            _lamps.Add(lamp);
        }

        void AddSpot(Transform parent, Vector3 at)
        {
            var go = new GameObject("Headlight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = SpotRange;
            l.spotAngle = SpotAngle;
            l.innerSpotAngle = SpotInner;
            l.intensity = SpotIntensity;
            l.color = HeadColor;
            l.shadows = LightShadows.None;
            l.enabled = false;
            _spots.Add(l);
        }

        void AddCone(Transform parent, Vector3 at)
        {
            if (_noGlow) return;
            var go = new GameObject("Beam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = Cone();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _glow;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            _cones.Add(r);
        }

        // The beam in the air: three flat strips (crossed 60 degrees apart) along +Z, bright on their centre line and clear at
        // their edges (the particle shader multiplies the vertex colour), so no hard outline shows from any side; opaque at
        // about a fifth of the length and gone at the far end. Double sided material.
        static Mesh Cone()
        {
            if (_cone != null) return _cone;
            float[] z = { 0f, 0.1f, 0.35f, 1f };
            float[] half = { 0.05f, 0.5f, 1.6f, ConeRadius };
            byte[] alpha = { 0, 255, 110, 0 };
            var v = new List<Vector3>();
            var c = new List<Color32>();
            var t = new List<int>();
            for (int plane = 0; plane < 3; plane++)
            {
                var dir = Quaternion.Euler(0f, 0f, plane * 60f) * Vector3.right;
                int first = v.Count;
                for (int i = 0; i < z.Length; i++)
                {
                    v.Add(dir * -half[i] + Vector3.forward * (z[i] * ConeLength)); c.Add(new Color32(255, 255, 255, 0));
                    v.Add(Vector3.forward * (z[i] * ConeLength)); c.Add(new Color32(255, 255, 255, alpha[i]));
                    v.Add(dir * half[i] + Vector3.forward * (z[i] * ConeLength)); c.Add(new Color32(255, 255, 255, 0));
                }
                for (int i = 0; i < z.Length - 1; i++)
                    for (int k = 0; k < 2; k++)
                    {
                        int a0 = first + i * 3 + k, a1 = a0 + 1, b0 = a0 + 3, b1 = b0 + 1;
                        t.Add(a0); t.Add(b0); t.Add(a1);
                        t.Add(a1); t.Add(b0); t.Add(b1);
                    }
            }
            _cone = new Mesh { name = "HeadBeam" };
            _cone.SetVertices(v);
            _cone.SetColors(c);
            _cone.SetTriangles(t, 0);
            _cone.RecalculateBounds();
            return _cone;
        }
    }
}

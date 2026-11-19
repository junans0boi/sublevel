using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Spent cases (#199), client-side only: each shot (or the bolt worked after it, or a break-action opened to reload)
    // throws a case out of the ejection port of the weapon shown, in first and third person alike. The cases come from
    // one shared pool (CasingPool): a few seconds' flight and rest each, oldest reused first, none for shooters far
    // from the camera. Landing plays WeaponSounds.Casing by the ground under it (CasingRules.Sound).
    [RequireComponent(typeof(Combatant))]
    public class CasingFx : MonoBehaviour
    {
        const double PelletWindow = 0.03;

        readonly ShotGrouper _shots = new ShotGrouper(PelletWindow);
        Combatant _combatant;
        WeaponVisual _weapons;
        float _boltAt = -1f;
        CasingSpec _boltSpec;
        int _spent, _spentWeapon;
        bool _wasReloading;

        // Cases this combatant threw out (tests).
        public int Ejected { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _weapons = GetComponent<WeaponVisual>();
            _combatant.ShotFired += OnShot;
        }

        void OnDestroy() => _combatant.ShotFired -= OnShot;

        void OnShot(Vector3 origin, Vector3 end)
        {
            if (!_shots.IsNewShot(Time.timeAsDouble)) return;
            int id = _combatant.ActiveWeaponId;
            if (!CasingRules.TryGet(id, out var spec)) return;
            switch (spec.Timing)
            {
                case EjectTiming.OnShot: Eject(spec, 1); break;
                case EjectTiming.Bolt: _boltAt = Time.time + spec.DelaySeconds; _boltSpec = spec; break;
                case EjectTiming.OnReload:
                    if (_spentWeapon != id) _spent = 0;
                    _spentWeapon = id;
                    _spent++;
                    break;
            }
        }

        void Update()
        {
            if (_boltAt >= 0f && Time.time >= _boltAt)
            {
                _boltAt = -1f;
                Eject(_boltSpec, 1);
            }
            bool reloading = _combatant.Reloading.Value;
            if (reloading && !_wasReloading && _spent > 0 && _combatant.ActiveWeaponId == _spentWeapon && CasingRules.TryGet(_spentWeapon, out var spec))
            {
                Eject(spec, _spent);
                _spent = 0;
            }
            _wasReloading = reloading;
        }

        void Eject(CasingSpec spec, int count)
        {
            if (_weapons == null || !CasingPool.ShowsAt(transform.position)) return;
            if (!_weapons.TryEjectionPort(out var port, out var right, out var up, out var forward)) return;
            var carry = _weapons.AtEye ? Vector3.zero : GetComponent<CombatantMotion>() is CombatantMotion m ? transform.rotation * m.LocalVelocity : Vector3.zero;
            CasingRules.Throw(spec.Kind, out float outward, out float lift);
            for (int i = 0; i < count; i++)
            {
                // A break action drops its shells out of the back, barely thrown.
                var v = spec.Timing == EjectTiming.OnReload
                    ? -forward * 0.6f + up * 0.3f + right * Random.Range(-0.15f, 0.15f)
                    : right * (outward * Random.Range(0.85f, 1.15f)) + up * (lift * Random.Range(0.8f, 1.2f)) - forward * Random.Range(0.1f, 0.5f);
                CasingPool.Spawn(spec.Kind, port + forward * (-0.02f * i), Quaternion.LookRotation(right, up), v + carry);
                Ejected++;
            }
        }
    }

    // The shared case pool and its per-frame flight (one component in the scene, made on first use).
    public class CasingPool : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        const int AudioVoices = 4;
        const float Gravity = 9.81f, Restitution = 0.35f, RestSpeed = 0.6f, MinSoundGap = 0.06f;

        static CasingPool _instance;
        static Mesh _mesh;
        static Material _brass, _hull;

        struct Case
        {
            public Transform T;
            public MeshRenderer R;
            public Vector3 Velocity, Spin;
            public float Born, Length;
            public int Bounces;
            public bool On, Resting;
            public CasingKind Kind;
        }

        Case[] _cases = new Case[CasingRules.MaxLiveHigh];
        int _next;
        AudioSource[] _voices;
        int _nextVoice;
        float _lastSoundAt = -10f;
        int _take;
        Material _source;

        // For the tests: cases in flight or lying now, and landings heard.
        public static int Live { get; private set; }
        public static int SoundsPlayed { get; private set; }
        public static int Capacity => GameSettings.HighEffects ? CasingRules.MaxLiveHigh : CasingRules.MaxLiveLow;

        // The middle of the cases lying on the ground now (the review shots look there); false when none lie.
        public static bool TryRestingCenter(out Vector3 center)
        {
            center = Vector3.zero;
            if (_instance == null) return false;
            int n = 0;
            foreach (var c in _instance._cases) if (c.On && c.Resting) { center += c.T.position; n++; }
            if (n > 0) center /= n;
            return n > 0;
        }

        // Cases are made only for a shooter this near the camera (they are a few millimetres wide).
        public static bool ShowsAt(Vector3 at)
        {
            var cam = Camera.main;
            return cam != null && (cam.transform.position - at).sqrMagnitude <= CasingRules.ShowWithinMeters * CasingRules.ShowWithinMeters;
        }

        public static void Spawn(CasingKind kind, Vector3 at, Quaternion turn, Vector3 velocity)
        {
            if (_instance == null) _instance = new GameObject("Casings").AddComponent<CasingPool>();
            _instance.Add(kind, at, turn, velocity);
        }

        void Add(CasingKind kind, Vector3 at, Quaternion turn, Vector3 velocity)
        {
            int cap = Capacity;
            if (_next >= cap) _next = 0;
            int i = _next;
            _next = (_next + 1) % cap;
            ref var c = ref _cases[i];
            if (c.T == null && !Make(ref c)) return;
            if (!c.On) Live++;
            CasingRules.Size(kind, out float diameter, out float length);
            // The built-in cylinder is 1 wide and 2 tall along its y: lay it across the throw (along `right`).
            c.T.SetPositionAndRotation(at, turn * Quaternion.Euler(90f, 0f, 0f));
            c.T.localScale = new Vector3(diameter, length * 0.5f, diameter);
            c.R.sharedMaterial = kind == CasingKind.Shell ? _hull : _brass;
            c.R.enabled = true;
            c.Velocity = velocity;
            c.Spin = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 1400f;
            c.Born = Time.time; c.Length = length; c.Bounces = 0; c.On = true; c.Resting = false; c.Kind = kind;
        }

        bool Make(ref Case c)
        {
            if (_mesh == null) _mesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            if (_brass == null && !MakeMaterials()) return false;
            var go = new GameObject("Case");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            c.R = go.AddComponent<MeshRenderer>();
            c.R.shadowCastingMode = ShadowCastingMode.Off;
            c.R.lightProbeUsage = LightProbeUsage.Off;
            c.R.reflectionProbeUsage = ReflectionProbeUsage.Off;
            c.T = go.transform;
            return true;
        }

        // Brass and a red plastic hull, made from a lit material already in the scene (shaders cannot be looked up by
        // name in a player build): the first character body's.
        static bool MakeMaterials()
        {
            Material source = null;
            foreach (var r in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) if (r.sharedMaterial != null) { source = r.sharedMaterial; break; }
            if (source == null) return false;
            Material Tint(string name, Color color, float metallic, float smooth)
            {
                var m = new Material(source) { name = name, color = color, mainTexture = null };
                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                return m;
            }
            _brass = Tint("CaseBrass", new Color(0.86f, 0.64f, 0.27f), 0.85f, 0.65f);
            _hull = Tint("CaseHull", new Color(0.62f, 0.09f, 0.07f), 0.1f, 0.35f);
            return true;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime, now = Time.time;
            if (dt <= 0f) return;
            int live = 0;
            for (int i = 0; i < _cases.Length; i++)
            {
                ref var c = ref _cases[i];
                if (!c.On) continue;
                float age = now - c.Born;
                if (age >= CasingRules.LifeSeconds || i >= Capacity) { c.On = false; c.R.enabled = false; continue; }
                live++;
                if (age > CasingRules.LifeSeconds - CasingRules.ShrinkSeconds)
                    c.T.localScale *= Mathf.Clamp01(1f - dt / Mathf.Max(1e-3f, CasingRules.LifeSeconds - age));
                if (c.Resting) continue;
                c.Velocity.y -= Gravity * dt;
                var from = c.T.position;
                var step = c.Velocity * dt;
                c.T.Rotate(c.Spin * dt, Space.Self);
                if (Physics.Raycast(from, step, out var hit, step.magnitude + 0.005f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<Combatant>() == null)
                {
                    if (c.Bounces == 0) Land(hit.point, c.Kind);
                    c.Bounces++;
                    c.Velocity = Vector3.Reflect(c.Velocity, hit.normal) * Restitution;
                    c.Spin *= 0.4f;
                    c.T.position = hit.point + hit.normal * 0.006f;
                    if (c.Velocity.magnitude < RestSpeed || c.Bounces >= CasingRules.MaxBounces)
                    {
                        // Lie on the ground: the case's long axis (its local y) turned into the ground's plane, its side down.
                        c.Resting = true;
                        var along = Vector3.ProjectOnPlane(c.T.up, hit.normal);
                        if (along.sqrMagnitude < 1e-6f) along = Vector3.ProjectOnPlane(Vector3.forward, hit.normal);
                        c.T.rotation = Quaternion.FromToRotation(c.T.up, along.normalized) * c.T.rotation;
                        c.T.position = hit.point + hit.normal * (CasingRules.RestLift(c.Kind));
                    }
                    continue;
                }
                c.T.position = from + step;
            }
            Live = live;
        }

        // The first touch of the ground: a tink by surface, if the listener is near and no other case just sounded.
        void Land(Vector3 at, CasingKind kind)
        {
            if (Time.time - _lastSoundAt < MinSoundGap || SpatialAudio.DistanceToListener(at) > CasingRules.HeardMeters) return;
            var sound = CasingRules.Sound(CombatantAudio.SurfaceIdAt(at + Vector3.up * 0.05f), kind);
            if (sound.Gain <= 0f) return;
            var clips = CombatantAudio.Clips(WeaponSounds.Casing);
            if (clips.Length == 0) return;
            if (_voices == null)
            {
                _voices = new AudioSource[AudioVoices];
                for (int i = 0; i < AudioVoices; i++)
                {
                    var go = new GameObject("CaseAudio");
                    go.transform.SetParent(transform, false);
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    SpatialAudio.Apply(s, new SpatialAudio.Range(1f, CasingRules.HeardMeters));
                    go.AddComponent<AudioLowPassFilter>();
                    _voices[i] = s;
                }
            }
            var voice = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % AudioVoices;
            _take = WeaponSounds.NextVariant(clips.Length, _take, Random.value);
            var take = WeaponSounds.VariantOf(clips.Length, _take);
            voice.transform.position = at;
            voice.GetComponent<AudioLowPassFilter>().cutoffFrequency = Mathf.Min(sound.CutoffHz, take.CutoffHz);
            voice.pitch = sound.Pitch * take.Pitch * Random.Range(0.95f, 1.05f);
            voice.PlayOneShot(clips[take.Clip], 0.35f * sound.Gain * GameSettings.EffectsVolume);
            _lastSoundAt = Time.time;
            SoundsPlayed++;
        }

        void OnDestroy()
        {
            if (_instance == this) { _instance = null; Live = 0; }
        }
    }
}

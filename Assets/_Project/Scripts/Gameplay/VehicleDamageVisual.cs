using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // What vehicle damage looks and sounds like on every side (#90, ADR-074), driven by VehicleDamage's replicated
    // state and events:
    //  - bullet holes: a dark hole with a bare-metal rim on the paint (a ring of MaxDecals per vehicle);
    //  - glass: a crack (then a shattered web) drawn on the hit pane, on the glass surface where the shot went in;
    //  - dents: the body mesh pushed in around bullet hits (slightly), collisions and blasts;
    //  - flat tyres: the tyre squashed onto the ground (the core already lowers that corner);
    //  - white then black smoke from the engine, fire, the explosion (flash, fireball, debris, smoke, light pulse,
    //    a shake of the local camera nearby, a synthesized bang) and the burnt-out wreck.
    // Textures and sounds are made here in code (no asset files). Uses the shot-effect materials of the scene's
    // ThrowableView (vertex coloured, tinted by _BaseColor); without one only dents, tyres and the wreck tint show.
    // Runs late: after Vehicle has placed the wheels (LateUpdate, order -100) and after the cameras (shake).
    [DefaultExecutionOrder(300)]
    [RequireComponent(typeof(VehicleDamage))]
    public class VehicleDamageVisual : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color WhiteSmoke = new Color(0.86f, 0.86f, 0.86f, 0.5f);
        static readonly Color BlackSmoke = new Color(0.07f, 0.07f, 0.07f, 0.75f);
        static readonly Color Charred = new Color(0.1f, 0.09f, 0.085f, 1f);
        static readonly Color HoleColor = new Color(0.02f, 0.02f, 0.02f, 1f);
        static readonly Color RimColor = new Color(0.62f, 0.62f, 0.64f, 1f);
        const float FlatTyreSquash = 0.8f;     // tyre height share when flat
        const float ShakeRange = 25f;
        const float ShakeSeconds = 0.6f;
        const float ShakeMetres = 0.35f;

        static Texture2D _puff, _crack, _shatter;
        static AudioClip _bang;

        VehicleDamage _damage;
        ParticleSystem _smoke, _fire, _flashPs, _fireball, _debris, _blastSmoke;
        Light _light;
        float _lightStart = -10f;
        float _shakeStart = -10f;
        float _blastSmokeDue = -1f;
        AudioSource _audio;
        VehicleDamageStage _shown = (VehicleDamageStage)255;
        int _panesShown;
        readonly MeshFilter[] _paneDecals = new MeshFilter[VehicleDamageMath.PaneCount];
        readonly int[] _paneDecalState = new int[VehicleDamageMath.PaneCount];
        readonly List<Transform> _holes = new List<Transform>();
        int _holesPlaced;
        MaterialPropertyBlock _block;
        Material _glow, _dust, _softGlow, _softDust, _crackMat, _shatterMat;
        // The drawn body: the main mesh (with the glass sub-mesh on a car) and the detachable parts' meshes.
        MeshFilter _body;
        int _glassSubmesh = -1;
        readonly List<Shell> _shells = new List<Shell>();
        int _partsShown;
        float _startTime;

        // For tests and captures.
        public int ExplosionsShown { get; private set; }
        public int DentsShown { get; private set; }
        public int PaneDecalsShown { get; private set; }
        public int HolesShown { get; private set; }
        public int PartsFallen { get; private set; }
        public AudioSource BangSource => _audio;

        void Awake()
        {
            _damage = GetComponent<VehicleDamage>();
            _block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            _damage.Holed += OnHoled;
            _damage.Exploded += OnExploded;
            _damage.PaneHit += OnPaneHit;
            _damage.Dented += OnDented;
        }

        void OnDisable()
        {
            _damage.Holed -= OnHoled;
            _damage.Exploded -= OnExploded;
            _damage.PaneHit -= OnPaneHit;
            _damage.Dented -= OnDented;
        }

        void Start()
        {
            var view = FindAnyObjectByType<ThrowableView>();
            if (view != null) { _glow = view.GlowMaterial; _dust = view.DustMaterial; }
            _softGlow = WithTexture(_glow, Puff(), "_Puff");
            _softDust = WithTexture(_dust, Puff(), "_Puff");
            _crackMat = WithTexture(_dust, Crack(false), "_Crack");
            _shatterMat = WithTexture(_dust, Crack(true), "_Shatter");
            // The body: the mesh carrying the glass material (cars), else the biggest one.
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.EndsWith("_Glass")) { _body = mf; _glassSubmesh = i; }
                if (_body == null && mf.name == "Body") _body = mf;
            }
            if (_body != null) _shells.Add(new Shell(_body, _glassSubmesh));
            for (int i = 0; i < VehicleDamageMath.PartCount; i++)
            {
                var part = _damage.Part(i);
                var mf = part != null ? part.GetComponent<MeshFilter>() : null;
                if (mf != null) _shells.Add(new Shell(mf, -1) { Part = i });
            }
            _startTime = Time.time;
        }

        void Update()
        {
            var stage = _damage.CurrentStage;
            if (stage != _shown) ShowStage(stage);
            int panes = _damage.Panes.Value;
            if (panes != _panesShown) ShowPanes(panes);
            int parts = _damage.Parts.Value;
            if (parts != _partsShown) ShowParts(parts);
        }

        void LateUpdate()
        {
            SquashFlatTyres();
            if (_light != null)
            {
                // A pulse: up at once, flickering down over 0.7 s.
                float t = Time.time - _lightStart;
                _light.enabled = t < 0.7f;
                if (_light.enabled) _light.intensity = 9f * (1f - t / 0.7f) * (0.75f + 0.25f * Mathf.PerlinNoise(t * 30f, 0.3f));
            }
            Shake();
            if (_blastSmokeDue > 0f && Time.time >= _blastSmokeDue && _blastSmoke != null) { _blastSmokeDue = -1f; _blastSmoke.Emit(18); }
        }

        // ---- Stages ----

        void ShowStage(VehicleDamageStage stage)
        {
            _shown = stage;
            if (_softDust != null && stage >= VehicleDamageStage.WhiteSmoke)
            {
                if (_smoke == null) _smoke = NewEmitter("Smoke", _softDust, EngineSpot(), 2.4f, 1.1f, 0.9f, 2.6f, 120);
                var main = _smoke.main;
                main.startColor = stage == VehicleDamageStage.WhiteSmoke ? WhiteSmoke : BlackSmoke;
                var emission = _smoke.emission;
                emission.rateOverTime = stage == VehicleDamageStage.WhiteSmoke ? 5f : stage == VehicleDamageStage.Wrecked ? 4f : 10f;
                // Above the flames once burning (dark smoke over the fire's root tinted it green).
                _smoke.transform.localPosition = EngineSpot() + Vector3.up * (stage >= VehicleDamageStage.Burning ? 0.9f : 0f);
                if (!_smoke.isPlaying) _smoke.Play();
            }
            if (_softGlow != null && stage == VehicleDamageStage.Burning)
            {
                if (_fire == null)
                {
                    // A motorcycle burns all over its small frame (tank, engine, seat): wider, fuller flames than its size alone.
                    bool bike = _damage.Vehicle.IsMotorcycle;
                    _fire = bike ? NewEmitter("Fire", _softGlow, EngineSpot(), 0.85f, 1.5f, 0.62f, 1.9f, 160)
                        : NewEmitter("Fire", _softGlow, EngineSpot(), 0.7f, 1.4f, 0.5f, 1.4f, 120);
                    if (bike)
                    {
                        var shape = _fire.shape;
                        shape.radius = 0.5f;
                    }
                    // Orange at the root, then deep red as it rises and fades. Green stays well under red and the alpha low:
                    // additive overlaps clip red first, and a yellow core under the smoke read olive green.
                    SetColorOverLife(_fire, new[] { (0f, new Color(1f, 0.5f, 0.14f)), (0.35f, new Color(1f, 0.3f, 0.05f)), (1f, new Color(0.55f, 0.07f, 0.02f)) },
                        new[] { (0f, 0f), (0.1f, 0.6f), (0.6f, 0.4f), (1f, 0f) });
                    var emission = _fire.emission;
                    emission.rateOverTime = bike ? 48f : 34f;
                }
                _fire.Play();
            }
            if (stage == VehicleDamageStage.Wrecked)
            {
                if (_fire != null) _fire.Stop();
                // The burnt-out shell: the body and everything on it darkened.
                foreach (var r in GetComponentsInChildren<MeshRenderer>()) Char(r);
            }
        }

        Vector3 EngineSpot()
        {
            var b = _damage.LocalBounds();
            return _damage.Vehicle.IsMotorcycle ? b.center + Vector3.up * b.extents.y * 0.3f : new Vector3(b.center.x, b.max.y - b.size.y * 0.35f, b.min.z + b.size.z * 0.85f);
        }

        // ---- The explosion ----

        void OnExploded()
        {
            ExplosionsShown++;
            var b = _damage.LocalBounds();
            var centre = b.center + Vector3.up * b.extents.y * 0.5f;
            if (_softGlow != null)
            {
                if (_flashPs == null)
                {
                    _flashPs = NewBurst("ExplosionFlash", _softGlow, centre, 0.2f, 0f, 6f, 1.3f);
                    SetColorOverLife(_flashPs, new[] { (0f, Color.white), (1f, new Color(1f, 0.55f, 0.2f)) }, new[] { (0f, 0.9f), (1f, 0f) });
                    _fireball = NewBurst("Fireball", _softGlow, centre, 1.0f, 4f, 1.5f, 2.6f);
                    SetColorOverLife(_fireball, new[] { (0f, new Color(1f, 0.55f, 0.2f)), (0.25f, new Color(1f, 0.32f, 0.05f)), (0.7f, new Color(0.5f, 0.08f, 0.02f)), (1f, new Color(0.15f, 0.04f, 0.02f)) },
                        new[] { (0f, 0.6f), (0.6f, 0.4f), (1f, 0f) });
                    // The glow over the dark smoke and debris, never under them (a dark core ringed by light).
                    _flashPs.GetComponent<ParticleSystemRenderer>().sortingFudge = -100f;
                    _fireball.GetComponent<ParticleSystemRenderer>().sortingFudge = -100f;
                    var shape = _fireball.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.8f;
                }
                _flashPs.Emit(1);
                _fireball.Emit(24); // additive: more overlap than this washes the core out to yellow-white
            }
            if (_softDust != null)
            {
                if (_debris == null)
                {
                    _debris = NewBurst("Debris", _softDust, centre, 1.8f, 11f, 0.14f, 1f);
                    var main = _debris.main;
                    main.gravityModifier = 1.6f;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
                    main.startColor = new Color(0.05f, 0.04f, 0.04f, 1f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    var shape = _debris.shape;
                    shape.shapeType = ParticleSystemShapeType.Hemisphere;
                    shape.radius = 0.6f;
                    _blastSmoke = NewBurst("BlastSmoke", _softDust, centre, 3.2f, 2.2f, 2f, 3.2f);
                    var smain = _blastSmoke.main;
                    smain.startColor = new Color(0.08f, 0.07f, 0.07f, 0.8f);
                    smain.gravityModifier = -0.15f;
                }
                _debris.Emit(40);
                _blastSmokeDue = Time.time + 0.35f; // the smoke rolls in after the flash
            }
            // The light above the body (from below the car it lit the ground under it, as if the car floated).
            if (_light == null)
            {
                _light = new GameObject("ExplosionLight").AddComponent<Light>();
                _light.transform.SetParent(transform, false);
                _light.type = LightType.Point;
                _light.color = new Color(1f, 0.58f, 0.22f);
                _light.range = 16f;
                _light.shadows = LightShadows.None;
            }
            _light.transform.localPosition = new Vector3(b.center.x, b.max.y + 1.2f, b.center.z);
            _lightStart = Time.time;
            _shakeStart = Time.time;
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.rolloffMode = AudioRolloffMode.Linear;
                _audio.minDistance = 5f;
                _audio.maxDistance = 180f;
            }
            _audio.PlayOneShot(Bang(), 1f);
        }

        // The local player's camera shakes when the blast is near (after every camera script has placed it).
        void Shake()
        {
            float t = Time.time - _shakeStart;
            if (t >= ShakeSeconds) return;
            var pc = PlayerController.Local;
            var cam = pc != null ? pc.Camera : null;
            if (cam == null) return;
            float distance = Vector3.Distance(cam.transform.position, transform.position);
            if (distance >= ShakeRange) return;
            float amount = ShakeMetres * (1f - distance / ShakeRange) * (1f - t / ShakeSeconds);
            float s = Time.time * 40f;
            cam.transform.position += new Vector3(Mathf.PerlinNoise(s, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, s) - 0.5f, Mathf.PerlinNoise(s, 0.9f) - 0.5f) * (2f * amount);
        }

        // A low boom with a crackling tail: a thump (falling sine) under low-passed noise, decaying.
        static AudioClip Bang()
        {
            if (_bang != null) return _bang;
            const int rate = 22050;
            int n = (int)(rate * 2.4f);
            var data = new float[n];
            var rng = new System.Random(74);
            float low = 0f, low2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.08f;
                low2 += (low - low2) * 0.2f;
                float thump = Mathf.Sin(2f * Mathf.PI * (38f + 50f * Mathf.Exp(-t * 6f)) * t) * Mathf.Exp(-t * 5f);
                float attack = Mathf.Clamp01(t / 0.004f);
                float crackle = noise * Mathf.Exp(-t * 9f) * 0.35f;
                data[i] = Mathf.Clamp((thump * 0.9f + low2 * 3.2f * Mathf.Exp(-t * 2.2f) + crackle) * attack, -1f, 1f);
            }
            _bang = AudioClip.Create("VehicleExplosion", n, 1, rate, false);
            _bang.SetData(data, 0);
            return _bang;
        }

        // ---- Tyres ----

        // Flat: the tyre squashed toward the ground about its contact point.
        void SquashFlatTyres()
        {
            int flat = _damage.FlatTyres.Value;
            if (flat == 0) return;
            var v = _damage.Vehicle;
            float radius = v.Rules.WheelRadius;
            for (int i = 0; i < v.WheelCount; i++)
            {
                if ((flat & (1 << i)) == 0) continue;
                var pivot = v.WheelPivot(i);
                if (pivot == null) continue;
                pivot.localScale = new Vector3(1f, FlatTyreSquash, 1f);
                // The core placed the wheel this frame (non-cumulative); keep its bottom on the ground.
                pivot.localPosition -= Vector3.up * radius * (1f - FlatTyreSquash);
            }
        }

        // ---- Glass ----

        void OnPaneHit(int pane, Vector3 localOrigin, Vector3 localDir)
        {
            if (pane < 0 || pane >= _paneDecals.Length || _paneDecals[pane] != null) return;
            PlacePaneDecal(pane, localOrigin, localDir);
        }

        // The state per pane; a pane with no decal yet (a blast, or joined late) gets one at its middle.
        void ShowPanes(int panes)
        {
            _panesShown = panes;
            for (int p = 0; p < VehicleDamageMath.PaneCount; p++)
            {
                int state = VehicleDamageMath.GetPane(panes, p);
                if (state == 0) continue;
                if (_paneDecals[p] == null && PaneRay(p, out var o, out var d)) PlacePaneDecal(p, o, d);
                if (_paneDecals[p] != null && _paneDecalState[p] != state)
                {
                    _paneDecalState[p] = state;
                    var mf = _paneDecals[p];
                    mf.GetComponent<MeshRenderer>().sharedMaterial = state >= 2 ? _shatterMat : _crackMat;
                    mf.sharedMesh = PaneDecalMesh(mf.sharedMesh, _paneAt[p], _paneNormal[p], state >= 2 ? 0.6f : 0.45f);
                }
            }
        }

        // From outside a pane's face, at the middle of the glass band, straight in.
        bool PaneRay(int pane, out Vector3 origin, out Vector3 dir)
        {
            origin = dir = Vector3.zero;
            if (!_damage.TryGlassBand(out var box, out float belt, out float roof)) return false;
            float y = (belt + roof) * 0.5f;
            float front = box.min.z + box.size.z * 0.62f, rear = box.min.z + box.size.z * 0.38f;
            switch (pane)
            {
                case 0: origin = new Vector3(box.center.x, y, box.max.z + 2f); dir = Vector3.back; break;
                case 1: origin = new Vector3(box.center.x, y, box.min.z - 2f); dir = Vector3.forward; break;
                case 2: origin = new Vector3(box.min.x - 2f, y, front); dir = Vector3.right; break;
                case 3: origin = new Vector3(box.min.x - 2f, y, rear); dir = Vector3.right; break;
                case 4: origin = new Vector3(box.max.x + 2f, y, front); dir = Vector3.left; break;
                default: origin = new Vector3(box.max.x + 2f, y, rear); dir = Vector3.left; break;
            }
            return true;
        }

        readonly Vector3[] _paneAt = new Vector3[VehicleDamageMath.PaneCount];     // in the body mesh's space
        readonly Vector3[] _paneNormal = new Vector3[VehicleDamageMath.PaneCount];

        void PlacePaneDecal(int pane, Vector3 localOrigin, Vector3 localDir)
        {
            if (_crackMat == null || _shells.Count == 0 || _glassSubmesh < 0) return;
            var body = _shells[0];
            var o = body.FromVehicle(transform, localOrigin);
            var d = body.DirFromVehicle(transform, localDir);
            if (!body.Ray(body.GlassTris, o, d, 10f, out float t, out var normal)) return;
            _paneAt[pane] = o + d * t;
            _paneNormal[pane] = normal;
            var go = new GameObject("GlassCrack");
            var mf = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _crackMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            // In the body mesh's space: the decal is made of the glass's own triangles, so it never leaves the pane.
            go.transform.SetParent(body.Filter.transform, false);
            _paneDecals[pane] = mf;
            _paneDecalState[pane] = 0;
            PaneDecalsShown++;
            ShowPanes(_damage.Panes.Value);
        }

        // The glass triangles around the hit facing the same way (this pane, not the next one round the corner), a few
        // mm out toward the shooter, with the crack texture projected square on them `size` across. Where the pane ends
        // the decal ends: it cannot stick out over the roof or the pillars.
        Mesh PaneDecalMesh(Mesh mesh, Vector3 at, Vector3 normal, float size)
        {
            var body = _shells[0];
            var rot = Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up);
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = body.GlassTris;
            var v = body.V;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                var fn = Vector3.Cross(b - a, c - a);
                if (fn.sqrMagnitude < 1e-12f || Mathf.Abs(Vector3.Dot(fn.normalized, normal)) < 0.5f) continue;
                // This layer of glass only, and only where the texture lands.
                if (Mathf.Abs(Vector3.Dot((a + b + c) / 3f - at, normal)) > 0.08f) continue;
                Vector2 Uv(Vector3 q) => new Vector2(Vector3.Dot(q - at, right) / size + 0.5f, Vector3.Dot(q - at, up) / size + 0.5f);
                Vector2 ua = Uv(a), ub = Uv(b), uc = Uv(c);
                if (Mathf.Max(ua.x, ub.x, uc.x) < 0f || Mathf.Min(ua.x, ub.x, uc.x) > 1f || Mathf.Max(ua.y, ub.y, uc.y) < 0f || Mathf.Min(ua.y, ub.y, uc.y) > 1f) continue;
                var lift = normal * 0.004f;
                verts.Add(a + lift); verts.Add(b + lift); verts.Add(c + lift);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            }
            if (mesh == null) mesh = new Mesh { name = "GlassCrackDecal" };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            var colours = new Color32[verts.Count];
            for (int i = 0; i < colours.Length; i++) colours[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = colours;
            // Both windings: seen from either side.
            var idx = new int[verts.Count * 2];
            for (int i = 0; i < verts.Count; i += 3)
            {
                idx[i] = i; idx[i + 1] = i + 1; idx[i + 2] = i + 2;
                idx[verts.Count + i] = i; idx[verts.Count + i + 1] = i + 2; idx[verts.Count + i + 2] = i + 1;
            }
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- Holes and dents ----

        // A hole sits on the drawn surface along the shot (the collider box only roughly wraps the body). Where the
        // line meets no body mesh (a thin frame, a gap round a rider) there is no hole: nothing floats in the air.
        void OnHoled(Vector3 local, Vector3 localNormal, float radius, float depth)
        {
            var n = localNormal.sqrMagnitude > 0f ? localNormal.normalized : Vector3.up;
            var from = local + n * 0.5f;
            if (!RaySurface(from, -n, 1f, out _, out var point, out _)) return;
            OnDented(point, -n, radius, depth);
            // Again on the dented surface.
            if (!RaySurface(from, -n, 1f, out var shell, out point, out var normal) || _dust == null) return;
            var rules = VehicleDamageRules.For(_damage.Vehicle.Kind);
            int slot = VehicleDamageMath.DecalSlot(_holesPlaced++, rules.MaxDecals);
            var hole = slot < _holes.Count ? _holes[slot] : null;
            if (hole == null) // new, or it went with a part that fell off and was cleared away
            {
                // A dark hole on a bare-metal rim, each two-sided (whatever the shader's culling).
                hole = new GameObject("BulletHole").transform;
                Disc(hole, "Rim", RimColor, 1f, 0f);
                Disc(hole, "BulletHole", HoleColor, 0.55f, 0.002f);
                if (slot < _holes.Count) _holes[slot] = hole; else _holes.Add(hole);
            }
            // On the mesh it struck (a part takes its holes along when it falls).
            var on = shell.Filter.transform;
            hole.SetParent(on, false);
            hole.localPosition = shell.FromVehicle(transform, point + normal * 0.003f);
            hole.localRotation = Quaternion.LookRotation(-shell.DirFromVehicle(transform, normal));
            hole.localScale = Vector3.one * rules.DecalSize;
            HolesShown++;
        }

        void Disc(Transform parent, string name, Color color, float size, float lift)
        {
            for (int side = 0; side < 2; side++)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.back * lift;
                go.transform.localRotation = Quaternion.Euler(0f, side * 180f, 0f);
                go.transform.localScale = Vector3.one * size;
                go.AddComponent<MeshFilter>().sharedMesh = CombatFxMeshes.Disc;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _dust;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                r.SetPropertyBlock(_block);
            }
        }

        // The nearest body surface (not glass) along a vehicle-local ray within `maxT`: the mesh, point and normal
        // (toward the ray's origin), vehicle-local.
        bool RaySurface(Vector3 o, Vector3 d, float maxT, out Shell hit, out Vector3 point, out Vector3 normal)
        {
            hit = null;
            point = normal = Vector3.zero;
            float best = maxT;
            foreach (var shell in _shells)
            {
                if (!shell.Attached) continue;
                var so = shell.FromVehicle(transform, o);
                var sd = shell.DirFromVehicle(transform, d);
                if (!shell.Ray(shell.SolidTris, so, sd, best, out float t, out var n)) continue;
                best = t;
                hit = shell;
                point = transform.InverseTransformPoint(shell.Filter.transform.TransformPoint(so + sd * t));
                normal = transform.InverseTransformDirection(shell.Filter.transform.TransformDirection(n)).normalized;
            }
            return hit != null;
        }

        // Pushes the body's vertices (and the parts') near `local` along `localDir` (vehicle-local), with a smooth falloff
        // to `radius`, never more than DentMaxDepth in total per vertex, and bends the normals with the new shape.
        void OnDented(Vector3 local, Vector3 localDir, float radius, float depth)
        {
            if (depth <= 0f || radius <= 0f) return;
            var rules = VehicleDamageRules.For(_damage.Vehicle.Kind);
            bool any = false;
            foreach (var shell in _shells)
                if (shell.Attached && shell.Dent(shell.FromVehicle(transform, local), shell.DirFromVehicle(transform, localDir), radius, depth, rules)) any = true;
            if (any) DentsShown++;
        }

        // ---- Detachable parts ----

        // Loose: the hood and trunk lid spring open on their hinge, a bumper drops at one end. Fallen: let go with physics,
        // cleared away after a while. A late joiner (or a part already gone when this vehicle appeared) just has none.
        void ShowParts(int packed)
        {
            int was = _partsShown;
            _partsShown = packed;
            var rules = VehicleDamageRules.For(_damage.Vehicle.Kind);
            foreach (var shell in _shells)
            {
                if (shell.Part < 0 || !shell.Attached) continue;
                var state = VehicleDamageMath.GetPart(packed, shell.Part);
                if (state == PartState.Dangling && VehicleDamageMath.GetPart(was, shell.Part) == PartState.Fixed) Dangle(shell, rules.PartDangleDeg);
                else if (state == PartState.Fallen) Fall(shell, rules, Time.time - _startTime > 1f);
            }
        }

        static void Dangle(Shell shell, float degrees)
        {
            var t = shell.Filter.transform;
            var b = shell.Filter.sharedMesh.bounds;
            Vector3 hinge;
            Quaternion turn;
            switch ((VehiclePart)shell.Part)
            {
                case VehiclePart.Hood: hinge = new Vector3(b.center.x, b.max.y, b.min.z); turn = Quaternion.Euler(-degrees, 0f, 0f); break;
                case VehiclePart.Trunk: hinge = new Vector3(b.center.x, b.max.y, b.max.z); turn = Quaternion.Euler(degrees, 0f, 0f); break;
                default: hinge = new Vector3(b.max.x, b.center.y, b.center.z); turn = Quaternion.Euler(0f, 0f, degrees * 0.6f); break;
            }
            t.localRotation = turn;
            t.localPosition = hinge - turn * hinge;
        }

        void Fall(Shell shell, VehicleDamageRules rules, bool animate)
        {
            var go = shell.Filter.gameObject;
            shell.Attached = false;
            _damage.ReleasePart(shell.Part);
            PartsFallen++;
            go.transform.SetParent(null, true);
            if (!animate) { Destroy(go); return; }
            var box = go.AddComponent<BoxCollider>(); // fits the mesh
            foreach (var c in _damage.Vehicle.Colliders) if (c != null) Physics.IgnoreCollision(box, c);
            var body = go.AddComponent<Rigidbody>();
            body.mass = 15f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            bool blown = _damage.CurrentStage >= VehicleDamageStage.Burning;
            var centre = transform.TransformPoint(_damage.LocalBounds().center);
            var outward = go.GetComponent<Renderer>().bounds.center - centre;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : transform.forward;
            // Seeded per part so every side throws it the same way (not synced: cosmetic).
            var rng = new System.Random(shell.Part * 7919 + 90);
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            body.linearVelocity = transform.forward * _damage.Vehicle.Speed + outward * (blown ? Rand(4f, 7f) : Rand(0.5f, 1.5f)) + Vector3.up * (blown ? Rand(4f, 7f) : 0.5f);
            body.angularVelocity = new Vector3(Rand(-4f, 4f), Rand(-4f, 4f), Rand(-4f, 4f)) * (blown ? 1f : 0.3f);
            if (_damage.CurrentStage == VehicleDamageStage.Wrecked)
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) Char(r);
            Destroy(go, rules.PartDespawnSeconds);
        }

        void Char(MeshRenderer r)
        {
            if (r.name == "BulletHole" || r.name == "Rim" || r.name == "GlassCrack") return;
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, Charred);
            r.SetPropertyBlock(_block);
        }

        // One drawn mesh of the body (the main shell or a detachable part): its vertices, read on first use and made
        // unique on the first dent; the normals bend with the dents.
        public sealed class Shell
        {
            public readonly MeshFilter Filter;
            public int Part = -1;              // VehiclePart, or -1 for the main body
            public bool Attached = true;
            readonly int _glassSub;
            Vector3[] _v, _n;
            int[] _solid, _glass;
            float[] _pushed;
            int[] _group;                      // weld group per vertex (same position = same group: no seams in the normals)
            List<int>[] _vertTris;             // the solid+glass triangles (first index) at each vertex
            int[] _all;
            Mesh _unique;

            public Shell(MeshFilter filter, int glassSub) { Filter = filter; _glassSub = glassSub; }

            public Vector3[] V { get { Read(); return _v; } }
            public int[] SolidTris { get { Read(); return _solid; } }
            public int[] GlassTris { get { Read(); return _glass; } }

            void Read()
            {
                if (_v != null) return;
                var mesh = Filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) { _v = new Vector3[0]; _n = _v; _solid = _glass = _all = new int[0]; return; }
                _v = mesh.vertices;
                _n = mesh.normals;
                var solid = new List<int>();
                var glass = new List<int>();
                for (int s = 0; s < mesh.subMeshCount; s++) (s == _glassSub ? glass : solid).AddRange(mesh.GetTriangles(s));
                _solid = solid.ToArray();
                _glass = glass.ToArray();
                var all = new int[_solid.Length + _glass.Length];
                _solid.CopyTo(all, 0);
                _glass.CopyTo(all, _solid.Length);
                _all = all;
            }

            public Vector3 FromVehicle(Transform vehicle, Vector3 local) => Filter.transform.InverseTransformPoint(vehicle.TransformPoint(local));
            public Vector3 DirFromVehicle(Transform vehicle, Vector3 dir) => Filter.transform.InverseTransformDirection(vehicle.TransformDirection(dir)).normalized;

            // Moller-Trumbore against `tris` (either winding): the nearest hit before maxT, normal toward the ray.
            public bool Ray(int[] tris, Vector3 o, Vector3 d, float maxT, out float best, out Vector3 normal)
            {
                var v = V;
                best = maxT;
                normal = Vector3.zero;
                bool found = false;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                    Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
                    float det = Vector3.Dot(e1, p);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float inv = 1f / det;
                    Vector3 s = o - a;
                    float u = Vector3.Dot(s, p) * inv;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(s, e1);
                    float w = Vector3.Dot(d, q) * inv;
                    if (w < 0f || u + w > 1f) continue;
                    float t = Vector3.Dot(e2, q) * inv;
                    if (t <= 0f || t >= best) continue;
                    best = t;
                    normal = Vector3.Cross(e1, e2).normalized;
                    found = true;
                }
                if (found && Vector3.Dot(normal, d) > 0f) normal = -normal;
                return found;
            }

            // Mesh-local dent. Normals: each vertex near the dent turns by the rotation its welded neighbourhood's face
            // normal went through, so the authored normals (hard edges, UV seams) are kept and the dent's slopes shade.
            public bool Dent(Vector3 centre, Vector3 dir, float radius, float depth, VehicleDamageRules rules)
            {
                Read();
                if (_v.Length == 0) return false;
                if (_unique == null)
                {
                    _unique = Object.Instantiate(Filter.sharedMesh);
                    _unique.name = Filter.sharedMesh.name + "_Dented";
                    Filter.sharedMesh = _unique;
                    _pushed = new float[_v.Length];
                    Topology();
                }
                float r2 = radius * radius;
                float reach = radius + Mathf.Max(0.15f, radius * 0.5f); // the moved vertices' neighbours turn too
                var near = new List<int>();
                for (int i = 0; i < _v.Length; i++) if ((_v[i] - centre).sqrMagnitude < reach * reach) near.Add(i);
                if (near.Count == 0) return false;
                var before = GroupNormals(near);
                bool moved = false;
                foreach (int i in near)
                {
                    float sqr = (_v[i] - centre).sqrMagnitude;
                    if (sqr >= r2) continue;
                    float push = VehicleDamageMath.DentPush(_pushed[i], depth, VehicleDamageMath.DentFalloff(Mathf.Sqrt(sqr), radius), rules);
                    if (push <= 0f) continue;
                    _v[i] += dir * push;
                    _pushed[i] += push;
                    moved = true;
                }
                if (!moved) return false;
                var after = GroupNormals(near);
                if (_n != null && _n.Length == _v.Length)
                    foreach (int i in near)
                    {
                        int g = _group[i];
                        if (!before.TryGetValue(g, out var b) || !after.TryGetValue(g, out var a) || b.sqrMagnitude < 1e-12f || a.sqrMagnitude < 1e-12f) continue;
                        _n[i] = Quaternion.FromToRotation(b, a) * _n[i];
                    }
                _unique.vertices = _v;
                if (_n != null && _n.Length == _v.Length) _unique.normals = _n;
                _unique.RecalculateBounds();
                return true;
            }

            void Topology()
            {
                _group = new int[_v.Length];
                var ids = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < _v.Length; i++)
                {
                    var key = Vector3Int.RoundToInt(_v[i] * 2000f); // half a millimetre
                    if (!ids.TryGetValue(key, out int g)) { g = ids.Count; ids[key] = g; }
                    _group[i] = g;
                }
                _vertTris = new List<int>[_v.Length];
                for (int t = 0; t + 2 < _all.Length; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = _all[t + k];
                        (_vertTris[vi] ?? (_vertTris[vi] = new List<int>(6))).Add(t);
                    }
            }

            // Area-weighted face normal sum per weld group of the given vertices.
            Dictionary<int, Vector3> GroupNormals(List<int> verts)
            {
                var sums = new Dictionary<int, Vector3>();
                foreach (int i in verts)
                {
                    sums.TryGetValue(_group[i], out var sum);
                    var tris = _vertTris[i];
                    if (tris != null)
                        foreach (int t in tris)
                        {
                            Vector3 a = _v[_all[t]], b = _v[_all[t + 1]], c = _v[_all[t + 2]];
                            sum += Vector3.Cross(b - a, c - a);
                        }
                    sums[_group[i]] = sum;
                }
                return sums;
            }
        }

        // ---- Emitters ----

        ParticleSystem NewEmitter(string name, Material material, Vector3 local, float lifetime, float speed, float size, float growTo, int max)
        {
            var ps = NewSystem(name, material, local, lifetime, speed, size, growTo, max);
            var main = ps.main;
            main.loop = true;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.3f;
            SetColorOverLife(ps, new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (0.15f, 1f), (1f, 0f) });
            return ps;
        }

        // A system that only emits on Emit() (the explosion's parts).
        ParticleSystem NewBurst(string name, Material material, Vector3 local, float lifetime, float speed, float size, float growTo)
        {
            var ps = NewSystem(name, material, local, lifetime, speed, size, growTo, 200);
            var main = ps.main;
            main.loop = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            SetColorOverLife(ps, new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (1f, 0f) });
            ps.Play();
            return ps;
        }

        ParticleSystem NewSystem(string name, Material material, Vector3 local, float lifetime, float speed, float size, float growTo, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = false;
            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, growTo));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static void SetColorOverLife(ParticleSystem ps, (float t, Color c)[] colors, (float t, float a)[] alphas)
        {
            var over = ps.colorOverLifetime;
            over.enabled = true;
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            over.color = g;
        }

        // ---- Materials and textures made in code ----

        // A copy of an effect material drawing `texture`, untinted (a tinted _BaseColor turned the fire green).
        static Material WithTexture(Material source, Texture2D texture, string suffix)
        {
            if (source == null) return null;
            var m = new Material(source) { name = source.name + suffix };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, Color.white);
            return m;
        }

        static Texture2D Puff()
        {
            if (_puff != null) return _puff;
            const int n = 64;
            _puff = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "VehiclePuff", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    _puff.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                }
            _puff.Apply();
            return _puff;
        }

        // Cracked glass: a white chip in the middle, cracks running out (forking), rings of short cross-cracks;
        // the shattered web has many more of both and a milky film. Seeded, so every side draws the same.
        static Texture2D Crack(bool shattered)
        {
            if (!shattered && _crack != null) return _crack;
            if (shattered && _shatter != null) return _shatter;
            const int n = 256;
            var px = new Color32[n * n];
            byte film = shattered ? (byte)70 : (byte)0;
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(235, 240, 245, film);
            var rng = new System.Random(shattered ? 91 : 17);
            float c = n * 0.5f;
            int rays = shattered ? 26 : 11;
            var ends = new List<Vector2>();
            for (int k = 0; k < rays; k++)
            {
                float angle = (k + (float)rng.NextDouble() * 0.6f) / rays * Mathf.PI * 2f;
                var p = new Vector2(c, c);
                float len = n * (0.32f + 0.17f * (float)rng.NextDouble());
                for (float run = 0f; run < len; run += 6f)
                {
                    angle += ((float)rng.NextDouble() - 0.5f) * 0.35f;
                    var q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 6f;
                    Line(px, n, p, q, 1f - run / len * 0.6f);
                    // Forks.
                    if (rng.NextDouble() < 0.07)
                    {
                        float fa = angle + (rng.NextDouble() < 0.5 ? -0.6f : 0.6f);
                        Line(px, n, q, q + new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)) * n * 0.08f, 0.6f);
                    }
                    p = q;
                }
                ends.Add(p);
            }
            // Concentric cross-cracks.
            int rings = shattered ? 6 : 3;
            for (int ring = 1; ring <= rings; ring++)
            {
                float radius = n * 0.42f * ring / (rings + 0.5f);
                for (int k = 0; k < rays; k++)
                {
                    if (rng.NextDouble() < 0.35) continue;
                    float a0 = (k + 0.2f) / rays * Mathf.PI * 2f, a1 = (k + 0.9f) / rays * Mathf.PI * 2f;
                    float r0 = radius * (0.9f + 0.2f * (float)rng.NextDouble()), r1 = radius * (0.9f + 0.2f * (float)rng.NextDouble());
                    Line(px, n, new Vector2(c + Mathf.Cos(a0) * r0, c + Mathf.Sin(a0) * r0), new Vector2(c + Mathf.Cos(a1) * r1, c + Mathf.Sin(a1) * r1), 0.75f);
                }
            }
            // The chip where the bullet went through, with a dark hole.
            for (int y = -7; y <= 7; y++)
                for (int x = -7; x <= 7; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    if (d > 7f) continue;
                    int i = ((int)c + y) * n + (int)c + x;
                    px[i] = d < 2.5f ? new Color32(20, 20, 20, 255) : new Color32(245, 248, 250, (byte)(230 - d * 12f));
                }
            // Fade out at the rim so the quad's edge never shows.
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                    float fade = Mathf.Clamp01((1f - d) / 0.15f);
                    var p = px[y * n + x];
                    p.a = (byte)(p.a * fade);
                    px[y * n + x] = p;
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = shattered ? "GlassShatter" : "GlassCrack", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true);
            if (shattered) _shatter = tex; else _crack = tex;
            return tex;
        }

        static void Line(Color32[] px, int n, Vector2 a, Vector2 b, float strength)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 1.5f) + 1;
            byte alpha = (byte)(Mathf.Clamp01(strength) * 235f);
            for (int s = 0; s <= steps; s++)
            {
                var p = Vector2.Lerp(a, b, s / (float)steps);
                int x = (int)p.x, y = (int)p.y;
                for (int oy = 0; oy <= 1; oy++)
                    for (int ox = 0; ox <= 1; ox++)
                    {
                        int xx = x + ox, yy = y + oy;
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        int i = yy * n + xx;
                        if (px[i].a < alpha) px[i] = new Color32(240, 244, 248, alpha);
                    }
            }
        }
    }
}

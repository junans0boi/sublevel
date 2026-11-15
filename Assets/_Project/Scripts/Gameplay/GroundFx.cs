using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // The ground reacts to what happens on it (#205 #207, ADR-163): footprints in soft ground (a capped pool of quads
    // that fade), low plants bent away from walkers (shader global _GameBends), water rings and droplets when someone
    // wades or swims, and in rain: splash rings on the ground, a wetness that darkens and glosses terrain and roads
    // (shader global _GameWet) and puddles. Added to the World by SkyAmbient; every peer draws its own from what it
    // sees (steps come from CombatantAudio on every combatant).
    public class GroundFx : MonoBehaviour
    {
        const float PrintRange = 45f, WadeTick = 0.12f, ActiveRange = 40f, BendRadius = 1.15f, SplashRadius = 13f, PrintLift = 0.02f;
        const int BendSlots = 4;

        struct Print
        {
            public Transform T;
            public Material M;
            public float Born, Life, Alpha;
            public Color Colour;
        }

        public static GroundFx Current { get; private set; }

        public float Wet { get; private set; }
        public int PrintsAlive { get; private set; }
        public int PrintsMade { get; private set; }
        public Vector3 LastPrint { get; private set; }
        public int RingsMade { get; private set; }
        public int SplashesMade { get; private set; }

        public Transform Root { get; private set; }
        Print[] _prints;
        Mesh _quad;
        ParticleSystem _rings, _drops;
        readonly Vector4[] _bends = new Vector4[BendSlots];
        float _wadeTick, _printTick, _splashDebt, _wadeDebt;
        bool _soaked;
        System.Collections.Generic.Dictionary<int, bool> _leftFoot = new System.Collections.Generic.Dictionary<int, bool>();
        System.Collections.Generic.Dictionary<int, Vector3> _last = new System.Collections.Generic.Dictionary<int, Vector3>();
        static readonly RaycastHit[] Hits = new RaycastHit[8];
        static readonly int WetId = Shader.PropertyToID("_GameWet"), BendsId = Shader.PropertyToID("_GameBends");
        static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            Current = this;
            Root = FxKit.Root(this, "GroundFxParts");
            _prints = new Print[GroundFxRules.MaxPrints];
            _quad = new Mesh { name = "PrintQuad" };
            // Flat on the ground, facing up; v runs along the foot (toe at +z).
            _quad.SetVertices(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) });
            _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quad.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            _quad.RecalculateBounds();
            _rings = FxKit.Particles("WaterRings", Root, FxKit.Material(FxKit.Sprite.Ring, false), 160, ParticleSystemRenderMode.HorizontalBillboard);
            _drops = FxKit.Particles("WaterDrops", Root, FxKit.Material(FxKit.Sprite.Dot, false), 160);
            // A ring grows to full size and fades as it spreads.
            var rs = _rings.sizeOverLifetime;
            rs.enabled = true;
            rs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(1f, 1f)));
            var rc = _rings.colorOverLifetime;
            rc.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            rc.color = g;
            var dm = _drops.main;
            dm.gravityModifier = 2.2f;
            var dr = _drops.sizeOverLifetime;
            dr.enabled = false;
            CombatantAudio.StepTaken += OnStep;
            Shader.SetGlobalFloat(WetId, 0f);
            Shader.SetGlobalVectorArray(BendsId, _bends);
        }

        void OnDestroy()
        {
            CombatantAudio.StepTaken -= OnStep;
            if (Current == this) Current = null;
            Shader.SetGlobalFloat(WetId, 0f);
            Shader.SetGlobalVectorArray(BendsId, new Vector4[BendSlots]);
            foreach (var p in _prints) if (p.M != null) Destroy(p.M);
            if (_quad != null) Destroy(_quad);
        }

        // Light of the hour as a colour scale, so unlit droplets are not bright against a dark night.
        static float Light()
        {
            var v = EnvironmentView.Current;
            return v == null ? 1f : Mathf.Clamp01(0.18f + v.Look.Ambient * 0.9f);
        }

        void Update()
        {
            var view = EnvironmentView.Current;
            bool raining = view != null && view.Raining;
            // A match that starts in the rain has been wet for a while already.
            if (raining && !_soaked) { _soaked = true; if (Wet < 0.6f) Wet = 0.6f; }
            Wet = GroundFxRules.Wetness(Wet, raining, Time.deltaTime);
            Shader.SetGlobalFloat(WetId, Wet);
            UpdateBends();
            _printTick -= Time.deltaTime;
            if (_printTick <= 0f) { _printTick = 0.1f; FadePrints(); }
            _wadeTick -= Time.deltaTime;
            if (_wadeTick <= 0f) { Wade(WadeTick); _wadeTick = WadeTick; }
            if (raining) RainSplashes(view);
        }

        // ---- Footprints ----

        void OnStep(Transform who, FootSurface surface)
        {
            var cam = Camera.main;
            if (cam == null || who == null || (who.position - cam.transform.position).sqrMagnitude > PrintRange * PrintRange) return;
            if (surface == FootSurface.Water) { WaterStep(who); return; }
            var look = GroundFxRules.Footprint(surface, Wet, false);
            if (look.Alpha <= 0f) return;
            int id = who.GetInstanceID();
            _leftFoot.TryGetValue(id, out bool left);
            _leftFoot[id] = !left;
            if (_leftFoot.Count > 64) _leftFoot.Clear();
            var from = who.position + who.right * (left ? -0.09f : 0.09f) + Vector3.up * 0.4f;
            if (!Ground(from, who, 1.2f, out var point, out var normal)) return;
            PlacePrint(point + normal * PrintLift, Quaternion.LookRotation(Vector3.ProjectOnPlane(who.forward, normal), normal), look);
        }

        // The nearest ground under `from` that is not part of `who`.
        static bool Ground(Vector3 from, Transform who, float range, out Vector3 point, out Vector3 normal)
        {
            point = from; normal = Vector3.up;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.distance < best && (who == null || !h.collider.transform.IsChildOf(who)) && h.collider.GetComponentInParent<Combatant>() == null)
                {
                    best = h.distance; point = h.point; normal = h.normal; found = true;
                }
            }
            return found;
        }

        void PlacePrint(Vector3 position, Quaternion rotation, FootprintLook look)
        {
            int slot = 0;
            float oldest = float.MaxValue;
            for (int i = 0; i < _prints.Length; i++)
            {
                if (_prints[i].T == null) { slot = i; oldest = -1f; break; }
                float born = _prints[i].Born;
                if (born < oldest) { oldest = born; slot = i; }
            }
            ref var p = ref _prints[slot];
            if (p.T == null)
            {
                var go = new GameObject("Footprint");
                go.transform.SetParent(Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = _quad;
                var r = go.AddComponent<MeshRenderer>();
                p.M = new Material(FxKit.Material(FxKit.Sprite.Print, false));
                r.sharedMaterial = p.M;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                p.T = go.transform;
            }
            p.T.gameObject.SetActive(true);
            p.T.SetPositionAndRotation(position, rotation);
            p.T.localScale = new Vector3(look.Width, 1f, look.Length);
            p.Born = Time.time;
            p.Life = look.LifeSeconds;
            p.Alpha = look.Alpha;
            p.Colour = Color.Lerp(new Color(0.20f, 0.24f, 0.10f), new Color(0.045f, 0.035f, 0.03f), look.Dark);
            p.M.SetColor(BaseColourId, new Color(p.Colour.r, p.Colour.g, p.Colour.b, p.Alpha));
            PrintsMade++;
            LastPrint = position;
        }

        void FadePrints()
        {
            int alive = 0;
            for (int i = 0; i < _prints.Length; i++)
            {
                ref var p = ref _prints[i];
                if (p.T == null || !p.T.gameObject.activeSelf) continue;
                float f = GroundFxRules.Fade(Time.time - p.Born, p.Life);
                if (f <= 0f) { p.T.gameObject.SetActive(false); continue; }
                alive++;
                p.M.SetColor(BaseColourId, new Color(p.Colour.r, p.Colour.g, p.Colour.b, p.Alpha * f));
            }
            PrintsAlive = alive;
        }

        // ---- Grass pressed by walkers ----

        void UpdateBends()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var at = cam.transform.position;
            for (int i = 0; i < BendSlots; i++) _bends[i] = Vector4.zero;
            int slot = 0;
            // The local player first, then the nearest others.
            var me = Combatant.LocalPlayer;
            if (me != null && !me.Dead.Value) _bends[slot++] = new Vector4(me.transform.position.x, me.transform.position.z, BendRadius, 1f);
            for (int round = slot; round < BendSlots; round++)
            {
                Combatant best = null;
                float bestSq = 30f * 30f;
                foreach (var c in Combatant.All)
                {
                    if (c == null || c == me || c.Dead.Value) continue;
                    var d = c.transform.position - at;
                    float sq = d.x * d.x + d.z * d.z;
                    bool taken = false;
                    for (int k = 0; k < slot; k++) if (Mathf.Approximately(_bends[k].x, c.transform.position.x) && Mathf.Approximately(_bends[k].y, c.transform.position.z)) taken = true;
                    if (!taken && sq < bestSq) { bestSq = sq; best = c; }
                }
                if (best == null) break;
                _bends[slot++] = new Vector4(best.transform.position.x, best.transform.position.z, BendRadius, 1f);
            }
            Shader.SetGlobalVectorArray(BendsId, _bends);
        }

        // ---- Water ----

        // A foot in water: a ring and a few droplets at the surface.
        void WaterStep(Transform who)
        {
            float depth = WaterVolume.DepthAt(who.position, out float surface, out _);
            if (depth < 0.05f) return;
            Ring(new Vector3(who.position.x, surface + 0.02f, who.position.z), 1.5f, 0.6f);
            Drops(new Vector3(who.position.x, surface + 0.03f, who.position.z), 5, 1.6f);
        }

        // Wading and swimming leave a trail of rings behind anyone moving through water.
        void Wade(float dt)
        {
            var cam = Camera.main;
            if (cam == null) return;
            int checkedCount = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.Dead.Value) continue;
                var p = c.transform.position;
                if ((p - cam.transform.position).sqrMagnitude > ActiveRange * ActiveRange) continue;
                if (++checkedCount > 12) break;
                int id = c.GetInstanceID();
                _last.TryGetValue(id, out var last);
                _last[id] = p;
                if (_last.Count > 64) _last.Clear();
                if (last == Vector3.zero) continue;
                float speed = new Vector2(p.x - last.x, p.z - last.z).magnitude / dt;
                float depth = WaterVolume.DepthAt(p, out float surface, out _);
                if (depth < 0.1f || speed < 0.3f) continue;
                _wadeDebt += GroundFxRules.WadeRingsPerSecond(speed) * dt;
                while (_wadeDebt >= 1f)
                {
                    _wadeDebt -= 1f;
                    float swim = depth > 1.1f ? 1.6f : 1f;
                    var at = new Vector3(p.x + Random.Range(-0.25f, 0.25f), surface + 0.02f, p.z + Random.Range(-0.25f, 0.25f));
                    Ring(at, 1.2f * swim, 0.7f);
                    if (swim > 1f || Random.value < 0.3f) Drops(at, 2, 1.2f);
                }
            }
        }

        public void Ring(Vector3 at, float size, float alpha)
        {
            float l = Light();
            var e = new ParticleSystem.EmitParams
            {
                position = at, startSize = size, startLifetime = 0.9f,
                startColor = new Color(0.8f * l + 0.1f, 0.86f * l + 0.1f, 0.95f * l + 0.1f, alpha),
            };
            _rings.Emit(e, 1);
            RingsMade++;
        }

        public void Drops(Vector3 at, int count, float speed)
        {
            float l = Light();
            for (int i = 0; i < count; i++)
            {
                var v = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.8f, 1f), Random.Range(-0.5f, 0.5f)) * speed;
                _drops.Emit(new ParticleSystem.EmitParams
                {
                    position = at, velocity = v, startSize = Random.Range(0.03f, 0.06f), startLifetime = 0.55f,
                    startColor = new Color(0.85f * l + 0.1f, 0.9f * l + 0.1f, 1f * l, 0.75f),
                }, 1);
            }
        }

        // ---- Rain on the ground ----

        // Test hook for the splash cap: the most rays cast in one frame.
        public int RaysLastFrame { get; private set; }

        void RainSplashes(EnvironmentView view)
        {
            RaysLastFrame = 0;
            var cam = Camera.main;
            if (cam == null) return;
            float quality = GameSettings.HighEffects ? 1f : 0.5f;
            _splashDebt += GroundFxRules.SplashesPerSecond(view.Look.RainPerSecond, quality, view.Indoors) * Time.deltaTime;
            var sea = WorldInfo.Current != null ? WorldInfo.Current.SeaLevel : float.NegativeInfinity;
            var centre = cam.transform.position;
            int budget = GroundFxRules.MaxSplashesPerFrame;
            while (_splashDebt >= 1f)
            {
                _splashDebt -= 1f;
                if (budget-- <= 0) { _splashDebt = 0f; break; }
                float a = Random.value * Mathf.PI * 2f, d = Mathf.Sqrt(Random.value) * SplashRadius;
                var from = centre + new Vector3(Mathf.Cos(a) * d, 12f, Mathf.Sin(a) * d);
                RaysLastFrame++;
                if (!Physics.Raycast(from, Vector3.down, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                var at = hit.point + hit.normal * 0.02f;
                if (at.y < sea) at.y = sea + 0.02f; // sea and lakes: the drop lands on the surface
                Ring(at, 0.55f, 0.6f + 0.4f * Wet);
                Drops(at, 2, 0.9f);
                SplashesMade++;
            }
        }
    }
}

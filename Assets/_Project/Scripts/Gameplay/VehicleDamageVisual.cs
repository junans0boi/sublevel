using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // What vehicle damage looks like on every side (#90, ADR-074), driven by VehicleDamage's replicated state:
    // bullet holes (dark discs on the body, a ring of MaxDecals), cracked / shattered glass, white then black smoke from
    // the engine, fire, the explosion (a burst and a flash), and the burnt-out wreck (the body darkened). Flat tyres
    // need nothing here: the core lowers that corner (VehicleState.WheelDrop).
    // Uses the shot-effect materials of the scene's ThrowableView (vertex coloured, tinted by _BaseColor); without
    // one only the wreck tint and the glass show.
    [RequireComponent(typeof(VehicleDamage))]
    public class VehicleDamageVisual : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color WhiteSmoke = new Color(0.85f, 0.85f, 0.85f, 0.45f);
        static readonly Color BlackSmoke = new Color(0.08f, 0.08f, 0.08f, 0.7f);
        static readonly Color Fire = new Color(1f, 0.45f, 0.1f, 1f);
        static readonly Color Charred = new Color(0.1f, 0.09f, 0.085f, 1f);
        static readonly Color HoleColor = new Color(0.03f, 0.03f, 0.03f, 1f);

        VehicleDamage _damage;
        ParticleSystem _smoke, _fire, _burst;
        Light _flash;
        float _flashUntil;
        VehicleDamageStage _shown = (VehicleDamageStage)255;
        int _panesShown = -1;
        readonly List<Transform> _holes = new List<Transform>();
        int _holesPlaced;
        MaterialPropertyBlock _block;
        Material _glow, _dust;
        readonly List<(Renderer renderer, int index, Material original)> _glass = new List<(Renderer, int, Material)>();
        Material _crackedGlass, _goneGlass;

        void Awake()
        {
            _damage = GetComponent<VehicleDamage>();
            _block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            _damage.Holed += OnHoled;
            _damage.Exploded += OnExploded;
        }

        void OnDisable()
        {
            _damage.Holed -= OnHoled;
            _damage.Exploded -= OnExploded;
        }

        void Start()
        {
            var view = FindAnyObjectByType<ThrowableView>();
            if (view != null) { _glow = view.GlowMaterial; _dust = view.DustMaterial; }
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.EndsWith("_Glass")) _glass.Add((r, i, mats[i]));
            }
        }

        void Update()
        {
            var stage = _damage.CurrentStage;
            if (stage != _shown) ShowStage(stage);
            int panes = _damage.Panes.Value;
            if (panes != _panesShown) ShowGlass(panes);
            if (_flash != null && _flash.enabled && Time.time > _flashUntil) _flash.enabled = false;
        }

        void ShowStage(VehicleDamageStage stage)
        {
            _shown = stage;
            if (_dust != null && stage >= VehicleDamageStage.WhiteSmoke)
            {
                if (_smoke == null) _smoke = NewEmitter("Smoke", _dust, 2.4f, 1.1f, 0.9f, 2.6f);
                var main = _smoke.main;
                main.startColor = stage == VehicleDamageStage.WhiteSmoke ? WhiteSmoke : BlackSmoke;
                var emission = _smoke.emission;
                emission.rateOverTime = stage == VehicleDamageStage.WhiteSmoke ? 5f : stage == VehicleDamageStage.Wrecked ? 4f : 10f;
                if (!_smoke.isPlaying) _smoke.Play();
            }
            if (_glow != null && stage == VehicleDamageStage.Burning)
            {
                if (_fire == null) _fire = NewEmitter("Fire", _glow, 0.6f, 1.6f, 0.55f, 1.2f);
                var main = _fire.main;
                main.startColor = Fire;
                var emission = _fire.emission;
                emission.rateOverTime = 28f;
                _fire.Play();
            }
            if (stage == VehicleDamageStage.Wrecked)
            {
                if (_fire != null) _fire.Stop();
                // The burnt-out shell: the body and everything on it darkened.
                foreach (var r in GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.GetComponent<ParticleSystem>() != null || _holes.Contains(r.transform)) continue;
                    r.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, Charred);
                    r.SetPropertyBlock(_block);
                }
            }
        }

        // shortcut: the glass is one material on the whole body mesh, so every pane shows the worst state seen
        // (any cracked -> all look cracked; half the panes shattered -> the glass is gone). Per-pane needs the panes
        // as separate meshes in VehicleModels; upgrade with the dents / parts follow-up (ADR-074).
        void ShowGlass(int panes)
        {
            _panesShown = panes;
            if (_glass.Count == 0) return;
            int cracked = 0, shattered = 0;
            for (int p = 0; p < VehicleDamageMath.PaneCount; p++)
            {
                int s = VehicleDamageMath.GetPane(panes, p);
                if (s == 1) cracked++;
                else if (s >= 2) shattered++;
            }
            Material use = null;
            if (shattered >= VehicleDamageMath.PaneCount / 2) use = Gone();
            else if (cracked + shattered > 0) use = Cracked();
            foreach (var (r, index, original) in _glass)
            {
                var mats = r.sharedMaterials;
                mats[index] = use != null ? use : original;
                r.sharedMaterials = mats;
            }
        }

        Material Cracked()
        {
            if (_crackedGlass != null) return _crackedGlass;
            _crackedGlass = new Material(_glass[0].original) { name = "VehicleGlass_Cracked" };
            // Milky and less see-through: a crazed pane.
            if (_crackedGlass.HasProperty(BaseColorId)) _crackedGlass.SetColor(BaseColorId, new Color(0.92f, 0.94f, 0.95f, 0.78f));
            return _crackedGlass;
        }

        Material Gone()
        {
            if (_goneGlass != null) return _goneGlass;
            _goneGlass = new Material(_glass[0].original) { name = "VehicleGlass_Gone" };
            if (_goneGlass.HasProperty(BaseColorId)) _goneGlass.SetColor(BaseColorId, new Color(1f, 1f, 1f, 0f));
            return _goneGlass;
        }

        void OnHoled(Vector3 local, Vector3 localNormal)
        {
            if (_dust == null) return;
            var rules = VehicleDamageRules.For(_damage.Vehicle.Kind);
            int slot = VehicleDamageMath.DecalSlot(_holesPlaced++, rules.MaxDecals);
            Transform hole;
            if (slot < _holes.Count) hole = _holes[slot];
            else
            {
                var go = new GameObject("BulletHole");
                go.AddComponent<MeshFilter>().sharedMesh = CombatFxMeshes.Disc;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _dust;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, HoleColor);
                mr.SetPropertyBlock(_block);
                hole = go.transform;
                hole.SetParent(transform, false);
                _holes.Add(hole);
            }
            var n = localNormal.sqrMagnitude > 0f ? localNormal.normalized : Vector3.up;
            // A hair off the surface, lying flat on it.
            hole.localPosition = local + n * 0.004f;
            hole.localRotation = Quaternion.LookRotation(-n);
            hole.localScale = Vector3.one * rules.DecalSize;
        }

        void OnExploded()
        {
            if (_glow != null)
            {
                if (_burst == null)
                {
                    _burst = NewEmitter("Explosion", _glow, 0.7f, 7f, 1.3f, 0.6f);
                    var emission = _burst.emission;
                    emission.rateOverTime = 0f;
                }
                var main = _burst.main;
                main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
                _burst.Emit(60);
            }
            if (_flash == null)
            {
                _flash = new GameObject("ExplosionFlash").AddComponent<Light>();
                _flash.transform.SetParent(transform, false);
                _flash.transform.localPosition = _damage.LocalBounds().center;
                _flash.type = LightType.Point;
                _flash.color = new Color(1f, 0.6f, 0.25f);
                _flash.range = 14f;
                _flash.intensity = 6f;
                _flash.shadows = LightShadows.None;
            }
            _flash.enabled = true;
            _flashUntil = Time.time + 0.35f;
        }

        // A world-space emitter at the front top of the body (the engine), rising.
        ParticleSystem NewEmitter(string name, Material material, float lifetime, float speed, float size, float growTo)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var b = _damage.LocalBounds();
            bool bike = _damage.Vehicle.IsMotorcycle;
            go.transform.localPosition = bike ? b.center : new Vector3(b.center.x, b.max.y - b.size.y * 0.35f, b.min.z + b.size.z * 0.85f);
            go.transform.localRotation = Quaternion.LookRotation(Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.3f;
            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, growTo));
            var colorOver = ps.colorOverLifetime;
            colorOver.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colorOver.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }
    }
}

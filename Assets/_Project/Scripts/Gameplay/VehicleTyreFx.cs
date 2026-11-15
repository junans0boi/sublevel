using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // What the wheels do to the ground, on every side from the vehicle's own motion (#203, ADR-155):
    //  - dust on dirt, grass specks on grass, white spray in water, tyre smoke when sliding on tarmac;
    //  - skid marks on tarmac (only while sliding) and faint tracks on dirt / grass, drawn as fading trails flat on the ground.
    // The amount comes from VehicleFxRules. Slip01 and Ground are read by VehicleAudio for the tyre screech.
    // Added at run time by VehicleAudio. Needs the scene's effect materials (ThrowableView); without them only the numbers run.
    [DefaultExecutionOrder(305)]
    public class VehicleTyreFx : MonoBehaviour
    {
        const float NearRange = 110f;       // beyond this from the camera nothing is drawn
        const float SurfaceEvery = 0.15f;
        static readonly RaycastHit[] Hits = new RaycastHit[8];
        static Material _skidMat;

        sealed class Wheel
        {
            public TrailRenderer Trail;
            public bool Grounded;
            public Vector3 Point, Normal;
            public FootSurface Surface = FootSurface.Concrete;
            public float Acc;
        }

        Vehicle _vehicle;
        Wheel[] _wheels;
        ParticleSystem _ps;
        Material _dustMat;
        Vector3 _last;
        float _surfaceAt;
        int _surfaceWheel;
        bool _ready, _haveLast;
        readonly System.Random _rng = new System.Random(2030);

        // For tests and captures.
        public float Slip01 { get; private set; }
        public FootSurface Ground { get; private set; } = FootSurface.Concrete;
        public int Emitted { get; private set; }
        public bool Skidding { get; private set; }

        void Awake() => _vehicle = GetComponent<Vehicle>();

        void Start()
        {
            var view = FindAnyObjectByType<ThrowableView>();
            if (view != null && view.DustMaterial != null)
            {
                _dustMat = VehicleDamageVisual.WithTexture(view.DustMaterial, VehicleDamageVisual.Puff(), "_Tyre");
                if (_skidMat == null) _skidMat = VehicleDamageVisual.WithTexture(view.DustMaterial, Texture2D.whiteTexture, "_Skid");
            }
            int n = _vehicle.WheelCount;
            _wheels = new Wheel[n];
            for (int i = 0; i < n; i++) _wheels[i] = new Wheel();
            if (_dustMat != null)
            {
                _ps = NewSystem();
                for (int i = 0; i < n; i++) _wheels[i].Trail = NewTrail(i);
            }
            _ready = true;
        }

        ParticleSystem NewSystem()
        {
            var go = new GameObject("TyreDust");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 260;
            main.startLifetime = 1.2f;
            main.startSpeed = 0f;
            main.startSize = 0.5f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.2f));
            VehicleDamageVisual.SetColorOverLife(ps, new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (0.12f, 1f), (1f, 0f) });
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _dustMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        TrailRenderer NewTrail(int wheel)
        {
            var go = new GameObject("SkidTrail" + wheel);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TrailRenderer>();
            t.sharedMaterial = _skidMat;
            t.alignment = LineAlignment.TransformZ;
            t.textureMode = LineTextureMode.Stretch;
            t.time = 16f;
            t.minVertexDistance = 0.18f;
            t.widthMultiplier = _vehicle.IsMotorcycle ? 0.1f : 0.22f;
            t.shadowCastingMode = ShadowCastingMode.Off;
            t.receiveShadows = false;
            t.emitting = false;
            t.numCapVertices = 0;
            return t;
        }

        void LateUpdate()
        {
            if (!_ready) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var pos = transform.position;
            var vel = _haveLast ? (pos - _last) / dt : Vector3.zero;
            if (vel.sqrMagnitude > 3600f) vel = Vector3.zero; // a teleport
            _last = pos;
            _haveLast = true;
            float along = Vector3.Dot(vel, transform.forward), side = Vector3.Dot(vel, transform.right);
            float decel = -_vehicle.ShownAccel * Mathf.Sign(_vehicle.Speed);
            float speed = vel.magnitude;
            var cam = PlayerController.Local != null ? PlayerController.Local.Camera : null;
            bool near = cam == null || (cam.transform.position - pos).sqrMagnitude < NearRange * NearRange;
            Slip01 = Mathf.MoveTowards(Slip01, VehicleFxRules.Slip(side, decel, along, false), 6f * dt);
            if (!near || speed < 0.5f)
            {
                SetTrails(false);
                return;
            }
            float radius = _vehicle.Rules.WheelRadius;
            if (Time.time - _surfaceAt > SurfaceEvery / Mathf.Max(1, _wheels.Length))
            {
                _surfaceAt = Time.time;
                _surfaceWheel = (_surfaceWheel + 1) % _wheels.Length;
            }
            bool skid = false;
            for (int i = 0; i < _wheels.Length; i++)
            {
                var w = _wheels[i];
                var pivot = _vehicle.WheelPivot(i);
                w.Grounded = pivot != null && CastGround(pivot.position, radius, out w.Point, out w.Normal);
                if (!w.Grounded) { if (w.Trail != null) w.Trail.emitting = false; continue; }
                if (i == _surfaceWheel) w.Surface = CombatantAudio.SurfaceAt(w.Point);
                if (i == _wheels.Length - 1 || (_wheels.Length > 2 && i == 2)) Ground = w.Surface;
                var kind = VehicleFxRules.DustFor(w.Surface, Slip01);
                float rate = VehicleFxRules.DustRate(kind, speed, Slip01) * (_vehicle.IsFrontWheel(i) ? 0.55f : 1f);
                Emit(w, kind, rate, vel, dt);
                // Marks: tarmac only while sliding; dirt and grass leave tracks when rolling, darker when sliding.
                bool paved = w.Surface == FootSurface.Concrete || w.Surface == FootSurface.Metal || w.Surface == FootSurface.Wood;
                bool mark = paved ? Slip01 > 0.3f : (w.Surface == FootSurface.Dirt || w.Surface == FootSurface.Grass) && speed > 3f;
                if (w.Trail != null)
                {
                    w.Trail.transform.SetPositionAndRotation(w.Point + w.Normal * 0.025f, Quaternion.LookRotation(w.Normal, transform.forward));
                    if (mark && !w.Trail.emitting) w.Trail.Clear();
                    w.Trail.emitting = mark;
                    if (mark) Tint(w.Trail, w.Surface, Slip01);
                }
                skid |= mark && paved;
            }
            Skidding = skid;
        }

        void SetTrails(bool on)
        {
            Skidding = false;
            foreach (var w in _wheels) if (w.Trail != null) w.Trail.emitting = on;
        }

        // The ground right under a wheel: the nearest hit that is not this vehicle.
        bool CastGround(Vector3 centre, float radius, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.up;
            int n = Physics.RaycastNonAlloc(centre, -transform.up, Hits, radius + 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider.GetComponentInParent<Vehicle>() != null) continue;
                if (Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                point = Hits[i].point;
                normal = Hits[i].normal;
                found = true;
            }
            return found;
        }

        static void Tint(TrailRenderer t, FootSurface surface, float slip)
        {
            bool paved = surface == FootSurface.Concrete || surface == FootSurface.Metal || surface == FootSurface.Wood;
            Color c = paved ? new Color(0.03f, 0.03f, 0.03f) : surface == FootSurface.Grass ? new Color(0.13f, 0.17f, 0.06f) : new Color(0.17f, 0.12f, 0.07f);
            float a = paved ? 0.7f : Mathf.Lerp(0.28f, 0.55f, slip);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a, 0.04f), new GradientAlphaKey(a, 0.55f), new GradientAlphaKey(0f, 1f) });
            t.colorGradient = g;
        }

        void Emit(Wheel w, VehicleFxRules.DustKind kind, float rate, Vector3 vel, float dt)
        {
            if (_ps == null || rate <= 0f) return;
            w.Acc += rate * dt;
            while (w.Acc >= 1f)
            {
                w.Acc -= 1f;
                Color c;
                float up, spread, life, size;
                switch (kind)
                {
                    case VehicleFxRules.DustKind.Spray: c = new Color(0.9f, 0.94f, 0.98f, 0.55f); up = 2.2f; spread = 1.4f; life = 0.7f; size = 0.35f; break;
                    case VehicleFxRules.DustKind.Smoke: c = new Color(0.88f, 0.88f, 0.88f, 0.7f); up = 0.8f; spread = 0.8f; life = 2.0f; size = 0.8f; break;
                    case VehicleFxRules.DustKind.Grass: c = new Color(0.5f, 0.55f, 0.3f, 0.7f); up = 1.6f; spread = 1.1f; life = 1.3f; size = 0.55f; break;
                    default: c = new Color(0.72f, 0.62f, 0.46f, 0.8f); up = 1.2f; spread = 1.1f; life = 1.9f; size = 0.9f; break;
                }
                var p = new ParticleSystem.EmitParams
                {
                    position = w.Point + Rand3() * 0.18f + Vector3.up * 0.08f,
                    velocity = -vel * 0.18f + Vector3.up * up * (0.5f + R()) + Rand3() * spread,
                    startColor = c,
                    startSize = size * (0.7f + R() * 0.8f),
                    startLifetime = life * (0.7f + R() * 0.6f),
                };
                _ps.Emit(p, 1);
                Emitted++;
            }
        }

        float R() => (float)_rng.NextDouble();
        Vector3 Rand3() => new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 2f;
    }
}

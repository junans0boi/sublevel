using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Small living things of the outdoor map (#206, ADR-163): butterflies by day, fireflies at night, leaves coming off
    // the trees, flocks of birds crossing the sky and bursting out of the trees at gunfire, fish jumping near the coast.
    // Everything is a handful of particles around the camera (nothing exists beyond ~40 m), counts from WildlifeRules
    // for the hour, weather and place (read from AmbientBeds). Added to the World by SkyAmbient.
    public class Wildlife : MonoBehaviour
    {
        const float SpawnRadius = 24f, ButterflyLife = 14f, FireflyLife = 12f, LeafLife = 9f, BirdLife = 16f;

        public static Wildlife Current { get; private set; }

        public WildlifeLevels Levels { get; private set; }
        public int ButterfliesAlive => _butterflies != null ? _butterflies.particleCount : 0;
        public int FirefliesAlive => _fireflies != null ? _fireflies.particleCount : 0;
        public int LeavesAlive => _leaves != null ? _leaves.particleCount : 0;
        public int BirdsAlive => _birds != null ? _birds.particleCount : 0;
        public int Scatters { get; private set; }
        public int FishSplashes { get; private set; }

        public Transform Root { get; private set; }
        ParticleSystem _butterflies, _fireflies, _leaves, _birds;
        AmbientBeds _beds;
        float _tick, _flockIn = 6f, _fishIn = 8f, _scatterCooldown;
        float _butterflyDebt, _fireflyDebt, _leafDebt;

        void Awake()
        {
            Current = this;
            Root = FxKit.Root(this, "WildlifeParts");
            _beds = GetComponent<AmbientBeds>();
            _butterflies = Make("Butterflies", FxKit.Sprite.Wing, false, WildlifeRules.MaxButterflies + 4);
            Flap(_butterflies, 9f, 0.12f);
            Wander(_butterflies, 1.6f);
            Fade(_butterflies, false);
            _fireflies = Make("Fireflies", FxKit.Sprite.Dot, true, WildlifeRules.MaxFireflies + 8);
            Wander(_fireflies, 1.1f);
            Fade(_fireflies, true);
            _leaves = Make("Leaves", FxKit.Sprite.Leaf, false, 120);
            var lm = _leaves.main;
            lm.gravityModifier = 0.05f;
            var rot = _leaves.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            Wander(_leaves, 0.9f);
            Fade(_leaves, false);
            _birds = Make("Birds", FxKit.Sprite.Wing, false, 40);
            Flap(_birds, 6f, 0.5f);
            Fade(_birds, false);
        }

        void OnDestroy() { if (Current == this) Current = null; }

        // Where one alive creature of a kind is, nearest to `to` (captures and tests aim at it).
        public bool Nearest(char kind, Vector3 to, out Vector3 position)
        {
            var ps = kind == 'b' ? _birds : kind == 'f' ? _fireflies : _butterflies;
            var buffer = new ParticleSystem.Particle[ps.particleCount];
            int n = ps.GetParticles(buffer);
            position = default;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float sq = (buffer[i].position - to).sqrMagnitude;
                if (sq < best) { best = sq; position = buffer[i].position; }
            }
            return n > 0;
        }

        ParticleSystem Make(string name, FxKit.Sprite sprite, bool glow, int max) => FxKit.Particles(name, Root, FxKit.Material(sprite, glow), max);

        // A wing beat: the sprite's width squeezes in and out; `beats` per lifetime of one second-ish, `floor` is the closed width.
        static void Flap(ParticleSystem ps, float hertz, float floor)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;
            var keys = new Keyframe[60];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(i / (keys.Length - 1f), i % 2 == 0 ? 1f : floor);
            size.x = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
            size.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
            size.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
            // shortcut: the flap rate follows the lifetime (60 half-beats per life), fine for 10-16 s lives; give birds a timed curve if it ever reads wrong.
        }

        static void Wander(ParticleSystem ps, float strength)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.4f;
            noise.damping = true;
        }

        // In for the first tenth, out for the last: nothing pops. Fireflies blink in between.
        static void Fade(ParticleSystem ps, bool blink)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (blink)
            {
                var keys = new GradientAlphaKey[8];
                for (int i = 0; i < 8; i++) keys[i] = new GradientAlphaKey(i == 0 || i == 7 ? 0f : (i % 2 == 1 ? 1f : 0.12f), i / 7f);
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f) }, keys);
            }
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _tick -= Time.deltaTime;
            if (_tick <= 0f)
            {
                _tick = 0.5f;
                var view = EnvironmentView.Current;
                var input = _beds != null ? _beds.Input : default;
                bool indoors = _beds != null && _beds.Input.Indoors || view != null && view.Indoors;
                Levels = WildlifeRules.Mix(view != null ? view.Hour : WorldSky.DayHour, view != null ? view.Weather : WeatherKind.Clear,
                    input.Region, indoors, input.Shore, _beds != null ? _beds.Levels.WindBoost : 0f);
            }
            float quality = GameSettings.HighEffects ? 1f : 0.5f;
            var at = cam.transform.position;
            _butterflyDebt += Levels.Butterflies * quality / ButterflyLife * Time.deltaTime * HoldFactor(_butterflies, Levels.Butterflies * quality);
            _fireflyDebt += Levels.Fireflies * quality / FireflyLife * Time.deltaTime * HoldFactor(_fireflies, Levels.Fireflies * quality);
            _leafDebt += Levels.LeavesPerSecond * quality * Time.deltaTime;
            while (_butterflyDebt >= 1f) { _butterflyDebt -= 1f; SpawnLow(_butterflies, at, 0.6f, 2.4f, 0.34f, new Color(1f, 0.85f, 0.35f, 1f), new Color(0.95f, 0.95f, 1f, 1f), ButterflyLife, 0.5f); }
            while (_fireflyDebt >= 1f) { _fireflyDebt -= 1f; SpawnLow(_fireflies, at, 0.4f, 2.2f, 0.16f, new Color(0.7f, 1f, 0.35f, 0.9f), new Color(0.9f, 1f, 0.5f, 0.9f), FireflyLife, 0.3f); }
            while (_leafDebt >= 1f) { _leafDebt -= 1f; SpawnLeaf(at); }

            _flockIn -= Time.deltaTime;
            if (_flockIn <= 0f) { _flockIn = Random.Range(25f, 55f); if (Levels.BirdsFlying > 0.5f) Flock(at); }
            _scatterCooldown -= Time.deltaTime;
            if (_scatterCooldown <= 0f && Levels.BirdsFlying > 0.5f && SpatialAudio.Loud01 > 0.5f) { _scatterCooldown = 10f; Scatter(at); }
            _fishIn -= Time.deltaTime;
            if (_fishIn <= 0f) { _fishIn = Random.Range(8f, 22f); if (Levels.FishSplashPerMinute > 0.5f) FishSplash(at); }
        }

        // The spawn rate only needs to top up what has gone: fewer new ones the nearer the cap.
        static float HoldFactor(ParticleSystem ps, float target) => target <= 0.01f ? 0f : Mathf.Clamp01((target - ps.particleCount) / Mathf.Max(1f, target) * 2f + 0.15f) * 2f;

        static bool GroundAt(Vector3 xz, float from, out float y)
        {
            bool hit = Physics.Raycast(new Vector3(xz.x, from, xz.z), Vector3.down, out var h, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            y = h.point.y;
            return hit;
        }

        Vector3 Around(Vector3 at, float min, float max)
        {
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(min, max);
            return at + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
        }

        // Hovering things (butterflies, fireflies) a little above the ground within reach of the camera.
        void SpawnLow(ParticleSystem ps, Vector3 at, float minH, float maxH, float size, Color a, Color b, float life, float speed)
        {
            var p = Around(at, 4f, SpawnRadius);
            if (!GroundAt(p, at.y + 20f, out float y)) return;
            var sea = WorldInfo.Current != null ? WorldInfo.Current.SeaLevel : float.NegativeInfinity;
            if (y < sea) return;
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = new Vector3(p.x, y + Random.Range(minH, maxH), p.z),
                velocity = Random.onUnitSphere * speed,
                startSize = size * Random.Range(0.8f, 1.3f),
                startLifetime = life * Random.Range(0.7f, 1f),
                startColor = Color.Lerp(a, b, Random.value) * Tint(),
                rotation = Random.value * 360f,
            }, 1);
        }

        float Tint() => EnvironmentView.Current == null ? 1f : Mathf.Clamp01(0.25f + EnvironmentView.Current.Look.Ambient);

        void SpawnLeaf(Vector3 at)
        {
            var p = Around(at, 2f, 20f);
            var wind = new Vector3(0.85f, 0f, 0.53f) * Random.Range(0.8f, 2.2f);
            var colour = Random.value < 0.5f ? new Color(0.35f, 0.5f, 0.18f) : Random.value < 0.5f ? new Color(0.75f, 0.6f, 0.2f) : new Color(0.5f, 0.3f, 0.14f);
            colour *= Tint();
            colour.a = 1f;
            _leaves.Emit(new ParticleSystem.EmitParams
            {
                position = new Vector3(p.x, at.y + Random.Range(2.5f, 8f), p.z),
                velocity = wind + Vector3.down * 0.3f,
                startSize = Random.Range(0.1f, 0.18f),
                startLifetime = LeafLife * Random.Range(0.8f, 1.1f),
                startColor = colour,
                rotation = Random.value * 360f,
            }, 1);
        }

        // A few birds crossing high overhead in a loose line.
        public void Flock(Vector3 at)
        {
            int n = Random.Range(3, WildlifeRules.MaxBirds + 1);
            float a = Random.value * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0.04f, Mathf.Sin(a));
            var start = at - dir * 80f + Vector3.up * Random.Range(22f, 40f);
            for (int i = 0; i < n; i++)
                _birds.Emit(new ParticleSystem.EmitParams
                {
                    position = start + new Vector3(Random.Range(-6f, 6f), Random.Range(-3f, 3f), Random.Range(-6f, 6f)) - dir * i * 2.5f,
                    velocity = dir * Random.Range(9f, 11f), startSize = 2.2f, startLifetime = BirdLife,
                    startColor = new Color(0.08f, 0.08f, 0.09f, 1f) * Tint() + new Color(0, 0, 0, 1f), rotation = 0f,
                }, 1);
        }

        // Gunfire: birds burst out of the trees round the listener and climb away.
        public void Scatter(Vector3 at)
        {
            Scatters++;
            int n = Random.Range(4, 8);
            for (int i = 0; i < n; i++)
            {
                var p = Around(at, 10f, 32f);
                if (!GroundAt(p, at.y + 30f, out float y)) continue;
                var away = (p - at).normalized;
                _birds.Emit(new ParticleSystem.EmitParams
                {
                    position = new Vector3(p.x, y + Random.Range(5f, 10f), p.z),
                    velocity = away * Random.Range(3f, 6f) + Vector3.up * Random.Range(4f, 8f), startSize = 1.6f, startLifetime = 9f,
                    startColor = new Color(0.1f, 0.1f, 0.1f, 1f), rotation = 0f,
                }, 1);
            }
        }

        // A fish leaps out of open water near the coast.
        public void FishSplash(Vector3 at)
        {
            var gfx = GroundFx.Current;
            if (gfx == null) return;
            for (int i = 0; i < 6; i++)
            {
                var p = Around(at, 8f, 30f);
                float depth = WaterVolume.DepthAt(new Vector3(p.x, at.y, p.z), out float surface, out _);
                if (depth < 1f) continue;
                var s = new Vector3(p.x, surface + 0.03f, p.z);
                gfx.Ring(s, 2.2f, 0.8f);
                gfx.Drops(s, 7, 2.6f);
                FishSplashes++;
                return;
            }
        }
    }
}

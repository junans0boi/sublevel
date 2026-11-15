using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // What keeps the night readable (#208, ADR-163): a field of stars and a moon in the sky (the moonlight itself is the
    // fill light of WorldSky), and warm light from some windows: a soft glow on lit panes seen from outside, and two
    // spare real lights on the nearest ones. Nothing here at day. Added to the World by SkyAmbient.
    public class NightFx : MonoBehaviour
    {
        const int Stars = 280;
        const float SkyRadius = 300f, WindowRange = 90f, LightRange = 45f;

        public static NightFx Current { get; private set; }

        public float NightLevel { get; private set; }
        public float StarAlpha { get; private set; }
        public int LitWindows { get; private set; }
        public int WindowLightsOn { get; private set; }

        public Transform Root { get; private set; }
        ParticleSystem _stars, _moon, _glows;
        Material _starMat, _moonMat;
        Light[] _lights;
        Transform _sun;
        float _tick;
        ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[200];

        void Awake()
        {
            Current = this;
            Root = FxKit.Root(this, "NightParts");
            _starMat = new Material(FxKit.Material(FxKit.Sprite.Dot, true));
            _moonMat = new Material(FxKit.Material(FxKit.Sprite.Dot, true));
            _stars = FxKit.Particles("Stars", Root, _starMat, Stars);
            var m = _stars.main;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.startLifetime = 1e6f;
            var rng = new System.Random(77);
            for (int i = 0; i < Stars; i++)
            {
                // Upper hemisphere (a little below the horizon is hidden by hills anyway).
                float y = 0.05f + (float)rng.NextDouble() * 0.95f, a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt(1f - y * y);
                float b = 0.35f + (float)rng.NextDouble() * 0.65f;
                _stars.Emit(new ParticleSystem.EmitParams
                {
                    position = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) * SkyRadius,
                    startSize = (1.2f + 2.4f * b * b) * 1.4f, startLifetime = 1e6f,
                    startColor = Color.Lerp(new Color(0.75f, 0.82f, 1f), new Color(1f, 0.95f, 0.85f), (float)rng.NextDouble()) * b,
                }, 1);
            }
            _moon = FxKit.Particles("Moon", Root, _moonMat, 1);
            var mm = _moon.main;
            mm.simulationSpace = ParticleSystemSimulationSpace.Local;
            _moon.Emit(new ParticleSystem.EmitParams { position = Vector3.forward * SkyRadius * 0.9f, startSize = 34f, startLifetime = 1e6f, startColor = new Color(0.9f, 0.94f, 1f, 1f) }, 1);
            _glows = FxKit.Particles("WindowGlow", Root, FxKit.Material(FxKit.Sprite.Dot, true), 200);
            var gm = _glows.main;
            gm.startLifetime = 100f;
            _lights = new Light[2];
            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject("WindowLight" + i);
                go.transform.SetParent(Root, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.72f, 0.4f);
                l.range = 9f;
                l.intensity = 1.5f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                _lights[i] = l;
            }
        }

        void OnDestroy()
        {
            Current = null;
            if (_starMat != null) Destroy(_starMat);
            if (_moonMat != null) Destroy(_moonMat);
        }

        // Whether this pane is one of the lit ones (a third of them, the same on every peer).
        public static bool IsLit(int paneId) => (uint)(paneId * 2654435761u >> 8) % 100u < 35u;

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var view = EnvironmentView.Current;
            float hour = view != null ? view.Hour : WorldSky.DayHour;
            var weather = view != null ? view.Weather : WeatherKind.Clear;
            NightLevel = 1f - AmbientRules.Day(hour);
            // Cloud and rain hide the stars; fog dims them.
            StarAlpha = NightLevel * (weather == WeatherKind.Clear ? 1f : weather == WeatherKind.Fog ? 0.25f : 0f);
            Root.position = cam.transform.position;
            _starMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, StarAlpha));
            if (_sun == null) { var s = GameObject.Find("Sun"); if (s != null) _sun = s.transform; }
            // The moon sits where the night's directional light comes from (the system's +z points at it).
            if (_sun != null) _moon.transform.rotation = Quaternion.LookRotation(-_sun.forward);
            _moonMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, StarAlpha * 0.75f));

            _tick -= Time.deltaTime;
            if (_tick <= 0f) { _tick = 1f; Windows(cam.transform.position, NightLevel); }
        }

        void Windows(Vector3 at, float night)
        {
            int n = 0, lights = 0;
            float[] best = { float.MaxValue, float.MaxValue };
            var near = new Vector3[2];
            LitWindows = 0;
            if (night > 0.3f)
            {
                var panes = GlassPane.All;
                for (int i = 0; i < panes.Count && n < _buffer.Length; i++)
                {
                    var p = panes[i];
                    if (p == null || p.Broken || !IsLit(p.Id)) continue;
                    var t = p.transform;
                    var to = at - t.position;
                    float sq = to.sqrMagnitude;
                    if (sq > WindowRange * WindowRange || Vector3.Dot(t.forward, to) <= 0f) continue; // seen from outside only
                    var b = t.lossyScale;
                    float size = Mathf.Max(b.x, b.y) * 1.9f;
                    _buffer[n] = new ParticleSystem.Particle
                    {
                        position = t.position + t.forward * 0.12f, startSize = size, startLifetime = 100f, remainingLifetime = 100f,
                        startColor = new Color(1f, 0.74f, 0.4f, Mathf.Clamp01(night) * 0.85f),
                    };
                    n++;
                    // The two nearest lit windows get a real light.
                    if (sq < LightRange * LightRange && sq < best[1])
                    {
                        int slot = sq < best[0] ? 0 : 1;
                        if (slot == 0) { best[1] = best[0]; near[1] = near[0]; }
                        best[slot] = sq;
                        near[slot] = t.position + t.forward * 0.9f;
                    }
                }
            }
            for (int i = 0; i < _lights.Length; i++)
            {
                bool on = best[i] < float.MaxValue;
                _lights[i].enabled = on;
                if (on) { _lights[i].transform.position = near[i]; lights++; }
            }
            _glows.SetParticles(_buffer, n);
            LitWindows = n;
            WindowLightsOn = lights;
        }
    }
}

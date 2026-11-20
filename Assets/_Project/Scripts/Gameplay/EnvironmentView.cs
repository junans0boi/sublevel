using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Time of day and weather of the generated World (ADR-119, #150). At scene start it takes the chosen (or a random)
    // hour and weather, and from then on sets the sun, the fill light, the sky, the ambient light, the fog, the rain
    // and the rain sound from WorldSky. The clock runs only when the time of day is random. Created by SkyAmbient.
    public class EnvironmentView : MonoBehaviour
    {
        const float TickSeconds = 0.25f, IndoorProbe = 9f, IndoorCheckSeconds = 0.3f;
        static readonly int ExposureId = Shader.PropertyToID("_Exposure"), TintId = Shader.PropertyToID("_Tint");

        // For tests and review captures: set before the scene starts, cleared by the test.
        public static DayTimeChoice? ForceTime;
        public static WeatherChoice? ForceWeather;
        public static int? ForceSeed;

        public static EnvironmentView Current { get; private set; }

        public float Hour { get; private set; }
        public WeatherKind Weather { get; private set; }
        public SkyLook Look { get; private set; }
        public bool Indoors { get; private set; }
        public int RainAlive => _rain != null ? _rain.particleCount : 0;
        public bool Raining => Look.RainPerSecond > 0f;
        public float RainEmission => _rain != null ? _rain.emission.rateOverTime.constant : 0f;
        public float RainVolume => _bed != null ? _bed.volume : 0f;
        public bool RoofSoundPlaying => _roof != null && _roof.isPlaying;

        bool _clockRuns, _overridden;
        SkySync _sync;
        float _tick, _indoorCheck;
        Light _sun, _fill;
        Material _sky, _baseSky;
        Color _baseTint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        float _baseExposure = 1f, _baseAmbient = 1f, _baseReflection = 1f, _reflection = 1f;
        InteriorLights _interior;
        Color _fogColour;
        ParticleSystem _rain;
        AudioSource _bed, _roof;
        static AudioClip _rainClip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            ForceTime = null;
            ForceWeather = null;
            ForceSeed = null;
        }

        // The server asks this for its sky (ADR-131): the settings, or a test's / capture's override.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void HookServerChoice() => SkySync.ChoiceSource = () =>
        {
            ReadArgs();
            return new SkySync.Choice { Time = ForceTime ?? GameSettings.DayTimeSetting, Weather = ForceWeather ?? GameSettings.WeatherSetting, Seed = ForceSeed };
        };

        // Review builds: `-daytime 1..3` and `-weather 1..3` (the setting's option numbers) fix them for a capture.
        static void ReadArgs()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-daytime");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int t)) ForceTime = (DayTimeChoice)Mathf.Clamp(t, 0, 3);
            i = System.Array.IndexOf(args, "-weather");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int w)) ForceWeather = (WeatherChoice)Mathf.Clamp(w, 0, 3);
        }

        void Start()
        {
            Current = this;
            ReadArgs();
            // In a match the server's SkySync decides; a scene without one (look-dev) keeps its own random sky.
            _sync = SkySync.Instance;
            if (_sync != null) { Hour = WorldSky.DayHour; FromSync(); }
            else
            {
                int seed = ForceSeed ?? Random.Range(int.MinValue, int.MaxValue);
                var time = ForceTime ?? GameSettings.DayTimeSetting;
                Hour = WorldSky.StartHour(time, seed);
                Weather = WorldSky.PickWeather(ForceWeather ?? GameSettings.WeatherSetting, seed);
                _clockRuns = WorldSky.ClockRuns(time);
            }

            var sun = GameObject.Find("Sun");
            var fill = GameObject.Find("Fill");
            _sun = sun != null ? sun.GetComponent<Light>() : null;
            _fill = fill != null ? fill.GetComponent<Light>() : null;
            _baseSky = RenderSettings.skybox;
            if (_baseSky != null)
            {
                _sky = new Material(_baseSky) { name = "SkyLive" };
                if (_sky.HasProperty(ExposureId)) _baseExposure = _sky.GetFloat(ExposureId);
                if (_sky.HasProperty(TintId)) _baseTint = _sky.GetColor(TintId);
                RenderSettings.skybox = _sky;
            }
            _baseAmbient = RenderSettings.ambientIntensity;
            _baseReflection = RenderSettings.reflectionIntensity;
            _fogColour = RenderSettings.fogColor;
            _interior = FindFirstObjectByType<InteriorLights>();
            BuildRain();
            Apply();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_baseSky != null && RenderSettings.skybox == _sky) RenderSettings.skybox = _baseSky;
            if (_sky != null) Destroy(_sky);
        }

        // The hour and weather the server chose, once this peer knows them (a client waits a moment for the message).
        void FromSync()
        {
            if (!_sync.Known) return;
            Hour = _sync.HourNow;
            Weather = _sync.Kind;
            _clockRuns = false; // the sync's clock is the clock
            _synced = true;
        }

        bool _synced;

        void Update()
        {
            // MatchDirector may add the sync after this view started (Start order is not fixed): adopt it when it appears.
            if (_sync == null && SkySync.Instance != null) _sync = SkySync.Instance;
            if (_sync != null && !_overridden)
            {
                bool first = !_synced;
                FromSync();
                if (first && _synced) Apply();
            }
            else if (_clockRuns) Hour = WorldSky.Wrap(Hour + Time.deltaTime * WorldSky.HoursPerRealMinute / 60f);
            _tick -= Time.deltaTime;
            if (_tick <= 0f)
            {
                _tick = TickSeconds;
                Apply();
            }
            ApplyReflection();
            TickRain();
        }

        // Test hook: jumps the clock and re-applies at once.
        public void Set(float hour, WeatherKind weather)
        {
            _overridden = true;
            Hour = WorldSky.Wrap(hour);
            Weather = weather;
            Apply();
        }

        void Apply()
        {
            var look = WorldSky.At(Hour, Weather);
            Look = look;
            if (_sun != null)
            {
                _sun.intensity = look.SunIntensity;
                _sun.color = new Color(look.SunR, look.SunG, look.SunB);
                _sun.transform.rotation = Quaternion.Euler(look.SunElevationDeg, -35f, 0f);
            }
            if (_fill != null)
            {
                _fill.intensity = look.Fill;
                _fill.color = new Color(look.FillR, look.FillG, look.FillB);
            }
            if (_sky != null)
            {
                _sky.SetFloat(ExposureId, _baseExposure * look.SkyExposure);
                _sky.SetColor(TintId, new Color(_baseTint.r * look.SkyR, _baseTint.g * look.SkyG, _baseTint.b * look.SkyB, _baseTint.a));
            }
            RenderSettings.ambientIntensity = _baseAmbient * look.Ambient;
            _reflection = _baseReflection * look.Ambient * look.Ambient; // squared: the sky reflected in smooth road must go dark with the night
            ApplyReflection();
            // The radiation fog of a closing region (ZoneFogView) takes the fog over while it is on.
            if (!ZoneFog.Current.Active) ApplyFog(look);
        }

        // Indoors the sky cubemap would still shine on smooth floors (ADR-543): it fades with the interior grade, every frame.
        void ApplyReflection() => RenderSettings.reflectionIntensity = _reflection * WorldSky.IndoorReflection(_interior != null ? _interior.GradeWeight : 0f);

        void ApplyFog(in SkyLook look)
        {
            RenderSettings.fogColor = new Color(_fogColour.r * look.FogR, _fogColour.g * look.FogG, _fogColour.b * look.FogB, 1f);
            if (look.FogEnd <= 0f) return; // clear: the scene's own fog distances (GraphicsOptions scales them)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;
        }

        // ---- Rain ----

        void BuildRain()
        {
            var material = Resources.Load<Material>("Weather/RainStreak");
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // the box emits along its +Z: straight down
            _rain = go.AddComponent<ParticleSystem>();
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rain.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.5f;
            main.startSpeed = 28f;
            main.startSize = 0.025f;
            main.maxParticles = 2500;
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // a looping system with no particles has no bounds to be seen by
            var shape = _rain.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 26f, 0.1f);
            var emission = _rain.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0.04f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (material != null) renderer.sharedMaterial = material;

            _bed = NewSource("RainBed");
            _bed.spatialBlend = 0f;
            _roof = NewSource("RainRoof");
            _roof.pitch = 1.25f;
            SpatialAudio.Apply(_roof, SpatialAudio.Category.Impact); // the patter comes from the roof overhead
        }

        AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = RainClip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            return source;
        }

        void TickRain()
        {
            var camera = Camera.main;
            if (camera == null || _rain == null) return;
            var at = camera.transform.position;
            bool rainy = Raining;
            _indoorCheck -= Time.deltaTime;
            if (_indoorCheck <= 0f)
            {
                _indoorCheck = IndoorCheckSeconds;
                // Under a roof only when a whole patch above is covered (a tree's branch is not a roof).
                RaycastHit roof = default;
                bool covered = rainy;
                for (int i = 0; i < 5 && covered; i++)
                {
                    var offset = i == 0 ? Vector3.zero : new Vector3(i < 3 ? (i == 1 ? 1.5f : -1.5f) : 0f, 0f, i >= 3 ? (i == 3 ? 1.5f : -1.5f) : 0f);
                    covered = Physics.Raycast(at + offset, Vector3.up, out var h, IndoorProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (i == 0) roof = h;
                }
                Indoors = covered;
                if (Indoors) _roof.transform.position = roof.point;
            }
            float quality = GameSettings.HighEffects ? 1f : 0.5f;
            var emission = _rain.emission;
            emission.rateOverTime = rainy && !Indoors ? Look.RainPerSecond * quality : 0f;
            _rain.transform.position = at + Vector3.up * 10f;
            if (rainy && !_rain.isPlaying) _rain.Play();
            var main = _rain.main;
            float light = Mathf.Clamp01(Look.Ambient * 1.4f);
            main.startColor = new Color(0.78f * light + 0.1f, 0.82f * light + 0.1f, 0.9f * light + 0.12f, 0.42f);

            // The bed is the rain around you; indoors it is muffled and the roof patter takes over.
            float volume = rainy ? 0.5f * GameSettings.AmbientVolume : 0f;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 4f);
            SetLoop(_bed, Mathf.Lerp(_bed.volume, Indoors ? volume * 0.25f : volume, k));
            SetLoop(_roof, Mathf.Lerp(_roof.volume, Indoors ? volume * 0.8f : 0f, k));
        }

        static void SetLoop(AudioSource source, float volume)
        {
            source.volume = volume;
            if (volume > 0.002f && !source.isPlaying) source.Play();
            else if (volume <= 0.002f && source.isPlaying) source.Stop();
        }

        // Rain made in code (no sound asset, like GlassFx's crash): shaped noise, a soft hiss over a low rumble, made
        // to loop by fading the tail into the head.
        static AudioClip RainClip
        {
            get
            {
                if (_rainClip != null) return _rainClip;
                const int rate = 44100, seconds = 4, fade = 4410;
                var data = new float[rate * seconds];
                var rng = new System.Random(2718);
                float low = 0f, rumble = 0f, peak = 0f;
                for (int i = 0; i < data.Length; i++)
                {
                    float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                    low += (n - low) * 0.25f;           // hiss: noise without its lows
                    rumble += (n - rumble) * 0.02f;     // rumble: the lows only
                    // Now and then a drop: a short bright tick.
                    float drop = rng.NextDouble() < 0.0015 ? (float)(rng.NextDouble() * 0.6) : 0f;
                    data[i] = (n - low) * 0.35f + rumble * 2.2f + drop * (n - low);
                    peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                }
                for (int i = 0; i < data.Length; i++) data[i] *= 0.7f / Mathf.Max(peak, 1e-3f);
                for (int k = 0; k < fade; k++)
                {
                    float w = k / (float)fade;
                    int tail = data.Length - fade + k;
                    data[tail] = data[tail] * (1f - w) + data[k] * w;
                }
                _rainClip = AudioClip.Create("RainLoop", data.Length, 1, rate, false);
                _rainClip.SetData(data, 0);
                return _rainClip;
            }
        }
    }
}

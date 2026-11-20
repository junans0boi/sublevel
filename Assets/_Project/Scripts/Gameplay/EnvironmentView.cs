using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Time of day and weather of the generated World (ADR-119, #150). At scene start it takes the chosen (or a random)
    // hour and weather, and from then on sets the sun, the fill light, the sky, the ambient light, and the fog from
    // WorldSky. The clock runs only when the time of day is random. Created by SkyAmbient.
    public class EnvironmentView : MonoBehaviour
    {
        const float TickSeconds = 0.25f;
        static readonly int ExposureId = Shader.PropertyToID("_Exposure"), TintId = Shader.PropertyToID("_Tint");

        // For tests and review captures: set before the scene starts, cleared by the test.
        public static DayTimeChoice? ForceTime;
        public static WeatherChoice? ForceWeather;
        public static int? ForceSeed;

        public static EnvironmentView Current { get; private set; }

        public float Hour { get; private set; }
        public WeatherKind Weather { get; private set; }
        public SkyLook Look { get; private set; }

        bool _clockRuns, _overridden;
        SkySync _sync;
        float _tick;
        Light _sun, _fill;
        Material _sky, _baseSky;
        Color _baseTint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        float _baseExposure = 1f, _baseAmbient = 1f, _baseReflection = 1f;
        Color _fogColour;

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

        // Review builds: `-daytime 1..3` and `-weather 1|3` (WeatherChoice: 1 clear, 3 fog) fix them for a capture.
        static void ReadArgs()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-daytime");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int t)) ForceTime = (DayTimeChoice)Mathf.Clamp(t, 0, 3);
            i = System.Array.IndexOf(args, "-weather");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int w)) ForceWeather = (WeatherChoice)w;
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
            RenderSettings.reflectionIntensity = _baseReflection * look.Ambient * look.Ambient; // squared: the sky reflected in smooth road must go dark with the night
            // The radiation fog of a closing region (ZoneFogView) takes the fog over while it is on.
            if (!ZoneFog.Current.Active) ApplyFog(look);
        }

        void ApplyFog(in SkyLook look)
        {
            RenderSettings.fogColor = new Color(_fogColour.r * look.FogR, _fogColour.g * look.FogG, _fogColour.b * look.FogB, 1f);
            if (look.FogEnd <= 0f) return; // clear: the scene's own fog distances (GraphicsOptions scales them)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;
        }
    }
}

using System;

namespace Game.Core
{
    // The setting's choices (index = the option shown; 0 = random per match).
    public enum DayTimeChoice { Random = 0, Day = 1, Dusk = 2, Night = 3 }
    // Rain was removed (ADR-497): its numbers (kind 1, choice 2) stay unused so the byte SkySync sends keeps its meaning.
    public enum WeatherKind { Clear = 0, Fog = 2 }
    public enum WeatherChoice { Random = 0, Clear = 1, Fog = 3 }

    // What the sky, sun and fog look like at one moment (ADR-119, #150). Colours are plain floats (Core has no engine).
    public struct SkyLook
    {
        public float SunElevationDeg, SunIntensity;
        public float SunR, SunG, SunB;
        public float Ambient;                 // multiplies the scene's ambient and reflection light
        public float FillR, FillG, FillB, Fill; // the cool second light (moonlight at night)
        public float SkyExposure, SkyR, SkyG, SkyB; // skybox exposure and tint (1 = as built)
        public float FogR, FogG, FogB;        // multiplies the scene's fog colour
        public float FogStart, FogEnd;        // metres; 0 = keep the scene's own
    }

    // Time of day and weather: pure numbers, EnvironmentView applies them (ADR-119).
    public static class WorldSky
    {
        public const float HoursPerRealMinute = 0.3f; // a 20-minute match spans 6 hours
        public const float DayHour = 12f, DuskHour = 18.3f, NightHour = 23.5f;

        // Hour keys (0..24 wrapping) and the look at each: elevation, sun, sun colour, ambient, fill colour/intensity, sky, fog.
        struct Key { public float Hour, Elev, Sun, Sr, Sg, Sb, Amb, Fr, Fg, Fb, Fill, Exp, Tr, Tg, Tb, Gr, Gg, Gb; }

        static readonly Key[] Keys =
        {
            new Key { Hour = 0f,    Elev = 40f, Sun = 0.22f, Sr = 0.55f, Sg = 0.65f, Sb = 1f,    Amb = 0.38f, Fr = 0.5f,  Fg = 0.6f,  Fb = 1f,   Fill = 0.5f, Exp = 0.17f, Tr = 0.55f, Tg = 0.65f, Tb = 1f,    Gr = 0.07f, Gg = 0.09f, Gb = 0.18f },
            new Key { Hour = 5f,    Elev = 40f, Sun = 0.22f, Sr = 0.55f, Sg = 0.65f, Sb = 1f,    Amb = 0.38f, Fr = 0.5f,  Fg = 0.6f,  Fb = 1f,   Fill = 0.5f, Exp = 0.17f, Tr = 0.55f, Tg = 0.65f, Tb = 1f,    Gr = 0.07f, Gg = 0.09f, Gb = 0.18f },
            new Key { Hour = 6.5f,  Elev = 5f,  Sun = 0.80f, Sr = 1f,    Sg = 0.62f, Sb = 0.36f, Amb = 0.55f, Fr = 0.7f,  Fg = 0.75f, Fb = 1f,   Fill = 0.25f, Exp = 0.65f, Tr = 1f,    Tg = 0.75f, Tb = 0.6f,  Gr = 0.85f, Gg = 0.62f, Gb = 0.5f },
            new Key { Hour = 9f,    Elev = 45f, Sun = 1.50f, Sr = 1f,    Sg = 0.95f, Sb = 0.88f, Amb = 1f,    Fr = 0.75f, Fg = 0.85f, Fb = 1f,   Fill = 0.30f, Exp = 1f,    Tr = 1f,    Tg = 1f,    Tb = 1f,    Gr = 1f,    Gg = 1f,    Gb = 1f },
            new Key { Hour = 16f,   Elev = 40f, Sun = 1.50f, Sr = 1f,    Sg = 0.95f, Sb = 0.88f, Amb = 1f,    Fr = 0.75f, Fg = 0.85f, Fb = 1f,   Fill = 0.30f, Exp = 1f,    Tr = 1f,    Tg = 1f,    Tb = 1f,    Gr = 1f,    Gg = 1f,    Gb = 1f },
            new Key { Hour = 18.3f, Elev = 4f,  Sun = 0.95f, Sr = 1f,    Sg = 0.50f, Sb = 0.24f, Amb = 0.55f, Fr = 0.7f,  Fg = 0.7f,  Fb = 1f,   Fill = 0.25f, Exp = 0.60f, Tr = 1f,    Tg = 0.65f, Tb = 0.5f,  Gr = 0.9f,  Gg = 0.55f, Gb = 0.42f },
            new Key { Hour = 20f,   Elev = 40f, Sun = 0.22f, Sr = 0.55f, Sg = 0.65f, Sb = 1f,    Amb = 0.38f, Fr = 0.5f,  Fg = 0.6f,  Fb = 1f,   Fill = 0.5f, Exp = 0.17f, Tr = 0.55f, Tg = 0.65f, Tb = 1f,    Gr = 0.07f, Gg = 0.09f, Gb = 0.18f },
            new Key { Hour = 24f,   Elev = 40f, Sun = 0.22f, Sr = 0.55f, Sg = 0.65f, Sb = 1f,    Amb = 0.38f, Fr = 0.5f,  Fg = 0.6f,  Fb = 1f,   Fill = 0.5f, Exp = 0.17f, Tr = 0.55f, Tg = 0.65f, Tb = 1f,    Gr = 0.07f, Gg = 0.09f, Gb = 0.18f },
        };

        static float L(float a, float b, float t) => a + (b - a) * t;

        public static float Wrap(float hour)
        {
            hour %= 24f;
            return hour < 0f ? hour + 24f : hour;
        }

        // The hour a match starts at: the chosen time of day, or a random one (seeded).
        public static float StartHour(DayTimeChoice choice, int seed)
        {
            switch (choice)
            {
                case DayTimeChoice.Day: return DayHour;
                case DayTimeChoice.Dusk: return DuskHour;
                case DayTimeChoice.Night: return NightHour;
                default: return Wrap(7f + 17f * (float)new Random(seed).NextDouble()); // 7:00 .. 24:00, mostly daylight
            }
        }

        // The hour `elapsedSeconds` of match time after the start (every peer derives its own clock from the one start hour).
        public static float HourAt(float startHour, float elapsedSeconds, bool clockRuns) =>
            clockRuns ? Wrap(startHour + Math.Max(0f, elapsedSeconds) * HoursPerRealMinute / 60f) : Wrap(startHour);

        public static WeatherKind PickWeather(WeatherChoice choice, int seed)
        {
            if (choice == WeatherChoice.Fog) return WeatherKind.Fog;
            if (choice != WeatherChoice.Random) return WeatherKind.Clear; // Clear, and the old Rain byte or anything unknown
            return new Random(seed * 31 + 7).NextDouble() < 0.70 ? WeatherKind.Clear : WeatherKind.Fog; // 70/30 (ADR-497)
        }

        // The clock runs only when the time is random; a chosen time of day stays put.
        public static bool ClockRuns(DayTimeChoice choice) => choice == DayTimeChoice.Random;

        public static SkyLook At(float hour, WeatherKind weather)
        {
            hour = Wrap(hour);
            int i = 0;
            while (i < Keys.Length - 2 && hour >= Keys[i + 1].Hour) i++;
            var a = Keys[i];
            var b = Keys[i + 1];
            float t = (hour - a.Hour) / (b.Hour - a.Hour);
            var look = new SkyLook
            {
                SunElevationDeg = L(a.Elev, b.Elev, t), SunIntensity = L(a.Sun, b.Sun, t),
                SunR = L(a.Sr, b.Sr, t), SunG = L(a.Sg, b.Sg, t), SunB = L(a.Sb, b.Sb, t),
                Ambient = L(a.Amb, b.Amb, t),
                FillR = L(a.Fr, b.Fr, t), FillG = L(a.Fg, b.Fg, t), FillB = L(a.Fb, b.Fb, t), Fill = L(a.Fill, b.Fill, t),
                SkyExposure = L(a.Exp, b.Exp, t), SkyR = L(a.Tr, b.Tr, t), SkyG = L(a.Tg, b.Tg, t), SkyB = L(a.Tb, b.Tb, t),
                FogR = L(a.Gr, b.Gr, t), FogG = L(a.Gg, b.Gg, t), FogB = L(a.Gb, b.Gb, t),
            };
            // How bright the hour is, 0 night .. 1 day: weather greys the fog toward this much light.
            float light = Math.Min(1f, look.Ambient);
            switch (weather)
            {
                case WeatherKind.Fog:
                    look.SunIntensity *= 0.55f; look.Ambient *= 0.95f; look.SkyExposure *= 0.75f;
                    look.FogStart = 4f; look.FogEnd = 110f;
                    Grey(ref look, 0.75f, light * 0.80f);
                    break;
            }
            return look;
        }

        // The fog multiplier moves toward a grey of `level`, share `k`.
        static void Grey(ref SkyLook l, float k, float level)
        {
            l.FogR = L(l.FogR, level, k);
            l.FogG = L(l.FogG, level * 1.03f, k);
            l.FogB = L(l.FogB, level * 1.07f, k);
        }
    }
}

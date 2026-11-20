using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-119 (#150): time of day and weather as pure numbers.
    public class WorldSkyTests
    {
        [Test]
        public void ChosenTimes_StartAtTheirHour_RandomIsSeededAndInRange()
        {
            Assert.AreEqual(WorldSky.DayHour, WorldSky.StartHour(DayTimeChoice.Day, 1));
            Assert.AreEqual(WorldSky.DuskHour, WorldSky.StartHour(DayTimeChoice.Dusk, 1));
            Assert.AreEqual(WorldSky.NightHour, WorldSky.StartHour(DayTimeChoice.Night, 1));
            Assert.AreEqual(WorldSky.StartHour(DayTimeChoice.Random, 5), WorldSky.StartHour(DayTimeChoice.Random, 5), "same seed, same hour");
            bool day = false, night = false;
            for (int seed = 0; seed < 200; seed++)
            {
                float h = WorldSky.StartHour(DayTimeChoice.Random, seed);
                Assert.That(h, Is.InRange(0f, 24f));
                day |= h > 9f && h < 16f;
                night |= h > 21f || h < 4f;
            }
            Assert.IsTrue(day && night, "random reaches both daylight and night");
        }

        [Test]
        public void TheClock_RunsOnlyWhenTheTimeIsRandom()
        {
            Assert.IsTrue(WorldSky.ClockRuns(DayTimeChoice.Random));
            foreach (var c in new[] { DayTimeChoice.Day, DayTimeChoice.Dusk, DayTimeChoice.Night }) Assert.IsFalse(WorldSky.ClockRuns(c));
        }

        [Test]
        public void Day_IsTheBuiltLook_Dusk_IsLowAndWarm_Night_IsDimAndBlue()
        {
            var day = WorldSky.At(WorldSky.DayHour, WeatherKind.Clear);
            Assert.AreEqual(WorldSky.ClearDaySun, day.SunIntensity, 1e-4f, "the clear-day sun (ADR-543)");
            Assert.AreEqual(WorldSky.ClearDayAmbient, day.Ambient, 1e-4f);
            Assert.AreEqual(0f, day.FogEnd, "clear weather keeps the scene's fog distances");
            var dusk = WorldSky.At(WorldSky.DuskHour, WeatherKind.Clear);
            Assert.Less(dusk.SunElevationDeg, 10f);
            Assert.Greater(dusk.SunR, dusk.SunB, "warm");
            var night = WorldSky.At(WorldSky.NightHour, WeatherKind.Clear);
            Assert.Less(night.SunIntensity, 0.3f);
            Assert.Less(night.Ambient, 0.4f);
            Assert.Greater(night.SunB, night.SunR, "moonlight is blue");
            Assert.Greater(night.Ambient, 0.1f, "dark but playable");
        }

        [Test]
        public void TheDay_ChangesSmoothly_AroundTheClock()
        {
            var prev = WorldSky.At(0f, WeatherKind.Clear);
            for (float h = 0.25f; h <= 24f; h += 0.25f)
            {
                var now = WorldSky.At(h, WeatherKind.Clear);
                Assert.Less(System.Math.Abs(now.SunIntensity - prev.SunIntensity), 0.5f, "sun at " + h);
                Assert.Less(System.Math.Abs(now.Ambient - prev.Ambient), 0.25f, "ambient at " + h);
                prev = now;
            }
            Assert.AreEqual(WorldSky.At(0f, WeatherKind.Clear).SunIntensity, WorldSky.At(24f, WeatherKind.Clear).SunIntensity, 1e-4f, "wraps");
            Assert.AreEqual(WorldSky.At(1f, WeatherKind.Clear).SunIntensity, WorldSky.At(25f, WeatherKind.Clear).SunIntensity, 1e-4f);
        }

        [Test]
        public void RainAndFog_DimTheSun_ShortenTheView_AndOnlyRainRains()
        {
            var clear = WorldSky.At(WorldSky.DayHour, WeatherKind.Clear);
            var rain = WorldSky.At(WorldSky.DayHour, WeatherKind.Rain);
            var fog = WorldSky.At(WorldSky.DayHour, WeatherKind.Fog);
            Assert.Less(rain.SunIntensity, clear.SunIntensity);
            Assert.Less(fog.SunIntensity, clear.SunIntensity);
            Assert.Greater(rain.RainPerSecond, 0f);
            Assert.AreEqual(0f, fog.RainPerSecond);
            Assert.AreEqual(0f, clear.RainPerSecond);
            Assert.Less(fog.FogEnd, rain.FogEnd, "fog is the thicker one");
            Assert.Greater(rain.FogEnd, 0f);
        }

        [Test]
        public void WeatherChoice_FixedOrSeededRandom()
        {
            Assert.AreEqual(WeatherKind.Rain, WorldSky.PickWeather(WeatherChoice.Rain, 3));
            Assert.AreEqual(WeatherKind.Clear, WorldSky.PickWeather(WeatherChoice.Clear, 3));
            Assert.AreEqual(WeatherKind.Fog, WorldSky.PickWeather(WeatherChoice.Fog, 3));
            int clear = 0, rain = 0, fog = 0;
            for (int seed = 0; seed < 400; seed++)
                switch (WorldSky.PickWeather(WeatherChoice.Random, seed)) { case WeatherKind.Clear: clear++; break; case WeatherKind.Rain: rain++; break; default: fog++; break; }
            Assert.Greater(clear, rain);
            Assert.Greater(rain, 30);
            Assert.Greater(fog, 30);
        }

        // ADR-125: every peer derives the hour from the one start hour and the match time it was told.
        [Test]
        public void HourAt_RunsFromTheStartHour_OnlyWhenTheClockRuns_AndWraps()
        {
            float start = WorldSky.StartHour(DayTimeChoice.Random, 99);
            Assert.AreEqual(start, WorldSky.HourAt(start, 0f, true), 1e-4f);
            Assert.AreEqual(WorldSky.Wrap(start + 0.3f), WorldSky.HourAt(start, 60f, true), 1e-3f, "0.3 h per real minute");
            Assert.AreEqual(WorldSky.DuskHour, WorldSky.HourAt(WorldSky.DuskHour, 600f, false), 1e-4f, "a chosen time stands still");
            Assert.AreEqual(0.5f, WorldSky.HourAt(23.5f, 200f, true), 1e-3f, "past midnight (200 s = 1 h)");
            Assert.AreEqual(start, WorldSky.HourAt(start, -5f, true), 1e-4f, "never before the start");
            // Two peers with the same seed and the same match time see the same hour and weather.
            Assert.AreEqual(WorldSky.PickWeather(WeatherChoice.Random, 99), WorldSky.PickWeather(WeatherChoice.Random, 99));
        }

        // ADR-210 (ADR-193 intent): out of the box the match is a clear day, not a random hour that is night a quarter of the time.
        [Test]
        public void Defaults_AreClearDay_AndThatLookIsBright()
        {
            foreach (var def in Game.Gameplay.SettingsCatalog.All)
            {
                if (def.Key == "settings.dayTime") Assert.AreEqual((float)DayTimeChoice.Day, def.Default);
                if (def.Key == "settings.weather") Assert.AreEqual((float)WeatherChoice.Clear, def.Default);
            }
            var look = WorldSky.At(WorldSky.StartHour(DayTimeChoice.Day, 1), WeatherKind.Clear);
            Assert.GreaterOrEqual(look.SunElevationDeg, 40f);
            Assert.GreaterOrEqual(look.SunIntensity, 1.5f);
            Assert.GreaterOrEqual(look.Ambient, 1f);
            Assert.AreEqual(1f, look.SkyExposure, 1e-3f);
            Assert.AreEqual(0f, look.RainPerSecond);
            Assert.AreEqual(0f, look.FogEnd, "clear keeps the scene's own fog (the ADR-193 haze colour)");
        }

        // ADR-543 (#318): a clean, readable clear day, and no sky glare on the floors indoors.
        [Test]
        public void ClearDay_IsNearWhite_AndIndoorsTheSkyReflectionFades()
        {
            var day = WorldSky.At(WorldSky.DayHour, WeatherKind.Clear);
            Assert.That(day.SunB / day.SunR, Is.GreaterThan(0.9f), "noon sun near white, not the dusk warmth");
            Assert.That(day.Ambient / day.SunIntensity, Is.InRange(0.6f, 0.8f), "sun still leads, shadowed faces still read");
            Assert.AreEqual(1f, WorldSky.IndoorReflection(0f), 1e-6f, "outdoors untouched");
            Assert.AreEqual(WorldSky.IndoorReflectionShare, WorldSky.IndoorReflection(1f), 1e-6f);
            Assert.AreEqual(WorldSky.IndoorReflectionShare, WorldSky.IndoorReflection(3f), 1e-6f, "clamped");
            Assert.AreEqual(1f, WorldSky.IndoorReflection(-1f), 1e-6f, "clamped");
            Assert.Less(WorldSky.IndoorReflection(0.5f), 1f);
            Assert.Greater(WorldSky.IndoorReflection(0.5f), WorldSky.IndoorReflectionShare);
            var spec = new WorldBuildSpec();
            Assert.GreaterOrEqual(spec.FogStart, 120f, "no haze over combat range on a clear day");
            Assert.Less(spec.FogEnd, spec.FarClip, "fog still ends before the far clip");
        }
    }
}

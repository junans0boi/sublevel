using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Footprints, wet ground, lightning timing and wildlife counts (#205 #206 #207 #208, ADR-163).
    public class GroundFxRulesTests
    {
        [Test]
        public void Footprints_SoftGroundKeepsThem_HardAndMetalDoNot_WetConcreteShortly()
        {
            Assert.Greater(GroundFxRules.Footprint(FootSurface.Dirt, 0f, false).Alpha, 0.3f);
            Assert.Greater(GroundFxRules.Footprint(FootSurface.Grass, 0f, false).Alpha, 0f);
            Assert.AreEqual(0f, GroundFxRules.Footprint(FootSurface.Metal, 1f, false).Alpha);
            Assert.AreEqual(0f, GroundFxRules.Footprint(FootSurface.Wood, 1f, false).Alpha);
            Assert.AreEqual(0f, GroundFxRules.Footprint(FootSurface.Concrete, 0f, false).Alpha, "dry concrete leaves nothing");
            var wet = GroundFxRules.Footprint(FootSurface.Concrete, 1f, false);
            Assert.Greater(wet.Alpha, 0f);
            Assert.Less(wet.LifeSeconds, GroundFxRules.Footprint(FootSurface.Dirt, 1f, false).LifeSeconds, "a wet print on stone dries sooner than a mud print");
            Assert.Greater(GroundFxRules.Footprint(FootSurface.Dirt, 1f, false).Alpha, GroundFxRules.Footprint(FootSurface.Dirt, 0f, false).Alpha, "wet mud shows more");
        }

        [Test]
        public void Fade_HoldsThenFallsToZero()
        {
            Assert.AreEqual(1f, GroundFxRules.Fade(0f, 30f), 1e-4f);
            Assert.AreEqual(1f, GroundFxRules.Fade(19f, 30f), 1e-4f);
            Assert.Less(GroundFxRules.Fade(28f, 30f), 0.3f);
            Assert.AreEqual(0f, GroundFxRules.Fade(30f, 30f));
        }

        [Test]
        public void Wetness_RisesInRain_DriesSlowlyAfter_StaysInRange()
        {
            float w = 0f;
            for (int i = 0; i < 100; i++) w = GroundFxRules.Wetness(w, true, 1f);
            Assert.AreEqual(1f, w, 1e-4f);
            float half = GroundFxRules.Wetness(0f, true, GroundFxRules.WetRiseSeconds * 0.5f);
            Assert.AreEqual(0.5f, half, 1e-3f);
            Assert.Greater(GroundFxRules.Wetness(1f, false, 60f), 0.7f, "it dries slower than it soaks");
            Assert.AreEqual(0f, GroundFxRules.Wetness(0f, false, 10f));
        }

        [Test]
        public void Splashes_ScaleWithRainAndQuality_NoneUnderARoof()
        {
            Assert.AreEqual(0f, GroundFxRules.SplashesPerSecond(900f, 1f, true));
            Assert.AreEqual(GroundFxRules.SplashesPerSecond(900f, 1f, false) * 0.5f, GroundFxRules.SplashesPerSecond(900f, 0.5f, false), 1e-3f);
            Assert.Less(GroundFxRules.SplashesPerSecond(900f, 1f, false) / 60f, GroundFxRules.MaxSplashesPerFrame, "the per-frame cap is enough at 60 fps");
        }

        [Test]
        public void Wading_StillWaterHasNoRings_FastHasMore()
        {
            Assert.AreEqual(0f, GroundFxRules.WadeRingsPerSecond(0f));
            Assert.Greater(GroundFxRules.WadeRingsPerSecond(4f), GroundFxRules.WadeRingsPerSecond(1.5f));
        }

        [Test]
        public void Thunder_ArrivesAfterDistanceOverSpeedOfSound_QuieterFarAway()
        {
            Assert.AreEqual(2.9155f, LightningRules.ThunderDelay(1000f), 1e-3f);
            Assert.Greater(LightningRules.ThunderVolume(300f), LightningRules.ThunderVolume(2000f));
            Assert.Greater(LightningRules.ThunderVolume(2150f), 0f, "even the farthest strike is faintly heard");
            for (float u = 0f; u <= 1f; u += 0.1f)
            {
                Assert.That(LightningRules.NextGap(u), Is.InRange(LightningRules.MinGap, LightningRules.MaxGap));
                Assert.That(LightningRules.Distance(u), Is.InRange(250f, 2150f));
            }
        }

        [Test]
        public void Flash_IsBrightAtOnce_DipsFlickersAgain_ThenEnds()
        {
            Assert.AreEqual(0f, LightningRules.Flash(-0.1f));
            Assert.AreEqual(1f, LightningRules.Flash(0.01f));
            Assert.Less(LightningRules.Flash(0.1f), 0.4f, "the dip");
            Assert.Greater(LightningRules.Flash(0.2f), LightningRules.Flash(0.1f), "the second flicker");
            Assert.AreEqual(0f, LightningRules.Flash(LightningRules.FlashSeconds + 0.01f));
        }

        [Test]
        public void Wildlife_ButterfliesByDay_FirefliesByNight_NoneInRainOrIndoors()
        {
            var day = WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Wetland, false, 0f, 0f);
            var night = WildlifeRules.Mix(23.5f, WeatherKind.Clear, RegionKind.Wetland, false, 0f, 0f);
            Assert.Greater(day.Butterflies, 5f);
            Assert.AreEqual(0f, day.Fireflies, 1e-4f);
            Assert.Greater(night.Fireflies, 15f);
            Assert.AreEqual(0f, night.Butterflies, 1e-4f);
            var rain = WildlifeRules.Mix(12f, WeatherKind.Rain, RegionKind.Wetland, false, 0f, 0.65f);
            Assert.AreEqual(0f, rain.Butterflies);
            Assert.AreEqual(0f, rain.BirdsFlying);
            Assert.AreEqual(0f, WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Wetland, true, 0f, 0f).Butterflies);
            Assert.Greater(WildlifeRules.Mix(12f, WeatherKind.Fog, RegionKind.Mountain, false, 0f, 0.25f).LeavesPerSecond, WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Mountain, false, 0f, 0f).LeavesPerSecond, "wind brings leaves");
            Assert.Greater(WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Mountain, false, 1f, 0f).FishSplashPerMinute, 0f);
            Assert.AreEqual(0f, WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Mountain, false, 0f, 0f).FishSplashPerMinute, "no coast, no fish");
        }

        [Test]
        public void Night_IsDarkButNeverBlack()
        {
            foreach (var weather in new[] { WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Fog })
            {
                var l = WorldSky.At(WorldSky.NightHour, weather);
                Assert.Greater(l.Ambient, 0.2f, weather + " ambient floor");
                Assert.Greater(l.Fill, 0.3f, weather + " moonlight");
                Assert.Greater(l.SkyExposure, 0.05f, weather + " sky");
            }
        }

        [Test]
        public void LitWindows_AreAboutAThird_TheSameEveryTime()
        {
            int lit = 0;
            for (int i = 0; i < 1000; i++) if (NightFx.IsLit(i)) lit++;
            Assert.That(lit, Is.InRange(250, 450));
            Assert.AreEqual(NightFx.IsLit(17), NightFx.IsLit(17));
        }
    }
}

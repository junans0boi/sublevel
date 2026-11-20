using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // Footprints and wildlife counts (#205 #206 #208, ADR-163).
    public class GroundFxRulesTests
    {
        [Test]
        public void Footprints_SoftGroundKeepsThem_HardAndMetalDoNot()
        {
            Assert.Greater(GroundFxRules.Footprint(FootSurface.Dirt, false).Alpha, 0.3f);
            Assert.Greater(GroundFxRules.Footprint(FootSurface.Grass, false).Alpha, 0f);
            Assert.AreEqual(0f, GroundFxRules.Footprint(FootSurface.Metal, false).Alpha);
            Assert.AreEqual(0f, GroundFxRules.Footprint(FootSurface.Wood, false).Alpha);
            Assert.AreEqual(0f, GroundFxRules.Footprint(FootSurface.Concrete, false).Alpha, "concrete leaves nothing");
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
        public void Wading_StillWaterHasNoRings_FastHasMore()
        {
            Assert.AreEqual(0f, GroundFxRules.WadeRingsPerSecond(0f));
            Assert.Greater(GroundFxRules.WadeRingsPerSecond(4f), GroundFxRules.WadeRingsPerSecond(1.5f));
        }

        [Test]
        public void Wildlife_ButterfliesByDay_FirefliesByNight_NoneIndoors_FewerInFog()
        {
            var day = WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Wetland, false, 0f, 0f);
            var night = WildlifeRules.Mix(23.5f, WeatherKind.Clear, RegionKind.Wetland, false, 0f, 0f);
            Assert.Greater(day.Butterflies, 5f);
            Assert.AreEqual(0f, day.Fireflies, 1e-4f);
            Assert.Greater(night.Fireflies, 15f);
            Assert.AreEqual(0f, night.Butterflies, 1e-4f);
            Assert.Less(WildlifeRules.Mix(12f, WeatherKind.Fog, RegionKind.Wetland, false, 0f, 0.25f).Butterflies, day.Butterflies);
            Assert.AreEqual(0f, WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Wetland, true, 0f, 0f).Butterflies);
            Assert.Greater(WildlifeRules.Mix(12f, WeatherKind.Fog, RegionKind.Mountain, false, 0f, 0.25f).LeavesPerSecond, WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Mountain, false, 0f, 0f).LeavesPerSecond, "wind brings leaves");
            Assert.Greater(WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Mountain, false, 1f, 0f).FishSplashPerMinute, 0f);
            Assert.AreEqual(0f, WildlifeRules.Mix(12f, WeatherKind.Clear, RegionKind.Mountain, false, 0f, 0f).FishSplashPerMinute, "no coast, no fish");
        }

        [Test]
        public void Night_IsDarkButNeverBlack()
        {
            foreach (var weather in new[] { WeatherKind.Clear, WeatherKind.Fog })
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

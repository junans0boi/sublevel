using Game.Core;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests
{
    // #202 / #203 (ADR-155): lamp switch, brake / reverse / indicator rules, tyre dust and screech amounts, engine gears, synthesized sounds.
    public class VehicleFxRulesTests
    {
        [Test]
        public void AutoLights_DuskNightAndWeather()
        {
            Assert.IsFalse(VehicleFxRules.AutoLights(1f, false), "full day");
            Assert.IsTrue(VehicleFxRules.AutoLights(0.4f, false), "dusk / night");
            Assert.IsTrue(VehicleFxRules.AutoLights(0.8f, true), "fog by day");
            Assert.IsFalse(VehicleFxRules.AutoLights(1f, true), "bright even in fog");
        }

        [Test]
        public void Lamps_NeedTheEngine_AndFollowTheSwitch()
        {
            Assert.IsFalse(VehicleFxRules.LightsLit(VehicleFxRules.LightsOn, true, false), "engine off: dark");
            Assert.IsTrue(VehicleFxRules.LightsLit(VehicleFxRules.LightsAuto, true, true));
            Assert.IsFalse(VehicleFxRules.LightsLit(VehicleFxRules.LightsAuto, false, true));
            Assert.IsFalse(VehicleFxRules.LightsLit(VehicleFxRules.LightsOff, true, true), "forced off at night");
            Assert.IsTrue(VehicleFxRules.LightsLit(VehicleFxRules.LightsOn, false, true), "forced on by day");
        }

        [Test]
        public void Toggle_FlipsWhatShows_AndReturnsToAuto()
        {
            // Night, auto (lit): L -> off; L again -> back to auto.
            byte m = VehicleFxRules.ToggleMode(VehicleFxRules.LightsAuto, true, true);
            Assert.AreEqual(VehicleFxRules.LightsOff, m);
            Assert.AreEqual(VehicleFxRules.LightsAuto, VehicleFxRules.ToggleMode(m, true, true));
            // Day, auto (dark): L -> on; L again -> auto.
            m = VehicleFxRules.ToggleMode(VehicleFxRules.LightsAuto, false, true);
            Assert.AreEqual(VehicleFxRules.LightsOn, m);
            Assert.AreEqual(VehicleFxRules.LightsAuto, VehicleFxRules.ToggleMode(m, false, true));
        }

        [Test]
        public void Brake_Reverse_Indicator()
        {
            Assert.IsTrue(VehicleFxRules.Braking(15f, -5f, false), "slowing hard going forward");
            Assert.IsFalse(VehicleFxRules.Braking(15f, -1f, false), "coasting");
            Assert.IsFalse(VehicleFxRules.Braking(15f, 3f, false), "accelerating");
            Assert.IsTrue(VehicleFxRules.Braking(-8f, 4f, false), "braking while reversing");
            Assert.IsTrue(VehicleFxRules.Braking(10f, 0f, true), "handbrake");
            Assert.IsFalse(VehicleFxRules.Braking(0.2f, -9f, false), "stopped");
            Assert.IsTrue(VehicleFxRules.Reversing(-2f));
            Assert.IsFalse(VehicleFxRules.Reversing(3f));
            Assert.AreEqual(1, VehicleFxRules.Indicator(20f, 6f));
            Assert.AreEqual(-1, VehicleFxRules.Indicator(-20f, 6f));
            Assert.AreEqual(0, VehicleFxRules.Indicator(3f, 6f), "wheel straight");
            Assert.AreEqual(0, VehicleFxRules.Indicator(20f, 25f), "too fast for a junction");
        }

        [Test]
        public void LightBudget_GivesTheNearestFew_TheRealLights()
        {
            var a = VehicleFxRules.NearestAllowed(new[] { 900f, 4f, 100f, 2500f, 36f }, 3);
            CollectionAssert.AreEqual(new[] { false, true, true, false, true }, a);
            Assert.AreEqual(2, System.Linq.Enumerable.Count(VehicleFxRules.NearestAllowed(new[] { 5f, 1f }, 3), x => x), "fewer than the budget");
            Assert.IsEmpty(VehicleFxRules.NearestAllowed(new float[0], 3));
        }

        [Test]
        public void Blink_IsOnAndOff()
        {
            Assert.IsTrue(VehicleFxRules.BlinkOn(0.1f));
            Assert.IsFalse(VehicleFxRules.BlinkOn(0.4f));
            Assert.IsTrue(VehicleFxRules.BlinkOn(0.7f));
        }

        [Test]
        public void Slip_FromDriftBrakingAndHandbrake()
        {
            Assert.AreEqual(0f, VehicleFxRules.Slip(0.5f, 1f, 20f, false), 1e-4f, "gripping");
            Assert.Greater(VehicleFxRules.Slip(6f, 0f, 20f, false), 0.5f, "sideways");
            Assert.Greater(VehicleFxRules.Slip(0f, 8.5f, 20f, false), 0.5f, "locking up");
            Assert.AreEqual(0f, VehicleFxRules.Slip(0f, 11f, 3f, false), 1e-4f, "too slow to leave a mark");
            Assert.Greater(VehicleFxRules.Slip(0f, 0f, 12f, true), 0.3f, "handbrake");
            Assert.LessOrEqual(VehicleFxRules.Slip(30f, 30f, 30f, true), 1f);
        }

        [Test]
        public void Dust_ByGround()
        {
            Assert.AreEqual(VehicleFxRules.DustKind.None, VehicleFxRules.DustFor(FootSurface.Concrete, 0f), "clean tarmac");
            Assert.AreEqual(VehicleFxRules.DustKind.Smoke, VehicleFxRules.DustFor(FootSurface.Concrete, 0.8f), "sliding on tarmac");
            Assert.AreEqual(VehicleFxRules.DustKind.Dust, VehicleFxRules.DustFor(FootSurface.Dirt, 0f));
            Assert.AreEqual(VehicleFxRules.DustKind.Grass, VehicleFxRules.DustFor(FootSurface.Grass, 0f));
            Assert.AreEqual(VehicleFxRules.DustKind.Spray, VehicleFxRules.DustFor(FootSurface.Water, 0f));
            Assert.AreEqual(0f, VehicleFxRules.DustRate(VehicleFxRules.DustKind.Dust, 1f, 0f), "crawling");
            Assert.Greater(VehicleFxRules.DustRate(VehicleFxRules.DustKind.Dust, 25f, 0f), VehicleFxRules.DustRate(VehicleFxRules.DustKind.Dust, 6f, 0f), "faster = more");
            Assert.AreEqual(0f, VehicleFxRules.DustRate(VehicleFxRules.DustKind.None, 30f, 1f));
        }

        [Test]
        public void Screech_OnlyOnHardGround_AndWindOnlyAtSpeed()
        {
            Assert.Greater(VehicleFxRules.ScreechVolume(FootSurface.Concrete, 0.6f), 0.5f);
            Assert.AreEqual(0f, VehicleFxRules.ScreechVolume(FootSurface.Grass, 1f), 1e-4f);
            Assert.AreEqual(0f, VehicleFxRules.WindVolume(10f), 1e-4f);
            Assert.Greater(VehicleFxRules.WindVolume(40f), 0.5f);
            Assert.AreEqual(0f, VehicleFxRules.ImpactVolume(0.02f), "a bullet");
            Assert.Greater(VehicleFxRules.ImpactVolume(0.2f), VehicleFxRules.ImpactVolume(0.06f));
        }

        [Test]
        public void EngineGear_ClimbsWithSpeed_AndMatchesTheRevDrop()
        {
            int last = -1;
            for (float v = 0f; v <= 40f; v += 0.5f)
            {
                int g = EngineNote.Gear(v, 40f, 5);
                Assert.GreaterOrEqual(g, last);
                last = g;
            }
            Assert.AreEqual(0, EngineNote.Gear(1f, 40f, 5));
            Assert.AreEqual(4, EngineNote.Gear(40f, 40f, 5));
            // Just over a shift the revs fall.
            float up = 0f;
            for (float v = 0.5f; v < 40f && up == 0f; v += 0.1f) if (EngineNote.Gear(v, 40f, 5) == 1) up = v;
            Assert.Less(EngineNote.Rpm01(up, 40f, 5, 0f), EngineNote.Rpm01(up - 0.2f, 40f, 5, 0f));
        }

        [Test]
        public void SynthSounds_AreFiniteAndLoudEnough()
        {
            foreach (var (name, data) in new[] { ("whine", VehicleAudio.SynthWhine()), ("screech", VehicleAudio.SynthScreech()), ("wind", VehicleAudio.SynthWind()),
                ("crunch", VehicleAudio.SynthCrunch()), ("clunk", VehicleAudio.SynthClunk()) })
            {
                float peak = 0f;
                foreach (float s in data) { Assert.IsFalse(float.IsNaN(s) || float.IsInfinity(s), name); peak = System.Math.Max(peak, System.Math.Abs(s)); }
                Assert.Greater(peak, 0.5f, name);
                Assert.LessOrEqual(peak, 1f, name);
            }
        }
    }
}

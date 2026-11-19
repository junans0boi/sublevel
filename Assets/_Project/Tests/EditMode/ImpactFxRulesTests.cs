using Game.Core;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-470/471 (#197 #198 #201): decal pooling, blood levels, debris per surface, water crossing and camera trauma.
    public class ImpactFxRulesTests
    {
        [Test]
        public void DecalRing_ReusesTheOldestFirst_AndNeverHoldsMoreThanItsCapacity()
        {
            var ring = new DecalRing(4, 10f, 2f);
            for (int i = 0; i < 4; i++) Assert.AreEqual(i, ring.Add(i * 0.1f));
            Assert.AreEqual(0, ring.Add(1f), "the fifth decal takes the oldest slot");
            for (int i = 0; i < 50; i++) ring.Add(2f);
            Assert.AreEqual(4, ring.Live(2f));
        }

        [Test]
        public void DecalRing_FadesOverTheLastSeconds_ThenFreesTheSlot()
        {
            var ring = new DecalRing(2, 10f, 2f);
            int s = ring.Add(0f);
            Assert.AreEqual(1f, ring.Alpha(s, 5f));
            Assert.AreEqual(0.5f, ring.Alpha(s, 9f), 1e-4f);
            Assert.AreEqual(0f, ring.Alpha(s, 10f));
            Assert.AreEqual(0f, ring.Alpha(1, 0f), "a slot never used is free");
            Assert.AreEqual(0, ring.Live(11f));
        }

        [Test]
        public void Blood_OffShowsNothing_LowShowsLessThanNormal()
        {
            Assert.AreEqual(0, BloodRules.SprayParticles(BloodLevel.Off, true, true));
            Assert.Less(BloodRules.SprayParticles(BloodLevel.Low, false, true), BloodRules.SprayParticles(BloodLevel.Normal, false, true));
            Assert.Less(BloodRules.SprayParticles(BloodLevel.Normal, false, false), BloodRules.SprayParticles(BloodLevel.Normal, false, true));
            Assert.Greater(BloodRules.SprayParticles(BloodLevel.Normal, true, true), BloodRules.SprayParticles(BloodLevel.Normal, false, true));
            Assert.IsFalse(BloodRules.FloorSplat(BloodLevel.Off));
            Assert.IsFalse(BloodRules.WallSplatter(BloodLevel.Low));
            Assert.IsTrue(BloodRules.WallSplatter(BloodLevel.Normal));
        }

        [Test]
        public void Blood_DripsWhileKnockedOrJustHit()
        {
            Assert.IsTrue(BloodRules.Drips(BloodLevel.Low, true, 100f));
            Assert.IsTrue(BloodRules.Drips(BloodLevel.Normal, false, 1f));
            Assert.IsFalse(BloodRules.Drips(BloodLevel.Normal, false, BloodRules.TrailSeconds + 0.1f));
            Assert.IsFalse(BloodRules.Drips(BloodLevel.Off, true, 0f));
        }

        [Test]
        public void EverySurface_HasItsOwnDebris_ExceptBodiesWhichBleed()
        {
            Assert.AreEqual(Debris.WoodChips, DebrisRules.Of(BulletSurface.Wood));
            Assert.AreEqual(Debris.ConcreteChunks, DebrisRules.Of(BulletSurface.Concrete));
            Assert.AreEqual(Debris.DirtClods, DebrisRules.Of(BulletSurface.Ground));
            Assert.AreEqual(Debris.Sparks, DebrisRules.Of(BulletSurface.Metal));
            Assert.AreEqual(Debris.GlassShards, DebrisRules.Of(BulletSurface.Glass));
            Assert.AreEqual(Debris.None, DebrisRules.Of(BulletSurface.Body));
            foreach (Debris d in System.Enum.GetValues(typeof(Debris)))
            {
                if (d == Debris.None) continue;
                Assert.Greater(DebrisRules.Count(d, true), DebrisRules.Count(d, false), d.ToString());
                Assert.GreaterOrEqual(DebrisRules.Count(d, false), 1, d.ToString());
            }
        }

        [Test]
        public void Water_IsCrossedOnlyGoingDownThroughTheSurface()
        {
            Assert.IsTrue(WaterCrossing.TryCross(2f, -2f, 0f, out float t));
            Assert.AreEqual(0.5f, t, 1e-5f);
            Assert.IsFalse(WaterCrossing.TryCross(2f, 0.5f, 0f, out _), "ends above the water");
            Assert.IsFalse(WaterCrossing.TryCross(-1f, -3f, 0f, out _), "already under water");
            Assert.IsFalse(WaterCrossing.TryCross(-1f, 3f, 0f, out _), "going up");
        }

        [Test]
        public void Shake_DrainsAndScalesWithTheSetting()
        {
            var shake = new CameraShake();
            shake.Add(0.5f);
            shake.Add(0.8f);
            Assert.AreEqual(1f, shake.Trauma, "capped at 1");
            float worst = 0f;
            for (int i = 0; i < 200; i++)
            {
                shake.Offset(i * 0.013, 1f, out float p, out float y, out float r);
                worst = System.Math.Max(worst, System.Math.Abs(p));
                Assert.LessOrEqual(System.Math.Abs(p), CameraShake.MaxPitchDeg + 1e-4f);
                Assert.LessOrEqual(System.Math.Abs(y), CameraShake.MaxYawDeg + 1e-4f);
                Assert.LessOrEqual(System.Math.Abs(r), CameraShake.MaxRollDeg + 1e-4f);
            }
            Assert.Greater(worst, 0.5f, "it does move");
            shake.Offset(0.37, 0f, out float p0, out float y0, out float r0);
            Assert.AreEqual(0f, p0 + y0 + r0, "0% setting = no shake");
            shake.Tick(1f / CameraShake.DrainPerSecond);
            Assert.AreEqual(0f, shake.Trauma);
        }

        [Test]
        public void Shake_EventsScaleSensibly()
        {
            Assert.AreEqual(CameraShake.Blast(1f, 1f), CameraShake.Blast(CameraShake.BlastFullMeters, 1f));
            Assert.Greater(CameraShake.Blast(10f, 1f), CameraShake.Blast(20f, 1f), "falls off with distance");
            Assert.AreEqual(0f, CameraShake.Blast(CameraShake.BlastZeroMeters, 1f));
            Assert.AreEqual(0f, CameraShake.Landing(0.5f), "a step down does not shake");
            Assert.Greater(CameraShake.Landing(6f), CameraShake.Landing(2f));
            Assert.Greater(CameraShake.Hit(true), CameraShake.Hit(false));
            Assert.Greater(CameraShake.OwnShot(25f, 9), CameraShake.OwnShot(20f, 1), "a shotgun kicks harder than a pistol");
            Assert.LessOrEqual(CameraShake.OwnShot(200f, 9), 0.22f, "own fire never shakes like a blast");
        }

        [Test]
        public void Settings_HaveTheShakeSliderAndTheBloodChoice()
        {
            var shake = SettingsCatalog.Get(SettingId.CameraShake);
            Assert.AreEqual(SettingKind.Slider, shake.Kind);
            Assert.AreEqual(0f, shake.Min);
            Assert.AreEqual(1f, shake.Max);
            var blood = SettingsCatalog.Get(SettingId.Blood);
            Assert.AreEqual(3, blood.OptionCount);
            Assert.AreEqual((int)BloodLevel.Normal, (int)blood.Default);
        }
    }
}

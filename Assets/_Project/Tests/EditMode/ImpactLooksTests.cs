using Game.Core;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-118 (#152): every bullet surface has its own impact look; marks stay on walls and ground only.
    public class ImpactLooksTests
    {
        [Test]
        public void EverySurface_HasALook_AndTheLooksDiffer()
        {
            var dust = new System.Collections.Generic.HashSet<UnityEngine.Color>();
            foreach (BulletSurface s in System.Enum.GetValues(typeof(BulletSurface)))
            {
                var look = ImpactLooks.Of(s);
                Assert.Greater(look.DustSize, 0f, s.ToString());
                dust.Add(look.Dust);
            }
            Assert.GreaterOrEqual(dust.Count, 5, "metal and car share sparks, everything else its own dust");
        }

        [Test]
        public void MarksStayOnWallsAndGround_NotOnBodiesGlassOrCars()
        {
            foreach (var s in new[] { BulletSurface.Ground, BulletSurface.Concrete, BulletSurface.Wood, BulletSurface.Metal })
                Assert.Greater(ImpactLooks.Of(s).Mark.a, 0f, s.ToString());
            foreach (var s in new[] { BulletSurface.Body, BulletSurface.Glass, BulletSurface.Vehicle })
                Assert.AreEqual(0f, ImpactLooks.Of(s).Mark.a, s.ToString());
        }

        [Test]
        public void MetalSparksBrighterThanSoil_AndABodyHasNoFlash()
        {
            Assert.Greater(ImpactLooks.Of(BulletSurface.Metal).Flash, ImpactLooks.Of(BulletSurface.Concrete).Flash);
            Assert.Greater(ImpactLooks.Of(BulletSurface.Concrete).Flash, ImpactLooks.Of(BulletSurface.Ground).Flash);
            Assert.AreEqual(0f, ImpactLooks.Of(BulletSurface.Body).Flash);
        }
    }
}

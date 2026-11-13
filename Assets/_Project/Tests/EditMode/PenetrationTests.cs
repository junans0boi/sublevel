using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-114 (#151): which surfaces a bullet goes through, and what it keeps.
    public class PenetrationTests
    {
        static readonly PenetrationRules R = new PenetrationRules();

        [TestCase(BulletSurface.Wood, 0.2f, 0.70f, 0.80f)]
        [TestCase(BulletSurface.Metal, 0.2f, 0.55f, 0.70f)]
        [TestCase(BulletSurface.Vehicle, 1.8f, 0.50f, 0.65f)]
        [TestCase(BulletSurface.Glass, 0.01f, 1f, 0.95f)]
        public void ThinPieces_AreGoneThrough_WithTheTabledLoss(BulletSurface s, float thick, float damage, float speed)
        {
            Assert.IsTrue(Penetration.TryPass(s, thick, 800f, R, out float dk, out float sk));
            Assert.AreEqual(damage, dk, 1e-5f);
            Assert.AreEqual(speed, sk, 1e-5f);
        }

        [TestCase(BulletSurface.Concrete, 0.01f)]
        [TestCase(BulletSurface.Ground, 0.01f)]
        [TestCase(BulletSurface.Body, 0.01f)]
        [TestCase(BulletSurface.Wood, 0.6f)]
        [TestCase(BulletSurface.Metal, 0.4f)]
        [TestCase(BulletSurface.Wood, float.PositiveInfinity)]
        public void ConcreteGroundAndThickPieces_StopTheBullet(BulletSurface s, float thick)
        {
            Assert.IsFalse(Penetration.TryPass(s, thick, 800f, R, out float dk, out float sk));
            Assert.AreEqual(0f, dk);
            Assert.AreEqual(0f, sk);
        }

        [Test]
        public void ASlowBullet_StopsInAPieceItWouldLoseTooMuchSpeedIn()
        {
            Assert.IsTrue(Penetration.TryPass(BulletSurface.Wood, 0.2f, 160f, R, out _, out _), "160 x 0.8 = 128 >= 120");
            Assert.IsFalse(Penetration.TryPass(BulletSurface.Wood, 0.2f, 140f, R, out _, out _), "140 x 0.8 = 112 < 120");
        }

        [Test]
        public void EverySurface_HasAFullRow()
        {
            int n = System.Enum.GetValues(typeof(BulletSurface)).Length;
            Assert.AreEqual(n, R.MaxThickness.Length);
            Assert.AreEqual(n, R.DamageKeep.Length);
            Assert.AreEqual(n, R.SpeedKeep.Length);
        }
    }
}

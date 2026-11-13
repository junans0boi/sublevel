using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-110 (PUBG_GAP_V2 G9): the fire bottle's fire, the burn that follows a body out of it, and the aim limit.
    public class MolotovTests
    {
        static readonly MolotovRules R = new MolotovRules();

        static float Run(ref float burn, bool inFire, float seconds)
        {
            float total = 0f;
            for (float t = 0f; t < seconds - 1e-4f; t += 1f / 60f) total += Molotov.Step(ref burn, inFire, 1f / 60f, R);
            return total;
        }

        [Test]
        public void FourSecondsInTheFire_CostsAboutFifty()
        {
            float burn = 0f;
            Assert.AreEqual(50f, Run(ref burn, true, 4f), 0.5f, "12.5 per second");
            Assert.AreEqual(R.BurnSeconds, burn, 1e-3f, "and the burn is topped up every tick");
        }

        [Test]
        public void LeavingTheFire_KeepsBurning_ThenGoesOut()
        {
            float burn = 0f;
            Run(ref burn, true, 1f);
            float after = Run(ref burn, false, 4.5f);
            Assert.AreEqual(40f, after, 0.7f, "10 per second for the four burn seconds, then nothing");
            Assert.AreEqual(0f, burn, 1e-4f);
            Assert.AreEqual(0f, Run(ref burn, false, 2f), "out");
        }

        [Test]
        public void FireAndBurn_DoNotStack()
        {
            float burn = R.BurnSeconds;
            Assert.AreEqual(12.5f, Molotov.Step(ref burn, true, 1f, R), 1e-4f, "the larger of 12.5 and 10, not 22.5");
        }

        [Test]
        public void InFire_IsTheDiscOnTheGround_AndTheBodyAboveIt()
        {
            Assert.IsTrue(Molotov.InFire(0f, 0f, 0f, 0f, 0f, 0f, R));
            Assert.IsTrue(Molotov.InFire(2.9f, 0f, 0f, 0f, 0f, 0f, R));
            Assert.IsFalse(Molotov.InFire(3.1f, 0f, 0f, 0f, 0f, 0f, R), "outside the radius");
            Assert.IsFalse(Molotov.InFire(0f, 3f, 0f, 0f, 0f, 0f, R), "on a roof above it");
            Assert.IsFalse(Molotov.InFire(0f, -2f, 0f, 0f, 0f, 0f, R), "in a basement below");
            Assert.IsTrue(Molotov.InFire(1f, 0.3f, 1f, 0f, 0f, 0f, R), "a step up is still in it");
        }

        [Test]
        public void BurningBodies_CannotUseTheSights()
        {
            Assert.AreEqual(AimMode.Shoulder, Molotov.LimitAim(AimMode.Sights, true));
            Assert.AreEqual(AimMode.Sights, Molotov.LimitAim(AimMode.Sights, false));
            Assert.AreEqual(AimMode.Hip, Molotov.LimitAim(AimMode.Hip, true));
            Assert.AreEqual(AimMode.Shoulder, Molotov.LimitAim(AimMode.Shoulder, true));
        }

        [Test]
        public void TheKind_IsPickedUpCarriedAndNamed()
        {
            Assert.IsTrue(Throwables.TryKindOfLoot(LootKind.Molotov, out var kind));
            Assert.AreEqual(ThrowableKind.Molotov, kind);
            Assert.AreEqual("화염병", Throwables.NameOf(ThrowableKind.Molotov));
            Assert.AreEqual(LootKind.Molotov, Throwables.LootKindOf(ThrowableKind.Molotov));
            Assert.AreEqual(4f, Throwables.FuseOf(ThrowableKind.Molotov, new ThrowableRules(), new SmokeRules()), 1e-4f);
            Assert.AreEqual(30, Carry.UnitTenths(LootKind.Molotov, AmmoType.Pistol9, new CarryRules()));
            Assert.AreEqual((int)DamageCause.Fire, 7);
        }
    }
}

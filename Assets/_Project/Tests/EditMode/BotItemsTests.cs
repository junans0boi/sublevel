using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #148 (ADR-116): how bots use what they carry.
    public class BotItemsTests
    {
        static readonly ThrowableRules Rules = new ThrowableRules();

        [TestCase(10f), TestCase(18f), TestCase(28f)]
        public void LaunchPitch_LandsTheThrowOnTheTarget(float distance)
        {
            Assert.IsTrue(BotItems.TryLaunchPitch(distance, 0f, Rules.ThrowSpeed, Rules.Gravity, out float pitch));
            float rad = pitch * UnityEngineMath.Deg2Rad;
            float vy = Rules.ThrowSpeed * (float)System.Math.Sin(rad), vx = Rules.ThrowSpeed * (float)System.Math.Cos(rad);
            float t = 2f * vy / Rules.Gravity; // back at hand height
            Assert.AreEqual(distance, vx * t, 0.05f);
            Assert.Less(pitch, 45f, "the flat arc, not the lob");
        }

        [Test]
        public void LaunchPitch_OutOfReach_IsRefused()
        {
            Assert.IsFalse(BotItems.TryLaunchPitch(60f, 0f, Rules.ThrowSpeed, Rules.Gravity, out _));
            Assert.IsFalse(BotItems.TryLaunchPitch(0.1f, 0f, Rules.ThrowSpeed, Rules.Gravity, out _));
        }

        [Test]
        public void PickThrow_FollowsDistanceAndStillness()
        {
            var r = new BotRules();
            Assert.IsNull(BotItems.PickThrow(5f, false, 2, 2, 2, 0f, r), "too close for anything");
            Assert.IsNull(BotItems.PickThrow(40f, true, 2, 2, 2, 0f, r), "too far");
            Assert.AreEqual(ThrowableKind.Frag, BotItems.PickThrow(24f, false, 2, 2, 2, 0f, r));
            Assert.AreEqual(ThrowableKind.Molotov, BotItems.PickThrow(15f, true, 1, 1, 1, 0f, r), "someone standing still gets the fire bottle");
            Assert.AreEqual(ThrowableKind.Frag, BotItems.PickThrow(15f, false, 1, 0, 1, 0f, r), "a mover does not");
            Assert.AreEqual(ThrowableKind.Flash, BotItems.PickThrow(15f, false, 1, 1, 0, 0.2f, r));
            Assert.AreEqual(ThrowableKind.Frag, BotItems.PickThrow(15f, false, 1, 1, 0, 0.8f, r));
            Assert.AreEqual(ThrowableKind.Flash, BotItems.PickThrow(8f, false, 1, 1, 0, 0.8f, r), "a grenade is not thrown this close");
            Assert.IsNull(BotItems.PickThrow(15f, true, 0, 0, 0, 0f, r), "nothing carried");
        }

        [Test]
        public void WantsLoot_StopsAtTheCarriedLimit()
        {
            var r = new BotRules();
            Assert.IsTrue(BotItems.WantsLoot(LootKind.Bandage, 0, 0, 0, 0, 0, 0, 0, 0, 0, r));
            Assert.IsFalse(BotItems.WantsLoot(LootKind.Bandage, 3, 1, 0, 0, 0, 0, 0, 0, 0, r));
            Assert.IsTrue(BotItems.WantsLoot(LootKind.Drink, 0, 0, 1, 0, 0, 0, 0, 0, 0, r));
            Assert.IsFalse(BotItems.WantsLoot(LootKind.Drink, 0, 0, 2, 0, 0, 0, 0, 0, 0, r));
            Assert.IsTrue(BotItems.WantsLoot(LootKind.Molotov, 0, 0, 0, 0, 0, 0, 0, 0, 1, r));
            Assert.IsFalse(BotItems.WantsLoot(LootKind.Flash, 0, 0, 0, 0, 0, 0, 0, 2, 0, r));
            Assert.IsFalse(BotItems.WantsLoot(LootKind.Weapon, 0, 0, 0, 0, 0, 0, 0, 0, 0, r), "weapons are the brain's");
        }

        [Test]
        public void WantsBoost_WhenHurtOrJustAfterAFight_NotWhenBusy()
        {
            var r = new BotRules();
            Assert.IsTrue(BotItems.WantsBoost(0.7f, 100, -100, 1, false, r), "hurt");
            Assert.IsTrue(BotItems.WantsBoost(1f, 100, 95, 1, false, r), "five seconds after a fight");
            Assert.IsFalse(BotItems.WantsBoost(1f, 100, 50, 1, false, r), "calm and healthy");
            Assert.IsFalse(BotItems.WantsBoost(0.5f, 100, 95, 0, false, r), "none carried");
            Assert.IsFalse(BotItems.WantsBoost(0.5f, 100, 95, 1, true, r), "reloading");
        }
    }

    static class UnityEngineMath
    {
        public const float Deg2Rad = (float)(System.Math.PI / 180.0);
    }
}

using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class ItemMotionTests
    {
        [Test]
        public void Throw_CocksBackThenSnapsForwardThenReturns_GrenadeLeavesTheHand()
        {
            var hold = ItemMotion.Throw(HandProp.Grenade, 0f, -1f, false);
            var cocked = ItemMotion.Throw(HandProp.Grenade, 1f, -1f, false);
            Assert.Less(cocked.RZ, hold.RZ - 0.4f, "arm goes back");
            Assert.Greater(cocked.RY, hold.RY + 0.2f, "and up");
            var end = ItemMotion.Throw(HandProp.Grenade, 1f, 0.45f, false);
            Assert.Greater(end.RZ, hold.RZ + 0.15f, "snaps forward");
            Assert.AreEqual(HandProp.None, end.Prop, "grenade released");
            var back = ItemMotion.Throw(HandProp.Grenade, 1f, 1f, false);
            Assert.AreEqual(hold.RZ, back.RZ, 1e-3f);
            Assert.AreEqual(HandProp.Grenade, ItemMotion.Throw(HandProp.Grenade, 1f, 0.05f, false).Prop);
        }

        [Test]
        public void Throw_CookingPutsTheLeftHandOnTheRight()
        {
            var cook = ItemMotion.Throw(HandProp.Grenade, 0f, -1f, true);
            Assert.AreEqual(1f, cook.LeftW);
            Assert.AreEqual(0f, ItemMotion.Throw(HandProp.Grenade, 0f, -1f, false).LeftW);
            Assert.Greater(cook.LX, 0.25f, "across the chest from the left shoulder");
        }

        [Test]
        public void Heal_HandsComeUpWorkAndGoDown_PerKind()
        {
            foreach (var kind in new[] { HealKind.Bandage, HealKind.FirstAid, HealKind.Drink, HealKind.Pill, HealKind.Syringe })
            {
                Assert.AreEqual(0f, ItemMotion.Heal(kind, 0f).RightW, 1e-3f, kind + " start");
                Assert.AreEqual(0f, ItemMotion.Heal(kind, 1f).RightW, 1e-3f, kind + " end");
                Assert.AreEqual(1f, ItemMotion.Heal(kind, 0.5f).RightW, 1e-3f, kind + " middle");
                Assert.AreNotEqual(HandProp.None, ItemMotion.Heal(kind, 0.5f).Prop);
            }
            Assert.Greater(ItemMotion.Heal(HealKind.Drink, 0.5f).RY, 0.1f, "bottle at the mouth");
            Assert.AreEqual(1f, ItemMotion.Heal(HealKind.Bandage, 0.5f).LeftW, "bandage uses both hands");
        }

        [Test]
        public void Gestures_ReachAndReturn()
        {
            Assert.AreEqual(0f, ItemMotion.Gesture(HandGesture.Pickup, 0f).RightW, 1e-3f);
            Assert.Less(ItemMotion.Gesture(HandGesture.Pickup, 0.5f).RY, -0.4f, "down to the ground");
            Assert.Greater(ItemMotion.Gesture(HandGesture.Door, 0.5f).RZ, 0.5f, "forward to the door");
            Assert.AreEqual(0f, ItemMotion.Gesture(HandGesture.Door, 1f).RightW, 1e-3f);
        }
    }
}

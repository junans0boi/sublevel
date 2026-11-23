using Game.Core;
using UnityEngine;
using NUnit.Framework;

namespace Game.Tests
{
    // #170, ADR-142: what the server tolerates from a client, as pure rules. The legit numbers are the worst a real
    // player makes at 100 ms / 2 % loss; the cheating ones are what a modified client would send.
    public class InputGuardTests
    {
        static readonly GuardRules G = new GuardRules();

        [Test]
        public void RulesJson_CarriesTheGuardLimits()
        {
            var g = JsonUtility.FromJson<GameRules>(System.IO.File.ReadAllText("Assets/_Project/Data/Resources/GameData/rules.json")).Guard;
            Assert.AreEqual(25f, g.TeleportMaxMeters);
            Assert.AreEqual(10f, g.ItemRequestsPerSecond, "#503");
            Assert.AreEqual(20, g.ItemRequestBurst);
        }

        [Test]
        public void AVehicleExit_IsATeleportTheServerTakes()
        {
            // The car ran on 0.25 s at 40 m/s = 10 m while the owner stepped out, plus the door offset.
            Assert.IsTrue(InputGuard.TeleportOk(12f, 5.0, G));
        }

        [Test]
        public void ATeleportAcrossTheMap_OrOneAfterAnother_IsRefused()
        {
            Assert.IsFalse(InputGuard.TeleportOk(300f, 5.0, G));
            Assert.IsFalse(InputGuard.TeleportOk(float.NaN, 5.0, G));
            Assert.IsFalse(InputGuard.TeleportOk(float.PositiveInfinity, 5.0, G));
            Assert.IsFalse(InputGuard.TeleportOk(10f, 0.1, G), "two in a row");
        }

        [Test]
        public void SlotsAndDirections_AreChecked()
        {
            Assert.IsTrue(InputGuard.ValidSlot(0, Loadout.SlotCount));
            Assert.IsFalse(InputGuard.ValidSlot(-1, Loadout.SlotCount));
            Assert.IsFalse(InputGuard.ValidSlot(Loadout.SlotCount, Loadout.SlotCount));
            Assert.IsTrue(InputGuard.Finite(0f, 0f, 1f));
            Assert.IsFalse(InputGuard.Finite(float.NaN, 0f, 1f));
            Assert.IsFalse(InputGuard.Finite(0f, float.PositiveInfinity, 1f));
        }

        // A client firing exactly at the weapon's rate, with network jitter that bunches 3 requests in one instant.
        [Test]
        public void ALegitShooter_AtTheWeaponsRate_IsNeverRefused_EvenWhenPacketsBunch()
        {
            var bucket = new RequestBucket();
            double interval = 0.1, rate = InputGuard.FireRequestsPerSecond(interval, G);
            for (int i = 0; i < 300; i++)
            {
                // on schedule, except that every 20th request is held 0.2 s and arrives with the next two
                double at = i * interval;
                if (i % 20 == 1 || i % 20 == 2) at = (i - i % 20 + 3) * interval;
                Assert.IsTrue(bucket.TryTake(at, rate, G.FireBurst), "request " + i);
            }
        }

        [Test]
        public void AFireMacroAtDoubleTheRate_IsRefused()
        {
            var bucket = new RequestBucket();
            double interval = 0.1, rate = InputGuard.FireRequestsPerSecond(interval, G);
            int refused = 0;
            for (int i = 0; i < 100; i++) if (!bucket.TryTake(i * interval / 2.0, rate, G.FireBurst)) refused++;
            Assert.Greater(refused, 20, "about a third of a double-rate stream does not fit");
        }

        [Test]
        public void MeleeAndBareHands_UseTheMeleeRate() =>
            Assert.AreEqual(G.MeleeRequestsPerSecond, InputGuard.FireRequestsPerSecond(0.0, G));

        [Test]
        public void TheBucket_RefillsWhileIdle()
        {
            var bucket = new RequestBucket();
            for (int i = 0; i < G.FireBurst; i++) Assert.IsTrue(bucket.TryTake(0, 1, G.FireBurst));
            Assert.IsFalse(bucket.TryTake(0, 1, G.FireBurst), "burst used up");
            Assert.IsTrue(bucket.TryTake(2.0, 1, G.FireBurst), "two seconds later");
        }
    }
}

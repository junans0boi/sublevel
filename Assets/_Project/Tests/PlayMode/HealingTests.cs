using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // PUBG_GAP 4-3 batch H in the real TestBox scene as a host: healing takes time, heals only when it finishes,
    // is cancelled (item kept) by damage, shots and weapon switches, and slows walking meanwhile. The uses go
    // through the server API (CombatantHealing), so no test depends on input.
    public class HealingTests
    {
        const string Wiring = "the Player prefab needs a CombatantHealing component (batch H wiring notes)";

        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox()
        {
            PlayerController.InputOverride = new ScriptedInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static CombatantHealing Healer(Combatant me)
        {
            var healing = me.GetComponent<CombatantHealing>();
            Assert.IsNotNull(healing, Wiring);
            return healing;
        }

        [UnityTest]
        public IEnumerator ABandage_HealsAfterItsTime_NotBefore_AndIsUsedUp()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var healing = Healer(me);
            me.ServerTakeDamage(50f);
            Assert.AreEqual(50f, me.Hp.Value, 1e-3f);
            Assert.AreEqual(2, healing.ServerAdd(HealKind.Bandage, 2));
            Assert.AreEqual(1f, CombatantHealing.MoveFactorOf(me), "not healing: full speed");

            Assert.IsTrue(healing.ServerUse(), "H uses the best fit: a bandage under 75 health");
            Assert.IsTrue(healing.Using);
            Assert.AreEqual(HealKind.Bandage, healing.Kind);
            Assert.AreEqual("붕대", healing.ItemName);
            Assert.AreEqual(3f, healing.Seconds, 1e-3f);
            Assert.AreEqual(HealRules.Current.MoveFactor, CombatantHealing.MoveFactorOf(me), 1e-4f);
            Assert.Less(CombatantHealing.MoveFactorOf(me), 1f);

            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(healing.Using);
            Assert.AreEqual(50f, me.Hp.Value, 1e-3f, "nothing before the time is out");
            Assert.AreEqual(2, healing.Bandages.Value, "and nothing is used up yet");
            Assert.AreEqual(0.5f, healing.Progress01, 0.2f);
            Assert.IsFalse(healing.ServerBegin(HealKind.FirstAid), "one use at a time");

            yield return WaitUntil(() => !healing.Using, 5f, "the bandage finished");
            Assert.AreEqual(62f, me.Hp.Value, 1e-3f, "+12");
            Assert.AreEqual(1, healing.Bandages.Value);
            Assert.AreEqual(1, healing.ServerCompleted);
            Assert.AreEqual(1f, CombatantHealing.MoveFactorOf(me));
            Assert.AreEqual(0f, healing.Progress01);
        }

        [UnityTest]
        public IEnumerator SmallItems_StopAt75_AndAreRefusedAbove()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var healing = Healer(me);
            me.ServerTakeDamage(30f); // 70
            healing.ServerAdd(HealKind.FirstAid, 1);
            Assert.IsTrue(healing.ServerBegin(HealKind.FirstAid));
            yield return WaitUntil(() => !healing.Using, 9f, "the first-aid kit finished");
            Assert.AreEqual(75f, me.Hp.Value, 1e-3f, "+40 would be 110: only up to 75");
            Assert.AreEqual(0, healing.FirstAids.Value);

            healing.ServerAdd(HealKind.Bandage, 1);
            Assert.IsFalse(healing.ServerBegin(HealKind.Bandage), "at 75 a bandage cannot help");
            Assert.IsFalse(healing.ServerUse());
            Assert.AreEqual(1, healing.Bandages.Value);
        }

        [UnityTest]
        public IEnumerator Damage_CancelsTheUse_AndKeepsTheItem()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var healing = Healer(me);
            me.ServerTakeDamage(60f);
            healing.ServerAdd(HealKind.FirstAid, 1);
            Assert.IsTrue(healing.ServerBegin(HealKind.FirstAid));
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(healing.Using);
            me.ServerTakeDamage(5f);
            yield return null;
            yield return null;
            Assert.IsFalse(healing.Using, "a hit cancels");
            Assert.AreEqual(1, healing.FirstAids.Value, "the kit is not used up");
            Assert.AreEqual(35f, me.Hp.Value, 1e-3f, "and nothing was healed");
            Assert.AreEqual(1, healing.ServerCancelled);
            Assert.AreEqual(1f, CombatantHealing.MoveFactorOf(me));
        }

        [UnityTest]
        public IEnumerator FiringAndSwitchingWeapons_Cancel()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var healing = Healer(me);
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }));
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 }));
            me.ServerSelectSlot(0);
            me.ServerTakeDamage(60f);
            healing.ServerAdd(HealKind.Bandage, 2);

            Assert.IsTrue(healing.ServerBegin(HealKind.Bandage));
            yield return null;
            Assert.IsTrue(healing.Using);
            Assert.IsTrue(me.ServerFire(Vector3.up), "a shot straight up");
            yield return null;
            yield return null;
            Assert.IsFalse(healing.Using, "firing cancels");
            Assert.AreEqual(2, healing.Bandages.Value);

            Assert.IsTrue(healing.ServerBegin(HealKind.Bandage));
            yield return null;
            Assert.IsTrue(healing.Using);
            Assert.IsTrue(me.ServerSelectSlot(1));
            yield return null;
            yield return null;
            Assert.IsFalse(healing.Using, "switching weapon cancels");
            Assert.AreEqual(2, healing.Bandages.Value);
            Assert.AreEqual(40f, me.Hp.Value, 1e-3f);
        }

        [UnityTest]
        public IEnumerator ThePickup_GoesIntoTheCount_AndTheMedkitTakesItsTime()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var healing = Healer(me);
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = HealRules.BandageLootKind, Amount = 2 }), "ServerGive hands the new kind to CombatantHealing");
            Assert.AreEqual(2, healing.Bandages.Value);
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = HealRules.FirstAidLootKind, Amount = 1 }));
            Assert.AreEqual(1, healing.FirstAids.Value);

            me.ServerTakeDamage(90f); // 10
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }));
            Assert.IsTrue(healing.ServerBegin(HealKind.Medkit));
            Assert.AreEqual(8f, healing.Seconds, 1e-3f);
            Assert.AreEqual("구급상자", healing.ItemName);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(10f, me.Hp.Value, 1e-3f, "the medkit is slow");
            healing.ServerCancel();
            Assert.AreEqual(1, me.Medkits.Value, "cancelled: still there");
        }
    }
}

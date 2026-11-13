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
    // PUBG_GAP 4-3 batch H in the real TestBox scene as a host: the bag has a capacity, what does not fit is
    // refused (ammo: as many rounds as fit), a vest makes it bigger, and ammo of a calibre a weapon uses is picked
    // up by walking over it. Needs the Player prefab's CombatantCarry and the ServerGive hook (wiring notes).
    public class CarryTests
    {
        const string Wiring = "the Player prefab needs a CombatantCarry component and Combatant.ServerGive must ask it first (batch H wiring notes)";

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

        static CombatantCarry Bag(Combatant me)
        {
            var carry = me.GetComponent<CombatantCarry>();
            Assert.IsNotNull(carry, Wiring);
            return carry;
        }

        static LootItem FindAmmo(int from, AmmoType type)
        {
            for (int i = from; i < LootItem.All.Count; i++)
            {
                var item = LootItem.All[i];
                if (item.Kind.Value == (int)LootKind.Ammo && item.Ammo.Value == (int)type) return item;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator TheBag_HasSixtyPoints_AndAVestAddsToIt()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var carry = Bag(me);
            Assert.AreEqual(60f, carry.Capacity, 1e-3f);
            Assert.AreEqual(60, carry.CapacityPoints);
            Assert.LessOrEqual(carry.Used, carry.Capacity);
            var gear = me.GetComponent<CombatantGear>();
            if (gear == null) Assert.Ignore("no CombatantGear on the player");
            gear.ServerEquip(ArmorSlot.Vest, 2);
            Assert.AreEqual(80f, carry.Capacity, 1e-3f, "tier 2 vest: +20");
            gear.ServerEquip(ArmorSlot.Vest, 3);
            Assert.AreEqual(90f, carry.Capacity, 1e-3f);
            gear.ServerEquip(ArmorSlot.Vest, 0);
            Assert.AreEqual(60f, carry.Capacity, 1e-3f, "vest off");
            Assert.AreEqual("가방 용량 " + carry.UsedPoints + "/60", carry.Label);
        }

        [UnityTest]
        public IEnumerator WhatDoesNotFit_IsRefused_AmmoIsTakenAsFarAsItFits()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var carry = Bag(me);
            int freeTenths = carry.FreeTenths;
            int rounds = freeTenths / 2; // 9mm is 0.2 a round
            int reserveBefore = me.ReserveFor(AmmoType.Pistol9);

            var big = new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Pistol9, Amount = rounds + 40 };
            Assert.IsTrue(me.ServerGive(big), Wiring);
            Assert.AreEqual(rounds, big.Amount, "the hook cut the entry to what fits: 40 rounds were left over");
            Assert.AreEqual(reserveBefore + rounds, me.ReserveFor(AmmoType.Pistol9));
            Assert.LessOrEqual(carry.UsedTenths, carry.CapacityTenths);
            Assert.Less(carry.FreeTenths, 2, "the bag is full");

            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Pistol9, Amount = 10 }), "nothing more fits");
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }), "a medkit is 6 points");
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = HealRules.BandageLootKind, Amount = 1 }), "a bandage is a point");
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = ThrowableRules.SmokeLootKind, Amount = 1 }), "a smoke grenade is 3");
            // Weapons take no bag room.
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }));

            // Dropping makes room again (the bag counts what the combatant has).
            Assert.AreEqual(0, me.Medkits.Value);
            me.ServerStripLoadout();
            Assert.AreEqual(0, carry.UsedTenths);
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }));
            Assert.AreEqual(6f, carry.Used, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Ammo_OfAWeaponsCalibre_IsPickedUpByWalkingOverIt()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var carry = Bag(me);
            var director = MatchDirector.Instance;
            me.ServerStripLoadout();
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }));
            var used = GameData.Weapons.Find(3).Ammo;
            var unused = used == AmmoType.Pistol9 ? AmmoType.Rifle762 : AmmoType.Pistol9;
            int before = me.ReserveFor(used);
            int lootBefore = LootItem.All.Count;

            var spot = me.transform.position + me.transform.forward * 0.8f + Vector3.up * 0.3f;
            director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Ammo, Ammo = used, Amount = 30 }, spot);
            director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Ammo, Ammo = unused, Amount = 30 }, spot + Vector3.right * 0.3f);
            yield return WaitUntil(() => FindAmmo(lootBefore, unused) != null, 2f, "the loot spawned");
            Assert.IsNotNull(FindAmmo(lootBefore, used) ?? FindAmmo(0, used), "the pickup exists before the scan");

            yield return WaitUntil(() => me.ReserveFor(used) == before + 30, 3f, "the ammo was picked up by itself");
            Assert.GreaterOrEqual(carry.ServerAutoPickups, 1);
            Assert.IsNotNull(FindAmmo(lootBefore, unused), "no weapon uses that calibre: it stays");
            Assert.AreEqual(0, me.ReserveFor(unused));
        }

        [UnityTest]
        public IEnumerator DroppedAmmo_IsNotPickedUpAgainWhereItLies()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Bag(me);
            me.ServerStripLoadout();
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }));
            var type = GameData.Weapons.Find(3).Ammo;
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = type, Amount = 40 }));
            yield return new WaitForSeconds(0.6f); // a scan runs and sees the reserve
            me.CmdDropAmmo((int)type);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(0, me.ReserveFor(type), "it lies there, one metre away, and stays");
        }
    }
}

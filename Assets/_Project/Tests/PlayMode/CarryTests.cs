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
            Assert.AreEqual(85f, carry.Capacity, 1e-3f, "any vest: +25 (ADR-091)");
            gear.ServerEquip(ArmorSlot.Vest, 3);
            Assert.AreEqual(85f, carry.Capacity, 1e-3f);
            gear.ServerEquip(ArmorSlot.Vest, 0);
            Assert.AreEqual(60f, carry.Capacity, 1e-3f, "vest off");
            Assert.AreEqual("가방 용량 " + carry.UsedPoints + "/60", carry.Label);
        }

        // ADR-091: a backpack raises the capacity; F on a better one swaps it (the old one lands at the feet), never a
        // worse one; a broken vest keeps its bonus; the inventory's capacity line follows.
        [UnityTest]
        public IEnumerator Backpack_RaisesCapacity_SwapsOnlyForBetter_AndABrokenVestKeepsItsBonus()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var carry = Bag(me);
            var gear = me.GetComponent<CombatantGear>();
            var director = MatchDirector.Instance;
            Assert.AreEqual(0, carry.BackpackTier.Value);
            Assert.AreEqual(60f, carry.Capacity, 1e-3f);

            // A tier 2 backpack on the ground, picked up with F (the owner's request).
            int lootBefore = LootItem.All.Count;
            director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Backpack, Tier = 2, Amount = 1 }, me.transform.position + Vector3.right + Vector3.up * 0.3f);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 1, 2f, "a backpack on the ground");
            var pack2 = LootItem.All[lootBefore];
            yield return null; // the view follows the synced values in Update
            Assert.AreEqual((int)LootKind.Backpack, pack2.Kind.Value);
            Assert.AreEqual("가방 2단계", LootNames.Label(pack2));
            Assert.AreEqual("backpack_2", InventoryBackend.LootIconKey(pack2));
            Assert.IsNotNull(ItemIcons.Get("backpack_2"), "the icon exists");
            Assert.IsNotNull(pack2.GetComponent<LootView>().ShownMesh, "the ground shows the backpack model");
            me.CmdPickup(pack2.NetworkObject);
            yield return WaitUntil(() => carry.BackpackTier.Value == 2, 3f, "tier 2 backpack worn");
            yield return WaitUntil(() => !LootItem.All.Contains(pack2), 2f, "it left the ground");
            Assert.AreEqual(160f, carry.Capacity, 1e-3f, "60 + 100");
            StringAssert.Contains("/160", carry.Label);
            StringAssert.Contains("가방 2단계", carry.Label);
            Assert.AreEqual(carry.Label, InventoryBackend.CarryLabel(me), "the inventory shows the same line");
            var visual = me.GetComponent<BackpackVisual>();
            Assert.IsNotNull(visual, "CombatantView adds the worn backpack");
            yield return null;
            Assert.AreEqual(2, visual.ShownTier);
            Assert.IsTrue(visual.Renderer.enabled, "worn on the back");

            // A worse or equal one is refused and stays.
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = LootKind.Backpack, Tier = 1, Amount = 1 }), "never downgrades");
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = LootKind.Backpack, Tier = 2, Amount = 1 }), "same tier: no swap");
            Assert.AreEqual(2, carry.BackpackTier.Value);

            // Tier 3 on top of tier 2: swapped, the tier 2 lands at the feet.
            lootBefore = LootItem.All.Count;
            director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Backpack, Tier = 3, Amount = 1 }, me.transform.position + Vector3.right + Vector3.up * 0.3f);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 1, 2f, "a tier 3 backpack on the ground");
            var pack3 = LootItem.All[lootBefore];
            me.CmdPickup(pack3.NetworkObject);
            yield return WaitUntil(() => carry.BackpackTier.Value == 3, 3f, "tier 3 backpack worn");
            Assert.AreEqual(185f, carry.Capacity, 1e-3f, "60 + 125");
            yield return WaitUntil(() => LootItem.All.Exists(i => i != pack3 && i.Kind.Value == (int)LootKind.Backpack && i.Tier.Value == 2), 2f, "the old backpack dropped");

            // A vest adds 25 and keeps it when shot to pieces.
            if (gear == null) Assert.Ignore("no CombatantGear on the player");
            gear.ServerEquip(ArmorSlot.Vest, 1, 5f);
            Assert.AreEqual(210f, carry.Capacity, 1e-3f, "60 + 25 + 125");
            me.ServerTakeDamage(new DamageInfo { Amount = 40f, Zone = HitZone.Body, Cause = DamageCause.Shot });
            Assert.AreEqual(1, gear.VestTier.Value, "the broken vest stays on");
            Assert.AreEqual(0f, gear.VestDurability.Value, 1e-4f);
            Assert.AreEqual(210f, carry.Capacity, 1e-3f, "and keeps its bonus");
            float hp = me.Hp.Value;
            me.ServerTakeDamage(new DamageInfo { Amount = 20f, Zone = HitZone.Body, Cause = DamageCause.Shot });
            Assert.AreEqual(hp - 16f, me.Hp.Value, 1e-3f, "a broken vest still takes 20%");

            // A whole vest replaces the broken one on F, whatever its tier.
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Vest, Tier = 1 }));
            Assert.AreEqual(200f, gear.VestDurability.Value, 1e-3f);
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

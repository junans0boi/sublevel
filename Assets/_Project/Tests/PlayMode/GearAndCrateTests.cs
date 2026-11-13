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
    // Armour and death crates in the real TestBox scene as a host: a helmet takes its share of a head shot and
    // wears out, armour is picked up only when it is better, and the dead leave what they carried on the ground.
    // Needs CombatantGear and DeathCrate on the Player and TrainingDummy prefabs.
    public class GearAndCrateTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        const int Pistol = 1;
        const int Rifle = 3; // 40 per projectile, head x2.35 (weapons.json, ADR-089)

        ScriptedInput _input;

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
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
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

        static CombatantGear GearOf(Combatant combatant)
        {
            var gear = combatant.GetComponent<CombatantGear>();
            Assert.IsNotNull(gear, combatant.name + " has no CombatantGear: add it to the prefab in TestBoxBuilder");
            return gear;
        }

        IEnumerator GiveRifle()
        {
            Combatant.LocalPlayer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => Combatant.LocalPlayer.ActiveWeaponId == Rifle, 2f, "rifle in hand");
        }

        // The first dummy, armed but no longer shooting on its own, with the player standing 5 m in front of it.
        IEnumerator FaceAQuietDummy(Action<Combatant> found)
        {
            Combatant dummy = null;
            yield return WaitUntil(() =>
            {
                foreach (var c in Combatant.All)
                    if (!c.IsPlayer) { dummy = c; return true; }
                return false;
            }, 5f, "a training dummy in the test box");
            yield return WaitUntil(() => dummy.ActiveWeaponId != 0, 5f, "the dummy armed itself");
            foreach (var c in Combatant.All)
            {
                if (c.IsPlayer) continue;
                // By name: the test assembly does not reference the AI assembly.
                foreach (var part in c.GetComponents<MonoBehaviour>())
                    if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            }
            var pc = PlayerController.Local;
            pc.Teleport(dummy.transform.position + dummy.transform.forward * 5f + Vector3.up * 0.1f);
            pc.LookAt(dummy.transform.position + Vector3.up * 1.1f);
            yield return new WaitForSeconds(0.5f); // on the floor, standing still, any weapon ready again
            found(dummy);
        }

        // One server-side shot from the shooter's eyes at a point, with the spread cone closed.
        static bool ShootAt(Combatant shooter, Vector3 point)
        {
            float before = shooter.StanceSpreadFactor;
            shooter.StanceSpreadFactor = 0f;
            bool fired = shooter.ServerFire(point - shooter.EyePosition);
            shooter.StanceSpreadFactor = before;
            return fired;
        }

        // The first pickup of that kind among those that appeared from index `from` on.
        static LootItem FindLoot(int from, LootKind kind, int weaponId = 0, int ammo = -1)
        {
            for (int i = from; i < LootItem.All.Count; i++)
            {
                var item = LootItem.All[i];
                if (item.Kind.Value != (int)kind) continue;
                if (kind == LootKind.Weapon && item.WeaponId.Value != weaponId) continue;
                if (kind == LootKind.Ammo && item.Ammo.Value != ammo) continue;
                return item;
            }
            return null;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        [UnityTest]
        public IEnumerator HeadShot_WithATier2Helmet_DealsLessByTheHelmetsShare()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveRifle();
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);
            var gear = GearOf(dummy);
            Assert.AreEqual(0.40f, ArmorRules.Current.Find(ArmorSlot.Helmet, 2).Reduction, 1e-4f, "tier 2 helmet takes 40%");
            Assert.AreEqual(150f, ArmorRules.Current.Find(ArmorSlot.Helmet, 2).Durability, 1e-4f);
            Assert.IsFalse(gear.Has(ArmorSlot.Helmet));
            Assert.AreEqual(100f, dummy.Hp.Value);

            // A bare head would take the full 40 x 2.35 = 94 (DamagePipelineTests); one shot would leave 6, so the
            // helmet goes on first (ADR-089 made the rifle hit harder).
            Assert.AreEqual(94f, DamageRules.ForZone(GameData.Weapons.Find(Rifle), 5f, HitZone.Head), 1e-3f, "no helmet: the full 94");
            Assert.AreEqual(0f, gear.LastAbsorbed);

            gear.ServerEquip(ArmorSlot.Helmet, 2);
            Assert.AreEqual(2, gear.HelmetTier.Value);
            Assert.AreEqual(150f, gear.HelmetDurability.Value, 1e-3f);
            Assert.AreEqual(1f, gear.FractionOf(ArmorSlot.Helmet), 1e-4f);
            yield return new WaitForSeconds(0.3f);

            // The head shot on the helmet: 40% of 94 = 37.6 stays in the helmet, 56.4 gets through.
            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.65f), "head shot fired");
            Assert.AreEqual(43.6f, dummy.Hp.Value, 1e-3f, "100 - 56.4");
            Assert.AreEqual(37.6f, gear.LastAbsorbed, 1e-3f);
            Assert.AreEqual(112.4f, gear.HelmetDurability.Value, 1e-3f, "150 - 37.6");
            Assert.AreEqual(2, gear.TierOf(ArmorSlot.Helmet));

            // A helmet does nothing for the body or the legs.
            dummy.ServerTakeDamage(new DamageInfo { Amount = 5f, Zone = HitZone.Body, Cause = DamageCause.Shot, Attacker = me });
            Assert.AreEqual(38.6f, dummy.Hp.Value, 1e-3f);
            dummy.ServerTakeDamage(new DamageInfo { Amount = 5f, Zone = HitZone.Limb, Cause = DamageCause.Shot, Attacker = me });
            Assert.AreEqual(33.6f, dummy.Hp.Value, 1e-3f);
            Assert.AreEqual(112.4f, gear.HelmetDurability.Value, 1e-3f, "untouched");
        }

        [UnityTest]
        public IEnumerator Armour_WearsOutAndIsGone_AndOnlyStopsShotsAndBlastsOnItsZone()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = GearOf(me);

            // A tier 1 helmet (30%) with 5 points left: of a 40-point hit it can only take 5.
            gear.ServerEquip(ArmorSlot.Helmet, 1, 5f);
            Assert.AreEqual(1, gear.HelmetTier.Value);
            Assert.AreEqual(5f, gear.HelmetDurability.Value, 1e-4f);
            me.ServerTakeDamage(new DamageInfo { Amount = 40f, Zone = HitZone.Head, Cause = DamageCause.Shot });
            Assert.AreEqual(65f, me.Hp.Value, 1e-3f, "35 got through");
            Assert.AreEqual(0, gear.HelmetTier.Value, "durability 0: the helmet is gone");
            Assert.AreEqual(0f, gear.HelmetDurability.Value);
            Assert.IsFalse(gear.Has(ArmorSlot.Helmet));
            Assert.AreEqual(1, gear.PiecesBroken);
            Assert.AreEqual(LootNames.None, LootNames.Worn(gear, ArmorSlot.Helmet));

            me.ServerTakeDamage(new DamageInfo { Amount = 20f, Zone = HitZone.Head, Cause = DamageCause.Shot });
            Assert.AreEqual(45f, me.Hp.Value, 1e-3f, "bare head again: the full 20");

            // A tier 3 vest (55%, 250): not for falls, the zone or the legs; yes for a blast on the body.
            gear.ServerEquip(ArmorSlot.Vest, 3);
            Assert.AreEqual("3단계  250 / 250", LootNames.Worn(gear, ArmorSlot.Vest));
            me.ServerTakeDamage(new DamageInfo { Amount = 10f, Zone = HitZone.Body, Cause = DamageCause.Fall });
            Assert.AreEqual(35f, me.Hp.Value, 1e-3f);
            me.ServerTakeDamage(new DamageInfo { Amount = 5f, Zone = HitZone.Body, Cause = DamageCause.Zone });
            Assert.AreEqual(30f, me.Hp.Value, 1e-3f);
            me.ServerTakeDamage(new DamageInfo { Amount = 5f, Zone = HitZone.Limb, Cause = DamageCause.Shot });
            Assert.AreEqual(25f, me.Hp.Value, 1e-3f);
            Assert.AreEqual(250f, gear.VestDurability.Value, 1e-3f, "none of that touched the vest");

            me.ServerTakeDamage(new DamageInfo { Amount = 20f, Zone = HitZone.Body, Cause = DamageCause.Blast });
            Assert.AreEqual(16f, me.Hp.Value, 1e-3f, "55% of 20 = 11 absorbed, 9 through");
            Assert.AreEqual(239f, gear.VestDurability.Value, 1e-3f);
            Assert.AreEqual(3, gear.VestTier.Value);
            yield return null;
            Assert.IsFalse(me.Dead.Value);
        }

        [UnityTest]
        public IEnumerator KillingADummy_LeavesWhatItCarriedAroundTheBody_AndItsLoadoutEmpty()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            Combatant dummy = null;
            yield return FaceAQuietDummy(d => dummy = d);
            var gear = GearOf(dummy);
            var crate = dummy.GetComponent<DeathCrate>();
            Assert.IsNotNull(crate, "the dummy has no DeathCrate: add it to the prefab in TestBoxBuilder");

            // On top of the pistol and light ammo it armed itself with.
            Assert.IsTrue(dummy.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 }));
            Assert.IsTrue(dummy.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 60 }));
            Assert.IsTrue(dummy.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 2 }));
            gear.ServerEquip(ArmorSlot.Helmet, 1);
            gear.ServerEquip(ArmorSlot.Vest, 2, 100f);
            Assert.AreEqual(Pistol, dummy.Slot2.Value, "the pistol sits in the sidearm slot");
            Assert.AreEqual(Rifle, dummy.Slot0.Value, "the rifle in the first primary slot");
            int lightRounds = dummy.ReservePistol9.Value;
            Assert.Greater(lightRounds, Combatant.DroppedReserveCap, "the dummy carries far more than it will drop");
            Assert.IsFalse(crate.ServerOpened);
            Assert.IsFalse(crate.MarkerVisible);

            int lootBefore = LootItem.All.Count;
            // The zone kills whatever the armour: the vest keeps its 100.
            dummy.ServerTakeDamage(new DamageInfo { Amount = 500f, Cause = DamageCause.Zone });
            Assert.IsTrue(dummy.Dead.Value);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 7, 3f, "two weapons, two ammo stacks, medkits, helmet and vest on the ground");
            Assert.IsTrue(crate.ServerOpened);
            Assert.AreEqual(7, crate.ServerItemCount);

            var rifle = FindLoot(lootBefore, LootKind.Weapon, Rifle);
            var pistol = FindLoot(lootBefore, LootKind.Weapon, Pistol);
            var medium = FindLoot(lootBefore, LootKind.Ammo, 0, (int)AmmoType.Rifle556);
            var light = FindLoot(lootBefore, LootKind.Ammo, 0, (int)AmmoType.Pistol9);
            var medkits = FindLoot(lootBefore, LootKind.Medkit);
            var helmet = FindLoot(lootBefore, LootKind.Helmet);
            var vest = FindLoot(lootBefore, LootKind.Vest);
            Assert.IsNotNull(rifle, "the rifle");
            Assert.IsNotNull(pistol, "the pistol");
            Assert.IsNotNull(medium, "medium ammo");
            Assert.IsNotNull(light, "light ammo");
            Assert.IsNotNull(medkits, "medkits");
            Assert.IsNotNull(helmet, "the helmet");
            Assert.IsNotNull(vest, "the vest");
            // ADR-081: guns land unloaded; their rounds join the calibre's (capped) ammo stack.
            Assert.AreEqual(0, rifle.Amount.Value, "unloaded");
            Assert.AreEqual(0, pistol.Amount.Value, "unloaded");
            Assert.AreEqual(60, medium.Amount.Value);
            Assert.AreEqual(60, light.Amount.Value, "a dummy leaves at most the cap per calibre");
            Assert.AreEqual(2, medkits.Amount.Value, "both kits in one pickup");
            Assert.AreEqual(1, helmet.Tier.Value);
            Assert.AreEqual(80f, helmet.Durability.Value, 1e-3f);
            Assert.AreEqual(2, vest.Tier.Value);
            Assert.AreEqual(100f, vest.Durability.Value, 1e-3f, "armour keeps the durability it had left");
            Assert.AreEqual("조끼 2단계", LootNames.Of(vest));
            Assert.AreEqual("헬멧 1단계 (내구도 80)", LootNames.Label(helmet));

            float ring = CrateRules.Current.RingRadius;
            for (int i = lootBefore; i < LootItem.All.Count; i++)
            {
                var p = LootItem.All[i].transform.position;
                float away = PlanarDistance(p, dummy.transform.position);
                Assert.LessOrEqual(away, ring + 0.05f, "item " + i + " lies near the body");
                Assert.Greater(away, 0.3f, "item " + i + " is not inside the body");
                Assert.AreEqual(dummy.transform.position.y + CrateRules.Current.LiftMeters, p.y, 0.1f, "item " + i + " lies just over the floor");
                for (int j = i + 1; j < LootItem.All.Count; j++)
                    Assert.Greater(Vector3.Distance(p, LootItem.All[j].transform.position), 0.3f, "items " + i + " and " + j + " do not overlap");
            }

            // Nothing is duplicated: the dead dummy holds nothing any more.
            for (int slot = 0; slot < Combatant.SlotCount; slot++)
            {
                Assert.AreEqual(0, dummy.WeaponInSlot(slot), "slot " + slot);
                Assert.AreEqual(0, dummy.MagInSlot(slot), "magazine " + slot);
            }
            Assert.AreEqual(0, dummy.ActiveWeaponId);
            Assert.AreEqual(0, dummy.ReservePistol9.Value);
            Assert.AreEqual(0, dummy.ReserveRifle556.Value);
            Assert.AreEqual(0, dummy.ReserveShotgun12.Value);
            Assert.AreEqual(0, dummy.ReserveRifle762.Value);
            Assert.AreEqual(0, dummy.Medkits.Value);
            Assert.IsFalse(dummy.Reloading.Value);
            Assert.IsFalse(gear.Has(ArmorSlot.Helmet));
            Assert.IsFalse(gear.Has(ArmorSlot.Vest));

            yield return null;
            yield return null;
            Assert.IsTrue(crate.MarkerVisible, "a marker box stands over the body");
            // The loadout really is empty: nothing brings the old values back, and the dead die only once.
            dummy.ServerTakeDamage(50);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0, dummy.Slot1.Value);
            Assert.AreEqual(0, dummy.Slot2.Value);
            Assert.AreEqual(lootBefore + 7, LootItem.All.Count);

            // The winner takes the rifle and its ammo like any other loot.
            Assert.AreEqual(0, me.ActiveWeaponId, "the player came unarmed");
            pc.Teleport(new Vector3(rifle.transform.position.x, dummy.transform.position.y + 0.1f, rifle.transform.position.z));
            yield return null;
            me.CmdPickup(rifle.NetworkObject);
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 3f, "rifle picked up from the crate");
            Assert.AreEqual(0, me.ActiveMag, "unloaded (ADR-081)");
            me.CmdPickup(medium.NetworkObject);
            yield return WaitUntil(() => me.ReserveRifle556.Value == 60, 3f, "its ammo picked up");
            me.CmdPickup(vest.NetworkObject);
            yield return WaitUntil(() => GearOf(me).VestTier.Value == 2, 3f, "the vest picked up");
            Assert.AreEqual(100f, GearOf(me).VestDurability.Value, 1e-3f);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 4, 3f, "the three pickups left the ground");
        }

        [UnityTest]
        public IEnumerator PickingUpArmour_SwapsForBetter_AndRefusesWorse()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = GearOf(me);
            var director = MatchDirector.Instance;
            var spot = me.transform.position + Vector3.right * 1f + Vector3.up * 0.3f;

            gear.ServerEquip(ArmorSlot.Vest, 3);
            int lootBefore = LootItem.All.Count;
            director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Vest, Tier = 1 }, spot);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 1, 2f, "a tier 1 vest on the ground");
            var worse = FindLoot(lootBefore, LootKind.Vest);
            Assert.IsNotNull(worse);
            Assert.AreEqual(1, worse.Tier.Value);
            Assert.AreEqual(200f, worse.Durability.Value, 1e-3f, "a table row is a fresh piece");
            Assert.AreEqual("조끼 1단계", LootNames.Of(worse));
            Assert.AreEqual("내구도 200", LootNames.Amount(worse));
            Assert.AreEqual("조끼 1단계 (내구도 200)", worse.GetComponent<LootView>().Label);

            // Worse than what is worn: refused, and it stays on the ground.
            me.CmdPickup(worse.NetworkObject);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(LootItem.All.Contains(worse), "the refused vest is still there");
            Assert.IsTrue(worse.IsSpawned);
            Assert.AreEqual(3, gear.VestTier.Value);
            Assert.AreEqual(250f, gear.VestDurability.Value, 1e-3f);
            Assert.IsFalse(me.ServerGive(worse.ToEntry()), "the server call says so too");
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = LootKind.Vest, Tier = 3, Durability = 100f }), "same tier in worse shape");
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = LootKind.Vest, Tier = 7 }), "no such tier");
            Assert.AreEqual(lootBefore + 1, LootItem.All.Count, "refusals drop nothing");

            // Same tier, but the worn one is nearly spent: swapped, and the old one lands on the ground.
            gear.ServerEquip(ArmorSlot.Vest, 1, 50f);
            me.CmdPickup(worse.NetworkObject);
            yield return WaitUntil(() => !LootItem.All.Contains(worse), 3f, "the fresh vest picked up");
            Assert.AreEqual(1, gear.VestTier.Value);
            Assert.AreEqual(200f, gear.VestDurability.Value, 1e-3f);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 1, 2f, "the old vest dropped");
            var old = FindLoot(lootBefore, LootKind.Vest);
            Assert.IsNotNull(old);
            Assert.AreEqual(1, old.Tier.Value);
            Assert.AreEqual(50f, old.Durability.Value, 1e-3f, "with the durability it had left");
            Assert.Less(Vector3.Distance(old.transform.position, me.transform.position), Combatant.PickupRange, "within reach");

            // A higher tier always wins; nothing worn means nothing dropped.
            Assert.IsFalse(gear.Has(ArmorSlot.Helmet));
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Helmet, Tier = 2 }));
            Assert.AreEqual(2, gear.HelmetTier.Value);
            Assert.AreEqual(150f, gear.HelmetDurability.Value, 1e-3f);
            Assert.AreEqual("2단계  150 / 150", LootNames.Worn(gear, ArmorSlot.Helmet));
            Assert.AreEqual(lootBefore + 1, LootItem.All.Count);
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Helmet, Tier = 3, Durability = 10f }), "tier before durability");
            Assert.AreEqual(3, gear.HelmetTier.Value);
            Assert.AreEqual(10f, gear.HelmetDurability.Value, 1e-3f);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 2, 2f, "the tier 2 helmet dropped");
            var dropped = FindLoot(lootBefore, LootKind.Helmet);
            Assert.AreEqual(2, dropped.Tier.Value);
            Assert.AreEqual(150f, dropped.Durability.Value, 1e-3f);

            // The loadout still takes its own kinds with the gear component present.
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }));
            Assert.AreEqual(1, me.Medkits.Value);
            Assert.IsFalse(me.ServerGive(new LootEntry { Kind = (LootKind)99, Amount = 1 }), "kinds nobody knows are still refused");
        }
    }
}

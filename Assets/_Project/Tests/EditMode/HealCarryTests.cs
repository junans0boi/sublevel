using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // PUBG_GAP 4-3 batch H: timed healing items and the carry capacity, as plain rules.
    public class HealCarryTests
    {
        static readonly HealRules Rules = new HealRules();
        static readonly CarryRules Bag = new CarryRules();

        // ---- Healing ----

        [Test]
        public void Defaults_AreTheAgreedNumbers()
        {
            Assert.AreEqual(3f, Rules.Bandage.Seconds, 1e-4f);
            Assert.AreEqual(12f, Rules.Bandage.Amount, 1e-4f);
            Assert.AreEqual(75f, Rules.Bandage.MaxHp, 1e-4f);
            Assert.AreEqual(6f, Rules.FirstAid.Seconds, 1e-4f);
            Assert.AreEqual(40f, Rules.FirstAid.Amount, 1e-4f);
            Assert.AreEqual(75f, Rules.FirstAid.MaxHp, 1e-4f);
            Assert.AreEqual(8f, Rules.Medkit.Seconds, 1e-4f);
            Assert.AreEqual(100f, Rules.Medkit.MaxHp, 1e-4f);
            Assert.AreEqual(13, (int)HealRules.BandageLootKind, "the value the loot enum gets for bandages");
            Assert.AreEqual(14, (int)HealRules.FirstAidLootKind, "... and for first-aid kits");
            Assert.AreEqual(15, (int)ThrowableRules.SmokeLootKind, "... and for smoke grenades");
        }

        [Test]
        public void Amount_StopsAtTheItemsCeiling()
        {
            Assert.AreEqual(12f, Healing.Amount(HealKind.Bandage, 50f, Rules), 1e-4f);
            Assert.AreEqual(5f, Healing.Amount(HealKind.Bandage, 70f, Rules), 1e-4f, "only up to 75");
            Assert.AreEqual(0f, Healing.Amount(HealKind.Bandage, 75f, Rules), 1e-4f);
            Assert.AreEqual(0f, Healing.Amount(HealKind.Bandage, 90f, Rules), 1e-4f, "never takes health away");
            Assert.AreEqual(40f, Healing.Amount(HealKind.FirstAid, 30f, Rules), 1e-4f);
            Assert.AreEqual(25f, Healing.Amount(HealKind.FirstAid, 50f, Rules), 1e-4f, "50 + 25 = 75");
            Assert.AreEqual(100f - 12f, Healing.Amount(HealKind.Medkit, 12f, Rules), 1e-4f, "the medkit fills up");
            Assert.AreEqual(1f, Healing.Amount(HealKind.Medkit, 99f, Rules), 1e-4f);
            Assert.AreEqual(0f, Healing.Amount(HealKind.None, 10f, Rules), 1e-4f);
            Assert.AreEqual(0f, Healing.Amount(HealKind.Medkit, 0f, Rules), 1e-4f, "the dead stay dead");
        }

        [Test]
        public void CanUse_NeedsRoomBelowTheCeiling()
        {
            Assert.IsTrue(Healing.CanUse(HealKind.Bandage, 74f, Rules));
            Assert.IsFalse(Healing.CanUse(HealKind.Bandage, 75f, Rules));
            Assert.IsFalse(Healing.CanUse(HealKind.FirstAid, 80f, Rules));
            Assert.IsTrue(Healing.CanUse(HealKind.Medkit, 80f, Rules));
            Assert.IsFalse(Healing.CanUse(HealKind.Medkit, 100f, Rules));
            Assert.IsFalse(Healing.CanUse(HealKind.Medkit, 0f, Rules));
            Assert.IsFalse(Healing.CanUse(HealKind.None, 10f, Rules));
        }

        [Test]
        public void BestFit_PicksTheSmallestItemThatHelps()
        {
            Assert.AreEqual(HealKind.Bandage, Healing.BestFit(50f, 2, 1, 1, Rules));
            Assert.AreEqual(HealKind.FirstAid, Healing.BestFit(50f, 0, 1, 1, Rules));
            Assert.AreEqual(HealKind.Medkit, Healing.BestFit(50f, 0, 0, 1, Rules));
            Assert.AreEqual(HealKind.Medkit, Healing.BestFit(80f, 3, 3, 1, Rules), "above 75 only the medkit helps");
            Assert.AreEqual(HealKind.None, Healing.BestFit(80f, 3, 3, 0, Rules));
            Assert.AreEqual(HealKind.None, Healing.BestFit(100f, 3, 3, 3, Rules), "full health");
            Assert.AreEqual(HealKind.None, Healing.BestFit(50f, 0, 0, 0, Rules));
        }

        [Test]
        public void Progress_RunsFromZeroToOne()
        {
            Assert.AreEqual(0f, Healing.Progress01(10.0, 10.0, 3f), 1e-4f);
            Assert.AreEqual(0.5f, Healing.Progress01(11.5, 10.0, 3f), 1e-4f);
            Assert.AreEqual(1f, Healing.Progress01(13.0, 10.0, 3f), 1e-4f);
            Assert.AreEqual(1f, Healing.Progress01(99.0, 10.0, 3f), 1e-4f);
            Assert.AreEqual(0f, Healing.Progress01(5.0, 10.0, 3f), 1e-4f, "before the start");
            Assert.AreEqual(1f, Healing.Progress01(10.0, 10.0, 0f), 1e-4f, "no duration: done");
        }

        [Test]
        public void KindOfLoot_MapsThePickups()
        {
            Assert.AreEqual(HealKind.Bandage, Healing.KindOfLoot(HealRules.BandageLootKind));
            Assert.AreEqual(HealKind.FirstAid, Healing.KindOfLoot(HealRules.FirstAidLootKind));
            Assert.AreEqual(HealKind.Medkit, Healing.KindOfLoot(LootKind.Medkit));
            Assert.AreEqual(HealKind.None, Healing.KindOfLoot(LootKind.Ammo));
            Assert.AreEqual("붕대", Healing.NameOf(HealKind.Bandage));
            Assert.AreEqual("응급키트", Healing.NameOf(HealKind.FirstAid));
            Assert.AreEqual("구급상자", Healing.NameOf(HealKind.Medkit));
        }

        // ---- Carry ----

        [Test]
        public void Capacity_GrowsWithTheVest()
        {
            Assert.AreEqual(600, Carry.CapacityTenths(Bag, 0));
            Assert.AreEqual(700, Carry.CapacityTenths(Bag, 1));
            Assert.AreEqual(800, Carry.CapacityTenths(Bag, 2));
            Assert.AreEqual(900, Carry.CapacityTenths(Bag, 3));
            Assert.AreEqual(600, Carry.CapacityTenths(Bag, 7), "no such tier: no bonus");
        }

        [Test]
        public void Used_AddsUpEveryItem()
        {
            // 30 x 9mm (6) + 60 x 5.56 (18) + 10 x 12ga (8) + 20 x 7.62 (10) + 2 bandages (2) + 1 first aid (3) + 1 medkit (6) + 1 grenade (3) + 1 smoke (3)
            int used = Carry.UsedTenths(Bag, 30, 60, 10, 20, 2, 1, 1, 1, 1);
            Assert.AreEqual(590, used);
            Assert.AreEqual(59, Carry.Points(used));
            Assert.AreEqual(0, Carry.UsedTenths(Bag, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            Assert.AreEqual(1, Carry.Points(1), "a started point counts");
            Assert.AreEqual(0, Carry.Points(0));
        }

        [Test]
        public void Units_FollowTheTable()
        {
            Assert.AreEqual(2, Carry.UnitTenths(LootKind.Ammo, AmmoType.Pistol9, Bag));
            Assert.AreEqual(3, Carry.UnitTenths(LootKind.Ammo, AmmoType.Rifle556, Bag));
            Assert.AreEqual(8, Carry.UnitTenths(LootKind.Ammo, AmmoType.Shotgun12, Bag));
            Assert.AreEqual(5, Carry.UnitTenths(LootKind.Ammo, AmmoType.Rifle762, Bag));
            Assert.AreEqual(10, Carry.UnitTenths(HealRules.BandageLootKind, AmmoType.Pistol9, Bag));
            Assert.AreEqual(30, Carry.UnitTenths(HealRules.FirstAidLootKind, AmmoType.Pistol9, Bag));
            Assert.AreEqual(60, Carry.UnitTenths(LootKind.Medkit, AmmoType.Pistol9, Bag));
            Assert.AreEqual(30, Carry.UnitTenths(ThrowableRules.GrenadeLootKind, AmmoType.Pistol9, Bag));
            Assert.AreEqual(30, Carry.UnitTenths(ThrowableRules.SmokeLootKind, AmmoType.Pistol9, Bag));
            Assert.AreEqual(0, Carry.UnitTenths(LootKind.Weapon, AmmoType.Pistol9, Bag), "weapons take no bag room");
            Assert.AreEqual(0, Carry.UnitTenths(LootKind.Vest, AmmoType.Pistol9, Bag), "nor does worn armour");
        }

        [Test]
        public void Fit_TakesAsManyAsFit()
        {
            Assert.AreEqual(30, Carry.Fit(600, 2, 30), "all of it fits");
            Assert.AreEqual(5, Carry.Fit(10, 2, 30), "10 tenths = 5 rounds of 0.2");
            Assert.AreEqual(1, Carry.Fit(5, 3, 3), "a whole item or none");
            Assert.AreEqual(0, Carry.Fit(2, 3, 3));
            Assert.AreEqual(0, Carry.Fit(0, 2, 30), "a full bag");
            Assert.AreEqual(0, Carry.Fit(-4, 2, 30), "an overfull bag");
            Assert.AreEqual(7, Carry.Fit(0, 0, 7), "no size: always fits");
            Assert.AreEqual(0, Carry.Fit(600, 2, 0), "nothing wanted");
        }

        [Test]
        public void ARoundOfEachKind_FillsTheBagExactly()
        {
            // 300 rounds of 9mm are exactly the 60 points.
            int used = Carry.UsedTenths(Bag, 300, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.AreEqual(Carry.CapacityTenths(Bag, 0), used);
            Assert.AreEqual(0, Carry.Fit(Carry.CapacityTenths(Bag, 0) - used, Carry.AmmoUnitTenths(AmmoType.Pistol9, Bag), 30));
        }
    }
}

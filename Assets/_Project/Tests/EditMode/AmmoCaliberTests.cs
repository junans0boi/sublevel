using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The four calibres of ADR-054: names, per-calibre reserves, which reserve a reload draws on, and the data.
    public class AmmoCaliberTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";

        static WeaponTable Table() => new WeaponTable
        {
            Weapons = new[]
            {
                new WeaponDef { Id = 1, Name = "P", Ammo = AmmoType.Pistol9, MagSize = 10, FireInterval = 0.1f, ReloadTime = 1f },
                new WeaponDef { Id = 2, Name = "R", Ammo = AmmoType.Rifle556, MagSize = 10, FireInterval = 0.1f, ReloadTime = 1f },
                new WeaponDef { Id = 3, Name = "S", Ammo = AmmoType.Shotgun12, MagSize = 4, FireInterval = 0.1f, ReloadTime = 1f },
                new WeaponDef { Id = 4, Name = "H", Ammo = AmmoType.Rifle762, MagSize = 5, FireInterval = 0.1f, ReloadTime = 1f },
            }
        };

        [Test]
        public void AmmoNames_MapEveryCaliber()
        {
            Assert.AreEqual(4, AmmoNames.Count);
            Assert.AreEqual("9mm", AmmoNames.Of(AmmoType.Pistol9));
            Assert.AreEqual("5.56mm", AmmoNames.Of(AmmoType.Rifle556));
            Assert.AreEqual("12게이지", AmmoNames.Of(AmmoType.Shotgun12));
            Assert.AreEqual("7.62mm", AmmoNames.Of(AmmoType.Rifle762));
            Assert.AreEqual("탄약", AmmoNames.Of(9), "unknown index");
            Assert.AreEqual(0, (int)AmmoType.Pistol9);
            Assert.AreEqual(3, (int)AmmoType.Rifle762);
            Assert.IsTrue(AmmoNames.IsValid(AmmoType.Rifle762));
            Assert.IsFalse(AmmoNames.IsValid((AmmoType)4));
        }

        [Test]
        public void Reserves_AreKeptPerCaliber()
        {
            var l = new Loadout(Table());
            l.AddAmmo(AmmoType.Pistol9, 10);
            l.AddAmmo(AmmoType.Rifle556, 20);
            l.AddAmmo(AmmoType.Shotgun12, 30);
            l.AddAmmo(AmmoType.Rifle762, 40);
            Assert.AreEqual(10, l.Reserve(AmmoType.Pistol9));
            Assert.AreEqual(20, l.Reserve(AmmoType.Rifle556));
            Assert.AreEqual(30, l.Reserve(AmmoType.Shotgun12));
            Assert.AreEqual(40, l.Reserve(AmmoType.Rifle762));
            Assert.AreEqual(0, l.Reserve((AmmoType)4), "no such caliber");
            l.AddAmmo((AmmoType)4, 5);
            Assert.AreEqual(40, l.RemoveAmmo(AmmoType.Rifle762));
            Assert.AreEqual(10, l.Reserve(AmmoType.Pistol9), "others untouched");
        }

        [Test]
        public void Reload_ConsumesOnlyTheWeaponsCaliber()
        {
            var l = new Loadout(Table());
            l.AddWeapon(2, 4);   // rifle, 4 of 10 in the magazine
            l.AddAmmo(AmmoType.Pistol9, 50);
            Assert.IsFalse(l.TryStartReload(0.0), "only the wrong caliber in reserve");
            l.AddAmmo(AmmoType.Rifle556, 8);
            Assert.IsTrue(l.TryStartReload(0.0));
            Assert.IsTrue(l.Tick(5.0));
            Assert.AreEqual(10, l.MagInSlot(0), "6 needed, 8 held");
            Assert.AreEqual(2, l.Reserve(AmmoType.Rifle556));
            Assert.AreEqual(50, l.Reserve(AmmoType.Pistol9));
        }

        [Test]
        public void Reload_FourthCaliberWorks()
        {
            var l = new Loadout(Table());
            l.AddWeapon(4, 0);
            l.AddAmmo(AmmoType.Rifle762, 3);
            Assert.IsTrue(l.TryStartReload(0.0));
            l.Tick(5.0);
            Assert.AreEqual(3, l.MagInSlot(0));
            Assert.AreEqual(0, l.Reserve(AmmoType.Rifle762));
        }

        [Test]
        public void SwapSlots_KeepsTheWeaponInHandInHand()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 7);
            l.AddWeapon(2, 9);
            Assert.AreEqual(0, l.ActiveSlot);
            Assert.IsTrue(l.SwapSlots());
            Assert.AreEqual(2, l.WeaponInSlot(0));
            Assert.AreEqual(9, l.MagInSlot(0));
            Assert.AreEqual(1, l.WeaponInSlot(1));
            Assert.AreEqual(7, l.MagInSlot(1));
            Assert.AreEqual(1, l.ActiveSlot, "the pistol is still the weapon in hand");
            Assert.AreEqual(1, l.ActiveWeapon.Id);
            Assert.IsFalse(new Loadout(Table()).SwapSlots(), "nothing to swap");
        }

        [Test]
        public void AddWeaponToSlot_ReturnsTheReplacedWeapon()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 7);
            l.AddWeapon(2, 9);
            Assert.AreEqual(2, l.AddWeaponToSlot(1, 3, 4, out int mag));
            Assert.AreEqual(9, mag);
            Assert.AreEqual(3, l.WeaponInSlot(1));
            Assert.AreEqual(1, l.WeaponInSlot(0), "the other slot is untouched");
            Assert.AreEqual(0, l.AddWeaponToSlot(5, 3, 4, out _), "no such slot");
        }

        [Test]
        public void WeaponData_EachAmmoIsAValidCaliber_AndMatchesAdr054()
        {
            var table = JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));
            Assert.AreEqual(4, table.Weapons.Length);
            foreach (var weapon in table.Weapons) Assert.IsTrue(AmmoNames.IsValid(weapon.Ammo), weapon.Name);
            Assert.AreEqual(AmmoType.Pistol9, table.Find(1).Ammo, "VK-9");
            Assert.AreEqual(AmmoType.Pistol9, table.Find(2).Ammo, "Ordo");
            Assert.AreEqual(AmmoType.Rifle556, table.Find(3).Ammo, "Warden");
            Assert.AreEqual(AmmoType.Shotgun12, table.Find(4).Ammo, "Breacher");
        }

        [Test]
        public void LootData_AmmoRowsUseValidCalibers_AndEveryCaliberHasARow()
        {
            var loot = JsonUtility.FromJson<LootTable>(File.ReadAllText(DataFolder + "loot.json"));
            var seen = new bool[AmmoNames.Count];
            foreach (var entry in loot.Entries)
            {
                if (entry.Kind != LootKind.Ammo) continue;
                Assert.IsTrue(AmmoNames.IsValid(entry.Ammo));
                seen[(int)entry.Ammo] = true;
                Assert.Greater(entry.Amount, 0);
            }
            for (int i = 0; i < seen.Length; i++) Assert.IsTrue(seen[i], "a loot row for " + AmmoNames.Of(i));
        }

        [Test]
        public void GrenadeLootKind_MatchesTheThrowableConstant()
        {
            Assert.AreEqual(10, (int)LootKind.Grenade);
            Assert.AreEqual(LootKind.Grenade, ThrowableRules.GrenadeLootKind);
        }
    }
}

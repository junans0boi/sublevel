using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Dropping things out of a loadout (the inventory screen's DROP buttons).
    public class LoadoutDropTests
    {
        static WeaponTable Table() => new WeaponTable
        {
            Weapons = new[]
            {
                new WeaponDef { Id = 1, Name = "P", Ammo = AmmoType.Pistol9, Damage = 20, FireInterval = 0.5f, MagSize = 3, ReloadTime = 1f },
                new WeaponDef { Id = 2, Name = "S", Ammo = AmmoType.Shotgun12, Damage = 10, FireInterval = 1f, MagSize = 2, ReloadTime = 2f },
            }
        };

        [Test]
        public void DroppingTheActiveWeapon_ReturnsItWithItsMagazine_AndSwitchesToTheOtherSlot()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 3);
            l.AddWeapon(2, 1);
            Assert.AreEqual(0, l.ActiveSlot);

            Assert.AreEqual(1, l.RemoveWeapon(0, out int mag));
            Assert.AreEqual(3, mag);
            Assert.AreEqual(0, l.WeaponInSlot(0));
            Assert.AreEqual(0, l.MagInSlot(0));
            Assert.AreEqual(1, l.ActiveSlot, "the other weapon comes to hand");
            Assert.AreEqual(2, l.ActiveWeapon.Id);
        }

        [Test]
        public void DroppingTheOtherWeapon_KeepsTheActiveSlot()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 3);
            l.AddWeapon(2, 1);

            Assert.AreEqual(2, l.RemoveWeapon(1, out int mag));
            Assert.AreEqual(1, mag);
            Assert.AreEqual(0, l.ActiveSlot);
            Assert.AreEqual(1, l.ActiveWeapon.Id);
        }

        [Test]
        public void DroppingTheOnlyWeapon_LeavesEmptyHands()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 3);

            Assert.AreEqual(1, l.RemoveWeapon(0, out _));
            Assert.AreEqual(0, l.ActiveSlot);
            Assert.IsNull(l.ActiveWeapon);
        }

        [Test]
        public void DroppingTheActiveWeapon_CancelsItsReload()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 0);
            l.AddAmmo(AmmoType.Pistol9, 3);
            Assert.IsTrue(l.TryStartReload(0));

            l.RemoveWeapon(0, out _);
            Assert.IsFalse(l.IsReloading);
            Assert.IsFalse(l.Tick(5), "nothing left to finish");
            Assert.AreEqual(3, l.Reserve(AmmoType.Pistol9), "the reserve is untouched");
        }

        [Test]
        public void DroppingAWeapon_FromAnEmptyOrMissingSlot_ReturnsZero()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 3);

            Assert.AreEqual(0, l.RemoveWeapon(1, out int mag), "empty slot");
            Assert.AreEqual(0, mag);
            Assert.AreEqual(0, l.RemoveWeapon(-1, out _), "below range");
            Assert.AreEqual(0, l.RemoveWeapon(2, out _), "an empty sidearm slot");
            Assert.AreEqual(0, l.RemoveWeapon(5, out _), "above range");
            Assert.AreEqual(1, l.WeaponInSlot(0), "nothing else changed");
        }

        [Test]
        public void SplittingAmmo_TakesUpToTheAmount()
        {
            var l = new Loadout(Table());
            l.AddAmmo(AmmoType.Pistol9, 45);
            Assert.AreEqual(22, l.RemoveAmmo(AmmoType.Pistol9, 45 / 2));
            Assert.AreEqual(23, l.Reserve(AmmoType.Pistol9));
            Assert.AreEqual(23, l.RemoveAmmo(AmmoType.Pistol9, 99), "never more than is there");
            Assert.AreEqual(0, l.RemoveAmmo(AmmoType.Pistol9, 5));
            Assert.AreEqual(0, l.RemoveAmmo(AmmoType.Pistol9, -1));
        }

        [Test]
        public void DroppingAmmo_ReturnsTheWholeReserve_AndZeroesIt()
        {
            var l = new Loadout(Table());
            l.AddAmmo(AmmoType.Pistol9, 45);
            l.AddAmmo(AmmoType.Shotgun12, 8);

            Assert.AreEqual(45, l.RemoveAmmo(AmmoType.Pistol9));
            Assert.AreEqual(0, l.Reserve(AmmoType.Pistol9));
            Assert.AreEqual(8, l.Reserve(AmmoType.Shotgun12), "other types are untouched");
            Assert.AreEqual(0, l.RemoveAmmo(AmmoType.Pistol9), "nothing left");
            Assert.AreEqual(0, l.RemoveAmmo(AmmoType.Rifle556), "never had any");
            Assert.AreEqual(0, l.RemoveAmmo((AmmoType)7), "no such type");
        }

        [Test]
        public void DroppingAMedkit_TakesOne_AndFailsWhenNoneLeft()
        {
            var l = new Loadout(Table());
            Assert.IsFalse(l.RemoveMedkit());
            l.AddMedkits(2);

            Assert.IsTrue(l.RemoveMedkit());
            Assert.AreEqual(1, l.Medkits);
            Assert.IsTrue(l.RemoveMedkit());
            Assert.IsFalse(l.RemoveMedkit());
            Assert.AreEqual(0, l.Medkits);
        }

        // ---- ADR-081 (#107): a dropped weapon lands unloaded, its rounds beside it as ammo ----

        [Test]
        public void DropEntries_SplitTheMagazineIntoAnAmmoItemOfTheWeaponsCalibre()
        {
            var e = Loadout.DropEntries(Table(), 2, 2);
            Assert.AreEqual(2, e.Length);
            Assert.AreEqual(LootKind.Weapon, e[0].Kind);
            Assert.AreEqual(2, e[0].WeaponId);
            Assert.AreEqual(0, e[0].Amount, "the weapon on the ground holds no rounds");
            Assert.AreEqual(LootKind.Ammo, e[1].Kind);
            Assert.AreEqual(AmmoType.Shotgun12, e[1].Ammo);
            Assert.AreEqual(2, e[1].Amount);
        }

        [Test]
        public void DropEntries_EmptyMagazine_OrMeleeWeapon_GiveTheWeaponAlone()
        {
            var table = Table();
            Assert.AreEqual(1, Loadout.DropEntries(table, 1, 0).Length);
            var melee = new WeaponTable { Weapons = new[] { new WeaponDef { Id = 9, Name = "M", Category = WeaponCategory.Melee } } };
            var e = Loadout.DropEntries(melee, 9, 5);
            Assert.AreEqual(1, e.Length);
            Assert.AreEqual(LootKind.Weapon, e[0].Kind);
            Assert.AreEqual(0, Loadout.DropEntries(table, 0, 3).Length, "no weapon, nothing");
        }

        // ---- ADR-136 (#174): a rolled gun lies unloaded, a magazine's rounds beside it as their own item ----

        [Test]
        public void SpawnEntries_RolledGun_IsUnloaded_WithItsAmmoBeside()
        {
            var e = Loadout.SpawnEntries(Table(), new LootEntry { Kind = LootKind.Weapon, WeaponId = 2, Amount = 8 });
            Assert.AreEqual(2, e.Length);
            Assert.AreEqual(0, e[0].Amount, "gun on the ground holds no rounds");
            Assert.AreEqual(LootKind.Ammo, e[1].Kind);
            Assert.AreEqual(AmmoType.Shotgun12, e[1].Ammo);
            Assert.AreEqual(8, e[1].Amount);
        }

        [Test]
        public void SpawnEntries_OtherLoot_IsUnchanged_AndNullIsNothing()
        {
            var heal = new LootEntry { Kind = LootKind.Bandage, Amount = 2 };
            var e = Loadout.SpawnEntries(Table(), heal);
            Assert.AreEqual(1, e.Length);
            Assert.AreSame(heal, e[0]);
            Assert.AreEqual(0, Loadout.SpawnEntries(Table(), null).Length);
        }
    }
}

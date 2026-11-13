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
            Assert.AreEqual(0, l.RemoveWeapon(2, out _), "above range");
            Assert.AreEqual(1, l.WeaponInSlot(0), "nothing else changed");
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
    }
}

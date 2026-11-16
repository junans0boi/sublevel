using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-172 / ADR-182: the 16 ordinary guns and the 5 supply guns, from the shipped data.
    public class WeaponRosterTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";
        const int Pistol = 1, Vesper = 6, Breacher = 4, Duo = 16;
        static readonly int[] Pistols = { 1, 6 }, Smgs = { 2, 7, 8 }, Ars = { 3, 9, 10, 11 }, Dmrs = { 12, 13, 14 }, Srs = { 5, 15 }, Shotguns = { 4, 16 };
        static readonly int[] Supply = { 31, 32, 33, 34, 35 };

        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));
        static WeaponDef[] Of(WeaponTable t, int[] ids) => ids.Select(i => t.Find(i)).ToArray();
        static int Count(WeaponTable t, int[] ids, AmmoType a) => Of(t, ids).Count(w => w.Ammo == a);

        [Test]
        public void OrdinaryLoot_Is16Guns_InTheDecidedClasses()
        {
            var t = Weapons();
            var ordinary = t.Weapons.Where(w => WeaponStats.IsFirearm(w) && !w.SupplyOnly).Select(w => w.Id).OrderBy(i => i).ToArray();
            var expected = Pistols.Concat(Smgs).Concat(Ars).Concat(Dmrs).Concat(Srs).Concat(Shotguns).OrderBy(i => i).ToArray();
            Assert.AreEqual(16, expected.Length);
            CollectionAssert.AreEqual(expected, ordinary);

            Assert.AreEqual(3, Count(t, Smgs, AmmoType.Pistol9), "SMG x3 in 9mm");
            Assert.AreEqual(2, Count(t, Ars, AmmoType.Rifle556)); Assert.AreEqual(2, Count(t, Ars, AmmoType.Rifle762));
            Assert.AreEqual(2, Count(t, Dmrs, AmmoType.Rifle556)); Assert.AreEqual(1, Count(t, Dmrs, AmmoType.Rifle762));
            Assert.AreEqual(2, Count(t, Srs, AmmoType.Rifle762));
            Assert.AreEqual(2, Count(t, Shotguns, AmmoType.Shotgun12));
            Assert.AreEqual(2, Count(t, Pistols, AmmoType.Pistol9));
            foreach (var w in Of(t, Pistols)) Assert.AreEqual(WeaponCategory.Sidearm, w.Category, w.Name);
            foreach (var w in Of(t, Smgs.Concat(Ars).Concat(Dmrs).Concat(Srs).Concat(Shotguns).ToArray())) Assert.AreEqual(WeaponCategory.Primary, w.Category, w.Name);
        }

        [Test]
        public void SupplyGuns_AreFiveAndSupplyOnly_NoneIsOrdinary()
        {
            var t = Weapons();
            var supply = t.Weapons.Where(w => w.SupplyOnly).Select(w => w.Id).OrderBy(i => i).ToArray();
            CollectionAssert.AreEqual(Supply, supply);
            Assert.That(supply.Length, Is.InRange(4, 6));
            Assert.AreEqual(2, Of(t, Supply).Count(w => w.MagSize >= 60), "two LMGs: big belts");
            foreach (var w in Of(t, Supply)) Assert.AreEqual(WeaponCategory.Primary, w.Category, w.Name);
        }

        [Test]
        public void EveryGun_DiffersFromTheOthers_InTheStatsThePlayerSees()
        {
            var t = Weapons();
            var guns = t.Weapons.Where(WeaponStats.IsFirearm).ToArray();
            Assert.AreEqual(21, guns.Length);
            var names = guns.Select(w => w.Name).ToArray();
            CollectionAssert.AllItemsAreUnique(names);
            CollectionAssert.AllItemsAreUnique(guns.Select(w => w.Id).ToArray());
            // The stat line: damage, muzzle speed, stopping, RPM, DPS, mode. No two guns share all of it.
            var lines = guns.Select(w => $"{w.Damage}|{w.MuzzleSpeed}|{w.Stopping}|{Mathf.RoundToInt(WeaponStats.Rpm(w))}|{Mathf.RoundToInt(WeaponStats.Dps(w))}|{w.Auto}|{w.Burst}").ToArray();
            CollectionAssert.AllItemsAreUnique(lines);
            // ...and no two guns share damage and rate either: even the cheapest stat line is its own.
            CollectionAssert.AllItemsAreUnique(guns.Select(w => $"{w.Damage}|{w.FireInterval}").ToArray());
            foreach (var w in guns)
            {
                Assert.That(w.Stopping, Is.InRange(1, 10), w.Name + " stopping power is 1-10");
                Assert.Greater(w.MuzzleSpeed, 0f, w.Name + " has a muzzle speed");
                Assert.Greater(WeaponStats.Dps(w), 0f, w.Name);
            }
        }

        [Test]
        public void StoppingPower_FollowsTheClass()
        {
            var t = Weapons();
            Assert.AreEqual(1, t.Find(7).Stopping, "the lightest SMG");
            Assert.AreEqual(10, t.Find(15).Stopping, "the heaviest rifle");
            Assert.Greater(t.Find(10).Stopping, t.Find(9).Stopping, "7.62 AR stops more than 5.56 AR");
            Assert.Greater(t.Find(14).Stopping, t.Find(13).Stopping, "7.62 DMR stops more than 5.56 DMR");
            Assert.GreaterOrEqual(t.Find(Duo).Stopping, t.Find(Breacher).Stopping);
            Assert.AreEqual(0, t.Find(21).Stopping, "melee has none");
        }

        [Test]
        public void Breacher_IsAFiveRoundMagazineSemiAuto()
        {
            var b = Weapons().Find(Breacher);
            Assert.AreEqual(5, b.MagSize);
            Assert.IsFalse(b.Auto, "semi-automatic");
            Assert.IsFalse(b.ReloadsByShell, "magazine, not a tube");
            Assert.AreEqual(9, b.Pellets);
            Assert.Greater(b.ReloadSeconds(0), b.ReloadSeconds(1), "empty takes longer");
        }

        [Test]
        public void Duo_FiresTwoShotsOneByOne_ThenIsDry()
        {
            var t = Weapons();
            var duo = t.Find(Duo);
            Assert.AreEqual(2, duo.MagSize);
            Assert.AreEqual(9, duo.Pellets);
            Assert.IsFalse(duo.ReloadsByShell, "both shells go in at once");
            var l = new Loadout(t);
            l.AddWeapon(Duo, 2);
            l.AddAmmo(AmmoType.Shotgun12, 10);
            double now = 0;
            Assert.IsTrue(l.TryFire(now, 0, 0.12), "first barrel");
            Assert.IsFalse(l.TryFire(now + duo.FireInterval * 0.5, 0, 0.12), "the second barrel is not instant");
            now += duo.FireInterval + 0.01;
            Assert.IsTrue(l.TryFire(now, 0, 0.12), "second barrel");
            Assert.AreEqual(0, l.MagInSlot(0));
            Assert.IsFalse(l.TryFire(now + 1.0, 0, 0.12), "dry: reload needed");
            Assert.IsTrue(l.TryStartReload(now + 1.0));
            l.Tick(now + 1.0 + duo.ReloadSeconds(0) + 0.01);
            Assert.AreEqual(2, l.MagInSlot(0), "both shells loaded together");
        }

        [Test]
        public void StrongPistol_IsHeavierAndSlowerThanTheStandardOne()
        {
            var t = Weapons();
            var vk = t.Find(Pistol); var v = t.Find(Vesper);
            Assert.AreEqual(AmmoType.Pistol9, v.Ammo);
            Assert.Greater(v.Damage, vk.Damage);
            Assert.Less(WeaponStats.Rpm(v), WeaponStats.Rpm(vk));
            Assert.Greater(v.Stopping, vk.Stopping);
            Assert.Less(v.MagSize, vk.MagSize);
        }

        [Test]
        public void EveryGun_HasOpticsAndAttachmentSlots_InTheData()
        {
            var t = Weapons();
            var rules = JsonUtility.FromJson<OpticRules>(File.ReadAllText(DataFolder + "optics.json"));
            foreach (var w in t.Weapons.Where(WeaponStats.IsFirearm))
            {
                Assert.IsTrue(rules.Weapons.Any(o => o.WeaponId == w.Id), w.Name + " has an optic list");
                Assert.IsTrue(rules.AttachmentSlots.Any(o => o.WeaponId == w.Id), w.Name + " has an attachment list");
                Assert.IsTrue(Optics.CanAttach(w.Id, 1, rules), w.Name + " takes at least the red dot");
            }
        }
    }
}

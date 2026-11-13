using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-089: draw delay, tactical / empty / shell-by-shell reloads, burst mode and PUBG damage, from the shipped data.
    public class WeaponTimingTests
    {
        const string DataFolder = "Assets/_Project/Data/Resources/GameData/";
        const int Pistol = 1, Smg = 2, Rifle = 3, Shotgun = 4, Sniper = 5;
        const double SingleMin = 0.12;

        static WeaponTable Weapons() => JsonUtility.FromJson<WeaponTable>(File.ReadAllText(DataFolder + "weapons.json"));

        static Loadout With(WeaponTable table, int weapon, int mag, int reserve)
        {
            var l = new Loadout(table);
            l.AddWeapon(weapon, mag);
            l.AddAmmo(table.Find(weapon).Ammo, reserve);
            return l;
        }

        [Test]
        public void EmptyReload_TakesLongerThanATacticalOne_ForEveryMagazineWeapon()
        {
            foreach (var w in Weapons().Weapons)
            {
                if (w.IsMelee || w.ReloadsByShell) continue;
                Assert.Greater(w.ReloadSeconds(0), w.ReloadSeconds(1), w.Name);
            }
            var rifle = Weapons().Find(Rifle);
            Assert.AreEqual(2.4f, rifle.ReloadSeconds(10), 1e-5f, "tactical, as the reference rifle");
            Assert.AreEqual(3.1f, rifle.ReloadSeconds(0), 1e-5f, "from empty");
        }

        [Test]
        public void Loadout_UsesTheEmptyTime_WhenTheMagazineIsEmpty()
        {
            var table = Weapons();
            var tactical = With(table, Rifle, 5, 90);
            Assert.IsTrue(tactical.TryStartReload(0));
            tactical.Tick(2.39);
            Assert.IsTrue(tactical.IsReloading);
            tactical.Tick(2.41);
            Assert.AreEqual(30, tactical.MagInSlot(0));

            var empty = With(table, Rifle, 0, 90);
            Assert.IsTrue(empty.TryStartReload(0));
            empty.Tick(3.0);
            Assert.IsTrue(empty.IsReloading, "an empty magazine is not done at 3.0 s");
            empty.Tick(3.11);
            Assert.IsFalse(empty.IsReloading);
            Assert.AreEqual(30, empty.MagInSlot(0));
        }

        [Test]
        public void Shotgun_LoadsOneShellAtATime_AndAShotStopsIt()
        {
            var table = Weapons();
            var shotgun = table.Find(Shotgun);
            var l = With(table, Shotgun, 2, 10);
            Assert.IsTrue(l.TryStartReload(0));
            l.Tick(shotgun.ShellReloadSeconds + 0.01);
            Assert.AreEqual(3, l.MagInSlot(0), "one shell");
            Assert.IsTrue(l.IsReloading);
            l.Tick(2 * shotgun.ShellReloadSeconds + 0.01);
            Assert.AreEqual(4, l.MagInSlot(0), "two shells");
            Assert.IsTrue(l.TryFire(2 * shotgun.ShellReloadSeconds + 0.05), "firing interrupts the reload");
            Assert.IsFalse(l.IsReloading);
            Assert.AreEqual(3, l.MagInSlot(0));
            Assert.AreEqual(8, l.Reserve(AmmoType.Shotgun12));

            // Left alone it stops when full, or when the reserve runs out.
            Assert.IsTrue(l.TryStartReload(5));
            l.Tick(100);
            Assert.AreEqual(6, l.MagInSlot(0));
            Assert.AreEqual(5, l.Reserve(AmmoType.Shotgun12));
            Assert.IsFalse(l.IsReloading);
        }

        [Test]
        public void MagazineReload_IsNotInterruptedByTheTrigger()
        {
            var table = Weapons();
            var l = With(table, Rifle, 5, 90);
            Assert.IsTrue(l.TryStartReload(0));
            Assert.IsFalse(l.TryFire(1.0), "a rifle reload cannot be cut short by firing");
        }

        [Test]
        public void SwitchingWeapons_FiringIsRefusedWhileDrawing()
        {
            var table = Weapons();
            var l = new Loadout(table);
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Pistol, 12);
            Assert.IsTrue(l.TryFire(0), "a picked-up weapon is in hand at once");
            Assert.IsTrue(l.SelectSlot(Loadout.SidearmSlot));
            l.Tick(1.0);
            Assert.IsTrue(l.IsDrawing(1.0));
            Assert.IsFalse(l.TryFire(1.0 + table.Find(Pistol).DrawSeconds - 0.01), "pistol still coming up");
            Assert.IsTrue(l.TryFire(1.0 + table.Find(Pistol).DrawSeconds + 0.01));

            Assert.IsTrue(l.SelectSlot(0));
            Assert.IsFalse(l.TryFire(5.0), "the first attempt starts the draw");
            Assert.IsFalse(l.TryFire(5.49));
            Assert.IsTrue(l.TryFire(5.51), "rifle: 0.5 s");
        }

        [Test]
        public void DrawTimes_GrowWithTheWeapon()
        {
            var t = Weapons();
            Assert.AreEqual(0.5f, t.Find(Rifle).DrawSeconds, 1e-5f, "the reference rifle readies in 500 ms");
            Assert.Less(t.Find(Pistol).DrawSeconds, t.Find(Smg).DrawSeconds);
            Assert.Less(t.Find(Smg).DrawSeconds, t.Find(Rifle).DrawSeconds);
            Assert.Less(t.Find(Rifle).DrawSeconds, t.Find(Sniper).DrawSeconds);
        }

        [Test]
        public void B_CyclesAutoSingleBurst_AndTheNamesFollow()
        {
            var table = Weapons();
            var l = new Loadout(table);
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Sniper, 5);
            Assert.AreEqual(FireMode.Auto, l.ModeOf(0));
            Assert.IsTrue(l.ToggleFireMode(0));
            Assert.AreEqual(FireMode.Single, l.ModeOf(0));
            Assert.IsTrue(l.ToggleFireMode(0));
            Assert.AreEqual(FireMode.Burst, l.ModeOf(0));
            Assert.AreEqual("점사", Loadout.FireModeName(l.ModeOf(0)));
            Assert.IsTrue(l.ToggleFireMode(0));
            Assert.AreEqual(FireMode.Auto, l.ModeOf(0));
            Assert.IsFalse(l.ToggleFireMode(1), "a bolt rifle has one mode");
            Assert.AreEqual(FireMode.Single, l.ModeOf(1));

            // The network bits say the same thing.
            l.ToggleFireMode(0);
            l.ToggleFireMode(0);
            int bits = l.ModeBits();
            Assert.AreEqual(FireMode.Burst, Loadout.ModeFromBits(table.Find(Rifle), bits, 0));
            Assert.AreEqual(FireMode.Single, Loadout.ModeFromBits(table.Find(Sniper), bits, 1));
            Assert.AreEqual(FireMode.Auto, Loadout.ModeFromBits(table.Find(Rifle), 0, 0));
        }

        [Test]
        public void Burst_FiresItsRoundsAtTheWeaponsRate_ThenPauses()
        {
            var table = Weapons();
            var rifle = table.Find(Rifle);
            var l = With(table, Rifle, 30, 0);
            l.ToggleFireMode(0);
            l.ToggleFireMode(0);
            double t = 0;
            Assert.IsTrue(l.TryFire(t, 0, SingleMin));
            Assert.IsTrue(l.TryFire(t += rifle.FireInterval, 0, SingleMin));
            Assert.IsTrue(l.TryFire(t += rifle.FireInterval, 0, SingleMin), "three rounds");
            Assert.IsFalse(l.TryFire(t + rifle.FireInterval, 0, SingleMin), "then the burst gap");
            Assert.IsTrue(l.TryFire(t += rifle.BurstGapSeconds, 0, SingleMin), "next burst");
            Assert.IsTrue(l.TryFire(t += rifle.FireInterval, 0, SingleMin));
            Assert.AreEqual(25, l.MagInSlot(0));
        }

        [Test]
        public void Damage_RifleKillsAnUnarmouredBodyInThreeShots()
        {
            var t = Weapons();
            var rifle = t.Find(Rifle);
            float body = DamageRules.ForZone(rifle, 10f, HitZone.Body);
            Assert.Less(2 * body, 100f, "two body shots leave them standing");
            Assert.GreaterOrEqual(3 * body, 100f, "three put them down");
            Assert.AreEqual(40f * 2.35f, DamageRules.ForZone(rifle, 10f, HitZone.Head), 1e-3f);
            Assert.AreEqual(40f * 0.9f, DamageRules.ForZone(rifle, 10f, HitZone.Limb), 1e-3f);
            var smg = t.Find(Smg);
            Assert.AreEqual(2.1f, smg.HeadFactor, 1e-5f);
            Assert.AreEqual(1.05f, smg.BodyFactor, 1e-5f);
            Assert.AreEqual(1.3f, smg.LimbFactor, 1e-5f);
            var health = new Health(100f);
            for (int i = 0; i < 3; i++) health.ApplyDamage(body);
            Assert.IsTrue(health.IsDead);
        }
    }
}

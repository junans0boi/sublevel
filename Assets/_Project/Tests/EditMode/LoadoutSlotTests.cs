using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-058: five weapon slots with categories, pickup routing, per-slot swap and drop, fire modes, unarmed speed.
    public class LoadoutSlotTests
    {
        const int Pistol = 1, Smg = 2, Rifle = 3, Shotgun = 4;

        static WeaponTable Table() => new WeaponTable
        {
            Weapons = new[]
            {
                new WeaponDef { Id = Pistol, Name = "VK", Category = WeaponCategory.Sidearm, Ammo = AmmoType.Pistol9, Auto = false, FireInterval = 0.18f, MagSize = 12, ReloadTime = 1f },
                new WeaponDef { Id = Smg, Name = "Ordo", Category = WeaponCategory.Primary, Ammo = AmmoType.Pistol9, Auto = true, FireInterval = 0.075f, MagSize = 30, ReloadTime = 1f },
                new WeaponDef { Id = Rifle, Name = "Warden", Category = WeaponCategory.Primary, Ammo = AmmoType.Rifle556, Auto = true, FireInterval = 0.11f, MagSize = 30, ReloadTime = 1f },
                new WeaponDef { Id = Shotgun, Name = "Breacher", Category = WeaponCategory.Primary, Ammo = AmmoType.Shotgun12, Auto = false, FireInterval = 0.85f, MagSize = 6, ReloadTime = 1f },
            }
        };

        // ---- Slots and categories ----

        [Test]
        public void ThereAreFiveSlots_InTheAgreedOrder()
        {
            Assert.AreEqual(5, Loadout.SlotCount);
            Assert.AreEqual(0, Loadout.PrimarySlotA);
            Assert.AreEqual(1, Loadout.PrimarySlotB);
            Assert.AreEqual(2, Loadout.SidearmSlot);
            Assert.AreEqual(3, Loadout.MeleeSlot);
            Assert.AreEqual(4, Loadout.ThrowableSlot);
        }

        [Test]
        public void SlotCategories_PrimaryTwice_ThenSidearm_AndNothingElse()
        {
            Assert.IsTrue(Loadout.SlotTakes(0, WeaponCategory.Primary));
            Assert.IsTrue(Loadout.SlotTakes(1, WeaponCategory.Primary));
            Assert.IsFalse(Loadout.SlotTakes(0, WeaponCategory.Sidearm));
            Assert.IsFalse(Loadout.SlotTakes(1, WeaponCategory.Sidearm));
            Assert.IsTrue(Loadout.SlotTakes(2, WeaponCategory.Sidearm));
            Assert.IsFalse(Loadout.SlotTakes(2, WeaponCategory.Primary));
            foreach (var category in new[] { WeaponCategory.Primary, WeaponCategory.Sidearm })
            {
                Assert.IsFalse(Loadout.SlotTakes(3, category), "melee slot cannot be filled");
                Assert.IsFalse(Loadout.SlotTakes(4, category), "throwable slot holds no weapon");
                Assert.IsFalse(Loadout.SlotTakes(-1, category));
                Assert.IsFalse(Loadout.SlotTakes(5, category));
            }
        }

        [Test]
        public void ThePistol_FitsOnlyTheSidearmSlot()
        {
            var l = new Loadout(Table());
            Assert.IsFalse(l.CanHold(0, Pistol));
            Assert.IsFalse(l.CanHold(1, Pistol));
            Assert.IsTrue(l.CanHold(2, Pistol));
            Assert.IsFalse(l.CanHold(3, Pistol));
            Assert.IsFalse(l.CanHold(4, Pistol));
            foreach (int id in new[] { Smg, Rifle, Shotgun })
            {
                Assert.IsTrue(l.CanHold(0, id));
                Assert.IsTrue(l.CanHold(1, id));
                Assert.IsFalse(l.CanHold(2, id), "primary weapons do not fit the sidearm slot");
            }
            Assert.IsFalse(l.CanHold(0, 0), "no weapon");
            Assert.IsFalse(l.CanHold(0, 99), "unknown weapon");
        }

        [Test]
        public void AddWeaponToSlot_RefusesAWeaponTheSlotDoesNotTake()
        {
            var l = new Loadout(Table());
            Assert.AreEqual(0, l.AddWeaponToSlot(0, Pistol, 5, out _));
            Assert.AreEqual(0, l.WeaponInSlot(0), "nothing was placed");
            Assert.AreEqual(0, l.AddWeaponToSlot(2, Rifle, 5, out _));
            Assert.AreEqual(0, l.WeaponInSlot(2));
            Assert.AreEqual(0, l.AddWeaponToSlot(3, Rifle, 5, out _));
            Assert.AreEqual(0, l.AddWeaponToSlot(4, Pistol, 5, out _));
            Assert.AreEqual(0, l.AddWeaponToSlot(2, Pistol, 7, out _));
            Assert.AreEqual(Pistol, l.WeaponInSlot(2));
            Assert.AreEqual(7, l.MagInSlot(2));
        }

        // ---- Pickup routing ----

        [Test]
        public void Pickup_GoesToTheFirstFreeCompatibleSlot()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Pistol, 12);
            Assert.AreEqual(Pistol, l.WeaponInSlot(2), "a pistol takes the sidearm slot, not slot 1");
            l.AddWeapon(Rifle, 30);
            Assert.AreEqual(Rifle, l.WeaponInSlot(0));
            l.AddWeapon(Smg, 30);
            Assert.AreEqual(Smg, l.WeaponInSlot(1));
            Assert.AreEqual(0, l.WeaponInSlot(3));
            Assert.AreEqual(0, l.WeaponInSlot(4));
        }

        [Test]
        public void Pickup_WithNoFreeSlot_ReplacesTheActiveSlotWhenItFits()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Smg, 20);
            l.AddWeapon(Pistol, 12);
            Assert.IsTrue(l.SelectSlot(1));
            int replaced = l.AddWeapon(Shotgun, 6, out int mag);
            Assert.AreEqual(Smg, replaced);
            Assert.AreEqual(20, mag);
            Assert.AreEqual(Shotgun, l.WeaponInSlot(1));
            Assert.AreEqual(Rifle, l.WeaponInSlot(0), "the other primary is untouched");
            Assert.AreEqual(Pistol, l.WeaponInSlot(2));
        }

        [Test]
        public void Pickup_WithNoFreeSlot_AndTheActiveSlotDoesNotFit_ReplacesTheCompatibleOne()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Smg, 20);
            l.AddWeapon(Pistol, 12);
            Assert.IsTrue(l.SelectSlot(Loadout.SidearmSlot));
            int replaced = l.AddWeapon(Shotgun, 6, out int mag);
            Assert.AreEqual(Rifle, replaced, "the first primary slot");
            Assert.AreEqual(30, mag);
            Assert.AreEqual(Shotgun, l.WeaponInSlot(0));
            Assert.AreEqual(Pistol, l.WeaponInSlot(2), "the pistol in hand stays");
            Assert.AreEqual(Loadout.SidearmSlot, l.ActiveSlot);
        }

        [Test]
        public void ASecondPistol_ReplacesTheFirstEvenWhenAPrimaryIsInHand()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Pistol, 5);
            Assert.AreEqual(0, l.ActiveSlot);
            Assert.AreEqual(Pistol, l.AddWeapon(Pistol, 12, out int mag));
            Assert.AreEqual(5, mag);
            Assert.AreEqual(12, l.MagInSlot(2));
            Assert.AreEqual(Rifle, l.WeaponInSlot(0));
        }

        [Test]
        public void Pickup_OfAnUnknownWeapon_IsRefused()
        {
            var l = new Loadout(Table());
            Assert.IsFalse(l.CanAddWeapon(99));
            Assert.AreEqual(-1, l.PickupSlotFor(99));
            Assert.AreEqual(0, l.AddWeapon(99, 3));
            Assert.IsFalse(l.HasAnyWeapon);
        }

        [Test]
        public void TheFirstWeaponPickedUp_IsTakenInHand_WhereverItLands()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Pistol, 12);
            Assert.AreEqual(Loadout.SidearmSlot, l.ActiveSlot);
            Assert.AreEqual(Pistol, l.ActiveWeapon.Id);
            l.AddWeapon(Rifle, 30);
            Assert.AreEqual(Loadout.SidearmSlot, l.ActiveSlot, "a weapon already in hand stays there");
        }

        // ---- Selecting, swapping, dropping ----

        [Test]
        public void AllFiveSlotsCanBeSelected_AndEmptyOnesLeaveTheHandsEmpty()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            for (int slot = 1; slot < 5; slot++)
            {
                Assert.IsTrue(l.SelectSlot(slot), "slot " + slot);
                Assert.IsNull(l.ActiveWeapon, "slot " + slot + " is empty");
            }
            Assert.IsTrue(l.SelectSlot(0));
            Assert.AreEqual(Rifle, l.ActiveWeapon.Id);
            Assert.IsFalse(l.SelectSlot(5));
            Assert.IsFalse(l.SelectSlot(-1));
        }

        [Test]
        public void SwapSlots_TradesTwoPrimaries_AndKeepsTheWeaponInHand()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Shotgun, 6);
            Assert.IsTrue(l.SwapSlots(0, 1));
            Assert.AreEqual(Shotgun, l.WeaponInSlot(0));
            Assert.AreEqual(6, l.MagInSlot(0));
            Assert.AreEqual(Rifle, l.WeaponInSlot(1));
            Assert.AreEqual(30, l.MagInSlot(1));
            Assert.AreEqual(1, l.ActiveSlot, "the rifle is still in hand");
            Assert.AreEqual(Rifle, l.ActiveWeapon.Id);
        }

        [Test]
        public void SwapSlots_RefusesToPutAPistolInAPrimarySlot()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Pistol, 12);
            Assert.IsFalse(l.SwapSlots(0, 2), "rifle cannot go to the sidearm slot");
            Assert.IsFalse(l.SwapSlots(2, 1), "pistol cannot go to a primary slot, even an empty one");
            Assert.IsFalse(l.SwapSlots(2, 3), "nor to the melee slot");
            Assert.IsFalse(l.SwapSlots(0, 4));
            Assert.AreEqual(Rifle, l.WeaponInSlot(0));
            Assert.AreEqual(Pistol, l.WeaponInSlot(2));
        }

        [Test]
        public void SwapSlots_MovesAWeaponToAnEmptyPrimarySlot()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            Assert.IsTrue(l.SwapSlots(0, 1));
            Assert.AreEqual(0, l.WeaponInSlot(0));
            Assert.AreEqual(Rifle, l.WeaponInSlot(1));
            Assert.AreEqual(1, l.ActiveSlot);
            Assert.IsFalse(l.SwapSlots(0, 0), "same slot");
            Assert.IsFalse(l.SwapSlots(2, 3), "both empty");
            Assert.IsFalse(l.SwapSlots(0, 9), "no such slot");
            Assert.IsFalse(l.SwapSlots(-1, 1), "no such slot");
        }

        [Test]
        public void Drop_FromTheSidearmSlot_ReturnsThePistolWithItsMagazine()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Pistol, 9);
            Assert.AreEqual(Pistol, l.RemoveWeapon(Loadout.SidearmSlot, out int mag));
            Assert.AreEqual(9, mag);
            Assert.AreEqual(0, l.WeaponInSlot(2));
            Assert.AreEqual(0, l.ActiveSlot, "the rifle stays in hand");
            Assert.AreEqual(0, l.RemoveWeapon(Loadout.MeleeSlot, out _), "nothing in the melee slot");
            Assert.AreEqual(0, l.RemoveWeapon(Loadout.ThrowableSlot, out _), "nothing in the throwable slot");
            Assert.AreEqual(0, l.RemoveWeapon(5, out _), "no such slot");
        }

        [Test]
        public void Drop_OfTheWeaponInHand_ComesToTheNextSlotThatHoldsOne()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Pistol, 12);
            l.RemoveWeapon(0, out _);
            Assert.AreEqual(Loadout.SidearmSlot, l.ActiveSlot);
            Assert.AreEqual(Pistol, l.ActiveWeapon.Id);
        }

        [Test]
        public void FreeSlotAndAnyWeaponQueries()
        {
            var l = new Loadout(Table());
            Assert.IsFalse(l.HasAnyWeapon);
            Assert.IsTrue(l.HasFreeWeaponSlot);
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Smg, 30);
            Assert.IsTrue(l.HasFreeWeaponSlot, "the sidearm slot is still free");
            l.AddWeapon(Pistol, 12);
            Assert.IsFalse(l.HasFreeWeaponSlot, "the melee and throwable slots do not count");
            Assert.IsTrue(l.HasAnyWeapon);
        }

        // ---- Fire mode ----

        [Test]
        public void FireMode_OnlyAutomaticWeaponsCanSwitch()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Shotgun, 6);
            l.AddWeapon(Pistol, 12);
            Assert.IsTrue(l.CanSwitchFireMode(0));
            Assert.IsFalse(l.CanSwitchFireMode(1), "pump shotgun");
            Assert.IsFalse(l.CanSwitchFireMode(2), "pistol");
            Assert.IsFalse(l.CanSwitchFireMode(3), "empty slot");
            Assert.IsTrue(l.IsAutoFire(0));
            Assert.IsFalse(l.IsAutoFire(1), "semi-automatic weapons only ever fire single shots");
            Assert.IsFalse(l.IsAutoFire(2));
            Assert.IsFalse(l.ToggleFireMode(1));
            Assert.IsFalse(l.ToggleFireMode(2));
            Assert.IsFalse(l.ToggleFireMode(4));
        }

        [Test]
        public void FireMode_Toggles_AndIsRememberedPerSlot()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.AddWeapon(Smg, 30);
            Assert.IsTrue(l.ToggleFireMode(0));
            Assert.IsFalse(l.IsAutoFire(0));
            Assert.IsTrue(l.IsAutoFire(1), "the other slot is untouched");
            Assert.IsTrue(l.ToggleFireMode(0));
            Assert.IsTrue(l.IsAutoFire(0));
            l.ToggleFireMode(0);
            Assert.IsTrue(l.SwapSlots(0, 1));
            Assert.IsFalse(l.IsAutoFire(1), "the setting travels with the weapon");
            Assert.IsTrue(l.IsAutoFire(0));
        }

        [Test]
        public void FireMode_ResetsWhenTheSlotGetsAnotherWeapon()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 30);
            l.ToggleFireMode(0);
            Assert.IsFalse(l.IsAutoFire(0));
            l.AddWeaponToSlot(0, Smg, 30, out _);
            Assert.IsTrue(l.IsAutoFire(0));
            l.ToggleFireMode(0);
            l.RemoveWeapon(0, out _);
            l.AddWeapon(Smg, 30);
            Assert.IsTrue(l.IsAutoFire(0));
        }

        [Test]
        public void FireModeNames_AreKorean()
        {
            Assert.AreEqual("연사", Loadout.FireModeName(true));
            Assert.AreEqual("단발", Loadout.FireModeName(false));
        }

        [Test]
        public void SingleMode_OnAnAutomaticWeapon_FiresNoFasterThanTheSingleModeInterval()
        {
            var rules = new SlotRules();
            Assert.AreEqual(0.12f, rules.SingleModeMinInterval, 1e-6f);
            var l = new Loadout(Table());
            l.AddWeapon(Smg, 30); // interval 0.075
            Assert.IsTrue(l.TryFire(0.0, 0.0, rules.SingleModeMinInterval));
            Assert.IsTrue(l.TryFire(0.08, 0.0, rules.SingleModeMinInterval), "automatic: the weapon's own interval");

            var single = new Loadout(Table());
            single.AddWeapon(Smg, 30);
            single.ToggleFireMode(0);
            Assert.AreEqual(0.12, single.EffectiveInterval(Table().Find(Smg), 0, rules.SingleModeMinInterval), 1e-6);
            Assert.IsTrue(single.TryFire(0.0, 0.0, rules.SingleModeMinInterval));
            Assert.IsFalse(single.TryFire(0.08, 0.0, rules.SingleModeMinInterval), "single mode: held to 0.12 s");
            Assert.IsTrue(single.TryFire(0.13, 0.0, rules.SingleModeMinInterval));
            // A weapon already slower than the limit keeps its own interval.
            var slow = new Loadout(Table());
            slow.AddWeapon(Shotgun, 6);
            Assert.AreEqual(0.85, slow.EffectiveInterval(Table().Find(Shotgun), 0, rules.SingleModeMinInterval), 1e-6);
        }

        [Test]
        public void CancelReload_StopsTheReloadInProgress()
        {
            var l = new Loadout(Table());
            l.AddWeapon(Rifle, 0);
            l.AddAmmo(AmmoType.Rifle556, 30);
            Assert.IsTrue(l.TryStartReload(0));
            l.CancelReload();
            Assert.IsFalse(l.IsReloading);
            Assert.IsFalse(l.Tick(10));
            Assert.AreEqual(0, l.MagInSlot(0));
        }

        // ---- Unarmed speed ----

        [Test]
        public void UnarmedMoveFactor_IsOneTwelve_AndSpeedsUpWalkAndSprint()
        {
            var p = new LocomotionParams();
            Assert.AreEqual(1.12f, p.UnarmedMoveFactor, 1e-6f);
            Assert.AreEqual(1f, Locomotion.MoveSpeedFactor(1f, 1f, 1f, true, p), 1e-6f);
            Assert.AreEqual(1.12f, Locomotion.MoveSpeedFactor(1f, 1f, 1f, false, p), 1e-6f);
            // The factor reaches the velocity: 4.5 * 1.12 walking, 7 * 1.12 sprinting.
            Locomotion.PlanarVelocity(0f, 1f, 0f, false, p, out float wx, out float wz, Locomotion.MoveSpeedFactor(1f, 1f, 1f, false, p));
            Assert.AreEqual(5.04f, (float)System.Math.Sqrt(wx * wx + wz * wz), 1e-4f);
            Locomotion.PlanarVelocity(0f, 1f, 0f, true, p, out float sx, out float sz, Locomotion.MoveSpeedFactor(1f, 1f, 1f, false, p));
            Assert.AreEqual(7.84f, (float)System.Math.Sqrt(sx * sx + sz * sz), 1e-4f);
            Locomotion.PlanarVelocity(0f, 1f, 0f, true, p, out float ax, out float az, Locomotion.MoveSpeedFactor(1f, 1f, 1f, true, p));
            Assert.Greater(System.Math.Sqrt(sx * sx + sz * sz), System.Math.Sqrt(ax * ax + az * az) * 1.1, "unarmed sprint is at least 10% faster");
        }

        [Test]
        public void UnarmedFactor_CombinesWithTheOtherFactors_ByMultiplying()
        {
            var p = new LocomotionParams();
            // Lying (0.18) with empty hands: 0.18 * 1.12.
            Assert.AreEqual(0.2016f, Locomotion.MoveSpeedFactor(1f, 1f, 0.18f, false, p), 1e-5f);
            // Leaning (0.85) and crouching (0.55) armed: no unarmed bonus.
            Assert.AreEqual(0.4675f, Locomotion.MoveSpeedFactor(1f, 0.85f, 0.55f, true, p), 1e-5f);
        }
    }
}

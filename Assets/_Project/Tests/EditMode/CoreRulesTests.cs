using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class CoreRulesTests
    {
        static WeaponTable Table() => new WeaponTable
        {
            Weapons = new[]
            {
                new WeaponDef { Id = 1, Name = "P", Ammo = AmmoType.Light, Damage = 20, FireInterval = 0.5f, MagSize = 3, ReloadTime = 1f,
                    SpreadDeg = 2, Pellets = 1, Range = 100, FalloffStart = 10, FalloffEnd = 30, MinDamageFactor = 0.5f },
                new WeaponDef { Id = 2, Name = "S", Ammo = AmmoType.Shell, Damage = 10, FireInterval = 1f, MagSize = 2, ReloadTime = 2f,
                    SpreadDeg = 8, Pellets = 8, Range = 40 },
            }
        };

        [Test]
        public void Health_DamageClampsAtZero_AndDeadStaysDead()
        {
            var h = new Health(100);
            Assert.AreEqual(30f, h.ApplyDamage(30));
            Assert.AreEqual(70f, h.ApplyDamage(500));
            Assert.IsTrue(h.IsDead);
            Assert.AreEqual(0f, h.ApplyDamage(10));
            Assert.AreEqual(0f, h.Heal(10));
            Assert.AreEqual(0f, h.Current);
        }

        [Test]
        public void Health_HealClampsAtMax()
        {
            var h = new Health(100);
            h.ApplyDamage(20);
            Assert.AreEqual(20f, h.Heal(50));
            Assert.AreEqual(100f, h.Current);
        }

        [Test]
        public void Damage_FallsOffLinearlyBetweenStartAndEnd()
        {
            var w = Table().Find(1);
            Assert.AreEqual(20f, DamageRules.AtDistance(w, 5), 1e-4f);
            Assert.AreEqual(15f, DamageRules.AtDistance(w, 20), 1e-4f);
            Assert.AreEqual(10f, DamageRules.AtDistance(w, 30), 1e-4f);
            Assert.AreEqual(10f, DamageRules.AtDistance(w, 90), 1e-4f);
            Assert.AreEqual(0f, DamageRules.AtDistance(w, 101), 1e-4f);
        }

        [Test]
        public void ShotOffsets_OnePerPellet_InsideConeAndSeeded()
        {
            var w = Table().Find(2);
            var yaw = new float[16]; var pitch = new float[16];
            int n = DamageRules.ShotOffsets(w, new Random(7), yaw, pitch);
            Assert.AreEqual(8, n);
            for (int i = 0; i < n; i++)
                Assert.LessOrEqual(Math.Sqrt(yaw[i] * yaw[i] + pitch[i] * pitch[i]), 4.0001);
            var yaw2 = new float[16]; var pitch2 = new float[16];
            DamageRules.ShotOffsets(w, new Random(7), yaw2, pitch2);
            CollectionAssert.AreEqual(yaw, yaw2);
        }

        [Test]
        public void Loadout_FireConsumesAmmoAndRespectsFireInterval()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 3);
            Assert.IsTrue(l.TryFire(0));
            Assert.IsFalse(l.TryFire(0.2), "too soon");
            Assert.IsTrue(l.TryFire(0.5));
            Assert.IsTrue(l.TryFire(1.0));
            Assert.IsFalse(l.TryFire(1.5), "empty");
            Assert.AreEqual(0, l.MagInSlot(0));
        }

        [Test]
        public void Loadout_ReloadMovesReserveIntoMagazineAfterReloadTime()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 1);
            l.AddAmmo(AmmoType.Light, 5);
            Assert.IsTrue(l.TryStartReload(10));
            Assert.IsFalse(l.TryFire(10.5), "cannot fire while reloading");
            Assert.IsFalse(l.Tick(10.5));
            Assert.IsTrue(l.Tick(11));
            Assert.AreEqual(3, l.MagInSlot(0));
            Assert.AreEqual(3, l.Reserve(AmmoType.Light));
        }

        [Test]
        public void Loadout_ReloadRefused_WhenFullOrNoReserve()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 3);
            l.AddAmmo(AmmoType.Light, 5);
            Assert.IsFalse(l.TryStartReload(0), "full magazine");
            var l2 = new Loadout(Table());
            l2.AddWeapon(1, 0);
            l2.AddAmmo(AmmoType.Shell, 5);
            Assert.IsFalse(l2.TryStartReload(0), "wrong ammo type only");
        }

        [Test]
        public void Loadout_ThirdWeaponReplacesActiveSlot()
        {
            var l = new Loadout(Table());
            Assert.AreEqual(0, l.AddWeapon(1, 3));
            Assert.AreEqual(0, l.AddWeapon(2, 2));
            Assert.AreEqual(0, l.ActiveSlot);
            Assert.AreEqual(1, l.AddWeapon(2, 1, out int leftInMag));
            Assert.AreEqual(3, leftInMag);
            Assert.AreEqual(2, l.WeaponInSlot(0));
            Assert.AreEqual(1, l.MagInSlot(0));
        }

        [Test]
        public void Loadout_SwitchingSlotCancelsReload()
        {
            var l = new Loadout(Table());
            l.AddWeapon(1, 0);
            l.AddWeapon(2, 2);
            l.AddAmmo(AmmoType.Light, 3);
            l.TryStartReload(0);
            Assert.IsTrue(l.SelectSlot(1));
            Assert.IsFalse(l.IsReloading);
            l.Tick(5);
            Assert.AreEqual(0, l.MagInSlot(0));
            Assert.IsFalse(l.SelectSlot(1), "already active");
            Assert.IsFalse(l.SelectSlot(2), "out of range");
        }

        [Test]
        public void Loadout_MedkitHealsAndIsConsumed_OnlyWhenHurt()
        {
            var l = new Loadout(Table());
            var h = new Health(100);
            l.AddMedkits(2);
            Assert.IsFalse(l.TryUseMedkit(h), "full health");
            h.ApplyDamage(80);
            Assert.IsTrue(l.TryUseMedkit(h));
            Assert.AreEqual(70f, h.Current);
            Assert.AreEqual(1, l.Medkits);
        }

        [Test]
        public void LootPlan_SameSeedSamePlan_AndFirstPointIsAWeapon()
        {
            var table = new LootTable
            {
                Entries = new[]
                {
                    new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Light, Amount = 20, Weight = 50 },
                    new LootEntry { Kind = LootKind.Medkit, Amount = 1, Weight = 30 },
                    new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 3, Weight = 1 },
                }
            };
            for (int seed = 0; seed < 20; seed++)
            {
                var a = table.Plan(6, seed);
                var b = table.Plan(6, seed);
                Assert.AreEqual(LootKind.Weapon, a[0].Kind, "seed " + seed);
                CollectionAssert.AreEqual(a, b);
            }
            bool anyDifferent = false;
            var first = table.Plan(6, 0);
            for (int seed = 1; seed < 20 && !anyDifferent; seed++)
            {
                var other = table.Plan(6, seed);
                for (int i = 0; i < 6; i++) anyDifferent |= other[i] != first[i];
            }
            Assert.IsTrue(anyDifferent, "the seed must change the plan");
        }

        [Test]
        public void EmptyLootTable_PlansNothingAndDoesNotThrow()
        {
            var table = new LootTable();
            Assert.IsNull(table.Roll(new Random(1)));
            Assert.AreEqual(2, table.Plan(2, 5).Length);
        }

        [Test]
        public void LootRoll_FollowsWeights()
        {
            var table = new LootTable
            {
                Entries = new[]
                {
                    new LootEntry { Kind = LootKind.Ammo, Weight = 3 },
                    new LootEntry { Kind = LootKind.Medkit, Weight = 1 },
                }
            };
            var rng = new Random(1);
            int ammo = 0;
            for (int i = 0; i < 4000; i++)
                if (table.Roll(rng).Kind == LootKind.Ammo) ammo++;
            Assert.That(ammo, Is.InRange(2850, 3150));
        }

        [Test]
        public void Locomotion_ForwardFollowsYaw_DiagonalIsNotFaster_SprintOnlyForward()
        {
            var p = new LocomotionParams();
            Locomotion.PlanarVelocity(0, 1, 90, false, p, out var vx, out var vz);
            Assert.AreEqual(p.WalkSpeed, vx, 1e-3f);
            Assert.AreEqual(0f, vz, 1e-3f);
            Locomotion.PlanarVelocity(1, 1, 0, false, p, out vx, out vz);
            Assert.AreEqual(p.WalkSpeed, Math.Sqrt(vx * vx + vz * vz), 1e-3);
            Locomotion.PlanarVelocity(0, 1, 0, true, p, out vx, out vz);
            Assert.AreEqual(p.SprintSpeed, vz, 1e-3f);
            Locomotion.PlanarVelocity(0, -1, 0, true, p, out vx, out vz);
            Assert.AreEqual(-p.WalkSpeed, vz, 1e-3f);
        }

        [Test]
        public void Locomotion_JumpOnlyFromGround_GravityInAir()
        {
            var p = new LocomotionParams();
            Assert.AreEqual(p.JumpSpeed, Locomotion.VerticalVelocity(0, true, true, 0.02f, p));
            Assert.AreEqual(p.GroundedStick, Locomotion.VerticalVelocity(-9, true, false, 0.02f, p));
            Assert.AreEqual(1f + p.Gravity * 0.5f, Locomotion.VerticalVelocity(1f, false, true, 0.5f, p), 1e-4f);
        }

        [Test]
        public void LegPace_MakesUpOnlyPartOfTheMissingSpeed_AndIsCapped()
        {
            var gait = new GaitRules { LegPaceFollow = 0.5f, MaxLegPace = 1.75f };
            Assert.AreEqual(1f, Locomotion.LegPace(2f, 2.86f, gait), 1e-4f, "slower than the clip: the blend covers it");
            Assert.AreEqual(1.287f, Locomotion.LegPace(4.5f, 2.86f, gait), 0.005f, "normal movement");
            Assert.AreEqual(1.724f, Locomotion.LegPace(7f, 2.86f, gait), 0.005f, "sprinting");
            Assert.AreEqual(1.75f, Locomotion.LegPace(20f, 2.86f, gait), 1e-4f, "never faster than the cap");
        }

        [Test]
        public void AirPhase_ShowsTheRisePoseGoingUp_AndTheFallPoseComingDown()
        {
            var jump = new JumpRules { RisePhase = 0.25f, FallPhase = 0.375f, PoseSpeed = 3f };
            Assert.AreEqual(0.25f, Locomotion.AirPhase(4.8f, jump), 1e-4f);
            Assert.AreEqual(0.375f, Locomotion.AirPhase(-6f, jump), 1e-4f);
            Assert.AreEqual(0.3125f, Locomotion.AirPhase(0f, jump), 1e-4f, "half way at the top of the jump");
        }

        [Test]
        public void CrosshairGap_IsTheSpreadConeOnScreen()
        {
            // A 3 degree cone is 1.5 degrees either side: tan(1.5) / tan(30) * 540 px.
            Assert.AreEqual(24.49f, Crosshair.GapPixels(3f, 60f, 1080f), 0.05f);
            Assert.Less(Crosshair.GapPixels(3f * 0.55f, 50f, 1080f), 24.49f, "shouldering tightens it even though the view zooms in");
            Assert.AreEqual(0f, Crosshair.GapPixels(0f, 60f, 1080f), 1e-4f);
        }

        static LegHeading Settled(float vx, float vz, GaitRules gait, LegHeading legs = null)
        {
            legs ??= new LegHeading();
            for (int i = 0; i < 100; i++) legs.Tick(vx, vz, 0.02f, gait);
            return legs;
        }

        [Test]
        public void Legs_TurnTowardTheWayTheCharacterMoves_AndRunBackwardWhenRetreating()
        {
            var gait = new GaitRules { MaxLegTurnDeg = 60f, LegTurnDegPerSecond = 360f, LegTurnMinSpeed = 0.5f };
            var forward = Settled(0, 4.5f, gait);
            Assert.AreEqual(0f, forward.TurnDeg, 1e-3f);
            Assert.IsFalse(forward.Backward);

            var diagonal = Settled(3f, 3f, gait);
            Assert.AreEqual(45f, diagonal.TurnDeg, 1e-3f, "forward-right: legs turned half right");
            Assert.IsFalse(diagonal.Backward);

            var sideways = Settled(4.5f, 0, gait);
            Assert.AreEqual(60f, sideways.TurnDeg, 1e-3f, "sideways: as far as the waist allows");
            Assert.IsFalse(sideways.Backward);

            var back = Settled(0, -4.5f, gait);
            Assert.AreEqual(0f, back.TurnDeg, 1e-3f);
            Assert.IsTrue(back.Backward);

            var backRight = Settled(3f, -3f, gait);
            Assert.AreEqual(-45f, backRight.TurnDeg, 1e-3f, "backing off to the right: legs turned left, running backward");
            Assert.IsTrue(backRight.Backward);

            var stopped = Settled(0, 0, gait, backRight);
            Assert.AreEqual(0f, stopped.TurnDeg, 1e-3f, "standing: legs face forward again");
        }

        [Test]
        public void Legs_TurnGradually_AndDoNotFlickerWhenMovingAlmostSideways()
        {
            var gait = new GaitRules { MaxLegTurnDeg = 60f, LegTurnDegPerSecond = 360f, LegTurnMinSpeed = 0.5f };
            var legs = new LegHeading();
            legs.Tick(3f, 3f, 0.05f, gait);
            Assert.AreEqual(18f, legs.TurnDeg, 1e-3f, "360 degrees a second for 0.05 s");

            // Sideways and a touch backward, then a touch forward: still running forward-turned, no flip.
            legs = Settled(4.5f, 0f, gait);
            legs.Tick(4.5f, -0.3f, 0.02f, gait);
            Assert.IsFalse(legs.Backward);
            legs.Tick(4.5f, 0.3f, 0.02f, gait);
            Assert.IsFalse(legs.Backward);
            // Clearly backward: flips.
            legs.Tick(3f, -3f, 0.02f, gait);
            Assert.IsTrue(legs.Backward);
        }
    }
}

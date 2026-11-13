using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // M1.6a: right-button hold/tap, recoil, lean and foot-contact rules.
    public class AimingTests
    {
        const float Tap = 0.2f;

        [Test]
        public void HoldingTheButton_ShouldersTheWeapon_UntilReleased()
        {
            var aim = new AimInput(Tap);
            Assert.AreEqual(AimMode.Hip, aim.Update(true, 0.00, true), "not yet: could still be a tap");
            Assert.AreEqual(AimMode.Shoulder, aim.Update(true, 0.25, true));
            Assert.AreEqual(AimMode.Shoulder, aim.Update(true, 1.00, true));
            Assert.AreEqual(AimMode.Hip, aim.Update(false, 1.05, true));
        }

        [Test]
        public void TappingTheButton_TogglesSights()
        {
            var aim = new AimInput(Tap);
            aim.Update(true, 0.00, true);
            Assert.AreEqual(AimMode.Sights, aim.Update(false, 0.10, true), "tap turns sights on");
            Assert.AreEqual(AimMode.Sights, aim.Update(false, 2.00, true), "and they stay on");
            aim.Update(true, 3.00, true);
            Assert.AreEqual(AimMode.Hip, aim.Update(false, 3.10, true), "second tap turns them off");
        }

        [Test]
        public void HoldingWhileInSights_KeepsSights()
        {
            var aim = new AimInput(Tap);
            aim.Update(true, 0.0, true);
            aim.Update(false, 0.1, true);
            Assert.AreEqual(AimMode.Sights, aim.Update(true, 1.0, true));
            Assert.AreEqual(AimMode.Sights, aim.Update(true, 1.5, true));
            Assert.AreEqual(AimMode.Sights, aim.Update(false, 1.6, true), "a long press is not a tap");
        }

        [Test]
        public void LosingTheAbilityToAim_DropsToHip_AndIgnoresTheRestOfThatPress()
        {
            var aim = new AimInput(Tap);
            aim.Update(true, 0.0, true);
            Assert.AreEqual(AimMode.Shoulder, aim.Update(true, 0.5, true));
            Assert.AreEqual(AimMode.Hip, aim.Update(true, 0.6, false), "e.g. the weapon was dropped");
            Assert.AreEqual(AimMode.Hip, aim.Update(true, 0.7, true), "still the same press");
            Assert.AreEqual(AimMode.Hip, aim.Update(false, 0.75, true), "its release must not count as a tap");
            aim.Update(true, 1.0, true);
            Assert.AreEqual(AimMode.Shoulder, aim.Update(true, 1.3, true), "a fresh press works again");
        }

        [Test]
        public void AimRules_GiveEachModeItsOwnStance()
        {
            var rules = new AimRules
            {
                Hip = new AimStance { Fov = 60, SpreadFactor = 1f },
                Shoulder = new AimStance { Fov = 50, SpreadFactor = 0.6f },
                Sights = new AimStance { Fov = 40, SpreadFactor = 0.3f },
            };
            Assert.AreEqual(60f, rules.For(AimMode.Hip).Fov);
            Assert.AreEqual(0.6f, rules.For(AimMode.Shoulder).SpreadFactor);
            Assert.AreEqual(40f, rules.For(AimMode.Sights).Fov);
        }

        [Test]
        public void Recoil_KicksUp_ThenRecovers()
        {
            var recoil = new ViewRecoil();
            recoil.Kick(2f, 0.5f, 10f);
            recoil.Kick(2f, -0.2f, 10f);
            Assert.AreEqual(4f, recoil.Pitch, 1e-4f);
            Assert.AreEqual(0.3f, recoil.Yaw, 1e-4f);
            recoil.Tick(0.1f, 10f); // recovers 1 degree of the kick's length
            Assert.Less(recoil.Pitch, 4f);
            Assert.Greater(recoil.Pitch, 2.9f);
            for (int i = 0; i < 20; i++) recoil.Tick(0.1f, 10f);
            Assert.AreEqual(0f, recoil.Pitch, 1e-4f);
            Assert.AreEqual(0f, recoil.Yaw, 1e-4f);
        }

        [Test]
        public void Recoil_IsCapped()
        {
            var recoil = new ViewRecoil();
            for (int i = 0; i < 30; i++) recoil.Kick(2f, 0f, 10f);
            Assert.AreEqual(10f, recoil.Pitch, 1e-4f);
        }

        [Test]
        public void Lean_EasesTowardTheHeldSide_AndBack()
        {
            var lean = new LeanState();
            lean.Tick(-1, 0.1f, 5f);
            Assert.AreEqual(-0.5f, lean.Value, 1e-4f);
            lean.Tick(-1, 0.5f, 5f);
            Assert.AreEqual(-1f, lean.Value, 1e-4f, "never past full lean");
            lean.Tick(0, 0.1f, 5f);
            Assert.AreEqual(-0.5f, lean.Value, 1e-4f);
            lean.Tick(0, 1f, 5f);
            Assert.AreEqual(0f, lean.Value, 1e-4f);
        }

        [Test]
        public void FootContact_FiresOncePerPlant()
        {
            var foot = new FootContact(0.05f, 0.12f);
            int steps = 0;
            // planted, lift, swing, come down, stay down, lift again, come down
            foreach (var h in new[] { 0.02f, 0.08f, 0.15f, 0.2f, 0.1f, 0.04f, 0.03f, 0.02f, 0.14f, 0.06f, 0.04f })
                if (foot.Update(h)) steps++;
            Assert.AreEqual(2, steps);
        }

        [Test]
        public void FootContact_IgnoresJitterAroundTheGround()
        {
            var foot = new FootContact(0.05f, 0.12f);
            int steps = 0;
            foreach (var h in new[] { 0.02f, 0.06f, 0.04f, 0.07f, 0.03f, 0.06f, 0.04f })
                if (foot.Update(h)) steps++;
            Assert.AreEqual(0, steps, "the foot never really left the ground");
        }

        [Test]
        public void MoveFactor_SlowsMovement()
        {
            var p = new LocomotionParams { WalkSpeed = 4f, SprintSpeed = 8f };
            Locomotion.PlanarVelocity(0, 1, 0, false, p, out _, out var full);
            Locomotion.PlanarVelocity(0, 1, 0, false, p, out _, out var aimed, 0.5f);
            Assert.AreEqual(4f, full, 1e-4f);
            Assert.AreEqual(2f, aimed, 1e-4f);
        }

        [Test]
        public void SpreadFactor_TightensTheCone()
        {
            var weapon = new WeaponDef { SpreadDeg = 8f, Pellets = 1 };
            var yaw = new float[1];
            var pitch = new float[1];
            float widest = 0f;
            var rng = new System.Random(3);
            for (int i = 0; i < 200; i++)
            {
                DamageRules.ShotOffsets(weapon, rng, yaw, pitch, 0.25f);
                widest = System.Math.Max(widest, (float)System.Math.Sqrt(yaw[0] * yaw[0] + pitch[0] * pitch[0]));
            }
            Assert.LessOrEqual(widest, 1.0001f, "8 degree cone at a quarter is 2 degrees wide: 1 degree off-axis at most");
            Assert.Greater(widest, 0.5f, "but it is still a cone, not a point");
        }

        [Test]
        public void SightsHold_AimsOnlyWhileHeld()
        {
            var aim = new AimInput(Tap) { SightsHold = true };
            Assert.AreEqual(AimMode.Sights, aim.Update(true, 0.0, true));
            Assert.AreEqual(AimMode.Sights, aim.Update(true, 1.0, true), "no shoulder after a long hold");
            Assert.AreEqual(AimMode.Hip, aim.Update(false, 1.1, true));
        }

        [Test]
        public void ShoulderToggle_StaysAfterRelease_NextPressPutsItAway()
        {
            var aim = new AimInput(Tap) { ShoulderToggle = true };
            aim.Update(true, 0.0, true);
            Assert.AreEqual(AimMode.Shoulder, aim.Update(true, Tap + 0.1, true));
            Assert.AreEqual(AimMode.Shoulder, aim.Update(false, Tap + 0.2, true), "kept");
            Assert.AreEqual(AimMode.Hip, aim.Update(true, 2.0, true), "next press puts it away");
            Assert.AreEqual(AimMode.Hip, aim.Update(true, 3.0, true), "and holding it does not raise it again");
            Assert.AreEqual(AimMode.Hip, aim.Update(false, 3.1, true));
        }
    }
}

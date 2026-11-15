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
    // ADR-112 in the TestBox with a test pool: the water depth test, the swim pose and speed, diving, drowning, and no
    // shooting or throwing from the water.
    public class SwimPlayTests
    {
        sealed class Input : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        SwimRules _saved;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_saved != null) SwimRules.Current = _saved;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Pool_SwimPoseSpeed_NoFiringOrThrowing_DiveDrowns_SurfacingRecovers()
        {
            // Quick breath: 2 s of it, 1 s to recover, so the test does not wait the real 20 and 9.
            _saved = SwimRules.Current;
            SwimRules.Current = new SwimRules { DiveSeconds = 2f, RecoverSeconds = 1f };
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = ThrowableRules.GrenadeLootKind, Amount = 1 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            yield return new WaitForSeconds(0.6f);

            // Dry: not swimming, shooting works.
            var swim = me.GetComponent<CombatantSwim>();
            Assert.IsNotNull(swim, "every combatant gets a swim component");
            Assert.IsFalse(pc.Swimming);

            // A 2.0 m deep pool.
            var pool = TestPool.Create(new Vector3(3000f, 0f, 3000f), 12f, 2.0f, out var water);
            Physics.SyncTransforms();
            Assert.AreEqual(2.0f, WaterVolume.DepthAt(new Vector3(3000f, 0.1f, 3000f), out float surface, out float floor), 0.05f, "depth = surface - floor");
            Assert.AreEqual(2.0f, surface, 1e-3f);
            pc.Teleport(new Vector3(3000f, 0.1f, 3000f));
            yield return WaitUntil(() => pc.Swimming, 2f, "swimming in deep water");
            yield return new WaitForSeconds(1.5f);
            float feetFloat = 2.0f + 0.1f - (me.UprightEyeHeight - StanceRules.Current.Prone.EyeDropMeters);
            Assert.AreEqual(Stance.Prone, pc.BodyStance, "lying swim pose");
            Assert.AreEqual(feetFloat, pc.transform.position.y, 0.15f, "floating with the eyes just over the surface");
            Assert.IsTrue(swim.InWater);
            Assert.IsFalse(swim.HeadUnder, "head up");
            Assert.IsTrue(me.Holstered.Value, "the weapon is put away in the water");
            Assert.AreEqual(0, me.ActiveWeaponId, "empty hands");
            var anim = me.GetComponent<CombatantAnimation>();
            Assert.IsNotNull(anim);
            Assert.Greater(anim.SwimPose, 0.95f, "swim pose shown");
            Assert.Less(anim.DivePose, 0.05f, "head up: no dive pose");
            Assert.AreEqual(1f, swim.Breath, 1e-3f);

            // No shooting, no throwing from the water.
            me.StanceSpreadFactor = 0f;
            Assert.IsFalse(me.ServerFire(Vector3.forward), "no shooting while swimming");
            var thrower = me.GetComponent<ThrowableUser>();
            Assert.IsFalse(thrower.ServerThrow(me.EyePosition, Vector3.forward * 10f), "no throwing while swimming");

            // Speed: forward for a second, about 2.9 m/s.
            input.Frame.Move = new Vector2(0f, 1f);
            pc.LookAt(new Vector3(3000f, pc.transform.position.y + 1f, 3100f));
            yield return new WaitForSeconds(0.4f);
            var from = pc.transform.position;
            yield return new WaitForSeconds(1.0f);
            var to = pc.transform.position;
            float speed = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            Assert.AreEqual(Swim.Speed(GameData.Rules.Locomotion.SprintSpeed, SwimRules.Current), speed, 0.5f, "swim speed");
            input.Frame.Move = Vector2.zero;
            float phase0 = anim.StrokePhase;
            input.Frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.3f);
            Assert.AreNotEqual(phase0, anim.StrokePhase, "the stroke runs while swimming");
            input.Frame.Move = Vector2.zero;

            // Dive (Ctrl held): the head goes under, the breath drains, then drowning hurts.
            float hp0 = me.Hp.Value;
            float drownBefore = swim.DrownDamage;
            input.Frame.SlowWalk = true;
            yield return WaitUntil(() => swim.HeadUnder, 3f, "the head under water");
            yield return WaitUntil(() => anim.DivePose > 0.95f, 2f, "dive pose");
            yield return WaitUntil(() => pc.transform.position.y < feetFloat - 0.7f, 3f, "sinking to the dive depth");
            yield return WaitUntil(() => swim.Breath <= 0f, 4f, "the breath to run out");
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(swim.DrownDamage, drownBefore + 3f, "drowning damage after the breath ran out");
            Assert.Less(me.Hp.Value, hp0 - 3f, "and it cost health");

            // Up again: no more damage, the breath refills.
            input.Frame.SlowWalk = false;
            yield return WaitUntil(() => !swim.HeadUnder, 3f, "the head back out");
            yield return WaitUntil(() => swim.Breath >= 0.99f, 4f, "the breath back");
            float hp1 = me.Hp.Value;
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(hp1, me.Hp.Value, 1e-3f, "no damage with the head up");

            // Out of the water: standing again, shooting works.
            pc.Teleport(new Vector3(3000f, 0.1f, 3008f));
            yield return WaitUntil(() => !pc.Swimming, 2f, "out of the water");
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(swim.InWater);
            Assert.IsFalse(me.Holstered.Value, "the weapon comes back out of the water");
            Assert.AreEqual(3, me.ActiveWeaponId);
            Assert.Less(anim.SwimPose, 0.05f, "swim pose gone");
            // A body standing on something higher than the surface inside the volume (a shore hill) has no depth.
            var hill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hill.transform.position = new Vector3(3003f, 2.5f, 3003f);
            hill.transform.localScale = new Vector3(2f, 3f, 2f);
            Physics.SyncTransforms();
            Assert.AreEqual(0f, WaterVolume.DepthAt(new Vector3(3003f, 4.05f, 3003f), out _, out _), 1e-3f, "on a hill over the water's square: dry");
            UnityEngine.Object.Destroy(hill);
            UnityEngine.Object.Destroy(pool);
        }
    }
}

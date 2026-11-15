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
    // #180 / ADR-144: standing still in the sights, single shots at a motionless chest 20 m away all hit
    // (PUBG: stationary ADS deviation is about 0). Goes through the real input path, not ServerFire with a closed cone.
    public class AdsAccuracyPlayTests
    {
        const int Rifle = 3;

        sealed class Scripted : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.FireHeld = false; // one frame of trigger = one automatic shot
                return f;
            }
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StationarySights_SingleShotsAt20m_AllHitTheChest()
        {
            var input = new Scripted();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");

            Combatant dummy = null;
            foreach (var c in Combatant.All) if (!c.IsPlayer) { dummy = c; break; }
            Assert.IsNotNull(dummy, "a dummy");
            foreach (var part in dummy.GetComponents<MonoBehaviour>())
                if (part.GetType().Name == "TrainingDummy" || part.GetType().Name == "BotController") part.enabled = false;
            // 20 m of open floor of its own, away from the test box.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(3000f, -0.5f, 3000f);
            floor.transform.localScale = new Vector3(80f, 1f, 80f);
            var dummyAt = new Vector3(3000f, 0.1f, 3020f);
            dummy.transform.position = dummyAt;
            Physics.SyncTransforms();
            pc.Teleport(new Vector3(3000f, 0.1f, 3000f));
            yield return new WaitForSeconds(0.6f);

            input.Frame.AimHeld = true; // a tap
            yield return new WaitForSeconds(0.05f);
            input.Frame.AimHeld = false;
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            yield return new WaitForSeconds(0.6f);

            int shots = 0, hits = 0;
            void On(in Combatant.ShotResult s)
            {
                if (s.Phase == Combatant.ShotPhase.Launch) return;
                shots++;
                if (s.Hit == ShotHit.Combatant || s.Hit == ShotHit.Kill) hits++;
            }
            me.ShotResolved += On;
            const int N = 30;
            for (int i = 0; i < N; i++)
            {
                dummy.transform.position = dummyAt; // nothing pushed it
                pc.LookAt(dummyAt + Vector3.up * 1.1f);
                yield return null;
                input.Frame.FirePressed = input.Frame.FireHeld = true;
                yield return new WaitForSeconds(0.7f); // the view settles back, the next shot is a fresh one
                dummy.ServerHeal(100f); // 3 hits would kill it, and a dead body stops the next bullet (that was the earlier 3/30)
                Assert.AreEqual(AimMode.Sights, me.AimMode, "still in the sights");
            }
            me.ShotResolved -= On;
            UnityEngine.Object.Destroy(floor);
            Assert.AreEqual(N, shots, "every trigger pull fired");
            Assert.AreEqual(N, hits, $"{hits}/{N} hits on a still chest at 20 m in the sights");
        }
    }
}

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
    // ADR-098: a sniper bullet needs time to reach a wall 300 m away; a wall 10 m away is hit in the same frame.
    public class BallisticsPlayTests
    {
        const int Sentinel = 5;

        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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

        static GameObject Wall(Vector3 at) // a huge slab across the line of fire
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = at;
            go.transform.localScale = new Vector3(0.5f, 200f, 200f);
            return go;
        }

        [UnityTest]
        public IEnumerator SniperBullet_ToAWall300mAway_ArrivesAfterMoreThan0_3s_AndANearWallIsInstant()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Sentinel, Amount = 5 });
            yield return WaitUntil(() => me.ActiveWeaponId == Sentinel, 2f, "sniper in hand");
            yield return new WaitForSeconds(1.5f);

            // Open ground of its own, far from the test box, so only the walls below are in the way.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(3000f, -0.5f, 3000f);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
            PlayerController.Local.Teleport(new Vector3(3000f, 0.1f, 3000f));
            yield return new WaitForSeconds(0.5f);
            var eye = me.EyePosition;
            var wall = Wall(eye + Vector3.forward * 10f);
            wall.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Physics.SyncTransforms();
            var shots = new System.Collections.Generic.List<(double time, Combatant.ShotResult shot)>();
            void On(in Combatant.ShotResult s) => shots.Add((Time.timeAsDouble, s));
            me.ShotResolved += On;

            double t0 = Time.timeAsDouble;
            me.StanceSpreadFactor = 0f;
            Assert.IsTrue(me.ServerFire(Vector3.forward), "near shot fired");
            Assert.AreEqual(1, shots.Count);
            Assert.AreEqual(Combatant.ShotPhase.Whole, shots[0].shot.Phase, "within 25 m: judged at once");
            Assert.AreEqual(ShotHit.World, shots[0].shot.Hit);
            Destroy(wall);

            yield return new WaitForSeconds(1.5f);
            shots.Clear();
            wall = Wall(eye + Vector3.forward * 300f);
            wall.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Physics.SyncTransforms();
            t0 = Time.timeAsDouble;
            Assert.IsTrue(me.ServerFire(Vector3.forward), "far shot fired");
            Assert.AreEqual(1, shots.Count);
            Assert.AreEqual(Combatant.ShotPhase.Launch, shots[0].shot.Phase, "beyond 25 m: a bullet in flight");
            Assert.AreEqual(1, BulletWorld.InFlight);
            yield return WaitUntil(() => shots.Count >= 2, 3f, "the bullet landed");
            double flight = shots[1].time - t0;
            Assert.AreEqual(Combatant.ShotPhase.Impact, shots[1].shot.Phase);
            Assert.AreEqual(ShotHit.World, shots[1].shot.Hit);
            Assert.Greater(flight, 0.3, "300 m at 800 m/s");
            Assert.Less(flight, 0.6);
            Assert.AreEqual(300f, Vector3.Distance(shots[1].shot.End, eye), 3f);
            me.ShotResolved -= On;
            Destroy(wall);
            Destroy(floor);
        }

        // ADR-099: zeroed at 300 m the bullet crosses the line of sight there and hits a 0.5 m target on it; with the
        // default 100 m zero it passes under.
        [UnityTest]
        public IEnumerator Zero300_HitsA300mTarget_Zero100_Misses()
        {
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Sentinel, Amount = 5 });
            yield return WaitUntil(() => me.ActiveWeaponId == Sentinel, 2f, "sniper in hand");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(3000f, -0.5f, 3000f);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
            PlayerController.Local.Teleport(new Vector3(3000f, 0.1f, 3000f));
            yield return new WaitForSeconds(1.5f);
            var eye = me.EyePosition;
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = eye + Vector3.forward * 300f;
            target.transform.localScale = Vector3.one * 0.5f;
            Physics.SyncTransforms();
            var seen = new System.Collections.Generic.List<Combatant.ShotResult>();
            void Grab(in Combatant.ShotResult s) { if (s.Phase == Combatant.ShotPhase.Impact) seen.Add(s); }
            me.ShotResolved += Grab;
            me.StanceSpreadFactor = 0f;

            me.ZeroMeters = 100f;
            Assert.IsTrue(me.ServerFire(Vector3.forward));
            yield return new WaitForSeconds(1.0f);
            Assert.AreEqual(0, seen.Count, "zeroed at 100 m the bullet is about 0.45 m under the target at 300 m");

            yield return new WaitForSeconds(0.6f);
            me.ZeroMeters = 300f;
            me.StanceSpreadFactor = 0f;
            Assert.IsTrue(me.ServerFire(Vector3.forward), "second shot");
            yield return WaitUntil(() => seen.Count > 0, 3f, "the zeroed bullet landed");
            Assert.AreEqual(300f, Vector3.Distance(seen[0].End, eye), 1f);
            Assert.AreEqual(eye.y, seen[0].End.y, 0.25f, "on the target");
            me.ShotResolved -= Grab;
            Destroy(target);
            Destroy(floor);
        }

        static void Destroy(GameObject go) => UnityEngine.Object.Destroy(go);
    }
}

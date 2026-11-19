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
    // #415 #416 #419 (ADR-382/383/386): the waiting phase in the real World scene, as a host with its own client: the person starts on the
    // island, cannot leave it, takes no damage, can bungee, and is sent to the elevator when the countdown ends.
    public class WaitingIslandPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        bool _oldFlag;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            // A short countdown for the deployment test; the lift and the jump need the room open longer.
            DedicatedServer.StartSecondsOverride = TestContext.CurrentContext.Test.Name.Contains("Deploys") ? 12f : 120f;
            PlayerController.InputOverride = new NoPlayerInput();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 90f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || WaitingRoom.Current == null)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "world loaded and the waiting room open");
                yield return null;
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchRules.Current.UseWaitingIsland = _oldFlag;
            WaitingRoom.ForceEnabled = false;
            DedicatedServer.StartSecondsOverride = -1f;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static bool OnIsland(Vector3 p) => WaitingIsland.Inside(p.x, p.z);

        [UnityTest, Timeout(300000)]
        public IEnumerator StartsOnTheIsland_NotBlocked_AndDeploysWhenTheCountdownEnds()
        {
            var me = Combatant.LocalPlayer;
            Assert.IsTrue(OnIsland(me.transform.position), "spawned on the island: " + me.transform.position);
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(SpawnProtection.InputBlocked, "the shut elevator doors do not hold a person on the island");
            float end = Time.realtimeSinceStartup + 60f;
            while (WaitingRoom.Current != null) { Assert.Less(Time.realtimeSinceStartup, end, "the countdown ended"); yield return null; }
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(OnIsland(me.transform.position), "sent to the elevator: " + me.transform.position);
            Assert.IsTrue(SpawnProtection.InputBlocked, "in the elevator with the doors shut");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CannotLeaveTheIsland_ServerPutsThemBack()
        {
            var me = Combatant.LocalPlayer;
            // The dedicated server guards every person (all remote); the host's own body is unguarded, so the test adds the guard.
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
            yield return new WaitForSeconds(MoveGuard.Interval * 2f);
            var p = me.transform.position;
            PlayerController.Local.Teleport(new Vector3(WaitingIsland.CentreX + 70f, p.y, WaitingIsland.CentreZ));
            yield return new WaitForSeconds(1.2f);
            var q = me.transform.position;
            float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(WaitingIsland.CentreX, WaitingIsland.CentreZ));
            Assert.LessOrEqual(d, WaitingIsland.WalkRadius + 1f, "pulled back inside the walk circle, now " + d + " m from the centre");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator NothingHurts_OnTheIsland()
        {
            var me = Combatant.LocalPlayer;
            float before = me.Hp.Value;
            me.ServerTakeDamage(40f);
            yield return null;
            Assert.AreEqual(before, me.Hp.Value, 1e-3f, "a hit does nothing while waiting");
        }

        static float DeckY => GameObject.Find("WaitingIsland").transform.position.y;
        static Vector3 Local(float lx, float y, float lz) => new Vector3(WaitingIsland.WorldX(lx), y, WaitingIsland.WorldZ(lz));

        // The r5 report: the cage's floor sat inside the ground sill (and the landing pad floated over the deck), so it looked sunk below ground.
        [UnityTest, Timeout(300000)]
        public IEnumerator LiftCarFloor_RestsOnTheDeck_AboveTheSill_UnderTheFeet()
        {
            float deck = DeckY;
            var car = GameObject.Find("WaitingIsland").transform.Find("BungeeLiftCar");
            Assert.IsNotNull(car, "the World scene has the lift car");
            float end = Time.realtimeSinceStartup + BungeeLift.Cycle + 2f;
            while (BungeeLift.Phase(WaitingRoom.Current.LiftSinceOpen, out _) != LiftPhase.OpenBottom || WaitingRoom.Current.LiftSinceOpen < 1f)
            { Assert.Less(Time.realtimeSinceStartup, end, "the car came to the bottom"); yield return null; }
            var lift = WaitingIsland.Lift;
            PlayerController.Local.Teleport(Local(lift.x, deck + 0.1f, lift.z));
            yield return new WaitForSeconds(0.5f);
            var floor = car.Find("Floor").GetComponent<MeshRenderer>().bounds;
            float top = floor.max.y - deck, feet = Combatant.LocalPlayer.transform.position.y - deck;
            Debug.Log($"[WaitingIslandPlayTests] car floor {floor.min.y - deck:0.000}..{top:0.000} m over the deck, sill top {WaitingIsland.LiftSillTop:0.000}, feet {feet:0.000}");
            Assert.Greater(top, WaitingIsland.LiftSillTop + 0.03f, "the car floor shows above the sill");
            Assert.Less(top, 0.15f, "the car floor is a low step, not a box");
            Assert.GreaterOrEqual(floor.min.y - deck, -0.05f, "nothing of the car floor is below the deck");
            Assert.Less(Mathf.Abs(feet - top), 0.15f, "the rider stands on the car floor, not sunk in it");
        }

        // Review shots (third person, the feet, the car floor and the deck together). Run on purpose with
        //   ISLAND_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.WaitingIslandPlayTests.CaptureLiftAndPad
        [UnityTest, Explicit, Category("Capture"), Timeout(300000)]
        public IEnumerator CaptureLiftAndPad()
        {
            string folder = System.Environment.GetEnvironmentVariable("ISLAND_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = System.IO.Path.Combine(Application.dataPath, "../Logs/island-shots");
            System.IO.Directory.CreateDirectory(folder);
            float deck = DeckY;
            float end = Time.realtimeSinceStartup + BungeeLift.Cycle + 2f;
            while (BungeeLift.Phase(WaitingRoom.Current.LiftSinceOpen, out _) != LiftPhase.OpenBottom || WaitingRoom.Current.LiftSinceOpen < 1f)
            { Assert.Less(Time.realtimeSinceStartup, end, "the car came to the bottom"); yield return null; }
            var cam = new GameObject("IslandShotCamera").AddComponent<Camera>();
            cam.enabled = false;
            if (PlayerController.Local.Camera != null) cam.CopyFrom(PlayerController.Local.Camera);
            cam.fieldOfView = 55f;
            var lift = WaitingIsland.Lift; var pad = WaitingIsland.BungeePad;
            var shots = new (string name, Vector3 at, Vector3 eye)[]
            {
                ("lift_car_bottom", Local(lift.x, deck + 0.1f, lift.z), Local(lift.x - 3.2f, deck + 1.9f, lift.z + 4.2f)),
                ("lift_car_low", Local(lift.x, deck + 0.1f, lift.z), Local(lift.x - 2.6f, deck + 0.6f, lift.z + 3.0f)),
                ("bungee_pad", Local(pad.x, deck + 0.1f, pad.z), Local(pad.x - 3.5f, deck + 1.8f, pad.z + 3.5f)),
            };
            foreach (var s in shots)
            {
                PlayerController.Local.Teleport(s.at);
                yield return new WaitForSeconds(0.8f);
                var look = Combatant.LocalPlayer.transform.position + Vector3.up * 0.5f;
                cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation(look - s.eye));
                const int w = 1280, h = 720;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                var active = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, s.name + ".png"), tex.EncodeToPNG());
            }
            Object.Destroy(cam.gameObject);
            Debug.Log("[WaitingIslandPlayTests] shots in " + folder);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Lift_GoesUpFast_AndBackDown_WithoutAGuardCorrection()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value, deck = DeckY;
            var lift = WaitingIsland.Lift;
            PlayerController.Local.Teleport(Local(lift.x, deck + 0.1f, lift.z));
            yield return null;
            // The guard as on the dedicated server, added after the test's own teleport (a host does not announce it).
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
            int corrections = MoveGuard.Corrections;
            float end = Time.realtimeSinceStartup + BungeeLift.Cycle + 6f, left = -1f, arrived = -1f;
            while (arrived < 0f)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "the lift took them up within one cycle, now at +" + (me.transform.position.y - deck) + " m");
                float h = me.transform.position.y - deck;
                if (left < 0f && h > 0.8f) left = Time.realtimeSinceStartup;
                if (h > BungeeCurve.Top - 0.5f) arrived = Time.realtimeSinceStartup;
                yield return null;
            }
            Assert.Less(arrived - left, BungeeLift.TravelSeconds + 0.6f, "an express: 60 m in about five seconds");
            yield return new WaitForSeconds(1f);
            Assert.Greater(me.transform.position.y - deck, BungeeCurve.Top - 0.5f, "stands on the top landing after the ride");
            // Staying in the car: the next trip takes them down again.
            end = Time.realtimeSinceStartup + BungeeLift.Cycle + 6f;
            while (me.transform.position.y - deck > 1.5f) { Assert.Less(Time.realtimeSinceStartup, end, "the lift took them down"); yield return null; }
            Assert.AreEqual(corrections, MoveGuard.Corrections, "the guard did not take the 15 m/s ride for a cheat");
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Jump_FromTheBoard_FallsBouncesAndLandsOnThePad_Unhurt()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value, deck = DeckY;
            var pad = WaitingIsland.BungeePad;
            PlayerController.Local.Teleport(Local(pad.x, deck + BungeeCurve.Top + 0.1f, pad.z));
            yield return null;
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
            int corrections = MoveGuard.Corrections;
            float low = float.MaxValue, end = Time.realtimeSinceStartup + 2f + BungeeCurve.Duration + 3f, fellAt = -1f;
            bool fell = false;
            while (Time.realtimeSinceStartup < end)
            {
                float h = me.transform.position.y - deck;
                if (h < BungeeCurve.Top - 10f && !fell) { fell = true; fellAt = Time.realtimeSinceStartup; }
                if (fell && Time.realtimeSinceStartup - fellAt < BungeeCurve.BouncePeriod) low = Mathf.Min(low, h);   // the first dip, before the lowering
                yield return null;
            }
            Assert.IsTrue(fell, "jumped after standing on the board");
            Assert.That(low, Is.InRange(1.5f, 6f), "the lowest point of the bounce is a few metres over the pad");
            var p = me.transform.position;
            Assert.Less(p.y - deck, 1.5f, "back on the deck");
            Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(WaitingIsland.WorldX(pad.x), WaitingIsland.WorldZ(pad.z))), 2f, "on the landing pad");
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f, "no damage from the jump");
            Assert.AreEqual(corrections, MoveGuard.Corrections, "the guard did not take the fall for a cheat");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FallingFromTheTower_IsPutBackOnTheDeck_Unhurt()
        {
            var me = Combatant.LocalPlayer;
            float hp = me.Hp.Value, deck = DeckY;
            var tw = WaitingIsland.Tower;
            PlayerController.Local.Teleport(Local(tw.x - 8f, deck + 30f, tw.z - 8f));   // in the air beside the tower, not on its top floor
            yield return new WaitForSeconds(0.6f);
            Assert.Less(me.transform.position.y - deck, 2.5f, "put back on the deck at once, now +" + (me.transform.position.y - deck) + " m");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f);
        }
    }
}

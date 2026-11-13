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
    // ADR-076 (#76) in the World scene as a host: after the deployment the player holds F in its station, waits
    // out the ride in the dark and steps out in another region's station; the elevators of a locked region stop
    // and a destination announced during the ride sends the car elsewhere.
    //   ELEV_SHOT=<file.png> also saves one HUD screenshot of the panel (with a region in warning).
    public class ElevatorTravelPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        ScriptedInput _input;
        DeploymentRules _savedRules;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            BotRules.Current = new BotRules { TargetsPlayers = false };
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "local player in World");
            _savedRules = DeploymentRules.Current;
            DeploymentRules.Current = new DeploymentRules { TravelSeconds = 2f, TravelHoldSeconds = 1f };
            Assert.IsNotNull(ElevatorRides.Instance, "World should have elevator rides next to its doors");
            yield return WaitUntil(() => !SpawnProtection.InputBlocked, 10f, "the deployment doors to open");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            if (_savedRules != null) DeploymentRules.Current = _savedRules;
            BotRules.Current = new BotRules();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Vector3 Pos => Combatant.LocalPlayer.transform.position;

        IEnumerator HoldF(float seconds)
        {
            _input.Frame.Interact = true;
            _input.Frame.InteractHeld = true;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
            _input.Frame.InteractHeld = false;
            yield return null;
        }

        static int StationOfRegion(int region, int not = -1)
        {
            var sites = ElevatorDoors.Instance.Sites;
            for (int i = 0; i < sites.Count; i++) if (sites[i].Region == region && i != not) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator HoldF_InTheStation_RidesToAnotherRegion_ThenCoolsDown()
        {
            var rides = ElevatorRides.Instance;
            int start = rides.StationAt(Pos);
            Assert.GreaterOrEqual(start, 0, "the player should still stand in its deployment station");
            int startRegion = rides.RegionOfStation(start);
            yield return null;
            Assert.IsTrue(Hud.Instance.Elevator.Shown, "the elevator panel should show in a station");
            StringAssert.Contains("F 길게 출발", Hud.Instance.Elevator.FooterText);
            int chosen = ElevatorPanel.Selected;
            Assert.GreaterOrEqual(chosen, 0);
            Assert.AreNotEqual(startRegion, chosen);

            // A tap changes the destination (World has 4 regions: 3 choices from any of them).
            _input.Frame.Interact = true;
            _input.Frame.InteractHeld = true;
            yield return null;
            _input.Frame.InteractHeld = false;
            yield return null;
            yield return null;
            Assert.AreNotEqual(chosen, ElevatorPanel.Selected, "a tap should pick the next destination");
            Assert.IsFalse(rides.LocalRiding, "a tap should not ride");
            chosen = ElevatorPanel.Selected;

            yield return HoldF(1.6f);
            yield return WaitUntil(() => rides.LocalRiding, 3f, "the ride to start");
            Assert.AreEqual(chosen, rides.LocalDestination);
            yield return null;
            yield return null;
            Assert.IsTrue(Hud.Instance.Elevator.TransitShown, "the screen goes dark during the ride");
            StringAssert.Contains("엘리베이터 이동 중", Hud.Instance.Elevator.TransitText);
            Assert.Less(Pos.y, ElevatorDoors.Instance.Sites[start].Spawn.y - 8f, "the rider waits down the shaft");

            yield return WaitUntil(() => !rides.LocalRiding, 5f, "the arrival");
            yield return null;
            yield return null;
            int arrived = rides.StationAt(Pos);
            Assert.GreaterOrEqual(arrived, 0, "the rider should stand in a station after the ride, at " + Pos);
            Assert.AreEqual(chosen, rides.RegionOfStation(arrived), "the rider should come out in the chosen region");
            Assert.IsFalse(Hud.Instance.Elevator.TransitShown);
            Assert.Greater(rides.LocalCooldownLeft, 50f);
            StringAssert.Contains("재사용 대기", Hud.Instance.Elevator.FooterText);

            // Cooling down: holding F again does nothing.
            yield return HoldF(1.6f);
            yield return null;
            Assert.IsFalse(rides.LocalRiding || rides.LocalPending, "no second ride during the cooldown");
        }

        [UnityTest]
        public IEnumerator Lockdown_StopsTheRegionsElevators_AndRedirectsARideIntoIt()
        {
            var rides = ElevatorRides.Instance;
            var zones = ZoneDirector.Instance;
            Assert.IsNotNull(zones);
            var player = Combatant.LocalPlayer;
            int start = rides.StationAt(Pos);
            int startRegion = rides.RegionOfStation(start);
            // The first region to close is not the player's: the player rides into it just before it is announced.
            var rules = new LockdownRules
            {
                FirstWarningSeconds = 1f, WarningSeconds = 1.5f, LockdownSeconds = 60f, BombardmentSeconds = 60f,
                MinSecondsBetweenClosings = 1000f, RadiationDps = 0.5f, RadiationGraceSeconds = 5f, KeysPerRegion = 0,
                SubZoneStartDelaySeconds = 1000f,
            };
            var g = zones.Geometry;
            int seed = 0;
            for (int s = 1; s < 2000 && seed == 0; s++)
                if (Lockdown.Plan(g.RegionCount, g.PassA, g.PassB, s, g.SubZoneCounts(), rules).Order[0] != startRegion) seed = s;
            zones.ServerConfigure(rules, seed);
            int doomed = zones.ServerPlan.Order[0];
            DeploymentRules.Current.TravelSeconds = 2.5f;

            Assert.IsTrue(rides.ServerTryStart(player, start, doomed), "safe -> safe should ride");
            yield return WaitUntil(() => rides.LocalRiding, 3f, "the ride to start");
            yield return WaitUntil(() => !rides.LocalRiding, 6f, "the arrival");
            yield return null;
            yield return null;
            Assert.AreNotEqual(ZoneState.Safe, zones.StateOf(doomed), "the destination should have been announced during the ride");
            int arrived = rides.StationAt(Pos);
            Assert.GreaterOrEqual(arrived, 0, "the rider should stand in a station after the ride");
            int region = rides.RegionOfStation(arrived);
            Assert.AreNotEqual(doomed, region, "the car should not come out in an announced region");
            Assert.AreEqual(ZoneState.Safe, zones.StateOf(region));

            // The panel now lists the announced region as out of reach (the screenshot is taken here).
            yield return null;
            Assert.IsTrue(Hud.Instance.Elevator.Shown);
            StringAssert.Contains("중지", Hud.Instance.Elevator.RowText(doomed));
            yield return Screenshot();

            // Locked: its stations stop, for players and for the server.
            yield return WaitUntil(() => zones.StateOf(doomed) == ZoneState.Lockdown, 5f, "the lockdown");
            int lockedStation = StationOfRegion(doomed);
            var site = ElevatorDoors.Instance.Sites[lockedStation];
            PlayerController.Local.Teleport(site.Spawn + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            Assert.IsTrue(Hud.Instance.Elevator.Shown);
            StringAssert.Contains("운행 중지", Hud.Instance.Elevator.FooterText);
            // No cooldown in the way: only the lockdown refuses the ride.
            DeploymentRules.Current.TravelCooldownSeconds = 0f;
            Assert.IsFalse(rides.ServerTryStart(player, lockedStation, region), "a locked region's elevator is off");
        }

        // One HUD frame to ELEV_SHOT: the HUD canvas is drawn by the main camera for a moment.
        static IEnumerator Screenshot()
        {
            string path = Environment.GetEnvironmentVariable("ELEV_SHOT");
            if (string.IsNullOrEmpty(path)) yield break;
            // No WaitForEndOfFrame: it never comes back in batchmode. The camera renders into a texture right here.
            yield return null;
            var cam = Camera.main;
            var canvas = Hud.Instance.GetComponentInChildren<Canvas>();
            if (canvas == null) canvas = Hud.Instance.GetComponent<Canvas>();
            Assert.IsNotNull(cam);
            Assert.IsNotNull(canvas);
            var mode = canvas.renderMode;
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.05f;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            cam.targetTexture = null;
            canvas.renderMode = mode;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}

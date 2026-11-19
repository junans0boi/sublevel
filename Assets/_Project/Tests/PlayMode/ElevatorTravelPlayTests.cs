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
    // ADR-076 (#76) in the World scene as a host: after the deployment the player holds F in its station, rides
    // the car down and another one up (ADR-079) and steps out in another region's station; the elevators of a locked region stop
    // and a destination announced during the ride sends the car elsewhere.
    //   ELEV_SHOT=<file.png> also saves one screenshot from inside the arrival car on its way up (ADR-079).
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
            PollutionDirector.AllowFreeRides = true; // these test the old free ride
            BotRules.Current = new BotRules { TargetsPlayers = false };
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "local player in World");
            _savedRules = DeploymentRules.Current;
            DeploymentRules.Current = new DeploymentRules { TravelSeconds = 6f, TravelHoldSeconds = 1f };
            Assert.IsNotNull(ElevatorRides.Instance, "World should have elevator rides next to its doors");
            yield return WaitUntil(() => !SpawnProtection.InputBlocked, 10f, "the deployment doors to open");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            PollutionDirector.AllowFreeRides = false;
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

        // A car other than `not`, moving up, with the local player in its shaft.
        static ElevatorCar RisingCarUnderPlayer(int not)
        {
            foreach (var car in ElevatorCar.All)
            {
                if (car == null || car.Station == not || car.Phase != ElevatorPhase.Rising) continue;
                var p = car.transform.position;
                if (new Vector2(Pos.x - p.x, Pos.z - p.z).magnitude < 1.7f) return car;
            }
            return null;
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
            Assert.IsTrue(Hud.Instance.Elevator.TransitShown, "a band shows the ride");
            StringAssert.Contains("엘리베이터 이동 중", Hud.Instance.Elevator.TransitText);
            // The real ride (ADR-079): the station door shuts, the car goes down with the rider in it, hums.
            var startTop = ElevatorDoors.Instance.Sites[start].Spawn;
            var startCar = ElevatorDoors.Instance.CarOf(start);
            Assert.IsNotNull(startCar, "World stations have cars");
            yield return WaitUntil(() => ElevatorDoors.Instance.Doors[start].Blocking, 2f, "the station door to shut");
            yield return WaitUntil(() => Pos.y < startTop.y - 8f, 5f, "the car to take the rider down");
            Assert.Less(Mathf.Abs(Pos.y - (startCar.transform.position.y + ElevatorCar.RideLift)), 0.6f, "the rider stands on the car floor");
            LogPose("descending");
            var audio = startCar.GetComponent<ElevatorAudio>();
            if (audio != null) Assert.Greater(audio.HumsStarted, 0, "the car hums on its way");

            // Changed cars at the bottom: the arrival car comes up with the rider; its doors open at the top.
            yield return WaitUntil(() => RisingCarUnderPlayer(start) != null, 8f, "the arrival car to rise with the rider");
            var arrivalCar = RisingCarUnderPlayer(start);
            Assert.Less(Mathf.Abs(Pos.y - (arrivalCar.transform.position.y + ElevatorCar.RideLift)), 0.6f, "on the arrival car floor");
            Assert.IsTrue(ElevatorDoors.Instance.Doors[arrivalCar.Station].Blocking, "the arrival doors are shut while it rises");
            yield return Screenshot();
            // Standing on the car, not in the jump pose (the probe under the feet sees no ground down a shaft).
            var motion = Combatant.LocalPlayer.GetComponentInChildren<CombatantMotion>();
            if (motion != null) Assert.IsFalse(motion.Airborne, "a rider is not in the air");
            yield return WaitUntil(() => !rides.LocalRiding, 10f, "the arrival");
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
        public IEnumerator Confirmation_StopsTheRegionsElevators_AndRedirectsARideIntoIt()
        {
            var rides = ElevatorRides.Instance;
            var zones = PollutionDirector.Instance;
            Assert.IsNotNull(zones);
            var player = Combatant.LocalPlayer;
            int start = rides.StationAt(Pos);
            int startRegion = rides.RegionOfStation(start);
            // The first region to close is not the player's: the player rides into it just before it is announced.
            var rules = PollutionDirectorTests.Timed(2.5f, 1.5f);
            PollutionDirectorTests.ConfigureFirst(zones, rules, startRegion, false);
            int doomed = zones.Match.Timeline.Order[0];

            Assert.IsTrue(rides.ServerTryStart(player, start, doomed), "safe -> safe should ride");
            yield return WaitUntil(() => rides.LocalRiding, 3f, "the ride to start");
            yield return WaitUntil(() => !rides.LocalRiding, 10f, "the arrival");
            yield return null;
            yield return null;
            Assert.AreNotEqual(ZoneState.Safe, zones.StateOf(doomed), "the destination should have been announced during the ride");
            int arrived = rides.StationAt(Pos);
            Assert.GreaterOrEqual(arrived, 0, "the rider should stand in a station after the ride");
            int region = rides.RegionOfStation(arrived);
            Assert.AreNotEqual(doomed, region, "the car should not come out in an announced region");
            Assert.AreEqual(ZoneState.Safe, zones.StateOf(region));

            // The panel now lists the announced region as out of reach.
            yield return null;
            Assert.IsTrue(Hud.Instance.Elevator.Shown, "the panel after the redirected arrival");
            StringAssert.Contains("중지", Hud.Instance.Elevator.RowText(doomed));

            // Locked: its stations stop, for players and for the server.
            yield return WaitUntil(() => zones.StateOf(doomed) == ZoneState.Closed, 5f, "the confirmation");
            // Bots rode out of the announced region (ADR-079): wait for a station whose car is back up.
            int lockedStation = -1;
            yield return WaitUntil(() =>
            {
                var all = ElevatorDoors.Instance.Sites;
                for (int i = 0; i < all.Count; i++) if (all[i].Region == doomed && !rides.StationBusy(i)) { lockedStation = i; return true; }
                return false;
            }, 12f, "a station of the locked region at rest");
            var site = ElevatorDoors.Instance.Sites[lockedStation];
            PlayerController.Local.Teleport(site.Spawn + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            Assert.IsTrue(Hud.Instance.Elevator.Shown, "the panel in the locked station");
            StringAssert.Contains("운행 중지", Hud.Instance.Elevator.FooterText);
            // No cooldown in the way: only the lockdown refuses the ride.
            DeploymentRules.Current.TravelCooldownSeconds = 0f;
            Assert.IsFalse(rides.ServerTryStart(player, lockedStation, region), "a locked region's elevator is off");
        }

        // Server authority with two riders (ADR-079): a bot rides on its own decision while the player rides, the
        // server carries the bot and wakes it in the other region; a request for a station the player is not in
        // comes back refused through the real client -> server broadcast.
        // Not a remote client: the game has no join path yet (host only), so the second rider is a bot.
        [UnityTest]
        public IEnumerator Bot_RidesOutOfAnAnnouncedRegion_AlongsideThePlayer_ServerDecides()
        {
            var rides = ElevatorRides.Instance;
            var zones = PollutionDirector.Instance;
            var player = Combatant.LocalPlayer;
            int playerStation = rides.StationAt(Pos);
            int playerRegion = rides.RegionOfStation(playerStation);

            // A forged request: a station far from the player. The server refuses; nothing moves.
            int elsewhere = StationOfRegion(playerRegion, playerStation);
            if (elsewhere < 0) elsewhere = StationOfRegion((playerRegion + 1) % zones.RegionCount);
            Assert.IsTrue(rides.RequestRide(elsewhere, (playerRegion + 1) % zones.RegionCount));
            yield return WaitUntil(() => !rides.LocalPending, 3f, "the server's answer");
            Assert.IsFalse(rides.LocalRiding, "a request from outside the station is refused");
            Assert.IsFalse(rides.ServerRiding(player));

            // The bot's region closes first; the bot stands in one of its stations, nearer than any pass.
            var rules = PollutionDirectorTests.Timed(30f, 29f);
            PollutionDirectorTests.ConfigureFirst(zones, rules, playerRegion, false);
            int doomed = zones.Match.Timeline.Order[0];
            int botStation = StationOfRegion(doomed);
            Combatant bot = null;
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && !c.Dead.Value) { bot = c; break; }
            Assert.IsNotNull(bot, "World has bots");
            var botSite = ElevatorDoors.Instance.Sites[botStation].Spawn;
            // Where the navmesh reaches in that station, as MatchDirector places bots.
            Assert.IsTrue(UnityEngine.AI.NavMesh.SamplePosition(botSite, out var onMesh, 2.5f, UnityEngine.AI.NavMesh.AllAreas), "a station floor on the navmesh");
            var agent = bot.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled) Assert.IsTrue(agent.Warp(onMesh.position));
            else bot.transform.position = onMesh.position;
            var brain = bot.GetComponent("BotController") as Behaviour;
            Assert.IsNotNull(brain);

            float until = Time.realtimeSinceStartup + 8f;
            while (!rides.ServerRiding(bot))
            {
                if (Time.realtimeSinceStartup > until)
                {
                    var bp = bot.transform.position;
                    int reg = zones.RegionIndexAt(bp);
                    zones.TryEscapeTarget(bp, -1, out var exit, out _, out _);
                    Assert.Fail("the bot never took the elevator: at " + bp + " region " + reg + " (" + zones.StateOf(reg) + "), station "
                        + botStation + " region " + rides.RegionOfStation(botStation) + " at " + botSite + ", flat "
                        + new Vector2(bp.x - botSite.x, bp.z - botSite.z).magnitude + ", exit " + exit + ", brain " + brain.enabled
                        + ", busy " + rides.StationBusy(botStation) + ", cooldown " + rides.ServerCooldownLeft(bot)
                        + ", goal " + brain.GetType().GetProperty("Goal").GetValue(brain) + ", dead " + bot.Dead.Value
                        + (agent != null ? ", agent on " + agent.enabled + "/" + agent.isOnNavMesh + " stopped " + (agent.isOnNavMesh && agent.isStopped)
                            + " dest " + agent.destination + " rem " + (agent.isOnNavMesh ? agent.remainingDistance : -1f) + " path " + agent.pathStatus
                            + " vel " + agent.velocity + " speed " + agent.speed : ", no agent"));
                }
                yield return null;
            }
            yield return null; // paused by the rides' own update, not inside the brain's
            yield return null;
            Assert.IsFalse(brain.enabled, "the bot's brain rests during the ride");
            // A bystander bot stands in the player's car (its brain off so it stays for the test): it goes down and
            // back up with the empty departure car (ADR-080) and is woken afterwards.
            Combatant bystander = null;
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && !c.Dead.Value && c != bot) { bystander = c; break; }
            Assert.IsNotNull(bystander);
            var bystanderBrain = bystander.GetComponent("BotController") as Behaviour;
            var playerTop = ElevatorDoors.Instance.Sites[playerStation].Spawn;
            var side = Quaternion.Euler(0f, ElevatorDoors.Instance.Sites[playerStation].YawDeg, 0f) * Vector3.right * 0.8f;
            var bystanderAgent = bystander.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (bystanderAgent != null) bystanderAgent.enabled = false;
            bystanderBrain.enabled = false;
            bystander.transform.position = playerTop + side + Vector3.up * ElevatorCar.RideLift;

            // The player rides while the bot is on its way, to a third region.
            int playerTo = -1;
            for (int r = 0; r < zones.RegionCount && playerTo < 0; r++) if (r != playerRegion && r != doomed) playerTo = r;
            Assert.IsTrue(rides.ServerTryStart(player, playerStation, playerTo));
            float lowest = float.MaxValue;
            float watchUntil = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < watchUntil && lowest >= playerTop.y - 8f)
            {
                lowest = Mathf.Min(lowest, bystander.transform.position.y);
                yield return null;
            }
            Assert.Less(lowest, playerTop.y - 8f, "the bystander goes down with the car");

            Assert.IsTrue(rides.ServerRiding(player) && rides.ServerRiding(bot), "two riders at once");
            yield return WaitUntil(() => !rides.ServerRiding(bot) && !rides.ServerRiding(player), 12f, "both rides to end");
            yield return null;
            yield return null;
            int botArrived = rides.StationAt(bot.transform.position);
            Assert.GreaterOrEqual(botArrived, 0, "the bot comes out in a station, at " + bot.transform.position);
            Assert.AreNotEqual(doomed, rides.RegionOfStation(botArrived), "out of the announced region");
            Assert.IsTrue(brain.enabled, "the bot is awake again");
            Assert.Greater(rides.ServerCooldownLeft(bot), 0f);
            yield return WaitUntil(() => !rides.StationBusy(playerStation), 10f, "the departure car back up and open");
            yield return null;
            yield return null;
            Assert.Less(Mathf.Abs(bystander.transform.position.y - playerTop.y), 0.5f, "the bystander is back up at the station floor");
            Assert.IsTrue(bystanderBrain.enabled, "the bystander is woken when the car stops");
            int playerArrived = rides.StationAt(Pos);
            Assert.GreaterOrEqual(playerArrived, 0);
            Assert.AreEqual(playerTo, rides.RegionOfStation(playerArrived));
        }

        // One HUD frame to ELEV_SHOT: the HUD canvas is drawn by the main camera for a moment.
        static void LogPose(string when)
        {
            var me = Combatant.LocalPlayer;
            var anim = me.GetComponentInChildren<CombatantAnimation>();
            var motion = me.GetComponentInChildren<CombatantMotion>();
            var animator = me.GetComponentInChildren<Animator>();
            Debug.Log("[ElevPose] " + when + " animator " + (animator != null ? animator.isActiveAndEnabled + " state " + animator.GetCurrentAnimatorStateInfo(0).shortNameHash
                + " air? " + animator.GetCurrentAnimatorStateInfo(0).IsName("Air") + " loco? " + animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") : "none")
                + " seated " + (anim != null ? anim.SeatedPose : -1f) + " tuck " + (anim != null ? anim.JumpTuck : -1f) + " dip " + (anim != null ? anim.LandDip : -1f)
                + " airborne " + (motion != null && motion.Airborne) + " vy " + (motion != null ? motion.VerticalSpeed : 0f) + " carried " + ElevatorCar.Carries(me.transform.position));
        }

        static IEnumerator Screenshot()
        {
            LogPose("shot");
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

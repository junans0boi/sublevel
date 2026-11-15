using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Managing;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #164: the world's state seen from a REMOTE client, and by a client that joins late (docs/MP_AUDIT_WORLD.md).
    // The World scene runs on the host; the late client is a second NetworkManager in the same process, so what it
    // receives is read two ways: broadcast listeners registered before it connects (the wire), and a second director
    // bound to its manager (the client's own logic).
    public class ReplicationWorldTests
    {
        const string LogDir = "Logs/net-replication/world";
        MultiHarness _net;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        BotRules _bots;

        void Log(string line)
        {
            Debug.Log(line);
            _log.AppendLine(line);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            _bots = BotRules.Current;
            BotRules.Current = new BotRules { TargetsPlayers = false };
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("World");
            yield return NetHarness.WaitUntil(() => FindDirector() != null && FindDirector().ServerReady, "zone director ready", Log, 30f);
            _net.HostPlayer.ServerDamageFilter = _ => 0f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            System.IO.Directory.CreateDirectory(LogDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            BotRules.Current = _bots;
            EnvironmentView.ForceTime = null;
            EnvironmentView.ForceWeather = null;
            EnvironmentView.ForceSeed = null;
            SupplyRules.Current = new SupplyRules();
            SmokeZones.Clear();
            yield return _net.Teardown();
        }

        static ZoneDirector FindDirector() => Object.FindFirstObjectByType<ZoneDirector>();

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        // A second singleton of a kind must not replace the host's in the static `Instance` the scene code reads.
        static void KeepInstance<T>(T original) where T : Component =>
            typeof(T).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetSetMethod(true).Invoke(null, new object[] { original });

        sealed class Heard
        {
            public readonly List<SkyState> Sky = new List<SkyState>();
            public readonly List<ZoneUpdate> Zone = new List<ZoneUpdate>();
            public readonly List<ElevatorDoorStatus> Elevator = new List<ElevatorDoorStatus>();
            public readonly List<DoorChanged> Doors = new List<DoorChanged>();
            public readonly List<GlassShattered> Glass = new List<GlassShattered>();
            public readonly List<SupplyDropped> Supply = new List<SupplyDropped>();
            public readonly List<MatchStatus> Match = new List<MatchStatus>();
            public SupplyDirector SupplyOnClient;
            public SkySync SkyOnClient;
        }

        // What the late client's side holds: listeners for every world broadcast, and its own sky and supply logic.
        static Heard Listen(NetworkManager manager, SupplyDirector hostSupply, SkySync hostSky)
        {
            var h = new Heard();
            manager.ClientManager.RegisterBroadcast<SkyState>((m, c) => h.Sky.Add(m));
            manager.ClientManager.RegisterBroadcast<ZoneUpdate>((m, c) => h.Zone.Add(m));
            manager.ClientManager.RegisterBroadcast<ElevatorDoorStatus>((m, c) => h.Elevator.Add(m));
            manager.ClientManager.RegisterBroadcast<DoorChanged>((m, c) => h.Doors.Add(m));
            manager.ClientManager.RegisterBroadcast<GlassShattered>((m, c) => h.Glass.Add(m));
            manager.ClientManager.RegisterBroadcast<SupplyDropped>((m, c) => h.Supply.Add(m));
            manager.ClientManager.RegisterBroadcast<MatchStatus>((m, c) => h.Match.Add(m));
            var skyGo = new GameObject("ClientSky");
            h.SkyOnClient = skyGo.AddComponent<SkySync>();
            h.SkyOnClient.Bind(manager);
            KeepInstance(hostSky);
            var supplyGo = new GameObject("ClientSupply");
            h.SupplyOnClient = supplyGo.AddComponent<SupplyDirector>();
            h.SupplyOnClient.Bind(manager);
            KeepInstance(hostSupply);
            return h;
        }

        [UnityTest]
        public IEnumerator Sky_IsPickedByTheServer_AndEveryClientDerivesTheSameHourAndWeather()
        {
            var sky = SkySync.Instance;
            Assert.IsNotNull(sky, "the match director adds the sky sync");
            Assert.IsTrue(sky.Known, "the server picked at start");
            var view = EnvironmentView.Current;
            yield return NetHarness.WaitUntil(() => view != null || (view = EnvironmentView.Current) != null, "environment view", Log, 10f);
            yield return Seconds(0.6f);
            Assert.AreEqual(sky.Kind, view.Weather, "the host's own view uses the server's weather");
            Assert.AreEqual(sky.HourNow, view.Hour, 0.05f, "and the server's hour");

            Heard late = null;
            yield return _net.AddClient(null, beforeConnect: nm => late = Listen(nm, SupplyDirector.Instance, sky));
            yield return NetHarness.WaitUntil(() => late.SkyOnClient.Known, "the client got the sky", Log, 10f);
            var client = late.SkyOnClient;
            Assert.AreEqual(sky.Seed, client.Seed);
            Assert.AreEqual(sky.TimeChoice, client.TimeChoice);
            Assert.AreEqual(sky.WeatherChoice, client.WeatherChoice);
            Assert.AreEqual(sky.Kind, client.Kind, "same weather");
            Assert.AreEqual(sky.HourNow, client.HourNow, 0.02f, "same hour now (the clock is derived from the same start)");
            Assert.AreEqual(1, late.Sky.Count);
            yield return Seconds(1f);
            Assert.AreEqual(sky.HourNow, client.HourNow, 0.02f, "and it keeps agreeing");
            KeepInstance(sky);
        }

        [UnityTest]
        public IEnumerator FixedChoice_OnTheServerReachesTheClient_WhateverItsOwnSettingsSay()
        {
            // The host's settings are what count: here a fixed night with rain on the server.
            yield return _net.Teardown();
            EnvironmentView.ForceTime = DayTimeChoice.Night;
            EnvironmentView.ForceWeather = WeatherChoice.Rain;
            EnvironmentView.ForceSeed = 777;
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("World");
            var sky = SkySync.Instance;
            Assert.IsTrue(sky.Known);
            Assert.AreEqual(DayTimeChoice.Night, sky.TimeChoice);
            Assert.AreEqual(WeatherKind.Rain, sky.Kind);
            Assert.AreEqual(WorldSky.NightHour, sky.HourNow, 1e-3f, "a chosen time of day stands still");
            // A client with other settings of its own (ForceTime cleared) still gets the server's.
            EnvironmentView.ForceTime = DayTimeChoice.Day;
            Heard late = null;
            yield return _net.AddClient(null, beforeConnect: nm => late = Listen(nm, SupplyDirector.Instance, sky));
            yield return NetHarness.WaitUntil(() => late.SkyOnClient.Known, "the client got the sky", Log, 10f);
            Assert.AreEqual(777, late.SkyOnClient.Seed);
            Assert.AreEqual(WeatherKind.Rain, late.SkyOnClient.Kind);
            Assert.AreEqual(WorldSky.NightHour, late.SkyOnClient.HourNow, 1e-3f);
            KeepInstance(sky);
        }

        [UnityTest]
        public IEnumerator LateJoiner_ReceivesZones_Elevators_Doors_Glass_Supply_AndTheMatchStatus()
        {
            var director = FindDirector();
            var player = _net.HostPlayer;
            // A short lockdown, the first region to close not the player's; a crate comes down at stage 1.
            SupplyRules.Current = new SupplyRules { FirstStage = 1, EveryStages = 1, DescentSeconds = 4f, OpenSeconds = 0.6f, SmokeSeconds = 60f };
            int mine = director.RegionIndexAt(player.transform.position);
            var rules = SupplyPlayTests.Fast();
            var g = director.Geometry;
            int seed = 0;
            for (int s = 1; s < 2000 && seed == 0; s++)
                if (Lockdown.Plan(g.RegionCount, g.PassA, g.PassB, s, g.SubZoneCounts(), rules).Order[0] != mine) seed = s;
            director.ServerConfigure(rules, seed);
            var supply = SupplyDirector.Instance;
            yield return NetHarness.WaitUntil(() => supply != null && supply.Crates.Count == 1, "the stage-1 crate on the server", Log, 20f);
            var crate = supply.Crates[0];

            // A door opened and a window broken before anyone else is there.
            var doors = DoorSystem.Instance;
            Assert.Greater(doors.Count, 0, "the map has doors");
            doors.ServerSet(0, DoorState.OpenAhead);
            GlassPane pane = Object.FindFirstObjectByType<GlassPane>();
            Assert.IsNotNull(pane, "the map has window glass");
            GlassPanes.ServerBreak(pane, 0);
            yield return Seconds(2.5f);

            Heard late = null;
            yield return _net.AddClient(null, beforeConnect: nm => late = Listen(nm, supply, SkySync.Instance));
            yield return NetHarness.WaitUntil(() => late.SkyOnClient.Known && late.Zone.Count > 0 && late.Doors.Count > 0 && late.Glass.Count > 0 && late.Supply.Count > 0 && late.Elevator.Count > 0, "the late client was told everything", Log, 15f);

            // Zones: the closing region's state with its time, as the server holds it.
            ZoneUpdate region = default;
            foreach (var z in late.Zone) if (z.Kind == ZoneUpdate.KindRegion) region = z;
            Assert.AreNotEqual((byte)ZoneState.Safe, region.State, "a region that is closing is told as closing");
            Assert.AreEqual(director.StateOf(region.Region), (ZoneState)region.State);
            // Doors and glass arrive quietly (nothing to hear for something that happened long ago).
            Assert.AreEqual(0, late.Doors[0].Index);
            Assert.AreEqual((byte)DoorState.OpenAhead, late.Doors[0].State);
            Assert.IsTrue(late.Doors[0].Quiet);
            Assert.AreEqual(pane.Id, late.Glass[0].Id);
            Assert.IsTrue(late.Glass[0].Quiet);
            // The deployment doors: scheduled, with the countdown left (or already open: nothing negative needed).
            Assert.IsTrue(late.Elevator[0].Scheduled);
            // The crate: the client's own director holds it, with its time already run.
            yield return NetHarness.WaitUntil(() => late.SupplyOnClient.Crates.Count == 1, "the client's supply director has the crate", Log, 5f);
            var seen = late.SupplyOnClient.Crates[0];
            Assert.AreEqual(crate.Id, seen.Id);
            Assert.AreEqual(crate.Region, seen.Region);
            Assert.AreEqual(crate.Seed, seen.Seed, "same contents");
            Assert.AreEqual(crate.Position.x, seen.Position.x, 1e-3f);
            Assert.AreEqual(crate.Position.z, seen.Position.z, 1e-3f);
            Assert.AreEqual(crate.LandsAt - Time.time, seen.LandsAt - Time.time, 0.3f, "lands at the same moment");
            Assert.Greater(late.Match.Count, 0, "the match status (alive count) comes with the join");
        }

        [UnityTest]
        public IEnumerator SupplyCrate_ReachesTheRemote_AndTheRemoteOpensItThroughTheServer()
        {
            var director = FindDirector();
            var player = _net.HostPlayer;
            SupplyRules.Current = new SupplyRules { FirstStage = 1, EveryStages = 1, DescentSeconds = 1.5f, OpenSeconds = 60f, SmokeSeconds = 60f };
            // OpenSeconds is only the bots' dwell: a bot leaving the elevator the crate lands in front of must not open it before the remote does.
            Heard remoteHeard = null;
            RemoteClient remote = null;
            var supply = SupplyDirector.Instance;
            yield return _net.AddClient(c => remote = c, beforeConnect: nm => remoteHeard = Listen(nm, supply, SkySync.Instance));
            int mine = director.RegionIndexAt(player.transform.position);
            var rules = SupplyPlayTests.Fast();
            var g = director.Geometry;
            int seed = 0;
            for (int s = 1; s < 2000 && seed == 0; s++)
                if (Lockdown.Plan(g.RegionCount, g.PassA, g.PassB, s, g.SubZoneCounts(), rules).Order[0] != mine) seed = s;
            director.ServerConfigure(rules, seed);
            yield return NetHarness.WaitUntil(() => remoteHeard.SupplyOnClient.Crates.Count == 1, "the crate shows up on the remote client's director", Log, 20f);
            var onClient = remoteHeard.SupplyOnClient.Crates[0];
            var onServer = supply.Crates[0];
            Assert.AreEqual(onServer.Position.x, onClient.Position.x, 1e-3f);
            // The remote stands next to it after it landed and asks; the server opens it into pickups.
            remote.Controller.Teleport(onServer.Position + Vector3.up * 0.2f);
            yield return Seconds(2.5f);
            Assert.IsTrue(Time.time >= onServer.LandsAt);
            int before = LootCount();
            remoteHeard.SupplyOnClient.RequestOpen(onClient, remote.Player);
            yield return NetHarness.WaitUntil(() => supply.Crates.Count == 0, "the server opened it on the remote's request", Log, 5f);
            yield return NetHarness.WaitUntil(() => remoteHeard.SupplyOnClient.Crates.Count == 0, "the client's list dropped it", Log, 5f);
            yield return NetHarness.WaitUntil(() => LootCount() > before, "the items lie around it", Log, 5f);
            KeepInstance(supply);
        }

        static int LootCount()
        {
            int n = 0;
            foreach (var item in LootItem.All) if (item != null && item.IsSpawned) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator Loot_AMatchStartedWithTheSameLoot_ALateClientSeesEverythingOnTheGround()
        {
            int onHost = 0;
            foreach (var nob in _net.Host.ServerManager.Objects.Spawned.Values) if (nob != null && nob.GetComponent<LootItem>() != null) onHost++;
            Assert.Greater(onHost, 20, "the map has loot");
            RemoteClient late = null;
            yield return _net.AddClient(c => late = c);
            int onClient = 0;
            yield return NetHarness.WaitUntil(() =>
            {
                onClient = 0;
                foreach (var nob in late.Manager.ClientManager.Objects.Spawned.Values) if (nob != null && nob.GetComponent<LootItem>() != null) onClient++;
                return onClient >= onHost;
            }, "the late client holds every loot item", Log, 20f);
            Assert.AreEqual(onHost, onClient);
        }
    }
}

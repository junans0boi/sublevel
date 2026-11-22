using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #483: a remote player on the waiting island walks into the race pit, sees the key to get into a race car, presses it and drives,
    // in both topologies (docs/MODE_PARITY_AUDIT.md: the live island runs on a dedicated server, a host with WaitingRoom.ForceEnabled is the same room).
    public class RaceEntryParityPlayTests
    {
        sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };
            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = Frame.Jerrycan = false;
                Frame.Seat = -1;
                return f;
            }
        }

        MultiHarness _net;
        bool _oldFlag;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            VehicleInput.InputOverride = null;
            WaitingRoom.ForceEnabled = false;
            DedicatedServer.StartSecondsOverride = -1f;
            MatchRules.Current.UseWaitingIsland = _oldFlag;
            if (_net != null) yield return _net.Teardown();
            _net = null;
        }

        static Combatant ServerCopy(MultiHarness net, Combatant player) =>
            net.Host.ServerManager.Objects.Spawned.TryGetValue(player.ObjectId, out var nob) ? nob.GetComponent<Combatant>() : null;

        static Vector3 Local(float lx, float y, float lz) => new Vector3(WaitingIsland.WorldX(lx), y, WaitingIsland.WorldZ(lz));

        IEnumerator RemoteOnTheIsland(MultiHarness.Topology topology, Keys keys, System.Action<RemoteClient> done)
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            DedicatedServer.StartSecondsOverride = 300f;
            VehicleInput.InputOverride = keys;
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.Start(topology, "World");
            yield return NetHarness.WaitUntil(() => WaitingRoom.Current != null && RaceRoom.Current != null, $"[{topology}] the waiting room and its race open", Debug.Log, 90f);
            RemoteClient remote = null;
            ReplicaLayers.SeparateVehicles = true;   // one process: the client's race car must not be pushed by the server's copy
            yield return _net.AddClient(c => remote = c, beforeConnect: WaitingHud.Attach);   // as MatchDirector does in a client process
            var car = RaceRoom.Current.CarAt(0);
            yield return NetHarness.WaitUntil(() => remote.Manager.ClientManager.Objects.Spawned.ContainsKey(car.ObjectId), $"[{topology}] the remote client holds the race car", Debug.Log, 20f);
            done(remote);
        }

        // The remote player in the pit beside race car 0's driver door, the server holding the body there.
        IEnumerator ToTheCar(MultiHarness.Topology topology, RemoteClient remote, Vehicle car)
        {
            var door = car.DoorPosition(0);
            float deck = GameObject.Find("WaitingIsland").transform.position.y;
            var at = new Vector3(door.x, deck + 0.1f, door.z);
            remote.Controller.Teleport(at);
            remote.Controller.LookAt(car.transform.position + Vector3.up);
            yield return NetHarness.WaitUntil(() => { var s = ServerCopy(_net, remote.Player); return s != null && (s.transform.position - at).sqrMagnitude < 2f; }, $"[{topology}] the server holds the remote body by the car", Debug.Log, 10f);
            float end = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator RemotePlayer_SeesTheKey_GetsIntoARaceCar_AndDrives([Values] MultiHarness.Topology topology)
        {
            var keys = new Keys();
            RemoteClient remote = null;
            yield return RemoteOnTheIsland(topology, keys, c => remote = c);
            var car = RaceRoom.Current.CarAt(0);
            var home = car.transform.position;
            yield return ToTheCar(topology, remote, car);
            Assert.AreSame(remote.Player, Combatant.LocalPlayer, "the HUD reads the remote player");
            Debug.Log($"[#483] [{topology}] by the car: prompt '{Hud.Instance.PromptText}'");
            Assert.AreEqual("[F]  탑승", Hud.Instance.PromptText, $"[{topology}] the remote player sees the key to get in");

            var body = ServerCopy(_net, remote.Player);
            float deckY = GameObject.Find("WaitingIsland").transform.position.y;
            var near = Vehicle.NearestEnterable(remote.Player.transform.position, VehicleRules.Current.EnterRange, out float nd);
            Debug.Log($"[#483] [{topology}] before F: blocked {SpawnProtection.InputBlocked} (doors {ElevatorDoors.Instance?.DoorsClosed} island {WaitingHud.OnIsland}) focus {UiFocus.Current} loot {remote.Controller.NearbyLoot != null}"
                + $" near {near?.name} server-side {near != null && near.IsServerInitialized} {nd:0.00} m, client h {remote.Player.transform.position.y - deckY:0.00}, server h {body.transform.position.y - deckY:0.00}");
            keys.Frame.Interact = true;
            // F takes the nearest free door, which may be the car behind: whichever race car it is, at its wheel.
            yield return NetHarness.WaitUntil(() => VehicleSeat.VehicleOf(body) != null, $"[{topology}] the server seated the remote player", Debug.Log, 5f);
            car = VehicleSeat.VehicleOf(body);
            home = car.transform.position;
            Assert.IsTrue(car.IsRaceCar, $"[{topology}] got into a race car, not {car.name}");
            Assert.AreEqual(body.ObjectId, car.OccupantId(Vehicles.DriverSeat), $"[{topology}] at the wheel");
            yield return NetHarness.WaitUntil(() => HudBackend.VehicleSeatNumber(remote.Player) == 1, $"[{topology}] the remote player's own side has them at the wheel", Debug.Log, 5f);

            keys.Frame.Throttle = 1f;
            yield return NetHarness.WaitUntil(() => (car.transform.position - home).magnitude > 3f, $"[{topology}] the race car drives off the grid (server copy)", Debug.Log, 8f);
            keys.Frame.Throttle = 0f;
            Assert.IsTrue(car.IsRaceCar, "still a race car in the hall");
            Assert.IsTrue(VehicleSeat.IsAttached(body), $"[{topology}] still at the wheel (not taken for a fall from the tower)");
        }

        // On the island by the gate (not yet at a car): a hint names the race hall and the key, so the way in is found.
        [UnityTest, Timeout(400000)]
        public IEnumerator RemotePlayer_AtTheGate_SeesTheRaceHallHint([Values] MultiHarness.Topology topology)
        {
            RemoteClient remote = null;
            yield return RemoteOnTheIsland(topology, new Keys(), c => remote = c);
            float deck = GameObject.Find("WaitingIsland").transform.position.y;
            remote.Controller.Teleport(Local(RaceTrack.PitX0 - 6f, deck + 0.1f, (RaceTrack.PitZ0 + RaceTrack.PitZ1) * 0.5f));
            yield return NetHarness.WaitUntil(() => Hud.Instance.PromptText.Contains("경주장") && Hud.Instance.PromptText.Contains("[F]"), $"[{topology}] a race hall hint at the gate", Debug.Log, 5f);
            Debug.Log($"[#483] [{topology}] at the gate: prompt '{Hud.Instance.PromptText}'");
            // A review shot of the hint, on purpose: I483_SHOTS=<folder> (dedicated: the remote player's own camera is the only one).
            string folder = System.Environment.GetEnvironmentVariable("I483_SHOTS");
            if (!string.IsNullOrEmpty(folder) && topology == MultiHarness.Topology.Dedicated)
            {
                System.IO.Directory.CreateDirectory(folder);
                remote.Controller.LookAt(Local(RaceTrack.PitX1, deck + 1.2f, (RaceTrack.PitZ0 + RaceTrack.PitZ1) * 0.5f));
                float end = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < end) yield return null;
                yield return WaitingIslandPlayTests.Shot(folder, "race_gate_hint");
                yield return ToTheCar(topology, remote, RaceRoom.Current.CarAt(0));
                yield return WaitingIslandPlayTests.Shot(folder, "race_car_prompt");
                Debug.Log($"[#483] by the car: prompt '{Hud.Instance.PromptText}', shots in {folder}");
            }
            // Far from the gate (the island's west side): no hint.
            remote.Controller.Teleport(Local(-20f, deck + 0.1f, 0f));
            yield return NetHarness.WaitUntil(() => !Hud.Instance.PromptText.Contains("경주장"), $"[{topology}] no hint away from the gate", Debug.Log, 5f);
        }
    }
}

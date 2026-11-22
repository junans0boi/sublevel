using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    // Input a test drives by hand (one per player).
    public sealed class HarnessInput : IPlayerInput
    {
        public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

        public PlayerInputFrame Read()
        {
            var f = Frame;
            // One-shot presses last a single frame.
            Frame.Jump = Frame.FirePressed = Frame.Reload = Frame.Interact = false;
            Frame.ToggleView = Frame.Heal = Frame.Restart = false;
            Frame.Ping = Frame.CarryPressed = Frame.Boost = false;
            Frame.SelectSlot = -1;
            return f;
        }
    }

    // One remote client: its own NetworkManager (client only) in the host's process, and the player it was given.
    public sealed class RemoteClient
    {
        public NetworkManager Manager;
        // Names this player's session for a reconnect (#166).
        public string Token = Guid.NewGuid().ToString("N");
        public Combatant Player;
        public HarnessInput Input;
        public PlayerController Controller => Player != null ? Player.GetComponent<PlayerController>() : null;
    }

    // #159: the host (a scene's own MatchDirector as server + client) plus remote clients over localhost, in one test run.
    // Build on NetHarness (two NetworkManagers in one process). Only 127.0.0.1 is ever used.
    public sealed class MultiHarness
    {
        // The latency simulator numbers the multiplayer tickets test against: 100 ms round trip, 2 % of the unreliable
        // packets lost (Fish-Net applies loss to unreliable packets only; reliable ones are resent late instead).
        public const long BadRoundTripMs = 100;
        public const double BadLoss = 0.02;

        public NetworkManager Host { get; private set; }
        public HarnessInput HostInput { get; private set; }
        // The host's own player (Combatant.LocalPlayer is whoever started last: remote owners overwrite it in this process).
        public Combatant HostPlayer { get; private set; }
        public readonly List<RemoteClient> Clients = new List<RemoteClient>();
        public Action<string> Log;
        ReplicaLayers _hider;
        InterestRules _interestBefore;

        // Loads `scene` (it hosts its own match) and waits for the host's own player.
        public IEnumerator StartHost(string scene)
        {
            MoveGuard.TrustTeleports = true;
            PrepareHostInput();
            SceneManager.LoadScene(scene);
            yield return AdoptHost();
        }

        // Where the server runs (docs/MODE_PARITY_AUDIT.md): Host = bot practice and a LAN room (server + local player in one
        // process); Dedicated = the live solo/squad server (server only, every player a remote client). A parity test runs both.
        public enum Topology { Host, Dedicated }

        public IEnumerator Start(Topology topology, string scene) => topology == Topology.Host ? StartHost(scene) : StartDedicated(scene);

        // The dedicated topology in one process: the scene's MatchDirector serves without a local player (NetSession.Dedicated,
        // as DedicatedServer sets it); players come with AddClient. HostPlayer stays null.
        public IEnumerator StartDedicated(string scene)
        {
            MoveGuard.TrustTeleports = true;
            PrepareHostInput();
            NetSession.Dedicated = true;
            SceneManager.LoadScene(scene);
            yield return NetHarness.WaitUntil(() => MatchDirector.Instance != null && InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.IsServerStarted, "dedicated server started", Log, 60f);
            Host = InstanceFinder.NetworkManager;
            Require(!Host.IsClientStarted, "a dedicated server has no local client");
            yield return null;
        }

        static void Require(bool condition, string what) { if (!condition) throw new Exception(what); }

        // Must come before the host's player spawns.
        public void PrepareHostInput()
        {
            // The replication audits teleport players far apart and still expect every client to hold everyone: interest
            // management (#167) is off unless a test turns it on after this.
            _interestBefore = InterestRules.Current;
            InterestRules.Current = new InterestRules { Enabled = false };
            HostInput = new HarnessInput();
            PlayerController.InputOverride = HostInput;
        }

        // The host is a match that is already loading or running (e.g. started from the lobby; call PrepareHostInput
        // first): waits for its player.
        public IEnumerator AdoptHost()
        {
            yield return NetHarness.WaitUntil(() => MatchDirector.Instance != null && Combatant.LocalPlayer != null && PlayerController.Local != null, "host player spawned", Log, 60f);
            Host = InstanceFinder.NetworkManager;
            HostPlayer = Combatant.LocalPlayer;
            ((Action<NetworkManager>)(nm => nm.TransportManager.LatencySimulator.GetType().GetField("_simulateHost", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(nm.TransportManager.LatencySimulator, false)))(Host);
            yield return null;
        }

        // Connects one more client to the host and waits until its player exists on its side.
        // `beforeConnect` gets the new client's manager before it connects (tests register their broadcast listeners there).
        public IEnumerator AddClient(Action<RemoteClient> done = null, string token = null, Action<NetworkManager> beforeConnect = null)
        {
            var client = new RemoteClient { Input = new HarnessInput() };
            if (token != null) client.Token = token;
            if (_hider == null) _hider = new GameObject("ReplicaLayers").AddComponent<ReplicaLayers>();
            _hider.Host = Host;
            ushort port = MatchDirector.Instance.Port;
            client.Manager = NetHarness.Build("NM_Remote" + Clients.Count, Host.SpawnablePrefabs, port);
            SessionHelloSender.Attach(client.Manager, client.Token);
            _hider.Managers.Add(client.Manager);
            beforeConnect?.Invoke(client.Manager);
            if (!client.Manager.ClientManager.StartConnection("127.0.0.1", port)) throw new Exception("remote client failed to start");
            yield return NetHarness.WaitUntil(() => client.Manager.IsClientStarted, "remote client connected+authenticated", Log);
            yield return NetHarness.WaitUntil(() => client.Manager.ClientManager.Connection.FirstObject != null, "remote client got its player", Log);
            var nob = client.Manager.ClientManager.Connection.FirstObject;
            client.Player = nob.GetComponent<Combatant>();
            yield return NetHarness.WaitUntil(() => client.Controller != null && client.Controller.isActiveAndEnabled, "remote player controller", Log);
            client.Controller.UseInput(client.Input);
            Clients.Add(client);
            done?.Invoke(client);
        }

        // The client's line drops (its process gone): the manager is stopped and removed, the player object is not touched here.
        public IEnumerator DropClient(RemoteClient client)
        {
            client.Manager.ClientManager.StopConnection();
            yield return NetHarness.WaitUntil(() => !client.Manager.ClientManager.Started, "dropped client stopped", Log);
            UnityEngine.Object.Destroy(client.Manager.gameObject);
            client.Manager = null;
            Clients.Remove(client);
        }

        // Every player combatant `manager` has spawned on its own side (replicas included).
        public static List<Combatant> PlayersSeenBy(NetworkManager manager)
        {
            var list = new List<Combatant>();
            foreach (var nob in manager.ClientManager.Objects.Spawned.Values)
            {
                var c = nob != null ? nob.GetComponent<Combatant>() : null;
                if (c != null && c.IsPlayer) list.Add(c);
            }
            return list;
        }

        // The copy of `player` that `manager` holds (the client replica, or the object itself on the host).
        public static Combatant ReplicaOf(NetworkManager manager, Combatant player)
        {
            return manager.ClientManager.Objects.Spawned.TryGetValue(player.ObjectId, out var nob) ? nob.GetComponent<Combatant>() : null;
        }

        // Round trip `rttMs` between the host and `client`, `loss` (0..1) of the unreliable packets lost, both directions.
        public void Simulate(RemoteClient client, long rttMs = BadRoundTripMs, double loss = BadLoss)
        {
            Apply(client.Manager, rttMs / 2, loss);
            Apply(Host, rttMs / 2, loss);
        }

        public void ClearSimulation(RemoteClient client)
        {
            Apply(client.Manager, 0, 0);
            Apply(Host, 0, 0);
        }

        static void Apply(NetworkManager nm, long oneWayMs, double loss)
        {
            var sim = nm.TransportManager.LatencySimulator;
            sim.SetLatency(oneWayMs);
            sim.SetPacketLoss(loss);
            sim.SetEnabled(oneWayMs > 0 || loss > 0);
        }

        public IEnumerator Teardown()
        {
            MoveGuard.TrustTeleports = false;
            NetSession.Dedicated = false;
            PlayerController.InputOverride = null;
            ReplicaLayers.SeparateVehicles = false;
            if (_interestBefore != null) InterestRules.Current = _interestBefore;
            foreach (var c in Clients)
            {
                if (c.Manager == null) continue;
                c.Manager.ClientManager.StopConnection();
            }
            float end = Time.realtimeSinceStartup + NetHarness.TimeoutSeconds;
            bool Busy() { foreach (var c in Clients) if (c.Manager != null && c.Manager.ClientManager.Started) return true; return false; }
            while (Busy() && Time.realtimeSinceStartup < end) yield return null;
            foreach (var c in Clients) if (c.Manager != null) UnityEngine.Object.Destroy(c.Manager.gameObject);
            Clients.Clear();
            if (_hider != null) UnityEngine.Object.Destroy(_hider.gameObject);
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }
    }

    // The remote clients' copies of every object live in the same physics scene as the host's real ones, so the server's
    // shots would hit those copies (which have no server state). Their colliders go on the Ignore Raycast layer, which
    // the default ray mask skips; bodies still collide as before. Test-only: in a real host the client IS the server object.
    //
    // Vehicles (#165): a vehicle that one manager simulates must not be pushed by the other managers' copies of the same
    // vehicle or of the people (in separate processes those copies would not exist in its world), or it is thrown about.
    public sealed class ReplicaLayers : MonoBehaviour
    {
        public readonly List<NetworkManager> Managers = new List<NetworkManager>();
        // The server's manager.
        public NetworkManager Host;
        // On for the vehicle audits only: the sweep costs a few thousand physics calls in a map full of vehicles.
        public static bool SeparateVehicles;
        readonly HashSet<(int, int)> _ignored = new HashSet<(int, int)>();
        int _lastCount;
        float _nextSweep;

        void LateUpdate()
        {
            int count = Host != null ? Host.ServerManager.Objects.Spawned.Count : 0;
            foreach (var m in Managers)
            {
                if (m == null || !m.IsClientStarted) continue;
                count += m.ClientManager.Objects.Spawned.Count;
                foreach (var nob in m.ClientManager.Objects.Spawned.Values)
                {
                    if (nob == null || nob.IsServerInitialized) continue;
                    foreach (var c in nob.GetComponentsInChildren<Collider>(true))
                        if (c.gameObject.layer != 2) c.gameObject.layer = 2;
                }
            }
            if (!SeparateVehicles || Host == null || (count == _lastCount && Time.unscaledTime < _nextSweep)) return;
            _lastCount = count;
            _nextSweep = Time.unscaledTime + 0.25f;
            SeparateWorlds();
        }

        // Manager 0 is the server's own objects, 1.. the clients' replicas.
        void SeparateWorlds()
        {
            var vehicles = new List<(Vehicle vehicle, int world, int id)>();
            var bodies = new List<(Collider[] colliders, int world)>();
            Collect(Host.ServerManager.Objects.Spawned.Values, 0, vehicles, bodies, false);
            for (int i = 0; i < Managers.Count; i++)
                if (Managers[i] != null && Managers[i].IsClientStarted) Collect(Managers[i].ClientManager.Objects.Spawned.Values, i + 1, vehicles, bodies, true);
            foreach (var v in vehicles)
            {
                foreach (var other in vehicles)
                    if (other.world != v.world && other.id == v.id) Ignore(v.vehicle.Colliders, other.vehicle.Colliders, v.vehicle.GetInstanceID(), other.vehicle.GetInstanceID());
                foreach (var body in bodies)
                    if (body.world != v.world) Ignore(v.vehicle.Colliders, body.colliders, v.vehicle.GetInstanceID(), body.colliders.Length > 0 && body.colliders[0] != null ? body.colliders[0].GetInstanceID() : 0);
            }
        }

        static void Collect(IEnumerable<NetworkObject> objects, int world, List<(Vehicle, int, int)> vehicles, List<(Collider[], int)> bodies, bool replicasOnly)
        {
            foreach (var nob in objects)
            {
                if (nob == null || (replicasOnly && nob.IsServerInitialized)) continue;
                var v = nob.GetComponent<Vehicle>();
                if (v != null) vehicles.Add((v, world, nob.ObjectId));
                else if (nob.GetComponent<Combatant>() != null) bodies.Add((nob.GetComponentsInChildren<Collider>(true), world));
            }
        }

        void Ignore(IReadOnlyList<Collider> mine, IReadOnlyList<Collider> theirs, int keyA, int keyB)
        {
            if (!_ignored.Add((keyA, keyB))) return;
            for (int i = 0; i < mine.Count; i++)
                for (int j = 0; j < theirs.Count; j++)
                    if (mine[i] != null && theirs[j] != null) Physics.IgnoreCollision(mine[i], theirs[j], true);
        }
    }
}

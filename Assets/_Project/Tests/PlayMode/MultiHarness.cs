using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
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
            Frame.SelectSlot = -1;
            return f;
        }
    }

    // One remote client: its own NetworkManager (client only) in the host's process, and the player it was given.
    public sealed class RemoteClient
    {
        public NetworkManager Manager;
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

        // Loads `scene` (it hosts its own match) and waits for the host's own player.
        public IEnumerator StartHost(string scene)
        {
            HostInput = new HarnessInput();
            PlayerController.InputOverride = HostInput;
            SceneManager.LoadScene(scene);
            yield return NetHarness.WaitUntil(() => MatchDirector.Instance != null && Combatant.LocalPlayer != null && PlayerController.Local != null, "host player spawned", Log);
            Host = InstanceFinder.NetworkManager;
            HostPlayer = Combatant.LocalPlayer;
            ((Action<NetworkManager>)(nm => nm.TransportManager.LatencySimulator.GetType().GetField("_simulateHost", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(nm.TransportManager.LatencySimulator, false)))(Host);
            yield return null;
        }

        // Connects one more client to the host and waits until its player exists on its side.
        public IEnumerator AddClient(Action<RemoteClient> done = null)
        {
            var client = new RemoteClient { Input = new HarnessInput() };
            ushort port = MatchDirector.Instance.Port;
            client.Manager = NetHarness.Build("NM_Remote" + Clients.Count, Host.SpawnablePrefabs, port);
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
            PlayerController.InputOverride = null;
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
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }
    }
}

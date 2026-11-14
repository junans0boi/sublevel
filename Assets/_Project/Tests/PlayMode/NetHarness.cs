using System;
using System.Collections;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using Game.Net;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Two NetworkManagers in one process (server-only + client-only) over Tugboat loopback. See docs/research/fishnet-headless-host-client.md.
    public sealed class NetHarness
    {
        public const float TimeoutSeconds = 10f;
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        public NetworkManager Server { get; private set; }
        public NetworkManager Client { get; private set; }
        public ushort Port { get; }
        readonly NetworkObject _prefab;
        SinglePrefabObjects _serverPrefabs, _clientPrefabs;

        static ushort _nextPort = 17770;

        public NetHarness(NetworkObject prefab, ushort? port = null)
        {
            _prefab = prefab;
            Port = port ?? _nextPort++;
        }

        NetworkManager Create(string name, out SinglePrefabObjects prefabs)
        {
            prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.AddObject(_prefab, false, false);
            return Build(name, prefabs, Port);
        }

        // A NetworkManager that coexists with others in the process (AllowMultiple), on its own Tugboat.
        public static NetworkManager Build(string name, PrefabObjects prefabs, ushort port)
        {
            var go = new GameObject(name);
            go.SetActive(false); // defer NetworkManager.Awake until configured
            // Editor-only: NetworkManager.Reset logs "SpawnablePrefabs is null" on AddComponent; we assign it right after.
            var prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            NetworkManager nm;
            try { nm = go.AddComponent<NetworkManager>(); }
            finally { LogAssert.ignoreFailingMessages = prevIgnore; }
            typeof(NetworkManager).GetField("_persistence", Priv).SetValue(nm, NetworkManager.PersistenceType.AllowMultiple);
            nm.SpawnablePrefabs = prefabs;
            go.AddComponent<Tugboat>().SetPort(port);
            go.SetActive(true);
            return nm;
        }

        public IEnumerator Connect(Action<string> log = null)
        {
            MoveGuard.TrustTeleports = true;
            Server = Create("NM_Server", out _serverPrefabs);
            Client = Create("NM_Client", out _clientPrefabs);
            if (!Server.ServerManager.StartConnection(Port)) throw new Exception("server failed to start");
            yield return WaitUntil(() => Server.IsServerStarted, "server started", log);
            if (!Client.ClientManager.StartConnection("127.0.0.1", Port)) throw new Exception("client failed to start");
            yield return WaitUntil(() => Client.IsClientStarted, "client connected+authenticated", log);
        }

        public IEnumerator Teardown()
        {
            MoveGuard.TrustTeleports = false;
            if (Client != null) Client.ClientManager.StopConnection();
            if (Server != null) Server.ServerManager.StopConnection(true);
            var end = Time.realtimeSinceStartup + TimeoutSeconds;
            while ((Client != null && Client.ClientManager.Started || Server != null && Server.ServerManager.Started)
                   && Time.realtimeSinceStartup < end)
                yield return null;
            if (Client != null && Client.ClientManager.Started || Server != null && Server.ServerManager.Started)
                Debug.LogError("NetHarness.Teardown: connections did not stop in time");
            if (Client != null) UnityEngine.Object.Destroy(Client.gameObject);
            if (Server != null) UnityEngine.Object.Destroy(Server.gameObject);
            if (_clientPrefabs != null) UnityEngine.Object.Destroy(_clientPrefabs);
            if (_serverPrefabs != null) UnityEngine.Object.Destroy(_serverPrefabs);
            yield return null;
        }

        public static IEnumerator WaitUntil(Func<bool> cond, string what, Action<string> log = null, float timeout = TimeoutSeconds)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!cond())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    log?.Invoke($"TIMEOUT waiting for: {what}");
                    throw new TimeoutException($"Timed out ({timeout}s) waiting for: {what}");
                }
                yield return null;
            }
            log?.Invoke($"ok: {what}");
        }
    }
}

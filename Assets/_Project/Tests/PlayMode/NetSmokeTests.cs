using System.Collections;
using System.IO;
using Game.Net;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public class NetSmokeTests
    {
        const string LogDir = "Logs/net-smoke";
        NetHarness _net;
        NetworkObject _prefab;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line)
        {
            Debug.Log(line);
            _log.AppendLine(line);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            var prefab = Resources.Load<NetworkObject>("NetSmoke/SmokeNetObject");
            Assert.IsNotNull(prefab, "Run `tools/unity.sh run NetSmokeFixtures.Create` to build the fixture prefab");
            _prefab = prefab;
            _net = new NetHarness(prefab);
            yield return _net.Connect(Log);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            yield return _net.Teardown();
        }

        [UnityTest]
        public IEnumerator ServerAndClient_Connect()
        {
            Assert.IsTrue(_net.Server.IsServerStarted);
            Assert.IsTrue(_net.Client.IsClientStarted);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Teardown_ThenReconnectOnSamePort()
        {
            yield return _net.Teardown();
            var again = new NetHarness(_prefab, _net.Port);
            yield return again.Connect(Log);
            Assert.IsTrue(again.Server.IsServerStarted);
            Assert.IsTrue(again.Client.IsClientStarted);
            yield return again.Teardown();
        }

        IEnumerator SpawnOnServer(System.Action<NetworkObject, NetworkObject> done)
        {
            var inst = Object.Instantiate(_prefab);
            _net.Server.ServerManager.Spawn(inst);
            Log($"server spawned ObjectId={inst.ObjectId}");
            NetworkObject clientNob = null;
            yield return NetHarness.WaitUntil(
                () => _net.Client.ClientManager.Objects.Spawned.TryGetValue(inst.ObjectId, out clientNob), "spawn observed on client", Log);
            Assert.AreNotSame(inst, clientNob, "client must hold its own replica");
            done(inst, clientNob);
        }

        [UnityTest]
        public IEnumerator ServerSpawn_IsObservedOnClient()
        {
            yield return SpawnOnServer((s, c) => Assert.IsTrue(c.IsSpawned));
        }

        [UnityTest]
        public IEnumerator ServerSyncVarChange_ReachesClient()
        {
            NetworkObject s = null, c = null;
            yield return SpawnOnServer((a, b) => { s = a; c = b; });
            s.GetComponent<SmokeNetObject>().Counter.Value = 42;
            Log("server set Counter=42");
            yield return NetHarness.WaitUntil(() => c.GetComponent<SmokeNetObject>().Counter.Value == 42, "Counter==42 on client", Log);
        }

        [UnityTest]
        public IEnumerator ClientSyncVarWrite_DoesNotChangeServer()
        {
            NetworkObject s = null, c = null;
            yield return SpawnOnServer((a, b) => { s = a; c = b; });
            s.GetComponent<SmokeNetObject>().Counter.Value = 7;
            yield return NetHarness.WaitUntil(() => c.GetComponent<SmokeNetObject>().Counter.Value == 7, "Counter==7 on client", Log);

            var prev = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // Fish-Net logs the rejected write
            try { c.GetComponent<SmokeNetObject>().Counter.Value = 999; }
            finally { LogAssert.ignoreFailingMessages = prev; }
            Log("client attempted Counter=999");

            // Let several ticks pass so a (wrongly accepted) write would have replicated.
            var end = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(7, s.GetComponent<SmokeNetObject>().Counter.Value, "server value must be unchanged");
            Assert.AreEqual(7, c.GetComponent<SmokeNetObject>().Counter.Value, "client value must be unchanged");
            Log("server authority held: Counter==7 on both");
        }
    }
}

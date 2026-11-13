using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #159: host + a remote client over localhost in the real TestBox match.
    public class MultiClientSmokeTests
    {
        const string LogDir = "Logs/net-multi";
        MultiHarness _net;
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
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            yield return _net.Teardown();
        }

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        [UnityTest]
        public IEnumerator RemoteClient_ConnectsAndSeesTheHostPlayer_AndTheHostSeesIt()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);

            Assert.IsTrue(remote.Manager.IsClientStarted);
            Assert.IsTrue(remote.Player.IsOwner, "the client owns its own player");
            Assert.AreEqual(2, MultiHarness.PlayersSeenBy(_net.Host).Count, "host sees two players");
            yield return NetHarness.WaitUntil(() => MultiHarness.PlayersSeenBy(remote.Manager).Count == 2, "client sees two players", Log);

            var hostPlayerOnClient = MultiHarness.ReplicaOf(remote.Manager, _net.HostPlayer);
            Assert.IsNotNull(hostPlayerOnClient, "client holds a replica of the host's player");
            Assert.AreNotSame(_net.HostPlayer, hostPlayerOnClient);
            Assert.IsFalse(hostPlayerOnClient.IsOwner);
        }

        [UnityTest]
        public IEnumerator RemoteClientMovement_ReachesTheHost()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);
            var onHost = MultiHarness.ReplicaOf(_net.Host, remote.Player);
            yield return Seconds(0.5f);
            var from = onHost.transform.position;

            remote.Input.Frame.Move = new Vector2(0, 1);
            yield return Seconds(1f);
            remote.Input.Frame.Move = Vector2.zero;
            yield return Seconds(0.5f);

            float moved = Vector3.Distance(from, onHost.transform.position);
            Log($"remote walked, host sees {moved:F2} m");
            Assert.Greater(moved, 2f, "about a second of walking arrives on the host");
        }

        [UnityTest]
        public IEnumerator HostMovement_ReachesTheRemoteClient()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);
            var onClient = MultiHarness.ReplicaOf(remote.Manager, _net.HostPlayer);
            yield return Seconds(0.5f);
            var from = onClient.transform.position;

            _net.HostInput.Frame.Move = new Vector2(0, 1);
            yield return Seconds(1f);
            _net.HostInput.Frame.Move = Vector2.zero;
            yield return Seconds(0.5f);

            float moved = Vector3.Distance(from, onClient.transform.position);
            Log($"host walked, remote sees {moved:F2} m");
            Assert.Greater(moved, 2f, "about a second of walking arrives on the client");
        }

        [UnityTest]
        public IEnumerator Movement_StillReplicates_At100msAnd2PercentLoss()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);
            _net.Simulate(remote);
            var onHost = MultiHarness.ReplicaOf(_net.Host, remote.Player);
            yield return Seconds(0.5f);
            var from = onHost.transform.position;

            remote.Input.Frame.Move = new Vector2(0, 1);
            yield return Seconds(1.5f);
            remote.Input.Frame.Move = Vector2.zero;
            yield return Seconds(1f);

            float moved = Vector3.Distance(from, onHost.transform.position);
            Log($"remote walked under simulation, host sees {moved:F2} m");
            Assert.Greater(moved, 2f);
        }
    }
}

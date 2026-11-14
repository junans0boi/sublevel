using System.Collections;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #169 / ADR-130: a ping reaches the pinger's teammates and nobody else, over a real connection. Host + client A
    // form team 1 (a duo), client B is the next team. The marker on the HUD is tested in TeamPlayTests.
    public class TeamPingPlayTests
    {
        const string LogDir = "Logs/net-team";
        MultiHarness _net;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            TeamMatch.Mode = TeamMode.Duo;
            TeamRules.Current = new TeamRules();
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            TeamMatch.Mode = TeamMode.Solo;
            TeamRules.Current = GameData.Rules.Team;
            yield return _net.Teardown();
        }

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        [UnityTest]
        public IEnumerator Ping_ReachesTheTeammate_NotTheOtherTeam_AndIsRateLimited()
        {
            RemoteClient mate = null, other = null;
            yield return _net.AddClient(c => mate = c);
            yield return _net.AddClient(c => other = c);
            var host = _net.HostPlayer;
            var mateOnServer = MultiHarness.ReplicaOf(_net.Host, mate.Player);
            var otherOnServer = MultiHarness.ReplicaOf(_net.Host, other.Player);
            yield return NetHarness.WaitUntil(() => host.HasTeam && mateOnServer.HasTeam && otherOnServer.HasTeam, "everyone in a team", Log);
            Assert.AreEqual(host.TeamId.Value, mateOnServer.TeamId.Value, "host and the first client are a duo");
            Assert.AreNotEqual(host.TeamId.Value, otherOnServer.TeamId.Value, "the second client is on another team");
            var at = host.transform.position + host.transform.forward * 25f;

            // The host's ping: the teammate hears it with the right kind, place and number; the other team hears nothing.
            host.CmdPing((byte)PingKind.Danger, at);
            yield return NetHarness.WaitUntil(() => mate.Player.PingsHeard.Count == 1, "the teammate hears the ping", Log);
            yield return Seconds(0.5f);
            var heard = mate.Player.PingsHeard.Active[0];
            Assert.AreEqual(PingKind.Danger, heard.Kind);
            Assert.AreEqual(host.ObjectId, heard.Owner);
            Assert.AreEqual(host.TeamSlot.Value, heard.Slot);
            Assert.AreEqual(at.x, heard.X, 0.01f);
            Assert.AreEqual(at.z, heard.Z, 0.01f);
            Assert.AreEqual(1, host.PingsHeard.Count, "the pinger sees its own ping too");
            Assert.AreEqual(0, other.Player.PingsHeard.Count, "another team hears nothing");
            Assert.AreEqual(0, MultiHarness.ReplicaOf(other.Manager, mate.Player).PingsHeard.Count);

            // The teammate answers: the host hears it.
            mate.Player.CmdPing((byte)PingKind.Loot, mate.Player.transform.position + Vector3.forward * 10f);
            yield return NetHarness.WaitUntil(() => host.PingsHeard.Count == 2, "the host hears the answer", Log);
            Assert.AreEqual(0, other.Player.PingsHeard.Count);

            // Too fast: a second ping within the minimum interval is dropped by the server.
            yield return Seconds(0.6f); // the answer's echo reaches its sender, and the interval is over
            int before = mate.Player.PingsHeard.Total;
            host.CmdPing((byte)PingKind.Here, at);
            host.CmdPing((byte)PingKind.Here, at + Vector3.right);
            yield return Seconds(0.8f);
            Assert.LessOrEqual(mate.Player.PingsHeard.Total - before, 1, "only one of two quick pings goes through");

            // A kind outside the wheel and a point that is not a number are refused.
            before = mate.Player.PingsHeard.Total;
            yield return Seconds(TeamRules.Current.PingMinIntervalSeconds + 0.1f);
            host.CmdPing(99, at);
            host.CmdPing((byte)PingKind.Go, new Vector3(float.NaN, 0f, 0f));
            yield return Seconds(0.8f);
            Assert.AreEqual(before, mate.Player.PingsHeard.Total, "bad pings are not passed on");

            // A ping beyond the aim reach is refused too.
            host.CmdPing((byte)PingKind.Go, host.transform.position + Vector3.forward * (TeamRules.Current.PingMaxMeters + 200f));
            yield return Seconds(0.8f);
            Assert.AreEqual(before, mate.Player.PingsHeard.Total, "farther than the reach");
        }
    }
}

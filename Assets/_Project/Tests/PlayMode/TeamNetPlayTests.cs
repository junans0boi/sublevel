using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #169: team features over a real connection. The host and a remote client form a duo in TestBox (both count
    // as players, so they share team 1); the host lifts the knocked remote player, walks slowly with it, puts it down
    // and revives it. Pings are checked in TeamPingPlayTests.
    public class TeamNetPlayTests
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
            TeamRules.Current = new TeamRules { ReviveSeconds = 1f };
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

        static void EndProtection()
        {
            foreach (var c in Combatant.All)
            {
                var p = c != null ? c.GetComponent<SpawnProtection>() : null;
                if (p != null) p.ServerEnd();
            }
        }

        [UnityTest]
        public IEnumerator HostCarriesAKnockedRemoteTeammate_SlowlyAndOnTheirSide_ThenRevivesIt()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);
            var host = _net.HostPlayer;
            var mate = MultiHarness.ReplicaOf(_net.Host, remote.Player); // the server's own object
            yield return NetHarness.WaitUntil(() => host.HasTeam && mate.HasTeam && host.TeamId.Value == mate.TeamId.Value, "both on one team", Log);
            EndProtection();

            mate.ServerTakeDamage(new DamageInfo { Amount = 500f, Zone = HitZone.Body, Cause = DamageCause.Shot });
            Assert.IsTrue(mate.Knocked.Value);
            yield return NetHarness.WaitUntil(() => remote.Player.Knocked.Value, "the remote client sees itself knocked", Log);
            Assert.AreEqual(100f / 90f, TeamRules.Current.BleedFor(1), 1e-3f);

            // Next to the host (the remote moves its own body).
            remote.Controller.Teleport(host.transform.position + host.transform.right * 1f);
            yield return NetHarness.WaitUntil(() => Vector3.Distance(host.transform.position, mate.transform.position) < 1.6f, "remote body beside the host", Log);
            yield return Seconds(0.3f);
            Assert.AreEqual(mate.ObjectId, TeamHud.CarryCandidate(host)?.ObjectId, "the knocked teammate is the one Y would lift");

            // (The Y key itself is tested in TeamPlayTests: in this process the HUD follows whichever player started last.)
            host.CmdBeginCarry(mate.NetworkObject);
            yield return NetHarness.WaitUntil(() => host.IsCarrying && mate.BeingCarried, "the lift", Log);
            yield return NetHarness.WaitUntil(() => remote.Player.BeingCarried && MultiHarness.ReplicaOf(remote.Manager, host).IsCarrying, "the remote client knows it is carried", Log);
            Assert.IsTrue(host.TeamBlocksAction, "no shooting while carrying");

            var from = host.transform.position;
            _net.HostInput.Frame.Move = new Vector2(0, 1);
            yield return Seconds(2f);
            _net.HostInput.Frame.Move = Vector2.zero;
            yield return Seconds(0.4f);
            float walked = Vector3.Distance(Flat(from), Flat(host.transform.position));
            float walk = GameData.Rules.Locomotion.WalkSpeed;
            Log($"carrier walked {walked:F2} m in 2 s (walk speed {walk:F2}), carried is {Vector3.Distance(host.transform.position, mate.transform.position):F2} m from it");
            Assert.Greater(walked, 0.8f, "the carrier can move");
            Assert.Less(walked, walk * 2f * 0.8f, "but slower than walking");
            Assert.Less(Vector3.Distance(Flat(host.transform.position), Flat(mate.transform.position)), 1.2f, "the carried body rides on the carrier (server view)");
            Assert.Less(Vector3.Distance(Flat(host.transform.position), Flat(remote.Player.transform.position)), 1.2f, "and on the carried client's own view");
            Assert.IsTrue(mate.Knocked.Value, "still knocked");

            host.CmdEndCarry();
            yield return NetHarness.WaitUntil(() => !host.IsCarrying && !mate.BeingCarried, "put down", Log);
            yield return NetHarness.WaitUntil(() => !remote.Player.BeingCarried, "the remote client sees it put down", Log);

            host.CmdBeginRevive(mate.NetworkObject);
            yield return NetHarness.WaitUntil(() => !mate.Knocked.Value, "revived", Log);
            yield return NetHarness.WaitUntil(() => !remote.Player.Knocked.Value, "the remote client is back up", Log);
            Assert.AreEqual(TeamRules.Current.ReviveHp, mate.Hp.Value, 1e-3f);
        }

        [UnityTest]
        public IEnumerator CarriedOne_CannotBeRevived_AndACarrierDoesNotRevive()
        {
            RemoteClient remote = null;
            yield return _net.AddClient(c => remote = c);
            var host = _net.HostPlayer;
            var mate = MultiHarness.ReplicaOf(_net.Host, remote.Player);
            yield return NetHarness.WaitUntil(() => host.HasTeam && mate.HasTeam && host.TeamId.Value == mate.TeamId.Value, "both on one team", Log);
            EndProtection();
            mate.ServerTakeDamage(new DamageInfo { Amount = 500f, Zone = HitZone.Body, Cause = DamageCause.Shot });
            remote.Controller.Teleport(host.transform.position + host.transform.right * 1f);
            yield return NetHarness.WaitUntil(() => Vector3.Distance(host.transform.position, mate.transform.position) < 1.6f, "beside", Log);
            Assert.IsTrue(host.ServerBeginCarry(mate));
            Assert.IsFalse(host.ServerBeginRevive(mate), "a carrier does not revive");
            host.ServerEndCarry();
            Assert.IsFalse(mate.BeingCarried);
            Assert.IsTrue(host.ServerBeginRevive(mate), "put down, it can be revived");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}

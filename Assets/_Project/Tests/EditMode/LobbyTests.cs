using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class LobbyTests
    {
        [Test]
        public void CheckJoin_VersionMismatch_WinsOverFull()
        {
            Assert.AreEqual(JoinResult.VersionMismatch, LobbyRules.CheckJoin("1/1", "1/2", 16, 16, false));
        }

        [Test]
        public void CheckJoin_FullRoom_AndStartedMatch_AreRefused()
        {
            Assert.AreEqual(JoinResult.Ok, LobbyRules.CheckJoin("v", "v", 15, 16, false));
            Assert.AreEqual(JoinResult.Full, LobbyRules.CheckJoin("v", "v", 16, 16, false));
            Assert.AreEqual(JoinResult.MatchStarted, LobbyRules.CheckJoin("v", "v", 1, 16, true));
        }

        [Test]
        public void Reason_HasATextForEveryRefusal()
        {
            foreach (var r in new[] { JoinResult.VersionMismatch, JoinResult.Full, JoinResult.MatchStarted })
                Assert.IsNotEmpty(LobbyRules.Reason(r), r.ToString());
            Assert.IsEmpty(LobbyRules.Reason(JoinResult.Ok));
        }

        [Test]
        public void Bots_FillTheMatchToTheTarget_AndNeverPassTheCap()
        {
            Assert.AreEqual(15, LobbyRules.DefaultBots(1, 16));
            Assert.AreEqual(12, LobbyRules.DefaultBots(4, 16));
            Assert.AreEqual(0, LobbyRules.DefaultBots(20, 16));
            Assert.AreEqual(LobbyRules.MaxParticipants - 3, LobbyRules.ClampBots(99, 3));
            Assert.AreEqual(0, LobbyRules.ClampBots(-5, 3));
        }

        [TestCase("192.168.0.5:7771", "192.168.0.5", 7771, true)]
        [TestCase(" 10.0.0.2 ", "10.0.0.2", 7770, true)]
        [TestCase("host.example:80", "host.example", 80, true)]
        [TestCase("1.2.3.4:0", "", 0, false)]
        [TestCase("1.2.3.4:99999", "", 0, false)]
        [TestCase(":7770", "", 0, false)]
        [TestCase("", "", 0, false)]
        public void TryParseAddress_Cases(string text, string host, int port, bool ok)
        {
            Assert.AreEqual(ok, LobbyRules.TryParseAddress(text, 7770, out var h, out var p));
            if (!ok) return;
            Assert.AreEqual(host, h);
            Assert.AreEqual(port, (int)p);
        }

        [Test]
        public void Roster_StartsWhenEveryGuestIsReady_TheHostAlwaysIs()
        {
            var r = new LobbyRoster { HostId = 0 };
            Assert.IsFalse(r.AllReady, "an empty room cannot start");
            r.Add(0, "호스트");
            Assert.IsTrue(r.AllReady, "alone, the host can start");
            r.Add(3, "");
            Assert.IsFalse(r.AllReady);
            Assert.AreEqual("플레이어 4", r.Members[1].Name);
            r.SetReady(3, true);
            Assert.IsTrue(r.AllReady);
            r.SetReady(3, false);
            r.Remove(3);
            Assert.IsTrue(r.AllReady, "a guest who leaves no longer holds the start");
        }
    }
}

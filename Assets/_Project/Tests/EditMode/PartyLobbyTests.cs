using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class PartyLobbyTests
    {
        static PartyLobby Squad()
        {
            var p = new PartyLobby(1, "해무");
            p.Join(2, "도윤"); p.Join(3, "서아");
            return p;
        }

        [Test]
        public void Solo_StartsAtOnce_AndShowsSoloLabel()
        {
            var b = new PartyLobby(1, "해무").ButtonFor(1);
            Assert.AreEqual(LobbyAction.Start, b.Action);
            Assert.AreEqual("혼자 시작", b.Label);
            Assert.IsTrue(b.Enabled);
        }

        [Test]
        public void Joining_MakesSquad_AndBlocksStartUntilAllReady()
        {
            var p = Squad();
            Assert.AreEqual(TeamMode.Squad, p.Mode);
            var b = p.ButtonFor(1);
            Assert.IsFalse(b.Enabled);
            Assert.AreEqual("도윤 님 외 1명이 준비 중입니다…", b.Reason);
            p.SetReady(2, true);
            Assert.AreEqual("서아 님이 준비 중입니다…", p.ButtonFor(1).Reason);
            p.SetReady(3, true);
            Assert.IsTrue(p.ButtonFor(1).Enabled);
            Assert.AreEqual("파티 시작", p.ButtonFor(1).Label);
        }

        [Test]
        public void Members_CannotChangeModeOrView_LeaderCan()
        {
            var p = Squad();
            Assert.IsFalse(p.SetMode(2, TeamMode.Solo));
            Assert.IsFalse(p.SetView(2, Perspective.First));
            Assert.IsTrue(p.SetView(1, Perspective.First));
            Assert.IsFalse(p.SetMode(1, TeamMode.Solo), "solo needs an empty party");
            Assert.IsFalse(p.CanChange(2));
        }

        [Test]
        public void PartyBiggerThanTheTeam_CannotPickThatModeOrStart()
        {
            var rules = TeamRules.Current;
            TeamRules.Current = new TeamRules { SquadSize = 3 };
            try
            {
                var p = Squad(); p.Join(4, "x");
                p.SetReady(2, true); p.SetReady(3, true); p.SetReady(4, true);
                Assert.IsFalse(p.SetMode(1, TeamMode.Duo), "4 people do not fit a duo (#499)");
                var b = p.ButtonFor(1);
                Assert.IsFalse(b.Enabled, "4 people do not fit a squad of 3");
                StringAssert.Contains("3명", b.Reason);
                Assert.IsFalse(p.Start(1));
            }
            finally { TeamRules.Current = rules; }
        }

        [Test]
        public void Matching_LocksEverything_AndLeaderCancels()
        {
            var p = Squad();
            p.SetReady(2, true); p.SetReady(3, true);
            Assert.IsFalse(p.Start(2), "only the leader starts");
            Assert.IsTrue(p.Start(1));
            Assert.IsFalse(p.SetView(1, Perspective.First));
            Assert.IsFalse(p.Join(4, "x"));
            Assert.AreEqual("매칭 취소 · 01:05", p.ButtonFor(1, 65).Label);
            Assert.AreEqual(LobbyAction.LeaveParty, p.ButtonFor(2).Action);
            Assert.IsFalse(p.CancelMatch(2));
            Assert.IsTrue(p.CancelMatch(1));
            Assert.IsFalse(p.Matching);
        }

        [Test]
        public void LeaderLeaving_PassesLeadership_AndKeepsMatching()
        {
            var p = Squad();
            p.SetReady(2, true); p.SetReady(3, true); p.Start(1);
            p.Leave(1);
            Assert.AreEqual(2, p.LeaderId);
            Assert.IsTrue(p.Matching);
            Assert.AreEqual(LobbyAction.CancelMatch, p.ButtonFor(2).Action);
            Assert.AreEqual(TeamMode.Squad, p.Mode, "mode kept while matching");
        }

        [Test]
        public void LastMemberLeaving_BeforeMatching_ReturnsToSolo()
        {
            var p = new PartyLobby(1, "해무");
            p.Join(2, "도윤");
            p.Leave(2);
            Assert.AreEqual(TeamMode.Solo, p.Mode);
            Assert.AreEqual(1, p.Count);
        }

        [Test]
        public void Squad_IsCappedAtFour()
        {
            var p = Squad();
            Assert.IsTrue(p.Join(4, "a"));
            Assert.IsFalse(p.Join(5, "b"));
            Assert.AreEqual(4, p.Members.Count);
        }

        [Test]
        public void MatchFound_ClearsReady()
        {
            var p = Squad();
            p.SetReady(2, true); p.SetReady(3, true); p.Start(1);
            p.MatchFound();
            Assert.IsFalse(p.Matching);
            Assert.IsTrue(p.Members.All(m => !m.Ready));
        }

        [Test]
        public void Assembly_CountdownAndFill()
        {
            Assert.AreEqual(-1, AssemblyRules.Remaining(12, 20f, -1f));
            Assert.IsTrue(AssemblyRules.ShouldStartCountdown(30));
            Assert.IsFalse(AssemblyRules.ShouldStartCountdown(29));
            Assert.AreEqual(30, AssemblyRules.Remaining(30, 50f, 0f));
            Assert.AreEqual(0, AssemblyRules.Remaining(30, 90f, 31f));
            Assert.AreEqual(0, AssemblyRules.Remaining(12, 120f, -1f), "max wait ends it");
            Assert.AreEqual(0, AssemblyRules.Remaining(50, 5f, -1f), "full area starts at once");
            Assert.AreEqual(38, AssemblyRules.Bots(12));
        }

        [Test]
        public void QueueText_ShowsOnlyRealState()
        {
            Assert.AreEqual("", QueueText.Line(new MatchQueueState { Open = true, Waiting = 3, Target = 50, SecondsLeft = 20 }, false), "not waiting: nothing");
            Assert.AreEqual(QueueText.Requested, QueueText.Line(default, true), "asked, the server has not opened a window yet");
            Assert.AreEqual("매칭 중 12/50 · 남은 00:24", QueueText.Line(new MatchQueueState { Open = true, Waiting = 12, Target = 50, SecondsLeft = 23.2f }, true));
            Assert.AreEqual("매칭 중 1/50 · 남은 01:05", QueueText.Line(new MatchQueueState { Open = true, Waiting = 1, Target = 50, SecondsLeft = 65f }, true));
            Assert.AreEqual("매치를 만드는 중입니다…", QueueText.Line(new MatchQueueState { Open = true, Started = true, Waiting = 12, Target = 50 }, true));
            Assert.IsNotEmpty(QueueText.NotConnected);
            Assert.IsNotEmpty(QueueText.Cancelled);
        }
    }
}

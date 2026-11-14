using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-130: the marker wheel's geometry, the six marker types, the ping lifetime and the rate limit.
    public class PingRulesTests
    {
        static TeamRules Rules => new TeamRules();

        [Test]
        public void Wheel_SegmentsRunClockwiseFromTheTop()
        {
            for (int i = 0; i < Pings.KindCount; i++)
            {
                float a = i * 2f * Mathf.PI / Pings.KindCount;
                Assert.AreEqual(i, Pings.SegmentAt(Mathf.Sin(a) * 80f, Mathf.Cos(a) * 80f, Pings.KindCount, 20f), "centre of part " + i);
            }
            Assert.AreEqual(0, Pings.SegmentAt(0f, 50f, 6, 20f), "straight up");
            Assert.AreEqual(1, Pings.SegmentAt(50f, 30f, 6, 20f), "up and right");
            Assert.AreEqual(3, Pings.SegmentAt(0f, -50f, 6, 20f), "straight down");
            Assert.AreEqual(0, Pings.SegmentAt(-10f, 50f, 6, 20f), "just left of up is still the top part");
            Assert.AreEqual(5, Pings.SegmentAt(-50f, 30f, 6, 20f), "up and left");
        }

        [Test]
        public void Wheel_DeadZoneSelectsNothing()
        {
            Assert.AreEqual(-1, Pings.SegmentAt(0f, 0f, 6, 20f));
            Assert.AreEqual(-1, Pings.SegmentAt(10f, 10f, 6, 20f));
            Assert.AreEqual(-1, Pings.SegmentAt(5f, 5f, 0, 1f), "no parts");
        }

        [Test]
        public void SixMarkerTypes_HaveDistinctNames()
        {
            Assert.AreEqual(6, Pings.KindCount);
            var names = Enumerable.Range(0, Pings.KindCount).Select(i => Pings.Name((PingKind)i)).ToArray();
            Assert.AreEqual(6, names.Distinct().Count());
            Assert.IsTrue(names.All(n => !string.IsNullOrEmpty(n)));
            Assert.IsTrue(Pings.IsValid(0));
            Assert.IsTrue(Pings.IsValid(5));
            Assert.IsFalse(Pings.IsValid(6));
            Assert.IsFalse(Pings.IsValid(-1));
        }

        [Test]
        public void Upgrade_PlainPingOnAnEnemyBecomesEnemySpotted()
        {
            Assert.AreEqual(PingKind.Enemy, Pings.Upgrade(PingKind.Go, true));
            Assert.AreEqual(PingKind.Enemy, Pings.Upgrade(PingKind.Here, true));
            Assert.AreEqual(PingKind.Go, Pings.Upgrade(PingKind.Go, false));
            Assert.AreEqual(PingKind.Loot, Pings.Upgrade(PingKind.Loot, true), "a chosen kind stays");
            Assert.AreEqual(PingKind.Danger, Pings.Upgrade(PingKind.Danger, true));
        }

        [Test]
        public void Limiter_AllowsOnePingPerInterval_PerOwner()
        {
            var rules = Rules;
            var limiter = new PingLimiter();
            Assert.IsTrue(limiter.TryAccept(1, 10f, rules));
            Assert.IsFalse(limiter.TryAccept(1, 10f + rules.PingMinIntervalSeconds * 0.5f, rules), "too soon");
            Assert.IsTrue(limiter.TryAccept(2, 10.1f, rules), "another owner has its own clock");
            Assert.IsTrue(limiter.TryAccept(1, 10f + rules.PingMinIntervalSeconds + 0.01f, rules));
            Assert.IsFalse(limiter.TryAccept(1, 10f + rules.PingMinIntervalSeconds + 0.02f, rules), "a refused ping does not move the clock back, an accepted one does");
        }

        [Test]
        public void Board_PingsLiveTheirLifetime_ThenGo()
        {
            var rules = Rules;
            var board = new PingBoard();
            board.Add(7, 2, PingKind.Danger, 1f, 2f, 3f, 100f, rules);
            Assert.AreEqual(1, board.Count);
            board.Prune(100f + rules.PingLifeSeconds - 0.01f, rules);
            Assert.AreEqual(1, board.Count, "still alive just before its time");
            board.Prune(100f + rules.PingLifeSeconds, rules);
            Assert.AreEqual(0, board.Count);
            Assert.AreEqual(1, board.Total, "the count of pings ever added stays");
        }

        [Test]
        public void Board_KeepsTheNewestPerOwner_AndOwnersAreIndependent()
        {
            var rules = Rules;
            var board = new PingBoard();
            for (int i = 0; i < rules.PingMaxPerOwner + 2; i++) board.Add(1, 1, PingKind.Go, i, 0f, 0f, i, rules);
            board.Add(2, 2, PingKind.Loot, 99f, 0f, 0f, 9f, rules);
            Assert.AreEqual(rules.PingMaxPerOwner + 1, board.Count);
            var mine = board.Active.Where(e => e.Owner == 1).Select(e => e.X).OrderBy(x => x).ToArray();
            Assert.AreEqual(rules.PingMaxPerOwner, mine.Length);
            Assert.AreEqual(2f, mine[0], "the oldest were pushed out");
            Assert.AreEqual(1, board.Active.Count(e => e.Owner == 2));
            Assert.AreEqual(board.Active.Select(e => e.Id).Distinct().Count(), board.Count, "ids are unique");
        }

        [Test]
        public void Alpha_FadesOverTheLastSecond()
        {
            var rules = Rules;
            var e = new PingEntry { Born = 0f };
            Assert.AreEqual(1f, PingBoard.Alpha(e, 1f, rules), 1e-5f);
            Assert.AreEqual(0.5f, PingBoard.Alpha(e, rules.PingLifeSeconds - 0.5f, rules), 1e-5f);
            Assert.AreEqual(0f, PingBoard.Alpha(e, rules.PingLifeSeconds, rules), 1e-5f);
        }

        [Test]
        public void DataFile_HasThePingNumbers()
        {
            var rules = JsonUtility.FromJson<GameRules>(Resources.Load<TextAsset>("GameData/rules").text).Team;
            var defaults = new TeamRules();
            Assert.AreEqual(defaults.PingLifeSeconds, rules.PingLifeSeconds, 1e-6f);
            Assert.AreEqual(defaults.PingMinIntervalSeconds, rules.PingMinIntervalSeconds, 1e-6f);
            Assert.AreEqual(defaults.PingMaxPerOwner, rules.PingMaxPerOwner);
            Assert.AreEqual(defaults.PingMaxMeters, rules.PingMaxMeters, 1e-6f);
            Assert.Greater(rules.PingLifeSeconds, 1f);
        }
    }
}

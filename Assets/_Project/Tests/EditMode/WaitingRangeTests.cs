using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #421 (ADR-501): the island's shooting range as pure rules — who may fire, what a shot does while people wait, where the booths
    // and their targets stand, and the room's target score.
    public class WaitingRangeTests
    {
        [Test]
        public void MayFire_OnlyInABooth_WithTheBoothGun_WhileWaiting()
        {
            Assert.IsTrue(WaitingRange.MayFire(waiting: true, inBooth: true, holdsLoan: true));
            Assert.IsFalse(WaitingRange.MayFire(waiting: true, inBooth: false, holdsLoan: true), "outside the booth");
            Assert.IsFalse(WaitingRange.MayFire(waiting: true, inBooth: true, holdsLoan: false), "in the booth with anything but the booth gun");
            Assert.IsFalse(WaitingRange.MayFire(waiting: true, inBooth: false, holdsLoan: false));
            Assert.IsTrue(WaitingRange.MayFire(waiting: false, inBooth: false, holdsLoan: false), "the match itself is not the range's business");
        }

        [Test]
        public void Shot_ScoresOnATargetFromTheBooth_PeopleAreNeverHit()
        {
            Assert.AreEqual(RangeShot.Score, WaitingRange.Judge(waiting: true, shooterInBooth: true, RangeHit.Target));
            Assert.AreEqual(RangeShot.Absorb, WaitingRange.Judge(true, false, RangeHit.Target), "a target hit from outside counts nothing (and hurts nothing)");
            Assert.AreEqual(RangeShot.Absorb, WaitingRange.Judge(true, true, RangeHit.Body), "a person or a bot in the way takes nothing, no hit marker");
            Assert.AreEqual(RangeShot.Absorb, WaitingRange.Judge(true, false, RangeHit.Body));
            Assert.AreEqual(RangeShot.Pass, WaitingRange.Judge(true, true, RangeHit.World), "walls stop the shot as always");
            Assert.AreEqual(RangeShot.Pass, WaitingRange.Judge(false, true, RangeHit.Body), "after the island the match decides");
            Assert.AreEqual(RangeShot.Pass, WaitingRange.Judge(false, false, RangeHit.Target));
        }

        [Test]
        public void BoothAt_FindsTheShootersStand_NotTheLaneNorTheHall()
        {
            for (int i = 0; i < WaitingIsland.Booths.Length; i++)
            {
                var stand = WaitingRange.Stand(i);
                Assert.AreEqual(i, WaitingRange.BoothAt(stand.x, stand.z), "booth " + i + " stand");
                var t = WaitingRange.Target(i, 0);
                Assert.AreEqual(-1, WaitingRange.BoothAt(t.x, t.z), "the targets stand down the lane, outside the booth");
                Assert.Less(t.x, stand.x - 6f, "the lane is several metres long");
                Assert.Less(t.x * t.x + t.z * t.z, (WaitingIsland.Radius - 1f) * (WaitingIsland.Radius - 1f), "targets inside the wall");
            }
            Assert.AreEqual(-1, WaitingRange.BoothAt(0f, 0f), "the middle of the island");
            Assert.AreEqual(-1, WaitingRange.BoothAt(WaitingIsland.Booths[1].x + WaitingRange.StandBack + 1.5f, WaitingIsland.Booths[1].z), "a step behind the stand");
            // Leaving needs a little more than entering, so standing on the line does not lend and take back every tick.
            float edge = WaitingIsland.Booths[0].x + WaitingRange.StandBack + 0.1f;
            Assert.AreEqual(-1, WaitingRange.BoothAt(edge, WaitingIsland.Booths[0].z));
            Assert.AreEqual(0, WaitingRange.BoothAt(edge, WaitingIsland.Booths[0].z, WaitingRange.LeaveMargin));
        }

        [Test]
        public void NobodySpawnsInABoothOrALane()
        {
            foreach (var p in WaitingIsland.SpawnPoints())
            {
                float lx = p.x - WaitingIsland.CentreX, lz = p.z - WaitingIsland.CentreZ;
                Assert.AreEqual(-1, WaitingRange.BoothAt(lx, lz, 0.5f), $"spawn ({lx:0.0}, {lz:0.0})");
                Assert.IsFalse(WaitingRange.InLane(lx, lz), $"spawn ({lx:0.0}, {lz:0.0}) in a lane");
            }
            Assert.AreEqual(WaitingIsland.SpawnCount, WaitingIsland.SpawnPoints().Count, "still a place for everyone");
        }

        [Test]
        public void TargetScore_AddsUp_PerShooter_AndRanks()
        {
            var r = new RoomRecords();
            r.Add(1, "하늘", 1); r.Add(2, "루나", 1); r.Add(1, "하늘", 1);
            Assert.AreEqual(2, r.ScoreOf(1));
            Assert.AreEqual(1, r.ScoreOf(2));
            Assert.AreEqual(0, r.ScoreOf(3));
            var top = r.Top(3);
            Assert.AreEqual(2, top.Count);
            Assert.AreEqual("하늘", top[0].Name); Assert.AreEqual(2, top[0].Score);
            r.Clear();
            Assert.AreEqual(0, r.Top(3).Count, "nothing is kept after the island");
        }
    }
}

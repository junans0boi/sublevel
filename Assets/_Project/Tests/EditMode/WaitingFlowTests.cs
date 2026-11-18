using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #415 (ADR-382): the whole waiting flow as the server drives it — join, stand on the island, countdown, deploy — without Unity objects.
    public class WaitingFlowTests
    {
        static MatchRules Rules() => new MatchRules { Participants = 80, MinBots = 16, WaitMaxSeconds = 90f, WaitFullSeconds = 15f };

        [Test]
        public void PeopleStandOnDistinctPlacesOfTheIsland()
        {
            var f = new WaitingFlow(Rules());
            var a = f.Join(1); var b = f.Join(2);
            Assert.IsNotNull(a); Assert.IsNotNull(b);
            Assert.AreNotEqual(a.Value, b.Value);
            Assert.IsTrue(WaitingIsland.Inside(a.Value.x, a.Value.z)); Assert.IsTrue(WaitingIsland.Inside(b.Value.x, b.Value.z));
            Assert.AreEqual(2, f.People);
        }

        [Test]
        public void Return_GetsTheSamePlace_AndKeepsTheDeploymentIndex()
        {
            var f = new WaitingFlow(Rules());
            var first = f.Join(7); f.Join(8);
            f.Leave(7);
            Assert.AreEqual(1, f.People);
            var back = f.Join(7);
            Assert.AreEqual(first, back);
            Assert.AreEqual(0, f.DeploymentIndex(7));
            Assert.AreEqual(1, f.DeploymentIndex(8));
        }

        [Test]
        public void TwoPeopleAndBots_CountdownEnds_ThenDeploysOnce()
        {
            var f = new WaitingFlow(Rules());
            f.Join(1); f.Join(2);
            f.SetBots(30);
            Assert.IsFalse(f.Tick(0.0));
            Assert.AreEqual(WaitingPhase.Waiting, f.Phase);
            Assert.AreEqual(90f, f.Last.SecondsLeft, 1e-3f);
            Assert.IsFalse(f.Tick(89.0));
            Assert.IsTrue(f.Tick(90.0), "the clock ends: send them to the elevators");
            Assert.AreEqual(WaitingPhase.Deploying, f.Phase);
            Assert.AreEqual(StartReason.TimeUp, f.Last.Reason);
            Assert.AreEqual(78, f.Last.BotsTotal);
            Assert.IsFalse(f.Tick(91.0), "only once");
        }

        [Test]
        public void FullRoom_DeploysAfter15Seconds_WithTheFullReason()
        {
            var f = new WaitingFlow(Rules());
            f.Join(1); f.Join(2);
            f.SetBots(78);
            Assert.IsFalse(f.Tick(10.0));
            Assert.IsFalse(f.Tick(24.0));
            Assert.IsTrue(f.Tick(25.0));
            Assert.AreEqual(StartReason.Full, f.Last.Reason);
        }

        [Test]
        public void EveryoneLeaves_NothingDeploys()
        {
            var f = new WaitingFlow(Rules());
            f.Join(1);
            f.Tick(0.0);
            f.Leave(1);
            Assert.IsFalse(f.Tick(500.0));
            Assert.AreEqual(WaitingPhase.Waiting, f.Phase);
        }

        [Test]
        public void Clamp_PutsOutsidersBackInside_Server_Authority()
        {
            var (x, z) = WaitingFlow.Clamp(WaitingIsland.CentreX + 500f, WaitingIsland.CentreZ + 500f);
            Assert.IsTrue(WaitingIsland.Inside(x, z));
            Assert.LessOrEqual(System.Math.Sqrt((x - WaitingIsland.CentreX) * (x - WaitingIsland.CentreX) + (z - WaitingIsland.CentreZ) * (z - WaitingIsland.CentreZ)), WaitingIsland.WalkRadius + 0.01);
        }

        [Test]
        public void FullRoomOfPeople_HasNoPlaceForTheNext()
        {
            var f = new WaitingFlow(Rules());
            int placed = 0;
            for (int i = 0; i < WaitingIsland.SpawnCount + 5; i++) if (f.Join(i) != null) placed++;
            Assert.GreaterOrEqual(placed, 80);
            Assert.LessOrEqual(placed, WaitingIsland.SpawnCount);
        }
    }
}

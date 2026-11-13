using System.Collections.Generic;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests
{
    // Who a dead player watches (ADR-094): teammates first, then the killer and the killer's killer, never the dead.
    public class SpectateTests
    {
        static SpectateCandidate C(int id, int team, bool alive) => new SpectateCandidate { Id = id, Team = team, Alive = alive };

        // 1 = me (dead). Solo: 2 killed me, 3 killed 2, 4 lives on.
        static readonly Dictionary<int, int> SoloKillers = new Dictionary<int, int> { { 1, 2 }, { 2, 3 } };
        static int SoloKillerOf(int id) => SoloKillers.TryGetValue(id, out int k) ? k : -1;

        [Test]
        public void Solo_WatchesTheKiller()
        {
            var all = new List<SpectateCandidate> { C(1, 0, false), C(4, 0, true), C(2, 0, true), C(3, 0, true) };
            Assert.AreEqual(2, SpectateCamera.FirstTarget(all, 1, 0, 2, SoloKillerOf));
        }

        [Test]
        public void Solo_KillerDead_FollowsTheChainToTheirKiller()
        {
            var all = new List<SpectateCandidate> { C(1, 0, false), C(4, 0, true), C(2, 0, false), C(3, 0, true) };
            Assert.AreEqual(3, SpectateCamera.FirstTarget(all, 1, 0, 2, SoloKillerOf));
        }

        [Test]
        public void Solo_WholeChainDead_WatchesAnyoneAlive()
        {
            var all = new List<SpectateCandidate> { C(1, 0, false), C(2, 0, false), C(3, 0, false), C(4, 0, true) };
            Assert.AreEqual(4, SpectateCamera.FirstTarget(all, 1, 0, 2, SoloKillerOf));
        }

        [Test]
        public void NoKiller_AndNobodyAlive_IsNone()
        {
            var all = new List<SpectateCandidate> { C(1, 0, false), C(2, 0, false) };
            Assert.AreEqual(-1, SpectateCamera.FirstTarget(all, 1, 0, -1, SoloKillerOf));
        }

        [Test]
        public void ALoopingChain_Ends()
        {
            var all = new List<SpectateCandidate> { C(1, 0, false), C(2, 0, false), C(3, 0, false), C(5, 0, true) };
            int Loop(int id) => id == 2 ? 3 : id == 3 ? 2 : -1;
            Assert.AreEqual(5, SpectateCamera.FirstTarget(all, 1, 0, 2, Loop));
        }

        [Test]
        public void Team_WatchesALivingTeammate_BeforeTheKiller()
        {
            // Me (1) and 5 on team 1; 6 dead on team 1; killer 2 on team 2.
            var all = new List<SpectateCandidate> { C(1, 1, false), C(2, 2, true), C(6, 1, false), C(5, 1, true) };
            Assert.AreEqual(5, SpectateCamera.FirstTarget(all, 1, 1, 2, SoloKillerOf));
        }

        [Test]
        public void Team_NoTeammateLeft_FallsBackToTheKiller()
        {
            var all = new List<SpectateCandidate> { C(1, 1, false), C(2, 2, true), C(5, 1, false) };
            Assert.AreEqual(2, SpectateCamera.FirstTarget(all, 1, 1, 2, SoloKillerOf));
        }

        [Test]
        public void Cycle_SkipsTheDead_AndWraps_BothWays()
        {
            var all = new List<SpectateCandidate> { C(1, 0, false), C(2, 0, true), C(3, 0, false), C(4, 0, true), C(5, 0, true) };
            Assert.AreEqual(4, SpectateCamera.Cycle(all, 1, 0, 2, 1));
            Assert.AreEqual(5, SpectateCamera.Cycle(all, 1, 0, 4, 1));
            Assert.AreEqual(2, SpectateCamera.Cycle(all, 1, 0, 5, 1), "wraps past me");
            Assert.AreEqual(5, SpectateCamera.Cycle(all, 1, 0, 2, -1));
            Assert.AreEqual(2, SpectateCamera.Cycle(all, 1, 0, 4, -1), "skips the dead 3");
        }

        [Test]
        public void Cycle_StaysOnTeammates_WhileAnyLives()
        {
            var all = new List<SpectateCandidate> { C(1, 1, false), C(2, 2, true), C(5, 1, true), C(7, 2, true), C(8, 1, true) };
            Assert.AreEqual(8, SpectateCamera.Cycle(all, 1, 1, 5, 1));
            Assert.AreEqual(5, SpectateCamera.Cycle(all, 1, 1, 8, 1), "never the enemy 2 or 7");
        }

        [Test]
        public void Cycle_TheOnlyOneLeft_StaysOnThem()
        {
            var all = new List<SpectateCandidate> { C(1, 0, false), C(2, 0, true), C(3, 0, false) };
            Assert.AreEqual(2, SpectateCamera.Cycle(all, 1, 0, 2, 1));
        }

        [Test]
        public void SurvivalTime_Reads_MinutesAndSeconds()
        {
            Assert.AreEqual("4:07", ResultScreen.TimeOf(247));
            Assert.AreEqual("0:00", ResultScreen.TimeOf(0));
        }
    }
}

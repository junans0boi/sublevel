using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-055 (1)(5): who starts in which elevator. 100 seeds x 1..24 participants over the World map's elevators.
    public class DeploymentTests
    {
        const int Seeds = 100;
        const int MaxParticipants = 24;

        // The World layout: 13 elevators in 4 regions (A 3, B 3, C 4, D 3), region ids in plan order.
        static readonly ElevatorSlot[] World = Layout(3, 3, 4, 3);

        static ElevatorSlot[] Layout(params int[] perRegion)
        {
            var slots = new List<ElevatorSlot>();
            for (int r = 0; r < perRegion.Length; r++)
                for (int i = 0; i < perRegion[r]; i++) slots.Add(new ElevatorSlot(r));
            return slots.ToArray();
        }

        static int Seed(int s) => s * 7919 + 13;

        [Test]
        public void Defaults_MatchTheDecision()
        {
            var rules = new DeploymentRules();
            Assert.AreEqual(3f, rules.DoorDelaySeconds, 1e-6f);
            Assert.AreEqual(6f, rules.ProtectionSeconds, 1e-6f);
            Assert.AreEqual(5, Deployment.RegionCap(13, 4));
            Assert.AreEqual(2, Deployment.RegionCap(1, 4));
            Assert.AreEqual(7, Deployment.RegionCap(24, 4));
        }

        [Test]
        public void EveryParticipant_GetsAValidElevator()
        {
            for (int s = 0; s < Seeds; s++)
                for (int n = 1; n <= MaxParticipants; n++)
                {
                    var assigned = Deployment.Assign(n, World, Seed(s));
                    Assert.AreEqual(n, assigned.Length);
                    foreach (int e in assigned) Assert.That(e, Is.InRange(0, World.Length - 1), $"seed {Seed(s)}, {n} participants");
                }
        }

        [Test]
        public void NoTwoParticipants_ShareAnElevator_WhileAFreeOneExists()
        {
            for (int s = 0; s < Seeds; s++)
                for (int n = 1; n <= MaxParticipants; n++)
                {
                    var assigned = Deployment.Assign(n, World, Seed(s));
                    var load = new int[World.Length];
                    for (int p = 0; p < n; p++)
                    {
                        bool freeExists = load.Any(l => l == 0);
                        if (freeExists) Assert.AreEqual(0, load[assigned[p]], $"seed {Seed(s)}, {n} participants: participant {p} shares an elevator while one is free");
                        load[assigned[p]]++;
                    }
                    // Shared elevators stay balanced: none holds two more than another.
                    Assert.LessOrEqual(load.Max() - load.Min(), 1, $"seed {Seed(s)}, {n} participants: uneven sharing");
                }
        }

        [Test]
        public void NoRegion_ExceedsItsCap()
        {
            for (int s = 0; s < Seeds; s++)
                for (int n = 1; n <= MaxParticipants; n++)
                {
                    var assigned = Deployment.Assign(n, World, Seed(s));
                    int cap = Deployment.RegionCap(n, 4);
                    foreach (var group in assigned.GroupBy(e => World[e].Region))
                        Assert.LessOrEqual(group.Count(), cap, $"seed {Seed(s)}, {n} participants: region {group.Key}");
                }
        }

        [Test]
        public void SameInput_SameAnswer()
        {
            for (int s = 0; s < Seeds; s++)
                for (int n = 1; n <= MaxParticipants; n++)
                    CollectionAssert.AreEqual(Deployment.Assign(n, World, Seed(s)), Deployment.Assign(n, World, Seed(s)));
        }

        [Test]
        public void DifferentSeeds_GiveDifferentAnswers()
        {
            for (int n = 2; n <= MaxParticipants; n++)
            {
                var distinct = new HashSet<string>();
                for (int s = 0; s < Seeds; s++) distinct.Add(string.Join(",", Deployment.Assign(n, World, Seed(s))));
                Assert.Greater(distinct.Count, Seeds / 2, $"{n} participants: seeds barely change the deployment");
            }
        }

        [Test]
        public void ASinglePlayer_LandsInEveryRegion_AcrossSeeds()
        {
            var regions = new int[4];
            for (int s = 0; s < Seeds; s++) regions[World[Deployment.Assign(1, World, Seed(s))[0]].Region]++;
            for (int r = 0; r < regions.Length; r++)
                Assert.Greater(regions[r], Seeds / 10, $"region {r} got {regions[r]} of {Seeds} single-player starts");
        }

        [Test]
        public void Participants_AreSpreadOverTheRegions()
        {
            // Four or more participants: every region is used (the regions take turns).
            for (int s = 0; s < Seeds; s++)
                for (int n = 4; n <= MaxParticipants; n++)
                    Assert.AreEqual(4, Deployment.Assign(n, World, Seed(s)).Select(e => World[e].Region).Distinct().Count(), $"seed {Seed(s)}, {n}");
        }

        [Test]
        public void EdgeCases_NoParticipants_NoElevators_UnsortedRegionIds()
        {
            Assert.AreEqual(0, Deployment.Assign(0, World, 1).Length);
            CollectionAssert.AreEqual(new[] { -1, -1 }, Deployment.Assign(2, new ElevatorSlot[0], 1));
            var odd = new[] { new ElevatorSlot(7), new ElevatorSlot(-2), new ElevatorSlot(7), new ElevatorSlot(40) };
            var assigned = Deployment.Assign(3, odd, 5);
            Assert.AreEqual(3, assigned.Distinct().Count());
            Assert.AreEqual(3, assigned.Select(e => odd[e].Region).Distinct().Count());
        }
    }
}

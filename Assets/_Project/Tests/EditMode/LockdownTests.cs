using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // M3 region lockdown (SPEC 3, ADR-058 (7)): the closing order, the final region, the timeline and the guarantee
    // that the regions still open stay connected through passes.
    public class LockdownTests
    {
        const int Seeds = 100;

        // The shipped island: A mountain, B wetland, C town, D factory. B and D do not touch.
        static readonly int[] From = { 0, 0, 1, 2 };
        static readonly int[] To = { 1, 2, 2, 3 };
        static readonly int[] SubZones = { 3, 4, 5, 4 };

        static LockdownPlan Make(int seed, LockdownRules rules = null) => Lockdown.Plan(4, From, To, seed, SubZones, rules);

        static bool[] OpenAt(LockdownPlan plan, float time)
        {
            var open = new bool[plan.RegionCount];
            for (int r = 0; r < open.Length; r++) open[r] = Lockdown.IsPassable(Lockdown.StateAt(plan, r, time));
            return open;
        }

        [Test]
        public void ClosingOrder_NeverContainsTheFinalRegion_AndNamesEveryOtherOnce()
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Make(seed);
                CollectionAssert.DoesNotContain(plan.Order, plan.FinalRegion, "seed " + seed);
                Assert.AreEqual(3, plan.Order.Length, "seed " + seed);
                Assert.AreEqual(3, plan.Order.Distinct().Count(), "seed " + seed);
                Assert.IsTrue(plan.FinalRegion >= 0 && plan.FinalRegion < 4);
                Assert.AreEqual(ZoneState.Safe, Lockdown.StateAt(plan, plan.FinalRegion, 1e6f), "the final region never closes (seed " + seed + ")");
            }
        }

        [Test]
        public void OpenRegions_StayConnectedAfterEveryClosing()
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Make(seed);
                var open = new bool[4];
                for (int i = 0; i < 4; i++) open[i] = true;
                for (int step = 0; step < plan.Order.Length; step++)
                {
                    open[plan.Order[step]] = false;
                    Assert.IsTrue(Lockdown.IsConnected(4, From, To, open), $"seed {seed}: open regions split after closing step {step} ({plan.Order[step]})");
                }
                // And the same read off the timeline, just after each lockdown begins.
                for (int step = 0; step < plan.Order.Length; step++)
                    Assert.IsTrue(Lockdown.IsConnected(4, From, To, OpenAt(plan, plan.LockdownAt[step] + 0.01f)), $"seed {seed}: timeline step {step}");
            }
        }

        [Test]
        public void SomeSeeds_NeedAReorder_BecauseBAndDOnlyMeetThroughAOrC()
        {
            // A naive order would cut the map in two for many seeds; the planner must not. B (1) and D (3) are leaves
            // of the chain B-A/C-D only through A and C, so closing A and C while B and D stay open is never allowed.
            int finalsBorD = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Make(seed);
                if (plan.FinalRegion != 1 && plan.FinalRegion != 3) continue;
                finalsBorD++;
                int other = plan.FinalRegion == 1 ? 3 : 1;
                // The other leaf has to go before the hubs between them are both gone.
                int hubA = plan.ClosingIndexOf(0), hubC = plan.ClosingIndexOf(2), leaf = plan.ClosingIndexOf(other);
                Assert.Less(leaf, System.Math.Max(hubA, hubC), "seed " + seed);
            }
            Assert.Greater(finalsBorD, 10, "the seeds should cover B and D as final regions");
        }

        [Test]
        public void StateTimeline_FollowsTheRules()
        {
            var rules = new LockdownRules();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Make(seed, rules);
                for (int i = 0; i < plan.Order.Length; i++)
                {
                    int r = plan.Order[i];
                    float w = rules.FirstWarningSeconds + i * rules.MinSecondsBetweenClosings;
                    Assert.AreEqual(w, plan.WarningAt[i], 1e-3f);
                    Assert.AreEqual(w + rules.WarningSeconds, plan.LockdownAt[i], 1e-3f);
                    Assert.AreEqual(w + rules.WarningSeconds + rules.LockdownSeconds, plan.BombardmentAt[i], 1e-3f);
                    Assert.AreEqual(w + rules.WarningSeconds + rules.LockdownSeconds + rules.BombardmentSeconds, plan.ClosedAt[i], 1e-3f);

                    Assert.AreEqual(ZoneState.Safe, Lockdown.StateAt(plan, r, 0f));
                    Assert.AreEqual(ZoneState.Safe, Lockdown.StateAt(plan, r, plan.WarningAt[i] - 0.01f));
                    Assert.AreEqual(ZoneState.Warning, Lockdown.StateAt(plan, r, plan.WarningAt[i]));
                    Assert.AreEqual(ZoneState.Warning, Lockdown.StateAt(plan, r, plan.LockdownAt[i] - 0.01f));
                    Assert.AreEqual(ZoneState.Lockdown, Lockdown.StateAt(plan, r, plan.LockdownAt[i]));
                    Assert.AreEqual(ZoneState.Bombardment, Lockdown.StateAt(plan, r, plan.BombardmentAt[i]));
                    Assert.AreEqual(ZoneState.Closed, Lockdown.StateAt(plan, r, plan.ClosedAt[i]));
                    Assert.AreEqual(ZoneState.Closed, Lockdown.StateAt(plan, r, plan.ClosedAt[i] + 9999f));
                }
            }
        }

        [Test]
        public void ClosingsAreSpacedByAtLeastTheMinimum()
        {
            var rules = new LockdownRules { MinSecondsBetweenClosings = 200f };
            var plan = Make(5, rules);
            for (int i = 1; i < plan.Order.Length; i++)
                Assert.GreaterOrEqual(plan.WarningAt[i] - plan.WarningAt[i - 1], 200f - 1e-3f);
        }

        [Test]
        public void FinalRegion_IsHiddenUntilTheLastAnnouncement()
        {
            var rules = new LockdownRules();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Make(seed, rules);
                Assert.AreEqual(plan.WarningAt[plan.Order.Length - 1], plan.FinalRevealAt, 1e-3f);
                Assert.IsFalse(Lockdown.IsFinalRevealed(plan, 0f), "seed " + seed);
                Assert.IsFalse(Lockdown.IsFinalRevealed(plan, plan.FinalRevealAt - 0.01f));
                Assert.AreEqual(-1, Lockdown.RevealedFinalRegion(plan, 0f));
                Assert.AreEqual(plan.FinalRegion, Lockdown.RevealedFinalRegion(plan, plan.FinalRevealAt));
                // Until then, more than one region could still be it.
                var candidates = new List<int>();
                Lockdown.Candidates(plan, plan.FinalRevealAt - 0.01f, candidates);
                Assert.GreaterOrEqual(candidates.Count, 2);
                CollectionAssert.Contains(candidates, plan.FinalRegion);
            }
        }

        [Test]
        public void FinalRegion_RevealPolicyIsAData_Value()
        {
            var rules = new LockdownRules { FinalRevealLastClosings = 2 };
            var plan = Make(3, rules);
            Assert.AreEqual(plan.WarningAt[1], plan.FinalRevealAt, 1e-3f);
            rules = new LockdownRules { FinalRevealLastClosings = 99 };
            plan = Make(3, rules);
            Assert.AreEqual(plan.WarningAt[0], plan.FinalRevealAt, 1e-3f);
        }

        [Test]
        public void FinalRegion_IsRandomBySeed_AndCoversEveryRegion()
        {
            var seen = new HashSet<int>();
            for (int seed = 0; seed < Seeds; seed++) seen.Add(Make(seed).FinalRegion);
            Assert.AreEqual(4, seen.Count);
        }

        [Test]
        public void SubZones_ThreeOrFourInPlay_LastOneSurvives()
        {
            var rules = new LockdownRules();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Make(seed, rules);
                int count = SubZones[plan.FinalRegion];
                Assert.AreEqual(count, plan.SubZoneCount);
                Assert.IsTrue(plan.FinalSubZones.Length >= 3 && plan.FinalSubZones.Length <= 4, $"seed {seed}: {plan.FinalSubZones.Length} sub-zones in play");
                Assert.AreEqual(plan.FinalSubZones.Length, plan.FinalSubZones.Distinct().Count());
                Assert.IsTrue(plan.FinalSubZones.All(i => i >= 0 && i < count));
                Assert.AreEqual(plan.FinalSubZones.Length - 1, plan.SubZoneWarningAt.Length);

                // Closes in order, after every region is closed, and nothing closes the survivor.
                float last = plan.AllRegionsClosedAt;
                for (int s = 0; s < plan.SubZoneWarningAt.Length; s++)
                {
                    Assert.GreaterOrEqual(plan.SubZoneWarningAt[s], last + rules.SubZoneStartDelaySeconds - 1e-3f);
                    Assert.Less(plan.SubZoneWarningAt[s], plan.SubZoneBombardmentAt[s]);
                    Assert.Less(plan.SubZoneBombardmentAt[s], plan.SubZoneClosedAt[s]);
                    Assert.AreEqual(ZoneState.Closed, Lockdown.SubZoneStateAt(plan, plan.FinalSubZones[s], plan.SubZoneClosedAt[s]));
                    last = plan.SubZoneWarningAt[s] - rules.SubZoneStartDelaySeconds;
                }
                Assert.AreEqual(ZoneState.Safe, Lockdown.SubZoneStateAt(plan, plan.ConvergenceSubZone, 1e6f));
                // Sub-zones that are not in play go down with the first step, so only the survivor is left.
                for (int i = 0; i < count; i++)
                    if (i != plan.ConvergenceSubZone)
                        Assert.AreEqual(ZoneState.Closed, Lockdown.SubZoneStateAt(plan, i, 1e6f), $"seed {seed} sub-zone {i}");
            }
        }

        [Test]
        public void Events_AreSortedAndNextEventAfterWalksThem()
        {
            var plan = Make(11);
            // 3 closings x 4 states + reveal + sub-zone steps x 3 states.
            Assert.AreEqual(12 + 1 + 3 * (plan.FinalSubZones.Length - 1), plan.Events.Length);
            for (int i = 1; i < plan.Events.Length; i++) Assert.GreaterOrEqual(plan.Events[i].Time, plan.Events[i - 1].Time);

            float t = -1f;
            int visited = 0;
            while (Lockdown.NextEventAfter(plan, t, out var e))
            {
                Assert.Greater(e.Time, t);
                t = e.Time;
                visited++;
                Assert.Less(visited, 1000);
            }
            Assert.Greater(visited, 0);
            Assert.IsFalse(Lockdown.NextEventAfter(plan, 1e9f, out _));
            Assert.IsTrue(Lockdown.NextEventAfter(plan, 0f, out var first));
            Assert.AreEqual(plan.WarningAt[0], first.Time, 1e-3f);
            Assert.AreEqual(ZoneState.Warning, first.State);
            Assert.AreEqual(plan.Order[0], first.Region);
        }

        [Test]
        public void StateEndsAt_PointsAtTheNextChange()
        {
            var plan = Make(2);
            int r = plan.Order[1];
            Assert.AreEqual(plan.WarningAt[1], Lockdown.StateEndsAt(plan, r, 0f), 1e-3f);
            Assert.AreEqual(plan.LockdownAt[1], Lockdown.StateEndsAt(plan, r, plan.WarningAt[1]), 1e-3f);
            Assert.AreEqual(plan.ClosedAt[1], Lockdown.StateEndsAt(plan, r, plan.BombardmentAt[1]), 1e-3f);
            Assert.AreEqual(-1f, Lockdown.StateEndsAt(plan, r, plan.ClosedAt[1]));
            Assert.AreEqual(-1f, Lockdown.StateEndsAt(plan, plan.FinalRegion, 0f));
            Assert.IsTrue(float.IsPositiveInfinity(Lockdown.OpenUntil(plan, plan.FinalRegion)));
        }

        [Test]
        public void Plan_IsDeterministic_AndSeedsDiffer()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var a = Make(seed);
                var b = Make(seed);
                Assert.AreEqual(a.FinalRegion, b.FinalRegion);
                CollectionAssert.AreEqual(a.Order, b.Order);
                CollectionAssert.AreEqual(a.FinalSubZones, b.FinalSubZones);
                Assert.AreEqual(a.Events.Length, b.Events.Length);
                for (int i = 0; i < a.Events.Length; i++)
                {
                    Assert.AreEqual(a.Events[i].Time, b.Events[i].Time);
                    Assert.AreEqual(a.Events[i].Region, b.Events[i].Region);
                    Assert.AreEqual(a.Events[i].State, b.Events[i].State);
                }
            }
            Assert.Greater(Enumerable.Range(0, 30).Select(s => string.Join(",", Make(s).Order) + "/" + Make(s).FinalRegion).Distinct().Count(), 3);
        }

        [Test]
        public void ShippedWorld_PlansWithFourRegions_AndBAndDDoNotTouch()
        {
            var world = WorldGenerator.Generate(WorldSpec.CreateDefault());
            Assert.AreEqual(4, world.Regions.Count);
            var touching = world.Passes.Select(p => (System.Math.Min(p.RegionA, p.RegionB), System.Math.Max(p.RegionA, p.RegionB))).ToHashSet();
            Assert.IsFalse(touching.Contains((1, 3)), "B and D should not share a pass");

            var geometry = ZoneGeometry.FromPlan(world);
            Assert.AreEqual(4, geometry.RegionCount);
            Assert.AreEqual(world.Passes.Count, geometry.PassCount);
            var a = world.Passes.Select(p => p.RegionA).ToArray();
            var b = world.Passes.Select(p => p.RegionB).ToArray();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Lockdown.Plan(world, seed);
                CollectionAssert.DoesNotContain(plan.Order, plan.FinalRegion);
                var open = new bool[4];
                for (int i = 0; i < 4; i++) open[i] = true;
                foreach (int r in plan.Order)
                {
                    open[r] = false;
                    Assert.IsTrue(Lockdown.IsConnected(4, a, b, open), "seed " + seed);
                }
                Assert.IsTrue(plan.FinalSubZones.Length >= 1, "the final region has sub-zones (seed " + seed + ")");
            }
        }

        [Test]
        public void Geometry_FindsTheRegionAPointIsIn_AndAgreesWithTheWorldPlan()
        {
            var world = WorldGenerator.Generate(WorldSpec.CreateDefault());
            var geometry = ZoneGeometry.FromPlan(world);
            int checkedPoints = 0;
            for (float x = 60f; x < 1000f; x += 80f)
                for (float z = 60f; z < 1000f; z += 80f)
                {
                    int expected = world.RegionAt(x, z);
                    if (expected < 0) continue; // sea: the plan knows the coast, the geometry only the cells
                    Assert.AreEqual(expected, geometry.RegionAt(x, z), $"({x},{z})");
                    checkedPoints++;
                }
            Assert.Greater(checkedPoints, 20);
            Assert.AreEqual(-1, geometry.RegionAt(-5f, 10f));
            Assert.AreEqual(-1, geometry.RegionAt(10f, 5000f));
            for (int s = 0; s < geometry.SubZoneTotal; s++)
                Assert.AreEqual(s, geometry.GlobalSubZone(geometry.SubRegion[s], geometry.LocalSubZone(s)));
        }

        [Test]
        public void RulesDefaults_AreSane_AndRadiationIsNeverInstant()
        {
            var r = new LockdownRules();
            Assert.AreEqual(10f, r.KeyOpenSeconds);
            Assert.AreEqual(0f, r.DamagePerSecond(ZoneState.Safe));
            Assert.AreEqual(0f, r.DamagePerSecond(ZoneState.Warning));
            Assert.Greater(r.DamagePerSecond(ZoneState.Lockdown), 0f);
            Assert.Greater(r.DamagePerSecond(ZoneState.Closed), r.DamagePerSecond(ZoneState.Lockdown));
            Assert.Greater(100f / r.DamagePerSecond(ZoneState.Lockdown), r.WarningSeconds / 2f, "lockdown radiation should leave time to find a key");
            Assert.Greater(r.RadiationGraceSeconds, 0f);
            var fast = r.Scaled(10f);
            Assert.AreEqual(r.WarningSeconds / 10f, fast.WarningSeconds, 1e-4f);
            Assert.AreEqual(r.RadiationDps, fast.RadiationDps);
        }

        [Test]
        public void BlastDamage_FallsOffBetweenTheRadii()
        {
            var r = new LockdownRules();
            Assert.AreEqual(r.BlastDamage, Lockdown.BlastDamageAt(0f, r));
            Assert.AreEqual(r.BlastDamage, Lockdown.BlastDamageAt(r.BlastInnerRadius, r));
            Assert.AreEqual(0f, Lockdown.BlastDamageAt(r.BlastOuterRadius, r));
            float mid = Lockdown.BlastDamageAt((r.BlastInnerRadius + r.BlastOuterRadius) * 0.5f, r);
            Assert.AreEqual(r.BlastDamage * 0.5f, mid, 1e-3f);
        }

        [Test]
        public void SingleRegionAndEmptyMaps_DoNotThrow()
        {
            var one = Lockdown.Plan(1, new int[0], new int[0], 1, new[] { 2 });
            Assert.AreEqual(0, one.Order.Length);
            Assert.AreEqual(0, one.FinalRegion);
            Assert.AreEqual(ZoneState.Safe, Lockdown.StateAt(one, 0, 1e6f));
            var none = Lockdown.Plan(0, new int[0], new int[0], 1);
            Assert.AreEqual(-1, none.FinalRegion);
            Assert.IsFalse(Lockdown.NextEventAfter(none, 0f, out _));
        }

        [Test]
        public void DisconnectedMaps_StillGetAnOrder()
        {
            var plan = Lockdown.Plan(4, new[] { 0 }, new[] { 1 }, 7);
            Assert.AreEqual(3, plan.Order.Length);
            CollectionAssert.DoesNotContain(plan.Order, plan.FinalRegion);
        }
    }
}

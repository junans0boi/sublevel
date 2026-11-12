using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Zone D blockout (M2-1): the generated layout must satisfy the zone design rules, and the rules must catch breakage.
    public class ZoneLayoutTests
    {
        static bool Violates(ZoneLayout layout, ZoneRule rule) => ZoneLayoutRules.Validate(layout).Any(v => v.Rule == rule);

        [Test]
        public void ZoneD_SatisfiesEveryDesignRule()
        {
            var violations = ZoneLayoutRules.Validate(ZoneDBlockout.Create());
            Assert.IsEmpty(violations, string.Join("\n", violations.Select(v => v.Rule + ": " + v.Detail)));
        }

        [Test]
        public void ZoneD_HasFourConvergencePointsAndFourElevators()
        {
            var layout = ZoneDBlockout.Create();
            CollectionAssert.AreEquivalent(new[] { "AssemblyHall", "WarehouseHall", "LoadingYard", "PowerPlaza" },
                layout.ConvergencePoints.Select(p => p.Name));
            CollectionAssert.AreEquivalent(new[] { "ElevatorNorth", "ElevatorSouth", "ElevatorEast", "ElevatorWest" },
                layout.Elevators.Select(e => e.Name));
            Assert.AreEqual(80f, layout.HalfX);
            Assert.AreEqual(60f, layout.HalfZ);
        }

        [Test]
        public void ZoneD_IsTheSameEveryTime()
        {
            var a = ZoneDBlockout.Create();
            var b = ZoneDBlockout.Create();
            Assert.AreEqual(a.Blocks.Count, b.Blocks.Count);
            for (int i = 0; i < a.Blocks.Count; i++) Assert.AreEqual(a.Blocks[i], b.Blocks[i], a.Blocks[i].Name);
            string Places(ZoneLayout l) => string.Join("|",
                l.ConvergencePoints.Select(p => $"{p.Name} {p.X} {p.Z} {p.ClearRadius}")
                    .Concat(l.Elevators.Select(e => $"{e.Name} {e.X} {e.Z} {e.FacingDeg}"))
                    .Concat(l.LootPoints.Select(p => $"{p.X} {p.Y} {p.Z}"))
                    .Concat(l.Landmarks.Select(p => $"{p.Name} {p.X} {p.Y} {p.Z}")));
            Assert.AreEqual(Places(a), Places(b));
        }

        [Test]
        public void CompartmentWithoutCover_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            var point = layout.ConvergencePoints.First(p => p.Name == "PowerPlaza");
            layout.Blocks.RemoveAll(b => (b.Kind == BlockKind.Cover || b.Kind == BlockKind.Container)
                && b.FootprintDistance(point.X, point.Z) <= ZoneLayoutRules.ConvergenceCoverRange);
            Assert.IsTrue(Violates(layout, ZoneRule.ConvergenceCover));
        }

        [Test]
        public void ObstacleAtTheFootOfARamp_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            var ramp = layout.Blocks.First(b => b.Name == "MezzanineRampEast"); // climbs toward +Z from z = 15
            layout.Blocks.Add(new ZoneBlock { Name = "Plug", Kind = BlockKind.Cover, X = ramp.X, Y = 0.8f, Z = 14f, SizeX = 3, SizeY = 1.6f, SizeZ = 2 });
            Assert.IsTrue(Violates(layout, ZoneRule.RampApproachClear));
        }

        [Test]
        public void RampAcrossAnElevatorExit_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            var e = layout.Elevators.First(x => x.Name == "ElevatorNorth"); // faces south
            layout.Blocks.Add(new ZoneBlock { Name = "StrayRamp", Kind = BlockKind.Ramp, X = e.X, Y = 1.9f, Z = e.Z - 6, SizeX = 3, SizeY = 0.3f, SizeZ = 11.7f, PitchDeg = -20f });
            Assert.IsTrue(Violates(layout, ZoneRule.ElevatorExitClear));
        }

        [Test]
        public void ZoneD_HasAMezzanineReachedByRamps()
        {
            var layout = ZoneDBlockout.Create();
            Assert.IsTrue(layout.Blocks.Any(b => b.Kind == BlockKind.Platform));
            Assert.GreaterOrEqual(layout.Blocks.Count(b => b.Kind == BlockKind.Ramp), 2);
            Assert.IsTrue(layout.Landmarks.Any(p => p.Name == "AssemblyMezzanine" && p.Y > 3f));
        }

        [Test]
        public void TooFewElevators_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.Elevators.RemoveRange(0, 2);
            Assert.IsTrue(Violates(layout, ZoneRule.ElevatorCount));
        }

        [Test]
        public void TooManyConvergencePoints_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.ConvergencePoints.Add(new ZonePoint { Name = "Extra", X = 0, Z = 0, ClearRadius = 6 });
            Assert.IsTrue(Violates(layout, ZoneRule.ConvergenceCount));
        }

        [Test]
        public void ObstacleInsideAConvergencePoint_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            var point = layout.ConvergencePoints[0];
            layout.Blocks.Add(new ZoneBlock { Name = "Intruder", Kind = BlockKind.Cover, X = point.X + 2, Y = 0.5f, Z = point.Z, SizeX = 1, SizeY = 1, SizeZ = 1 });
            Assert.IsTrue(Violates(layout, ZoneRule.ConvergenceClearRadius));
        }

        [Test]
        public void CrampedConvergencePoint_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.ConvergencePoints[0].ClearRadius = 3f;
            Assert.IsTrue(Violates(layout, ZoneRule.ConvergenceClearRadius));
        }

        [Test]
        public void ConvergencePointsTooClose_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.ConvergencePoints[1].X = layout.ConvergencePoints[0].X + 10;
            layout.ConvergencePoints[1].Z = layout.ConvergencePoints[0].Z;
            Assert.IsTrue(Violates(layout, ZoneRule.ConvergenceSpacing));
        }

        [Test]
        public void ElevatorAwayFromTheOuterWall_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.Elevators[0].X = 0;
            layout.Elevators[0].Z = 0;
            Assert.IsTrue(Violates(layout, ZoneRule.ElevatorAtPerimeter));
        }

        [Test]
        public void ElevatorsTooClose_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.Elevators[1].X = layout.Elevators[0].X + 12;
            layout.Elevators[1].Z = layout.Elevators[0].Z;
            Assert.IsTrue(Violates(layout, ZoneRule.ElevatorSpacing));
        }

        [Test]
        public void ElevatorNextToAConvergencePoint_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.ConvergencePoints[0].X = layout.Elevators[0].X;
            layout.ConvergencePoints[0].Z = layout.Elevators[0].Z - 8;
            Assert.IsTrue(Violates(layout, ZoneRule.ElevatorAwayFromConvergence));
        }

        [Test]
        public void BlockedElevatorExit_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            var e = layout.Elevators.First(x => x.Name == "ElevatorNorth"); // faces south
            layout.Blocks.Add(new ZoneBlock { Name = "Plug", Kind = BlockKind.Wall, X = e.X, Y = 2, Z = e.Z - 4, SizeX = 6, SizeY = 4, SizeZ = 1 });
            Assert.IsTrue(Violates(layout, ZoneRule.ElevatorExitClear));
        }

        [Test]
        public void PointOutsideTheZone_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.LootPoints[0].X = layout.HalfX + 5;
            Assert.IsTrue(Violates(layout, ZoneRule.InsideBounds));
        }

        [Test]
        public void LootInsideAnObstacle_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            var block = layout.Blocks.First(b => b.Kind == BlockKind.Container && b.Y < 2f);
            layout.LootPoints[0].X = block.X;
            layout.LootPoints[0].Y = 0.3f;
            layout.LootPoints[0].Z = block.Z;
            Assert.IsTrue(Violates(layout, ZoneRule.LootPointFree));
        }

        [Test]
        public void SparseCover_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            layout.Blocks.RemoveAll(b => b.Kind == BlockKind.Cover);
            Assert.IsTrue(Violates(layout, ZoneRule.CoverCount));
        }

        [Test]
        public void TooManyBlocks_IsAViolation()
        {
            var layout = ZoneDBlockout.Create();
            for (int i = 0; i < ZoneLayoutRules.MaxBlocks; i++)
                layout.Blocks.Add(new ZoneBlock { Name = "Filler" + i, Kind = BlockKind.Floor, SizeX = 1, SizeY = 0.1f, SizeZ = 1 });
            Assert.IsTrue(Violates(layout, ZoneRule.BlockBudget));
        }
    }
}

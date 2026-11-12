using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum BlockKind { Floor, PerimeterWall, Wall, Cover, Container, Platform, Ramp, ElevatorPad, ElevatorShaft }

    // One axis-aligned box of a blockout. Ramps are the only tilted blocks (PitchDeg about X, RollDeg about Z).
    [Serializable]
    public struct ZoneBlock
    {
        public string Name;
        public BlockKind Kind;
        public float X, Y, Z;             // center
        public float SizeX, SizeY, SizeZ;
        public float PitchDeg, RollDeg;

        // Floors and elevator pads are walked on; everything else gets in the way at ground level.
        public bool IsObstacle => Kind != BlockKind.Floor && Kind != BlockKind.ElevatorPad;
        // Lowest point of the block; a tilted block (ramp) reaches down by half its tilted length.
        public float Bottom => Y - SizeY * 0.5f
            - Math.Abs((float)Math.Sin(PitchDeg * Math.PI / 180.0)) * SizeZ * 0.5f
            - Math.Abs((float)Math.Sin(RollDeg * Math.PI / 180.0)) * SizeX * 0.5f;

        public bool FootprintContains(float x, float z, float margin = 0f) =>
            Math.Abs(x - X) <= SizeX * 0.5f + margin && Math.Abs(z - Z) <= SizeZ * 0.5f + margin;

        // Distance in the ground plane from a point to this block's footprint (0 when inside).
        public float FootprintDistance(float x, float z)
        {
            float dx = Math.Max(0f, Math.Abs(x - X) - SizeX * 0.5f);
            float dz = Math.Max(0f, Math.Abs(z - Z) - SizeZ * 0.5f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    // A named place in a zone. ClearRadius is only meaningful for convergence points.
    [Serializable]
    public class ZonePoint
    {
        public string Name;
        public float X, Y, Z;
        public float ClearRadius;
    }

    // Where an elevator car stands. FacingDeg is the yaw of the way out (0 = +Z, 90 = +X).
    [Serializable]
    public class ElevatorSpot
    {
        public string Name;
        public float X, Z;
        public float FacingDeg;

        public float ForwardX => (float)Math.Round(Math.Sin(FacingDeg * Math.PI / 180.0), 6);
        public float ForwardZ => (float)Math.Round(Math.Cos(FacingDeg * Math.PI / 180.0), 6);
    }

    // The blockout of one zone, centered on the origin: geometry plus the places gameplay refers to.
    [Serializable]
    public class ZoneLayout
    {
        public string Id;
        public float HalfX, HalfZ;
        public float WallHeight;
        public List<ZoneBlock> Blocks = new List<ZoneBlock>();
        public List<ZonePoint> ConvergencePoints = new List<ZonePoint>();
        public List<ElevatorSpot> Elevators = new List<ElevatorSpot>();
        public List<ZonePoint> LootPoints = new List<ZonePoint>();
        public List<ZonePoint> Landmarks = new List<ZonePoint>();
    }

    public enum ZoneRule
    {
        ConvergenceCount, ConvergenceClearRadius, ConvergenceSpacing, ConvergenceCover, RampApproachClear,
        ElevatorCount, ElevatorAtPerimeter, ElevatorSpacing, ElevatorAwayFromConvergence, ElevatorExitClear,
        InsideBounds, LootPointFree, CoverCount, BlockBudget,
    }

    public struct ZoneViolation
    {
        public ZoneRule Rule;
        public string Detail;
    }

    // Zone design principles (SPEC 3-2, 4, 5; ADR-040) as checks over a layout.
    public static class ZoneLayoutRules
    {
        public const int MinConvergencePoints = 3, MaxConvergencePoints = 4;
        public const int MinElevators = 3, MaxElevators = 4;
        public const float MinClearRadius = 6f;                // convergence points are wide halls and yards
        public const float MinConvergenceSpacing = 30f;
        public const float MinElevatorSpacing = 50f;
        public const float MaxElevatorWallDistance = 10f;
        public const float MinElevatorToConvergence = 20f;     // nobody steps out of an elevator into the fight
        public const float ElevatorExitDepth = 7f;
        public const int MinCoverPieces = 60;                  // zone D is cover-rich
        public const int MinCoverNearConvergence = 8;          // ...in every compartment, not just in total
        public const float ConvergenceCoverRange = 25f;
        public const float RampApproachDepth = 2f;
        public const int MaxBlocks = 600;                      // keeps the blockout far inside the draw-call budget
        const float GroundLevel = 1f;

        public static List<ZoneViolation> Validate(ZoneLayout layout)
        {
            var found = new List<ZoneViolation>();
            void Add(ZoneRule rule, string detail) => found.Add(new ZoneViolation { Rule = rule, Detail = detail });
            bool Inside(float x, float z) => Math.Abs(x) <= layout.HalfX && Math.Abs(z) <= layout.HalfZ;
            float Distance(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

            var points = layout.ConvergencePoints;
            if (points.Count < MinConvergencePoints || points.Count > MaxConvergencePoints)
                Add(ZoneRule.ConvergenceCount, $"{points.Count} convergence points, expected {MinConvergencePoints}-{MaxConvergencePoints}");
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (!Inside(p.X, p.Z)) Add(ZoneRule.InsideBounds, p.Name);
                if (p.ClearRadius < MinClearRadius) Add(ZoneRule.ConvergenceClearRadius, $"{p.Name} clear radius {p.ClearRadius} < {MinClearRadius}");
                foreach (var b in layout.Blocks)
                    if (b.IsObstacle && b.FootprintDistance(p.X, p.Z) < p.ClearRadius)
                        Add(ZoneRule.ConvergenceClearRadius, $"{b.Name} stands inside {p.Name}");
                for (int j = i + 1; j < points.Count; j++)
                    if (Distance(p.X, p.Z, points[j].X, points[j].Z) < MinConvergenceSpacing)
                        Add(ZoneRule.ConvergenceSpacing, $"{p.Name} and {points[j].Name}");
                int near = 0;
                foreach (var b in layout.Blocks)
                    if (IsCover(b) && b.FootprintDistance(p.X, p.Z) <= ConvergenceCoverRange) near++;
                if (near < MinCoverNearConvergence)
                    Add(ZoneRule.ConvergenceCover, $"{p.Name} has {near} cover pieces within {ConvergenceCoverRange} m, expected {MinCoverNearConvergence}+");
            }

            var elevators = layout.Elevators;
            if (elevators.Count < MinElevators || elevators.Count > MaxElevators)
                Add(ZoneRule.ElevatorCount, $"{elevators.Count} elevators, expected {MinElevators}-{MaxElevators}");
            for (int i = 0; i < elevators.Count; i++)
            {
                var e = elevators[i];
                if (!Inside(e.X, e.Z)) Add(ZoneRule.InsideBounds, e.Name);
                float toWall = Math.Min(layout.HalfX - Math.Abs(e.X), layout.HalfZ - Math.Abs(e.Z));
                if (toWall > MaxElevatorWallDistance) Add(ZoneRule.ElevatorAtPerimeter, $"{e.Name} is {toWall:F0} m from the outer wall");
                for (int j = i + 1; j < elevators.Count; j++)
                    if (Distance(e.X, e.Z, elevators[j].X, elevators[j].Z) < MinElevatorSpacing)
                        Add(ZoneRule.ElevatorSpacing, $"{e.Name} and {elevators[j].Name}");
                foreach (var p in points)
                    if (Distance(e.X, e.Z, p.X, p.Z) < MinElevatorToConvergence)
                        Add(ZoneRule.ElevatorAwayFromConvergence, $"{e.Name} and {p.Name}");
                // The way out of the car must be open: walk straight ahead and meet nothing.
                string blocker = null;
                for (float step = 1f; step <= ElevatorExitDepth && blocker == null; step += 1f)
                    blocker = GroundObstacleAt(layout, e.X + e.ForwardX * step, e.Z + e.ForwardZ * step, 0.5f, null);
                if (blocker != null) Add(ZoneRule.ElevatorExitClear, $"{blocker} blocks the way out of {e.Name}");
            }

            foreach (var loot in layout.LootPoints)
            {
                if (!Inside(loot.X, loot.Z)) { Add(ZoneRule.InsideBounds, "loot point"); continue; }
                if (loot.Y >= GroundLevel) continue; // on a platform: reachability is checked on the baked navmesh
                var inside = GroundObstacleAt(layout, loot.X, loot.Z, 0.4f, null);
                if (inside != null) Add(ZoneRule.LootPointFree, $"loot point ({loot.X}, {loot.Z}) is inside {inside}");
            }
            foreach (var mark in layout.Landmarks)
                if (!Inside(mark.X, mark.Z)) Add(ZoneRule.InsideBounds, mark.Name);

            // A ramp is only useful if its foot can be walked up to head-on.
            foreach (var ramp in layout.Blocks)
            {
                if (ramp.Kind != BlockKind.Ramp || ramp.PitchDeg == 0f) continue;
                float run = ramp.SizeZ * (float)Math.Cos(ramp.PitchDeg * Math.PI / 180.0);
                float toward = ramp.PitchDeg < 0f ? -1f : 1f; // the low end: negative pitch climbs toward +Z
                for (float step = 0.5f; step <= RampApproachDepth; step += 0.5f)
                {
                    var blocker = GroundObstacleAt(layout, ramp.X, ramp.Z + toward * (run * 0.5f + step), 0.3f, ramp.Name);
                    if (blocker == null) continue;
                    Add(ZoneRule.RampApproachClear, $"{blocker} blocks the foot of {ramp.Name}");
                    break;
                }
            }

            int cover = 0;
            foreach (var b in layout.Blocks)
                if (IsCover(b)) cover++;
            if (cover < MinCoverPieces) Add(ZoneRule.CoverCount, $"{cover} cover pieces < {MinCoverPieces}");
            if (layout.Blocks.Count > MaxBlocks) Add(ZoneRule.BlockBudget, $"{layout.Blocks.Count} blocks > {MaxBlocks}");
            return found;
        }

        static bool IsCover(ZoneBlock b) => b.Kind == BlockKind.Cover || b.Kind == BlockKind.Container;

        // Name of an obstacle standing on the ground at this spot, or null.
        static string GroundObstacleAt(ZoneLayout layout, float x, float z, float margin, string ignore)
        {
            foreach (var b in layout.Blocks)
                if (b.IsObstacle && b.Name != ignore && b.Bottom < GroundLevel && b.FootprintContains(x, z, margin))
                    return b.Name;
            return null;
        }
    }
}

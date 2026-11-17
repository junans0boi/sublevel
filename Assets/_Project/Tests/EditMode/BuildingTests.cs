using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Enterable box buildings (issue #50): every generated plan must satisfy the building rules, and the rules must catch breakage.
    public class BuildingTests
    {
        const int Seeds = 20;

        static readonly BuildingKind[] Kinds =
        {
            BuildingKind.House, BuildingKind.Warehouse, BuildingKind.FactoryHall, BuildingKind.Shed, BuildingKind.ElevatorStation,
        };

        static (float w, float d)[] Sizes(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.House: return new[] { (8f, 7f), (10f, 8f), (14f, 10f) };
                case BuildingKind.Warehouse: return new[] { (14f, 10f), (20f, 14f), (28f, 18f) };
                case BuildingKind.FactoryHall: return new[] { (20f, 14f), (28f, 18f), (36f, 24f) };
                case BuildingKind.Shed: return new[] { (3.5f, 3.5f), (4f, 5f), (6f, 4f) };
                default: return new[] { (3.6f, 5.5f), (5f, 7f) };
            }
        }

        static bool Violates(BuildingPlan plan, BuildingRule rule) => BuildingRules.Validate(plan).Any(v => v.Rule == rule);

        static string Describe(IEnumerable<BuildingViolation> violations) => string.Join("\n", violations.Select(v => v.Rule + ": " + v.Detail));

        static BuildingPiece Plug(string name, float x0, float x1, float y0, float y1, float z0, float z1, SurfaceKind kind = SurfaceKind.WallOuter) =>
            new BuildingPiece
            {
                Name = name, Kind = kind, X = (x0 + x1) / 2, Y = (y0 + y1) / 2, Z = (z0 + z1) / 2,
                SizeX = x1 - x0, SizeY = y1 - y0, SizeZ = z1 - z0,
            };

        // A wall piece that fills a door from jamb to jamb.
        static BuildingPiece Fill(BuildingOpening door)
        {
            bool wallAlongX = System.Math.Abs(door.ForwardZ) > 0.5f;
            float alongHalf = door.Width / 2, acrossHalf = System.Math.Max(door.Depth / 2, 0.15f);
            float hx = wallAlongX ? alongHalf : acrossHalf, hz = wallAlongX ? acrossHalf : alongHalf;
            return Plug("Plug" + door.Name, door.X - hx, door.X + hx, door.Y, door.Y + door.Height, door.Z - hz, door.Z + hz);
        }

        static BuildingPlan TwoStoreyHouse()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.House, 12f, 9f, seed);
                if (plan.Storeys == 2) return plan;
            }
            Assert.Fail("no two-storey house in 200 seeds");
            return null;
        }

        [TestCase(BuildingKind.House)]
        [TestCase(BuildingKind.Warehouse)]
        [TestCase(BuildingKind.FactoryHall)]
        [TestCase(BuildingKind.Shed)]
        [TestCase(BuildingKind.ElevatorStation)]
        public void EverySeedAndSize_SatisfiesEveryRule(BuildingKind kind)
        {
            foreach (var (w, d) in Sizes(kind))
                for (int seed = 0; seed < Seeds; seed++)
                {
                    var violations = BuildingRules.Validate(Buildings.Generate(kind, w, d, seed));
                    Assert.IsEmpty(violations, $"{kind} {w}x{d} seed {seed}\n{Describe(violations)}");
                }
        }

        [Test]
        public void SameInputs_GiveTheSamePlan()
        {
            foreach (var kind in Kinds)
                foreach (var (w, d) in Sizes(kind))
                {
                    var a = Buildings.Generate(kind, w, d, 7);
                    var b = Buildings.Generate(kind, w, d, 7);
                    Assert.AreEqual(a.Pieces.Count, b.Pieces.Count, kind.ToString());
                    for (int i = 0; i < a.Pieces.Count; i++) Assert.AreEqual(a.Pieces[i], b.Pieces[i], a.Pieces[i].Name);
                    CollectionAssert.AreEqual(a.Doors, b.Doors);
                    CollectionAssert.AreEqual(a.Windows, b.Windows);
                    CollectionAssert.AreEqual(a.Rooms, b.Rooms);
                    CollectionAssert.AreEqual(a.Stairs, b.Stairs);
                    CollectionAssert.AreEqual(a.LootPoints, b.LootPoints);
                    CollectionAssert.AreEqual(a.NavHints, b.NavHints);
                    Assert.AreEqual(a.Height, b.Height);
                }
        }

        [Test]
        public void DifferentSeeds_GiveDifferentHouses()
        {
            string Shape(BuildingPlan p) => string.Join("|", p.Pieces.Select(x => $"{x.Kind} {x.X:F2} {x.Y:F2} {x.Z:F2} {x.SizeX:F2} {x.SizeY:F2} {x.SizeZ:F2}"));
            var shapes = new HashSet<string>();
            for (int seed = 0; seed < Seeds; seed++) shapes.Add(Shape(Buildings.Generate(BuildingKind.House, 12f, 9f, seed)));
            Assert.AreEqual(Seeds, shapes.Count);
        }

        [Test]
        public void Houses_ComeWithOneAndTwoStoreys_AndTwoToFourRoomsPerFloor()
        {
            var storeys = new HashSet<int>();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.House, 12f, 9f, seed);
                storeys.Add(plan.Storeys);
                for (int s = 0; s < plan.Storeys; s++)
                    Assert.That(plan.Rooms.Count(r => r.Storey == s), Is.InRange(2, 4), $"seed {seed} storey {s}");
                Assert.AreEqual(plan.Storeys == 2 ? 1 : 0, plan.Stairs.Count, $"seed {seed}");
                Assert.GreaterOrEqual(plan.Doors.Where(x => x.Exterior).Select(x => x.FacingDeg).Distinct().Count(), 2, $"seed {seed}");
            }
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, storeys);
        }

        [Test]
        public void Footprint_IsClampedToWhatTheKindNeeds()
        {
            foreach (var kind in Kinds)
            {
                Buildings.FootprintLimits(kind, out float minW, out float minD, out float maxW, out float maxD);
                var small = Buildings.Generate(kind, 1f, 1f, 3);
                Assert.AreEqual(minW, small.Width, 0.001f, kind.ToString());
                Assert.AreEqual(minD, small.Depth, 0.001f, kind.ToString());
                var large = Buildings.Generate(kind, 500f, 500f, 3);
                Assert.AreEqual(maxW, large.Width, 0.001f, kind.ToString());
                Assert.AreEqual(maxD, large.Depth, 0.001f, kind.ToString());
                Assert.IsEmpty(BuildingRules.Validate(small), kind + " at its smallest\n" + Describe(BuildingRules.Validate(small)));
                Assert.IsEmpty(BuildingRules.Validate(large), kind + " at its largest\n" + Describe(BuildingRules.Validate(large)));
            }
        }

        [Test]
        public void DoorsAndCeilings_LeaveRoomForTheThirdPersonCamera()
        {
            foreach (var kind in Kinds)
                foreach (var (w, d) in Sizes(kind))
                {
                    var plan = Buildings.Generate(kind, w, d, 11);
                    foreach (var door in plan.Doors)
                    {
                        Assert.GreaterOrEqual(door.Width, BuildingRules.MinDoorWidth, door.Name);
                        Assert.GreaterOrEqual(door.Height, BuildingRules.MinDoorHeight, door.Name);
                    }
                    foreach (var room in plan.Rooms)
                        Assert.GreaterOrEqual(room.CeilingY - room.FloorY, BuildingRules.MinCeiling - 0.001f, room.Name);
                    Assert.LessOrEqual(plan.FloorY, BuildingRules.MaxFloorStep + 0.001f);
                }
        }

        [Test]
        public void Warehouse_HasTwoRollDoorsOnOppositeSides_ASideDoorAndAnOffice()
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.Warehouse, 20f, 14f, seed);
                var roll = plan.Doors.Where(x => x.Exterior && x.Width >= 3f).ToList();
                Assert.AreEqual(2, roll.Count, $"seed {seed}");
                Assert.AreEqual(180f, System.Math.Abs(roll[0].FacingDeg - roll[1].FacingDeg), $"seed {seed}");
                Assert.IsTrue(plan.Doors.Any(x => x.Exterior && x.Width < 3f), $"seed {seed}");
                Assert.IsTrue(plan.Rooms.Any(r => r.Name == "Office"));
                Assert.GreaterOrEqual(plan.Rooms.First(r => r.Name == "Hall").CeilingY - plan.FloorY, 6f - 0.001f);
                Assert.IsTrue(plan.Pieces.Any(p => p.Prop == PropKind.Shelf) || plan.Pieces.Any(p => p.Prop == PropKind.Crate), $"seed {seed}");
            }
        }

        [Test]
        public void FactoryHall_HasAMezzanineReachedByStairs_AndSeveralDoors()
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.FactoryHall, 28f, 18f, seed);
                var mezzanine = plan.Rooms.First(r => r.Name == "Mezzanine");
                Assert.Greater(mezzanine.FloorY, plan.FloorY + 3f);
                Assert.AreEqual(1, plan.Stairs.Count);
                Assert.AreEqual(mezzanine.FloorY, plan.Stairs[0].TopY, 0.001f);
                Assert.GreaterOrEqual(plan.Doors.Count(x => x.Exterior), 3);
                Assert.IsTrue(plan.Pieces.Any(p => p.Prop == PropKind.Machine), $"seed {seed}");
                Assert.IsTrue(plan.LootPoints.Any(p => p.Room == "Mezzanine"), $"seed {seed}");
            }
        }

        [Test]
        public void ElevatorStation_SpawnsInTheCarBayFacingTheWayOut()
        {
            var plan = Buildings.Generate(BuildingKind.ElevatorStation, 4f, 6f, 1);
            Assert.IsTrue(plan.HasSpawn);
            Assert.AreEqual(0f, plan.SpawnFacingDeg);
            Assert.AreEqual(plan.FloorY, plan.SpawnY, 0.001f);
            var bay = plan.Rooms.Single();
            Assert.AreEqual(Buildings.ElevatorBay, bay.MaxX - bay.MinX, 0.001f);
            Assert.AreEqual(Buildings.ElevatorBay, bay.MaxZ - bay.MinZ, 0.001f);
            Assert.AreEqual((bay.MinX + bay.MaxX) / 2, plan.SpawnX, 0.001f);
            Assert.AreEqual((bay.MinZ + bay.MaxZ) / 2, plan.SpawnZ, 0.001f);
            Assert.Greater(plan.Depth / 2 - bay.MaxZ, 1.5f, "porch depth");
            Assert.IsFalse(Buildings.Generate(BuildingKind.House, 10f, 8f, 1).HasSpawn);
        }

        [Test]
        public void LootPoints_ScaleWithFloorArea()
        {
            foreach (var kind in new[] { BuildingKind.House, BuildingKind.Warehouse, BuildingKind.FactoryHall, BuildingKind.Shed })
            {
                var sizes = Sizes(kind);
                var (sw, sd) = sizes[0];
                var (lw, ld) = kind == BuildingKind.Shed ? (7f, 7f) : sizes[sizes.Length - 1];
                for (int seed = 0; seed < Seeds; seed++)
                {
                    var small = Buildings.Generate(kind, sw, sd, seed);
                    var large = Buildings.Generate(kind, lw, ld, seed);
                    Assert.Greater(large.LootPoints.Count, small.LootPoints.Count, $"{kind} seed {seed}");
                    // a small room beside the door may hold none (door keep-out, ADR-271), the building at least one point per room
                    Assert.GreaterOrEqual(small.LootPoints.Count, small.Rooms.Count, $"{kind} seed {seed}");
                }
            }
        }

        // WorldBuilder.Structures makes the roof deck walkable by these piece names (ADR-273): bots reach the roof loot over it.
        [Test]
        public void RoofAccessBlocks_HaveNamedWalkableDeckSlabs()
        {
            foreach (var kind in new[] { BuildingKind.Shop, BuildingKind.Villa, BuildingKind.Apartment })
            {
                var (w, d) = Sizes(kind)[0];
                var plan = Buildings.Generate(kind, w, d, 3);
                foreach (var name in new[] { "RoofFloor", "RoofLanding", "RoofFloorFront" })
                    Assert.IsTrue(plan.Pieces.Any(p => p.Name == name && p.Kind == SurfaceKind.Roof), $"{kind} {name}");
            }
        }

        [Test]
        public void PieceCount_StaysInsideTheBudget()
        {
            foreach (var kind in Kinds)
                foreach (var (w, d) in Sizes(kind))
                    for (int seed = 0; seed < Seeds; seed++)
                        Assert.LessOrEqual(Buildings.Generate(kind, w, d, seed).Pieces.Count, BuildingRules.MaxPieces(kind), $"{kind} {w}x{d} seed {seed}");
            // The sizes used on the map keep a house near a hundred boxes.
            for (int seed = 0; seed < Seeds; seed++)
                Assert.LessOrEqual(Buildings.Generate(BuildingKind.House, 14f, 10f, seed).Pieces.Count, 120);
        }

        // ---- the rules catch breakage ----

        [Test]
        public void DoorFilledWithAWall_LeavesTheRoomUnreachable()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            plan.Pieces.Add(Fill(plan.Doors.Single()));
            var violations = BuildingRules.Validate(plan);
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.RoomUnreachable && v.Detail.Contains("Room")), Describe(violations));
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.DoorBlocked));
        }

        [Test]
        public void HouseWithEveryExteriorDoorFilled_HasNoReachableRoom()
        {
            var plan = Buildings.Generate(BuildingKind.House, 12f, 9f, 4);
            foreach (var door in plan.Doors.Where(x => x.Exterior).ToList()) plan.Pieces.Add(Fill(door));
            var unreachable = BuildingRules.Validate(plan).Where(v => v.Rule == BuildingRule.RoomUnreachable).ToList();
            Assert.AreEqual(plan.Rooms.Count, unreachable.Count);
        }

        [Test]
        public void InteriorDoorFilled_CutsOffTheOffice()
        {
            var plan = Buildings.Generate(BuildingKind.Warehouse, 20f, 14f, 5);
            plan.Pieces.Add(Fill(plan.Doors.Single(x => !x.Exterior)));
            var violations = BuildingRules.Validate(plan);
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.RoomUnreachable && v.Detail.StartsWith("Office")), Describe(violations));
            Assert.IsFalse(violations.Any(v => v.Rule == BuildingRule.RoomUnreachable && v.Detail.StartsWith("Hall")));
        }

        [Test]
        public void PropAcrossADoorway_BlocksTheDoor()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            var door = plan.Doors.Single();   // faces +Z
            float z = door.Z - door.Depth / 2 - 0.5f;
            plan.Pieces.Add(Plug("Barricade", -plan.Width / 2 + 0.2f, plan.Width / 2 - 0.2f, plan.FloorY, plan.FloorY + 1.2f, z - 0.3f, z + 0.3f, SurfaceKind.Prop));
            Assert.IsTrue(Violates(plan, BuildingRule.DoorBlocked));
        }

        [Test]
        public void RemovedStairs_LeaveTheUpperFloorUnreachable()
        {
            var plan = TwoStoreyHouse();
            plan.Pieces.RemoveAll(p => p.Kind == SurfaceKind.Stairs);
            var violations = BuildingRules.Validate(plan);
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.StairStep));
            Assert.AreEqual(plan.Rooms.Count(r => r.Storey == 1), violations.Count(v => v.Rule == BuildingRule.RoomUnreachable), Describe(violations));
            plan.Stairs.Clear();
            Assert.IsTrue(Violates(plan, BuildingRule.StairMissing));
        }

        [Test]
        public void MissingStep_IsTooHighToClimb()
        {
            var plan = TwoStoreyHouse();
            plan.Pieces.RemoveAll(p => p.Name == "StairStep8" || p.Name == "StairStep9");
            var violations = BuildingRules.Validate(plan);
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.StairStep), Describe(violations));
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.RoomUnreachable), Describe(violations));
        }

        [Test]
        public void StairsThatStopShort_AreAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.FactoryHall, 28f, 18f, 3);
            var stair = plan.Stairs[0];
            plan.Pieces.RemoveAll(p => p.Kind == SurfaceKind.Stairs && p.MaxY > stair.TopY - 0.6f);
            var violations = BuildingRules.Validate(plan);
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.StairTop), Describe(violations));
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.RoomUnreachable && v.Detail.StartsWith("Mezzanine")), Describe(violations));
        }

        [Test]
        public void TooFewExteriorDoors_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.House, 10f, 8f, 1);
            var keep = plan.Doors.First(x => x.Exterior);
            plan.Doors.RemoveAll(x => x.Exterior && x.Name != keep.Name);
            var violations = BuildingRules.Validate(plan);
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.ExteriorDoorCount));
            Assert.IsTrue(violations.Any(v => v.Rule == BuildingRule.ExteriorDoorSides));
        }

        [Test]
        public void NarrowOrLowDoor_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            var door = plan.Doors[0];
            door.Width = 0.9f;
            plan.Doors[0] = door;
            Assert.IsTrue(Violates(plan, BuildingRule.DoorSize));
            door.Width = 1.3f;
            door.Height = 1.9f;
            plan.Doors[0] = door;
            Assert.IsTrue(Violates(plan, BuildingRule.DoorSize));
        }

        [Test]
        public void LowerLintel_BlocksTheDoor()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            var door = plan.Doors.Single();
            plan.Pieces.Add(Plug("Lintel", door.X - door.Width / 2, door.X + door.Width / 2, door.Y + 1.5f, door.Y + door.Height, door.Z - door.Depth / 2, door.Z + door.Depth / 2));
            Assert.IsTrue(Violates(plan, BuildingRule.DoorBlocked));
        }

        [Test]
        public void LootOutsideInsideOrOnNothing_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Warehouse, 20f, 14f, 5);
            var loot = plan.LootPoints[0];
            loot.X = plan.Width / 2 + 3f;
            plan.LootPoints[0] = loot;
            Assert.IsTrue(Violates(plan, BuildingRule.LootOutside));

            plan = Buildings.Generate(BuildingKind.Warehouse, 20f, 14f, 5);
            var shelf = plan.Pieces.First(p => p.Kind == SurfaceKind.Prop && p.SizeY > 1.5f);
            loot = plan.LootPoints[0];
            loot.X = shelf.X;
            loot.Y = plan.FloorY;
            loot.Z = shelf.Z;
            plan.LootPoints[0] = loot;
            Assert.IsTrue(Violates(plan, BuildingRule.LootInsidePiece));

            plan = Buildings.Generate(BuildingKind.Warehouse, 20f, 14f, 5);
            loot = plan.LootPoints.First(p => !p.OnProp);
            loot.Y += 1f;
            plan.LootPoints[plan.LootPoints.FindIndex(p => p.Name == loot.Name)] = loot;
            Assert.IsTrue(Violates(plan, BuildingRule.LootUnsupported));
        }

        [Test]
        public void LootBehindAFilledDoor_IsUnreachable()
        {
            var plan = Buildings.Generate(BuildingKind.Warehouse, 20f, 14f, 5);
            plan.Pieces.Add(Fill(plan.Doors.Single(x => !x.Exterior)));
            Assert.IsTrue(Violates(plan, BuildingRule.LootUnreachable));
        }

        [Test]
        public void PieceOutsideTheFootprint_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            plan.Pieces.Add(Plug("Annex", plan.Width / 2, plan.Width / 2 + 1f, 0f, 1f, 0f, 1f, SurfaceKind.Prop));
            Assert.IsTrue(Violates(plan, BuildingRule.PieceOutsideFootprint));

            plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            float x = plan.Width / 2 + 1f, z = plan.Depth / 2 + 1f;
            plan.Pieces.Add(Plug("WideRoof", -x, x, plan.Height, plan.Height + 0.2f, -z, z, SurfaceKind.Roof));
            Assert.IsTrue(Violates(plan, BuildingRule.PieceOutsideFootprint));
        }

        [Test]
        public void LowCeiling_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            plan.Pieces.Add(Plug("Soffit", -1f, 1f, plan.FloorY + 2.4f, plan.FloorY + 2.6f, -1f, 1f, SurfaceKind.Floor));
            Assert.IsTrue(Violates(plan, BuildingRule.CeilingClearance));

            plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            plan.Pieces.RemoveAll(p => p.Kind == SurfaceKind.Roof);
            Assert.IsTrue(Violates(plan, BuildingRule.CeilingClearance));
        }

        [Test]
        public void WindowSillOutOfRange_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            var window = plan.Windows[0];
            window.Y = plan.FloorY + 1.6f;
            plan.Windows[0] = window;
            Assert.IsTrue(Violates(plan, BuildingRule.WindowSill));
        }

        [Test]
        public void RoomWithoutAWindow_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.House, 10f, 8f, 1);
            string room = plan.Rooms[0].Name;
            plan.Windows.RemoveAll(w => w.Room == room);
            Assert.IsTrue(Violates(plan, BuildingRule.RoomWithoutWindow));
        }

        [Test]
        public void WallTooThinOrTooThick_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            plan.Pieces.Add(Plug("Paper", -1f, 1f, plan.FloorY, plan.FloorY + 1f, -0.05f, 0.05f, SurfaceKind.WallInner));
            Assert.IsTrue(Violates(plan, BuildingRule.WallThickness));
        }

        [Test]
        public void HighFloorSlab_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            int slab = plan.Pieces.FindIndex(p => p.Name == "Slab");
            var piece = plan.Pieces[slab];
            piece.SizeY = 0.8f;
            piece.Y = 0.4f;
            plan.Pieces[slab] = piece;
            Assert.IsTrue(Violates(plan, BuildingRule.FloorStep));
        }

        [Test]
        public void BlockedOrMissingSpawn_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.ElevatorStation, 4f, 6f, 1);
            plan.Pieces.Add(Plug("Barrier", -1f, 1f, plan.FloorY, plan.FloorY + 1f, plan.Depth / 2 - 1f, plan.Depth / 2 - 0.7f, SurfaceKind.Prop));
            Assert.IsTrue(Violates(plan, BuildingRule.SpawnPoint));

            plan = Buildings.Generate(BuildingKind.ElevatorStation, 4f, 6f, 1);
            plan.HasSpawn = false;
            Assert.IsTrue(Violates(plan, BuildingRule.SpawnPoint));
        }

        [Test]
        public void TooManyPieces_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 4f, 5f, 2);
            for (int i = 0; i < BuildingRules.MaxPieces(BuildingKind.Shed); i++)
                plan.Pieces.Add(Plug("Filler" + i, 0f, 0.1f, 5f, 5.1f, 0f, 0.1f, SurfaceKind.Trim));
            Assert.IsTrue(Violates(plan, BuildingRule.PieceBudget));
        }

        // ---- furniture and lamps (#190, #191, ADR-147) ----

        static HashSet<PropKind> PropKinds(BuildingKind kind, float w, float d, int seeds)
        {
            var found = new HashSet<PropKind>();
            for (int seed = 0; seed < seeds; seed++)
                foreach (var p in Buildings.Generate(kind, w, d, seed).Pieces)
                    if (p.Kind == SurfaceKind.Prop) found.Add(p.Prop);
            return found;
        }

        [Test]
        public void Houses_GetADistinctFurnitureSetPerRoomRole()
        {
            var kinds = PropKinds(BuildingKind.House, 12f, 9f, 40);
            foreach (var needed in new[] { PropKind.Bed, PropKind.Sofa, PropKind.Kitchen, PropKind.BathSet, PropKind.Wardrobe, PropKind.Table, PropKind.Desk, PropKind.TvStand, PropKind.Bookcase })
                Assert.IsTrue(kinds.Contains(needed), "no house furnished with " + needed);
            // A kitchen is only in a kitchen or a studio, a bed never in a bathroom.
            for (int seed = 0; seed < 40; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.House, 12f, 9f, seed);
                foreach (var piece in plan.Pieces.Where(p => p.Kind == SurfaceKind.Prop))
                {
                    var room = plan.Rooms.First(r => r.Contains(piece.X, piece.Z) && piece.MinY >= r.FloorY - 0.01f && piece.MinY < r.CeilingY);
                    if (room.Role == RoomRole.Bath) Assert.IsTrue(piece.Prop == PropKind.BathSet || piece.Prop == PropKind.Bathtub, $"{piece.Prop} in a bathroom, seed {seed}");
                    if (piece.Prop == PropKind.BathSet || piece.Prop == PropKind.Bathtub) Assert.AreEqual(RoomRole.Bath, room.Role, $"seed {seed}");
                }
            }
        }

        [Test]
        public void IndustrialAndSmallBuildings_GetTheirOwnSets()
        {
            var warehouse = PropKinds(BuildingKind.Warehouse, 24f, 16f, 20);
            foreach (var needed in new[] { PropKind.Shelf, PropKind.Pallet, PropKind.Barrel, PropKind.Desk, PropKind.Locker })
                Assert.IsTrue(warehouse.Contains(needed), "warehouse without " + needed);
            var factory = PropKinds(BuildingKind.FactoryHall, 30f, 20f, 20);
            foreach (var needed in new[] { PropKind.Machine, PropKind.Workbench, PropKind.Crate })
                Assert.IsTrue(factory.Contains(needed), "factory without " + needed);
            var shed = PropKinds(BuildingKind.Shed, 5f, 5f, 30);
            Assert.IsTrue(shed.Contains(PropKind.Workbench), "shed without a workbench");
            Assert.IsFalse(shed.Contains(PropKind.Bed));
        }

        [TestCase(BuildingKind.House, 10f, 8f)]
        [TestCase(BuildingKind.Warehouse, 24f, 16f)]
        [TestCase(BuildingKind.FactoryHall, 30f, 20f)]
        [TestCase(BuildingKind.Shed, 5f, 5f)]
        public void Furniture_IsBuiltFromManyPartsAndStaysInsideTheBudget(BuildingKind kind, float w, float d)
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Buildings.Generate(kind, w, d, seed);
                int props = plan.Pieces.Count(p => p.Kind == SurfaceKind.Prop);
                Assert.Greater(props, 0, $"{kind} seed {seed} has no furniture");
                // Every prop is more than one box: parts are named after the prop they belong to.
                foreach (var piece in plan.Pieces.Where(p => p.Kind == SurfaceKind.Prop))
                    Assert.GreaterOrEqual(plan.Decor.Count(x => x.Name.StartsWith(piece.Name + ".")), 4, $"{piece.Name} ({piece.Prop}) is too plain, seed {seed}");
                Assert.LessOrEqual(plan.Decor.Count, BuildingRules.MaxDecor(kind), $"{kind} seed {seed}");
            }
        }

        [Test]
        public void EveryRoomHasALampUnderItsCeiling_AndLampsAreFlaggedForTiredTubesOnlyInHalls()
        {
            foreach (var kind in new[] { BuildingKind.House, BuildingKind.Warehouse, BuildingKind.FactoryHall, BuildingKind.Shed })
                for (int seed = 0; seed < 10; seed++)
                {
                    var plan = Buildings.Generate(kind, kind == BuildingKind.Shed ? 5f : 24f, kind == BuildingKind.Shed ? 5f : 16f, seed);
                    Assert.IsNotEmpty(plan.Lamps);
                    Assert.LessOrEqual(plan.Lamps.Count, BuildingRules.MaxLamps);
                    foreach (var room in plan.Rooms)
                        Assert.IsTrue(plan.Lamps.Any(l => l.Room == room.Name && room.Contains(l.X, l.Z)), $"{kind} {room.Name} seed {seed} has no lamp");
                    if (kind == BuildingKind.House) Assert.IsFalse(plan.Lamps.Any(l => l.Flicker));
                }
        }

        [Test]
        public void FurnitureNeverStandsInADoorway()
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.House, 12f, 9f, seed);
                Assert.IsFalse(BuildingRules.Validate(plan).Any(v => v.Rule == BuildingRule.DecorBlocksDoor), $"seed {seed}");
            }
        }

        [Test]
        public void DecorBlockingADoor_OrALampMissing_IsAViolation()
        {
            var plan = Buildings.Generate(BuildingKind.Shed, 5f, 5f, 3);
            var door = plan.Doors[0];
            plan.Decor.Add(new BuildingDecor { Name = "Wardrobe", Mat = DecorMat.Wood, X = door.X, Y = door.Y + 1f, Z = door.Z, SizeX = 0.8f, SizeY = 2f, SizeZ = 0.8f });
            Assert.IsTrue(Violates(plan, BuildingRule.DecorBlocksDoor));
            plan = Buildings.Generate(BuildingKind.Shed, 5f, 5f, 3);
            plan.Lamps.Clear();
            Assert.IsTrue(Violates(plan, BuildingRule.LampMissing));
        }

        [Test]
        public void Furniture_FacesIntoTheRoomAndSameSeedGivesTheSameParts()
        {
            var a = Buildings.Generate(BuildingKind.House, 12f, 9f, 5);
            var b = Buildings.Generate(BuildingKind.House, 12f, 9f, 5);
            Assert.AreEqual(a.Decor.Count, b.Decor.Count);
            for (int i = 0; i < a.Decor.Count; i++) Assert.AreEqual(a.Decor[i].X, b.Decor[i].X, 1e-5f);
            // The first sofa's backrest (its tallest parts) is nearer the wall behind it than the front edge of its box.
            foreach (var sofa in a.Pieces.Where(p => p.Prop == PropKind.Sofa))
            {
                var parts = a.Decor.Where(x => x.Name.StartsWith(sofa.Name + ".")).ToList();
                var back = parts.OrderByDescending(x => x.Y + x.SizeY / 2).First();
                float fx = 0f, fz = 0f;
                if (sofa.Facing == 0) fz = 1f; else if (sofa.Facing == 2) fz = -1f; else if (sofa.Facing == 1) fx = 1f; else fx = -1f;
                float backAlong = (back.X - sofa.X) * fx + (back.Z - sofa.Z) * fz;
                Assert.Less(backAlong, 0f, "the sofa's backrest is not at its back");
            }
        }

        // ---- lived-in rooms (#190, ADR-154) ----

        [Test]
        public void HouseRooms_HoldAtLeastThreePiecesOnAverage_AndNeverANarrowSlotBetweenProps()
        {
            int rooms = 0, props = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.House, 12f, 9f, seed);
                var pieces = plan.Pieces.Where(p => p.Kind == SurfaceKind.Prop).ToList();
                props += pieces.Count;
                rooms += plan.Rooms.Count(r => r.Role != RoomRole.Bath);
                for (int i = 0; i < pieces.Count; i++)
                    for (int j = i + 1; j < pieces.Count; j++)
                    {
                        var a = pieces[i]; var b = pieces[j];
                        if (Math.Abs(a.MinY - b.MinY) > 0.5f) continue;   // another floor
                        float sx = Math.Max(b.MinX - a.MaxX, a.MinX - b.MaxX), sz = Math.Max(b.MinZ - a.MaxZ, a.MinZ - b.MaxZ);
                        if (sx < 0f && sz < 0f) Assert.Fail($"{a.Name} overlaps {b.Name}, seed {seed}");
                        float gap = (float)Math.Sqrt(Math.Max(sx, 0f) * Math.Max(sx, 0f) + Math.Max(sz, 0f) * Math.Max(sz, 0f));
                        Assert.IsFalse(gap > 0.1001f && gap < 0.7999f, $"{a.Name} and {b.Name} leave a {gap:0.00} m slot, seed {seed}");
                    }
            }
            Assert.GreaterOrEqual((float)props / rooms, 3f, "pieces per room (bathrooms left out)");
        }

        [Test]
        public void Houses_HaveWallPicturesCurtainsAndFloorClutter_ClearOfDoorsAndLoot()
        {
            int pictures = 0, curtains = 0, clutter = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                var plan = Buildings.Generate(BuildingKind.House, 12f, 9f, seed);
                pictures += plan.Decor.Count(d => d.Name.Contains(".") && d.Y > 1.2f && d.Y < 1.9f && Math.Min(d.SizeX, d.SizeZ) < 0.03f && Math.Max(d.SizeX, d.SizeZ) > 0.5f);
                curtains += plan.Decor.Count(d => d.Name.StartsWith("Curtain") && d.Name.EndsWith("Rod"));
                foreach (var c in plan.Decor.Where(d => d.Name.StartsWith("Clutter") && !d.Name.EndsWith("Lid") && d.Name.Length < 12))
                {
                    clutter++;
                    foreach (var loot in plan.LootPoints.Where(l => !l.OnProp))
                        Assert.IsFalse(Math.Abs(loot.X - c.X) < 0.35f && Math.Abs(loot.Z - c.Z) < 0.35f && Math.Abs(loot.Y - (c.Y - c.SizeY / 2)) < 0.3f, $"clutter on loot {loot.Name}, seed {seed}");
                }
                Assert.IsFalse(BuildingRules.Validate(plan).Any(v => v.Rule == BuildingRule.DecorBlocksDoor), $"seed {seed}");
            }
            Assert.Greater(pictures, 20, "wall pictures");
            Assert.Greater(curtains, 40, "curtain rods");
            Assert.Greater(clutter, 100, "floor clutter");
        }
    }
}
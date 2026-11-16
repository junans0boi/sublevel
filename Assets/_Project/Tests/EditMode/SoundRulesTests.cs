using System;
using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using NVector3 = System.Numerics.Vector3;

namespace Game.Tests
{
    // #120 / ADR-092: footsteps by surface and gait, gunshot layers by distance, the bullet whiz, and the ground
    // under a foot read from real colliders and terrain layers.
    public class SoundRulesTests
    {
        // ---- Surface -> clip set ----

        [TestCase(FootSurface.Metal)]
        [TestCase(FootSurface.Wood)]
        [TestCase(FootSurface.Grass)]
        [TestCase(FootSurface.Dirt)]
        [TestCase(FootSurface.Water)]
        public void Every_surface_has_its_own_clip_set(FootSurface surface)
        {
            var clips = CombatantAudio.StepClips(surface);
            Assert.GreaterOrEqual(clips.Length, 4, $"{surface}: several variations");
            foreach (FootSurface other in Enum.GetValues(typeof(FootSurface)))
                if (other != surface && other != FootSurface.Concrete)
                    Assert.AreNotEqual(SoundRules.StepClips(surface), SoundRules.StepClips(other));
        }

        [Test]
        public void Building_and_terrain_map_to_the_painted_material()
        {
            Assert.AreEqual(FootSurface.Wood, SoundRules.BuildingSurface(BuildingKind.House, SurfaceKind.Floor));
            Assert.AreEqual(FootSurface.Wood, SoundRules.BuildingSurface(BuildingKind.House, SurfaceKind.Stairs));
            Assert.AreEqual(FootSurface.Concrete, SoundRules.BuildingSurface(BuildingKind.Warehouse, SurfaceKind.Floor));
            Assert.AreEqual(FootSurface.Metal, SoundRules.BuildingSurface(BuildingKind.FactoryHall, SurfaceKind.Stairs));
            Assert.AreEqual(FootSurface.Concrete, SoundRules.BuildingSurface(BuildingKind.ElevatorStation, SurfaceKind.Stairs));
            Assert.AreEqual(FootSurface.Grass, SoundRules.TerrainSurface("World_Grass", false));
            Assert.AreEqual(FootSurface.Dirt, SoundRules.TerrainSurface("World_Dirt", false), "bare ground");
            Assert.AreEqual(FootSurface.Concrete, SoundRules.TerrainSurface("World_Rock", false));
            Assert.AreEqual(FootSurface.Dirt, SoundRules.TerrainSurface("World_Mud", false), "damp ground and shore are not water");
            Assert.AreEqual(FootSurface.Water, SoundRules.TerrainSurface("World_Grass", true), "below the sea surface");
        }

        // The asphalt bug: nothing but real water (ground below the sea surface) may sound like water.
        [TestCase("World_Grass", FootSurface.Grass)]
        [TestCase("World_Dirt", FootSurface.Dirt)]
        [TestCase("World_Rock", FootSurface.Concrete)]
        [TestCase("World_Mud", FootSurface.Dirt)]
        [TestCase(null, FootSurface.Concrete)]
        [TestCase("", FootSurface.Concrete)]
        [TestCase("World_Asphalt", FootSurface.Concrete)]
        [TestCase("SomethingNew", FootSurface.Concrete)]
        public void Terrain_layers_map_to_a_step_set_and_unknown_is_neutral(string layer, FootSurface expected)
        {
            Assert.AreEqual(expected, SoundRules.TerrainSurface(layer, false));
            Assert.AreEqual(FootSurface.Water, SoundRules.TerrainSurface(layer, true), "water only below the sea surface");
        }

        [Test]
        public void Every_building_piece_maps_to_a_dry_step_set()
        {
            foreach (BuildingKind building in Enum.GetValues(typeof(BuildingKind)))
                foreach (SurfaceKind piece in Enum.GetValues(typeof(SurfaceKind)))
                {
                    var surface = SoundRules.BuildingSurface(building, piece);
                    Assert.AreNotEqual(FootSurface.Water, surface, $"{building}/{piece}");
                    Assert.AreNotEqual(FootSurface.Grass, surface, $"{building}/{piece}");
                    Assert.AreNotEqual(FootSurface.Dirt, surface, $"{building}/{piece}");
                }
            Assert.AreEqual(FootSurface.Metal, SoundRules.BuildingSurface(BuildingKind.FactoryHall, SurfaceKind.Roof));
            Assert.AreEqual(FootSurface.Concrete, SoundRules.BuildingSurface(BuildingKind.ElevatorStation, SurfaceKind.Floor));
        }

        // ---- Stance and gait -> loudness and range ----

        [Test]
        public void Gait_and_stance_set_loudness_and_range_in_pubg_ratios()
        {
            var stances = new StanceRules();
            float slow = new LocomotionParams().SlowWalkFootstepVolume;
            float run = SoundRules.StepLoudness(1f, false, FootSurface.Concrete);
            float sprint = SoundRules.StepLoudness(1f, true, FootSurface.Concrete);
            float slowWalk = SoundRules.StepLoudness(slow, false, FootSurface.Concrete);
            float crouchRun = SoundRules.StepLoudness(stances.Crouch.FootstepVolumeFactor, false, FootSurface.Concrete);
            float crouchWalk = SoundRules.StepLoudness(stances.Crouch.FootstepVolumeFactor * slow, false, FootSurface.Concrete);

            Assert.AreEqual(1f, run, 1e-5f);
            Assert.AreEqual(SoundRules.RunStepRange, SoundRules.StepRange(run), 1e-4f, "a run is heard 40 m");
            Assert.AreEqual(50f, SoundRules.StepRange(sprint), 1e-3f, "a sprint further");
            Assert.Less(SoundRules.StepRange(slowWalk), 0.3f * SoundRules.RunStepRange + 1e-3f, "slow walk: quiet");
            Assert.Less(SoundRules.StepRange(crouchRun), 0.5f * SoundRules.RunStepRange, "crouched: under half");
            Assert.Less(crouchWalk, 0.15f * run, "crouched slow walk: much quieter");
            Assert.AreEqual(SoundRules.MinStepRange, SoundRules.StepRange(crouchWalk * 0.1f), "never below the floor range");
            Assert.Greater(SoundRules.StepLoudness(1f, false, FootSurface.Metal), run, "metal rings");
            Assert.Less(SoundRules.StepLoudness(1f, false, FootSurface.Grass), run, "grass muffles");
        }

        [Test]
        public void Sprint_is_told_from_a_run_by_speed_and_stance()
        {
            Assert.IsFalse(SoundRules.IsSprint(4.7f, 4.7f, 6.3f, 1f));
            Assert.IsTrue(SoundRules.IsSprint(6.3f, 4.7f, 6.3f, 1f));
            Assert.IsFalse(SoundRules.IsSprint(3.48f, 4.7f, 6.3f, 0.74f), "a crouched run");
            Assert.IsTrue(SoundRules.IsSprint(4.66f, 4.7f, 6.3f, 0.74f), "a crouched sprint");
        }

        // ---- Distance -> gunshot layer mix ----

        [Test]
        public void Gunshot_layers_crossfade_from_crack_to_thump()
        {
            SoundRules.ShotLayers(5f, out float near, out float far);
            Assert.AreEqual(1f, near, 1e-5f);
            Assert.AreEqual(0f, far);
            SoundRules.ShotLayers(300f, out near, out far);
            Assert.AreEqual(0f, near);
            Assert.AreEqual(1f, far, 1e-5f);
            float last = 1f;
            for (float d = SoundRules.NearLayerFull; d <= SoundRules.FarLayerFull; d += 10f)
            {
                SoundRules.ShotLayers(d, out near, out far);
                Assert.AreEqual(1f, near * near + far * far, 1e-4f, $"equal power at {d} m");
                Assert.LessOrEqual(near, last);
                last = near;
            }
        }

        [Test]
        public void A_suppressed_shot_is_heard_as_a_farther_one()
        {
            Assert.AreEqual(160f, SoundRules.HeardDistance(40f, true), 1e-4f);
            Assert.AreEqual(40f, SoundRules.HeardDistance(40f, false));
            SoundRules.ShotLayers(SoundRules.HeardDistance(60f, true), out float near, out _);
            SoundRules.ShotLayers(SoundRules.HeardDistance(60f, false), out float open, out _);
            Assert.Less(near, open, "less crack at the same distance");
        }

        // ---- Whiz ----

        [Test]
        public void A_round_whizzes_only_when_it_passes_close_to_someone_else()
        {
            var origin = NVector3.Zero;
            var end = new NVector3(0f, 0f, 100f);
            Assert.IsTrue(SoundRules.Whiz(origin, end, new NVector3(1f, 0f, 50f), out var passing));
            Assert.AreEqual(50f, passing.Z, 1e-3f);
            Assert.IsFalse(SoundRules.Whiz(origin, end, new NVector3(5f, 0f, 50f), out _), "5 m off the path");
            Assert.IsFalse(SoundRules.Whiz(origin, end, new NVector3(0.5f, 0f, 2f), out _), "the shooter");
            Assert.IsFalse(SoundRules.Whiz(origin, new NVector3(0f, 0f, 30f), new NVector3(1f, 0f, 50f), out _), "stopped short");
            Assert.IsFalse(SoundRules.Whiz(origin, end, new NVector3(1f, 0f, 120f), out _), "behind the end");
        }

        // ---- The ground under a foot, from real colliders ----

        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        BoxCollider Building(string name, Vector3 at, Vector3 size, SurfaceId surface)
        {
            var building = new GameObject(name);
            building.transform.SetParent(_root.transform, false);
            building.transform.position = at;
            var colliders = new GameObject("Colliders");
            colliders.transform.SetParent(building.transform, false);
            colliders.AddComponent<Game.Net.SurfaceIdTag>().Id = surface; // WorldBuilder tags every collider group (#246); footsteps read the tag, not the name
            var box = colliders.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        [Test]
        public void SurfaceAt_reads_buildings_and_terrain_layers()
        {
            _root = new GameObject("SoundRulesTestRoot");
            Building("Lot01_House", new Vector3(1000f, 50f, 1000f), new Vector3(8f, 0.2f, 8f), SurfaceId.Wood);
            Building("Lot02_FactoryHall", new Vector3(1020f, 50f, 1000f), new Vector3(10f, 0.2f, 10f), SurfaceId.Concrete);
            Building("Lot03_FactoryHall", new Vector3(1040f, 50f, 1000f), new Vector3(1.3f, 0.5f, 0.28f), SurfaceId.Metal); // a stair step
            Building("Station01", new Vector3(1060f, 50f, 1000f), new Vector3(8f, 0.2f, 8f), SurfaceId.Concrete);

            var data = new TerrainData { alphamapResolution = 16 };
            data.heightmapResolution = 33;
            data.size = new Vector3(64f, 10f, 64f);
            var grass = new TerrainLayer { name = "World_Grass" };
            var dirt = new TerrainLayer { name = "World_Dirt" };
            data.terrainLayers = new[] { grass, dirt };
            var maps = new float[16, 16, 2];
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    bool road = x >= 8; // the +X half is road
                    maps[z, x, 0] = road ? 0f : 1f;
                    maps[z, x, 1] = road ? 1f : 0f;
                }
            data.SetAlphamaps(0, 0, maps);
            var terrain = Terrain.CreateTerrainGameObject(data);
            terrain.transform.SetParent(_root.transform, false);
            terrain.transform.position = new Vector3(2000f, 0f, 2000f);
            Physics.SyncTransforms();

            Assert.AreEqual(FootSurface.Wood, CombatantAudio.SurfaceAt(new Vector3(1000f, 50.1f, 1000f)), "house floor");
            Assert.AreEqual(FootSurface.Concrete, CombatantAudio.SurfaceAt(new Vector3(1020f, 50.1f, 1000f)), "factory floor");
            Assert.AreEqual(FootSurface.Metal, CombatantAudio.SurfaceAt(new Vector3(1040f, 50.25f, 1000f)), "factory stairs");
            Assert.AreEqual(FootSurface.Concrete, CombatantAudio.SurfaceAt(new Vector3(1060f, 50.1f, 1000f)), "station");
            Assert.AreEqual(FootSurface.Grass, CombatantAudio.SurfaceAt(new Vector3(2010f, 0f, 2032f)), "grass half");
            Assert.AreEqual(FootSurface.Dirt, CombatantAudio.SurfaceAt(new Vector3(2054f, 0f, 2032f)), "road half");
            Assert.AreEqual(FootSurface.Concrete, CombatantAudio.SurfaceAt(new Vector3(5000f, -500f, 5000f)), "nothing underfoot");
        }

        // ---- ADR-183: eighteen surfaces, clip folders from the cut Freesound recordings ----

        [Test]
        public void Every_surface_has_step_clips_of_its_own_or_a_stand_in()
        {
            foreach (SurfaceId id in System.Enum.GetValues(typeof(SurfaceId)))
            {
                var clips = CombatantAudio.StepClips(id, out float gain);
                Assert.GreaterOrEqual(clips.Length, 4, id + ": several variations");
                Assert.That(gain, Is.InRange(0.1f, 1f), id.ToString());
                foreach (var c in clips) StringAssert.StartsWith("step_", c.name, id.ToString());
            }
        }

        [Test]
        public void A_surface_with_no_folder_plays_its_stand_in_quieter()
        {
            CombatantAudio.StepClips(SurfaceId.Carpet, out float carpet);
            CombatantAudio.StepClips(SurfaceId.Wood, out float wood);
            Assert.AreEqual(0.6f, carpet, 1e-4f);
            Assert.AreEqual(1f, wood, 1e-4f);
        }

        [TestCase("World_Grass", false, SurfaceId.Grass)]
        [TestCase("World_Dirt", false, SurfaceId.Dirt)]
        [TestCase("World_Mud", false, SurfaceId.Mud)]
        [TestCase("World_Rock", false, SurfaceId.Rock)]
        [TestCase("World_Sand", false, SurfaceId.Sand)]
        [TestCase("anything", true, SurfaceId.Water)]
        [TestCase(null, false, SurfaceId.Concrete)]
        public void Terrain_layers_map_to_surfaces(string layer, bool underWater, SurfaceId expected)
        {
            Assert.AreEqual(expected, SoundRules.TerrainSurfaceId(layer, underWater));
        }

        [Test]
        public void A_collider_without_a_tag_is_concrete_whatever_its_name()
        {
            _root = new GameObject("SoundRulesTestRoot");
            var b = new GameObject("Lot09_House");
            b.transform.SetParent(_root.transform, false);
            b.transform.position = new Vector3(3000f, 50f, 3000f);
            var c = new GameObject("Colliders");
            c.transform.SetParent(b.transform, false);
            c.AddComponent<BoxCollider>().size = new Vector3(8f, 0.2f, 8f);
            Physics.SyncTransforms();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.AreEqual(FootSurface.Concrete, CombatantAudio.SurfaceAt(new Vector3(3000f, 50.1f, 3000f)), "no name parsing: a house name alone is not wood");
        }
    }
}

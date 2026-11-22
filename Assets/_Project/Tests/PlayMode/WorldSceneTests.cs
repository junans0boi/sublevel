using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // W1-3 (#49, #50): the generated outdoor map scene. What later systems (bots, elevator deployment, region
    // closure) rely on: players come up inside an elevator station, every region can be walked from its elevators
    // and through its passes, loot lies where it can be picked up, and a view from the ground fits the render budget.
    public class WorldSceneTests
    {
        const float SnapDistance = 2f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 40f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for the local player in World");
                yield return null;
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Vector3 OnNavMesh(Vector3 position, string what)
        {
            Assert.IsTrue(NavMesh.SamplePosition(position, out var hit, SnapDistance, NavMesh.AllAreas), what + " is not on the navmesh");
            return hit.position;
        }

        // CalculatePath gives up on routes of several hundred metres and returns PathPartial even when they are joined
        // (ADR-544: 31 of 96 such routes on the old radius/3 bake, 17 on radius/2). So a partial path is carried on from where
        // it stopped; only a leg that gets no closer means the two points are really cut apart.
        static void AssertWalkable(Vector3 from, Vector3 to, string what)
        {
            var at = OnNavMesh(from, what + " (start)");
            var end = OnNavMesh(to, what + " (end)");
            var path = new NavMeshPath();
            for (int leg = 0; leg < 12; leg++)
            {
                Assert.IsTrue(NavMesh.CalculatePath(at, end, NavMesh.AllAreas, path), "no path: " + what);
                if (path.status == NavMeshPathStatus.PathComplete) return;
                var stop = path.corners[path.corners.Length - 1];
                Assert.Greater(Vector3.Distance(at, end) - Vector3.Distance(stop, end), 1f, $"path is cut off at {stop}: " + what);
                at = stop;
            }
            Assert.Fail($"path still partial after 12 legs, at {at}: " + what);
        }

        // #191, ADR-147: hundreds of lamps in the map, but only the few nearest the camera are lit.
        [UnityTest]
        public IEnumerator InteriorLamps_AreLitOnlyNearTheCamera()
        {
            var pool = Object.FindFirstObjectByType<InteriorLights>();
            Assert.IsNotNull(pool, "the World scene should hold the interior light pool");
            Assert.Greater(pool.TotalLamps, 100, "lamps in the map");
            var lamps = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(l => l.transform.parent != null && l.transform.parent.name == "Lamps").ToList();
            Assert.AreEqual(pool.TotalLamps, lamps.Count);
            var focus = new GameObject("LampFocus").transform;
            InteriorLights.Focus = focus;
            focus.position = lamps[0].transform.position + Vector3.down * 1.5f;
            yield return new WaitForSeconds(0.6f);
            int on = lamps.Count(l => l.enabled);
            Assert.That(on, Is.InRange(1, InteriorLights.MaxOn), "lamps on near a lamp");
            Assert.IsTrue(lamps[0].enabled, "the nearest lamp is on");
            Assert.IsTrue(pool.CameraInside, "a camera under a lamp is inside a building");
            Assert.Greater(pool.GradeWeight, 0.5f, "the view is darker inside");
            focus.position = new Vector3(0f, -400f, 0f);
            yield return new WaitForSeconds(1.0f);
            Assert.AreEqual(0, lamps.Count(l => l.enabled), "lamps on far from every building");
            Assert.IsFalse(pool.CameraInside);
            Assert.AreEqual(0f, pool.GradeWeight, 0.01f, "outdoors keeps the shared grade");
            InteriorLights.Focus = null;
            Object.Destroy(focus.gameObject);
        }

        [UnityTest]
        public IEnumerator Player_SpawnsStandingInsideAnElevatorStation()
        {
            var world = WorldInfo.Current;
            Assert.IsNotNull(world, "World scene should contain a WorldInfo");
            var player = PlayerController.Local.transform;

            // The car rises out of its shaft before the doors open (ADR-065 (1)): stand still from then on.
            float doors = Time.realtimeSinceStartup + 30f;
            while (SpawnProtection.InputBlocked && Time.realtimeSinceStartup < doors) yield return null;
            Assert.IsFalse(SpawnProtection.InputBlocked, "the elevator doors open");

            // Not falling: the height holds for a second.
            float lowest = player.position.y, highest = lowest;
            float end = Time.time + 1f;
            while (Time.time < end)
            {
                yield return null;
                lowest = Mathf.Min(lowest, player.position.y);
                highest = Mathf.Max(highest, player.position.y);
            }
            Assert.LessOrEqual(highest - lowest, 0.3f, $"the player's height should hold still (moved between {lowest:F2} and {highest:F2})");

            float nearest = float.MaxValue;
            var spawn = Vector3.zero;
            foreach (var elevator in world.Elevators)
            {
                float d = Vector2.Distance(new Vector2(player.position.x, player.position.z), new Vector2(elevator.Spawn.x, elevator.Spawn.z));
                if (d < nearest) { nearest = d; spawn = elevator.Spawn; }
            }
            Assert.Less(nearest, 1.5f, "the player should stand at an elevator's spawn point");
            Assert.Less(Mathf.Abs(player.position.y - spawn.y), 0.5f, "the player should stand on the station floor");

            // A floor under the feet and a roof overhead (ignoring the player's own colliders).
            var chest = player.position + Vector3.up * 1f;
            bool floor = Physics.RaycastAll(chest, Vector3.down, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Any(h => !h.collider.transform.IsChildOf(player));
            bool roof = Physics.RaycastAll(chest, Vector3.up, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Any(h => !h.collider.transform.IsChildOf(player));
            Assert.IsTrue(floor, "there should be ground right under the player");
            Assert.IsTrue(roof, "the player should be under the station's roof");
        }

        [UnityTest]
        public IEnumerator World_ListsSixRegionsAndTheirElevators()
        {
            var world = WorldInfo.Current;
            Assert.IsNotNull(world, "World scene should contain a WorldInfo");
            Assert.AreEqual(6, world.Regions.Count, "regions"); // the C2 island (IslandSpec) has six regions
            Assert.GreaterOrEqual(world.Elevators.Count, 13, "elevators");
            Assert.Greater(world.Size, 0f);
            foreach (var region in world.Regions)
            {
                Assert.IsFalse(string.IsNullOrEmpty(region.Name), "region " + region.Id + " should have a display name");
                Assert.GreaterOrEqual(world.ElevatorsIn(region.Index).Count, 3, "elevators in region " + region.Id);
                Assert.Greater(world.ConvergencePointsIn(region.Index).Count, 0, "convergence points in region " + region.Id);
            }
            foreach (var elevator in world.Elevators)
                Assert.That(elevator.Region, Is.InRange(0, world.Regions.Count - 1), elevator.Name + " region");
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryElevator_ReachesItsRegionsFirstConvergencePoint()
        {
            var world = WorldInfo.Current;
            foreach (var elevator in world.Elevators)
            {
                var points = world.ConvergencePointsIn(elevator.Region);
                Assert.Greater(points.Count, 0, "convergence points for " + elevator.Name);
                AssertWalkable(elevator.Spawn, points[0].Position, elevator.Name + " -> " + points[0].Name);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator NeighbouringRegions_AreJoinedThroughTheirPasses()
        {
            var world = WorldInfo.Current;
            Assert.Greater(world.Passes.Count, 0, "passes");
            var joined = new HashSet<(int, int)>();
            foreach (var pass in world.Passes)
            {
                var a = world.Regions[pass.RegionA];
                var b = world.Regions[pass.RegionB];
                // Two legs rather than one long search: hub to the pass, the pass to the other hub.
                AssertWalkable(a.Hub, pass.Position, a.Id + " hub -> " + pass.Name);
                AssertWalkable(pass.Position, b.Hub, pass.Name + " -> " + b.Id + " hub");
                joined.Add((Mathf.Min(pass.RegionA, pass.RegionB), Mathf.Max(pass.RegionA, pass.RegionB)));
            }
            // No region is left on its own.
            foreach (var region in world.Regions)
                Assert.IsTrue(joined.Any(pair => pair.Item1 == region.Index || pair.Item2 == region.Index), "region " + region.Id + " should have a pass");
            yield break;
        }

        [UnityTest]
        public IEnumerator Loot_LiesAboveTheGroundAndOutOfTheWater()
        {
            var world = WorldInfo.Current;
            var terrain = Terrain.activeTerrain;
            Assert.IsNotNull(terrain, "World scene should contain a terrain");
            Assert.GreaterOrEqual(LootItem.All.Count, 100, "loot items spawned");
            foreach (var item in LootItem.All)
            {
                var p = item.transform.position;
                float groundY = terrain.SampleHeight(p) + terrain.GetPosition().y;
                Assert.GreaterOrEqual(p.y, groundY - 0.05f, $"loot at {p} is under the terrain ({groundY:F2})");
                Assert.Greater(p.y, world.SeaLevel, $"loot at {p} is under water");
            }
            yield break;
        }

        [UnityTest]
        [Category("Perf")]
        public IEnumerator World_RendersWithinBudget()
        {
            var world = WorldInfo.Current;
            Assert.AreEqual(6, world.Regions.Count, "one viewpoint per region");

            // Eye level beside an elevator of each region, looking across the region's centre compound.
            var cam = new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 700f;   // 높음, the worst preset
            GraphicsOptions.ApplyLayerCull(cam);   // small/medium props stop at 150/300 m as on the player's camera (#477)
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            // The map's own renderer, as the player's camera uses (no SSAO on the outdoor map).
            cam.GetUniversalAdditionalCameraData().SetRenderer(world.RendererIndex);

            var ids = new List<string>();
            var results = new List<RenderProbe.Result>();
            foreach (var region in world.Regions)
            {
                var elevators = world.ElevatorsIn(region.Index);
                Assert.Greater(elevators.Count, 0, "elevators in region " + region.Id);
                var station = elevators[0].Position;
                var toward = region.Hub - station;
                toward.y = 0f;
                toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
                cam.transform.position = station + toward * 8f + Vector3.up * 1.7f;
                cam.transform.LookAt(region.Hub + Vector3.up * 2f);

                RenderProbe.Result result = default;
                yield return RenderProbe.Measure(cam, r => result = r, screenshotPath: $"Logs/perf/world_{region.Id}.png");
                if (System.Environment.GetEnvironmentVariable("WORLD_CULL_COMPARE") == "1")
                {
                    // Same view without the layer cull distances: the before of #477.
                    var cull = cam.layerCullDistances;
                    cam.layerCullDistances = new float[32];
                    RenderProbe.Result off = default;
                    yield return RenderProbe.Measure(cam, r => off = r, screenshotPath: $"Logs/perf/world_{region.Id}_nocull.png");
                    Debug.Log($"[Perf] world {region.Id} no layer cull: {off}");
                    cam.layerCullDistances = cull;
                }
                ids.Add(region.Id);
                results.Add(result);
            }
            Object.Destroy(cam.gameObject);

            RenderProbe.WriteReport("Logs/perf/world.json", "{" + string.Join(",", ids.Select((id, i) => $"\"{id}\":{results[i].ToJson()}")) + "}");
            Debug.Log("[Perf] world " + string.Join(" | ", ids.Select((id, i) => id + ": " + results[i])));
            foreach (var result in results) RenderProbe.AssertWithinBudget(result);
        }
    }
}

using System;
using System.Collections;
using Game.Core;
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
    // M2-1: the generated zone D blockout scene. Bots (M2-2) will rely on exactly these guarantees:
    // every place gameplay refers to is on the navmesh and can be walked to from every elevator.
    public class ZoneDTests
    {
        const float SnapDistance = 1f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ZoneD");
            float end = Time.realtimeSinceStartup + 15f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for the local player in ZoneD");
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

        static void AssertWalkable(Vector3 from, Vector3 to, string what)
        {
            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(OnNavMesh(from, what + " (start)"), OnNavMesh(to, what + " (end)"), NavMesh.AllAreas, path), "no path: " + what);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "path is cut off: " + what);
        }

        [UnityTest]
        public IEnumerator Zone_ExposesFourConvergencePointsAndFourElevators()
        {
            var zone = ZoneInfo.Current;
            Assert.IsNotNull(zone, "ZoneD scene should contain a ZoneInfo");
            Assert.AreEqual("D", zone.ZoneId);
            Assert.AreEqual(4, zone.ConvergencePoints.Count);
            Assert.AreEqual(4, zone.Elevators.Count);
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryElevator_ReachesEveryConvergencePoint()
        {
            var zone = ZoneInfo.Current;
            foreach (var elevator in zone.Elevators)
                foreach (var point in zone.ConvergencePoints)
                    AssertWalkable(elevator.position, point.position, elevator.name + " -> " + point.name);
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryElevator_ReachesEveryOtherElevator()
        {
            var elevators = ZoneInfo.Current.Elevators;
            for (int i = 0; i < elevators.Count; i++)
                for (int j = i + 1; j < elevators.Count; j++)
                    AssertWalkable(elevators[i].position, elevators[j].position, elevators[i].name + " -> " + elevators[j].name);
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryLootPoint_IsReachable_IncludingTheMezzanine()
        {
            var start = ZoneInfo.Current.Elevators[0].position;
            var loot = MatchDirector.Instance.LootSpawns;
            Assert.Greater(loot.Length, 20, "zone D should have plenty of loot points");
            bool anyRaised = false;
            foreach (var point in loot)
            {
                AssertWalkable(start, point.position, "elevator -> " + point.name);
                anyRaised |= point.position.y > 3f;
            }
            Assert.IsTrue(anyRaised, "some loot should sit on the mezzanine");
            yield break;
        }

        [UnityTest]
        public IEnumerator Mezzanine_IsReachedByWalkingUpARamp()
        {
            var zone = ZoneInfo.Current;
            var deck = zone.Landmark("AssemblyMezzanine");
            Assert.IsNotNull(deck, "landmark AssemblyMezzanine");
            var onDeck = OnNavMesh(deck.position, "mezzanine deck");
            Assert.Greater(onDeck.y, 3.5f, "the navmesh point should be on the deck, not on the floor under it");
            AssertWalkable(zone.Elevators[0].position, deck.position, "elevator -> mezzanine");

            // Each ramp on its own: from the floor in front of its foot straight up to the deck, without a detour over the other one.
            int ramps = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.name.StartsWith("MezzanineRamp")) continue;
                ramps++;
                var bounds = renderer.bounds;
                var foot = new Vector3(bounds.center.x, 0f, bounds.min.z - 1f);
                var head = new Vector3(bounds.center.x, bounds.max.y, bounds.max.z + 1f);
                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(OnNavMesh(foot, renderer.name + " foot"), OnNavMesh(head, renderer.name + " head"), NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, renderer.name);
                float length = 0f;
                for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                Assert.Less(length, 18f, renderer.name + " should be climbed head-on (path was " + length + " m)");
            }
            Assert.AreEqual(2, ramps);
            yield break;
        }

        [UnityTest]
        public IEnumerator NavMesh_CoversMostOfTheZone()
        {
            var layout = ZoneDBlockout.Create();
            float zoneArea = layout.HalfX * 2f * layout.HalfZ * 2f;
            var mesh = NavMesh.CalculateTriangulation();
            float ground = 0f, raised = 0f;
            for (int i = 0; i < mesh.indices.Length; i += 3)
            {
                Vector3 a = mesh.vertices[mesh.indices[i]], b = mesh.vertices[mesh.indices[i + 1]], c = mesh.vertices[mesh.indices[i + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (Mathf.Max(a.y, b.y, c.y) < 0.5f) ground += area; else raised += area;
            }
            Debug.Log($"[ZoneD] navmesh: ground {ground:F0} m2, raised {raised:F0} m2, zone {zoneArea:F0} m2");
            Assert.That(ground, Is.InRange(zoneArea * 0.6f, zoneArea), "walkable ground");
            // Only the mezzanine deck and its two ramps are raised; crate and container tops must not be walkable islands.
            Assert.That(raised, Is.InRange(150f, 400f), "raised walkable area");
            yield break;
        }

        [UnityTest]
        public IEnumerator Player_StartsAtAnElevator()
        {
            var player = PlayerController.Local.transform.position;
            float nearest = float.MaxValue;
            foreach (var elevator in ZoneInfo.Current.Elevators)
                nearest = Mathf.Min(nearest, Vector3.Distance(player, elevator.position));
            Assert.Less(nearest, 3f);
            yield break;
        }

        [UnityTest]
        [Category("Perf")]
        public IEnumerator ZoneD_RendersWithinBudget()
        {
            RenderProbe.Result player = default, overview = default;
            yield return RenderProbe.Measure(PlayerController.Local.Camera, r => player = r, screenshotPath: "Logs/perf/zoned_player.png");

            // Worst case: the whole zone in view from a corner, nothing culled.
            var cam = new GameObject("OverviewCamera").AddComponent<Camera>();
            cam.transform.position = new Vector3(-95f, 70f, -80f);
            cam.transform.LookAt(new Vector3(0f, 0f, 0f));
            cam.farClipPlane = 400f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            yield return RenderProbe.Measure(cam, r => overview = r, screenshotPath: "Logs/perf/zoned_overview.png");
            UnityEngine.Object.Destroy(cam.gameObject);

            RenderProbe.WriteReport("Logs/perf/zoned.json", $"{{\"Player\":{player.ToJson()},\"Overview\":{overview.ToJson()}}}");
            Debug.Log($"[Perf] zoned player: {player} | overview: {overview}");
            RenderProbe.AssertWithinBudget(player);
            RenderProbe.AssertWithinBudget(overview);
        }
    }
}

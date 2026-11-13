using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #71 (ADR-070): the generated World scene carries its trees, bushes and grass; trunks are solid; and the view
    // from inside the densest wood, where the instanced trees cost the most, fits the render budget.
    public class VegetationSceneTests
    {
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

        static VegetationRenderer Plants()
        {
            var plants = Object.FindFirstObjectByType<VegetationRenderer>();
            Assert.IsNotNull(plants, "World has a VegetationRenderer");
            return plants;
        }

        static int Trees(VegetationRenderer plants) => plants.CountOf(PlantKind.Broadleaf) + plants.CountOf(PlantKind.Conifer);

        [UnityTest]
        public IEnumerator World_HasTreesBushesAndGrass()
        {
            var plants = Plants();
            Assert.Greater(Trees(plants), 800);
            Assert.Greater(plants.CountOf(PlantKind.Conifer), 100);
            Assert.Greater(plants.CountOf(PlantKind.Bush), 500);
            foreach (var proto in plants.Prototypes)
            {
                Assert.IsNotEmpty(proto.Lods, proto.Name);
                foreach (var lod in proto.Lods)
                {
                    Assert.IsNotNull(lod.Mesh, proto.Name + " mesh");
                    Assert.AreEqual(lod.Mesh.subMeshCount, lod.Materials.Length, proto.Name + ": one material per submesh");
                    foreach (var m in lod.Materials) Assert.IsTrue(m != null && m.enableInstancing, proto.Name + ": instanced material");
                }
            }
            var grass = Object.FindFirstObjectByType<GrassRenderer>();
            Assert.IsNotNull(grass, "World has a GrassRenderer");
            Assert.IsTrue(grass.HasMap, "the grass map is loaded");
            yield break;
        }

        [UnityTest]
        public IEnumerator TreeTrunks_StopShots_AndStandOnTheGround()
        {
            var plants = Plants();
            var terrain = Terrain.activeTerrain;
            int checkedTrees = 0;
            for (int i = 0; i < plants.Count && checkedTrees < 25; i += 37)
            {
                if (plants.KindOf(i) == PlantKind.Bush) continue;
                var foot = plants.Foot(i);
                float ground = terrain.SampleHeight(foot) + terrain.GetPosition().y;
                Assert.AreEqual(ground, foot.y, 0.3f, $"tree at {foot} stands on the ground");
                // A shot from 4 m away at chest height hits the trunk (or something in front of it), never nothing.
                var from = foot + new Vector3(4f, 1.3f, 0f);
                Assert.IsTrue(Physics.Raycast(from, (foot + Vector3.up * 1.3f - from).normalized, out var hit, 4.5f), $"a shot at the tree at {foot} hits");
                checkedTrees++;
            }
            Assert.Greater(checkedTrees, 10);
            yield break;
        }

        [UnityTest]
        [Category("Perf")]
        public IEnumerator Forest_RendersWithinBudget()
        {
            var plants = Plants();
            var trees = new List<Vector3>();
            for (int i = 0; i < plants.Count; i++)
                if (plants.KindOf(i) != PlantKind.Bush) trees.Add(plants.Foot(i));
            // The tree with the most others within 20 m, looked across from beside it.
            Vector3 best = trees[0];
            int most = -1;
            foreach (var t in trees)
            {
                int n = 0;
                foreach (var o in trees)
                    if ((o - t).sqrMagnitude < 400f) n++;
                if (n > most) { most = n; best = t; }
            }
            var cam = new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 700f;
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.GetUniversalAdditionalCameraData().SetRenderer(WorldInfo.Current.RendererIndex);
            cam.transform.position = best + new Vector3(1.8f, 1.7f, 1.2f);
            cam.transform.LookAt(best + new Vector3(30f, 3f, 20f));

            RenderProbe.Result result = default;
            yield return RenderProbe.Measure(cam, r => result = r, screenshotPath: "Logs/perf/world_forest.png");
            Object.Destroy(cam.gameObject);
            RenderProbe.WriteReport("Logs/perf/world_forest.json", result.ToJson());
            Debug.Log($"[Perf] world forest ({most} trees within 20 m): {result}");
            RenderProbe.AssertWithinBudget(result);
        }
    }
}

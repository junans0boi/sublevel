using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FishNet;
using FishNet.Object;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Game.Tests
{
    // Animation track, PERF (ADR-191, ADR-233): what 50 combatants cost in animation. Two parts:
    //   1. 50 Humanoid roster characters evaluated by their Animator alone (manual Update, Stopwatch), layers on and off.
    //   2. 50 real bots (all 12 roster models) in the TestBox: frame time and the stopwatch of CombatantAnimation's own Update + LateUpdate,
    //      with every bot near the camera (full detail) and with the camera far away (level 2 detail).
    // Results go to Logs/perf/animator.txt. Run alone: tools/unity.sh test playmode Game.Tests.AnimatorPerfTests (audio engine off).
    [Category("Perf")]
    public class AnimatorPerfTests
    {
        const string ControllerPath = "Assets/_Project/Prefabs/M1_5/Character.controller";
        static readonly string[] Ids = { "MixamoSwat", "MixamoBrian", "MixamoLouise", "MixamoJames", "MixamoMegan", "MixamoKate", "MixamoJosh", "MixamoAdam", "MixamoMartha", "MixamoAlex", "UbcMale", "UbcFemale" };
        static readonly StringBuilder Report = new StringBuilder();

        static void Say(string line) { Report.AppendLine(line); Debug.Log("[AnimPerf] " + line); }

        [OneTimeTearDown]
        public void Write() { Directory.CreateDirectory("Logs/perf"); File.WriteAllText("Logs/perf/animator.txt", Report.ToString()); }

        static double TimeMs(Action frame, int frames)
        {
            for (int i = 0; i < 20; i++) frame();
            var samples = new List<double>();
            for (int i = 0; i < frames; i++)
            {
                long t = Stopwatch.GetTimestamp();
                frame();
                samples.Add((Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency);
            }
            samples.Sort();
            return samples.Average();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Fifty_Animators_EvaluateInAFewMilliseconds()
        {
            var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            Assert.IsNotNull(controller);
            var animators = new List<Animator>();
            var rng = new System.Random(3);
            for (int i = 0; i < 50; i++)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Roster/Roster_{Ids[i % Ids.Length]}.prefab");
                var go = UnityEngine.Object.Instantiate(prefab, new Vector3(rng.Next(-30, 30), 0, rng.Next(5, 60)), Quaternion.identity);
                var a = go.GetComponent<CharacterDef>().Animator;
                a.runtimeAnimatorController = controller;
                a.applyRootMotion = false; a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.enabled = false; // evaluated by hand below
                a.SetFloat("Move", 3f); a.SetFloat("Pace", 1f);
                a.Play("Aim", 1, 0f);
                animators.Add(a);
            }
            yield return null;
            foreach (var (name, upper, action, additive) in new[] { ("base only", 0f, 0f, 0f), ("armed run: base+upper+additive", 1f, 0f, 1f), ("reloading: all four layers", 1f, 1f, 1f) })
            {
                foreach (var a in animators) { a.SetLayerWeight(1, upper); a.SetLayerWeight(2, action); a.SetLayerWeight(3, additive); if (action > 0f) a.Play("ReloadStand", 2, 0f); }
                double ms = TimeMs(() => { foreach (var a in animators) a.Update(1f / 60f); }, 120);
                Say($"50 Animators, {name}: {ms:F2} ms/frame ({ms / 50 * 1000:F0} us each)");
                Assert.Less(ms, 4f, $"50 animators ({name}) leave room in the frame (PERF_BUDGET: the 6 ms of game logic)");
            }
        }

        static IEnumerator Spawn(int count, List<GameObject> into)
        {
            var rng = new System.Random(11);
            for (int i = 0; i < count; i++)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkObject>($"Assets/_Project/Prefabs/M1/Bot_{Ids[i % Ids.Length]}.prefab");
                Assert.IsNotNull(prefab, "Bot_ prefab (TestBoxBuilder.Build)");
                var pos = new Vector3(rng.Next(-25, 25), 0.3f, rng.Next(-20, 25));
                var bot = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0, rng.Next(0, 360), 0));
                InstanceFinder.ServerManager.Spawn(bot);
                into.Add(bot.gameObject);
                if (i % 10 == 9) yield return null;
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Fifty_Bots_CostOfTheCodeAnimation()
        {
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 15f;
            while ((PlayerController.Local == null || InstanceFinder.ServerManager == null || !InstanceFinder.IsServerStarted) && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsNotNull(PlayerController.Local, "player");
            var spawned = new List<GameObject>();
            yield return Spawn(40, spawned);
            yield return new WaitForSeconds(4f);
            var all = UnityEngine.Object.FindObjectsByType<CombatantAnimation>(FindObjectsSortMode.None);
            Say($"combatants with CombatantAnimation: {all.Length}");
            var pc = PlayerController.Local;
            foreach (var (name, teleport) in new[] { ("near: everyone within 40 m", (Vector3?)null), ("far: camera 120 m away", new Vector3(0, 0.1f, -24f) + new Vector3(0, 0, -120f)) })
            {
                if (teleport.HasValue) { pc.Teleport(new Vector3(200f, 0.1f, 200f)); }
                yield return new WaitForSeconds(1f);
                int lods0 = all.Count(a => a.Lod == 0), lods1 = all.Count(a => a.Lod == 1), lods2 = all.Count(a => a.Lod == 2);
                CombatantAnimation.PerfTicks = 0;
                int frames = 0; float t0 = Time.realtimeSinceStartup;
                while (frames < 240) { yield return null; frames++; }
                double wall = (Time.realtimeSinceStartup - t0) * 1000.0 / frames;
                double code = CombatantAnimation.PerfTicks * 1000.0 / Stopwatch.Frequency / frames;
                Say($"{name}: levels 0/1/2 = {lods0}/{lods1}/{lods2}; CombatantAnimation Update+LateUpdate {code:F2} ms/frame over {all.Length} combatants; whole frame {wall:F1} ms");
                Assert.Less(code, 3f, $"code animation of {all.Length} combatants ({name}) within 3 ms");
            }
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
        }
    }
}

using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Drives the real TestBox scene as a host by injecting player input (M1 gate: farm -> fight -> die).
    public class CoreLoopTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                // One-shot presses last a single frame.
                Frame.Jump = Frame.FirePressed = Frame.Reload = Frame.Interact = false;
                Frame.ToggleView = Frame.SwapShoulder = Frame.Heal = Frame.Restart = false;
                Frame.SelectSlot = -1;
                return f;
            }
        }

        ScriptedInput _input;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Combatant DummyNear(Vector3 point)
        {
            Combatant best = null;
            foreach (var c in Combatant.All)
                if (!c.IsPlayer && (best == null || (c.transform.position - point).sqrMagnitude < (best.transform.position - point).sqrMagnitude))
                    best = c;
            return best;
        }

        [UnityTest]
        public IEnumerator Player_WalksForward_AndSprintsFaster()
        {
            var player = PlayerController.Local.transform;
            float z0 = player.position.z;
            _input.Frame.Move = new Vector2(0, 1);
            yield return WaitSeconds(0.5f);
            float walked = player.position.z - z0;
            _input.Frame.Sprint = true;
            float z1 = player.position.z;
            yield return WaitSeconds(0.5f);
            float sprinted = player.position.z - z1;

            Assert.That(walked, Is.InRange(1.5f, 3f), "walk 0.5s at 4.5 m/s");
            Assert.Greater(sprinted, walked * 1.2f);
        }

        [UnityTest]
        public IEnumerator Camera_SwitchesToFirstPerson_AndStaysInsideWalls()
        {
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            yield return null;
            Assert.AreEqual(ViewMode.ThirdPerson, pc.Rig.Mode);
            Assert.Greater(Vector3.Distance(pc.Camera.transform.position, me.EyePosition), 2f, "third person sits behind the player");

            _input.Frame.ToggleView = true;
            yield return null;
            yield return null;
            Assert.AreEqual(ViewMode.FirstPerson, pc.Rig.Mode);
            Assert.Less(Vector3.Distance(pc.Camera.transform.position, me.EyePosition), 0.05f, "first person sits at the eyes");

            _input.Frame.ToggleView = true;
            yield return null;
            // Back against the south wall (inner face at z = -29.5), looking north.
            pc.Teleport(new Vector3(0, 0.1f, -28.8f));
            pc.LookAt(new Vector3(0, 1.6f, 0));
            yield return null;
            yield return null;
            Assert.Greater(pc.Camera.transform.position.z, -29.5f, "camera must not pass through the wall");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator FarmFightDie_ThenRestart()
        {
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            Assert.AreEqual(100f, me.Hp.Value);
            Assert.AreEqual(0, me.ActiveWeaponId, "starts unarmed");

            // Farm: the first loot point always holds a weapon.
            int lootBefore = LootItem.All.Count;
            pc.Teleport(new Vector3(0, 0.1f, -21.5f));
            yield return null;
            yield return null;
            Assert.IsNotNull(pc.NearbyLoot, "loot in reach");
            _input.Frame.Interact = true;
            yield return WaitUntil(() => me.ActiveWeaponId != 0, 5f, "weapon picked up");
            yield return WaitUntil(() => LootItem.All.Count == lootBefore - 1, 5f, "picked-up loot removed from the world");
            int magAtStart = me.ActiveMag;
            Assert.Greater(magAtStart, 0);

            // Fight: shoot the west dummy until it dies.
            var dummy = DummyNear(new Vector3(-15, 0, 0));
            pc.Teleport(new Vector3(-15, 0.1f, -8f));
            float end = Time.realtimeSinceStartup + 20f;
            bool press = false;
            while (!dummy.Dead.Value)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "dummy should die; hp " + dummy.Hp.Value + ", mag " + me.ActiveMag);
                pc.LookAt(dummy.transform.position + Vector3.up * 1.1f);
                _input.Frame.FireHeld = true;
                _input.Frame.FirePressed = press = !press;
                yield return null;
            }
            _input.Frame.FireHeld = false;
            Assert.AreEqual(0f, dummy.Hp.Value);
            Assert.Less(me.ActiveMag, magAtStart, "shooting uses ammo");

            // Die: stand in front of the east dummy without fighting back.
            pc.Teleport(new Vector3(15, 0.1f, -8f));
            yield return WaitUntil(() => me.Dead.Value, 30f, "player killed by dummy");
            Assert.AreEqual(0f, me.Hp.Value);

            // Restart: a fresh match with a fresh, unarmed, healthy player.
            _input.Frame.Restart = true;
            yield return WaitUntil(() => Combatant.LocalPlayer != null && Combatant.LocalPlayer != me && Combatant.LocalPlayer.Hp.Value == 100f, 20f, "new match after restart");
            Assert.AreEqual(0, Combatant.LocalPlayer.ActiveWeaponId);
            Assert.AreEqual(lootBefore, LootItem.All.Count);
        }

        [UnityTest]
        [Category("Perf")]
        public IEnumerator TestBox_RendersWithinBudget()
        {
            const int frames = 90, warmup = 30;
            var cam = PlayerController.Local.Camera;
            cam.enabled = false; // rendered manually so only our call is timed
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");

            var times = new float[frames];
            var sw = new Stopwatch();
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                sw.Restart();
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = prev;
                sw.Stop();
                times[i] = (float)sw.Elapsed.TotalMilliseconds;
            }
            var stats = FrameStats.Compute(times, warmup);
            long drawCalls = draws.LastValue, triangles = tris.LastValue, setPasses = setPass.LastValue;
            draws.Dispose(); tris.Dispose(); setPass.Dispose();
            // Keep one frame as an image so the scene can be checked by eye.
            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            Directory.CreateDirectory("Logs/perf");
            File.WriteAllBytes("Logs/perf/testbox.png", shot.EncodeToPNG());
            UnityEngine.Object.Destroy(shot);

            cam.targetTexture = null;
            cam.enabled = true;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(probe);

            Directory.CreateDirectory("Logs/perf");
            File.WriteAllText("Logs/perf/testbox.json",
                $"{{\"AvgMs\":{stats.AvgMs:F3},\"P99Ms\":{stats.P99Ms:F3},\"DrawCalls\":{drawCalls},\"Triangles\":{triangles},\"SetPass\":{setPasses}}}");
            UnityEngine.Debug.Log($"[Perf] testbox: avg {stats.AvgMs:F2}ms p99 {stats.P99Ms:F2}ms draw {drawCalls} tris {triangles} setpass {setPasses}");

            // PERF_BUDGET maximums (ADR-028).
            Assert.LessOrEqual(stats.P99Ms, 14f, "render time p99");
            Assert.LessOrEqual(drawCalls, 4000);
            Assert.LessOrEqual(triangles, 2500000);
            Assert.LessOrEqual(setPasses, 120);
        }
    }
}

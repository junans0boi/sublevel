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
        public IEnumerator Player_Jumps_AndLandsAgain()
        {
            var player = PlayerController.Local.transform;
            yield return WaitSeconds(0.3f); // settle on the floor
            float ground = player.position.y;
            _input.Frame.Jump = true;
            float peak = ground;
            float end = Time.time + 1.5f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, player.position.y);
                yield return null;
            }
            Assert.Greater(peak - ground, 0.4f, "jump height");
            Assert.Less(Mathf.Abs(player.position.y - ground), 0.1f, "back on the floor");
        }

        [UnityTest]
        public IEnumerator Reload_SwitchWeapon_AndHeal_AreAppliedByTheServer()
        {
            var me = Combatant.LocalPlayer;
            // Host shortcut: hand the items over on the server instead of walking to them.
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 6 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Light, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 });
            yield return null;

            _input.Frame.Reload = true;
            yield return WaitUntil(() => me.Reloading.Value, 2f, "reload started");
            yield return WaitUntil(() => !me.Reloading.Value && me.Mag0.Value == 12, 5f, "magazine refilled");
            Assert.AreEqual(20, me.ReserveLight.Value);

            _input.Frame.SelectSlot = 1;
            yield return WaitUntil(() => me.ActiveSlot.Value == 1, 2f, "second slot active");
            Assert.AreEqual(4, me.ActiveWeaponId);

            me.ServerTakeDamage(70);
            yield return null;
            Assert.AreEqual(30f, me.Hp.Value);
            _input.Frame.Heal = true;
            yield return WaitUntil(() => me.Medkits.Value == 0, 2f, "medkit used");
            Assert.AreEqual(80f, me.Hp.Value);
        }

        [UnityTest]
        public IEnumerator DataFiles_LoadAndAreConsistent()
        {
            Assert.AreEqual(4, GameData.Weapons.Weapons.Length);
            foreach (var entry in GameData.Loot.Entries)
                if (entry.Kind == LootKind.Weapon)
                    Assert.IsNotNull(GameData.Weapons.Find(entry.WeaponId), "loot refers to weapon " + entry.WeaponId);
            Assert.Greater(GameData.Rules.MedkitHeal, 0f);
            Assert.AreEqual(MatchDirector.Instance.LootSpawns.Length, LootItem.All.Count, "one item per loot point");
            yield return null;
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
            yield return null;
            Assert.AreEqual(ViewMode.ThirdPerson, pc.Rig.Mode);

            // Shoulder swap moves the camera to the other side of the player.
            float sideBefore = pc.transform.InverseTransformPoint(pc.Camera.transform.position).x;
            _input.Frame.SwapShoulder = true;
            yield return null;
            yield return null;
            float sideAfter = pc.transform.InverseTransformPoint(pc.Camera.transform.position).x;
            Assert.Less(sideBefore * sideAfter, 0f, "camera switched shoulders");

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

            // PERF_BUDGET targets (ADR-028). Lower bounds prove the counters were live.
            Assert.That(stats.P99Ms, Is.GreaterThan(0f).And.LessThanOrEqualTo(10f), "render time p99");
            Assert.That(drawCalls, Is.GreaterThan(0).And.LessThanOrEqualTo(2500));
            Assert.That(triangles, Is.GreaterThan(0).And.LessThanOrEqualTo(1500000));
            Assert.That(setPasses, Is.GreaterThan(0).And.LessThanOrEqualTo(60));
        }
    }
}

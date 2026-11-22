using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #458: a swimmer gets out of the water. Swimming into a sloped bank or a low lip climbs out; Space climbs a pier or
    // a pool wall; a wall too high for swimming into still needs Space. Then the island's own coast, gentle and steep.
    public class SwimExitPlayTests
    {
        sealed class Input : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static GameObject Box(string name, Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
            return go;
        }

        // Swims from `from` toward `to` (W, and Space held when `space`) for at most `seconds`; true once out of the
        // water and standing again.
        static IEnumerator SwimOut(PlayerController pc, Input input, Vector3 from, Vector3 to, bool space, float seconds, bool[] result)
        {
            input.Frame.Move = Vector2.zero;
            input.Frame.JumpHeld = false;
            pc.Teleport(from);
            yield return WaitUntil(() => pc.Swimming, 3f, "swimming at " + from);
            pc.LookAt(new Vector3(to.x, pc.transform.position.y + 0.5f, to.z));
            yield return new WaitForSeconds(0.3f);
            input.Frame.Move = new Vector2(0f, 1f);
            input.Frame.JumpHeld = space;
            float end = Time.time + seconds;
            while (pc.Swimming && Time.time < end) yield return null;
            input.Frame.JumpHeld = false;
            input.Frame.Move = Vector2.zero;
            yield return new WaitForSeconds(1.2f); // the climb runs out, the body stands
            result[0] = !pc.Swimming && pc.BodyStance == Stance.Stand;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Pool_RampAndLowLipByWalking_PierAndWallBySpace()
        {
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;

            // A 2 m pool 16 m across (walls 1 m over the surface), a pier 1.5 m over it on +x, a lip 0.4 m over it on -x
            // and a 30 degree ramp rising out of it on +z.
            var c = new Vector3(3000f, 0f, 3000f);
            const float surface = 2f;
            var pool = TestPool.Create(c, 16f, surface, out _);
            var made = new List<GameObject> { pool };
            const float pierTop = surface + 1.5f, lipTop = surface + 0.4f;
            made.Add(Box("Pier", new Vector3(3006f, pierTop / 2f, 2997f), new Vector3(4f, pierTop, 4f), Quaternion.identity));
            made.Add(Box("Lip", new Vector3(2994f, lipTop / 2f, 2997f), new Vector3(4f, lipTop, 4f), Quaternion.identity));
            var tilt = Quaternion.Euler(-30f, 0f, 0f);
            var onSlope = new Vector3(3000f, surface, 3003f); // the ramp's top face crosses the surface here
            made.Add(Box("Ramp", onSlope - tilt * Vector3.up * 0.2f, new Vector3(4f, 0.4f, 12f), tilt));
            Physics.SyncTransforms();
            var r = new bool[1];

            // The ramp: swimming up it is enough.
            yield return SwimOut(pc, input, new Vector3(3000f, 0.1f, 2998f), new Vector3(3000f, 0f, 3010f), false, 8f, r);
            Assert.IsTrue(r[0], "swimming up a 30 degree bank stands up out of the water: " + pc.transform.position);
            Assert.Greater(pc.transform.position.y, surface - SwimRules.Current.ExitDepth - 0.05f, "standing on the bank");

            // The low lip: swimming into it climbs out.
            yield return SwimOut(pc, input, new Vector3(2999f, 0.1f, 2997f), new Vector3(2990f, 0f, 2997f), false, 6f, r);
            Assert.IsTrue(r[0], "swimming into a lip 0.4 m over the surface climbs out: " + pc.transform.position);
            Assert.AreEqual(lipTop, pc.transform.position.y, 0.15f, "on top of the lip");

            // The pool wall (1 m over the surface): swimming into it alone stays in the water, Space climbs it.
            yield return SwimOut(pc, input, new Vector3(3000f, 0.1f, 2995f), new Vector3(3000f, 0f, 2980f), false, 4f, r);
            Assert.IsFalse(r[0], "a 1 m wall is not climbed by swimming into it");
            yield return SwimOut(pc, input, pc.transform.position, new Vector3(3000f, 0f, 2980f), true, 4f, r);
            Assert.IsTrue(r[0], "Space at the pool wall climbs out: " + pc.transform.position);
            Assert.AreEqual(surface + 1f, pc.transform.position.y, 0.15f, "on top of the wall");

            // The pier, 1.5 m over the surface: Space climbs it.
            yield return SwimOut(pc, input, new Vector3(3001f, 0.1f, 2997f), new Vector3(3010f, 0f, 2997f), true, 6f, r);
            Assert.IsTrue(r[0], "Space at a pier 1.5 m over the surface climbs out: " + pc.transform.position);
            Assert.AreEqual(pierTop, pc.transform.position.y, 0.15f, "on the pier");

            foreach (var go in made) UnityEngine.Object.Destroy(go);
        }

        // Along rays out from the island's middle: where the coast goes under. `swimFrom` is a point at least 2.2 m
        // deep, `landward` the way back; `rise` the coast's steepness (height over distance from 1.5 m deep to 0.3 m up).
        struct Shore { public Vector3 SwimFrom, Landward; public float Rise; public int Angle; }

        static List<Shore> Shores(float step)
        {
            var found = new List<Shore>();
            var world = WorldInfo.Current;
            var terrain = Terrain.activeTerrain;
            if (world == null || terrain == null) return found;
            float H(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.transform.position.y;
            var centre = new Vector2(world.Size * 0.5f, world.Size * 0.5f);
            for (float a = 0; a < 360f; a += step)
            {
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                float dry = -1f, wet = -1f, deep = -1f;
                for (float d = 100f; d < world.Size * 0.5f - 4f; d += 0.5f)
                {
                    float h = H(centre + dir * d);
                    if (h >= world.SeaLevel + 0.3f) { dry = d; wet = deep = -1f; continue; }
                    if (dry < 0f) continue;
                    if (wet < 0f && h < world.SeaLevel - 1.5f) wet = d;
                    if (h < world.SeaLevel - 2.2f) { deep = d; break; }
                }
                if (dry < 0f || wet < 0f || deep < 0f) continue;
                var p = centre + dir * (deep + 1f);
                found.Add(new Shore
                {
                    SwimFrom = new Vector3(p.x, H(p) + 0.1f, p.y), Landward = new Vector3(-dir.x, 0f, -dir.y),
                    Rise = 1.8f / Mathf.Max(0.25f, wet - dry), Angle = (int)a,
                });
            }
            return found;
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator IslandCoast_GentleByWalking_SteepBySpace()
        {
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "local player");
            float end = Time.realtimeSinceStartup + 30f;
            while (SpawnProtection.InputBlocked && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;

            var shores = Shores(7.5f);
            Assert.Greater(shores.Count, 6, "coast found");
            shores.Sort((x, y) => x.Rise.CompareTo(y.Rise));
            var picks = new List<Shore> { shores[0], shores[shores.Count / 4], shores[shores.Count / 2] };
            for (int i = 1; i <= 4; i++) picks.Add(shores[shores.Count - i]);
            var r = new bool[1];
            var failed = new List<string>();
            foreach (var s in picks)
            {
                var to = s.SwimFrom + s.Landward * 40f;
                yield return SwimOut(pc, input, s.SwimFrom, to, false, 12f, r);
                bool walked = r[0], spaced = walked;
                if (!walked)
                {
                    yield return SwimOut(pc, input, pc.transform.position, to, true, 6f, r);
                    spaced = r[0];
                }
                Debug.Log($"[SwimExit] angle {s.Angle} rise {s.Rise:F2} walked {walked} space {spaced} at {pc.transform.position}");
                if (!spaced) failed.Add($"{s.Angle}deg rise {s.Rise:F2} stuck at {pc.transform.position}");
                else if (!walked && s.Rise < 1f) failed.Add($"{s.Angle}deg rise {s.Rise:F2}: a bank under 45 degrees needed Space");
            }
            Assert.IsEmpty(failed, "every coast lets the swimmer out");
        }
    }
}

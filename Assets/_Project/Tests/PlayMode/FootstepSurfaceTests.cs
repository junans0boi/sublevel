using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Footsteps on the generated World's roads: asphalt rings like concrete, never like water, whatever terrain
    // layer lies under the strip. Walks a real player over asphalt tiles and reads what the audio chose.
    public class FootstepSurfaceTests
    {
        class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        ScriptedInput _input;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Walking_on_asphalt_plays_concrete_steps_never_water()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => WorldInfo.Current != null && PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "World with a local player");
            yield return null;
            var pc = PlayerController.Local;
            var audio = Combatant.LocalPlayer.GetComponent<CombatantAudio>();

            // Centre line of every asphalt strip, a few tiles per mesh (three vertices per cross-section).
            var tiles = new List<Vector3>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m => m.name.StartsWith("Asphalt")))
            {
                var v = mf.sharedMesh.vertices;
                int sections = v.Length / 3;
                for (int k = 1; k <= 3; k++) tiles.Add(mf.transform.TransformPoint(v[sections * k / 4 * 3 + 1]));
            }
            Assert.GreaterOrEqual(tiles.Count, 6, "the world has asphalt roads");

            var seen = new SortedDictionary<FootSurface, int>();
            foreach (var feet in tiles.Take(12))
            {
                Assert.AreEqual(FootSurface.Concrete, CombatantAudio.SurfaceAt(feet), $"asphalt at {feet}");
                pc.Teleport(feet + Vector3.up * 0.1f);
                yield return new WaitForSeconds(0.3f);
                _input.Frame.Move = new Vector2(0f, 1f);
                int steps = audio.FootstepsPlayed;
                yield return WaitUntil(() => audio.FootstepsPlayed > steps, 3f, "a footstep on the road");
                _input.Frame.Move = Vector2.zero;
                seen[audio.LastSurface] = seen.TryGetValue(audio.LastSurface, out var n) ? n + 1 : 1;
            }
            Debug.Log("FOOTSTEP-ROADS " + string.Join(", ", seen.Select(p => p.Key + "=" + p.Value)));
            Assert.IsFalse(seen.ContainsKey(FootSurface.Water), "no water steps on asphalt");
            Assert.AreEqual(1, seen.Count, "only concrete steps on asphalt");
            Assert.IsTrue(seen.ContainsKey(FootSurface.Concrete));
        }
    }
}

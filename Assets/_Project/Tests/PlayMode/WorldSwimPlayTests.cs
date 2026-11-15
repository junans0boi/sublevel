using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // The sea is swimmable (#146, ADR-144): walking into it from a beach, the body swims from 1.2 m of water and the
    // ring of invisible walls stops it at about 5 m, so the coast has a band of open water to swim in.
    public class WorldSwimPlayTests
    {
        public sealed class Input : IPlayerInput
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

        // A beach: walking from the island's middle outward, the first place the ground goes under the sea. `land` is
        // dry ground a few metres before it, `outward` the way to the water.
        public static bool FindShore(out Vector3 land, out Vector3 outward)
        {
            var world = WorldInfo.Current;
            var terrain = Terrain.activeTerrain;
            land = outward = Vector3.zero;
            if (world == null || terrain == null) return false;
            var centre = new Vector2(world.Size * 0.5f, world.Size * 0.5f);
            for (int a = 0; a < 360; a += 10)
            {
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                for (float d = 100f; d < world.Size * 0.5f - 4f; d += 1f)
                {
                    var p = centre + dir * d;
                    float h = terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.transform.position.y;
                    if (h >= world.SeaLevel - 0.3f) continue;
                    var back = centre + dir * (d - 14f);
                    float hb = terrain.SampleHeight(new Vector3(back.x, 0f, back.y)) + terrain.transform.position.y;
                    land = new Vector3(back.x, hb + 0.1f, back.y);
                    outward = new Vector3(dir.x, 0f, dir.y);
                    return hb > world.SeaLevel + 0.3f;
                }
            }
            return false;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Coast_WalkIn_SwimsFrom1_2m_AndTheBarrierStopsAtAbout5m()
        {
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 40f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            end = Time.realtimeSinceStartup + 30f;
            while (SpawnProtection.InputBlocked && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(FindShore(out var land, out var outward), "a beach to walk from");
            pc.Teleport(land);
            pc.LookAt(land + outward * 50f + Vector3.up * land.y);
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(pc.Swimming, "dry on the beach");
            Assert.IsFalse(me.Holstered.Value);

            input.Frame.Move = new Vector2(0f, 1f);
            float maxDepth = 0f;
            Vector3 last = pc.transform.position;
            float still = 0f, until = Time.realtimeSinceStartup + 120f, nextCheck = Time.time + 0.5f;
            bool swam = false;
            while (still < 2f && Time.realtimeSinceStartup < until)
            {
                yield return null;
                float depth = WaterVolume.DepthAt(pc.transform.position, out _, out _);
                maxDepth = Mathf.Max(maxDepth, depth);
                swam |= pc.Swimming;
                if (Time.time < nextCheck) continue;
                nextCheck = Time.time + 0.5f;
                still = (pc.transform.position - last).sqrMagnitude < 0.04f ? still + 0.5f : 0f; // under 0.2 m in half a second
                last = pc.transform.position;
            }
            input.Frame.Move = Vector2.zero;
            Debug.Log($"[WorldSwim] end {pc.transform.position} from {land} depth {WaterVolume.DepthAt(pc.transform.position, out float sf, out float fl):F2} surface {sf:F2} floor {fl:F2} blocked {SpawnProtection.InputBlocked}");
            Assert.IsTrue(swam, "walking into the sea ends in swimming");
            Assert.IsTrue(pc.Swimming, "still swimming at the wall");
            Assert.GreaterOrEqual(maxDepth, SwimRules.Current.EnterDepth, "went deeper than the swim depth");
            Assert.LessOrEqual(maxDepth, 5.6f, "the barrier holds at about 5 m");
            Assert.IsTrue(me.Holstered.Value, "weapon away while swimming");
            Debug.Log($"[WorldSwim] shore {land} outward {outward} max depth {maxDepth:F2}");
        }
    }
}

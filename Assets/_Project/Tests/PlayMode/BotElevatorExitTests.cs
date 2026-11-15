using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #181: bots that share an elevator (ADR-055, 0.8 m apart) walk out of the doors without ending up inside one another.
    public class BotElevatorExitTests
    {
        sealed class NoPlayerInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator BotsLeavingTheElevators_DoNotOverlap()
        {
            PlayerController.InputOverride = new NoPlayerInput();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null) { Assert.Less(Time.realtimeSinceStartup, end, "world loaded"); yield return null; }
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            end = Time.realtimeSinceStartup + 60f;
            while (!SpawnProtection.InputBlocked) { if (Time.realtimeSinceStartup > end) break; yield return null; }
            end = Time.realtimeSinceStartup + 60f;
            while (SpawnProtection.InputBlocked) { Assert.Less(Time.realtimeSinceStartup, end, "doors opened"); yield return null; }
            float worst = 99f, overlapSeconds = 0f;
            string worstAt = "";
            for (float t = 0f; t < 8f; t += Time.deltaTime)
            {
                var bots = Combatant.All.Where(c => c != null && !c.IsPlayer && !c.Dead.Value).ToList();
                bool any = false;
                for (int i = 0; i < bots.Count; i++)
                    for (int j = i + 1; j < bots.Count; j++)
                    {
                        var a = bots[i].transform.position; var b = bots[j].transform.position;
                        if (Mathf.Abs(a.y - b.y) > 1f) continue;
                        float d = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                        if (d < 0.45f) any = true;
                        if (d < worst) { worst = d; worstAt = $"{bots[i].name}/{bots[j].name} at t+{t:F1}"; }
                    }
                if (any) overlapSeconds += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[#181] bots {Combatant.All.Count(c => c != null && !c.IsPlayer)}, closest pair {worst:F2} m ({worstAt}), seconds with a pair inside 0.45 m: {overlapSeconds:F1}");
            Assert.Less(overlapSeconds, 0.5f, $"bots overlap for {overlapSeconds:F1} s leaving the elevators (closest {worst:F2} m, {worstAt})");
        }
    }
}

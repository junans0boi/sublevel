using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Integration review shots of the r5 effects in the real World scene (not TestBox): shooting a bot -> blood and holes on the
    // wall behind -> the death ragdoll; spent cases; the rifle sidestep. Run on purpose with
    //   WORLDFX_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.WorldFxCaptureTests
    [Explicit, Category("Capture")]
    public class WorldFxCaptureTests
    {
        const int Rifle = 3;

        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Reload = false;
                Frame.Look = Vector2.zero;
                return f;
            }
        }

        BotRules _botRules;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            if (_botRules != null) BotRules.Current = _botRules;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static void Save(Camera cam, string path, Vector3 at, Vector3 lookAt, float fov)
        {
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(lookAt - at));
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[WorldFxCapture] SHOT " + path);
        }

        static Vector3 Ground(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : p;
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator ShootBotRagdoll_Casings_RifleSidestep()
        {
            string folder = Environment.GetEnvironmentVariable("WORLDFX_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/worldfx";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            string P(string n) => Path.Combine(folder, n);

            _botRules = BotRules.Current;
            BotRules.Current = new BotRules { TargetsPlayers = false };
            var input = new ScriptedInput();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 60f, "the local player in World");
            yield return new WaitForSeconds(3f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            me.StanceSpreadFactor = 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 120 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 5f, "rifle in hand");
            GameSettings.Preview(SettingId.Blood, (float)BloodLevel.Normal);

            // A wall near where the player stands: the first ray at chest height that meets an upright, non-combatant surface 6-18 m away.
            var start = me.transform.position;
            Vector3 dir = Vector3.forward, wall = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 24 && !found; i++)
            {
                var d = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
                if (Physics.Raycast(start + Vector3.up * 1.3f, d, out var hit, 18f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.distance > 6f && Mathf.Abs(hit.normal.y) < 0.2f && hit.collider.GetComponentInParent<Combatant>() == null)
                {
                    dir = d; wall = hit.point; found = true;
                }
            }
            if (!found) wall = start + dir * 12f;
            Debug.Log($"[WorldFxCapture] start {start} wall {(found ? "hit" : "none")} {wall}");

            // ---- 1. shot -> blood and holes -> death ragdoll ----
            Combatant victim = null;
            float best = float.MaxValue;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer || c.Dead.Value || c.GetComponentInChildren<CombatantRagdoll>() == null) continue;
                float dd = (c.transform.position - start).sqrMagnitude;
                if (dd < best) { best = dd; victim = c; }
            }
            Assert.IsNotNull(victim, "a living bot with a ragdoll in World");
            foreach (var part in victim.GetComponents<MonoBehaviour>())
                if (part.GetType().Name == "BotController") part.enabled = false;
            victim.ServerDamageFilter = _ => 0f;
            var standAt = Ground(wall - dir * 1.6f);
            victim.transform.SetPositionAndRotation(standAt, Quaternion.LookRotation(-dir));
            pc.Teleport(Ground(standAt - dir * 7f) + Vector3.up * 0.1f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(1f);

            var cam = new GameObject("WorldFxCam").AddComponent<Camera>();
            cam.enabled = false;
            cam.CopyFrom(pc.Camera);
            var side = Vector3.Cross(Vector3.up, dir).normalized;

            IEnumerator Fire(Vector3 at)
            {
                pc.LookAt(at);
                yield return new WaitForSeconds(0.2f);
                Assert.IsTrue(me.ServerFire(at - me.EyePosition), "fired at " + at);
            }
            var chest = victim.transform.position + Vector3.up * 1.15f;
            int stains = ImpactFx.BloodStains, bursts = ImpactFx.DebrisBursts;
            for (int i = 0; i < 4; i++) yield return Fire(chest + new Vector3(0f, UnityEngine.Random.Range(-0.25f, 0.3f), 0f) + side * UnityEngine.Random.Range(-0.1f, 0.1f));
            for (int i = 0; i < 5; i++) yield return Fire(wall + side * UnityEngine.Random.Range(0.7f, 1.6f) * (i % 2 == 0 ? 1f : -1f) + Vector3.up * UnityEngine.Random.Range(-0.6f, 0.6f));
            yield return new WaitForSeconds(0.8f);
            Debug.Log($"[WorldFxCapture] blood stains +{ImpactFx.BloodStains - stains}, debris bursts +{ImpactFx.DebrisBursts - bursts}");

            var rig = victim.GetComponentInChildren<CombatantRagdoll>();
            victim.ServerDamageFilter = null;
            victim.ServerTakeDamage(Mathf.Max(0f, victim.Hp.Value - 1f));
            yield return null;
            yield return Fire(victim.transform.position + Vector3.up * 1.15f);
            yield return WaitUntil(() => victim.Dead.Value, 3f, "the shot to kill");
            yield return WaitUntil(() => rig.Simulating, 2f, "the ragdoll");
            yield return new WaitForSeconds(0.25f);
            var sceneAt = standAt - dir * 3.2f + side * 2.4f + Vector3.up * 1.8f;
            Save(cam, P("w1_shot_blood_ragdoll_falling.png"), sceneAt, standAt + Vector3.up * 0.7f, 55f);
            yield return new WaitForSeconds(2.5f);
            Save(cam, P("w1_shot_blood_ragdoll.png"), sceneAt, standAt + Vector3.up * 0.5f, 55f);
            Debug.Log($"[WorldFxCapture] ragdoll simulating {rig.Simulating} frozen {rig.Frozen} hips {rig.Hips.position} diedAt {standAt}");

            // ---- 2. spent cases ----
            var shooter = me.transform.position;
            var aimAt = Ground(shooter - dir * 10f) + Vector3.up * 0.6f; // away from the wall, at the ground
            int ejected = me.GetComponent<CasingFx>().Ejected;
            for (int i = 0; i < 5; i++) yield return Fire(aimAt + side * (i - 2) * 0.3f);
            yield return new WaitForSeconds(0.06f);
            var right = pc.transform.right;
            var gunAt = me.transform.position + Vector3.up * 1.3f;
            Save(cam, P("w2_casings_flight.png"), gunAt + right * 1.6f - pc.transform.forward * 0.4f + Vector3.up * 0.2f, gunAt + right * 0.5f, 50f);
            yield return new WaitForSeconds(1.2f);
            var feet = me.transform.position;
            Save(cam, P("w2_casings_ground.png"), feet + right * 1.2f + Vector3.up * 1.1f - pc.transform.forward * 0.6f, feet + right * 0.9f, 50f);
            Debug.Log($"[WorldFxCapture] cases ejected +{me.GetComponent<CasingFx>().Ejected - ejected}, live {CasingPool.Live}");
            Assert.Greater(me.GetComponent<CasingFx>().Ejected, ejected, "the rifle threw cases");

            // ---- 3. rifle sidestep (aimed, moving right) ----
            pc.Teleport(Ground(shooter) + Vector3.up * 0.1f);
            pc.LookAt(shooter - side * 20f + Vector3.up * 1.4f); // so the step to the right runs along the open line back toward the wall-free side
            yield return new WaitForSeconds(0.5f);
            input.Frame.AimHeld = true;
            input.Frame.Move = new Vector2(1f, 0f);
            yield return new WaitForSeconds(0.9f);
            for (int i = 0; i < 3; i++)
            {
                var p = me.transform.position;
                var f = pc.transform.forward; var r = pc.transform.right;
                Save(cam, P($"w3_rifle_sidestep_{i}.png"), p + f * 4.2f + r * 1.2f + Vector3.up * 1.3f, p + Vector3.up * 0.95f, 40f);
                yield return new WaitForSeconds(0.13f);
            }
            var v = me.GetComponent<CombatantMotion>().LocalVelocity;
            Debug.Log($"[WorldFxCapture] sidestep local velocity {v}");
            input.Frame.Move = Vector2.zero;
            input.Frame.AimHeld = false;
            Assert.Greater(v.x, 0.5f, "moving to the right");
        }
    }
}

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
    // Review shots of the result screen, the death cam and the pickup list, with every screen canvas on the shot.
    // Not part of the suite:  RES_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.ResultPickupCaptureTests
    [Explicit, Category("Capture")]
    public class ResultPickupCaptureTests
    {
        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 1280, 720 } };

        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() { var f = Frame; Frame.Interact = false; Frame.ZoomSteps = 0; Frame.Crouch = false; return f; }
        }

        ScriptedInput _input;

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("RES_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/resshots";
            Directory.CreateDirectory(folder);
            return folder;
        }

        IEnumerator LoadTestBox()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || MatchState.Instance == null || MatchState.Instance.TotalCount != 4)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.6f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // The player walks (in short hops) so the distance counter has something to show.
        static IEnumerator Walk(int hops)
        {
            var pc = PlayerController.Local;
            for (int i = 0; i < hops; i++)
            {
                pc.Teleport(pc.transform.position + pc.transform.forward * 4f);
                yield return null;
                yield return null;
            }
        }

        static IEnumerator Shot(string folder, string name, int w, int h)
        {
            yield return null; // not WaitForEndOfFrame: it stalls in a batch mode editor
            var cam = PlayerController.Local != null && PlayerController.Local.Camera != null ? PlayerController.Local.Camera : Camera.main;
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;
                if (canvases[i].isRootCanvas && modes[i] == RenderMode.ScreenSpaceOverlay)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = cam;
                    canvases[i].planeDistance = cam.nearClipPlane + 0.02f; // in front of any wall the camera is close to
                }
            }
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.targetTexture = before;
            for (int i = 0; i < canvases.Length; i++)
                if (canvases[i] != null && canvases[i].isRootCanvas) canvases[i].renderMode = modes[i];
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(folder, $"{name}_{w}x{h}.png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        static void Kill(Combatant victim, Combatant attacker)
        {
            var hit = new DamageInfo { Amount = 1000f, Attacker = attacker, Zone = HitZone.Body, WeaponId = 3, Cause = DamageCause.Shot };
            victim.ServerTakeDamage(in hit);
        }

        static Combatant AnyDummy()
        {
            foreach (var c in Combatant.All) if (!c.IsPlayer) return c;
            return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureWin()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f; // the dummies shoot back
            yield return Walk(12);
            yield return new WaitForSeconds(2.5f);
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].IsPlayer) Kill(all[i], me);
            float end = Time.realtimeSinceStartup + 5f;
            while (!ResultScreen.Instance.IsShown && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(0.5f);
            Debug.Log("[ResultShot] win: " + ResultScreen.Instance.PlacementText + " | dist " + ResultScreen.Instance.DistanceText);
            foreach (var s in Sizes) yield return Shot(folder, "result_win", s[0], s[1]);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureEliminatedAndDeathCam()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return Walk(8);
            var dummy = AnyDummy();
            Kill(me, dummy);
            yield return new WaitForSeconds(1.2f);
            foreach (var s in Sizes) yield return Shot(folder, "deathcam", s[0], s[1]);
            float end = Time.realtimeSinceStartup + ResultScreen.DeathCamSeconds + 3f;
            while (!ResultScreen.Instance.IsShown && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(0.5f);
            foreach (var s in Sizes) yield return Shot(folder, "result_eliminated", s[0], s[1]);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CapturePickupList()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var entries = new[]
            {
                new LootEntry { Kind = LootKind.Medkit, Amount = 1 },
                new LootEntry { Kind = LootKind.Bandage, Amount = 2 },
                new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 },
                new LootEntry { Kind = LootKind.Backpack, Tier = 2, Amount = 1 },
                new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 60 },
            };
            // One alone: the bottom prompt.
            MatchDirector.Instance.ServerSpawnLoot(entries[0], me.transform.position + me.transform.forward * 0.9f + Vector3.up * 0.3f);
            yield return new WaitForSeconds(1f);
            foreach (var s in Sizes) yield return Shot(folder, "pickup_single", s[0], s[1]);
            for (int i = 1; i < entries.Length; i++)
            {
                MatchDirector.Instance.ServerSpawnLoot(entries[i], me.transform.position + me.transform.forward * (0.9f + 0.45f * i) + me.transform.right * (i % 2 == 0 ? 0.5f : -0.5f) + Vector3.up * 0.3f);
                yield return new WaitForSeconds(0.2f);
            }
            yield return new WaitForSeconds(1.2f);
            foreach (var s in Sizes) yield return Shot(folder, "pickup_list", s[0], s[1]);
            _input.Frame.ZoomSteps = -2; // two notches down
            yield return null;
            yield return new WaitForSeconds(0.3f);
            foreach (var s in Sizes) yield return Shot(folder, "pickup_list_wheel", s[0], s[1]);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureGroundLootAndCrouch()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            var at = me.transform.position + me.transform.forward * 1.6f;
            var tiers = new[] { 1, 2, 3 };
            for (int i = 0; i < 3; i++)
                MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Backpack, Tier = tiers[i], Amount = 1 }, at + me.transform.right * (i - 1) * 0.8f + Vector3.up * 0.3f);
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }, at + me.transform.forward * 0.7f + Vector3.up * 0.3f);
            yield return new WaitForSeconds(1.5f);
            pc.LookAt(at + Vector3.down * 0.3f);
            yield return new WaitForSeconds(0.3f);
            foreach (var s in Sizes) yield return Shot(folder, "ground_backpacks", s[0], s[1]);
            _input.Frame.Crouch = true;
            yield return new WaitForSeconds(1.2f);
            foreach (var s in Sizes) yield return Shot(folder, "crouch", s[0], s[1]);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureVehicleExplosion()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            Vehicle car = null;
            float best = float.MaxValue;
            foreach (var v in Vehicle.All)
                if (v != null && !v.IsMotorcycle && (v.transform.position - me.transform.position).sqrMagnitude < best) { car = v; best = (v.transform.position - me.transform.position).sqrMagnitude; }
            Assert.IsNotNull(car, "a car in the scene");
            var damage = car.GetComponent<VehicleDamage>();
            var visual = car.GetComponent<VehicleDamageVisual>();
            var away = me.transform.position - car.transform.position; away.y = 0f;
            Debug.Log($"[ResultShot] car {car.name} at {car.transform.position}, player {me.transform.position}");
            pc.Teleport(car.transform.position + away.normalized * 9f + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.5f);
            pc.LookAt(car.transform.position + Vector3.up * 1f);
            yield return new WaitForSeconds(0.3f);
            damage.ServerBlast(car.transform.position + Vector3.up, d => 10000f, 1f);
            float end = Time.realtimeSinceStartup + 20f;
            while (visual.ExplosionsShown == 0 && Time.realtimeSinceStartup < end) yield return null;
            Assert.Greater(visual.ExplosionsShown, 0, "the car exploded");
            foreach (float wait in new[] { 0.12f, 0.2f, 0.4f, 0.8f })
            {
                yield return new WaitForSeconds(wait);
                yield return Shot(folder, "explosion_" + (int)(wait * 1000), 1280, 720);
            }
        }
    }
}

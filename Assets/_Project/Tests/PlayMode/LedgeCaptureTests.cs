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
    // Review shots of the ledge hang and pull-up (#115): a 2.3 m block in the TestBox, jumped at, hung from and climbed,
    // seen from the side, from behind and through the player's own camera. Not part of the suite: run on purpose with
    //   LEDGE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.LedgeCaptureTests
    [Explicit, Category("Capture")]
    public class LedgeCaptureTests
    {
        sealed class Input : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Jump = false;
                return f;
            }
        }

        // Renders after every LateUpdate (the ledge pose is a LateUpdate bone pose), from a fixed camera or the player's.
        [DefaultExecutionOrder(30000)]
        sealed class LateShot : MonoBehaviour
        {
            public string Path;
            public Camera Cam;

            void LateUpdate()
            {
                if (Path == null || Cam == null) return;
                const int w = 1280, h = 720;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                var before = Cam.targetTexture;
                Cam.targetTexture = rt;
                Cam.Render();
                Cam.targetTexture = before;
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path, tex.EncodeToPNG());
                Destroy(tex);
                Debug.Log("[LedgeCapture] " + Path);
                Path = null;
            }
        }

        string _folder;
        LateShot _shot;
        Camera _fixed;

        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureLedgeShots()
        {
            _folder = Environment.GetEnvironmentVariable("LEDGE_SHOTS");
            if (string.IsNullOrEmpty(_folder)) _folder = "Logs/ledgeshots";
            _folder = Path.GetFullPath(_folder);
            Directory.CreateDirectory(_folder);
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;

            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "LedgeBlock";
            block.transform.position = new Vector3(0f, 1.15f, -21f);
            block.transform.localScale = new Vector3(3f, 2.3f, 2f);
            Physics.SyncTransforms();
            pc.Teleport(new Vector3(0f, 0.1f, -22.6f));
            pc.LookAt(new Vector3(0f, 1.6f, 0f));
            yield return new WaitForSeconds(0.4f);

            var go = new GameObject("LedgeShotCamera");
            _fixed = go.AddComponent<Camera>();
            _fixed.enabled = false;
            if (pc.Camera != null) _fixed.CopyFrom(pc.Camera);
            _fixed.fieldOfView = 50f;
            _shot = go.AddComponent<LateShot>();

            input.Frame.Jump = true;
            end = Time.realtimeSinceStartup + 3f;
            while (!pc.OnLedge && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(pc.OnLedge, "caught the edge");
            yield return new WaitForSeconds(0.5f);
            var at = pc.transform.position;
            Debug.Log($"[LedgeCapture] hanging at {at}, pose {me.GetComponent<CombatantAnimation>().LedgePose:F2}");

            yield return Shot("ledge_01_hang_side", at + new Vector3(3.2f, 1.5f, -1.8f), at + new Vector3(0f, 1.2f, 0.2f));
            yield return Shot("ledge_02_hang_behind", at + new Vector3(1.2f, 1.5f, -2.6f), at + new Vector3(0f, 1.5f, 0.3f));
            yield return Shot("ledge_03_hang_close", at + new Vector3(0.9f, 2.3f, -0.9f), at + new Vector3(0f, 1.9f, 0.4f));
            yield return Shot("ledge_04_hang_tpp", null, default);

            input.Frame.Jump = true;
            float started = Time.time;
            var side = at + new Vector3(3.6f, 2.6f, -1.9f);
            var look = at + new Vector3(0f, 2.0f, 0.4f);
            foreach (float when in new[] { 0.2f, 0.4f, 0.55f, 0.7f })
            {
                while (Time.time - started < when && pc.InParkour) yield return null;
                Debug.Log($"[LedgeCapture] climb {Time.time - started:F2}s: {pc.ParkourUnderWay} at {pc.transform.position}");
                yield return Shot($"ledge_1{(int)(when * 10)}_climb_side_{(int)(when * 100):000}", side, look);
            }
            while (pc.InParkour) yield return null;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("ledge_20_on_top_side", side, look);
            Debug.Log($"[LedgeCapture] done on top at {pc.transform.position}: {_folder}");
            PlayerController.InputOverride = null;
        }

        // ADR-492: a sprint jump across a 2 m gap at a block 1.5 m higher, Space held: the mantle seen from the side.
        //   LEDGE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.LedgeCaptureTests.CaptureMantleShots
        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureMantleShots()
        {
            _folder = Environment.GetEnvironmentVariable("LEDGE_SHOTS");
            if (string.IsNullOrEmpty(_folder)) _folder = "Logs/ledgeshots";
            _folder = Path.GetFullPath(_folder);
            Directory.CreateDirectory(_folder);
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            foreach (var (name, center, size) in new[] {
                ("MantleFrom", new Vector3(0f, 0.5f, -27f), new Vector3(3f, 1f, 5f)),
                ("MantleTo", new Vector3(0f, 1.25f, -19.5f), new Vector3(3f, 2.5f, 6f)) })
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = name;
                block.transform.position = center;
                block.transform.localScale = size;
            }
            Physics.SyncTransforms();
            pc.Teleport(new Vector3(0f, 1.1f, -27.5f));
            pc.LookAt(new Vector3(0f, 2.6f, 0f));
            yield return new WaitForSeconds(0.4f);

            var go = new GameObject("MantleShotCamera");
            _fixed = go.AddComponent<Camera>();
            _fixed.enabled = false;
            if (pc.Camera != null) _fixed.CopyFrom(pc.Camera);
            _fixed.fieldOfView = 50f;
            _shot = go.AddComponent<LateShot>();

            input.Frame.Sprint = true;
            input.Frame.Move = new Vector2(0f, 1f);
            end = Time.realtimeSinceStartup + 3f;
            while (pc.transform.position.z < -24.85f && Time.realtimeSinceStartup < end) yield return null;
            input.Frame.Jump = input.Frame.JumpHeld = true;
            while (pc.ParkourUnderWay != ParkourMove.LedgeClimb && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(ParkourMove.LedgeClimb, pc.ParkourUnderWay, "mantling");
            input.Frame.Move = Vector2.zero;
            input.Frame.Sprint = input.Frame.JumpHeld = false;
            float started = Time.time;
            var side = new Vector3(4.2f, 2.6f, -23.2f);
            var look = new Vector3(0f, 2.2f, -22.4f);
            foreach (float when in new[] { 0f, 0.2f, 0.4f, 0.6f })
            {
                while (Time.time - started < when && pc.InParkour) yield return null;
                var anim = Combatant.LocalPlayer.GetComponent<CombatantAnimation>();
                var animator = Combatant.LocalPlayer.GetComponentInChildren<Animator>();
                var hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                Debug.Log($"[LedgeCapture] mantle {Time.time - started:F2}s: {pc.ParkourUnderWay} at {pc.transform.position}, pose {anim.LedgePose:F2}, right hand {(hand != null ? hand.position.ToString("F2") : "?")}");
                yield return Shot($"mantle_{(int)(when * 100):000}_side", side, look);
            }
            yield return Shot("mantle_060_tpp", null, default);
            while (pc.InParkour) yield return null;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("mantle_90_on_top_side", side, look);
            Debug.Log($"[LedgeCapture] mantle done on top at {pc.transform.position}: {_folder}");
            Assert.AreEqual(2.5f, pc.transform.position.y, 0.12f, "on top");
            PlayerController.InputOverride = null;
        }

        // `from` null: through the player's own camera.
        IEnumerator Shot(string name, Vector3? from, Vector3 at)
        {
            var pc = PlayerController.Local;
            if (from.HasValue)
            {
                _fixed.transform.SetPositionAndRotation(from.Value, Quaternion.LookRotation(at - from.Value));
                _shot.Cam = _fixed;
            }
            else _shot.Cam = pc.Camera;
            _shot.Path = Path.Combine(_folder, name + ".png");
            while (_shot.Path != null) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
        }
    }
}

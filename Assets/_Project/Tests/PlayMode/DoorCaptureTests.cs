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
    // Review shots of the map's doors (#137): shut and open, from outside and from inside. Run on purpose with
    //   DOOR_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.DoorCaptureTests
    [Explicit, Category("Capture")]
    public class DoorCaptureTests
    {
        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

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
                Cam.targetTexture = rt;
                Cam.Render();
                Cam.targetTexture = null;
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path, tex.EncodeToPNG());
                Path = null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator CaptureDoors()
        {
            string folder = Environment.GetEnvironmentVariable("DOOR_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/doorshots";
            folder = System.IO.Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || DoorSystem.Instance == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            var system = DoorSystem.Instance;

            // Doors with a clear 4 m of ground in front, on the outside of a building (a free line from 4 m out to the door).
            var picks = new System.Collections.Generic.List<Door>();
            for (int i = 0; i < system.Count && picks.Count < 3; i++)
            {
                var d = system[i];
                foreach (float sign in new[] { 1f, -1f })
                {
                    var from = d.transform.position + d.Forward * 4f * sign + Vector3.up * 1.6f;
                    var to = d.transform.position + Vector3.up * 1.2f;
                    if (Physics.Linecast(from, to, out var hit) && hit.collider == d.Panel && Physics.Raycast(d.transform.position + Vector3.up * 1.2f - d.Forward * 3f * sign, -d.Forward * sign, 3f) == false)
                    {
                        // Skip doors seen again from the other side.
                        if (!picks.Contains(d)) picks.Add(d);
                    }
                }
            }
            Assert.Greater(picks.Count, 0, "a door seen from outside");
            var go = new GameObject("DoorShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            if (pc.Camera != null) cam.CopyFrom(pc.Camera);
            cam.fieldOfView = 60f;
            var shot = go.AddComponent<LateShot>();
            int n = 0;
            foreach (var d in picks)
            {
                pc.Teleport(d.transform.position + Vector3.up * 0.1f + d.Forward * 6f);
                yield return new WaitForSeconds(1.0f); // the map streams in around the player
                foreach (float sign in new[] { 1f, -1f })
                {
                    var eye = d.transform.position + d.Forward * 3.2f * sign + Vector3.up * 1.6f;
                    var look = d.transform.position + Vector3.up * 1.1f;
                    if (!Physics.Linecast(eye, look, out var h) || h.collider != d.Panel) continue;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    system.ServerSet(d.Index, DoorState.Closed);
                    yield return new WaitForSeconds(0.6f);
                    shot.Cam = cam; shot.Path = System.IO.Path.Combine(folder, $"door{n}_{(sign > 0 ? "front" : "back")}_closed.png");
                    yield return null; yield return null;
                    system.ServerToggle(d.Index, eye);
                    yield return new WaitForSeconds(0.7f);
                    shot.Path = System.IO.Path.Combine(folder, $"door{n}_{(sign > 0 ? "front" : "back")}_open.png");
                    yield return null; yield return null;
                    system.ServerSet(d.Index, DoorState.Closed);
                }
                n++;
            }
            Debug.Log("[DoorCapture] shots in " + folder);
        }
    }
}

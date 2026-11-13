using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots for #161: a remote player sprinting, as client B sees it at 100 ms round trip and 2 % loss (top row), and
    // the same instants of the player's own body (bottom row). Not part of the suite: run on purpose with
    //   MOVE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.MovementNetCaptureTests
    [Explicit, Category("Capture")]
    public class MovementNetCaptureTests
    {
        MultiHarness _net;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _net = new MultiHarness();
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _net.Teardown();

        static void Show(GameObject root, bool on)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !on;
        }

        static void Save(Camera cam, string path)
        {
            var rt = new RenderTexture(640, 360, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(640, 360, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureRemoteSprint()
        {
            string folder = Environment.GetEnvironmentVariable("MOVE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/moveshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);

            RemoteClient a = null, b = null;
            yield return _net.AddClient(c => a = c);
            yield return _net.AddClient(c => b = c);
            _net.Simulate(a);
            _net.Simulate(b);
            var aOnB = MultiHarness.ReplicaOf(b.Manager, a.Player);
            a.Controller.Teleport(new Vector3(-27f, 0.1f, -2f));
            a.Controller.LookAt(new Vector3(-13f, 1.7f, -2f));
            yield return new WaitForSeconds(1f);

            var cam = new GameObject("MoveShotCam").AddComponent<Camera>();
            cam.enabled = false;
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 60f;

            // Everything of the runner's but the copy B holds is hidden for the top row; only the runner's own body for the bottom.
            var copies = new[] { a.Player.gameObject, aOnB.gameObject, MultiHarness.ReplicaOf(_net.Host, a.Player).gameObject };
            a.Input.Frame.Move = new Vector2(0, 1);
            a.Input.Frame.Sprint = true;
            float start = Time.time;
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                while (Time.time < start + 0.6f + i * 0.2f) yield return null;
                // Beside the lane, level with the runner's own body: both rows share the framing, so the gap is the lag.
                float cx = a.Player.transform.position.x;
                cam.transform.SetPositionAndRotation(new Vector3(cx, 1.6f, 2.5f), Quaternion.LookRotation(new Vector3(cx, 1f, -2f) - new Vector3(cx, 1.6f, 2.5f)));
                foreach (var c in copies) Show(c, false);
                Show(aOnB.gameObject, true);
                Save(cam, Path.Combine(folder, $"seen_{i:D2}.png"));
                Show(aOnB.gameObject, false);
                Show(a.Player.gameObject, true);
                Save(cam, Path.Combine(folder, $"own_{i:D2}.png"));
                log.AppendLine($"{i}: t {Time.time - start:F2} own x {a.Player.transform.position.x:F2} seen-by-B x {aOnB.transform.position.x:F2} (behind {a.Player.transform.position.x - aOnB.transform.position.x:F2} m)");
                foreach (var c in copies) Show(c, true);
            }
            a.Input.Frame.Move = Vector2.zero;
            a.Input.Frame.Sprint = false;
            File.WriteAllText(Path.Combine(folder, "log.txt"), log.ToString());
            Debug.Log("[MoveCapture]\n" + log);
            UnityEngine.Object.Destroy(cam.gameObject);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    // Review shots from inside the map's buildings at eye height (#190, #191). Run on purpose with
    //   INTERIOR_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.InteriorCaptureTests
    [Explicit, Category("Capture")]
    public class InteriorCaptureTests
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
            InteriorLights.Focus = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator CaptureInteriors()
        {
            string folder = Environment.GetEnvironmentVariable("INTERIOR_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/interiorshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            PlayerController.InputOverride = new Idle();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;

            var go = new GameObject("InteriorShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            if (pc.Camera != null) cam.CopyFrom(pc.Camera);
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.1f;
            var shot = go.AddComponent<LateShot>();
            InteriorLights.Focus = go.transform;

            // The first buildings of each kind that carry lamps; two houses.
            var wanted = new[] { ("House", 5), ("Warehouse", 1), ("FactoryHall", 1), ("Shed", 1) };
            var all = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith("Lot") && t.Find("Lamps") != null)
                .OrderBy(t => t.name, StringComparer.Ordinal).ToList();
            int shots = 0;
            foreach (var (kind, count) in wanted)
                foreach (var building in all.Where(t => t.name.EndsWith("_" + kind)).Take(count))
                {
                    var lamps = building.Find("Lamps");
                    var spots = new List<Transform>();
                    for (int i = 0; i < lamps.childCount; i++) spots.Add(lamps.GetChild(i));
                    // Up to three lamps per building, spread over its list: eye height under each.
                    var picks = spots.Take(4).ToList();
                    int p = 0;
                    foreach (var lamp in picks)
                    {
                        var hits = Physics.RaycastAll(lamp.position, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore);
                        // The floor: the nearest wide, thin box below the lamp (slabs and upper floors; furniture is thicker or smaller).
                        var floorHits = hits.Where(h => h.collider is BoxCollider fb && Vector3.Scale(fb.size, fb.transform.lossyScale) is var fs && fs.y < 0.45f && fs.x > 1.5f && fs.z > 1.5f).OrderBy(h => h.distance).ToList();
                        if (floorHits.Count == 0) continue;
                        float floorY = floorHits[0].point.y;
                        // A standing place near the lamp, then the furniture boxes (low boxes that are not walls) in view from it.
                        var offsets = new List<Vector3>();
                        for (float dx = -6f; dx <= 6f; dx += 1.5f)
                            for (float dz = -6f; dz <= 6f; dz += 1.5f) offsets.Add(new Vector3(dx, 0f, dz));
                        offsets.Sort((u, v) => u.sqrMagnitude.CompareTo(v.sqrMagnitude));
                        var seen = new HashSet<Collider>();
                        int taken = 0;
                        foreach (var off in offsets)
                        {
                            if (taken >= 3) break;
                            var spot = new Vector3(lamp.position.x, floorY, lamp.position.z) + off;
                            if (Physics.CheckCapsule(spot + Vector3.up * 0.45f, spot + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            var eye = spot + Vector3.up * 1.65f;
                            if (Vector3.Distance(new Vector3(eye.x, lamp.position.y, eye.z), lamp.position) > 7f) continue;
                            // Is the spot inside this building's own footprint? The lamp's room walls are in the way otherwise.
                            if (Physics.Linecast(lamp.position, eye + Vector3.up * 0.2f)) continue;
                            var near = Physics.OverlapSphere(eye, 6f, ~0, QueryTriggerInteraction.Ignore)
                                .OfType<BoxCollider>().OrderBy(c => (c.bounds.center - eye).sqrMagnitude).ToList();
                            foreach (var box in near)
                            {
                                if (taken >= 3) break;
                                var size = Vector3.Scale(box.size, box.transform.lossyScale);
                                bool furniture = size.y > 0.3f && size.y < 2.6f && Mathf.Max(size.x, size.z) < 4.5f && Mathf.Min(size.x, size.z) > 0.25f;
                                var target = box.bounds.center + Vector3.down * (box.bounds.extents.y * 0.3f);
                                if (!furniture || (target - eye).magnitude < 1.4f || seen.Contains(box)) continue;
                                // The furniture box must be what the view meets first (nothing but the room's air before it).
                                if (!Physics.Linecast(eye, target, out var seenFirst, ~0, QueryTriggerInteraction.Ignore) || seenFirst.collider != box) continue;
                                seen.Add(box);
                                cam.transform.position = eye;
                                cam.transform.rotation = Quaternion.LookRotation(target - eye);
                                yield return new WaitForSeconds(0.7f);
                                shot.Cam = cam;
                                shot.Path = Path.Combine(folder, $"{kind}_{building.name}_{p}_{taken}.png");
                                yield return null;
                                yield return null;
                                shots++;
                                taken++;
                            }
                        }
                        p++;
                    }
                }
            Debug.Log($"[InteriorCapture] {shots} shots in {folder}, lamps on: {GameObject.FindFirstObjectByType<InteriorLights>()?.ActiveCount}/{GameObject.FindFirstObjectByType<InteriorLights>()?.TotalLamps}");
            Assert.Greater(shots, 0);
        }
    }
}

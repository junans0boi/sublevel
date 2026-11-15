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
    // "I cannot get into the sports car": every vehicle TYPE parked in the World is walked up to from the side of its
    // driver's door with the real input (move keys, F) and got into, the prompt is on screen, and F gets out again.
    public class VehicleEnterExitTests
    {
        public static readonly string[] Types = { "GoblinV12", "FairheavenSW84", "LightbodyLifted85", "FairheavenLT80", "DirtBike", "RoadMotorcycle", "Shvan92" };

        sealed class Walker : IPlayerInput
        {
            public Vector2 Move;
            public bool Interact;
            public PlayerInputFrame Read()
            {
                var f = new PlayerInputFrame { SelectSlot = -1, Move = Move, Interact = Interact, InteractHeld = Interact };
                Interact = false;
                return f;
            }
        }

        sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };
            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = Frame.Jerrycan = false;
                Frame.Seat = -1;
                return f;
            }
        }

        static string TypeOf(Vehicle v) => v.name.Replace("(Clone)", "").Trim();

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
            VehicleInput.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator LoadWorld(Walker walker, Keys keys)
        {
            PlayerController.InputOverride = walker;
            VehicleInput.InputOverride = keys;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && Hud.Instance != null, 40f, "local player in World");
            yield return WaitUntil(() => Vehicle.All.Count(v => v != null) > 0, 20f, "world vehicles");
            for (int n = 0; SpawnProtection.InputBlocked && n < 160; n++) yield return new WaitForSeconds(0.25f);
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            yield return new WaitForSeconds(2f);
        }

        // Stands 4 m out from the driver's door (on the outer side), faces it and walks up with the move keys until the
        // door is within reach or the body stops the player. The only teleport is the starting point.
        static IEnumerator WalkToDoor(Walker walker, Vehicle v)
        {
            var pc = PlayerController.Local;
            var door = v.DoorPosition(0);
            var centre = v.transform.position;
            var outward = new Vector3(door.x - centre.x, 0f, door.z - centre.z);
            // The door's side of the body: along the vehicle's own right/left, not toward the centre point.
            var side = Vector3.Project(outward, v.transform.right);
            if (side.sqrMagnitude < 0.01f) side = outward;
            var start = door + side.normalized * 4f;
            pc.Teleport(new Vector3(start.x, door.y + 0.05f, start.z));
            yield return null;
            yield return null;
            float end = Time.realtimeSinceStartup + 8f;
            float reach = VehicleRules.Current.EnterRange * 0.5f;
            while (Time.realtimeSinceStartup < end)
            {
                var me = pc.transform.position;
                if (Vector2.Distance(new Vector2(me.x, me.z), new Vector2(door.x, door.z)) <= reach) break;
                pc.LookAt(new Vector3(door.x, pc.transform.position.y + 1.5f, door.z));
                walker.Move = new Vector2(0f, 1f);
                yield return null;
            }
            walker.Move = Vector2.zero;
            yield return null;
            yield return null;
        }

        // A player standing against the body (nose, tail, either side, corners) must be able to board: the points 0.5 m
        // off the body surface that are further than EnterRange from every free door.
        static string Unreachable(Vehicle v)
        {
            var bad = new List<string>();
            float range = VehicleRules.Current.EnterRange;
            var centre = v.transform.position;
            foreach (var p in new[] { new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(-1, 0), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                // The point of the body nearest to a far-away spot in that direction, then 0.4 m further out: where a
                // player walking up to the car stops against it.
                var dir = (v.transform.right * p.x + v.transform.forward * p.y).normalized;
                var far = centre + dir * 20f;
                var surface = centre;
                float best = float.MaxValue;
                foreach (var c in v.Colliders)
                {
                    if (c == null) continue;
                    var q = c.ClosestPoint(far);
                    float d = Vector3.Distance(q, far);
                    if (d < best) { best = d; surface = q; }
                }
                var world = surface + dir * 0.4f;
                world.y = v.DoorPosition(0).y;
                if (Vehicle.NearestEnterable(world, range, out _) != v) bad.Add($"({p.x:0},{p.y:0}) door {Vector3.Distance(world, v.DoorPosition(0)):F1} m");
            }
            return bad.Count > 0 ? "standing against the body, out of reach at " + string.Join(" ", bad) : null;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator EveryVehicleType_CanBeWalkedUpToAndEnteredAndLeft_InTheWorld()
        {
            var walker = new Walker();
            var keys = new Keys();
            yield return LoadWorld(walker, keys);
            var me = Combatant.LocalPlayer;
            var failures = new List<string>();
            var present = Vehicle.All.Where(x => x != null).Select(TypeOf).Distinct().ToList();
            Debug.Log("[#190] vehicle types in World: " + string.Join(", ", present));
            foreach (var type in Types)
            {
                var candidates = Vehicle.All.Where(x => x != null && TypeOf(x) == type && x.HasFreeSeat).ToList();
                if (candidates.Count == 0) { failures.Add(type + ": none parked in the World"); continue; }
                // One per type, the one with the most open ground at the driver's door.
                foreach (var v in candidates.Take(3))
                {
                    string tag = $"{type} at {v.transform.position:F0}";
                    yield return WalkToDoor(walker, v);
                    var feet = me.transform.position;
                    float dDoor = Vector3.Distance(feet, v.DoorPosition(0));
                    bool enterable = Vehicle.NearestEnterable(feet, VehicleRules.Current.EnterRange, out _) == v;
                    bool prompt = Hud.Instance.PromptText.Contains("탑승");
                    Debug.Log($"[#190] {tag}: door {dDoor:F2} m, enterable {enterable}, prompt '{Hud.Instance.PromptText}'");
                    string problem = null;
                    if (!enterable) problem = $"not enterable at the door (door {dDoor:F2} m)";
                    else if (!prompt) problem = "no enter prompt on the HUD";
                    string unreachable = Unreachable(v);
                    if (unreachable != null) problem = (problem != null ? problem + "; " : "") + unreachable;
                    if (enterable)
                    {
                        for (int tries = 0; tries < 3 && !VehicleSeat.IsAttached(me); tries++)
                        {
                            keys.Frame.Interact = true;
                            walker.Interact = true;
                            float wait = Time.realtimeSinceStartup + 1.2f;
                            while (!VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < wait) yield return null;
                        }
                        if (!VehicleSeat.IsAttached(me)) problem = (problem != null ? problem + "; " : "") + "F did not get in";
                    }
                    if (VehicleSeat.IsAttached(me))
                    {
                        yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.5f);
                        VehicleSeat.TryGet(me, out var on, out int seat);
                        if (on != v || seat != 0) problem = (problem != null ? problem + "; " : "") + $"F boarded {(on != null ? on.name : "nothing")} seat {seat}";
                        keys.Frame.Interact = true;
                        yield return WaitUntil(() => !VehicleSeat.IsAttached(me), 6f, "got out of " + tag);
                        yield return new WaitForSeconds(0.5f);
                        if (Vector3.Distance(me.transform.position, v.transform.position) > 6f) problem = (problem != null ? problem + "; " : "") + "exit landed " + Vector3.Distance(me.transform.position, v.transform.position).ToString("F1") + " m away";
                    }
                    if (problem != null) failures.Add($"{tag}: {problem}");
                    else break; // this type works on this one
                    if (problem != null && v == candidates[Mathf.Min(2, candidates.Count - 1)]) break;
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // Review shot of the prompt beside the sports car. Not part of the suite:
        //   ENTER_SHOTS=<folder> tools/unity.sh run-gfx Game.Tests.VehicleEnterExitTests.CaptureEnterPrompt
        [UnityTest, Explicit, Category("Capture"), Timeout(300000)]
        public IEnumerator CaptureEnterPrompt()
        {
            string folder = Environment.GetEnvironmentVariable("ENTER_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/entershots";
            Directory.CreateDirectory(folder);
            var walker = new Walker();
            var keys = new Keys();
            yield return LoadWorld(walker, keys);
            var v = Vehicle.All.First(x => x != null && TypeOf(x) == "GoblinV12");
            yield return WalkToDoor(walker, v);
            PlayerController.Local.LookAt(v.transform.position + Vector3.up * 0.5f);
            yield return null;
            yield return null;
            Debug.Log("[EnterShot] prompt: '" + Hud.Instance.PromptText + "'");
            yield return ResultShot(folder, "enter_prompt_GoblinV12", 1920, 1080);
        }

        static IEnumerator ResultShot(string folder, string name, int w, int h)
        {
            yield return null; // not WaitForEndOfFrame: it stalls in a batch mode editor
            var cam = PlayerController.Local.Camera != null ? PlayerController.Local.Camera : Camera.main;
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;
                if (canvases[i].isRootCanvas && modes[i] == RenderMode.ScreenSpaceOverlay)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = cam;
                    canvases[i].planeDistance = cam.nearClipPlane + 0.02f;
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
    }
}

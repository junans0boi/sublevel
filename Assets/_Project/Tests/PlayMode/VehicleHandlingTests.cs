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
    // How the car drives (준환: steering alone spins it, it spins entering a slope, a Space drift slides on into the wall):
    // the TestBox car on a big flat pad (and a 12° ramp) built beside the test box, as a host. Silent.
    public class VehicleHandlingTests
    {
        static readonly Vector3 Pad = new Vector3(600f, 0f, 0f);

        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };
            public VehicleInputFrame Read() { var f = Frame; Frame.Interact = Frame.EngineToggle = Frame.ToggleView = false; Frame.Seat = -1; return f; }
        }

        Keys _keys;
        GameObject _ground;

        static IEnumerator WaitUntil(Func<bool> c, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!c()) { if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what); yield return null; }
        }

        IEnumerator Load()
        {
            _keys = new Keys();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = _keys;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player");
            yield return WaitUntil(() => Car() != null, 10f, "car");
            AudioListener.volume = 0f;
            _ground = new GameObject("HandlingPad");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(_ground.transform, false);
            floor.transform.position = Pad + Vector3.down * 0.5f;
            floor.transform.localScale = new Vector3(400f, 1f, 400f);
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null; VehicleInput.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null; yield return null;
        }

        public static readonly string[] Models = { "FairheavenLT80", "FairheavenSW84", "LightbodyLifted85", "Shvan92", "GoblinV12" };
        static string _model = "FairheavenLT80";
        static Vehicle Car()
        {
            foreach (var v in Vehicle.All) if (v != null && v.Kind == VehicleKind.Car && v.name.StartsWith(_model)) return v;
            return null;
        }

        // Real terrain (a TerrainCollider, as in the World): flat, then a 12° climb, with 4 cm bumps all over.
        void TerrainSlope(float startX, float deg, float blend = 0f)
        {
            var data = new TerrainData { heightmapResolution = 257 };
            const float size = 200f, height = 60f;
            data.size = new Vector3(size, height, size);
            int n = data.heightmapResolution;
            var h = new float[n, n];
            var rng = new System.Random(7);
            float slope = Mathf.Tan(deg * Mathf.Deg2Rad);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float wx = x / (float)(n - 1) * size - size / 2f; // pad-space x relative to the terrain centre
                float d = wx - startX;
                // `blend` m of rounded crest (a parabola) instead of a sharp crease where the climb starts.
                float rise = blend > 0f ? (d <= 0f ? 0f : d < blend ? d * d / (2f * blend) : d - blend / 2f) : Mathf.Max(0f, d);
                float y = 5f + rise * slope + (float)rng.NextDouble() * 0.04f;
                h[z, x] = y / height;
            }
            data.SetHeights(0, 0, h);
            var go = Terrain.CreateTerrainGameObject(data);
            go.transform.SetParent(_ground.transform, false);
            go.transform.position = Pad + new Vector3(-size / 2f, -5f, -size / 2f);
        }
        static int SeatOf(Combatant c) => VehicleSeat.TryGet(c, out _, out int s) ? s : -1;

        IEnumerator Board(Vehicle v)
        {
            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null; yield return null;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => SeatOf(me) == 0, 3f, "driver's seat");
            yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.3f);
            me.ServerSetHolstered(true);
        }

        // On the pad at `local` (pad space), facing +X, rolling at `kmh` with the engine on and nothing pressed.
        IEnumerator Roll(Vehicle v, Vector3 local, float kmh)
        {
            v.Body.position = Pad + local + Vector3.up * 0.3f;
            v.Body.rotation = Quaternion.Euler(0f, 90f, 0f);
            v.Body.angularVelocity = Vector3.zero;
            v.Body.linearVelocity = Vector3.zero;
            _keys.Frame = new VehicleInputFrame { Seat = -1 };
            yield return new WaitForSeconds(0.4f);
            v.Body.linearVelocity = v.transform.forward * (kmh / 3.6f);
            yield return new WaitForFixedUpdate();
        }

        // A ramp rising along +X at `deg`, its top surface meeting the pad at pad-space x = `startX`.
        void Ramp(float startX, float deg, float length = 30f, float width = 12f)
        {
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Ramp";
            ramp.transform.SetParent(_ground.transform, false);
            const float thick = 1f;
            var rot = Quaternion.Euler(0f, 0f, deg);
            var edge = Pad + new Vector3(startX, 0f, 0f);
            ramp.transform.SetPositionAndRotation(edge - rot * new Vector3(-length / 2f, thick / 2f, 0f), rot);
            ramp.transform.localScale = new Vector3(length, thick, width);
        }

        // The body's own collisions (not the wheels: they are casts) with anything, summed.
        sealed class BodyHits : MonoBehaviour
        {
            public int Count; public float Impulse;
            void OnCollisionEnter(Collision c) { Count++; Impulse += c.impulse.magnitude; }
            void OnCollisionStay(Collision c) { Impulse += c.impulse.magnitude; }
        }

        public struct Track
        {
            public float PeakSlipDeg, PeakYawDeg, BodyTurnDeg, PathTurnDeg, StartKmh, EndKmh, EndSlipDeg;
            public override string ToString() =>
                $"peak slip {PeakSlipDeg:F1} deg, end slip {EndSlipDeg:F1} deg, peak yaw {PeakYawDeg:F0} deg/s, body turned {BodyTurnDeg:F0} deg, path turned {PathTurnDeg:F0} deg, {StartKmh:F0} -> {EndKmh:F0} km/h";
        }

        // Plays `steps` (input, seconds) in a row and tracks the car the whole time.
        static IEnumerator Drive(Vehicle v, Keys keys, (VehicleInputFrame press, float seconds)[] steps, Action<Track> done)
        {
            var t = new Track();
            Vector3 Flat(Vector3 d) => Vector3.ProjectOnPlane(d, Vector3.up);
            var f0 = Flat(v.transform.forward);
            var p0 = Flat(v.Body.linearVelocity);
            t.StartKmh = v.Body.linearVelocity.magnitude * 3.6f;
            float body = 0f, path = 0f;
            var lastF = f0; var lastV = p0;
            foreach (var (press, seconds) in steps)
            {
                keys.Frame = press;
                for (float s = 0f; s < seconds; s += Time.fixedDeltaTime)
                {
                    yield return new WaitForFixedUpdate();
                    var f = Flat(v.transform.forward); var vel = Flat(v.Body.linearVelocity);
                    body += Vector3.SignedAngle(lastF, f, Vector3.up); lastF = f;
                    if (vel.magnitude > 1.5f && lastV.magnitude > 1.5f) path += Vector3.SignedAngle(lastV, vel, Vector3.up);
                    lastV = vel;
                    if (vel.magnitude > 2f) t.PeakSlipDeg = Mathf.Max(t.PeakSlipDeg, Mathf.Abs(Vector3.SignedAngle(f, vel, Vector3.up)));
                    t.PeakYawDeg = Mathf.Max(t.PeakYawDeg, Mathf.Abs(v.Body.angularVelocity.y * Mathf.Rad2Deg));
                }
            }
            var endV = Flat(v.Body.linearVelocity);
            t.EndSlipDeg = endV.magnitude > 2f ? Mathf.Abs(Vector3.SignedAngle(Flat(v.transform.forward), endV, Vector3.up)) : 0f;
            t.BodyTurnDeg = Mathf.Abs(body); t.PathTurnDeg = Mathf.Abs(path);
            t.EndKmh = v.Body.linearVelocity.magnitude * 3.6f;
            keys.Frame = new VehicleInputFrame { Seat = -1 };
            done(t);
        }

        static VehicleInputFrame Press(float throttle = 0f, float steer = 0f, bool handbrake = false) =>
            new VehicleInputFrame { Seat = -1, Throttle = throttle, Steer = steer, Handbrake = handbrake };

        // Full steer at speed with the throttle on: the car turns on its tyres, it does not swap ends.
        [UnityTest]
        public IEnumerator FullSteer_AtSpeed_TurnsWithoutSpinning([ValueSource(nameof(Models))] string model)
        {
            _model = model;
            yield return Load();
            var v = Car();
            yield return Board(v);
            foreach (float kmh in new[] { 50f, 75f })
            {
                yield return Roll(v, new Vector3(-150f, 0f, 0f), kmh);
                Track t = default;
                yield return Drive(v, _keys, new[] { (Press(1f, 1f), 2.5f) }, x => t = x);
                Debug.Log($"[handling] {model} full steer + throttle from {kmh} km/h: {t}");
                Assert.Less(t.PeakSlipDeg, 15f, $"from {kmh} km/h the tyres hold: {t}");
                Assert.Greater(t.PathTurnDeg, 45f, $"from {kmh} km/h it turns: {t}");
            }
        }

        // Near the top speed (sports car over 200 km/h) full and half lock: the tyres give up gently, the car does not spin.
        [UnityTest]
        public IEnumerator HighSpeed_Steer_DoesNotSpin([ValueSource(nameof(Models))] string model)
        {
            _model = model;
            yield return Load();
            var v = Car();
            yield return Board(v);
            float kmh = v.Rules.MaxSpeedKmh * 0.9f;
            foreach (float steer in new[] { 0.5f, 1f })
            {
                yield return Roll(v, new Vector3(-180f, 0f, 0f), kmh);
                Track t = default;
                yield return Drive(v, _keys, new[] { (Press(1f, steer), 1.5f) }, x => t = x);
                Debug.Log($"[handling] {model} steer {steer} from {kmh:F0} km/h: {t}");
                Assert.Less(t.PeakSlipDeg, 15f, $"no slide at {kmh:F0} km/h: {t}");
                Assert.Less(Mathf.Abs(t.BodyTurnDeg - t.PathTurnDeg), 10f, $"no spin at {kmh:F0} km/h: {t}");
            }
        }

        // 0 -> 100 km/h at full throttle through the gearbox: every car gets there, the sports car first, the van last.
        [UnityTest]
        public IEnumerator ZeroToHundred_ThroughTheGears([ValueSource(nameof(Models))] string model)
        {
            _model = model;
            yield return Load();
            var v = Car();
            yield return Board(v);
            yield return Roll(v, new Vector3(-180f, 0f, 0f), 0f);
            _keys.Frame = Press(1f);
            float t = 0f; int topGear = 0, shifts = v.ShiftCount;
            while (v.Body.linearVelocity.magnitude * 3.6f < 100f && t < 20f)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                topGear = Mathf.Max(topGear, v.Gear);
            }
            _keys.Frame = new VehicleInputFrame { Seat = -1 };
            Debug.Log($"[handling] {model} 0-100 km/h {t:F2} s, gear {topGear} at 100, {v.ShiftCount - shifts} shifts, {v.EngineRpm:F0} rpm (redline {v.RedlineRpm:F0})");
            Assert.Less(t, 12f, "reaches 100 km/h");
            Assert.GreaterOrEqual(topGear, 2, "shifted up on the way");
            Assert.LessOrEqual(v.EngineRpm, v.RedlineRpm + 1f);
        }

        // Renders the player's camera with every overlay canvas on it into a 1920x1080 PNG (batch mode has no screen).
        static void Snap(string path)
        {
            var cam = PlayerController.Local.Camera;
            var rt = new RenderTexture(1920, 1080, 24);
            var moved = new System.Collections.Generic.List<Canvas>();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || !c.enabled || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f;
                moved.Add(c);
            }
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = before;
            foreach (var c in moved) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
            UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
        }

        // Screenshots of the instrument cluster (ADR-558) under way: Logs/cluster/*.png. Tuning aid, judged by eye.
        [UnityTest, Explicit("capture")]
        public IEnumerator CaptureCluster()
        {
            _model = "GoblinV12";
            yield return Load();
            var v = Car();
            yield return Board(v);
            System.IO.Directory.CreateDirectory("Logs/cluster");
            yield return Roll(v, new Vector3(-180f, 0f, 0f), 0f);
            _keys.Frame = Press(1f);
            float start = Time.time;
            foreach (float at in new[] { 0.6f, 1.6f, 3.2f, 5.5f })
            {
                while (Time.time - start < at) yield return null;
                yield return null;
                Snap($"Logs/cluster/third_{at:F1}s_{v.SpeedKmh:F0}kmh_g{v.Gear}.png");
            }
            _keys.Frame = new VehicleInputFrame { Seat = -1, Throttle = 1f, ToggleView = true };
            yield return new WaitForSeconds(0.8f);
            Snap($"Logs/cluster/first_{v.SpeedKmh:F0}kmh_g{v.Gear}.png");
            _keys.Frame = new VehicleInputFrame { Seat = -1 };
        }

        // Driving onto a 12° slope (straight, and with a little steer) at speed: the car climbs, it does not turn round.
        [UnityTest]
        public IEnumerator SlopeEntry_KeepsHeading([ValueSource(nameof(Models))] string model, [Values(0f, 0.3f)] float steer, [Values(0f, 8f)] float blend)
        {
            _model = model;
            yield return Load();
            var v = Car();
            yield return Board(v);
            TerrainSlope(-40f, 12f, blend);
            var hits = v.gameObject.AddComponent<BodyHits>();
            yield return Roll(v, new Vector3(-60f, 0.1f, 0f), 50f);
            Track t = default;
            yield return Drive(v, _keys, new[] { (Press(1f, steer), 3.5f) }, x => t = x);
            Debug.Log($"[handling] {model} onto a 12° terrain slope (crest {blend} m) at 50 km/h, steer {steer}: {t} | body contacts {hits.Count}, impulse {hits.Impulse:F0} N s");
            // A spin is the body turning away from its path; turning with the steering (path and body together) is fine.
            Assert.Less(t.PeakSlipDeg, 12f, $"no slide on the slope: {t}");
            Assert.Less(Mathf.Abs(t.BodyTurnDeg - t.PathTurnDeg), 10f, $"no spin on the slope: {t}");
            if (steer == 0f) Assert.Less(t.BodyTurnDeg, 15f, $"straight on stays straight: {t}");
        }

        // Space tapped into a corner, then throttle and steer: the tail comes round and the car leaves the corner turned
        // further than steering alone, still carrying speed, and straightens up again (no spin, no plough into the wall).
        [UnityTest]
        public IEnumerator HandbrakeTap_TurnsTighter_KeepsSpeed_AndRecovers([ValueSource(nameof(Models))] string model)
        {
            _model = model;
            yield return Load();
            var v = Car();
            yield return Board(v);
            Track plain = default, drift = default;
            yield return Roll(v, new Vector3(-150f, 0f, -60f), 60f);
            yield return Drive(v, _keys, new[] { (Press(0f, 1f), 0.5f), (Press(1f, 1f), 1.2f) }, x => plain = x);
            Debug.Log($"[handling] {model} steer only from 60 km/h: {plain}");
            yield return Roll(v, new Vector3(-150f, 0f, 60f), 60f);
            yield return Drive(v, _keys, new[] { (Press(0f, 1f, true), 0.5f), (Press(1f, 0.4f), 1.2f), (Press(1f, 0f), 1.0f) }, x => drift = x);
            Debug.Log($"[handling] {model} handbrake tap then throttle from 60 km/h: {drift}");
            Assert.Greater(drift.PeakSlipDeg, 20f, $"the tail comes round: {drift}");
            Assert.Greater(drift.PathTurnDeg, plain.PathTurnDeg, $"the drift turns the car's path further than steering alone ({plain}): {drift}");
            Assert.Greater(drift.EndKmh, drift.StartKmh * 0.55f, $"the drift keeps speed: {drift}");
            Assert.Less(drift.EndSlipDeg, 12f, $"it straightens up again: {drift}");
            Assert.Less(drift.BodyTurnDeg, 200f, $"no spin: {drift}");
        }
    }
}

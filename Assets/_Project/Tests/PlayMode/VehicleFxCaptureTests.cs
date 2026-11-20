using System;
using System.Collections;
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
    // Review shots of the vehicle lamps, tyre effects and damage in the World (#202, #203, #90, #99, ADR-155). Run on purpose with
    //   VEHFX_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.VehicleFxCaptureTests
    [Explicit, Category("Capture")]
    public class VehicleFxCaptureTests
    {
        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };

            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = Frame.Jerrycan = Frame.Lights = false;
                Frame.Seat = -1;
                return f;
            }
        }

        // A camera that renders one frame to a png from a given place, after every other script has moved.
        [DefaultExecutionOrder(30000)]
        sealed class LateShot : MonoBehaviour
        {
            public string Path;
            public Camera Cam;
            public Transform Follow;
            public Vector3 Offset;       // in the followed transform's space
            public Vector3 LookAtOffset;

            void LateUpdate()
            {
                if (Follow != null)
                {
                    Cam.transform.position = Follow.TransformPoint(Offset);
                    Cam.transform.rotation = Quaternion.LookRotation(Follow.TransformPoint(LookAtOffset) - Cam.transform.position);
                }
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

        string _folder;
        Camera _cam;
        LateShot _shot;
        Keys _keys;
        EnvironmentView _env;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            EnvironmentView.ForceTime = null;
            EnvironmentView.ForceWeather = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator Setup(DayTimeChoice time)
        {
            _folder = Environment.GetEnvironmentVariable("VEHFX_SHOTS");
            if (string.IsNullOrEmpty(_folder)) _folder = "Logs/vehfx";
            _folder = Path.GetFullPath(_folder);
            Directory.CreateDirectory(_folder);
            _keys = new Keys();
            PlayerController.InputOverride = new Idle();
            VehicleInput.InputOverride = _keys;
            EnvironmentView.ForceTime = time;
            EnvironmentView.ForceWeather = WeatherChoice.Clear;
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || Vehicle.All.Count(v => v != null) == 0)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player / vehicles");
                yield return null;
            }
            for (int n = 0; SpawnProtection.InputBlocked && n < 160; n++) yield return new WaitForSeconds(0.25f);
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            _env = EnvironmentView.Current;
            var go = new GameObject("VehFxShotCamera");
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;
            if (PlayerController.Local.Camera != null) _cam.CopyFrom(PlayerController.Local.Camera);
            _cam.fieldOfView = 55f;
            _cam.nearClipPlane = 0.1f;
            _shot = go.AddComponent<LateShot>();
            _shot.Cam = _cam;
            yield return new WaitForSeconds(1.5f);
        }

        IEnumerator Take(string name)
        {
            _shot.Path = Path.Combine(_folder, name + ".png");
            for (int i = 0; i < 4 && _shot.Path != null; i++) yield return null;
        }

        // Open ground with the given surface, 45 m clear ahead; null when none found near the world's first elevator.
        static bool FindSpot(Func<FootSurface, bool> wanted, out Vector3 spot, out float yaw)
        {
            spot = Vector3.zero;
            yaw = 0f;
            var world = WorldInfo.Current;
            var centre = world.Elevators[0].Position;
            for (float radius = 8f; radius <= 300f; radius += 5f)
                for (int a = 0; a < 48; a++)
                {
                    float ang = a * 7.5f * Mathf.Deg2Rad;
                    var p = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
                    if (!Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (!(hit.collider is TerrainCollider) && !wanted(FootSurface.Concrete)) continue;
                    if (hit.normal.y < 0.97f || hit.point.y < world.SeaLevel + 1f) continue;
                    if (!wanted(CombatantAudio.SurfaceAt(hit.point))) continue;
                    for (int d = 0; d < 8; d++)
                    {
                        var dir = Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;
                        var o = hit.point + Vector3.up * 1.0f;
                        if (Physics.SphereCast(o, 1.3f, dir, out _, 48f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                        // The surface stays the wanted one for the run.
                        bool all = true;
                        for (float s = 8f; s <= 40f && all; s += 8f)
                        {
                            var q = hit.point + dir * s;
                            if (Physics.Raycast(q + Vector3.up * 50f, Vector3.down, out var h2, 120f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                                all = wanted(CombatantAudio.SurfaceAt(h2.point)) && Mathf.Abs(h2.point.y - hit.point.y) < 0.7f && h2.normal.y > 0.97f;
                            else all = false;
                        }
                        if (!all) continue;
                        spot = hit.point;
                        yaw = Quaternion.LookRotation(dir).eulerAngles.y;
                        return true;
                    }
                }
            return false;
        }

        static void Place(Vehicle v, Vector3 pos, float yaw)
        {
            v.Body.position = pos + Vector3.up * 0.4f;
            v.Body.rotation = Quaternion.Euler(0f, yaw, 0f);
            v.Body.linearVelocity = v.Body.angularVelocity = Vector3.zero;
            v.transform.SetPositionAndRotation(v.Body.position, v.Body.rotation);
            var fuel = v.GetComponent<VehicleFuel>();
            if (fuel != null) fuel.ServerSetFuel(1f);
            v.ServerSetEngine(true);
        }

        static Vehicle Pick(bool bike, int skip = 0) =>
            Vehicle.All.Where(v => v != null && v.IsMotorcycle == bike && v.HasFreeSeat).OrderBy(v => v.name).Skip(skip).First();

        IEnumerator Board(Vehicle v)
        {
            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            for (int tries = 0; tries < 6 && !VehicleSeat.IsAttached(me); tries++)
            {
                _keys.Frame.Interact = true;
                float wait = Time.realtimeSinceStartup + 1.2f;
                while (!VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < wait) yield return null;
            }
            Assert.IsTrue(VehicleSeat.IsAttached(me), "boarded " + v.name);
            yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.5f);
        }

        IEnumerator Leave()
        {
            _keys.Frame.Throttle = 0f;
            _keys.Frame.Interact = true;
            float end = Time.realtimeSinceStartup + 5f;
            while (VehicleSeat.IsAttached(Combatant.LocalPlayer) && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(0.3f);
        }

        // ---- Lamps ----

        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureLamps()
        {
            yield return Setup(DayTimeChoice.Day);
            Assert.IsTrue(FindSpot(s => true, out var spot, out float yaw), "an open spot");
            var car = Pick(false);
            var bike = Pick(true);
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Place(car, spot, yaw);
            Place(bike, spot + right * 6f, yaw);
            PlayerController.Local.Teleport(spot - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 10f + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.5f);
            var lights = car.GetComponent<VehicleLights>();
            var bikeLights = bike.GetComponent<VehicleLights>();
            Assert.IsNotNull(lights, "the car has lamps");
            yield return new WaitForSeconds(0.5f);

            IEnumerator Around(string tag)
            {
                _shot.Follow = null;
                var p = car.transform;
                _cam.transform.position = p.TransformPoint(new Vector3(-3.2f, 1.3f, 5.8f));
                _cam.transform.rotation = Quaternion.LookRotation(p.TransformPoint(new Vector3(0f, 0.7f, 0f)) - _cam.transform.position);
                yield return Take($"lamps_{tag}_car_front");
                _cam.transform.position = p.TransformPoint(new Vector3(2.8f, 1.3f, -5.4f));
                _cam.transform.rotation = Quaternion.LookRotation(p.TransformPoint(new Vector3(0f, 0.7f, 0f)) - _cam.transform.position);
                yield return Take($"lamps_{tag}_car_rear");
                var b = bike.transform;
                _cam.transform.position = b.TransformPoint(new Vector3(-1.4f, 1.0f, 3.0f));
                _cam.transform.rotation = Quaternion.LookRotation(b.TransformPoint(new Vector3(0f, 0.6f, 0f)) - _cam.transform.position);
                yield return Take($"lamps_{tag}_bike_front");
                _cam.transform.position = b.TransformPoint(new Vector3(1.4f, 1.0f, -3.0f));
                _cam.transform.rotation = Quaternion.LookRotation(b.TransformPoint(new Vector3(0f, 0.6f, 0f)) - _cam.transform.position);
                yield return Take($"lamps_{tag}_bike_rear");
                // The road ahead from behind the car (the beam on the ground).
                _cam.transform.position = p.TransformPoint(new Vector3(0f, 2.2f, -7f));
                _cam.transform.rotation = Quaternion.LookRotation(p.TransformPoint(new Vector3(0f, 0.2f, 18f)) - _cam.transform.position);
                yield return Take($"lamps_{tag}_car_beam");
                Debug.Log($"[VehFx] {tag}: car lit {lights.Lit} lamps {lights.LampCount} spots {lights.SpotsOn}/{lights.SpotCount}; bike lit {bikeLights.Lit} lamps {bikeLights.LampCount} spots {bikeLights.SpotsOn}");
            }

            _env.Set(WorldSky.DayHour, WeatherKind.Clear);
            yield return new WaitForSeconds(1f);
            yield return Around("1day");
            _env.Set(WorldSky.DuskHour, WeatherKind.Clear);
            yield return new WaitForSeconds(1f);
            yield return Around("2dusk");
            _env.Set(WorldSky.NightHour, WeatherKind.Clear);
            yield return new WaitForSeconds(1f);
            yield return Around("3night");
            Assert.IsTrue(lights.Lit, "auto lamps lit at night");
            Assert.AreEqual(2, lights.SpotsOn, "car spots");
            // Frame cost: every parked vehicle of the World started at night (lamps and cones everywhere, real lights on the nearest 3 only).
            float msOff = 0f, msOn = 0f;
            var others = Vehicle.All.Where(v => v != null && v != car && v != bike).ToList();
            foreach (var v in others) v.ServerSetEngine(false);
            yield return new WaitForSeconds(1f);
            for (float t = 0f, n = 0; t < 3f; n++) { msOff += Time.unscaledDeltaTime * 1000f; t += Time.unscaledDeltaTime; if (t >= 3f) msOff /= (n + 1); yield return null; }
            foreach (var v in others) { var f = v.GetComponent<VehicleFuel>(); if (f != null) f.ServerSetFuel(1f); v.ServerSetEngine(true); }
            yield return new WaitForSeconds(1f);
            for (float t = 0f, n = 0; t < 3f; n++) { msOn += Time.unscaledDeltaTime * 1000f; t += Time.unscaledDeltaTime; if (t >= 3f) msOn /= (n + 1); yield return null; }
            Debug.Log($"[VehFx] frame ms, engines off {msOff:F1} vs {others.Count + 2} vehicles lit {msOn:F1}; vehicles with real lights {VehicleLights.SpotVehicles}");
            Assert.LessOrEqual(VehicleLights.SpotVehicles, VehicleLights.MaxSpotVehicles, "real lights stay within the budget");
            // Fog by day.
            _env.Set(WorldSky.DayHour, WeatherKind.Fog);
            yield return new WaitForSeconds(1f);
            yield return Take("lamps_4dayfog_car_front");
            Debug.Log($"[VehFx] fog by day: lit {lights.Lit}");

            // Driving at night: brake lamps, reverse lamps, indicators.
            _env.Set(WorldSky.NightHour, WeatherKind.Clear);
            yield return Board(car);
            _shot.Follow = car.transform;
            _shot.Offset = new Vector3(-2.2f, 1.5f, -6.2f);
            _shot.LookAtOffset = new Vector3(0f, 0.8f, 2f);
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(3.5f);
            _keys.Frame.Throttle = -1f;
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(lights.Braking, "brake lamps while braking");
            yield return Take("lamps_5night_brake");
            yield return new WaitForSeconds(2.2f);
            _keys.Frame.Throttle = -1f;
            yield return new WaitForSeconds(1.8f);
            Assert.IsTrue(lights.Reversing, "reverse lamps while reversing");
            yield return Take("lamps_6night_reverse");
            _keys.Frame.Throttle = 0.6f;
            _keys.Frame.Steer = 1f;
            yield return new WaitForSeconds(2.2f);
            Debug.Log($"[VehFx] indicator {lights.Indicator}");
            yield return Take("lamps_7night_indicator");
            _keys.Frame.Steer = 0f;
            // L toggles: off by hand.
            _keys.Frame.Lights = true;
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(lights.Lit, "L turns the lamps off");
            Assert.AreEqual(VehicleFxRules.LightsOff, lights.Mode);
            yield return Take("lamps_8night_forced_off");
            _keys.Frame.Lights = true;
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(lights.Lit, "L again: back to auto (on at night)");
            _keys.Frame.Throttle = 0f;
            yield return Leave();
        }

        // ---- Tyres ----

        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureTyreFx()
        {
            yield return Setup(DayTimeChoice.Day);
            _env.Set(WorldSky.DayHour, WeatherKind.Clear);
            var car = Pick(false);
            var bike = Pick(true);
            bool dirt = FindSpot(s => s == FootSurface.Dirt, out var dirtSpot, out float dirtYaw);
            bool grass = FindSpot(s => s == FootSurface.Grass, out var grassSpot, out float grassYaw);
            bool paved = FindSpot(s => s == FootSurface.Concrete, out var pavedSpot, out float pavedYaw);
            Debug.Log($"[VehFx] spots: dirt {dirt} grass {grass} paved {paved}");

            IEnumerator Run(string tag, Vehicle v, Vector3 spot, float yaw, bool drift)
            {
                Place(v, spot, yaw);
                yield return new WaitForSeconds(1.5f);
                yield return Board(v);
                var fx = v.GetComponent<VehicleTyreFx>();
                _shot.Follow = v.transform;
                _shot.Offset = new Vector3(v.IsMotorcycle ? -1.6f : -2.6f, 1.2f, -5.2f);
                _shot.LookAtOffset = new Vector3(0f, 0.5f, -0.5f);
                _keys.Frame.Throttle = 1f;
                _keys.Frame.Boost = false;
                yield return new WaitForSeconds(2.4f);
                Debug.Log($"[VehFx] {tag}: speed {v.SpeedKmh:F0} km/h ground {fx.Ground} emitted {fx.Emitted} slip {fx.Slip01:F2}");
                yield return Take($"tyre_{tag}_drive");
                if (drift)
                {
                    _keys.Frame.Steer = 1f;
                    _keys.Frame.Handbrake = true;
                    yield return new WaitForSeconds(0.9f);
                    Debug.Log($"[VehFx] {tag} drift: slip {fx.Slip01:F2} skidding {fx.Skidding} emitted {fx.Emitted}");
                    yield return Take($"tyre_{tag}_drift");
                    yield return new WaitForSeconds(0.6f);
                    yield return Take($"tyre_{tag}_drift2");
                    _keys.Frame.Steer = 0f;
                    _keys.Frame.Handbrake = false;
                }
                _keys.Frame.Throttle = -1f;
                yield return new WaitForSeconds(1.2f);
                Debug.Log($"[VehFx] {tag} brake: slip {fx.Slip01:F2} skidding {fx.Skidding}");
                yield return Take($"tyre_{tag}_brake");
                yield return new WaitForSeconds(2.5f);
                _keys.Frame.Throttle = 0f;
                // The marks left behind, seen from above-behind once stopped.
                _shot.Follow = null;
                var p = v.transform;
                _cam.transform.position = p.position + Vector3.up * 11f - p.forward * 4f;
                _cam.transform.rotation = Quaternion.LookRotation(p.position - p.forward * 12f - _cam.transform.position);
                yield return Take($"tyre_{tag}_marks");
                yield return Leave();
            }

            if (dirt) yield return Run("1car_dirt", car, dirtSpot, dirtYaw, true);
            if (grass && Environment.GetEnvironmentVariable("VEHFX_GRASS") != null) yield return Run("2car_grass", Pick(false, 1), grassSpot, grassYaw, false);
            if (paved) yield return Run("3car_paved", Pick(false, 2), pavedSpot, pavedYaw, true);
            if (dirt) yield return Run("4bike_dirt", bike, dirtSpot + Quaternion.Euler(0f, dirtYaw + 90f, 0f) * Vector3.forward * 6f, dirtYaw, false);
            Assert.IsTrue(dirt || grass || paved, "at least one test ground");
        }

        // ---- Damage and the cabin ----

        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureDamageAndCabin()
        {
            yield return Setup(DayTimeChoice.Day);
            _env.Set(WorldSky.DayHour, WeatherKind.Clear);
            Assert.IsTrue(FindSpot(s => true, out var spot, out float yaw), "an open spot");
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

            // Dents at the game camera's distance: a blast beside the car's flank, a blast at the nose; then bullets.
            var car = Pick(false);
            Place(car, spot, yaw);
            PlayerController.Local.Teleport(spot - fwd * 14f + Vector3.up * 0.2f);
            yield return new WaitForSeconds(1.5f);
            var damage = car.GetComponent<VehicleDamage>();
            var visual = car.GetComponent<VehicleDamageVisual>();
            void View(float dist, float side, float up)
            {
                var p = car.transform;
                _shot.Follow = null;
                _cam.transform.position = p.TransformPoint(new Vector3(side, up, dist));
                _cam.transform.rotation = Quaternion.LookRotation(p.TransformPoint(new Vector3(0f, 0.7f, 0f)) - _cam.transform.position);
            }
            View(7f, -6f, 2.2f); yield return Take("dmg_1_car_clean_7m");
            // Flank (right side), 1.6 m out from the body.
            var side = car.transform.TransformPoint(new Vector3(1.0f + 1.6f, 0.6f, 0.2f));
            damage.ServerBlast(side, d => 0f, 0f);
            side = car.transform.TransformPoint(new Vector3(-(1.0f + 1.6f), 0.6f, -1.0f));
            damage.ServerBlast(side, d => 0f, 0f);
            var nose = car.transform.TransformPoint(new Vector3(0.4f, 0.6f, 2.6f + 1.6f));
            damage.ServerBlast(nose, d => 0f, 0f);
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[VehFx] dents shown {visual.DentsShown}, smudges {visual.SmudgesShown}");
            View(7f, -6f, 2.2f); yield return Take("dmg_2_car_dented_7m_front");
            View(-7f, 6f, 2.2f); yield return Take("dmg_3_car_dented_7m_rear");
            _cam.transform.position = car.transform.TransformPoint(new Vector3(6.5f, 2.4f, 4f));
            _cam.transform.rotation = Quaternion.LookRotation(car.transform.TransformPoint(new Vector3(0f, 0.6f, 0f)) - _cam.transform.position);
            yield return Take("dmg_4_car_dented_flank_7m");
            View(14f, -8f, 3.5f); yield return Take("dmg_5_car_dented_14m");
            _cam.transform.position = car.transform.TransformPoint(new Vector3(4.2f, 1.2f, 0.4f));
            _cam.transform.rotation = Quaternion.LookRotation(car.transform.TransformPoint(new Vector3(1f, 0.6f, 0.2f)) - _cam.transform.position);
            yield return Take("dmg_5b_car_flank_close");
            _cam.transform.position = car.transform.TransformPoint(new Vector3(1.8f, 1.2f, 6.2f));
            _cam.transform.rotation = Quaternion.LookRotation(car.transform.TransformPoint(new Vector3(0.4f, 0.6f, 2.6f)) - _cam.transform.position);
            yield return Take("dmg_5c_car_nose_close");

            // Bullet holes on a motorcycle, from the side and the front.
            var bike = Pick(true);
            Place(bike, spot + fwd * 9f, yaw);
            yield return new WaitForSeconds(1.2f);
            var bd = bike.GetComponent<VehicleDamage>();
            int holes0 = bike.GetComponent<VehicleDamageVisual>().HolesShown;
            for (int i = 0; i < 7; i++)
            {
                var target = bike.transform.TransformPoint(new Vector3(0f, 0.5f + 0.08f * i, 0.5f - 0.18f * i));
                var from = target + bike.transform.right * (i % 2 == 0 ? 3f : -3f) + Vector3.up * 0.1f;
                var dir = (target - from).normalized;
                if (Physics.Raycast(from, dir, out var hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    bd.ServerShot(from, dir, 8f, hit.collider, hit.point, 30f);
            }
            yield return new WaitForSeconds(0.5f);
            int holes = bike.GetComponent<VehicleDamageVisual>().HolesShown - holes0;
            Debug.Log($"[VehFx] bike holes {holes}, dents {bike.GetComponent<VehicleDamageVisual>().DentsShown}");
            var b = bike.transform;
            _shot.Follow = null;
            _cam.transform.position = b.TransformPoint(new Vector3(2.4f, 1.0f, 0.8f));
            _cam.transform.rotation = Quaternion.LookRotation(b.TransformPoint(new Vector3(0f, 0.5f, 0.2f)) - _cam.transform.position);
            yield return Take("dmg_6_bike_holes_right");
            _cam.transform.position = b.TransformPoint(new Vector3(-2.2f, 1.0f, 0.8f));
            _cam.transform.rotation = Quaternion.LookRotation(b.TransformPoint(new Vector3(0f, 0.5f, 0.2f)) - _cam.transform.position);
            yield return Take("dmg_7_bike_holes_left");
            _cam.transform.position = b.TransformPoint(new Vector3(1.3f, 0.9f, 0.5f));
            _cam.transform.rotation = Quaternion.LookRotation(b.TransformPoint(new Vector3(0f, 0.5f, 0.2f)) - _cam.transform.position);
            yield return Take("dmg_8_bike_holes_close_right");
            _cam.transform.position = b.TransformPoint(new Vector3(-1.3f, 0.9f, 0.5f));
            _cam.transform.rotation = Quaternion.LookRotation(b.TransformPoint(new Vector3(0f, 0.5f, 0.2f)) - _cam.transform.position);
            yield return Take("dmg_9_bike_holes_close_left");
            Assert.Greater(holes, 0, "bullet holes appear on a motorcycle");

            // The cabin in first person, by day: each distinct car model, looking ahead and to each side.
            var models = Vehicle.All.Where(v => v != null && !v.IsMotorcycle && v.HasFreeSeat && v != car).GroupBy(v => v.name.Replace("(Clone)", "")).Select(g => g.First()).Take(5).ToList();
                        int n = 0;
            foreach (var v in models)
            {
                Place(v, spot - right * 8f, yaw);
                yield return new WaitForSeconds(1.2f);
                yield return Board(v);
                _keys.Frame.ToggleView = true;
                yield return new WaitForSeconds(0.6f);
                var pc = PlayerController.Local;
                string tag = v.name.Replace("(Clone)", "");
                IEnumerator CabinShot(string what, Vector3 dirLocal)
                {
                    pc.LookAt(pc.Camera.transform.position + v.transform.TransformDirection(dirLocal) * 5f);
                    yield return new WaitForSeconds(0.4f);
                    _shot.Follow = null;
                    _cam.fieldOfView = 75f;
                    _cam.transform.SetPositionAndRotation(pc.Camera.transform.position, pc.Camera.transform.rotation);
                    yield return Take($"cabin_{n}_{tag}_{what}");
                    _cam.transform.SetPositionAndRotation(pc.Camera.transform.position, pc.Camera.transform.rotation);
                }
                yield return CabinShot("ahead", new Vector3(0f, -0.15f, 1f));
                yield return CabinShot("right", new Vector3(1f, -0.25f, 0.35f));
                yield return CabinShot("left", new Vector3(-1f, -0.25f, 0.35f));
                _keys.Frame.ToggleView = true;
                yield return Leave();
                n++;
            }
        }

        // The cabin in first person, each distinct car model by day and by night (#99): shots only, judged by eye and by the lower-frame luminance.
        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureCabinDayNight()
        {
            VehicleCamera.CabinLift = true;
            yield return Setup(DayTimeChoice.Day);
            Assert.IsTrue(FindSpot(s => true, out var spot, out float yaw), "an open spot");
            var models = Vehicle.All.Where(v => v != null && !v.IsMotorcycle && v.HasFreeSeat).GroupBy(v => v.name.Replace("(Clone)", "")).Select(g => g.First()).ToList();
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            int at = 0;
            foreach (var v in models)
            {
                Place(v, spot + fwd * (8f * at++), yaw);   // each on its own ground: the earlier ones stay parked
                yield return new WaitForSeconds(1.2f);
                yield return Board(v);
                _keys.Frame.ToggleView = true;
                yield return new WaitForSeconds(0.6f);
                var pc = PlayerController.Local;
                string tag = v.name.Replace("(Clone)", "");
                foreach (var (time, hour) in new[] { ("day", WorldSky.DayHour), ("night", WorldSky.NightHour) })
                {
                    _env.Set(hour, WeatherKind.Clear);
                    yield return new WaitForSeconds(0.5f);
                    foreach (var (what, dir) in new[] { ("ahead", new Vector3(0f, -0.15f, 1f)), ("left", new Vector3(-1f, -0.25f, 0.35f)) })
                    {
                        pc.LookAt(pc.Camera.transform.position + v.transform.TransformDirection(dir) * 5f);
                        yield return new WaitForSeconds(0.4f);
                        _shot.Follow = null;
                        _cam.fieldOfView = VehicleCamera.DrivingFov;
                        _cam.transform.SetPositionAndRotation(pc.Camera.transform.position, pc.Camera.transform.rotation);
                        foreach (bool lift in new[] { false, true })
                        {
                            VehicleCamera.CabinLift = lift;
                            yield return null;
                            yield return Take($"cabin_{tag}_{time}_{what}_{(lift ? "after" : "before")}");
                        }
                    }
                    Debug.Log($"[Cabin] {time} ambientIntensity {RenderSettings.ambientIntensity:F2} probe L0 {RenderSettings.ambientProbe[0, 0]:F3}");
                }
                _env.Set(WorldSky.DayHour, WeatherKind.Clear);
                _keys.Frame.ToggleView = true;
                yield return Leave();
            }
        }
    }
}

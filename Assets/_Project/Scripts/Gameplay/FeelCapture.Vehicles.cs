using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // `-vehshots <folder>` (#89): every vehicle model of the scene from outside (front quarter and side), then sat in:
    // the driver in third person, from beside, in first person (wheel and hands, the cabin through the glass), the front
    // wheels turned while steering, the passenger / pillion from beside. In the World (no vehicles near the start) it
    // also looks at the parked and wrecked cars in the towns. Development aid like the other capture modes.
    public partial class FeelCapture
    {
        bool _vehShots;

        // Set when the vehicle shots are finished (the play mode capture test waits for it).
        public static bool VehicleShotsDone { get; private set; }

        // Starts the vehicle shots in the running scene (play mode capture test; a build uses -vehshots).
        public static FeelCapture StartVehicleShots(string folder)
        {
            VehicleShotsDone = false;
            var go = new GameObject("FeelCapture");
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = System.IO.Path.GetFullPath(folder);
            capture._vehShots = true;
            PlayerController.InputOverride = capture;
            VehicleInput.InputOverride = capture;
            return capture;
        }

        // In batch mode (the editor running the capture test; nothing reaches a screen) the player camera is rendered
        // into a texture instead of grabbing the screen.
        IEnumerator VShot(string name)
        {
            Debug.Log("[FeelCapture] shot " + name);
            if (!Application.isBatchMode)
            {
                yield return Shot(name);
                yield break;
            }
            yield return null; // not WaitForEndOfFrame: it can stall in a batch mode editor, and Camera.Render needs no frame end
            LateUpdate(); // the coroutine resumes before LateUpdate: place the outside camera now
            var pc = PlayerController.Local;
            var cam = pc != null && pc.Camera != null ? pc.Camera : Camera.main;
            if (cam == null) yield break;
            const int w = 1280, h = 720;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = before;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(_folder, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        static bool BootVehicles(string[] args)
        {
            int i = Array.IndexOf(args, "-vehshots");
            if (i < 0 || i + 1 >= args.Length) return false;
            // Captures keep going behind other windows (the player otherwise pauses when it loses focus).
            Application.runInBackground = true;
            var go = new GameObject("FeelCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = System.IO.Path.GetFullPath(args[i + 1]);
            capture._vehShots = true;
            PlayerController.InputOverride = capture;
            VehicleInput.InputOverride = capture;
            return true;
        }

        IEnumerator VehShots(Combatant me)
        {
            var pc = PlayerController.Local;
            Debug.Log("[FeelCapture] veh shots start, vehicles " + Vehicle.All.Count);
            me.ServerDamageFilter = _ => 0f;
            for (int n = 0; SpawnProtection.InputBlocked && n < 60; n++) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1f);
            var props = GameObject.Find("ParkedCars");
            if (props != null) yield return PropShots(props.transform);
            var all = Vehicle.All.Where(v => v != null).OrderBy(v => v.name).ToList();
            var home = me.transform.position;
            string only = Environment.GetEnvironmentVariable("VEH_ONLY"); // comma list of names (re-shoots)
            foreach (var v in all)
            {
                if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(Clean(v.name))) continue;
                // The World's vehicles are spread over the map: only the nearest of each model.
                if (props != null && all.Any(o => o != v && o.name == v.name && (o.transform.position - home).sqrMagnitude < (v.transform.position - home).sqrMagnitude)) continue;
                yield return OneVehicle(v, Clean(v.name));
                pc.Teleport(home);
                yield return new WaitForSeconds(0.3f);
            }
            VehicleShotsDone = true;
        }

        // How far the lowest point of the vehicle's renderers is above the ground below it (ground = first hit from just above that point).
        static void LogGap(Vehicle v, string tag, string when)
        {
            var rs = v.GetComponentsInChildren<Renderer>();
            float low = float.MaxValue; Vector3 at = v.transform.position;
            foreach (var r in rs) if (r.bounds.min.y < low) { low = r.bounds.min.y; at = r.bounds.center; }
            string ground = Physics.Raycast(new Vector3(at.x, low + 0.05f, at.z), Vector3.down, out var gh, 3f, ~0, QueryTriggerInteraction.Ignore) ? $"{gh.point.y:F3}" : "none";
            Debug.Log($"[FeelCapture] veh {tag} ({when}): lowest renderer y {low:F3}, ground y {ground}, body y {v.transform.position.y:F3}, wheels grounded {v.WheelCount}");
        }

        static string Clean(string name) => name.Replace("(Clone)", "").Trim();

        IEnumerator OneVehicle(Vehicle v, string tag)
        {
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            Debug.Log("[FeelCapture] veh " + tag + " begin");
            // The others stand in the way of the outside views: hidden for this vehicle's shots (shadows too).
            var hidden = new List<Renderer>();
            foreach (var o in Vehicle.All)
                if (o != null && o != v) foreach (var r in o.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }
            LogGap(v, tag, "start");
            var bounds = new Bounds(v.transform.position, Vector3.zero);
            foreach (var r in v.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float halfLength = Mathf.Max(1f, bounds.extents.z, bounds.extents.x);
            float height = bounds.size.y;
            bool bike = v.IsMotorcycle;
            // Out of the pictures: well behind the vehicle.
            pc.Teleport(v.transform.position - v.transform.forward * (halfLength + 6f) + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.4f);
            _watched = v;
            _watch = Watch.Vehicle;
            float s = bike ? 0.75f : 1f;
            _fixFrom = new Vector3(-halfLength * 1.25f * s, height * 0.95f, halfLength * 1.55f);
            _fixAt = new Vector3(0f, height * 0.42f, 0f);
            yield return VShot(tag + "_01_front34");
            _fixFrom = new Vector3(-halfLength * 2.6f, height * 0.5f, 0f);
            yield return VShot(tag + "_02_side");
            _fixFrom = new Vector3(halfLength * 1.2f * s, height * 1.0f, -halfLength * 1.5f);
            yield return VShot(tag + "_03_rear34");
            _watch = Watch.Game;

            // Driver.
            pc.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            float until;
            // Loot lying beside the door takes the F key (the pickup comes first): out of the way for the shot.
            if (pc.NearbyLoot != null) { Debug.Log("[FeelCapture] veh " + tag + ": hiding loot at the door " + pc.NearbyLoot.name); pc.NearbyLoot.gameObject.SetActive(false); }
            // F may pick up loot lying beside it first: ask again until seated.
            for (int tries = 0; tries < 4 && !VehicleSeat.IsAttached(me); tries++)
            {
                _vehicleFrame.Interact = true;
                float wait = Time.realtimeSinceStartup + 1.2f;
                while (!VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < wait) yield return null;
                if (!VehicleSeat.IsAttached(me)) Debug.Log($"[FeelCapture] veh {tag}: enter try {tries} failed, nearest {Vehicle.NearestEnterable(me.transform.position, 3f, out float nd)?.name} {nd:F2} m, door {v.DoorPosition(0)}, me {me.transform.position}");
            }
            yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.5f);
            Debug.Log($"[FeelCapture] veh {tag}: seated {VehicleSeat.SeatedAmount(me):F2}, seat eye {v.Seat(0).transform.localPosition}, wheel r {v.Rules.WheelRadius:F3}, mass {v.Rules.MassKg}, up {v.transform.up}");
            pc.LookAt(v.transform.position + v.transform.forward * 30f + Vector3.up * 1f);
            yield return new WaitForSeconds(0.5f);
            yield return VShot(tag + "_04_driver_tpp");
            _watched = v;
            _watch = Watch.Vehicle;
            var eye = v.transform.InverseTransformPoint(v.Seat(0).transform.position);
            _fixFrom = new Vector3(-(bike ? 2.2f : 2.6f), eye.y - 0.1f, eye.z + 0.4f);
            _fixAt = new Vector3(eye.x * 0.5f, eye.y - 0.45f, eye.z + 0.25f);
            yield return VShot(tag + "_05_driver_side");
            if (bike)
            {
                // The rider from the right too (the right foot and hand), and from above-front.
                _fixFrom = new Vector3(2.2f, eye.y - 0.1f, eye.z + 0.4f);
                yield return VShot(tag + "_05b_driver_right");
                _fixFrom = new Vector3(-0.6f, eye.y + 0.6f, eye.z + 1.8f);
                _fixAt = new Vector3(0f, eye.y - 0.5f, eye.z);
                yield return VShot(tag + "_05c_driver_front");
            }
            _watch = Watch.Game;
            _vehicleFrame.ToggleView = true;
            yield return new WaitForSeconds(0.5f);
            pc.LookAt(me.EyePosition + v.transform.forward * 10f - Vector3.up * 2.5f);
            yield return new WaitForSeconds(0.3f);
            yield return VShot(tag + "_06_driver_fpp");
            if (bike)
            {
                // Looking down at the bars and the hands.
                pc.LookAt(me.EyePosition + v.transform.forward * 3f - Vector3.up * 2.2f);
                yield return new WaitForSeconds(0.3f);
                yield return VShot(tag + "_06b_fpp_down");
                pc.LookAt(me.EyePosition + v.transform.forward * 10f - Vector3.up * 2.5f);
            }
            pc.LookAt(me.EyePosition - v.transform.right * 10f + v.transform.forward * 3f);
            yield return new WaitForSeconds(0.3f);
            yield return VShot(tag + "_07_fpp_left");
            pc.LookAt(me.EyePosition + v.transform.forward * 10f - Vector3.up * 2.5f);
            // Steering on the spot: the wheels, the steering wheel and the hands turn.
            _vehicleFrame.Steer = 1f;
            yield return new WaitForSeconds(1.2f);
            yield return VShot(tag + "_08_fpp_steer");
            _vehicleFrame.ToggleView = true;
            yield return new WaitForSeconds(0.4f);
            _watched = v;
            _watch = Watch.Vehicle;
            _fixFrom = new Vector3(-halfLength * 0.9f, height * 0.6f, halfLength * 1.9f);
            _fixAt = new Vector3(0f, height * 0.3f, halfLength * 0.4f);
            LogGap(v, tag, "steering");
            yield return VShot(tag + "_09_steer_wheels");
            _fixFrom = new Vector3(-(bike ? 2.2f : 2.6f), eye.y - 0.1f, eye.z + 0.4f);
            _fixAt = new Vector3(eye.x * 0.5f, eye.y - 0.45f, eye.z + 0.25f);
            yield return VShot(tag + "_10_steer_driver");
            _vehicleFrame.Steer = 0f;
            _watch = Watch.Game;
            yield return new WaitForSeconds(0.6f);

            // Passenger / pillion.
            _vehicleFrame.Seat = 1;
            until = Time.realtimeSinceStartup + 2f;
            while (!(VehicleSeat.TryGet(me, out _, out int seat) && seat == 1) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(v.Rules.SwitchSeconds + 0.4f);
            var pEye = v.transform.InverseTransformPoint(v.Seat(1).transform.position);
            _watched = v;
            _watch = Watch.Vehicle;
            float side = bike ? -1f : 1f;
            _fixFrom = new Vector3(side * (bike ? 2.2f : 2.6f), pEye.y - 0.1f, pEye.z + 0.4f);
            _fixAt = new Vector3(pEye.x * 0.5f, pEye.y - 0.45f, pEye.z + 0.25f);
            yield return VShot(tag + "_11_passenger_side");
            _watch = Watch.Game;
            if (!bike)
            {
                _vehicleFrame.ToggleView = true;
                yield return new WaitForSeconds(0.5f);
                pc.LookAt(me.EyePosition + v.transform.forward * 10f - v.transform.right * 3f - Vector3.up * 1.5f);
                yield return new WaitForSeconds(0.3f);
                yield return VShot(tag + "_12_passenger_fpp");
                _vehicleFrame.ToggleView = true;
                yield return new WaitForSeconds(0.4f);
                if (v.SeatCount > 2)
                {
                    _vehicleFrame.Seat = 2;
                    yield return new WaitForSeconds(v.Rules.SwitchSeconds + 0.6f);
                    var rEye = v.transform.InverseTransformPoint(v.Seat(2).transform.position);
                    _watched = v;
                    _watch = Watch.Vehicle;
                    _fixFrom = new Vector3(-2.8f, rEye.y + 0.2f, rEye.z + 0.6f);
                    _fixAt = new Vector3(rEye.x * 0.5f, rEye.y - 0.45f, rEye.z);
                    yield return VShot(tag + "_13_rear_side");
                    _watch = Watch.Game;
                }
            }
            _vehicleFrame.Interact = true;
            until = Time.realtimeSinceStartup + 3f;
            while (VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(0.4f);
            foreach (var r in hidden) if (r != null) r.enabled = true;
        }

        // Parked and wrecked cars: the first few from a standing height, a few metres off.
        IEnumerator PropShots(Transform root)
        {
            int count = Mathf.Min(6, root.childCount);
            for (int i = 0; i < count; i++)
            {
                var prop = root.GetChild(i);
                var r = prop.GetComponent<Renderer>();
                var centre = r != null ? r.bounds.center : prop.position;
                _watch = Watch.Fixed;
                _fixFrom = centre + prop.right * 5.5f + prop.forward * 4f + Vector3.up * 1.6f;
                _fixAt = centre;
                yield return null;
                yield return new WaitForSeconds(0.6f); // streaming in
                yield return VShot($"prop_{i:00}_{prop.name}");
            }
            if (root.childCount > 0)
            {
                // A wider look along the street at the first one.
                var prop = root.GetChild(0);
                _fixFrom = prop.position - prop.forward * 14f + prop.right * 4f + Vector3.up * 3f;
                _fixAt = prop.position + prop.forward * 6f;
                yield return VShot("prop_street");
            }
            _watch = Watch.Game;
        }
    }
}

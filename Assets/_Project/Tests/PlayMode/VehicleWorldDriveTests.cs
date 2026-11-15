using System;
using System.Collections;
using System.Collections.Generic;
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
    // #180: every parked vehicle of the World (host, one process) can be got into and driven off with the throttle.
    public class VehicleWorldDriveTests
    {
        sealed class NoPlayerInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
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

        [UnityTest, Timeout(900000)]
        public IEnumerator EveryParkedWorldVehicle_DrivesOffWithTheThrottle()
        {
            var keys = new Keys();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = keys;
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "local player in World");
            yield return WaitUntil(() => Vehicle.All.Count(v => v != null) > 0, 20f, "world vehicles");
            for (int n = 0; SpawnProtection.InputBlocked && n < 160; n++) yield return new WaitForSeconds(0.25f);
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            yield return new WaitForSeconds(2f); // the parked vehicles settle on their springs
            var failures = new List<string>();
            int tried = 0;
            foreach (var v in Vehicle.All.Where(x => x != null && x.HasFreeSeat && !(x.GetComponent<VehicleState>() != null && x.GetComponent<VehicleState>().Disabled)).OrderBy(x => x.name).Take(14).ToList())
            {
                tried++;
                string tag = $"{v.name.Replace("(Clone)", "")} at {v.transform.position:F0}";
                // Only where the road ahead is open for 12 m (a car parked against a wall cannot go).
                var from = v.transform.position + Vector3.up * 1f;
                bool clear = !Physics.SphereCast(from, 1.1f, v.transform.forward, out var wall, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || wall.collider.GetComponentInParent<Vehicle>() == v;
                if (!clear) { Debug.Log("[#180] skipped (blocked ahead) " + tag); continue; }
                PlayerController.Local.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
                yield return null;
                yield return null;
                for (int tries = 0; tries < 4 && !VehicleSeat.IsAttached(me); tries++)
                {
                    keys.Frame.Interact = true;
                    float wait = Time.realtimeSinceStartup + 1.2f;
                    while (!VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < wait) yield return null;
                }
                if (!VehicleSeat.IsAttached(me)) { failures.Add(tag + ": could not get in"); continue; }
                yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.4f);
                VehicleSeat.TryGet(me, out var seatedOn, out int seatedAt);
                if (seatedOn != v || seatedAt != 0)
                {
                    // F takes the nearest enterable vehicle: a neighbour parked closer to the door gets the player (not a fault).
                    Debug.Log($"[#180] {tag}: F boarded the neighbour {(seatedOn != null ? seatedOn.name : "nothing")} seat {seatedAt}; skipped");
                    keys.Frame.Interact = true;
                    yield return WaitUntil(() => !VehicleSeat.IsAttached(me), 5f, "got out");
                    continue;
                }
                var fuel = v.GetComponent<VehicleFuel>();
                if (fuel != null && fuel.Fuel01 < 0.3f) fuel.ServerSetFuel(1f);
                var start = v.transform.position;
                float top = 0f;
                keys.Frame.Throttle = 1f;
                for (float t = 0f; t < 6f; t += Time.deltaTime)
                {
                    top = Mathf.Max(top, v.SpeedKmh);
                    yield return null;
                }
                keys.Frame.Throttle = 0f;
                float moved = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(v.transform.position.x, v.transform.position.z));
                Debug.Log($"[#180] {tag}: top {top:F0} km/h, moved {moved:F1} m, engine {v.EngineOn.Value}, airborne {v.Airborne}, up.y {v.transform.up.y:F2}");
                var st = v.GetComponent<VehicleState>();
                if (moved < 8f) failures.Add($"{tag}: moved {moved:F1} m, top {top:F0} km/h (engine {v.EngineOn.Value}, airborne {v.Airborne}, up.y {v.transform.up.y:F2}, fuel {(st != null ? st.Fuel01 : -1f):F2}, disabled {(st != null && st.Disabled)}, health {(st != null ? st.Health01 : -1f):F2}, top factor {(st != null ? st.TopSpeedFactor : -1f):F2}, seat0 {v.OccupantId(0)}, owner {v.IsOwner})");
                keys.Frame.Throttle = -1f;
                yield return new WaitForSeconds(2f);
                keys.Frame.Throttle = 0f;
                keys.Frame.Interact = true;
                yield return WaitUntil(() => !VehicleSeat.IsAttached(me), 5f, "got out of " + tag);
                yield return new WaitForSeconds(0.4f);
            }
            Assert.Greater(tried, 0, "the World has vehicles");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}

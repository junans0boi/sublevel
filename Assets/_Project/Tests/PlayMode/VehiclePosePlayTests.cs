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
    // #102 / U2-U5 on every vehicle model of the TestBox: the driver's palms on the grips with the fingers closed,
    // the hands never crossing at full lock, the passengers' hands inside the body, and the eye (camera and shot
    // origin) at the seat's eye marker along the vehicle's up even when it is tilted.
    public class VehiclePosePlayTests
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
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = false;
                Frame.Seat = -1;
                return f;
            }
        }

        const float PalmOnGrip = 0.05f;     // palm centre to the grip point
        const float TipOnGrip = 0.09f;      // a closed hand's middle fingertip stays round the bar

        Keys _keys;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator Load()
        {
            _keys = new Keys();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = _keys;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => Vehicle.All.Count(v => v != null) >= 2, 10f, "vehicles");
            // The test box's dummies shoot at whoever stands in view; this test is about poses, not survival.
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            yield return new WaitForSeconds(1f);
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

        static string Name(Vehicle v) => v.name.Replace("(Clone)", "").Trim();

        IEnumerator Enter(Vehicle v)
        {
            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            // Loot beside the door would take the F key first.
            foreach (var loot in LootItem.All.ToList())
                if (loot != null && (loot.transform.position - me.transform.position).sqrMagnitude < 9f) { LootItem.All.Remove(loot); loot.gameObject.SetActive(false); }
            for (int tries = 0; tries < 4 && !VehicleSeat.IsAttached(me); tries++)
            {
                _keys.Frame.Interact = true;
                float wait = Time.realtimeSinceStartup + 1.2f;
                while (!VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < wait) yield return null;
            }
            VehicleSeat.TryGet(me, out var on, out int seat);
            Assert.IsTrue(on == v && seat == 0, $"{Name(v)}: in the driver's seat (got {(on != null ? Name(on) : "none")} seat {seat}, door {v.DoorPosition(0)}, me {me.transform.position})");
            yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.4f);
        }

        IEnumerator Leave(Vehicle v)
        {
            var me = Combatant.LocalPlayer;
            _keys.Frame.Steer = 0f;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => !VehicleSeat.IsAttached(me), 4f, Name(v) + ": got out");
            yield return new WaitForSeconds(0.3f);
        }

        // The vehicle's renderers' bounds in its own space.
        static Bounds LocalBounds(Vehicle v)
        {
            var t = v.transform;
            Bounds? local = null;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = t.InverseTransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (local == null) local = new Bounds(p, Vector3.zero);
                    else { var l = local.Value; l.Encapsulate(p); local = l; }
                }
            }
            return local ?? new Bounds();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator EveryVehicle_DriverGripsAndPassengerHandsInside()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var anim = me.GetComponent<CombatantAnimation>();
            var failures = new List<string>();
            foreach (var v in Vehicle.All.Where(x => x != null).OrderBy(x => x.name).ToList())
            {
                string tag = Name(v);
                yield return Enter(v);
                var seat = v.Seat(0);
                if (seat.HasGrips)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        var grip = (side == 0 ? seat.GripLeft : seat.GripRight).position;
                        float palm = Vector3.Distance(anim.PalmPoint(side), grip), tip = Vector3.Distance(anim.FingerTip(side), grip);
                        Debug.Log($"[PoseTest] {tag} hand {side}: palm {palm:F3} m, tip {tip:F3} m from the grip; torso lean {Vector3.Angle(anim.TorsoUpShown, v.transform.up):F1}");
                        if (palm > PalmOnGrip) failures.Add($"{tag} hand {side} palm {palm:F3} m off the grip");
                        if (tip > TipOnGrip) failures.Add($"{tag} hand {side} fingertip {tip:F3} m off the grip (open hand)");
                    }
                    // Full lock: the hands stay on their own sides (no X).
                    _keys.Frame.Steer = 1f;
                    yield return new WaitForSeconds(1.2f);
                    float l = v.transform.InverseTransformPoint(anim.PalmPoint(0)).x, r = v.transform.InverseTransformPoint(anim.PalmPoint(1)).x;
                    Debug.Log($"[PoseTest] {tag} full lock: left palm x {l:F3}, right palm x {r:F3}");
                    // At the 90 degree limit the hands are one over the other (same x); crossed is left right of right.
                    if (l > r + 0.03f) failures.Add($"{tag} hands crossed at full lock ({l:F3} / {r:F3})");
                    _keys.Frame.Steer = 0f;
                    yield return new WaitForSeconds(0.6f);
                }
                // Eye at the seat's eye marker.
                float eyeOff = Vector3.Distance(me.EyePosition, seat.transform.position);
                if (eyeOff > 0.03f) failures.Add($"{tag} eye {eyeOff:F3} m off the seat's eye marker");

                // Passengers: every other seat, hands inside the cabin box (ADR-077 `Cabin`, root space) or, on a
                // motorcycle, inside the body's sides and under its top.
                var cabin = v.transform.Find("Cabin");
                var box = cabin != null ? new Bounds(cabin.localPosition, cabin.localScale + Vector3.one * 0.16f) : LocalBounds(v);
                for (int s = 1; s < v.SeatCount; s++)
                {
                    _keys.Frame.Seat = s;
                    yield return WaitUntil(() => VehicleSeat.TryGet(me, out _, out int now) && now == s, 3f, tag + " seat " + s);
                    yield return new WaitForSeconds(v.Rules.SwitchSeconds + 0.4f);
                    for (int side = 0; side < 2; side++)
                    {
                        var hand = v.transform.InverseTransformPoint(anim.PalmPoint(side));
                        Debug.Log($"[PoseTest] {tag} seat {s} hand {side} at {hand:F3}, body x {box.min.x:F2}..{box.max.x:F2}");
                        if (Mathf.Abs(hand.x - box.center.x) > box.extents.x - 0.08f || hand.y > box.max.y)
                            failures.Add($"{tag} seat {s} hand {side} outside the body ({hand:F3})");
                    }
                }
                yield return Leave(v);
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // #155: the driver's helmet stays under the roof of every car (it showed through the roof of the Fairheaven and the Goblin).
        [UnityTest, Timeout(300000)]
        public IEnumerator EveryCar_DriversHeadStaysUnderTheRoof()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var failures = new List<string>();
            foreach (var v in Vehicle.All.Where(x => x != null && !x.IsMotorcycle).OrderBy(x => x.name).ToList())
            {
                yield return Enter(v);
                float roof = float.NegativeInfinity, head = float.NegativeInfinity;
                foreach (var r in v.GetComponentsInChildren<Renderer>())
                    if (!(r is ParticleSystemRenderer) && r.enabled && !r.name.Contains("Antenna")) roof = Mathf.Max(roof, r.bounds.max.y);
                string highest = "";
                foreach (var r in me.GetComponentsInChildren<Renderer>())
                    if (!(r is ParticleSystemRenderer) && r.enabled && r.bounds.max.y > head) { head = r.bounds.max.y; highest = r.name; }
                Debug.Log($"[#155] {Name(v)}: head top {head:F3}, roof top {roof:F3}, eye {me.EyePosition.y:F3}, highest renderer {highest}, body y {me.transform.position.y:F3}");
                if (head > roof - 0.02f) failures.Add($"{Name(v)}: head top {head:F3} m is not under the roof top {roof:F3} m (eye {me.EyePosition.y:F3})");
                yield return Leave(v);
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [UnityTest]
        public IEnumerator SeatedOnATiltedMotorcycle_TheEyeStaysAtTheSeatMarker()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var bike = Vehicle.All.First(x => x != null && x.IsMotorcycle);
            yield return Enter(bike);
            bike.Body.isKinematic = true;
            var tilted = Quaternion.AngleAxis(30f, bike.transform.forward) * bike.transform.rotation;
            bike.Body.rotation = tilted;
            bike.transform.rotation = tilted;
            // The body interpolates from its last physics pose: two steps for both ends to be the tilted one.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            var marker = bike.Seat(0).transform.position;
            Assert.AreEqual(0f, Vector3.Distance(me.EyePosition, marker), 0.03f, "eye at the seat marker on a tilted bike");
            Assert.Greater(Vector3.Angle(me.transform.up, Vector3.up), 20f, "the body tilts with the bike");
        }
    }
}

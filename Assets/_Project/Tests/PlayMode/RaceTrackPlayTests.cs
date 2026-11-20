using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #438 (ADR-505/506/508/511): the race hall of the waiting island in the real World scene, as a host with its own client: five race cars
    // on the grid, no damage or fuel use for them, no roadkill, people on foot kept off the track, empty cars back on the grid, and the
    // departure takes everyone out of the cars and the cars away.
    public class RaceTrackPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        bool _oldFlag;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            MoveGuard.TrustTeleports = true;   // the tests move people about on purpose; the waiting clamp still applies
            DedicatedServer.StartSecondsOverride = TestContext.CurrentContext.Test.Name.Contains("Departure") ? 14f : 120f;
            PlayerController.InputOverride = new NoPlayerInput();
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 90f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || WaitingRoom.Current == null || RaceRoom.Current == null)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "world loaded, the waiting room and the race open");
                yield return null;
            }
            yield return null;
            var me = Combatant.LocalPlayer;
            // The dedicated server guards every person (all remote); the host's own body is unguarded, so the test adds the guard.
            if (!me.TryGetComponent<MoveGuard>(out _)) me.gameObject.AddComponent<MoveGuard>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchRules.Current.UseWaitingIsland = _oldFlag;
            WaitingRoom.ForceEnabled = false;
            MoveGuard.TrustTeleports = false;
            DedicatedServer.StartSecondsOverride = -1f;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static float DeckY => GameObject.Find("WaitingIsland").transform.position.y;
        static Vector3 Local(float lx, float y, float lz) => new Vector3(WaitingIsland.WorldX(lx), y, WaitingIsland.WorldZ(lz));
        static (float x, float z) ToLocal(Vector3 p) => (p.x - WaitingIsland.CentreX, p.z - WaitingIsland.CentreZ);

        [UnityTest, Timeout(300000)]
        public IEnumerator FiveRaceCars_StandOnTheGrid_TakeNoDamage_UseNoFuel()
        {
            var race = RaceRoom.Current;
            Assert.AreEqual(RaceTrack.Cars, race.CarCount);
            for (int i = 0; i < race.CarCount; i++)
            {
                var (lx, lz) = ToLocal(race.CarAt(i).transform.position);
                Assert.IsTrue(RaceTrack.InPit(lx, lz), $"car {i} in the pit: {lx:0.0}, {lz:0.0}");
                Assert.AreEqual(VehicleKind.Car, race.CarAt(i).Kind);
                Assert.IsTrue(race.CarAt(i).IsRaceCar);
            }
            var car = race.CarAt(0);
            var damage = car.GetComponent<VehicleDamage>();
            damage.ServerBlast(car.transform.position, d => 10000f, 1f);
            car.ServerCrash(40f);
            var body = car.GetComponentInChildren<Collider>();
            var from = car.transform.position + car.transform.forward * 8f + Vector3.up;
            damage.ServerShot(from, -car.transform.forward, 8f, body, car.transform.position + Vector3.up, 60f);
            var fuel = car.GetComponent<VehicleFuel>();
            fuel.ServerSetFuel(0.3f);
            yield return null; yield return null;
            Assert.AreEqual(1f, damage.Health.Value, 1e-4f, "a blast or a shot does nothing to a race car");
            Assert.AreEqual(0, damage.FlatTyres.Value, "no tyre is shot flat");
            Assert.AreEqual(1f, fuel.Fuel01, 1e-4f, "a race car's tank stays full");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator OnFoot_TheTrackIsOffLimits_ButTheDriverGoesWhereTheCarGoes()
        {
            var me = Combatant.LocalPlayer;
            float deck = DeckY;
            PlayerController.Local.Teleport(Local(180f, deck + 0.1f, 0f));   // on the track between T5 and T6
            yield return new WaitForSeconds(1.2f);
            var (lx, lz) = ToLocal(me.transform.position);
            Assert.IsTrue(RaceTrack.Walkable(lx, lz), $"walked onto the track: put back on the island or in the pit, now {lx:0.0}, {lz:0.0}");
            Assert.IsTrue(RaceTrack.InPit(lx, lz), "the nearest walkable place from there is the pit");

            // In a car on the track: not clamped.
            var car = RaceRoom.Current.CarAt(0);
            PlayerController.Local.Teleport(car.transform.position + car.transform.right * -2f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.3f);
            Assert.GreaterOrEqual(car.ServerEnter(me), 0, "took the driver's seat");
            float end = Time.realtimeSinceStartup + 5f;
            while (!VehicleSeat.IsAttached(me)) { Assert.Less(Time.realtimeSinceStartup, end, "seated"); yield return null; }
            car.ServerCrash(40f);   // a crash report from the driver: nobody is thrown out of a race car
            yield return null;
            Assert.IsTrue(VehicleSeat.IsAttached(me), "still seated after a crash");
            car.ServerPlace(Local(RaceTrack.MainX, deck + 0.3f, RaceTrack.LineZ - 20f), Quaternion.Euler(0f, 180f, 0f));
            yield return new WaitForSeconds(MoveGuard.Interval * 4f);
            (lx, lz) = ToLocal(me.transform.position);
            Assert.IsTrue(RaceTrack.OnTrack(lx, lz), $"the driver is on the track with the car, now {lx:0.0}, {lz:0.0}");

            // Out of the car on the track: back to the pit.
            car.ServerExit(me);
            yield return new WaitForSeconds(1.5f);
            (lx, lz) = ToLocal(me.transform.position);
            Assert.IsTrue(RaceTrack.Walkable(lx, lz), $"got out on the track: put back in the pit, now {lx:0.0}, {lz:0.0}");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EmptyCar_GoesBackToItsGridSpot()
        {
            var car = RaceRoom.Current.CarAt(1);
            var home = car.transform.position;
            car.ServerPlace(Local(180f, DeckY + 0.3f, 0f), Quaternion.identity);
            yield return new WaitForSeconds(1f);
            Assert.Greater(Vector3.Distance(car.transform.position, home), 30f, "still out on the track a moment later");
            yield return new WaitForSeconds(RaceRoom.ResetSeconds + 0.5f);
            Assert.Less(Vector3.Distance(car.transform.position, home), 0.6f, "back on its spot");

            // Upside down on its own spot: put back on its wheels too.
            car.ServerPlace(home + Vector3.up * 1.5f, car.transform.rotation * Quaternion.Euler(0f, 0f, 180f));
            yield return new WaitForSeconds(RaceRoom.ResetSeconds + 1.5f);
            Assert.Greater(car.transform.up.y, 0.9f, "a flipped empty car is put back on its wheels");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator ARaceCar_NeverRunsOverOrShovesAPerson()
        {
            var me = Combatant.LocalPlayer;
            float deck = DeckY;
            var car = RaceRoom.Current.CarAt(3);   // back row: its way east runs between two front-row cars, inside the pit
            // The person stands in the pit about 2 m ahead of the car's nose; the empty car is launched at 15 m/s into them.
            var stand = car.transform.position + car.transform.forward * 4.5f; stand.y = deck + 0.1f;
            var (sx, sz) = ToLocal(stand);
            Assert.IsTrue(RaceTrack.InPit(sx, sz), "the person stands in the pit");
            PlayerController.Local.Teleport(stand);
            me.GetComponent<MoveGuard>().ServerRelocate(stand);   // the host's own teleport does not reach the guard the test added
            yield return new WaitForSeconds(0.5f);
            var before = me.transform.position;
            float hp = me.Hp.Value;
            car.Body.linearVelocity = car.transform.forward * 15f;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f, "no damage");
            var moved = me.transform.position - before; moved.y = 0f;
            Assert.Less(moved.magnitude, 1f, $"not shoved (a roadkill shove is about 4 m): moved {moved.magnitude:0.00} m");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Departure_MidRace_TakesTheDriverOut_ToTheElevator_AndTheCarsAway()
        {
            var me = Combatant.LocalPlayer;
            var car = RaceRoom.Current.CarAt(0);
            PlayerController.Local.Teleport(car.transform.position + car.transform.right * -2f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.3f);
            car.ServerEnter(me);
            float end = Time.realtimeSinceStartup + 5f;
            while (!VehicleSeat.IsAttached(me)) { Assert.Less(Time.realtimeSinceStartup, end, "seated"); yield return null; }
            car.ServerPlace(Local(RaceTrack.MainX, DeckY + 0.3f, RaceTrack.LineZ - 20f), Quaternion.identity);
            end = Time.realtimeSinceStartup + 60f;
            while (WaitingRoom.Current != null) { Assert.Less(Time.realtimeSinceStartup, end, "the countdown ended"); yield return null; }
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(VehicleSeat.IsAttached(me), "out of the car");
            var p = me.transform.position;
            Assert.IsFalse(WaitingIsland.Inside(p.x, p.z) || RaceTrack.InArea(p.x, p.z), "sent to the elevator: " + p);
            Assert.IsTrue(SpawnProtection.InputBlocked, "in the elevator with the doors shut");
            Assert.IsNull(RaceRoom.Current);
            foreach (var v in Vehicle.All) Assert.IsFalse(v.IsRaceCar, "no race car is left: " + v.name);
        }
        // PERF A (ADR-041) with the race cars out: inside the island looking at the gate, in the pit at the grid, on the west straight, and
        // into the hairpin and the chicane. Run in a quiet window: the p99 is the maximum of ~60 frames.
        [UnityTest, Category("Perf"), Timeout(300000)]
        public IEnumerator RaceHall_RendersWithinBudget()
        {
            float deck = DeckY;
            var cam = new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.enabled = false; cam.nearClipPlane = 0.05f; cam.farClipPlane = 700f; cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.GetUniversalAdditionalCameraData().SetRenderer(WorldInfo.Current.RendererIndex);
            var views = new (string id, Vector3 from, Vector3 to)[]
            {
                ("island_gate", Local(4f, deck + 1.7f, -12f), Local(48f, deck + 1.2f, -15f)),
                ("pit_grid", Local(RaceTrack.MainX + 2f, deck + 2.2f, RaceTrack.LineZ - 6f), Local(43f, deck + 0.5f, -13f)),
                ("main_straight", Local(RaceTrack.MainX, deck + 1.2f, RaceTrack.LineZ + 14f), Local(RaceTrack.MainX, deck + 1f, -110f)),
                ("hairpin", Local(RaceTrack.MainX + 1f, deck + 1.2f, -95f), Local(80f, deck + 1f, -135f)),
                ("chicane", Local(180f, deck + 1.2f, -10f), Local(130f, deck + 1f, 40f)),
            };
            var results = new List<RenderProbe.Result>();
            foreach (var v in views)
            {
                cam.transform.position = v.from; cam.transform.LookAt(v.to);
                RenderProbe.Result r = default;
                yield return RenderProbe.Measure(cam, x => r = x, screenshotPath: $"Logs/perf/race_{v.id}.png");
                results.Add(r);
            }
            Object.Destroy(cam.gameObject);
            RenderProbe.WriteReport("Logs/perf/race.json", "{" + string.Join(",", views.Select((v, i) => $"\"{v.id}\":{results[i].ToJson()}")) + "}");
            Debug.Log("[Perf] race " + string.Join(" | ", views.Select((v, i) => v.id + ": " + results[i])));
            foreach (var r in results) RenderProbe.AssertWithinBudget(r);
        }
    }
}

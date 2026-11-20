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
    // #445 (ADR-512..516): the event race scene as a local host: the player starts at the wheel of a grid car, nothing moves until GO, there
    // is no getting out, and a car off the course is put back on it.
    public class RaceEventPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        static RaceEvent.CourseInfo Course => RaceEvent.Courses[0];

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            MoveGuard.TrustTeleports = true;
            PlayerController.InputOverride = new NoPlayerInput();
            SceneManager.LoadScene(Course.Scene);
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || RaceDirector.Current == null)
            {
                Assert.Less(Time.realtimeSinceStartup, end, "race scene loaded, the player and the race director there");
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MoveGuard.TrustTeleports = false;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
            Assert.IsFalse(Vehicle.EventRace, "leaving the race scene ends the event-race rules");
        }

        static IEnumerator Until(System.Func<bool> ok, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok()) { Assert.Less(Time.realtimeSinceStartup, end, what); yield return null; }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator StartsAtTheWheel_OnTheGrid_LockedUntilGo()
        {
            var me = Combatant.LocalPlayer;
            yield return Until(() => VehicleSeat.TryGet(me, out _, out int seat) && seat == Vehicles.DriverSeat, 10f, "seated at the wheel");
            var car = VehicleSeat.VehicleOf(me);
            Assert.IsTrue(car.IsRaceCar, "every car of the event scene is a race car");
            Assert.AreEqual(1, RaceDirector.Current.CarCount, "one car per person");
            var (s, off) = Course.Course.Project(car.transform.position.x, car.transform.position.z);
            Assert.Greater(s, Course.Course.LapLength - 120f, "on the grid behind the line");
            Assert.Less(Mathf.Abs(off), Course.Course.Width * 0.5f, "on the tarmac");

            yield return Until(() => RaceHud.Phase == RacePhase.Countdown, 12f, "the countdown starts once everyone is in");
            Assert.IsTrue(SpawnProtection.InputBlocked, "no driving before GO");
            var at = car.transform.position;
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, RaceEvent.CountdownSeconds + 2f, "GO");
            Assert.IsFalse(SpawnProtection.InputBlocked, "free to drive after GO");
            Assert.Less(Vector3.Distance(at, car.transform.position), 0.5f, "nothing moved during the countdown");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator NoGettingOut_AndACarOffTheCourseIsPutBack()
        {
            var me = Combatant.LocalPlayer;
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 20f, "the race is on");
            var car = VehicleSeat.VehicleOf(me);
            Assert.IsNotNull(car);
            car.RequestExit(me);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(car, VehicleSeat.VehicleOf(me), "no getting out in an event race");

            // R: back onto the nearest point of the course, facing the way of the race.
            var c = Course.Course;
            car.Place(new Vector3(9f, 0.4f, 0f), Quaternion.Euler(0f, 90f, 0f));
            yield return new WaitForSeconds(0.3f);
            RaceHud.RequestReset();
            yield return new WaitForSeconds(0.5f);
            var p = car.transform.position;
            Assert.Less(Mathf.Abs(c.Project(p.x, p.z).offset), 1f, "R put it on the centre line: " + p);
            Assert.Greater(Vector3.Dot(car.transform.forward, Vector3.back), 0.9f, "facing north, the way of the race");

            // Left upside down in the field: put back on its own.
            car.Place(new Vector3(-60f, 1f, 0f), Quaternion.Euler(0f, 0f, 180f));
            yield return new WaitForSeconds(RaceEvent.StuckResetSeconds + 1.5f);
            p = car.transform.position;
            Assert.Less(Mathf.Abs(c.Project(p.x, p.z).offset), 1.5f, "stuck off the course: back on it, now " + p);
            Assert.Greater(car.transform.up.y, 0.9f, "on its wheels");
        }

        // Review shots. Run on purpose with
        //   RACE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.RaceEventPlayTests.CaptureStartAndRace
        [UnityTest, Explicit, Category("Capture"), Timeout(180000)]
        public IEnumerator CaptureStartAndRace()
        {
            string folder = System.Environment.GetEnvironmentVariable("RACE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/lookdev";
            Directory.CreateDirectory(folder);
            var me = Combatant.LocalPlayer;
            yield return Until(() => VehicleSeat.VehicleOf(me) != null, 10f, "seated");
            yield return Until(() => RaceHud.Phase == RacePhase.Countdown && RaceHud.CountdownNow < 1.9f, 15f, "two lights to go");
            yield return UiShot.Save(Path.Combine(folder, "event_countdown.png"), 1920, 1080);
            yield return Until(() => RaceHud.GoShowing, 3f, "GO");
            yield return UiShot.Save(Path.Combine(folder, "event_go.png"), 1920, 1080);
        }
    }
}

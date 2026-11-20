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
            Assert.AreEqual(car, RaceDirector.Current.CarOf(me), "the player's own grid car");
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

            // Left upside down in the field, far east by the fork (the bots will not be there for a minute): put back on its own.
            car.Place(new Vector3(470f, 1f, -40f), Quaternion.Euler(0f, 0f, 180f));
            yield return new WaitForSeconds(RaceEvent.StuckResetSeconds + 1.5f);
            p = car.transform.position;
            Assert.Less(Mathf.Abs(c.Project(p.x, p.z).offset), c.Width * 0.5f, "stuck off the course: back on it (a passing bot may nudge it), now " + p);
            Assert.Greater(car.transform.up.y, 0.9f, "on its wheels");
        }

        // #446: driven round the course twice (placed along the centre line at a steady pace, a lap a little over the course's shortest real
        // lap), the server counts the laps, finishes the race, shows the results and the game goes back to the menu.
        [UnityTest, Timeout(400000)]
        public IEnumerator TwoLaps_Finish_Results_ThenBackToTheMenu()
        {
            var me = Combatant.LocalPlayer;
            var c = Course.Course;
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 20f, "the race is on");
            var car = VehicleSeat.VehicleOf(me);
            float lapSeconds = c.MinLapSeconds + 3f, speed = c.LapLength / lapSeconds;
            float s = c.Project(car.transform.position.x, car.transform.position.z).s - c.LapLength;   // behind the line: negative
            float goal = c.LapLength * Course.Laps + 5f;
            int lapsSeen = 0;
            while (s < goal)
            {
                s += speed * Time.deltaTime;
                var (x, z, yaw) = c.PointAt(s);
                car.Place(new Vector3(x, 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                var m = RaceHud.Last;
                if (m.LapsDone != null && m.LapsDone.Length > 0) lapsSeen = Mathf.Max(lapsSeen, m.LapsDone[0]);
                yield return null;
            }
            // The bots are far behind (a lap here takes them more than twice as long): the rest get AfterWinnerSeconds, then the results.
            yield return Until(() => RaceHud.Phase == RacePhase.Finishing, 5f, "the winner is in: the rest get their while");
            yield return Until(() => RaceHud.Phase == RacePhase.Results, RaceEvent.AfterWinnerSeconds + 5f, "results");
            var r = RaceHud.Last;
            Assert.AreEqual(RaceEvent.Racers, r.Keys.Length, "the player and the bots");
            Assert.AreEqual(me.ObjectId, r.Keys[0], "the local player first");
            for (int i = 1; i < r.Keys.Length; i++) Assert.IsFalse(r.Done[i], "the bots did not finish in time: DNF");
            Assert.IsTrue(r.Done[0], "finished");
            Assert.AreEqual(Course.Laps, r.LapsDone[0]);
            Assert.GreaterOrEqual(lapsSeen, Course.Laps - 1, "the laps showed while racing");
            Assert.AreEqual(lapSeconds * Course.Laps, r.Times[0], 6f, "the race time");
            Assert.Greater(r.Best[0], c.MinLapSeconds, "a clean best lap");
            yield return Until(() => SceneManager.GetActiveScene().name == MainMenu.SceneName, RaceEvent.ResultsSeconds + 10f, "back to the menu after the results");
        }

        // #447 (ADR-517): bots fill the grid to RaceEvent.Racers and every one of them drives a full lap round the course on its own.
        [UnityTest, Timeout(480000)]
        public IEnumerator Bots_FillTheGrid_AndEachDrivesALap()
        {
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 20f, "the race is on");
            yield return new WaitForSeconds(1f);
            var m = RaceHud.Last;
            Assert.AreEqual(RaceEvent.Racers, m.Keys.Length, "people + bots = " + RaceEvent.Racers);
            int bots = 0; foreach (int k in m.Keys) if (k < 0) bots++;
            Assert.AreEqual(RaceEvent.Racers - 1, bots);
            float start = Time.realtimeSinceStartup, end = start + 360f;
            while (true)
            {
                m = RaceHud.Last;
                int lapped = 0;
                for (int i = 0; i < m.Keys.Length; i++) if (m.Keys[i] < 0 && m.LapsDone[i] >= 1) lapped++;
                if (lapped == bots) break;
                Assert.Less(Time.realtimeSinceStartup, end, $"every bot round once (so far {lapped}/{bots})");
                yield return new WaitForSeconds(1f);
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < m.Keys.Length; i++) sb.Append($"{m.Names[i]} laps {m.LapsDone[i]} best {m.Best[i]:0.0}; ");
            Debug.Log($"[RaceBots] all bots lapped in {Time.realtimeSinceStartup - start:0} s, {RaceDirector.Current.BotResets} resets: {sb}");
            Assert.LessOrEqual(RaceDirector.Current.BotResets, bots, "the bots keep to the course (at most one put-back each)");
        }

        // Review shots. Run on purpose with
        //   RACE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.RaceEventPlayTests.CaptureStartAndRace
        [UnityTest, Explicit, Category("Capture"), Timeout(400000)]
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
            // Round the course (as in TwoLaps_Finish_Results_ThenBackToTheMenu): a shot mid-race, one of the results.
            var c = Course.Course; var car = VehicleSeat.VehicleOf(me);
            float lapSeconds = c.MinLapSeconds + 3f, speed = c.LapLength / lapSeconds;
            float s = c.Project(car.transform.position.x, car.transform.position.z).s - c.LapLength, goal = c.LapLength * Course.Laps + 5f;
            bool shot = false;
            while (s < goal)
            {
                s += speed * Time.deltaTime;
                var (x, z, yaw) = c.PointAt(s);
                car.Place(new Vector3(x, 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                if (!shot && s > c.LapLength * 0.28f) { shot = true; yield return UiShot.Save(Path.Combine(folder, "event_racing.png"), 1920, 1080); }
                yield return null;
            }
            yield return Until(() => RaceHud.Phase == RacePhase.Results, RaceEvent.AfterWinnerSeconds + 5f, "results");
            yield return new WaitForSeconds(0.5f);
            yield return UiShot.Save(Path.Combine(folder, "event_results.png"), 1920, 1080);
        }
    }
}

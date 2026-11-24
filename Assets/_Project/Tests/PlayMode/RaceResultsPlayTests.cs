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
    // #516 (spec #505, ADR-562): a solo race from the race room ends in the results dialog; 방으로 돌아가기 goes back to the room at once with
    // everyone unready and the course, laps, car, paint and slots kept.
    public class RaceResultsPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey);
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MoveGuard.TrustTeleports = false;
            PlayerController.InputOverride = null;
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
            PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey);
            RaceLobbyScreen.ReturnPending = false;
            RaceEvent.Starting = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Until(System.Func<bool> ok, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok()) { Assert.Less(Time.realtimeSinceStartup, end, what); yield return null; }
        }

        // The menu's race room set up as `setup` says, started, and my car placed round the course at `pace` x the shortest lap until the
        // finish; returns once the results are up.
        static IEnumerator RaceToResults(System.Action<RaceLobbyScreen> setup, float pace)
        {
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return Until(() => MainMenu.Instance != null, 20f, "main menu");
            yield return null;
            MainMenu.Instance.ShowEventEntry();
            var room = MainMenu.Instance.EventEntry;
            setup(room);
            MoveGuard.TrustTeleports = true;
            PlayerController.InputOverride = new NoPlayerInput();
            room.Go();
            yield return Until(() => RaceDirector.Current != null && Combatant.LocalPlayer != null, 60f, "the race scene and the player");
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 25f, "the race is on");
            var car = VehicleSeat.VehicleOf(Combatant.LocalPlayer);
            var c = RaceDirector.Current.Info.Course;
            float speed = c.LapLength / (c.MinLapSeconds * pace);
            float s = c.Project(car.transform.position.x, car.transform.position.z).s - c.LapLength, goal = c.LapLength * RaceDirector.Current.Laps + 5f;
            while (s < goal)
            {
                s += speed * Time.deltaTime;
                var (x, z, yaw) = c.PointAt(s);
                car.Place(new Vector3(x, 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                yield return null;
            }
            yield return Until(() => RaceHud.Phase == RacePhase.Results, RaceEvent.AfterWinnerSeconds + 5f, "results");
            yield return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Solo_Finish_Results_BackToRoom_SameSettings_Unready()
        {
            yield return RaceToResults(room =>
            {
                Assert.IsTrue(room.Room.SetCar(room.Me, "GoblinV12", 2));
                Assert.IsTrue(room.Room.SetCourse(room.Me, "snake", 1));
                Assert.IsTrue(room.Room.SetSettings(room.Me, "준환의 레이스", false, false));   // fill off: a race alone, results at once
                room.CloseSlot(7);
            }, 1.05f);

            var view = Object.FindAnyObjectByType<RaceResultsView>();
            Assert.IsNotNull(view, "the results dialog");
            Assert.IsTrue(RaceResultsView.Shown);
            Assert.AreEqual("결과 · 스네이크 서킷", view.Title);
            Assert.AreEqual(1, view.RowCount, "me alone");
            var label = view.BackButton.GetComponentInChildren<UnityEngine.UI.Text>().text;
            Assert.AreEqual("방으로 돌아가기", label);

            float pressed = Time.realtimeSinceStartup;
            view.BackButton.onClick.Invoke();
            yield return Until(() => MainMenu.Instance != null && MainMenu.Instance.EventEntryShown, 20f, "back in the room");
            Assert.Less(Time.realtimeSinceStartup - pressed, RaceEvent.ResultsSeconds, "the button, not the results clock");
            Assert.IsFalse(RaceResultsView.Shown);

            var back = MainMenu.Instance.EventEntry.Room;
            CollectionAssert.Contains(back.Lines, "레이스가 끝났습니다. 다시 준비해 주세요.");
            Assert.AreEqual("snake", back.Course);
            Assert.AreEqual(1, back.Laps);
            Assert.AreEqual("준환의 레이스", back.Name);
            Assert.IsFalse(back.FillBots);
            Assert.IsTrue(back.IsClosed(7));
            var me = back.Member(MainMenu.Instance.EventEntry.Me);
            Assert.AreEqual("GoblinV12", me.Car);
            Assert.AreEqual(2, me.Paint);
            Assert.IsFalse(back.Members.Any(m => m.Ready), "everyone unready");
        }

        // Review shots of the results dialog and the room after it, against docs/research/ui_mockups/race_room.html:
        //   RACE_SHOTS=Logs/race_results tools/unity.sh test playmode Game.Tests.RaceResultsPlayTests.CaptureResults
        [UnityTest, Explicit, Category("Capture"), Timeout(600000)]
        public IEnumerator CaptureResults()
        {
            string folder = System.Environment.GetEnvironmentVariable("RACE_SHOTS");
            folder = Path.GetFullPath(string.IsNullOrEmpty(folder) ? "Logs/race_results" : folder);
            Directory.CreateDirectory(folder);
            // Two laps at the bots' pace, so some of them finish inside the 30 s after me and the rows show gaps.
            yield return RaceToResults(room => room.Room.SetCar(room.Me, "GoblinV12", 2), 2.1f);
            yield return new WaitForSeconds(0.5f);
            yield return UiShot.Save(Path.Combine(folder, "01_results_1920x1080.png"), 1920, 1080);
            yield return UiShot.Save(Path.Combine(folder, "02_results_1728x1080.png"), 1728, 1080);
            Object.FindAnyObjectByType<RaceResultsView>().BackButton.onClick.Invoke();
            yield return Until(() => MainMenu.Instance != null && MainMenu.Instance.EventEntryShown, 20f, "back in the room");
            yield return new WaitForSeconds(0.5f);
            yield return UiShot.Save(Path.Combine(folder, "03_room_after.png"), 1920, 1080);
        }
    }
}

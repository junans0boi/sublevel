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
    // #445 (ADR-512), #511 (spec #505, ADR-562): 이벤트 in the lobby's top bar opens the race room; its 시작 (F5) starts the room's course
    // with bots in the open empty slots, and the player lands back in the room after the race.
    public class RaceEventMenuTests
    {
        static IEnumerator LoadMenu()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
        }

        [SetUp]
        public void SetUp() => PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
            PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey);
            RaceLobbyScreen.ReturnPending = false;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
        }

        static IEnumerator Race(RaceLobbyScreen room)
        {
            room.Go();
            yield return NetHarness.WaitUntil(() => RaceDirector.Current != null && Combatant.LocalPlayer != null, "the race scene and the player", null, 60f);
            // Bots take their cars when the countdown starts.
            yield return NetHarness.WaitUntil(() => RaceDirector.Current.Flow != null && RaceDirector.Current.Flow.Phase >= RacePhase.Countdown, "the countdown", null, 30f);
            yield return null;
        }

        // After the race: the HUD leaves the match like this (RaceHudView on Over).
        static IEnumerator BackFromRace()
        {
            MainMenu.LeaveMatch();
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null && MainMenu.Instance.EventEntryShown, "back in the room", null, 30f);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Solo_EventOpensTheRoom_StartRacesTheCourse_AndReturnsToTheRoom()
        {
            yield return LoadMenu();
            var menu = MainMenu.Instance;
            menu.ShowEventEntry();
            Assert.IsTrue(menu.EventEntryShown);
            Assert.AreEqual(1, menu.ActiveScreenRoots);
            var room = menu.EventEntry;
            Assert.AreEqual(room.Me, room.Room.HostId, "alone = the host");
            Assert.IsTrue(room.Room.GoFor(room.Me).Enabled, "alone the host may always start");
            Assert.AreEqual(7, room.Room.BotCount);

            yield return Race(room);
            Assert.AreEqual(RaceEvent.Courses[0].Scene, SceneManager.GetActiveScene().name);
            Assert.IsTrue(Vehicle.EventRace);
            Assert.AreEqual("snake", RaceEvent.Starting.CourseId);
            Assert.AreEqual(7, RaceEvent.Starting.Bots);
            Assert.AreEqual(2, RaceDirector.Current.Laps);
            Assert.AreEqual(8, RaceDirector.Current.CarCount, "me + 7 bots");

            yield return BackFromRace();
            var back = MainMenu.Instance.EventEntry;
            Assert.AreEqual(1, MainMenu.Instance.ActiveScreenRoots);
            CollectionAssert.Contains(back.Room.Lines, "레이스가 끝났습니다. 다시 준비해 주세요.");
            Assert.AreEqual("snake", back.Room.Course);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator ClosingThreeSlots_GivesFourBots_FillOff_RacesAlone()
        {
            yield return LoadMenu();
            var menu = MainMenu.Instance;
            menu.ShowEventEntry();
            var room = menu.EventEntry;
            room.CloseSlot(5); room.CloseSlot(6); room.CloseSlot(7);
            room.CloseSlot(0);   // my own seat: refused
            Assert.IsFalse(room.Room.IsClosed(0));
            Assert.AreEqual(4, room.Room.BotCount);
            yield return Race(room);
            Assert.AreEqual(4, RaceEvent.Starting.Bots);
            Assert.AreEqual(5, RaceDirector.Current.CarCount, "me + 4 bots");

            yield return BackFromRace();
            room = MainMenu.Instance.EventEntry;
            Assert.IsTrue(room.Room.IsClosed(5) && room.Room.IsClosed(6) && room.Room.IsClosed(7), "closed slots kept (PlayerPrefs)");
            Assert.IsTrue(room.Room.SetSettings(room.Me, room.Room.Name, false, false));
            Assert.AreEqual(0, room.Room.BotCount);
            yield return Race(room);
            Assert.AreEqual(0, RaceEvent.Starting.Bots);
            Assert.AreEqual(1, RaceDirector.Current.CarCount, "a race alone");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Dialogs_EscCloses_LeaveConfirmGoesHome_F5WaitsForDialogs()
        {
            yield return LoadMenu();
            var menu = MainMenu.Instance;
            menu.ShowEventEntry();
            var room = menu.EventEntry;
            foreach (var d in new[] { "garage", "course", "invite", "settings" })
            {
                room.OpenDialog(d);
                Assert.AreEqual(d, room.Dialog);
                room.Escape();
                Assert.IsNull(room.Dialog);
            }
            room.OpenDialog("course");
            room.Go();   // F5 with a dialog open does nothing
            Assert.IsFalse(menu.IsLoading);
            room.CloseDialog();
            room.Escape();   // no dialog: Esc asks to leave
            Assert.AreEqual("leave", room.Dialog);
            room.CloseDialog();   // 남기
            Assert.IsTrue(menu.EventEntryShown);
            room.OpenDialog("leave");
            room.ConfirmLeave();   // 나가기
            Assert.IsTrue(menu.LobbyHomeShown);
            Assert.IsFalse(menu.EventEntryShown);
        }

        // Review shots of the room (#511) against docs/research/ui_mockups/race_room.html:
        //   RACE_SHOTS=Logs/race_room tools/unity.sh test playmode Game.Tests.RaceEventMenuTests.CaptureEventScreen
        [UnityTest, Explicit, Category("Capture"), Timeout(120000)]
        public IEnumerator CaptureEventScreen()
        {
            string folder = System.Environment.GetEnvironmentVariable("RACE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/race_room";
            Directory.CreateDirectory(folder);
            yield return LoadMenu();
            var menu = MainMenu.Instance;
            menu.ShowEventEntry();
            var room = menu.EventEntry;
            yield return new WaitForSeconds(0.5f);
            yield return UiShot.Save(Path.Combine(folder, "01_room_solo.png"), 1920, 1080);
            room.CloseSlot(7);
            room.Room.Join(new RaceLobbyMember { Id = 902, Name = "민트초코", Level = 12, Car = "Shvan92", Paint = 2 });
            room.Room.Join(new RaceLobbyMember { Id = 903, Name = "도로위의사자", Level = 7, Car = "FairheavenLT80", Paint = 1 });
            room.Room.ToggleReady(902);
            room.Refresh();
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "02_room_party_states.png"), 1920, 1080);
            room.Room.SetSettings(room.Me, room.Room.Name, true, false);
            room.Room.SetCourse(room.Me, "pass_up", 1);
            room.Refresh();
            room.Go();   // the host with someone not ready: the toast names them
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "03_room_sprint_fill_off_toast.png"), 1920, 1080);
            room.OpenDialog("leave");
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "04_leave_confirm.png"), 1920, 1080);
            room.OpenDialog("garage");
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "05_garage_todo.png"), 1920, 1080);
            room.CloseDialog();
            foreach (var (w, h) in new[] { (1440, 900), (2560, 1080) })
                yield return UiShot.Save(Path.Combine(folder, $"06_room_{w}x{h}.png"), w, h);
        }
    }
}

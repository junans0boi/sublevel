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
using UnityEngine.UI;

namespace Game.Tests
{
    // #514 (garage) and #515 (course, laps, room settings) on the race room screen (spec #505, ADR-562), against the mockup
    // docs/research/ui_mockups/race_room.html.
    public class RaceRoomDialogTests
    {
        static IEnumerator OpenRoom()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
            MainMenu.Instance.ShowEventEntry();
            yield return null;
        }

        // The portrait is not CPU-readable (its pixels live on the GPU only): copy it back through a render texture.
        static Color32[] Pixels(Texture2D t)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            Object.Destroy(copy);
            return px;
        }

        static RaceLobbyScreen Room => MainMenu.Instance.EventEntry;

        [SetUp]
        public void SetUp() => PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
            PlayerPrefs.DeleteKey(RaceLobbyScreen.PrefsKey);
            RaceLobbyScreen.ReturnPending = false;
            RaceEvent.Starting = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
        }

        static bool Shows(string text) => Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text == text);
        static Button Button(string name) => GameObject.Find(name).GetComponent<Button>();

        // Me as a guest of host 900, with friend 902 waiting.
        static void BeGuest(RaceLobbyScreen room)
        {
            var s = room.Room.Snapshot();
            s.Host = 900;
            s.Members.Insert(0, new RaceLobbyMember { Id = 900, Name = "방장님", Level = 5, Car = "GoblinV12" });
            room.Room.Load(s);
            room.Refresh();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Garage_Shvan92Paint3_UpdatesMySlotBarAndNextRace()
        {
            yield return OpenRoom();
            var room = Room;
            room.OpenDialog("garage");
            Assert.AreEqual("garage", room.Dialog);
            room.PickCar(4);
            room.PickPaint(3);
            yield return null;
            Assert.IsTrue(Shows("느리지만 부딪혀도 안 밀림 · 계기판 <color=#4CD964>B 반원</color>"), "character line and cluster style");
            // The five bars are RaceStats of the car's race row.
            var s = RaceStats.Of(VehicleHandling.RaceFor("Shvan92"));
            int[] want = { s.Speed, s.Accel, s.Cornering, s.Grip, s.Durability };
            for (int i = 0; i < 5; i++)
            {
                float bar = ((RectTransform)GameObject.Find("Bar" + i).transform).sizeDelta.x, fill = ((RectTransform)GameObject.Find("Fill" + i).transform).sizeDelta.x;
                Assert.AreEqual(want[i] / 10f, fill / bar, 1e-3f, "bar " + i);
            }

            room.SaveCar();
            Assert.IsNull(room.Dialog);
            var me = room.Room.Member(room.Me);
            Assert.AreEqual("Shvan92", me.Car);
            Assert.AreEqual(3, me.Paint);
            CollectionAssert.Contains(room.Room.Lines, $"{me.Name} 님이 Shvan92을(를) 골랐습니다.");
            yield return null;
            Assert.IsTrue(GameObject.Find("MyCar").GetComponentsInChildren<Text>().Any(t => t.text == "Shvan92"), "the bottom bar shows the new car");
            StringAssert.Contains("Shvan92", PlayerPrefs.GetString(RaceLobbyScreen.PrefsKey), "kept as the last car");

            room.Go();
            Assert.AreEqual("Shvan92", RaceEvent.Starting.Racers[0].Car);
            Assert.AreEqual(RaceLobbyScreen.RaceTint[3], RaceEvent.Starting.Racers[0].Paint);
            Assert.IsTrue(VehicleColors.Valid(RaceEvent.Starting.Racers[0].Paint));
            yield return NetHarness.WaitUntil(() => RaceDirector.Current != null && Combatant.LocalPlayer != null, "the race scene", null, 60f);
        }

        // The room's car pictures are the race's own models (CarPortrait), rendered once per model and paint on a clear background.
        [UnityTest, Timeout(120000)]
        public IEnumerator CarPortraits_AreTheRaceModels_CachedPerModelAndPaint()
        {
            yield return OpenRoom();
            var tex = CarPortrait.Get("Shvan92", RaceLobbyScreen.RaceTint[3]);
            Assert.IsNotNull(tex, "the menu scene's LobbyStage carries the race car prefabs");
            Assert.AreSame(tex, CarPortrait.Get("Shvan92", RaceLobbyScreen.RaceTint[3]), "cached");
            Assert.AreNotSame(tex, CarPortrait.Get("Shvan92", RaceLobbyScreen.RaceTint[2]), "one per paint");
            var px = Pixels(tex);
            Assert.AreEqual(0, px[0].a, "transparent corner");
            int opaque = 0;
            foreach (var p in px) if (p.a > 200) opaque++;
            Assert.Greater(opaque, px.Length / 5, "the car fills the picture");
            // No portrait object left in the scene.
            Assert.IsNull(GameObject.Find("CarPortrait"));
            Assert.IsNull(GameObject.Find("CarPortraitCamera"));
            foreach (var (id, _, _, _) in RaceLobbyScreen.Cars) Assert.IsNotNull(CarPortrait.Get(id, VehicleColors.None), id);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ReadyGuest_GarageLocked_CourseAndSettingsHostOnly()
        {
            yield return OpenRoom();
            var room = Room;
            BeGuest(room);
            yield return null;   // the redraw destroys the old stage at the end of the frame
            Assert.IsTrue(Button("Bar차고").interactable, "a waiting guest may change cars");
            Assert.IsFalse(Button("Bar코스").interactable);
            Assert.IsFalse(Button("Settings").interactable);
            Assert.IsTrue(Shows("코스·방 설정: 방장만"));
            room.OpenDialog("course");
            Assert.IsNull(room.Dialog);
            room.OpenDialog("settings");
            Assert.IsNull(room.Dialog);

            room.Go();   // a guest's big button: ready
            Assert.IsTrue(room.Room.Member(room.Me).Ready);
            yield return null;
            Assert.IsFalse(Button("Bar차고").interactable);
            Assert.IsTrue(Shows("준비를 취소해야 차를 바꿀 수 있습니다"));
            room.OpenDialog("garage");
            Assert.IsNull(room.Dialog);
            Assert.AreEqual("준비를 취소해야 차를 바꿀 수 있습니다.", room.ToastText);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Course_LoopAndLaps_UpdateTopBarAndCard_AndUnreadyGuests()
        {
            yield return OpenRoom();
            var room = Room;
            room.Room.Join(new RaceLobbyMember { Id = 902, Name = "민트초코", Car = "Shvan92" });
            room.Room.ToggleReady(902);
            room.OpenDialog("course");
            Assert.AreEqual(0, room.CourseDraft);
            Assert.AreEqual(2, room.LapsDraft);
            room.PickCourse(1);   // a sprint: no laps, the note
            yield return null;
            Assert.IsNull(GameObject.Find("Laps1"));
            Assert.IsTrue(Shows("스프린트: 출발점에서 결승점까지 한 번"));
            room.PickCourse(3);
            room.PickLaps(5);
            room.PickLaps(4);   // not a choice
            Assert.AreEqual(5, room.LapsDraft);
            room.SaveCourse();
            Assert.IsNull(room.Dialog);
            Assert.AreEqual("pass_loop", room.Room.Course);
            Assert.AreEqual(5, room.Room.Laps);
            Assert.IsFalse(room.Room.Member(902).Ready, "a course change unreadies everyone");
            CollectionAssert.Contains(room.Room.Lines, "코스가 고개 순환 5랩(으)로 바뀌어 모두 다시 준비해야 합니다.");
            yield return null;
            Assert.IsTrue(Shows("5랩"), "the top bar's laps pill");
            Assert.IsTrue(Shows("고개 순환"), "the course card");

            room.OpenDialog("course");
            room.PickCourse(2);
            room.SaveCourse();
            Assert.AreEqual("pass_down", room.Room.Course);
            Assert.AreEqual(1, room.Room.Laps, "a sprint is one run");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Settings_EmptyNameShowsInlineError_DoesNotSave_ThenSaves()
        {
            yield return OpenRoom();
            var room = Room;
            string before = room.Room.Name;
            room.OpenDialog("settings");
            yield return null;
            var input = Object.FindAnyObjectByType<InputField>();
            Assert.AreEqual(RaceLobby.NameMax, input.characterLimit);
            Assert.AreEqual(before, input.text);
            input.text = "   ";
            room.SaveSettings();
            Assert.AreEqual("settings", room.Dialog, "not saved");
            Assert.AreEqual("방 이름을 입력하세요.", room.SettingsError);
            Assert.AreEqual(before, room.Room.Name);
            yield return null;
            Assert.IsTrue(Shows("방 이름을 입력하세요."));
            input = Object.FindAnyObjectByType<InputField>();
            input.text = "  금요 레이스 ";   // typing clears the error
            Assert.IsNull(room.SettingsError);
            Assert.IsFalse(Shows("방 이름을 입력하세요."));
            room.SetPrivate(true);
            room.SetFillBots(false);
            room.SaveSettings();
            Assert.IsNull(room.Dialog);
            Assert.AreEqual("금요 레이스", room.Room.Name);
            Assert.IsTrue(room.Room.Private);
            Assert.AreEqual(0, room.Room.BotCount);
            CollectionAssert.Contains(room.Room.Lines, "방 설정을 바꿨습니다.");
        }

        // Review shots of the dialogs against the mockup:
        //   RACE_SHOTS=Logs/race_room tools/unity.sh test playmode Game.Tests.RaceRoomDialogTests.CaptureDialogs
        [UnityTest, Explicit, Category("Capture"), Timeout(120000)]
        public IEnumerator CaptureDialogs()
        {
            string folder = System.Environment.GetEnvironmentVariable("RACE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/race_room";
            Directory.CreateDirectory(folder);
            yield return OpenRoom();
            var room = Room;
            yield return new WaitForSeconds(0.5f);
            room.OpenDialog("garage");
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "10_garage.png"), 1920, 1080);
            room.PickCar(4);
            room.PickPaint(3);
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "11_garage_shvan92_paint3.png"), 1920, 1080);
            room.SaveCar();
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "12_room_after_garage.png"), 1920, 1080);
            room.OpenDialog("course");
            room.PickCourse(3);
            room.PickLaps(3);
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "13_course_loop_laps.png"), 1920, 1080);
            room.PickCourse(1);
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "14_course_sprint.png"), 1920, 1080);
            room.PickCourse(3);
            room.SaveCourse();
            yield return null;
            room.OpenDialog("settings");
            Object.FindAnyObjectByType<InputField>().text = "";
            room.SaveSettings();
            room.SetFillBots(false);
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "15_settings_empty_name.png"), 1920, 1080);
            room.CloseDialog();
            room.Room.Join(new RaceLobbyMember { Id = 902, Name = "민트초코", Level = 12, Car = "GoblinV12", Paint = 0 });
            room.Room.Join(new RaceLobbyMember { Id = 903, Name = "도로위의사자", Level = 7, Car = "FairheavenSW84", Paint = 2 });
            room.Room.ToggleReady(902);
            room.Refresh();
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "16_room_course_changed.png"), 1920, 1080);
            BeGuest(room);
            room.Go();   // ready: garage locked
            yield return null;
            yield return UiShot.Save(Path.Combine(folder, "17_guest_ready_locked.png"), 1920, 1080);
        }
    }
}

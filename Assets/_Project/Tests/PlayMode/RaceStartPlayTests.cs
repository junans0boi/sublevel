using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #508 (ADR-562): the solo path (MainMenu.StartEvent) hands the race scene its start config: the laps, the bot count and the player's
    // car and paint.
    public class RaceStartPlayTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MoveGuard.TrustTeleports = false;
            PlayerController.InputOverride = null;
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
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

        [UnityTest, Timeout(600000)]
        public IEnumerator Solo_ThreeLaps_FourBots_Shvan92InPaint2()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return Until(() => MainMenu.Instance != null, 20f, "main menu");
            MoveGuard.TrustTeleports = true;
            PlayerController.InputOverride = new NoPlayerInput();
            var course = RaceEvent.ById("snake");
            MainMenu.Instance.StartEvent(course, laps: 3, bots: 4, car: "Shvan92", paint: 2);
            yield return Until(() => RaceDirector.Current != null && Combatant.LocalPlayer != null, 60f, "the race scene and the player");

            var me = Combatant.LocalPlayer;
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 25f, "the race is on");
            yield return new WaitForSeconds(0.5f);
            var car = VehicleSeat.VehicleOf(me);
            Assert.IsNotNull(car, "at the wheel");
            Assert.AreEqual("Shvan92", car.ModelName, "my chosen car");
            Assert.AreEqual(2, car.PaintIndex.Value, "my chosen paint");
            var m = RaceHud.Last;
            Assert.AreEqual(5, m.Keys.Length, "me and 4 bots");
            Assert.AreEqual(5, RaceDirector.Current.CarCount, "5 cars on the grid");
            Assert.AreEqual(3, m.Laps, "the race's laps");

            // Round the course as in RaceEventPlayTests.TwoLaps: not done after two laps, done after three.
            var c = course.Course;
            float lapSeconds = c.MinLapSeconds + 3f, speed = c.LapLength / lapSeconds;
            float s = c.Project(car.transform.position.x, car.transform.position.z).s - c.LapLength;
            bool checkedTwo = false;
            while (s < c.LapLength * 3 + 5f)
            {
                s += speed * Time.deltaTime;
                var (x, z, yaw) = c.PointAt(s);
                car.Place(new Vector3(x, 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                if (!checkedTwo && s > c.LapLength * 2 + 30f)
                {
                    checkedTwo = true;
                    var two = RaceHud.Last;
                    Assert.AreEqual(2, two.LapsDone[0], "two laps done");
                    Assert.IsFalse(two.Done[0], "not finished after two of three laps");
                }
                yield return null;
            }
            Assert.IsTrue(checkedTwo);
            yield return Until(() => RaceHud.Phase == RacePhase.Finishing || RaceHud.Phase == RacePhase.Results, 5f, "finished after three laps");
            var r = RaceHud.Last;
            Assert.AreEqual(me.ObjectId, r.Keys[0]);
            Assert.IsTrue(r.Done[0]);
            Assert.AreEqual(3, r.LapsDone[0]);
        }
    }
}

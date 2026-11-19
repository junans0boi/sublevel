using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #417 (ADR-384): waiting bots arrive a few at a time, never all, never past the room.
    public class WaitingBotScheduleTests
    {
        [Test] public void NobodyThere_NoBots() => Assert.AreEqual(0, WaitingBotSchedule.BotsAt(30f, 90f, 78, 0, 80));

        [Test] public void AtTheStart_AFew() => Assert.AreEqual(3, WaitingBotSchedule.BotsAt(0f, 90f, 78, 2, 80));

        [Test]
        public void GrowsWithTheClock_Monotonic()
        {
            int prev = -1;
            for (float t = 0f; t <= 90f; t += 5f)
            {
                int n = WaitingBotSchedule.BotsAt(t, 90f, 78, 2, 80);
                Assert.GreaterOrEqual(n, prev); prev = n;
            }
        }

        [Test]
        public void AtTheEnd_ShortOfAll_LeavingTheRestForTheFill()
        {
            int n = WaitingBotSchedule.BotsAt(90f, 90f, 78, 2, 80);
            Assert.Less(n, 78);
            Assert.AreEqual((int)System.Math.Floor(78 * WaitingBotSchedule.FinalShare), n);
        }

        [Test]
        public void SlowAtFirst_HalfwayHasLessThanHalfOfTheFinalCount()
        {
            int half = WaitingBotSchedule.BotsAt(45f, 90f, 78, 2, 80), end = WaitingBotSchedule.BotsAt(90f, 90f, 78, 2, 80);
            Assert.Less(half, end / 2 + 1);
        }

        [Test]
        public void NeverMoreThanTheRoomOrWhatTheMatchNeeds()
        {
            Assert.AreEqual(13, WaitingBotSchedule.BotsAt(90f, 90f, 16, 60, 80), "16 needed, 85% = 13, the room holds 20");
            Assert.LessOrEqual(WaitingBotSchedule.BotsAt(90f, 90f, 200, 70, 80), 10, "the room: 80 - 70 people");
            Assert.AreEqual(2, WaitingBotSchedule.BotsAt(0f, 90f, 2, 5, 80), "fewer needed than the opening number");
        }

        [Test] public void NoTimeLimit_IsTreatedAsTheEnd() => Assert.AreEqual((int)System.Math.Floor(10 * WaitingBotSchedule.FinalShare), WaitingBotSchedule.BotsAt(5f, 0f, 10, 1, 80));
    }
}

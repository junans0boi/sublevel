using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #422 (ADR-502): the board's rotation and rankings as pure rules — the same clock gives every client the same page.
    public class WaitingBoardTests
    {
        [Test]
        public void Rank_HighestFirst_TiesGoToWhoGotThereFirst()
        {
            var r = new RoomRecords();
            r.Add(10, "A", 3);           // A reaches 3 first
            r.Add(11, "B", 1);
            r.Add(12, "C", 2); r.Add(12, "C", 1);   // C reaches 3 later
            r.Add(13, "D", 5);
            var top = r.Top(3);
            CollectionAssert.AreEqual(new[] { "D", "A", "C" }, top.ConvertAll(x => x.Name));
            CollectionAssert.AreEqual(new[] { 5, 3, 3 }, top.ConvertAll(x => x.Score));
        }

        [Test]
        public void Rank_EmptyList_AndFewerThanThree()
        {
            Assert.AreEqual(0, new RoomRecords().Top(3).Count);
            Assert.AreEqual(0, RoomRecords.Rank(new List<RecordEntry>(), 3).Count);
            var r = new RoomRecords();
            r.Add(1, "A", 0);            // nothing scored: not on the board
            r.Add(2, "B", 1);
            Assert.AreEqual(1, r.Top(3).Count);
            Assert.AreEqual(0, r.Top(0).Count);
        }

        [Test]
        public void Rank_KeepsTheLastNameGiven_AndCountsVersion()
        {
            var r = new RoomRecords();
            int v0 = r.Version;
            r.Add(1, "", 1);
            r.Add(1, "하늘", 1);
            Assert.AreEqual("하늘", r.Top(1)[0].Name);
            Assert.Greater(r.Version, v0, "changes are visible to whoever sends them");
        }

        static IslandRules Rules() => new IslandRules
        {
            BoardPageSeconds = 5f, BoardFinalSeconds = 10f,
            BoardPages = new[] { "Countdown", "BungeeTop", "RangeTop", "Regions", "Tip" },
            Tips = new[] { "팁 하나", "팁 둘" },
        };

        // 100 s left is a whole number of turns (5 pages x 5 s), so the rotation's first page starts there.
        static float Left(float runFor) => 100f - runFor;

        [Test]
        public void Rotation_StepsThroughThePages_OnThePeriod()
        {
            var r = Rules();
            var seen = new List<BoardPage>();
            for (int i = 0; i < 6; i++) seen.Add(BoardRotation.PageAt(Left(i * 5f + 0.1f), r, out _));
            CollectionAssert.AreEqual(new[] { BoardPage.Countdown, BoardPage.BungeeTop, BoardPage.RangeTop, BoardPage.Regions, BoardPage.Tip, BoardPage.Countdown }, seen);
            Assert.AreEqual(BoardPage.Countdown, BoardRotation.PageAt(Left(4.99f), r, out _), "a page holds for its whole period");
        }

        [Test]
        public void Rotation_TheLastSeconds_AlwaysShowTheCountdown()
        {
            var r = Rules();
            Assert.AreEqual(BoardPage.Countdown, BoardRotation.PageAt(9.5f, r, out _), "9.5 s left would be the regions page");
            Assert.AreEqual(BoardPage.RangeTop, BoardRotation.PageAt(14.5f, r, out _), "before the last seconds the rotation runs");
        }

        [Test]
        public void Rotation_TipChangesEachCycle()
        {
            var r = Rules();
            BoardRotation.PageAt(Left(20.5f), r, out int c0);         // a turn's tip page
            BoardRotation.PageAt(Left(45.5f), r, out int c1);         // the next turn's
            Assert.AreEqual(BoardPage.Tip, BoardRotation.PageAt(Left(20.5f), r, out _));
            Assert.AreEqual(c0 + 1, c1);
            Assert.AreNotEqual(BoardRotation.TipAt(c0, r.Tips), BoardRotation.TipAt(c1, r.Tips));
            Assert.AreEqual(BoardRotation.TipAt(c0, r.Tips), BoardRotation.TipAt(c1 + 1, r.Tips));
            Assert.AreEqual("", BoardRotation.TipAt(3, new string[0]));
            Assert.AreEqual("", BoardRotation.TipAt(3, null));
        }

        [Test]
        public void Rotation_ContentsAreData_UnknownPagesSkipped_EmptyMeansCountdown()
        {
            var r = new IslandRules { BoardPageSeconds = 4f, BoardPages = new[] { "RangeTop", "Nonsense", "Tip" } };
            Assert.AreEqual(BoardPage.RangeTop, BoardRotation.PageAt(80f - 1f, r, out _));
            Assert.AreEqual(BoardPage.Tip, BoardRotation.PageAt(80f - 5f, r, out _));
            Assert.AreEqual(BoardPage.RangeTop, BoardRotation.PageAt(80f - 9f, r, out _));
            Assert.AreEqual(BoardPage.Countdown, BoardRotation.PageAt(50f, new IslandRules { BoardPages = new string[0] }, out _));
            Assert.AreEqual(BoardPage.Countdown, BoardRotation.PageAt(50f, new IslandRules { BoardPages = null }, out _));
            Assert.AreEqual(BoardPage.Countdown, BoardRotation.PageAt(float.NaN, Rules(), out _), "no countdown yet");
        }

        [Test]
        public void Board_TopPage_ListsRanks_OrSaysNothingYet()
        {
            var lines = new string[BoardText.Lines];
            var view = new BoardView { SecondsLeft = 47f, Total = 38, Target = 80, RangeTop = new List<RecordRow> { new RecordRow { Name = "하늘", Score = 12 }, new RecordRow { Name = "루나", Score = 7 } } };
            BoardText.Compose(BoardPage.RangeTop, view, lines);
            StringAssert.Contains("사격", lines[BoardText.Title]);
            StringAssert.Contains("하늘", lines[BoardText.Row0]); StringAssert.Contains("12", lines[BoardText.Row0]);
            StringAssert.Contains("루나", lines[BoardText.Row0 + 1]);
            Assert.AreEqual("", lines[BoardText.Row0 + 2]);
            StringAssert.Contains("0:47", lines[BoardText.Footer], "the countdown stays in sight on every page");
            BoardText.Compose(BoardPage.BungeeTop, view, lines);
            StringAssert.Contains("번지", lines[BoardText.Title]);
            StringAssert.Contains("기록", lines[BoardText.Row0 + 1], "an empty ranking says so");
        }

        [Test]
        public void Board_CountdownRegionsAndTip_Pages()
        {
            var lines = new string[BoardText.Lines];
            var view = new BoardView { SecondsLeft = 75.2f, Total = 38, Target = 80, BungeeBest = 19f, Regions = new[] { "산악", "습지", "마을", "공장" }, Tip = "첫 줄\n둘째 줄" };
            BoardText.Compose(BoardPage.Countdown, view, lines);
            Assert.AreEqual("1:16", lines[BoardText.Big]);
            StringAssert.Contains("38 / 80", lines[BoardText.Row0 + 2]);
            StringAssert.Contains("19.0", lines[BoardText.Footer]);
            BoardText.Compose(BoardPage.Regions, view, lines);
            StringAssert.Contains("산악", lines[BoardText.Row0]); StringAssert.Contains("공장", lines[BoardText.Row0 + 1]);
            BoardText.Compose(BoardPage.Tip, view, lines);
            Assert.AreEqual("첫 줄", lines[BoardText.Row0]);
            Assert.AreEqual("둘째 줄", lines[BoardText.Row0 + 1]);
            view.BungeeBest = 0f;
            BoardText.Compose(BoardPage.Countdown, view, lines);
            Assert.AreEqual("", lines[BoardText.Footer], "no bungee yet: no line for it");
        }

        [Test]
        public void ShippedRules_HaveKoreanTips_AndTheSixPages()
        {
            var r = new IslandRules();
            Assert.GreaterOrEqual(r.Tips.Length, 5);
            foreach (var t in r.Tips) Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(t, "[가-힣]"), "Korean: " + t);
            Assert.AreEqual(6, BoardRotation.Parse(r.BoardPages).Length);   // + the race hall's LAP TOP3 (ADR-509)
        }
    }
}

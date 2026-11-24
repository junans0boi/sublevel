using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // The race room's rules (#507, spec #505, ADR-562), mirroring the approved mockup's act()/goState().
    public class RaceLobbyTests
    {
        static RaceLobbyMember M(int id, string n) => new RaceLobbyMember { Id = id, Name = n, Level = id, Car = "LT80", Paint = 0 };

        static RaceLobby Room()
        {
            var r = new RaceLobby(M(1, "준환"));
            r.Join(M(2, "민트초코")); r.Join(M(3, "한강라이더"));
            r.Lines.Clear();
            return r;
        }

        [Test]
        public void NewRoom_HasMockupDefaults_AndHostInSlot0()
        {
            var r = new RaceLobby(M(1, "준환"));
            Assert.AreEqual("준환의 레이스", r.Name);
            Assert.AreEqual("snake", r.Course);
            Assert.AreEqual(2, r.Laps);
            Assert.IsTrue(r.FillBots);
            Assert.IsFalse(r.Private);
            Assert.AreEqual(1, r.HostId);
            Assert.AreEqual(1, r.SeatAt(0).Id);
            Assert.AreEqual(8, r.Capacity);
            Assert.AreEqual(7, r.BotCount);
            Assert.IsFalse(r.IsMulti);
        }

        [Test]
        public void Solo_StartsAtOnce_WithBotsLabel()
        {
            var g = new RaceLobby(M(1, "준환")).GoFor(1);
            Assert.IsTrue(g.Start);
            Assert.IsTrue(g.Enabled);
            Assert.AreEqual("시작 (F5)", g.Label);
            Assert.AreEqual("나머지는 봇으로 출발", g.Sub);
        }

        [Test]
        public void Host_StartsOnlyWhenEveryOtherHumanIsReady_AndNamesTheWaiting()
        {
            var r = Room();
            var g = r.GoFor(1);
            Assert.IsFalse(g.Enabled);
            Assert.AreEqual("시작", g.Label);
            Assert.AreEqual("준비 안 한 사람 2명", g.Sub);
            CollectionAssert.AreEqual(new[] { "민트초코", "한강라이더" }, g.NotReady);
            Assert.IsTrue(r.ToggleReady(2));
            CollectionAssert.AreEqual(new[] { "한강라이더" }, r.GoFor(1).NotReady);
            Assert.IsTrue(r.ToggleReady(3));
            g = r.GoFor(1);
            Assert.IsTrue(g.Enabled);
            Assert.AreEqual("모두 준비 완료", g.Sub);
            Assert.IsTrue(r.IsMulti);
        }

        [Test]
        public void Guest_ReadyButtonToggles_HostHasNoReady()
        {
            var r = Room();
            var g = r.GoFor(2);
            Assert.IsFalse(g.Start);
            Assert.AreEqual("준비 (F5)", g.Label);
            r.ToggleReady(2);
            Assert.AreEqual("준비 취소", r.GoFor(2).Label);
            Assert.AreEqual("방장이 시작하면 출발", r.GoFor(2).Sub);
            r.ToggleReady(2);
            Assert.IsFalse(r.Member(2).Ready);
            Assert.IsFalse(r.ToggleReady(1));
            Assert.IsFalse(r.ToggleReady(99));
        }

        [Test]
        public void Car_IsLockedWhileReady_AndChoiceIsAnnounced()
        {
            var r = Room();
            r.ToggleReady(2);
            Assert.IsFalse(r.SetCar(2, "GoblinV12", 1));
            Assert.AreEqual("LT80", r.Member(2).Car);
            r.ToggleReady(2);
            r.Lines.Clear();
            Assert.IsTrue(r.SetCar(2, "GoblinV12", 1));
            Assert.AreEqual(1, r.Member(2).Paint);
            CollectionAssert.AreEqual(new[] { "민트초코 님이 GoblinV12을(를) 골랐습니다." }, r.Lines);
            Assert.IsFalse(r.SetCar(2, "GoblinV12", 6));
            Assert.IsFalse(r.SetCar(2, "", 0));
            Assert.IsTrue(r.SetCar(1, "SW84", 5)); // the host never readies, so never locked
        }

        [Test]
        public void CarName_FormatsTheLine()
        {
            var r = Room();
            r.CarName = id => id == "LT80" ? "Fairheaven LT80" : id;
            r.SetCar(3, "LT80", 2);
            CollectionAssert.AreEqual(new[] { "한강라이더 님이 Fairheaven LT80을(를) 골랐습니다." }, r.Lines);
        }

        [Test]
        public void CourseChange_UnreadiesEveryone_HostOnly()
        {
            var r = Room();
            r.ToggleReady(2); r.ToggleReady(3);
            r.Lines.Clear();
            Assert.IsFalse(r.SetCourse(2, "pass_loop", 3));
            Assert.IsTrue(r.SetCourse(1, "pass_loop", 3));
            Assert.AreEqual("pass_loop", r.Course);
            Assert.AreEqual(3, r.Laps);
            Assert.IsFalse(r.Member(2).Ready);
            Assert.IsFalse(r.Member(3).Ready);
            CollectionAssert.AreEqual(new[] { "코스가 고개 순환 3랩(으)로 바뀌어 모두 다시 준비해야 합니다." }, r.Lines);
        }

        [Test]
        public void LapsChangeAlone_AlsoUnreadies_SameCourseAndLapsDoesNot()
        {
            var r = Room();
            r.ToggleReady(2);
            Assert.IsTrue(r.SetCourse(1, "snake", 2));
            Assert.IsTrue(r.Member(2).Ready);
            Assert.IsTrue(r.SetCourse(1, "snake", 5));
            Assert.IsFalse(r.Member(2).Ready);
        }

        [Test]
        public void Laps_AreOneTwoThreeFive_AndSprintsForceOne()
        {
            var r = Room();
            Assert.IsFalse(r.SetCourse(1, "snake", 4));
            Assert.IsFalse(r.SetCourse(1, "snake", 0));
            Assert.IsFalse(r.SetCourse(1, "nowhere", 1));
            Assert.IsTrue(r.SetCourse(1, "pass_up", 3));
            Assert.AreEqual(1, r.Laps);
            Assert.IsFalse(r.IsLoop);
            r.Lines.Clear();
            Assert.IsTrue(r.SetCourse(1, "pass_down", 2));
            CollectionAssert.AreEqual(new[] { "코스가 고개 다운힐(으)로 바뀌어 모두 다시 준비해야 합니다." }, r.Lines);
        }

        [Test]
        public void Settings_HostOnly_NameTrimmedOneToTwenty()
        {
            var r = Room();
            Assert.IsFalse(r.SetSettings(2, "x", false, true));
            Assert.IsFalse(r.SetSettings(1, "   ", false, true));
            Assert.IsFalse(r.SetSettings(1, new string('가', 21), false, true));
            Assert.IsTrue(r.SetSettings(1, "  금요 레이스  ", true, false));
            Assert.AreEqual("금요 레이스", r.Name);
            Assert.IsTrue(r.Private);
            Assert.IsFalse(r.FillBots);
            Assert.AreEqual(0, r.BotCount);
            CollectionAssert.AreEqual(new[] { "방 설정을 바꿨습니다." }, r.Lines);
            Assert.IsTrue(r.SetSettings(1, new string('가', 20), true, false));
        }

        [Test]
        public void Slots_CloseAndOpen_HostOnly_OccupiedCannotClose()
        {
            var r = Room();
            Assert.IsFalse(r.CloseSlot(2, 7));
            Assert.IsFalse(r.CloseSlot(1, 1)); // 민트초코 sits there
            Assert.IsFalse(r.CloseSlot(1, 8));
            Assert.IsTrue(r.CloseSlot(1, 7));
            Assert.IsTrue(r.CloseSlot(1, 6));
            Assert.IsFalse(r.CloseSlot(1, 6));
            Assert.IsTrue(r.IsClosed(7));
            Assert.AreEqual(6, r.Capacity);
            Assert.AreEqual(3, r.BotCount);
            Assert.IsFalse(r.OpenSlot(2, 7));
            Assert.IsTrue(r.OpenSlot(1, 7));
            Assert.IsFalse(r.OpenSlot(1, 7));
            Assert.AreEqual(7, r.Capacity);
        }

        [Test]
        public void Join_FillsNonClosedSlotsInOrder_RefusedWhenFull()
        {
            var r = new RaceLobby(M(1, "준환"));
            r.CloseSlot(1, 1);
            Assert.IsTrue(r.Join(M(2, "민트초코")));
            Assert.IsNull(r.SeatAt(1));
            Assert.AreEqual(2, r.SeatAt(2).Id);
            Assert.IsFalse(r.Join(M(2, "민트초코")));
            for (int i = 3; i <= 7; i++) Assert.IsTrue(r.Join(M(i, "p" + i)));
            Assert.AreEqual(7, r.Capacity);
            Assert.AreEqual(0, r.BotCount);
            Assert.IsFalse(r.Join(M(8, "p8")));
            Assert.IsFalse(r.Member(7).Ready);
        }

        [Test]
        public void Kick_HostOnly_FreesSlot_AndSaysSo()
        {
            var r = Room();
            Assert.IsFalse(r.Kick(2, 3));
            Assert.IsFalse(r.Kick(1, 1));
            Assert.IsTrue(r.Kick(1, 2));
            Assert.IsNull(r.Member(2));
            Assert.AreEqual(3, r.SeatAt(1).Id);
            Assert.IsNull(r.SeatAt(2));
            Assert.AreEqual(6, r.BotCount);
            CollectionAssert.AreEqual(new[] { "민트초코 님을 내보냈습니다." }, r.Lines);
        }

        [Test]
        public void HostLeaving_PassesToLongestStandingMember()
        {
            var r = Room();
            Assert.IsTrue(r.Leave(1));
            Assert.AreEqual(2, r.HostId);
            Assert.AreEqual(2, r.SeatAt(0).Id);
            CollectionAssert.AreEqual(new[] { "준환 님이 나갔습니다.", "민트초코 님이 방장이 되었습니다." }, r.Lines);
            Assert.IsTrue(r.GoFor(2).Start);
            Assert.IsFalse(r.Leave(1));
        }

        [Test]
        public void NewHost_IsNotReady_SoStartWaitsOnlyForOthers()
        {
            var r = Room();
            r.ToggleReady(2);
            r.Leave(1);
            Assert.IsFalse(r.Member(2).Ready);
            CollectionAssert.AreEqual(new[] { "한강라이더" }, r.GoFor(2).NotReady);
        }

        [Test]
        public void BackToRoom_UnreadiesEveryone_KeepsSettings()
        {
            var r = Room();
            r.SetCourse(1, "pass_up", 1);
            r.CloseSlot(1, 7);
            r.ToggleReady(2); r.ToggleReady(3);
            r.Lines.Clear();
            r.BackToRoom();
            Assert.IsFalse(r.Members.Any(m => m.Ready));
            Assert.AreEqual("pass_up", r.Course);
            Assert.IsTrue(r.IsClosed(7));
            Assert.AreEqual(3, r.Members.Count);
            CollectionAssert.AreEqual(new[] { "레이스가 끝났습니다. 다시 준비해 주세요." }, r.Lines);
        }

        [Test]
        public void Load_FromSnapshot_EmitsLinesForTheDiff()
        {
            var r = Room();
            var s = r.Snapshot();
            s.Members.RemoveAt(2);                                         // 한강라이더 left
            s.Members.Add(M(4, "도로위의사자"));                            // joined
            s.Members[1].Car = "Shvan92";                                  // car chosen
            s.Course = "pass_loop"; s.Laps = 1;                            // course changed
            s.Name = "새 이름";                                             // settings changed
            r.Load(s);
            CollectionAssert.AreEqual(new[]
            {
                "도로위의사자 님이 들어왔습니다.",
                "한강라이더 님이 나갔습니다.",
                "민트초코 님이 Shvan92을(를) 골랐습니다.",
                "코스가 고개 순환 1랩(으)로 바뀌어 모두 다시 준비해야 합니다.",
                "방 설정을 바꿨습니다.",
            }, r.Lines);
            Assert.AreEqual(4, r.SeatAt(2).Id);
            Assert.AreEqual("새 이름", r.Name);
        }

        [Test]
        public void Snapshot_IsACopy_AndLoadingTheSameSaysNothing()
        {
            var r = Room();
            var s = r.Snapshot();
            s.Members[0].Name = "바뀜";
            Assert.AreEqual("준환", r.Member(1).Name);
            r.Load(r.Snapshot());
            Assert.IsEmpty(r.Lines);
        }
    }
}

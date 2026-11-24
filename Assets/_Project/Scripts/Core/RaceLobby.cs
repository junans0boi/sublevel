using System;
using System.Collections.Generic;

namespace Game.Core
{
    [Serializable]
    public sealed class RaceLobbyMember
    {
        public int Id;
        public string Name;
        public int Level;
        public string Car;    // the car's model id, e.g. "GoblinV12"
        public int Paint;     // 0..RaceLobby.Paints-1
        public bool Ready;    // the host never readies (implicitly ready)
        public RaceLobbyMember Copy() => (RaceLobbyMember)MemberwiseClone();
    }

    // The whole room as the backend party view carries it (spec #505): members in join order, the host's settings.
    [Serializable]
    public sealed class RaceLobbyState
    {
        public string Name, Course;
        public int Laps, Closed, Host;   // Closed: bit i = slot i closed
        public bool Private, FillBots;
        public List<RaceLobbyMember> Members = new List<RaceLobbyMember>();

        public RaceLobbyState Copy()
        {
            var s = (RaceLobbyState)MemberwiseClone();
            s.Members = Members.ConvertAll(m => m.Copy());
            return s;
        }
    }

    // What the one big button of the room shows (mockup goState()): the host's 시작, a guest's 준비 / 준비 취소.
    public struct RaceGo
    {
        public bool Start;          // host: pressing starts the race; guest: pressing toggles ready
        public bool Enabled;
        public string Label, Sub;
        public string[] NotReady;   // the host's waiting list (named in a toast when the disabled button is pressed)
    }

    // The race room (레이스 방, #507, ADR-562): 8 slots, a host, members' car/paint/ready, the course, laps, name, private, fillBots and
    // closed slots, with every rule of who may do what. Pure: fed locally (party of 1) or from the backend party (Load); the screen reads it
    // and calls its commands, each of which answers whether it was allowed. Changes leave system lines for the room's chat in Lines.
    // Named RaceLobby because Net/RaceRoom is the waiting island's hall.
    public sealed class RaceLobby
    {
        public const int Slots = 8;
        public const int Paints = 6;
        public const int NameMax = 20;
        public static readonly int[] LapChoices = { 1, 2, 3, 5 };

        RaceLobbyState _s;
        public readonly List<string> Lines = new List<string>();
        public Func<string, string> CarName = id => id;   // the garage's display name for a model id

        public RaceLobby(RaceLobbyMember host)
        {
            var h = host.Copy(); h.Ready = false;
            _s = new RaceLobbyState { Name = h.Name + "의 레이스", Course = "snake", Laps = RaceEvent.Laps, FillBots = true, Host = h.Id };
            _s.Members.Add(h);
        }

        public string Name => _s.Name;
        public string Course => _s.Course;
        public int Laps => _s.Laps;
        public bool Private => _s.Private;
        public bool FillBots => _s.FillBots;
        public int HostId => _s.Host;
        public IReadOnlyList<RaceLobbyMember> Members => _s.Members;
        public bool IsLoop => IsLoopCourse(_s.Course);
        public bool IsMulti => _s.Members.Count >= 2;
        public int Capacity => Slots - Bits(_s.Closed);
        public int BotCount => _s.FillBots ? Capacity - _s.Members.Count : 0;   // the open empty slots

        public static bool IsLoopCourse(string id) => id == "snake" || id == "pass_loop";
        public RaceLobbyMember Member(int id) => _s.Members.Find(m => m.Id == id);
        public bool IsClosed(int slot) => (_s.Closed >> slot & 1) != 0;

        // Members sit in the non-closed slots in join order, so empty open slots always come after them.
        public RaceLobbyMember SeatAt(int slot)
        {
            if (slot < 0 || slot >= Slots || IsClosed(slot)) return null;
            int n = 0;
            for (int i = 0; i < slot; i++) if (!IsClosed(i)) n++;
            return n < _s.Members.Count ? _s.Members[n] : null;
        }

        public RaceLobbyState Snapshot() => _s.Copy();

        // The backend's view replaces the room; the differences become chat lines (a member gone reads as left: kicks are told locally).
        public void Load(RaceLobbyState s) => Apply(() => _s = s.Copy());

        public bool Join(RaceLobbyMember m)
        {
            if (Member(m.Id) != null || _s.Members.Count >= Capacity) return false;
            var c = m.Copy(); c.Ready = false;
            return Apply(() => _s.Members.Add(c));
        }

        public bool Leave(int who)
        {
            var m = Member(who);
            if (m == null) return false;
            return Apply(() =>
            {
                _s.Members.Remove(m);
                if (who == _s.Host && _s.Members.Count > 0) { _s.Host = _s.Members[0].Id; _s.Members[0].Ready = false; }
            });
        }

        public bool Kick(int who, int target)
        {
            var m = Member(target);
            if (who != _s.Host || target == _s.Host || m == null) return false;
            return Apply(() => _s.Members.Remove(m), target);
        }

        public bool SetCar(int who, string car, int paint)
        {
            var m = Member(who);
            if (m == null || m.Ready || string.IsNullOrEmpty(car) || paint < 0 || paint >= Paints) return false;
            return Apply(() => { m.Car = car; m.Paint = paint; });
        }

        public bool ToggleReady(int who)
        {
            var m = Member(who);
            if (m == null || who == _s.Host) return false;
            m.Ready = !m.Ready;
            return true;
        }

        // Host only. A sprint is one run; a loop takes 1, 2, 3 or 5 laps. Any change unreadies everyone (backend does the same for parties).
        public bool SetCourse(int who, string course, int laps)
        {
            if (who != _s.Host || RaceEvent.ById(course) == null) return false;
            if (!IsLoopCourse(course)) laps = 1;
            else if (Array.IndexOf(LapChoices, laps) < 0) return false;
            return Apply(() =>
            {
                if (course == _s.Course && laps == _s.Laps) return;
                _s.Course = course; _s.Laps = laps;
                foreach (var m in _s.Members) m.Ready = false;
            });
        }

        public bool SetSettings(int who, string name, bool isPrivate, bool fillBots)
        {
            name = name?.Trim();
            if (who != _s.Host || string.IsNullOrEmpty(name) || name.Length > NameMax) return false;
            return Apply(() => { _s.Name = name; _s.Private = isPrivate; _s.FillBots = fillBots; });
        }

        public bool CloseSlot(int who, int slot)
        {
            if (who != _s.Host || slot < 0 || slot >= Slots || IsClosed(slot) || SeatAt(slot) != null) return false;
            _s.Closed |= 1 << slot;
            return true;
        }

        public bool OpenSlot(int who, int slot)
        {
            if (who != _s.Host || slot < 0 || slot >= Slots || !IsClosed(slot)) return false;
            _s.Closed &= ~(1 << slot);
            return true;
        }

        // The results' 방으로 돌아가기: everyone unready, course, laps, cars and slots kept.
        public void BackToRoom()
        {
            foreach (var m in _s.Members) m.Ready = false;
            Lines.Add("레이스가 끝났습니다. 다시 준비해 주세요.");
        }

        public RaceGo GoFor(int me)
        {
            if (me == _s.Host)
            {
                var wait = _s.Members.FindAll(m => m.Id != _s.Host && !m.Ready).ConvertAll(m => m.Name).ToArray();
                return wait.Length > 0
                    ? new RaceGo { Start = true, Label = "시작", Sub = $"준비 안 한 사람 {wait.Length}명", NotReady = wait }
                    : new RaceGo { Start = true, Enabled = true, Label = "시작 (F5)", Sub = IsMulti ? "모두 준비 완료" : "나머지는 봇으로 출발", NotReady = wait };
            }
            bool ready = Member(me)?.Ready ?? false;
            return new RaceGo { Enabled = true, Label = ready ? "준비 취소" : "준비 (F5)", Sub = ready ? "방장이 시작하면 출발" : "차를 고른 뒤 준비", NotReady = new string[0] };
        }

        // Runs a change and says what it changed. Always true, so commands can return it.
        bool Apply(Action change, int kicked = 0)
        {
            var b = _s.Copy();
            change();
            foreach (var m in _s.Members) if (!b.Members.Exists(o => o.Id == m.Id)) Lines.Add($"{m.Name} 님이 들어왔습니다.");
            foreach (var o in b.Members) if (Member(o.Id) == null) Lines.Add(o.Id == kicked ? $"{o.Name} 님을 내보냈습니다." : $"{o.Name} 님이 나갔습니다.");
            if (b.Host != _s.Host && Member(_s.Host) != null) Lines.Add($"{Member(_s.Host).Name} 님이 방장이 되었습니다.");
            foreach (var o in b.Members)
            {
                var m = Member(o.Id);
                if (m != null && (m.Car != o.Car || m.Paint != o.Paint)) Lines.Add($"{m.Name} 님이 {CarName(m.Car)}을(를) 골랐습니다.");
            }
            if (b.Course != _s.Course || b.Laps != _s.Laps)
            {
                var name = RaceEvent.ById(_s.Course)?.Name ?? _s.Course;
                Lines.Add($"코스가 {(IsLoop ? $"{name} {_s.Laps}랩" : name)}(으)로 바뀌어 모두 다시 준비해야 합니다.");
            }
            if (b.Name != _s.Name || b.Private != _s.Private || b.FillBots != _s.FillBots) Lines.Add("방 설정을 바꿨습니다.");
            return true;
        }

        static int Bits(int v) { int n = 0; for (; v != 0; v &= v - 1) n++; return n; }
    }
}

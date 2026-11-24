using System;
using Game.Core;

namespace Game.Net
{
    // The race room on the backend party (#519, spec #505, ADR-562 (2)/(3)). With a party of 2+ the room (RaceLobby) is read from the
    // polled party view and every command goes through the backend: shown at once, put back with a toast when the backend refuses. A party
    // of 1, no party or no login keeps the local room (commands run on RaceLobby only). Poll differences become the room's system lines
    // (RaceLobby.Load). Call Sync every frame after SocialClient.Tick; it answers whether the room needs a redraw.
    public sealed class RaceParty
    {
        // The garage's model ids (RaceLobbyScreen.Cars) and the backend's car ids (RACE_CARS), same order.
        static readonly string[] Models = { "GoblinV12", "FairheavenLT80", "FairheavenSW84", "LightbodyLifted85", "Shvan92" };
        static readonly string[] Wire = { "GoblinV12", "LT80", "SW84", "Lifted85", "Shvan92" };
        public static string CarToWire(string model) { int i = Array.IndexOf(Models, model); return Wire[i >= 0 ? i : 1]; }      // unknown: LT80, the default
        public static string CarFromWire(string wire) { int i = Array.IndexOf(Wire, wire); return Models[i >= 0 ? i : 1]; }

        readonly Func<SocialClient> _social;
        readonly RaceLobby _room;
        readonly int _me;
        PartyDto _seen;
        bool _dirty;
        string _toast;

        public bool Online { get; private set; }

        public RaceParty(Func<SocialClient> social, RaceLobby room, int me) { _social = social; _room = room; _me = me; }

        // A party the room can be read from: 2+ members and race state (an older backend has none).
        public static bool IsRoom(PartyDto p) => p != null && p.members != null && p.members.Length >= 2 && p.race != null && RaceEvent.ById(p.race.course) != null;

        public static RaceLobbyState ToState(PartyDto p)
        {
            var s = new RaceLobbyState { Name = p.race.name, Course = p.race.course, Laps = p.race.laps, Closed = p.race.closed, Private = p.race.@private, FillBots = p.race.fill_bots, Host = (int)p.leader_id };
            foreach (var m in p.members)
                s.Members.Add(new RaceLobbyMember { Id = (int)m.account_id, Name = m.nickname, Car = CarFromWire(m.race?.car), Paint = m.race?.paint ?? 0, Ready = m.account_id != p.leader_id && (m.race?.ready ?? false) });
            return s;
        }

        // The toast to show once (a refusal, a kick), else null.
        public string TakeToast() { var t = _toast; _toast = null; return t; }

        public bool Sync()
        {
            bool changed = _dirty;
            _dirty = false;
            var p = _social()?.Party;
            if (p == _seen) return changed;
            _seen = p;
            if (IsRoom(p))
            {
                if (Online) _room.Load(ToState(p));
                else
                {
                    Online = true;
                    Quietly(() => _room.Load(ToState(p)));
                    _room.Lines.Add($"파티 방에 들어왔습니다. 방장: {_room.Member(_room.HostId)?.Name}");
                }
                return true;
            }
            if (!Online) return changed;
            // Online -> local. No party at all while I did not leave: the leader kicked me.
            if (p == null) { Quietly(Solo); _room.Lines.Add(_toast = "방에서 내보내졌습니다."); }
            else Solo();
            return true;
        }

        // Leaving the room leaves the party (the backend hands the leader over).
        public void Leave()
        {
            if (!Online) return;
            _social()?.LeaveParty();
            Online = false;
            Quietly(Solo);
        }

        void Solo()
        {
            Online = false;
            var s = _room.Snapshot();
            s.Members.RemoveAll(m => m.Id != _me);
            if (s.Members.Count == 0) return;
            s.Host = _me;
            s.Members[0].Ready = false;
            _room.Load(s);
        }

        void Quietly(Action a)
        {
            int n = _room.Lines.Count;
            a();
            if (_room.Lines.Count > n) _room.Lines.RemoveRange(n, _room.Lines.Count - n);
        }

        // ---- commands: the room's own rules first, then the backend ----

        public bool SetCar(string car, int paint) => Run(() => _room.SetCar(_me, car, paint), MyRace);
        public bool ToggleReady() => Run(() => _room.ToggleReady(_me), MyRace);
        public bool SetCourse(string course, int laps) => Run(() => _room.SetCourse(_me, course, laps), RoomRace);
        public bool SetSettings(string name, bool isPrivate, bool fillBots) => Run(() => _room.SetSettings(_me, name, isPrivate, fillBots), RoomRace);
        public bool CloseSlot(int slot) => Run(() => _room.CloseSlot(_me, slot), RoomRace);
        public bool OpenSlot(int slot) => Run(() => _room.OpenSlot(_me, slot), RoomRace);
        public bool Kick(int target) => Run(() => _room.Kick(_me, target), done => _social().Kick(target, done));

        void MyRace(Action<string> done)
        {
            var m = _room.Member(_me);
            _social().SetMyRace(new MemberRaceDto { car = CarToWire(m.Car), paint = m.Paint, ready = m.Ready }, done);
        }

        void RoomRace(Action<string> done)
        {
            var s = _room.Snapshot();
            _social().SetPartyRace(new PartyRaceDto { name = s.Name, course = s.Course, laps = s.Laps, @private = s.Private, fill_bots = s.FillBots, closed = s.Closed }, done);
        }

        // Optimistic: the change shows now; a refusal puts the room (and its lines) back as they were and leaves the reason as a toast.
        bool Run(Func<bool> local, Action<Action<string>> send)
        {
            if (!Online || _social() == null) return local();
            var before = _room.Snapshot();
            int lines = _room.Lines.Count;
            if (!local()) return false;
            send(err =>
            {
                if (err == null) return;
                _room.Load(before);
                if (_room.Lines.Count > lines) _room.Lines.RemoveRange(lines, _room.Lines.Count - lines);
                _toast = err;
                _dirty = true;
            });
            return true;
        }
    }
}

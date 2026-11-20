using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server -> every client, reliable: the room's TOP3s for the board (ADR-502, #422). Sent when they change and again every few
    // seconds (latecomers), empty when the island closes. Names and scores, nothing else; nothing of it is ever saved.
    public struct WaitingRecordsBroadcast : IBroadcast
    {
        public string[] BungeeNames; public int[] BungeeScores;   // completed jumps
        public string[] RangeNames; public int[] RangeScores;     // booth target hits
    }

    // The shooting range of the island (ADR-501, #421) and the room's records (ADR-502). Server only, part of the waiting room:
    // stepping into a booth's stand lends its gun and rounds, stepping out (or the departure) takes them back; while the room is open a
    // body may fire only in a booth with that gun (Combatant.ServerFireGate) and a bullet that meets a booth target scores while one
    // that meets a body stops harmlessly (Combatant.ServerShotIntercept, rules in Core/WaitingRange). Waiting bots use the booths too.
    public sealed partial class WaitingRoom
    {
        sealed class Loan { public int Slot; public int WeaponId; public AmmoType Ammo; public int ReserveBefore; }
        readonly Dictionary<Combatant, Loan> _loans = new Dictionary<Combatant, Loan>();
        readonly List<Combatant> _loanScratch = new List<Combatant>();
        readonly RoomRecords _rangeRecords = new RoomRecords(), _bungeeRecords = new RoomRecords();
        int _sentRange = -1, _sentBungee = -1;
        float _nextRecords;
        const float RecordsResendSeconds = 3f;
        const int BoardTop = 3;

        public RoomRecords RangeRecords => _rangeRecords;
        public RoomRecords BungeeRecords => _bungeeRecords;
        public bool HasLoan(Combatant c) => c != null && _loans.ContainsKey(c);
        public int KeyOf(Combatant c) => c != null && _keys.TryGetValue(c, out int k) ? k : int.MinValue;

        static string NameOf(Combatant c)
        {
            var state = MatchDirector.Instance != null ? MatchDirector.Instance.State : null;
            string name = state != null ? state.ServerNameOf(c) : "";
            return string.IsNullOrEmpty(name) ? "" : name;
        }

        static int BoothOf(Combatant c, float margin)
        {
            var p = c.transform.position;
            return WaitingRange.BoothAt(p.x - WaitingIsland.CentreX, p.z - WaitingIsland.CentreZ, margin);
        }

        bool HoldsLoan(Combatant c) => _loans.TryGetValue(c, out var l) && c.ActiveWeaponId == l.WeaponId && c.ActiveSlot.Value == l.Slot;

        // Called for every body the room registers: the gate and the bullet hook ask the room while it is open, and let everything
        // through once it has closed (the match).
        void ArmRange(Combatant c)
        {
            c.ServerFireGate = () => !Open || WaitingRange.MayFire(true, BoothOf(c, WaitingRange.LeaveMargin) >= 0, HoldsLoan(c));
            c.ServerShotIntercept = col => Open && Intercept(c, col);
            c.ServerItemGate = () => !Open;
        }

        bool Intercept(Combatant shooter, Collider col)
        {
            var body = col.GetComponentInParent<Combatant>();
            if (body == shooter) return false;
            var target = col.GetComponent<RangeTarget>();
            var hit = target != null ? RangeHit.Target : body != null ? RangeHit.Body : RangeHit.World;
            // A target scores only for a shooter standing in its own booth.
            bool inBooth = BoothOf(shooter, WaitingRange.LeaveMargin) is int booth && booth >= 0 && (target == null || target.Booth == booth);
            var verdict = WaitingRange.Judge(true, inBooth, hit);
            if (verdict == RangeShot.Score) _rangeRecords.Add(KeyOf(shooter), NameOf(shooter), 1);
            return verdict != RangeShot.Pass;
        }

        // Every tick while the room is open: lend to who stepped onto a stand, take back from who left it, top the rounds up.
        void TickRange()
        {
            var rules = IslandRules.Current ?? new IslandRules();
            foreach (var kv in _keys)
            {
                var c = kv.Key;
                if (c == null) continue;
                bool loaned = _loans.TryGetValue(c, out var loan);
                bool stands = !c.Dead.Value && !Carries(c) && BoothOf(c, loaned ? WaitingRange.LeaveMargin : 0f) >= 0;
                if (stands && !loaned) Lend(c, rules);
                else if (!stands && loaned) TakeBack(c);
                else if (loaned && c.ReserveFor(loan.Ammo) < loan.ReserveBefore + rules.RangeRounds / 2)
                    c.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = loan.Ammo, Amount = loan.ReserveBefore + rules.RangeRounds - c.ReserveFor(loan.Ammo) });
            }
            // A body that went away (despawned) keeps nothing to take back.
            _loanScratch.Clear();
            foreach (var c in _loans.Keys) if (c == null) _loanScratch.Add(c);
            foreach (var c in _loanScratch) _loans.Remove(c);
        }

        void Lend(Combatant c, IslandRules rules)
        {
            var weapon = BoothGun(rules);
            if (weapon == null || !CanBorrow(c, weapon)) return;
            int before = c.ReserveFor(weapon.Ammo);
            // Only into an empty slot (nothing the body carries is swapped out or dropped): the loan is whichever empty slot took it.
            int empty = 0;
            for (int s = 0; s < Loadout.ThrowableSlot; s++) if (c.WeaponInSlot(s) == 0) empty |= 1 << s;
            if (!c.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weapon.Id, Amount = weapon.MagSize })) return;
            int slot = -1;
            for (int s = 0; s < Loadout.ThrowableSlot && slot < 0; s++) if ((empty & (1 << s)) != 0 && c.WeaponInSlot(s) == weapon.Id) slot = s;
            if (slot < 0) return;
            c.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = weapon.Ammo, Amount = rules.RangeRounds });
            c.ServerSelectSlot(slot);
            _loans[c] = new Loan { Slot = slot, WeaponId = weapon.Id, Ammo = weapon.Ammo, ReserveBefore = before };
            Debug.Log($"[WaitingRoom] range: {c.name} ({(c.IsPlayer ? "person" : "bot")}) takes a booth gun");
        }

        static WeaponDef BoothGun(IslandRules rules) { var w = GameData.Weapons.Find(rules.RangeWeaponId); return w != null && !w.IsMelee ? w : null; }

        // Has an empty slot that takes the booth gun.
        static bool CanBorrow(Combatant c, WeaponDef weapon)
        {
            for (int s = 0; s < Loadout.ThrowableSlot; s++) if (Loadout.SlotTakes(s, weapon.Category) && c.WeaponInSlot(s) == 0) return true;
            return false;
        }

        void TakeBack(Combatant c)
        {
            if (!_loans.TryGetValue(c, out var l)) return;
            _loans.Remove(c);
            if (c != null) c.ServerTakeBack(l.Slot, l.WeaponId, l.Ammo, l.ReserveBefore);
        }

        // The departure: every loan back before anyone reaches an elevator, and the records are gone with the island.
        void CloseRange()
        {
            _loanScratch.Clear();
            _loanScratch.AddRange(_loans.Keys);
            foreach (var c in _loanScratch) TakeBack(c);
            _rangeRecords.Clear(); _bungeeRecords.Clear();
            SendRecords(true);
        }

        void RecordJump(Combatant jumper) { if (jumper != null) _bungeeRecords.Add(KeyOf(jumper), NameOf(jumper), 1); }

        void SendRecords(bool force)
        {
            if (!force && _rangeRecords.Version == _sentRange && _bungeeRecords.Version == _sentBungee && Time.unscaledTime < _nextRecords) return;
            _sentRange = _rangeRecords.Version; _sentBungee = _bungeeRecords.Version;
            _nextRecords = Time.unscaledTime + RecordsResendSeconds;
            var bungee = _bungeeRecords.Top(BoardTop); var range = _rangeRecords.Top(BoardTop);
            var m = new WaitingRecordsBroadcast
            {
                BungeeNames = new string[bungee.Count], BungeeScores = new int[bungee.Count],
                RangeNames = new string[range.Count], RangeScores = new int[range.Count],
            };
            for (int i = 0; i < bungee.Count; i++) { m.BungeeNames[i] = bungee[i].Name; m.BungeeScores[i] = bungee[i].Score; }
            for (int i = 0; i < range.Count; i++) { m.RangeNames[i] = range[i].Name; m.RangeScores[i] = range[i].Score; }
            _nm.ServerManager.Broadcast(m, true, Channel.Reliable);
        }

        // ---- waiting bots at the range (simple: walk to a free stand, shoot its targets a while, walk off) ----
        const float BotRangeChance = 0.3f;
        const float BotAimErrorDeg = 2.5f;

        bool PickBooth(WaitingBot b)
        {
            if (Random.value > BotRangeChance) return false;
            var weapon = BoothGun(IslandRules.Current ?? new IslandRules());
            if (weapon == null || !CanBorrow(b.Body, weapon)) return false;   // a bot whose hands are full would only stand there
            int booth = Random.Range(0, WaitingIsland.Booths.Length);
            foreach (var o in _bots) if (o.Booth == booth) return false;   // one bot per stand
            var stand = WaitingRange.Stand(booth);
            b.Booth = booth; b.ShootUntil = 0f;
            b.Target = new Vector3(WaitingIsland.WorldX(stand.x), b.Body.transform.position.y, WaitingIsland.WorldZ(stand.z));
            b.PickAt = float.PositiveInfinity;   // the range decides when it leaves
            return true;
        }

        // True while the bot stands at its booth (it aims and fires instead of walking).
        bool ShootAtRange(WaitingBot b)
        {
            if (b.Booth < 0) return false;
            var c = b.Body;
            var p = c.transform.position;
            var d = b.Target - p; d.y = 0f;
            if (d.sqrMagnitude > 0.04f) return false;   // still on its way
            if (b.ShootUntil <= 0f) b.ShootUntil = Time.time + 8f + Random.value * 6f;
            if (Time.time > b.ShootUntil) { b.Booth = -1; b.ShootUntil = 0f; b.PickAt = Time.time; return false; }
            if (!_loans.ContainsKey(c)) return true;    // the loan comes with the next range tick
            var t = WaitingRange.Target(b.Booth, (int)(Time.time / 3f) & 1);
            var chest = new Vector3(WaitingIsland.WorldX(t.x), _deckY + 1.1f, WaitingIsland.WorldZ(t.z));
            var look = chest - p; look.y = 0f;
            c.transform.rotation = Quaternion.Slerp(c.transform.rotation, Quaternion.LookRotation(look), 8f * Time.deltaTime);
            if (c.ActiveMag == 0) { c.ServerReload(); return true; }
            if (Time.time < b.NextShot) return true;
            b.NextShot = Time.time + 0.7f + Random.value * 0.7f;
            var to = chest - c.EyePosition;
            var aim = Quaternion.LookRotation(to) * Quaternion.Euler((Random.value * 2f - 1f) * BotAimErrorDeg, (Random.value * 2f - 1f) * BotAimErrorDeg, 0f);
            c.ServerFire(aim * Vector3.forward, to.magnitude);
            return true;
        }
    }
}

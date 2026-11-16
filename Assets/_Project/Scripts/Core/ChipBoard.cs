using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The team line the HUD shows ("칩 1 바닥 · 1 보관").
    public struct ChipTeamStatus
    {
        public int Dropped, Carried, Stored;
        public float SoonestLeft;   // seconds until the next loose chip is gone, 0 when none
    }

    // All revive chips of a match (WP 8, ADR-171): drops one on a death, tracks pick-up, deposit, expiry and
    // revival. Pure; the server finds bodies and boxes, this decides what is allowed.
    public sealed class ChipBoard
    {
        readonly Dictionary<int, ReviveChip> _chips = new Dictionary<int, ReviveChip>(); // by owner
        public IEnumerable<ReviveChip> All => _chips.Values;
        public ReviveChip Of(int owner) => _chips.TryGetValue(owner, out var c) ? c : null;

        // A combatant died. Returns the chip when one drops (squad, someone of the team still alive).
        public ReviveChip OnDeath(int owner, int team, TeamMode mode, int othersAlive, double now)
        {
            if (!ReviveChip.ShouldDrop(mode, othersAlive)) return null;
            var chip = new ReviveChip(owner, team, now);
            _chips[owner] = chip;
            return chip;
        }

        // The carrier died or went down: whatever they carry falls.
        public void OnCarrierDown(int carrier)
        {
            foreach (var c in _chips.Values) if (c.State == ChipState.Carried && c.Carrier == carrier) c.Drop();
        }

        public ChipResult PickUp(int owner, double now, int carrier, int carrierTeam, PollutionRules rules) =>
            _chips.TryGetValue(owner, out var c) ? c.PickUp(now, carrier, carrierTeam, rules) : ChipResult.WrongState;

        // Puts every chip the carrier holds into the box in `boxRegion`; the first failure code, else Ok.
        public ChipResult DepositAll(int carrier, double now, int boxRegion, IReadOnlyList<PollutionState> states, PollutionRules rules, List<int> deposited = null)
        {
            var result = ChipResult.WrongState;
            foreach (var c in _chips.Values)
            {
                if (c.State != ChipState.Carried || c.Carrier != carrier) continue;
                var r = c.Deposit(now, boxRegion, states, rules);
                if (r == ChipResult.Ok) { deposited?.Add(c.Owner); if (result == ChipResult.WrongState) result = ChipResult.Ok; }
                else result = r;
            }
            return result;
        }

        public void Tick(double now, IReadOnlyList<PollutionState> states, PollutionRules rules)
        {
            foreach (var c in _chips.Values) c.Tick(now, states, rules);
        }

        // Dead members of `team` whose chip is boxed: who could be revived now.
        public void Revivable(int team, List<int> owners)
        {
            owners.Clear();
            foreach (var c in _chips.Values) if (c.Team == team && c.State == ChipState.Stored) owners.Add(c.Owner);
            owners.Sort();
        }

        // The oldest boxed chip of the team goes to `destRegion` (an owner id picks one instead when >= 0).
        public ChipResult Revive(int team, int destRegion, IReadOnlyList<PollutionState> states, out int owner, int wantedOwner = -1)
        {
            owner = -1;
            ReviveChip pick = null;
            foreach (var c in _chips.Values)
            {
                if (c.Team != team || c.State != ChipState.Stored) continue;
                if (wantedOwner >= 0 && c.Owner != wantedOwner) continue;
                if (pick == null || c.DiedAt < pick.DiedAt || (c.DiedAt == pick.DiedAt && c.Owner < pick.Owner)) pick = c;
            }
            if (pick == null) return ChipResult.WrongState;
            var r = pick.Revive(destRegion, states);
            if (r == ChipResult.Ok) owner = pick.Owner;
            return r;
        }

        public ChipTeamStatus StatusOf(int team, double now, PollutionRules rules)
        {
            var s = new ChipTeamStatus();
            double soonest = double.MaxValue;
            foreach (var c in _chips.Values)
            {
                if (c.Team != team) continue;
                if (c.State == ChipState.Dropped) s.Dropped++;
                else if (c.State == ChipState.Carried) s.Carried++;
                else if (c.State == ChipState.Stored) { s.Stored++; continue; }
                else continue;
                soonest = Math.Min(soonest, c.ExpiresAt(rules) - now);
            }
            s.SoonestLeft = soonest == double.MaxValue ? 0f : (float)Math.Max(0.0, soonest);
            return s;
        }
    }
}

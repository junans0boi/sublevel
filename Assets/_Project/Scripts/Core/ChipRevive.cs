using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum ChipState : byte { None = 0, Dropped = 1, Carried = 2, Stored = 3, Revived = 4, Expired = 5, Destroyed = 6 }
    public enum ChipResult : byte { Ok, WrongState, TooLate, NotTeammate, BadDestination }

    // The revive chip of one dead squad member (ADR-171, grilling 2026-11-16). A knocked player is still saved by
    // the 10 s transfusion (TeamRules); a chip exists only after a death. It drops at the body, a teammate picks it
    // up and puts it into a platform's chip box within ChipSeconds of the death; a box in a region that becomes
    // contaminated loses its chips; a stored chip revives the member at a platform in a non-contaminated region.
    public sealed class ReviveChip
    {
        public int Owner { get; }
        public int Team { get; }
        public double DiedAt { get; }
        public ChipState State { get; private set; } = ChipState.Dropped;
        public int Carrier { get; private set; } = -1;
        public int BoxRegion { get; private set; } = -1;

        public ReviveChip(int owner, int team, double diedAt) { Owner = owner; Team = team; DiedAt = diedAt; }

        // The chip drops only in a squad, and only while some other member of the team is still alive
        // (the last one to die ends the team; nobody could use it). `othersAlive` counts knocked ones too.
        public static bool ShouldDrop(TeamMode mode, int othersAlive) => mode == TeamMode.Squad && othersAlive > 0;

        public double ExpiresAt(PollutionRules rules) => DiedAt + rules.ChipSeconds;

        public ChipResult PickUp(double now, int carrier, int carrierTeam, PollutionRules rules)
        {
            if (State != ChipState.Dropped) return ChipResult.WrongState;
            if (now >= ExpiresAt(rules)) return ChipResult.TooLate;
            if (carrierTeam != Team || carrier == Owner) return ChipResult.NotTeammate;
            State = ChipState.Carried; Carrier = carrier;
            return ChipResult.Ok;
        }

        // The carrier died or was knocked down: the chip falls where they are (the clock keeps running).
        public void Drop()
        {
            if (State == ChipState.Carried) { State = ChipState.Dropped; Carrier = -1; }
        }

        public ChipResult Deposit(double now, int boxRegion, IReadOnlyList<PollutionState> states, PollutionRules rules)
        {
            if (State != ChipState.Carried) return ChipResult.WrongState;
            if (now >= ExpiresAt(rules)) return ChipResult.TooLate;
            if (states == null || boxRegion < 0 || boxRegion >= states.Count || states[boxRegion] == PollutionState.Contaminated)
                return ChipResult.BadDestination;
            State = ChipState.Stored; BoxRegion = boxRegion; Carrier = -1;
            return ChipResult.Ok;
        }

        // Call every server tick: a chip not yet in a box is gone after the time; a boxed one is destroyed when its
        // region is contaminated.
        public void Tick(double now, IReadOnlyList<PollutionState> states, PollutionRules rules)
        {
            if (State == ChipState.Dropped || State == ChipState.Carried)
            {
                if (now >= ExpiresAt(rules)) { State = ChipState.Expired; Carrier = -1; }
            }
            else if (State == ChipState.Stored && states != null && BoxRegion >= 0 && BoxRegion < states.Count
                     && states[BoxRegion] == PollutionState.Contaminated)
            {
                State = ChipState.Destroyed;
            }
        }

        // Revive the owner at a platform in `destRegion` (not contaminated). The caller places the owner at the
        // chosen platform with ChipReviveHp, a sitting pose and no weapon in hand (same as a transfusion).
        public ChipResult Revive(int destRegion, IReadOnlyList<PollutionState> states)
        {
            if (State != ChipState.Stored) return ChipResult.WrongState;
            if (states == null || destRegion < 0 || destRegion >= states.Count || states[destRegion] == PollutionState.Contaminated)
                return ChipResult.BadDestination;
            State = ChipState.Revived;
            return ChipResult.Ok;
        }
    }
}

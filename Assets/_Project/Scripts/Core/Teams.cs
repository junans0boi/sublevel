using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Team modes (ADR-072). The match is chosen in the main menu; bots fill every team up to its size.
    public enum TeamMode { Solo = 0, Duo = 1, Squad = 2 }

    // Team play tuning (ADR-072). Data: rules.json "Team".
    [Serializable]
    public class TeamRules
    {
        // Set from data when there is any; tests may replace it.
        public static TeamRules Current = new TeamRules();

        public int DuoSize = 2;
        public int SquadSize = 4;
        // Off: shots, punches, blasts and vehicles of a teammate do no damage (one's own grenade still does).
        public bool FriendlyFire = false;

        // ---- Knocked down (기절) ----
        public float KnockedHp = 100f;            // the knock bar, drained by bleeding and by further damage
        public float BleedPerSecond = 2.5f;       // first knock: 100 hp in 40 s
        public float BleedGrowthPerKnock = 0.5f;  // every further knock in the same life bleeds this much faster (+50 %)
        // Seconds the n-th knock of one life takes to bleed the whole bar out (ADR-129, PUBG: 90, 30, 30, 10, 7, 5, 4, 3, 2);
        // knocks past the end of the table keep the last value. Empty/null: the older per-second fields above are used.
        public float[] BleedSeconds = { 90f, 30f, 30f, 10f, 7f, 5f, 4f, 3f, 2f };
        public float DownInvulnerableSeconds = 1.5f; // no damage for this long after going down
        public float KnockedMoveFactor = 1f;      // crawling, on top of the prone stance's own factor
        public float KnockedDamageFactor = 1f;    // damage taken while knocked

        // ---- Reviving (F held) ----
        public float ReviveSeconds = 10f;
        public float ReviveRangeMeters = 2f;      // to start; the server lets it go on up to ReviveKeepMeters
        public float ReviveKeepMeters = 2.75f;
        public float ReviveHp = 10f;              // health right after being revived

        // ---- Carrying a knocked teammate (ADR-129) ----
        public float CarryMoveFactor = 0.55f;     // the carrier's walking speed
        public float CarryRangeMeters = 2f;       // to lift
        public float CarryKeepMeters = 4f;        // the carried one is let go beyond this (lag, a vault)

        // ---- Pings (ADR-130) ----
        public float PingLifeSeconds = 8f;          // a team ping stays this long
        public float PingMinIntervalSeconds = 0.4f; // per player: faster requests are dropped by the server
        public int PingMaxPerOwner = 3;             // a player's newest pings that stay at once
        public float PingMaxMeters = 600f;          // reach of the aim ray (PlayerController.PingDistance)

        // ---- Bots in a team ----
        public float BotFollowMeters = 30f;       // a roaming bot walks back once the team's centre is farther than this (ADR-312)...
        public float BotRegroupMeters = 60f;      // ...and sprints (dropping loot) beyond this
        public float BotReviveSearchMeters = 45f; // goes to a knocked teammate this close
        public float BotReviveCalmSeconds = 2f;   // with no enemy seen for this long

        public int SizeOf(TeamMode mode) => mode == TeamMode.Duo ? Math.Max(1, DuoSize) : mode == TeamMode.Squad ? Math.Max(1, SquadSize) : 1;

        // Bleeding of the `knocks`-th knock of this life (1 = first), hp per second.
        public float BleedFor(int knocks)
        {
            if (BleedSeconds != null && BleedSeconds.Length > 0)
                return KnockedHp / Math.Max(0.01f, BleedSeconds[Math.Min(Math.Max(0, knocks - 1), BleedSeconds.Length - 1)]);
            return BleedPerSecond * (1f + BleedGrowthPerKnock * Math.Max(0, knocks - 1));
        }
    }

    // Who is on which team. Teams are numbered from 1; 0 means "no team" (solo, training dummies outside a match):
    // everyone else is an enemy. Participants are counted like the deployment counts them (ADR-055): players first,
    // then bots, so team members also share an elevator.
    public static class Teams
    {
        public const int None = 0;
        public const int MaxSize = 4;

        public static string ModeName(TeamMode mode) => mode == TeamMode.Duo ? "듀오" : mode == TeamMode.Squad ? "스쿼드" : "솔로";

        public static TeamMode Next(TeamMode mode) => mode == TeamMode.Solo ? TeamMode.Duo : mode == TeamMode.Duo ? TeamMode.Squad : TeamMode.Solo;

        // "solo" / "duo" / "squad" (any case, also 1 / 2 / 4); false for anything else.
        public static bool TryParse(string text, out TeamMode mode)
        {
            mode = TeamMode.Solo;
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "solo": case "1": case "솔로": mode = TeamMode.Solo; return true;
                case "duo": case "2": case "듀오": mode = TeamMode.Duo; return true;
                case "squad": case "4": case "스쿼드": mode = TeamMode.Squad; return true;
                default: return false;
            }
        }

        // Team of participant `index` (0-based) with teams of `size`: 1, 1, 2, 2, ... In solo (size 1) every
        // participant is its own team, so team numbers are still set and the rest of the rules need no special case.
        public static int TeamOf(int index, int size) => index < 0 ? None : index / Math.Max(1, size) + 1;

        // Number of the participant within its team, 1..size: the number shown next to its name and on the maps.
        public static int SlotOf(int index, int size) => index < 0 ? 0 : index % Math.Max(1, size) + 1;

        public static int TeamCount(int participants, int size) => participants <= 0 ? 0 : (participants + Math.Max(1, size) - 1) / Math.Max(1, size);

        // Two combatants on the same team (never true for "no team").
        public static bool Same(int teamA, int teamB) => teamA != None && teamA == teamB;

        // Elevator per participant with teams of `size`: each team is one participant of Deployment.Assign, and every
        // member starts in the team's elevator. Size 1 gives exactly Deployment.Assign.
        public static int[] Deploy(int participants, int size, IReadOnlyList<ElevatorSlot> elevators, int seed)
        {
            size = Math.Max(1, size);
            // Teams never share a car with another team (ADR-260): a surplus team gets -1.
            if (size == 1) return Deployment.Assign(participants, elevators, seed, false);
            var perTeam = Deployment.Assign(TeamCount(participants, size), elevators, seed, false);
            var result = new int[Math.Max(0, participants)];
            for (int i = 0; i < result.Length; i++) result[i] = perTeam[TeamOf(i, size) - 1];
            return result;
        }
    }

    // One combatant as the team rules see it.
    public struct TeamMember
    {
        public int Team;
        public bool Dead;
        public bool Knocked;

        public TeamMember(int team, bool dead, bool knocked)
        {
            Team = team;
            Dead = dead;
            Knocked = knocked;
        }

        // On its feet: alive and not knocked down.
        public readonly bool Standing => !Dead && !Knocked;
    }

    // Who is still in the match by team (ADR-072). A team is out when none of its members stands: knocked members of
    // a team with nobody left standing die at once (nobody could revive them), so "out" and "all dead" end up equal.
    public static class TeamStandings
    {
        // A member of `team` other than `except` (an index into `members`, or -1) is on its feet.
        public static bool HasStanding(IReadOnlyList<TeamMember> members, int team, int except = -1)
        {
            for (int i = 0; i < members.Count; i++)
                if (i != except && members[i].Team == team && members[i].Standing) return true;
            return false;
        }

        // A knocked member whose team has nobody standing: it is finished off.
        public static bool MustFinish(IReadOnlyList<TeamMember> members, int index)
        {
            var m = members[index];
            return m.Knocked && !m.Dead && !HasStanding(members, m.Team);
        }

        // Teams with at least one member still alive (knocked counts as alive). Members without a team count one each.
        public static int AliveTeams(IReadOnlyList<TeamMember> members)
        {
            int count = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.Dead) continue;
                if (m.Team == Teams.None) { count++; continue; }
                bool first = true;
                for (int j = 0; j < i && first; j++)
                    if (!members[j].Dead && members[j].Team == m.Team) first = false;
                if (first) count++;
            }
            return count;
        }

        // A member of `team` is still alive.
        public static bool TeamAlive(IReadOnlyList<TeamMember> members, int team)
        {
            for (int i = 0; i < members.Count; i++)
                if (members[i].Team == team && !members[i].Dead) return true;
            return false;
        }

        // The match is decided once at most one team is alive. `winner` is that team (None when nobody is left, or
        // when the last one standing has no team).
        public static bool Decided(IReadOnlyList<TeamMember> members, out int winner)
        {
            winner = Teams.None;
            if (AliveTeams(members) > 1) return false;
            for (int i = 0; i < members.Count; i++)
                if (!members[i].Dead) { winner = members[i].Team; break; }
            return true;
        }

        // The placement a team gets when it goes out: one behind the teams still alive.
        public static int PlacementOnElimination(IReadOnlyList<TeamMember> members) => AliveTeams(members) + 1;
    }

    // The knock rule (ADR-072): a hit that would kill knocks down instead, as long as a teammate is on its feet and
    // this is a team match. Someone already knocked dies.
    public static class Knocks
    {
        public static bool KnocksDown(TeamMode mode, bool alreadyKnocked, bool teammateStanding) =>
            mode != TeamMode.Solo && !alreadyKnocked && teammateStanding;

        // Seconds a knock bar of `hp` lasts on the `knocks`-th knock while nobody revives.
        public static float SecondsLeft(float hp, int knocks, TeamRules rules)
        {
            float rate = rules.BleedFor(knocks);
            return rate <= 0f ? float.PositiveInfinity : Math.Max(0f, hp) / rate;
        }

        // Damage between two combatants on these teams: zero between teammates while friendly fire is off (one's own
        // damage always counts), scaled for a knocked victim.
        public static float DamageBetween(float amount, int attackerTeam, int victimTeam, bool self, bool victimKnocked, TeamRules rules)
        {
            if (!self && !rules.FriendlyFire && Teams.Same(attackerTeam, victimTeam)) return 0f;
            return victimKnocked ? amount * rules.KnockedDamageFactor : amount;
        }
    }

    // Carrying a knocked teammate on the back (ADR-129): the rules of starting and keeping it, pure.
    public static class KnockCarry
    {
        public static bool CanStart(float distance, bool carrierStanding, bool carrierBusy, bool targetKnocked, bool targetFree, bool sameTeam, TeamRules rules) =>
            carrierStanding && !carrierBusy && targetKnocked && targetFree && sameTeam && distance <= rules.CarryRangeMeters;

        public static bool KeepGoing(float distance, bool carrierStanding, bool targetKnocked, TeamRules rules) =>
            carrierStanding && targetKnocked && distance <= rules.CarryKeepMeters;

        // The carried body rides piggyback (#173), not lying: upright, knees round the carrier's waist, arms over its
        // shoulders, leaning onto its back. The model is drawn RideDrop below the carried origin so its hips sit at the
        // carrier's lower back (the origin, and with it the carried one's camera, stays where Offset puts it).
        public const float RideThighDeg = 70f, RideShinDeg = 80f, RideArmDeg = 70f, RideTorsoDeg = 12f, RideDrop = 0.45f;

        // Where the carried body rides: on the carrier's back, a little above its feet.
        public static (float x, float y, float z) Offset(float yawDeg)
        {
            float r = yawDeg * 0.0174532925f;
            return (-(float)Math.Sin(r) * 0.3f, 0.55f, -(float)Math.Cos(r) * 0.3f);
        }
    }

    public enum ReviveStep { Idle, Going, Done, Cancelled }

    // One revive under way, run by the reviver (ADR-072): ReviveSeconds of holding F next to the knocked teammate.
    // It breaks off when either one is no longer in a state to go on or they move apart.
    public class ReviveTimer
    {
        public bool Active { get; private set; }
        public float Elapsed { get; private set; }
        public float Progress01(TeamRules rules) => rules.ReviveSeconds <= 0f ? 1f : Math.Min(1f, Elapsed / rules.ReviveSeconds);

        public bool CanStart(float distance, bool reviverStanding, bool targetKnocked, bool sameTeam, TeamRules rules) =>
            !Active && reviverStanding && targetKnocked && sameTeam && distance <= rules.ReviveRangeMeters;

        public void Start()
        {
            Active = true;
            Elapsed = 0f;
        }

        public void Cancel()
        {
            Active = false;
            Elapsed = 0f;
        }

        // Done once the full time has run; Cancelled (and stopped) when it may not go on.
        public ReviveStep Tick(float dt, float distance, bool reviverStanding, bool targetKnocked, TeamRules rules)
        {
            if (!Active) return ReviveStep.Idle;
            if (!reviverStanding || !targetKnocked || distance > rules.ReviveKeepMeters)
            {
                Cancel();
                return ReviveStep.Cancelled;
            }
            Elapsed += Math.Max(0f, dt);
            if (Elapsed < rules.ReviveSeconds) return ReviveStep.Going;
            Active = false;
            return ReviveStep.Done;
        }
    }

    // How a bot in a team keeps with its squad (ADR-312, ADR-546). Pure rules; BotController.Team applies them.
    public static class BotSquad
    {
        // Walks back to the squad while roaming, looting or looking into a noise, never while farming (each bot's own) or while backing off
        // after a regroup that stalled; past BotRegroupMeters it sprints, and only then does it leave a pickup.
        public static bool Regroups(BotState state, bool farming, bool backingOff, float awayMeters, TeamRules rules, out bool sprint)
        {
            sprint = awayMeters > rules.BotRegroupMeters;
            if (state != BotState.Roam && state != BotState.Investigate && state != BotState.Loot) return false;
            if (farming || backingOff || awayMeters <= rules.BotFollowMeters) return false;
            return state != BotState.Loot || sprint;
        }

        // Where it walks: the teammate nearest the centre, not the centre itself, which can sit in a wall, a building or a river
        // between them. -1 for none.
        public static int NearestTo(BotVec centre, IReadOnlyList<BotVec> mates)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < mates.Count; i++)
            {
                float d = BotVec.PlanarDistance(mates[i], centre);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        // Standing still on purpose is no stall: healing, reloading, just having shot, or reviving (10 s, longer than the stall window).
        public static bool StallExcused(bool healing, bool reloading, bool reviving, float sinceAttack, float stallSeconds) =>
            healing || reloading || reviving || sinceAttack < stallSeconds;
    }
}

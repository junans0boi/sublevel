using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One ticket in the matchmaking queue: a solo player or a party (1..team size) that goes in as a unit (ADR-247).
    // `Id` is the connection id of whoever pressed start (no accounts yet).
    public struct QueueParty
    {
        public int Id;
        public int Size;
        public QueueParty(int id, int size) { Id = id; Size = size; }
    }

    // One team of the match as composed: a human party (PartyId = its ticket) or a bot team (PartyId = -1).
    public struct PlannedTeam
    {
        public int PartyId;
        public int Size;
        public bool IsBot => PartyId < 0;
    }

    // Who plays one match (ADR-248). Solo: every participant is a team of one. Squad/Duo: human parties stay whole as
    // teams, bots form teams of the team size (balanced, never a lone leftover).
    public sealed class MatchPlan
    {
        public TeamMode Mode;
        public int Humans, Bots;
        public readonly List<PlannedTeam> Teams = new List<PlannedTeam>();
        public readonly List<int> Placed = new List<int>();   // ticket ids that play
        public readonly List<int> Overflow = new List<int>(); // ticket ids that did not fit (stay in the queue for the next match)
        public int Participants => Humans + Bots;
        public int HumanTeams { get { int n = 0; foreach (var t in Teams) if (!t.IsBot) n++; return n; } }
        public int BotTeams { get { int n = 0; foreach (var t in Teams) if (t.IsBot) n++; return n; } }
    }

    public static class MatchComposer
    {
        public static int TeamSizeOf(TeamMode mode, TeamRules rules) => mode == TeamMode.Solo ? 1 : rules.SizeOf(mode);

        // `parties` in arrival order. People are placed while they fit under rules.MaxHumans (Participants - MinBots); a party
        // that does not fit whole is left over (so is everyone behind it: first come, first served). Bots fill up to Participants.
        public static MatchPlan Compose(TeamMode mode, IReadOnlyList<QueueParty> parties, MatchRules rules, TeamRules teams)
        {
            var plan = new MatchPlan { Mode = mode };
            int size = TeamSizeOf(mode, teams);
            int maxHumans = rules.MaxHumans;
            bool full = false;
            foreach (var p in parties)
            {
                int n = Math.Max(1, Math.Min(p.Size, size)); // a party is at most one team; in solo everyone is alone
                if (mode == TeamMode.Solo) n = 1;
                if (full || plan.Humans + n > maxHumans) { full = true; plan.Overflow.Add(p.Id); continue; }
                plan.Placed.Add(p.Id);
                plan.Humans += n;
                plan.Teams.Add(new PlannedTeam { PartyId = p.Id, Size = n });
            }
            plan.Bots = BotFill.Plan(plan.Humans, rules.Participants, rules.MinBots);
            // Bot teams: as many as the team size needs, as even as possible (49 bots in fours: 10 teams of 4 and 3 of 3).
            int botTeams = (plan.Bots + size - 1) / size;
            for (int i = 0; i < botTeams; i++)
                plan.Teams.Add(new PlannedTeam { PartyId = -1, Size = plan.Bots / botTeams + (i < plan.Bots % botTeams ? 1 : 0) });
            return plan;
        }
    }

    // What the lobby UI shows while waiting (ADR-249): replicated by the server to every queued client.
    public struct MatchQueueState
    {
        public bool Open;          // somebody is waiting and the window runs
        public int Waiting;        // people in the queue (parties summed)
        public int Parties;
        public int Target;         // participants of a match (people + bots)
        public float SecondsLeft;  // of the window; 0 when it is not running
        public bool Started;       // the match is being made: the clients should go to the port that follows
    }

    // The server's queue (ADR-247): the window opens when the first ticket comes in and closes WindowSeconds later, or at once
    // when the room is full of people. Pure: the caller passes the clock.
    public sealed class MatchQueue
    {
        readonly List<QueueParty> _tickets = new List<QueueParty>();
        readonly float _window;
        readonly MatchRules _rules;
        double _openedAt = double.NaN;

        public MatchQueue(MatchRules rules) { _rules = rules; _window = rules.QueueWindowSeconds; }

        public IReadOnlyList<QueueParty> Tickets => _tickets;
        public int Waiting { get { int n = 0; foreach (var t in _tickets) n += t.Size; return n; } }

        // False when that id is already queued.
        public bool Enqueue(int id, int size, double now)
        {
            foreach (var t in _tickets) if (t.Id == id) return false;
            _tickets.Add(new QueueParty(id, Math.Max(1, size)));
            if (double.IsNaN(_openedAt)) _openedAt = now;
            return true;
        }

        public bool Leave(int id, double now)
        {
            int i = _tickets.FindIndex(t => t.Id == id);
            if (i < 0) return false;
            _tickets.RemoveAt(i);
            if (_tickets.Count == 0) _openedAt = double.NaN; // nobody waits: the next one opens a fresh window
            return true;
        }

        public float SecondsLeft(double now) => double.IsNaN(_openedAt) ? 0f : (float)Math.Max(0.0, _window - (now - _openedAt));

        public bool ShouldStart(TeamMode mode, double now, TeamRules teams)
        {
            if (_tickets.Count == 0) return false;
            if (SecondsLeft(now) <= 0f) return true;
            var plan = Compose(mode, teams);
            return plan.Overflow.Count > 0 || plan.Humans >= _rules.MaxHumans; // full: no reason to wait
        }

        public MatchPlan Compose(TeamMode mode, TeamRules teams) => MatchComposer.Compose(mode, _tickets, _rules, teams);

        // The match was made: the placed tickets leave, the overflow stays and a new window opens for it.
        public MatchPlan Start(TeamMode mode, double now, TeamRules teams)
        {
            var plan = Compose(mode, teams);
            _tickets.RemoveAll(t => plan.Placed.Contains(t.Id));
            _openedAt = _tickets.Count > 0 ? now : double.NaN;
            return plan;
        }

        public MatchQueueState State(double now)
        {
            return new MatchQueueState
            {
                Open = _tickets.Count > 0,
                Waiting = Waiting,
                Parties = _tickets.Count,
                Target = _rules.Participants,
                SecondsLeft = SecondsLeft(now),
            };
        }
    }
}

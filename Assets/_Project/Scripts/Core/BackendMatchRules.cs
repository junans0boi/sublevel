using System.Collections.Generic;

namespace Game.Core
{
    // A match the backend assigned to this game server (GET /api/match/matches, docs/match-api.md): who may play in it.
    public sealed class AssignedMatch
    {
        public string Id = ""; public long[] Accounts = new long[0]; public int Bots;
    }

    // Which people the server lets into the room, and which backend match a started match belongs to. Pure.
    public static class BackendMatchRules
    {
        public static bool Allows(IReadOnlyList<AssignedMatch> active, long account)
        {
            for (int i = 0; i < active.Count; i++) foreach (long a in active[i].Accounts) if (a == account) return true;
            return false;
        }

        // The assigned match whose accounts most of the people in the room are in (null when none overlaps); ties go to the first (the oldest).
        public static AssignedMatch Pick(IReadOnlyList<AssignedMatch> active, ICollection<long> present)
        {
            AssignedMatch best = null; int bestN = 0;
            for (int i = 0; i < active.Count; i++)
            {
                int n = 0;
                foreach (long a in active[i].Accounts) if (present.Contains(a)) n++;
                if (n > bestN) { best = active[i]; bestN = n; }
            }
            return best;
        }
    }
}

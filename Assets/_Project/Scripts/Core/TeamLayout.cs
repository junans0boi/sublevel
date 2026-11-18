using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Who is on which team (ADR-332). Participants are numbered people first, then bots (the order the elevator deployment
    // counts them in). A party of N people is one team of N and is never topped up with bots; bots form teams of at most
    // `size`, split as evenly as possible (79 bots in fours: 19 teams of 4 and one of 3). Pure.
    public sealed class TeamLayout
    {
        readonly int[] _sizes;
        readonly int[] _firstIndex;

        public TeamLayout(IReadOnlyList<int> teamSizes)
        {
            _sizes = new int[teamSizes.Count];
            _firstIndex = new int[teamSizes.Count];
            int at = 0;
            for (int i = 0; i < _sizes.Length; i++) { _sizes[i] = Math.Max(1, teamSizes[i]); _firstIndex[i] = at; at += _sizes[i]; }
            Participants = at;
        }

        public int Participants { get; }
        public int TeamCount => _sizes.Length;
        public int SizeOfTeam(int team) => _sizes[team - 1];

        // People in chunks of `size` in order (no party info): one person is a team of one. Then the bot teams.
        public static TeamLayout For(int humans, int bots, int size)
        {
            var sizes = new List<int>();
            size = Math.Max(1, size);
            for (int left = Math.Max(0, humans); left > 0; left -= size) sizes.Add(Math.Min(size, left));
            return new TeamLayout(Concat(sizes, bots, size));
        }

        // `partySizes`: each party is one team (clamped to `size`).
        public static TeamLayout ForParties(IReadOnlyList<int> partySizes, int bots, int size)
        {
            var sizes = new List<int>();
            foreach (var p in partySizes) sizes.Add(Math.Max(1, Math.Min(p, Math.Max(1, size))));
            return new TeamLayout(Concat(sizes, bots, size));
        }

        static List<int> Concat(List<int> sizes, int bots, int size) { sizes.AddRange(BotTeamSizes(bots, size)); return sizes; }

        public static IEnumerable<int> BotTeamSizes(int bots, int size)
        {
            size = Math.Max(1, size);
            bots = Math.Max(0, bots);
            int teams = (bots + size - 1) / size;
            for (int i = 0; i < teams; i++) yield return bots / teams + (i < bots % teams ? 1 : 0);
        }

        // Team number (1-based) of participant `index`; None past the layout (a late joiner).
        public int TeamOf(int index)
        {
            if (index < 0 || index >= Participants) return Teams.None;
            int t = Array.BinarySearch(_firstIndex, index);
            return (t >= 0 ? t : ~t - 1) + 1;
        }

        public int SlotOf(int index) => TeamOf(index) == Teams.None ? 0 : index - _firstIndex[TeamOf(index) - 1] + 1;

        // One elevator per team (ADR-260); every member starts in it.
        public int[] Deploy(IReadOnlyList<ElevatorSlot> elevators, int seed)
        {
            var perTeam = Deployment.Assign(TeamCount, elevators, seed, false);
            var result = new int[Participants];
            for (int i = 0; i < result.Length; i++) result[i] = perTeam[TeamOf(i) - 1];
            return result;
        }
    }
}

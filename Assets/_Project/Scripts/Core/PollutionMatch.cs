using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The little the server sends once so every client can rebuild the same pollution (s6 spec 6): the clients
    // compute the front themselves from the match seed and the shared map, and compare the hash.
    public struct PollutionSnapshot
    {
        public int Seed;
        public int[] Order;
        public uint GridHash;
    }

    // One match's pollution: order from the seed, the front on the map grid, and what it does to a combatant.
    // Pure: the server (PollutionDirector) feeds it server time; a client builds its own from the snapshot.
    public sealed class PollutionMatch
    {
        public readonly int Seed;
        public readonly PollutionTimeline Timeline;
        public readonly PollutionRules Rules;
        public readonly uint GridHash;
        readonly PollutionState[] _states;

        public int RegionCount => Timeline.RegionCount;
        public int FinalRegion => Timeline.FinalRegion;
        public PollutionSnapshot Snapshot => new PollutionSnapshot { Seed = Seed, Order = (int[])Timeline.Order.Clone(), GridHash = GridHash };

        public PollutionMatch(PollutionGrid grid, IReadOnlyList<int[]> adjacency, int regionCount, int seed, PollutionRules rules = null)
            : this(grid, PollutionOrder.Plan(regionCount, adjacency, seed), seed, rules) { }

        public PollutionMatch(PollutionGrid grid, int[] order, int seed, PollutionRules rules = null)
        {
            Rules = rules ?? PollutionRules.Current;
            Seed = seed;
            Timeline = new PollutionTimeline(grid, order, Rules);
            GridHash = PollutionGridBuilder.Hash(Timeline);
            _states = new PollutionState[order.Length];
        }

        // A client: the same order as the server, and a check that its own front came out the same.
        public static PollutionMatch FromSnapshot(PollutionGrid grid, PollutionSnapshot s, PollutionRules rules, out bool hashMatches)
        {
            var m = new PollutionMatch(grid, s.Order, s.Seed, rules);
            hashMatches = m.GridHash == s.GridHash;
            return m;
        }

        // Region states now. The array is reused: copy it to keep it.
        public PollutionState[] States(double seconds) { Timeline.States(seconds, _states); return _states; }

        public int CellAt(float x, float z) => Timeline.Grid.CellAt(x, z);
        public int RegionOfCell(int cell) => Timeline.Grid.Region[cell];

        // Health lost per second at this world position right now (after any gas cover).
        public float DamagePerSecondAt(float x, float z, double seconds, GasCover cover)
        {
            int cell = CellAt(x, z);
            if (Timeline.Grid.IsSea(cell)) return 0f;
            return Timeline.DamagePerSecond(cell, seconds, cover);
        }
    }

    // One combatant's exposure: damage comes in steps of TickSeconds (0.5 s), the gas cover is theirs.
    public sealed class PollutionExposure
    {
        public GasCover Cover;
        double _next = -1;

        public void UseFilter(double now, PollutionRules r) => Cover = Cover.With(GasCover.Filter(now, r), now);
        public void UseAntidote(double now, PollutionRules r) => Cover = Cover.With(GasCover.Antidote(now, r), now);
        public float CoverLeft(double now) => (float)Math.Max(0.0, Cover.Until - now);

        // Call each server tick. Returns the health to take now (0 between steps or outside the gas). Time spent
        // outside the gas is not charged: the step clock restarts on entering.
        public float Step(PollutionMatch m, float x, float z, double now)
        {
            float dps = m.DamagePerSecondAt(x, z, now, Cover);
            if (dps <= 0f) { _next = -1; return 0f; }
            float tick = Math.Max(0.05f, m.Rules.TickSeconds);
            if (_next < 0) { _next = now + tick; return 0f; }
            float total = 0f;
            while (now >= _next) { total += dps * tick; _next += tick; }
            return total;
        }
    }
}

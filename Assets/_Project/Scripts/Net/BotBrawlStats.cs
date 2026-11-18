namespace Game.Net
{
    // What the bots do empty-handed and after fights (ADR-310, #374), for the perf log. Server only; the match start clears it.
    public static class BotBrawlStats
    {
        public static int UnarmedEngages, FistFights, FleeStarts, FleeBroke, FleeCaught, CorpseTrips, CorpseArrived, CorpseGaveUp, PeekCycles, ExposeEpisodes, ExposeCutShort, CoverSearches, NoiseHeardBots, NoiseInvestigatedBots;
        public static float ExposeSeconds, HideSeconds, EngagedSeconds;
        // Seconds the bots that finished farming spent in each BotState (index = state), and in total.
        public static readonly float[] StateSeconds = new float[8];

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            UnarmedEngages = FistFights = FleeStarts = FleeBroke = FleeCaught = CorpseTrips = CorpseArrived = CorpseGaveUp = PeekCycles = ExposeEpisodes = ExposeCutShort = CoverSearches = NoiseHeardBots = NoiseInvestigatedBots = 0;
            ExposeSeconds = HideSeconds = EngagedSeconds = 0f;
            System.Array.Clear(StateSeconds, 0, StateSeconds.Length);
        }

        public static float StateShare(int state)
        {
            float total = 0f;
            foreach (var s in StateSeconds) total += s;
            return total > 0f ? StateSeconds[state] / total : 0f;
        }
    }
}

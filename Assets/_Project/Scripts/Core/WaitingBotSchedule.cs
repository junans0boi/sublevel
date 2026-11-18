using System;

namespace Game.Core
{
    // How many of the match's bots stand on the island at a given moment (ADR-384, #417): a few at first, more as the clock runs,
    // never all of them (the rest are filled in at the end, ADR-380), and never more than the room has left for them. Pure.
    public static class WaitingBotSchedule
    {
        public const int OpeningBots = 3;           // there at the first sight, so the island is not empty
        public const float FinalShare = 0.85f;      // of the bots the match needs, on the island by the end of a full countdown
        public const float EaseExponent = 1.5f;     // slow start, quicker later

        // `elapsed`: seconds since the first person came; `maxWait`: the countdown's length; `botsNeeded`: BotFill.Plan for the people now.
        public static int BotsAt(float elapsed, float maxWait, int botsNeeded, int people, int participants)
        {
            if (botsNeeded <= 0 || people <= 0) return 0;
            float t = maxWait > 0f ? Math.Min(1f, Math.Max(0f, elapsed / maxWait)) : 1f;
            int byClock = (int)Math.Floor(botsNeeded * FinalShare * Math.Pow(t, EaseExponent));
            int n = Math.Max(Math.Min(OpeningBots, botsNeeded), byClock);
            int room = Math.Max(0, Math.Min(participants, MatchRules.MaxParticipantsLimit) - people);   // beside the people
            return Math.Min(Math.Min(n, botsNeeded), room);
        }
    }
}

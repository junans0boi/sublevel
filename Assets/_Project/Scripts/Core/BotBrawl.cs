using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One place a bot could run to, judged against the one it flees from.
    public struct FleeCandidate
    {
        public BotVec Point;
        public bool Hidden;          // the threat has no clear line to it
        public float WalkMeters;     // from where the bot stands
    }

    // Running from an armed enemy without a weapon (ADR-310, #374). Pure: the controller supplies the candidates (it does the ray casts).
    public static class BotFlee
    {
        // Higher is better; -infinity for a place that means running at the threat. Out of its sight beats everything,
        // the nearer the better (it is the sooner there); with nothing hidden, the one that opens the most distance.
        public static float Score(in FleeCandidate c, BotVec from, BotVec threat)
        {
            float now = BotVec.PlanarDistance(from, threat);
            float after = BotVec.PlanarDistance(c.Point, threat);
            if (after < now - 2f) return float.NegativeInfinity;
            return c.Hidden ? 1000f - c.WalkMeters : after - now;
        }

        // Index of the best candidate, -1 when there is none that does not run at the threat.
        public static int Pick(IReadOnlyList<FleeCandidate> candidates, BotVec from, BotVec threat)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                float s = Score(candidates[i], from, threat);
                if (s > bestScore) { bestScore = s; best = i; }
            }
            return best;
        }
    }

    // Going to where somebody fell, for what dropped there (ADR-310, #374).
    public static class BotCorpse
    {
        // `sinceShot`: seconds since the last gunshot heard near the place (large when none). The bot goes when it still lacks gear, the
        // place is near and fresh, nobody on their feet is in sight or was just seen, and the shooting there has stopped.
        public static bool Approach(bool lacksGear, float meters, double deathAge, double sinceShot, bool enemyRecent, BotRules rules)
        {
            if (!lacksGear || enemyRecent) return false;
            if (meters > rules.CorpseLootMeters || deathAge > rules.CorpseMemorySeconds) return false;
            return sinceShot >= rules.CorpseQuietSeconds;
        }

        // The freshest-and-nearest of the remembered places: smaller is better. Used to pick among several.
        public static float Rank(float meters, double deathAge) => meters + (float)deathAge * 0.5f;
    }
}

namespace Game.Core
{
    // The peace of the first minutes (ADR-315): until EarlyPeaceSeconds after the drop a bot does not see enemies farther than EarlyPeaceMeters
    // as targets unless it was hurt in the last EarlyPeaceHurtSeconds. The ones next to each other still fight; the rest farm.
    public static class BotPeace
    {
        // `extraSeconds`: this bot's own share of EarlyPeaceSpreadSeconds, so the bots do not all start fighting at the same moment.
        public static bool Holds(double sinceDrop, double sinceHurt, BotRules rules, float extraSeconds = 0f) =>
            sinceDrop >= 0 && sinceDrop < rules.EarlyPeaceSeconds + extraSeconds && sinceHurt > rules.EarlyPeaceHurtSeconds;

        // Two bots with nothing in their hands do not start a fist fight in the peace at any distance: those fights killed most bots within seconds of the drop.
        public static bool Ignores(bool peace, float meters, bool bothUnarmed, BotRules rules) => peace && (bothUnarmed || meters > rules.EarlyPeaceMeters);
    }
}

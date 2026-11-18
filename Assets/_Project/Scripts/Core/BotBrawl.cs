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

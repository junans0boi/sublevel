using System;
using System.Collections.Generic;

namespace Game.Core
{
    // What a gun asks of its user (ADR-311, #375). Worked out from the weapon's data, so a new weapon needs no entry here.
    public enum BotWeaponStyle { Rifle = 0, Sniper = 1, Dmr = 2, Smg = 3, Shotgun = 4, Pistol = 5 }

    public static class BotWeapons
    {
        // Category 1 = sidearm; ammo 2 = shotgun shells; a slow heavy single shot = sniper; a semi-auto of the rifle calibres with a
        // big hit = marksman rifle; light-calibre automatic = SMG; the rest are rifles.
        public static BotWeaponStyle StyleOf(int category, int ammo, bool automatic, float damage, float fireInterval)
        {
            if (category == 1) return BotWeaponStyle.Pistol;
            if (ammo == 2) return BotWeaponStyle.Shotgun;
            if (!automatic && damage >= 80f) return BotWeaponStyle.Sniper;
            if (!automatic && damage >= 45f) return BotWeaponStyle.Dmr;
            if (ammo == 0 && automatic) return BotWeaponStyle.Smg;
            return BotWeaponStyle.Rifle;
        }

        // The distance the bot likes to fight at (data: BotRules.Prefer*), at most the weapon's own reach.
        public static float PreferredMeters(BotWeaponStyle style, float weaponRange, BotRules rules)
        {
            float meters;
            switch (style)
            {
                case BotWeaponStyle.Sniper: meters = rules.PreferSniperMeters; break;
                case BotWeaponStyle.Dmr: meters = rules.PreferDmrMeters; break;
                case BotWeaponStyle.Smg: meters = rules.PreferSmgMeters; break;
                case BotWeaponStyle.Shotgun: meters = rules.PreferShotgunMeters; break;
                case BotWeaponStyle.Pistol: meters = rules.PreferPistolMeters; break;
                default: meters = rules.PreferredMeters; break;
            }
            return Math.Min(meters, weaponRange * rules.EngageRangeFactor);
        }

        // Guns that are fought from behind cover (the close-range ones rush instead; a pistol hardly ever leaves it).
        public static bool UsesCover(BotWeaponStyle style) => style != BotWeaponStyle.Smg && style != BotWeaponStyle.Shotgun;
    }

    // One place to hide from the enemy and the spots beside it from which the bot sees the enemy again.
    public struct CoverCandidate
    {
        public BotVec Hide;
        public float WalkMeters;       // to the hiding place from where the bot stands
        public bool PeekA, PeekB;      // a peek spot on each side has a clear line to the enemy
        public float PeekMeters;       // from the hiding place to those spots
    }

    public enum PeekPhase { None = 0, Expose = 1, Hide = 2, Peek = 3 }

    public static class BotCover
    {
        // Higher is better; -infinity when the place is no use. It needs a way to shoot again (a peek spot); a place with both
        // sides is worth more (the bot can swap sides); near beats far; one that means running at the enemy does not count.
        public static float Score(in CoverCandidate c, BotVec from, BotVec threat)
        {
            if (!c.PeekA && !c.PeekB) return float.NegativeInfinity;
            float now = BotVec.PlanarDistance(from, threat);
            float after = BotVec.PlanarDistance(c.Hide, threat);
            if (after < now - 2f) return float.NegativeInfinity;
            return (c.PeekA && c.PeekB ? 20f : 0f) - c.WalkMeters - c.PeekMeters;
        }

        public static int Pick(IReadOnlyList<CoverCandidate> candidates, BotVec from, BotVec threat)
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
}

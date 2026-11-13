using System;

namespace Game.Core
{
    // Which part of a combatant a hit landed on (ADR-053). Limb means the legs for now: zones come from the
    // height of the hit point alone, so arms count as body until hits are resolved per bone.
    public enum HitZone { Body = 0, Head = 1, Limb = 2 }

    // What hurt a combatant. Melee: a punch (ADR-059), later the melee slot's weapons.
    public enum DamageCause { Shot = 0, Fall = 1, Blast = 2, Zone = 3, Melee = 4, Bleed = 5 }

    // Height bands of a combatant, measured up from the feet. A lowered stance (crouch) shrinks them.
    [Serializable]
    public class HitZoneRules
    {
        public float StandingHeight = 1.8f; // top of the head when upright
        public float HeadMeters = 0.3f;     // the head is this much at the top, whatever the stance
        public float LimbFraction = 0.45f;  // below this share of the current height: legs
        public float MinHeight = 0.6f;      // a stance never makes the body shorter than this

        // Top of the head for a stance whose eyes sit `stanceOffset` metres away from upright (negative = lower).
        public float Height(float stanceOffset) => Math.Max(MinHeight, StandingHeight + stanceOffset);

        public float HeadFrom(float stanceOffset) => Height(stanceOffset) - HeadMeters;

        public float LimbBelow(float stanceOffset) => Height(stanceOffset) * LimbFraction;
    }

    public static class HitZones
    {
        // `heightAboveFeet`: the hit point's height over the victim's feet. `stanceOffset`: the victim's
        // current eye height offset (0 standing, negative when crouched).
        public static HitZone At(float heightAboveFeet, float stanceOffset, HitZoneRules rules)
        {
            if (heightAboveFeet >= rules.HeadFrom(stanceOffset)) return HitZone.Head;
            if (heightAboveFeet < rules.LimbBelow(stanceOffset)) return HitZone.Limb;
            return HitZone.Body;
        }

        // The weapon's damage multiplier for a zone.
        public static float Multiplier(WeaponDef weapon, HitZone zone) =>
            zone == HitZone.Head ? weapon.HeadFactor : zone == HitZone.Limb ? weapon.LimbFactor : weapon.BodyFactor;
    }
}

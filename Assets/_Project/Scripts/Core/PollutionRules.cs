using System;

namespace Game.Core
{
    // Where a region stands in the pollution (SPEC 3, ADR-169): safe -> warning (announced) -> contaminated (confirmed).
    // Inside a contaminated region the front decides which cells hurt.
    public enum PollutionState : byte { Safe = 0, Warning = 1, Contaminated = 2 }

    // Tuning of the pollution, the elevator keys and the revive chips (ADR-169/170/171). Minutes are match time
    // unless the name says Seconds. Every value is data so playtests can move it (SPEC 3 marks them [proposal]).
    [Serializable]
    public class PollutionRules
    {
        public static PollutionRules Current = new PollutionRules();

        // ---- Timeline ----
        public float[] ConfirmMinutes = { 4f, 7.5f, 10.5f, 13f, 15f, 16.5f }; // closing k is confirmed at [k]; the last one is the final region
        public float WarningLeadSeconds = 105f;                                // siren, announcement, map blink before a confirmation
        public float MinConfirmGapMinutes = 0.5f;                              // the table is stretched to keep at least this gap

        // ---- Front speed (metres per minute, pollution_sim.html DEF) ----
        public float FlatSpeed = 60f;       // slope <= FlatMaxDeg
        public float RoadSpeed = 100f;      // paved road and bridge; an unpaved road is the mean of RoadSpeed and FlatSpeed
        public float SteepSpeed = 30f;      // slope >= SteepMinDeg; between the two it is interpolated
        public float FlatMaxDeg = 15f;
        public float SteepMinDeg = 35f;
        public float RiverFactor = 0.6f;    // on a river cell, before roads
        public float StageAccel = 0.05f;    // region closing k runs (1 + StageAccel * k) times faster
        public float FinalFactor = 0.7f;    // extra factor in the final region (about 42 m/min)

        // ---- Damage (health per second at 100 hp), by number of confirmed regions 1..6 ----
        public float[] Dps = { 2f, 3.2f, 5f, 8f, 12.6f, 20f };
        public float TickSeconds = 0.5f;    // damage is applied in steps this long

        // ---- Gas protection (grilling 2026-11-16): consumables that cut the damage for a while, never in the final region ----
        public float FilterSeconds = 60f;    // gas-mask filter
        public float FilterFactor = 0.5f;
        public float AntidoteSeconds = 25f;  // antidote
        public float AntidoteFactor = 0.1f;

        // ---- Keys (ADR-170) ----
        public int KeysMin = 6;
        public int KeysMax = 8;
        public float KeyBias = 0.5f;            // fewer keys in regions that close early
        public int KeysFromSupplyCrates = 1;    // at least this many of the keys are in supply crates; one more with 50 %
        public float DoorOpenSeconds = 30f;     // doors open, anybody may board
        public float SelectSeconds = 15f;       // the key holder picks a region, else the use is void
        public float RechargeSeconds = 90f;     // a station in a contaminated region is dead this long after one use
        public float TeamCooldownSeconds = 180f; // a team cannot use another key for this long after a ride
        public int CarMax = 4;

        // ---- Chips (ADR-171) ----
        public float ChipSeconds = 90f;         // from death: pick up and put into a box, or it is gone
        public float ChipReviveHp = 10f;

        // Confirmation time of closing k in seconds, stretched so neighbours are at least MinConfirmGapMinutes apart.
        public float[] ConfirmSeconds(int count)
        {
            var t = new float[count];
            for (int k = 0; k < count; k++)
            {
                float m = ConfirmMinutes == null || ConfirmMinutes.Length == 0 ? 0f : ConfirmMinutes[Math.Min(k, ConfirmMinutes.Length - 1)];
                if (k > 0 && m < t[k - 1] / 60f + MinConfirmGapMinutes) m = t[k - 1] / 60f + MinConfirmGapMinutes;
                t[k] = m * 60f;
            }
            return t;
        }

        // Health per second inside the contaminated area once `confirmed` regions are confirmed (0 before the first).
        public float DamagePerSecond(int confirmed)
        {
            if (confirmed <= 0 || Dps == null || Dps.Length == 0) return 0f;
            return Dps[Math.Min(confirmed, Dps.Length) - 1];
        }
    }

    // A gas-protection effect in progress on one combatant. Server time in seconds.
    public struct GasCover
    {
        public double Until;
        public float Factor;

        public static GasCover Filter(double now, PollutionRules r) => new GasCover { Until = now + r.FilterSeconds, Factor = r.FilterFactor };
        public static GasCover Antidote(double now, PollutionRules r) => new GasCover { Until = now + r.AntidoteSeconds, Factor = r.AntidoteFactor };

        // Using a second item keeps whichever protects better: a stronger factor wins while it lasts, else the later end.
        public GasCover With(GasCover other, double now)
        {
            bool aLive = Until > now, bLive = other.Until > now;
            if (!bLive) return this;
            if (!aLive) return other;
            if (other.Factor < Factor) return other;
            if (other.Factor == Factor && other.Until > Until) return other;
            return this;
        }

        // Damage multiplier now: 1 outside the cover and always 1 in the final region.
        public float Multiplier(double now, bool inFinalRegion) => inFinalRegion || Until <= now ? 1f : Factor;
    }
}

using System;
using System.Collections.Generic;

namespace Game.Core
{
    // ADR-395 (#196): a dead body falls as a client-side ragdoll. Visual only: the server never sees it, and loot, the
    // death crate and the chip stay at the server's death position. These are the pure rules; CombatantRagdoll runs them.
    public sealed class RagdollRules
    {
        public float DelaySeconds = 0.08f;   // the death clip shows this long first, so the killing shot can arrive
        public float HitMemorySeconds = 0.5f; // a hit older than this at the switch does not push the body
        public int MaxActive = 6;            // bodies simulated at once; the rest keep the death clip
        public float MaxMeters = 60f;        // farther from the camera than this: the death clip
        public float ImpulsePerHit = 18f;    // N*s per bullet / punch (a shotgun's pellets add up)
        public float MaxImpulse = 60f;
        public float BlastImpulse = 90f;     // a grenade within BlastMeters, falling off with distance
        public float BlastMeters = 8f;
        public float Lift = 0.25f;           // share of the push turned upward, so a body tips over instead of sliding
        public float FallbackImpulse = 6f;   // no known hit (zone, fall): a small push backwards so it does not fold in place
        public float StillSpeed = 0.15f;     // m/s: every body slower than this counts as still
        public float StillSeconds = 0.6f;    // still this long: frozen
        public float MaxSeconds = 5f;        // simulated at most this long
        public float MaxDriftMeters = 2.5f;  // hips farther than this from the death position: frozen where they are

        public static RagdollRules Current = new RagdollRules();
    }

    public static class RagdollSwitch
    {
        // The death clip turns into a ragdoll only for a death this side saw happen (someone already dead when first
        // seen keeps the clip), out of the water (no buoyancy: it would sink through the surface), out of a seat, with
        // a built rig, once the delay is over.
        public static bool MayStart(bool sawDeath, bool swimming, bool seated, bool hasRig, float sinceDeath, RagdollRules rules) =>
            sawDeath && !swimming && !seated && hasRig && sinceDeath >= rules.DelaySeconds;
    }

    public static class RagdollBudget
    {
        public const int Reject = -2, Free = -1;

        // A new body at `distance` from the camera asks for a slot among the ones simulating at `active` distances.
        // Free: a slot is open. Reject: too far, or every simulated body is nearer. Otherwise the index of the
        // farthest simulated body, which is frozen early to make room.
        public static int Admit(IReadOnlyList<float> active, float distance, RagdollRules rules)
        {
            if (rules.MaxActive <= 0 || !(distance <= rules.MaxMeters)) return Reject;
            if (active.Count < rules.MaxActive) return Free;
            int farthest = -1;
            for (int i = 0; i < active.Count; i++)
                if (farthest < 0 || active[i] > active[farthest]) farthest = i;
            return active[farthest] > distance ? farthest : Reject;
        }
    }

    // Decides when a simulating body is frozen into its pose.
    public struct RagdollSettle
    {
        public float Age, Still;

        // `fastest` = speed of the fastest part (m/s), `drift` = hips' distance from the death position (m).
        public bool Tick(float fastest, float drift, float dt, RagdollRules rules)
        {
            Age += dt;
            Still = fastest < rules.StillSpeed ? Still + dt : 0f;
            return Still >= rules.StillSeconds || Age >= rules.MaxSeconds || drift > rules.MaxDriftMeters || float.IsNaN(drift);
        }
    }

    public static class RagdollImpulse
    {
        // The push given to the body: along the hit direction (dx,dy,dz, any length) scaled by the hits, capped, with
        // some lift. With no direction it is a small push opposite the body's facing (fx,fz).
        public static void Hit(float dx, float dy, float dz, int hits, float fx, float fz, RagdollRules rules,
            out float x, out float y, out float z)
        {
            float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            float strength;
            if (hits <= 0 || len < 1e-4f || float.IsNaN(len))
            {
                float f = (float)Math.Sqrt(fx * fx + fz * fz);
                if (f < 1e-4f) { fx = 0f; fz = 1f; f = 1f; }
                dx = -fx / f; dy = 0f; dz = -fz / f;
                strength = rules.FallbackImpulse;
            }
            else
            {
                dx /= len; dy /= len; dz /= len;
                strength = Math.Min(rules.ImpulsePerHit * hits, rules.MaxImpulse);
            }
            x = dx * strength;
            y = dy * strength + rules.Lift * strength;
            z = dz * strength;
        }

        // A blast at `distance`: full strength at the centre, nothing past BlastMeters.
        public static float Blast(float distance, RagdollRules rules) =>
            !(distance < rules.BlastMeters) ? 0f : rules.BlastImpulse * (1f - Math.Max(0f, distance) / rules.BlastMeters);
    }
}

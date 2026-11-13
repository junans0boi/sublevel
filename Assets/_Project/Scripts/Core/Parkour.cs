using System;

namespace Game.Core
{
    // What Space does in front of an obstacle (ADR-065 (4)). None = an ordinary jump (nothing to get over, or too
    // low to bother); Refuse = there is something but it cannot be got over (also an ordinary jump).
    public enum ParkourMove { None = 0, Vault = 1, Climb = 2, Window = 3, Refuse = 4 }

    // Parkour tuning, loaded from data (rules.json "Parkour").
    [Serializable]
    public class ParkourRules
    {
        public static ParkourRules Current = new ParkourRules();

        public float ProbeMeters = 0.7f;           // how far ahead of the body an obstacle is looked for
        // Low obstacles: vaulted over when no taller and no deeper than this; lower ones are simply jumped.
        public float VaultMinHeight = 0.5f;
        public float VaultMaxHeight = 1.0f;
        public float VaultMaxDepth = 0.8f;
        // Taller (or deeper) obstacles up to this height are climbed onto, if the top has room to stand.
        public float ClimbMaxHeight = 2.0f;
        public float ClimbClearance = 1.8f;        // free height needed over the top
        public float ClimbMinDepth = 0.6f;         // and this much top to stand on
        // A window: an opening in a wall with its sill in this band and at least this tall. Openings lower than
        // SmallWindowHeight are only gone through crouched.
        public float WindowSillMin = 0.9f;
        public float WindowSillMax = 1.1f;
        public float WindowMinOpening = 0.6f;
        public float SmallWindowHeight = 1.0f;
        // Durations (s): vault, window, climb at VaultMaxHeight rising to ClimbHighSeconds at ClimbMaxHeight.
        public float VaultSeconds = 0.55f;
        public float WindowSeconds = 0.65f;
        public float ClimbLowSeconds = 0.6f;
        public float ClimbHighSeconds = 0.9f;
        public float LiftMeters = 0.15f;           // the feet pass this far over the top
        public float CancelShare = 0.6f;           // S in this first share of the motion goes back to the start
        // Server sanity check (movement is the owner's, ADR-029): how far a parkour may carry a body.
        public float MaxHorizontalMeters = 3.2f;
        public float MaxRiseMeters = 2.3f;
        public float StartToleranceMeters = 1.5f;  // the server's view of a remote owner lags behind
        public float EndToleranceMeters = 0.6f;
        // Pose (CombatantAnimation): knees tucked, torso forward, arms reaching forward and down onto the top.
        public float PoseTuck = 0.8f;
        public float PoseTorsoPitchDeg = 18f;
        public float PoseArmReachDeg = 70f;
        public float PosePerSecond = 8f;
    }

    // What the probe in front of the body found, in metres from the feet. Built by the owner from ray casts; the
    // classification below is pure so its rules can be tested with literal numbers.
    public struct ObstacleProfile
    {
        public bool Blocked;          // something stands in front of the feet within the probe distance
        public float TopHeight;       // height of its top (of the sill, for an opening)
        public float Depth;           // how far its top goes on, along the facing
        public bool Opening;          // the wall continues above the gap: a window
        public float OpeningHeight;   // free height of that gap
        public float Clearance;       // free height over the top where the body would stand on it
    }

    public static class Parkour
    {
        // Which move Space makes for this obstacle. `crouched`: small windows need it.
        public static ParkourMove Classify(in ObstacleProfile p, bool crouched, ParkourRules r)
        {
            if (!p.Blocked) return ParkourMove.None;
            if (p.Opening)
            {
                bool sillFits = p.TopHeight >= r.WindowSillMin && p.TopHeight <= r.WindowSillMax;
                if (sillFits && p.OpeningHeight >= r.WindowMinOpening)
                    return p.OpeningHeight < r.SmallWindowHeight && !crouched ? ParkourMove.Refuse : ParkourMove.Window;
                // A gap in a wall that is not a window (a slot too narrow, a sill too high or too low) is not gone
                // through; a low one is still jumped at like any low obstacle.
                return p.TopHeight < r.VaultMinHeight ? ParkourMove.None : ParkourMove.Refuse;
            }
            if (p.TopHeight < r.VaultMinHeight) return ParkourMove.None;
            if (p.TopHeight > r.ClimbMaxHeight) return ParkourMove.Refuse;
            if (p.TopHeight <= r.VaultMaxHeight && p.Depth <= r.VaultMaxDepth) return ParkourMove.Vault;
            // Taller or deeper: onto it, if there is room to stand up there.
            bool room = p.Depth >= r.ClimbMinDepth && p.Clearance >= r.ClimbClearance;
            return room ? ParkourMove.Climb : ParkourMove.Refuse;
        }

        public static bool IsMotion(ParkourMove move) => move == ParkourMove.Vault || move == ParkourMove.Climb || move == ParkourMove.Window;

        // Seconds the move takes; a climb takes longer the higher it goes.
        public static float Duration(ParkourMove move, float topHeight, ParkourRules r)
        {
            switch (move)
            {
                case ParkourMove.Vault: return r.VaultSeconds;
                case ParkourMove.Window: return r.WindowSeconds;
                case ParkourMove.Climb:
                    float span = r.ClimbMaxHeight - r.VaultMaxHeight;
                    float k = span <= 0f ? 1f : Clamp01((topHeight - r.VaultMaxHeight) / span);
                    return r.ClimbLowSeconds + (r.ClimbHighSeconds - r.ClimbLowSeconds) * k;
                default: return 0f;
            }
        }

        // Where along the move the feet are at time share `t` (0..1): `along` is the share of the horizontal
        // distance covered, `height` the feet's height. `startY`, `endY`: feet at both ends; `topY`: the top.
        // Over: up and over in one arc, the feet clearing the top by `lift` at the middle. Climb: up first, close
        // to the face, then forward onto the top.
        public static void PathPoint(ParkourMove move, float t, float startY, float endY, float topY, float lift, out float along, out float height)
        {
            t = Clamp01(t);
            if (move == ParkourMove.Climb)
            {
                height = startY + (endY - startY) * Smooth(Clamp01(t / 0.65f));
                along = Smooth(Clamp01((t - 0.4f) / 0.6f));
                return;
            }
            along = Smooth(t);
            float straight = startY + (endY - startY) * t;
            float mid = startY + (endY - startY) * 0.5f;
            float bump = Math.Max(0f, topY + lift - mid);
            height = straight + bump * (float)Math.Sin(Math.PI * t);
        }

        // May S still take the move back?
        public static bool CanCancel(float t, ParkourRules r) => t < r.CancelShare;

        // Server sanity check of a finished (or started) parkour as the owner reports it.
        public static bool Plausible(float dx, float dy, float dz, ParkourRules r)
        {
            if (float.IsNaN(dx) || float.IsNaN(dy) || float.IsNaN(dz)) return false;
            return dx * dx + dz * dz <= r.MaxHorizontalMeters * r.MaxHorizontalMeters && dy <= r.MaxRiseMeters;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}

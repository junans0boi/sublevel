using System;

namespace Game.Core
{
    // Doors that open and close (ADR-104, G11). Closed: 0. OpenAhead: the free edge swings to the opening's FacingDeg side,
    // OpenBack: to the other side. A door always opens away from whoever pushes it. Broken (ADR-147): shot, hit or rammed
    // to pieces, an empty doorway for good.
    public enum DoorState : byte { Closed = 0, OpenAhead = 1, OpenBack = 2, Broken = 3 }

    [Serializable]
    public class DoorRules
    {
        public static DoorRules Current = new DoorRules();

        public float ReachMeters = 2.0f;      // F reaches a door whose middle is this close (flat distance)
        public float AimCosine = 0.5f;        // ... and that lies within this cosine of the view direction
        public float OpenSeconds = 0.35f;
        public float OpenAngleDeg = 95f;
        public float ClosedSoundFactor = 0.5f; // heard distance through one closed door is divided by this
        public float BotReachMeters = 1.8f;    // a bot opens a closed door this close on its way
        public float PanelThickness = 0.06f;
        // Destruction (ADR-147). A door has MaxHp; shots take the weapon's damage at the hit (as dealt to a body, falloff and
        // penetration included), a shotgun's pellets ShotgunFactor times that; melee takes MeleeFactor times the blow;
        // a vehicle at RamMinSpeed (closing m/s) or more takes RamDamagePerMps per m/s. Below Stage1Share of MaxHp the panel
        // is splintered, below Stage2Share its hinge is cracked and it sags.
        public float MaxHp = 400f;
        public float ShotgunFactor = 1.5f;
        public float MeleeFactor = 1f;
        public float RamMinSpeed = 3f;
        public float RamDamagePerMps = 60f;
        public float Stage1Share = 0.66f;
        public float Stage2Share = 0.33f;
    }

    public static class Doors
    {
        // +1 when the point is on the FacingDeg side of the door's plane, -1 on the other side.
        public static int SideOf(float px, float pz, float doorX, float doorZ, float forwardX, float forwardZ) =>
            (px - doorX) * forwardX + (pz - doorZ) * forwardZ >= 0f ? 1 : -1;

        // What F does: a closed door opens away from the user, an open one closes.
        public static DoorState Toggle(DoorState state, int userSide) =>
            state == DoorState.Broken ? state : state != DoorState.Closed ? DoorState.Closed : userSide > 0 ? DoorState.OpenBack : DoorState.OpenAhead;

        // Panel yaw (degrees) about its hinge, in the door's own frame (z = ahead, x = right, hinge on the left).
        public static float TargetAngle(DoorState state, DoorRules rules) =>
            state == DoorState.OpenAhead ? -rules.OpenAngleDeg : state == DoorState.OpenBack ? rules.OpenAngleDeg : 0f;

        public static float StepAngle(float angle, float target, float dt, DoorRules rules)
        {
            float step = rules.OpenAngleDeg / Math.Max(1e-3f, rules.OpenSeconds) * dt;
            return angle < target ? Math.Min(target, angle + step) : Math.Max(target, angle - step);
        }

        // ---- Destruction (ADR-147) ----

        // Damage a shot does to a door: `dealt` is what it would do to a body there; a shotgun's pellets count more.
        public static float ShotDamage(float dealt, int pellets, DoorRules rules) =>
            Math.Max(0f, dealt) * (pellets > 1 ? rules.ShotgunFactor : 1f);

        public static float MeleeDamage(float blow, DoorRules rules) => Math.Max(0f, blow) * rules.MeleeFactor;

        public static float RamDamage(float closingSpeed, DoorRules rules) =>
            closingSpeed < rules.RamMinSpeed ? 0f : closingSpeed * rules.RamDamagePerMps;

        // 0 whole, 1 splintered, 2 hinge cracked, 3 broken (no hit points left).
        public static int Stage(float hp, DoorRules rules) =>
            hp <= 0f ? 3 : hp <= rules.MaxHp * rules.Stage2Share ? 2 : hp <= rules.MaxHp * rules.Stage1Share ? 1 : 0;

        // True while the panel is far enough shut to stop sight, bullets and bodies (it is a box in the way).
        public static bool Blocks(float angle, DoorRules rules) => Math.Abs(angle) < rules.OpenAngleDeg * 0.5f;

        // The door `index` that F reaches from an eye at (ex, ez) looking along (lx, lz): the nearest within reach and
        // aim. -1 when none. Doors: middles at floor level (x, y, z); the eye must be within a storey's height of the floor.
        public static int Pick(float[] xs, float[] ys, float[] zs, int count, float ex, float ey, float ez, float lx, float lz, DoorRules rules)
        {
            float len = (float)Math.Sqrt(lx * lx + lz * lz);
            if (len < 1e-4f) return -1;
            lx /= len; lz /= len;
            int best = -1;
            float bestSqr = rules.ReachMeters * rules.ReachMeters;
            for (int i = 0; i < count; i++)
            {
                float rise = ey - ys[i];
                if (rise < 0.4f || rise > 2.6f) continue;
                float dx = xs[i] - ex, dz = zs[i] - ez;
                float sqr = dx * dx + dz * dz;
                if (sqr > bestSqr) continue;
                float d = (float)Math.Sqrt(sqr);
                // Right at the door the direction means little: anything inside half a metre counts as aimed.
                if (d > 0.5f && (dx * lx + dz * lz) / d < rules.AimCosine) continue;
                best = i;
                bestSqr = sqr;
            }
            return best;
        }

        // Sound through doors: each closed door on the way divides the distance it is heard from by ClosedSoundFactor.
        public static float HeardMeters(float meters, int closedDoors, DoorRules rules)
        {
            for (int i = 0; i < closedDoors; i++) meters /= Math.Max(0.05f, rules.ClosedSoundFactor);
            return meters;
        }

        // Does the flat segment a-b cross the door's panel line (centre cx, cz; half width hw along (rx, rz))?
        public static bool Crosses(float ax, float az, float bx, float bz, float cx, float cz, float rx, float rz, float hw)
        {
            float px = cx - rx * hw, pz = cz - rz * hw, qx = cx + rx * hw, qz = cz + rz * hw;
            float d1 = Cross(bx - ax, bz - az, px - ax, pz - az), d2 = Cross(bx - ax, bz - az, qx - ax, qz - az);
            float d3 = Cross(qx - px, qz - pz, ax - px, az - pz), d4 = Cross(qx - px, qz - pz, bx - px, bz - pz);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        static float Cross(float ux, float uz, float vx, float vz) => ux * vz - uz * vx;
    }
}

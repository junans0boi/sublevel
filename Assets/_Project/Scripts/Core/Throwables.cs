using System;

namespace Game.Core
{
    // The kinds of thrown item. The flight is shared; what happens when the fuse runs out differs.
    public enum ThrowableKind { Frag = 0, Smoke = 1 }

    // Tuning of thrown items (PUBG_GAP 4-3 batch I). The fragmentation grenade's numbers; the smoke grenade
    // reuses the same flight (its own numbers are SmokeRules).
    [Serializable]
    public class ThrowableRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static ThrowableRules Current = new ThrowableRules();

        // The loot kind of a grenade pickup.
        public const LootKind GrenadeLootKind = LootKind.Grenade;
        // Shown in the inventory and on pickups.
        public const string GrenadeName = "파편 수류탄";
        // The smoke grenade's loot kind.
        // Its own numbers (fuse, cloud) are SmokeRules.
        public const LootKind SmokeLootKind = LootKind.Smoke;
        public const string SmokeName = "연막탄";
        public const string FlashName = "섬광탄";
        public const string MolotovName = "화염병";

        public int MaxCarried = 3;
        public float FuseSeconds = 5f;          // counted from the pin (cooking runs it in the hand), ADR-101
        public float ThrowSpeed = 18f;          // m/s as it leaves the hand
        public float LoftDeg = 10f;             // the throw leaves this much above where the thrower looks
        public float MinThrowInterval = 1f;     // seconds between two throws of one combatant
        public float Gravity = 9.81f;           // m/s^2, downwards
        public float Radius = 0.07f;            // of the grenade itself, for collisions
        public float ArmDistance = 1f;          // flown this far before it can touch its own thrower

        // A bounce keeps this share of the speed into the surface (turned around) and along it.
        public float Restitution = 0.35f;
        public float SurfaceFriction = 0.7f;
        public float RestSpeed = 0.8f;          // slower than this after landing on a floor: it stays there
        public float FloorNormalY = 0.6f;       // a surface at least this upright-facing counts as a floor

        // Lethal / medium / weak rings (ADR-101, PUBG ratio): full damage inside Inner, down to MidDamage at Mid, to
        // nothing at Outer. Lying down takes ProneReduction off; a vest takes its own share without wearing.
        public float BlastDamage = 100f;
        public float BlastInnerRadius = 3.5f;
        public float BlastMidRadius = 8.5f;
        public float BlastMidDamage = 40f;
        public float BlastOuterRadius = 10f;
        public float ProneReduction = 0.2f;

        // A rolled grenade leaves the hand low at this speed along the ground, from this far above the feet.
        public float RollSpeed = 7f;
        public float RollHandHeight = 0.35f;

        // Preview arc: how finely and how far ahead the flight is drawn.
        public float PreviewStepSeconds = 0.06f;
        public float PreviewSeconds = 2.6f;
    }

    public static class Throwables
    {
        // Where a throw is after `t` seconds of free flight, one axis at a time.
        public static float HorizontalAt(float origin, float velocity, float t) => origin + velocity * t;

        public static float HeightAt(float originY, float velocityY, float gravity, float t) =>
            originY + velocityY * t - 0.5f * gravity * t * t;

        // Free flight from an origin with a velocity, gravity pulling along -Y.
        public static void PositionAt(float ox, float oy, float oz, float vx, float vy, float vz, float gravity, float t,
            out float x, out float y, out float z)
        {
            x = HorizontalAt(ox, vx, t);
            y = HeightAt(oy, vy, gravity, t);
            z = HorizontalAt(oz, vz, t);
        }

        // Seconds until a throw is back at the height it left from (0 when thrown level or downwards).
        public static float FlightTime(float velocityY, float gravity) =>
            velocityY <= 0f || gravity <= 0f ? 0f : 2f * velocityY / gravity;

        // How many points the preview arc has, the first being the hand.
        public static int PreviewPoints(ThrowableRules rules) =>
            rules.PreviewStepSeconds <= 0f ? 2 : Math.Max(2, (int)Math.Ceiling(rules.PreviewSeconds / rules.PreviewStepSeconds) + 1);

        // The velocity after hitting a surface with unit normal n: the part into the surface is turned around and
        // damped, the part along it is slowed.
        public static void Bounce(ref float vx, ref float vy, ref float vz, float nx, float ny, float nz, ThrowableRules rules)
        {
            float into = vx * nx + vy * ny + vz * nz;
            if (into >= 0f) return; // already moving away
            float tx = vx - into * nx, ty = vy - into * ny, tz = vz - into * nz;
            float back = -into * rules.Restitution;
            vx = tx * rules.SurfaceFriction + nx * back;
            vy = ty * rules.SurfaceFriction + ny * back;
            vz = tz * rules.SurfaceFriction + nz * back;
        }

        // After a bounce: slow enough on a floor to stay where it is.
        public static bool ComesToRest(float vx, float vy, float vz, float normalY, ThrowableRules rules) =>
            normalY >= rules.FloorNormalY && vx * vx + vy * vy + vz * vz < rules.RestSpeed * rules.RestSpeed;

        // Blast damage at a distance from the centre: full inside the inner radius, then linear down to nothing
        // at the outer radius.
        public static float BlastDamage(float distance, ThrowableRules rules)
        {
            if (distance <= rules.BlastInnerRadius) return rules.BlastDamage;
            if (distance >= rules.BlastOuterRadius || rules.BlastOuterRadius <= rules.BlastInnerRadius) return 0f;
            float mid = Math.Min(Math.Max(rules.BlastMidRadius, rules.BlastInnerRadius), rules.BlastOuterRadius);
            if (distance <= mid)
                return mid <= rules.BlastInnerRadius ? rules.BlastDamage
                    : rules.BlastDamage + (rules.BlastMidDamage - rules.BlastDamage) * (distance - rules.BlastInnerRadius) / (mid - rules.BlastInnerRadius);
            return rules.BlastMidDamage * (rules.BlastOuterRadius - distance) / (rules.BlastOuterRadius - mid);
        }

        // What reaches the body: lying down and a vest both cut the blast, the vest by `vestReduction` (0..1).
        public static float AdjustBlast(float damage, bool prone, float vestReduction, ThrowableRules rules)
        {
            float v = vestReduction < 0f ? 0f : vestReduction > 1f ? 1f : vestReduction;
            return damage * (prone ? 1f - rules.ProneReduction : 1f) * (1f - v);
        }

        // Seconds a grenade still has to burn after `cooked` seconds in the hand (0 = it goes off in the hand).
        public static float FuseLeft(float fuse, float cooked) => Math.Max(0f, fuse - Math.Max(0f, cooked));

        // A roll goes level along the ground whatever the pitch of the view; zero when looking straight up or down.
        public static void RollVelocity(float dx, float dy, float dz, ThrowableRules rules, out float vx, out float vz)
        {
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f) { vx = vz = 0f; return; }
            vx = dx / len * rules.RollSpeed;
            vz = dz / len * rules.RollSpeed;
        }

        public static string NameOf(ThrowableKind kind) => kind == ThrowableKind.Smoke ? ThrowableRules.SmokeName : ThrowableRules.GrenadeName;

        public static LootKind LootKindOf(ThrowableKind kind) => kind == ThrowableKind.Smoke ? ThrowableRules.SmokeLootKind : ThrowableRules.GrenadeLootKind;

        // Seconds from the throw to the effect.
        public static float FuseOf(ThrowableKind kind, ThrowableRules rules, SmokeRules smoke) => kind == ThrowableKind.Smoke ? smoke.FuseSeconds : rules.FuseSeconds;

        // The next kind when cycling the selection (the T key).
        public static ThrowableKind Next(ThrowableKind kind) => kind == ThrowableKind.Frag ? ThrowableKind.Smoke : ThrowableKind.Frag;

        // How many of `offered` grenades fit next to the `carried` ones.
        public static int Room(int carried, int offered, ThrowableRules rules) =>
            Math.Max(0, Math.Min(offered, rules.MaxCarried - carried));

        // Its own thrower is in the way only once it has flown a little: it leaves from inside their reach.
        public static bool TouchesThrower(float flownMeters, ThrowableRules rules) => flownMeters >= rules.ArmDistance;
    }
}

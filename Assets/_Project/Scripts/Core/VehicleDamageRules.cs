using System;

namespace Game.Core
{
    // Where a hit on a vehicle landed (#90, ADR-067): the zone picks the multiplier on the vehicle's health.
    public enum VehicleHitZone { Body = 0, Engine = 1, Roof = 2, Glass = 3, Tyre = 4 }

    // The vehicle's condition as everyone sees it: smoke, fire, then the wreck (#90, ADR-067, ADR-074).
    public enum VehicleDamageStage { Healthy = 0, WhiteSmoke = 1, BlackSmoke = 2, Burning = 3, Wrecked = 4 }

    // Detachable body parts (#90): only models built with them as separate meshes have them.
    public enum VehiclePart { Hood = 0, Trunk = 1, BumperFront = 2, BumperRear = 3 }

    // Per part: still fixed, hanging loose, gone.
    public enum PartState { Fixed = 0, Dangling = 1, Fallen = 2 }

    // Vehicle damage tuning (#90, ADR-067, ADR-074). rules.json "VehicleDamage" = cars, "MotorcycleDamage" = motorcycles.
    [Serializable]
    public class VehicleDamageRules
    {
        public static VehicleDamageRules Current = new VehicleDamageRules();
        public static VehicleDamageRules Motorcycle = MotorcycleDefaults();

        public static VehicleDamageRules For(VehicleKind kind) => kind == VehicleKind.Motorcycle ? Motorcycle : Current;

        public static VehicleDamageRules MotorcycleDefaults() => new VehicleDamageRules
        {
            MaxHealth = 450f, EngineMinZ01 = 0.3f, EngineMaxZ01 = 0.65f, EngineMaxY01 = 0.6f, RoofMinY01 = 2f,
            FlatDropMeters = 0.05f, FlatFrontPullDeg = 0f, FlatRearPullDeg = 0f, FlatGrip = 0.45f,
            DeformScale = 0.4f, CollisionDamagePerMps = 14f,
        };

        // Health and zones (ADR-067: engine 100%, body 75%, roof 50%).
        public float MaxHealth = 1000f;
        public float EngineMultiplier = 1f;
        public float BodyMultiplier = 0.75f;
        public float RoofMultiplier = 0.5f;
        public float BulletFactor = 1f;          // weapon damage x zone multiplier x this
        // Zones in the body's bounds, 0..1 along each axis (z01 = 1 at the nose): the engine is a box, the roof a layer.
        public float EngineMinZ01 = 0.7f;
        public float EngineMaxZ01 = 1f;
        public float EngineMaxY01 = 0.62f;
        public float RoofMinY01 = 0.82f;

        // Stages by health share; at 0 the fire burns this long before the explosion.
        public float WhiteSmokeBelow01 = 0.5f;
        public float BlackSmokeBelow01 = 0.25f;
        public float BurnSeconds = 5f;
        public float BlackSmokeTopSpeedFactor = 0.85f;

        // The explosion: Throwables.BlastDamage with these numbers; anyone still seated takes OccupantBlastDamage.
        public float ExplosionDamage = 100f;
        public float ExplosionInnerRadius = 2.5f;
        public float ExplosionOuterRadius = 8f;
        public float OccupantBlastDamage = 250f;
        public float ExplosionToVehicleFactor = 4f; // a vehicle blast on other vehicles, like a grenade's

        // Grenades on the vehicle: blast damage x this; a blast this close (to the body's surface) wrecks it at once.
        public float GrenadeToVehicleFactor = 6f;
        public float InstantWreckRadius = 1.2f;
        public float GlassBlastRadius = 5f;

        // Collisions (speed along the contact normal, m/s): nothing below the minimum, then linear.
        public float CollisionDamageMinSpeed = 6f;
        public float CollisionDamagePerMps = 22f;
        public float MaxReportedImpact = 40f;

        // Glass: the first hit cracks a pane, this many shatter it.
        public int GlassHitsToShatter = 2;

        // Tyres: health per tyre (a bullet's damage), then flat.
        public float TyreHealth = 30f;
        public float FlatGrip = 0.55f;           // multiplies that wheel's grip
        public float FlatDropMeters = 0.09f;     // that corner sits lower
        public float FlatFrontPullDeg = 5f;      // steering pulled toward a flat front tyre's side
        public float FlatRearPullDeg = 2f;
        public float FlatTopSpeedFactor = 0.9f;  // per flat tyre

        // Detachable parts: health; loose (dangling) at half, gone at 0. Hits within PartReach of a part count on it.
        public float PartHealth = 120f;
        public float PartDangleBelow01 = 0.5f;
        public float PartReach = 0.25f;
        public float PartBlastFactor = 1.5f;     // of the blast damage the vehicle takes
        public float PartCollisionFactor = 1.5f;

        // Cosmetic (every side alike, driven by the replicated hit events).
        public int MaxDecals = 30;
        public float DecalSize = 0.07f;
        public float DeformScale = 1f;
        public float DentBulletRadius = 0.14f;
        public float DentBulletDepth = 0.008f;
        public float DentImpactRadiusPerMps = 0.04f; // radius = speed x this, capped
        public float DentImpactMaxRadius = 0.9f;
        public float DentImpactDepthPerMps = 0.006f;
        public float DentBlastRadius = 1.1f;
        public float DentBlastDepth = 0.07f;
        public float DentMaxDepth = 0.2f;        // no vertex goes further in than this in total
        public int DentHistory = 48;             // kept by the server for late joiners
    }

    // Pure vehicle damage rules (#90).
    public static class VehicleDamageMath
    {
        public const int PaneCount = 6; // 0 windshield, 1 rear, 2 left front, 3 left rear, 4 right front, 5 right rear
        public const int PartCount = 4;

        public static float Multiplier(VehicleHitZone zone, VehicleDamageRules r)
        {
            switch (zone)
            {
                case VehicleHitZone.Engine: return r.EngineMultiplier;
                case VehicleHitZone.Roof: return r.RoofMultiplier;
                case VehicleHitZone.Body: return r.BodyMultiplier;
                default: return 0f; // glass and tyres do not take the vehicle's health
            }
        }

        // The zone of a hit point given as 0..1 shares of the body's bounds (y01 up, z01 toward the nose).
        public static VehicleHitZone ZoneAt(float y01, float z01, VehicleDamageRules r)
        {
            if (y01 >= r.RoofMinY01) return VehicleHitZone.Roof;
            if (z01 >= r.EngineMinZ01 && z01 <= r.EngineMaxZ01 && y01 <= r.EngineMaxY01) return VehicleHitZone.Engine;
            return VehicleHitZone.Body;
        }

        public static float BulletDamage(float weaponDamage, VehicleHitZone zone, VehicleDamageRules r) =>
            Math.Max(0f, weaponDamage) * Multiplier(zone, r) * r.BulletFactor;

        public static float CollisionDamage(float impactSpeed, VehicleDamageRules r)
        {
            float speed = Math.Min(impactSpeed, r.MaxReportedImpact);
            return speed <= r.CollisionDamageMinSpeed ? 0f : (speed - r.CollisionDamageMinSpeed) * r.CollisionDamagePerMps;
        }

        // A stage from health alone (the fire and the wreck are entered by the server's timer, never left).
        public static VehicleDamageStage StageFor(float health, float maxHealth, VehicleDamageRules r)
        {
            if (health <= 0f) return VehicleDamageStage.Burning;
            float share = maxHealth > 0f ? health / maxHealth : 1f;
            if (share < r.BlackSmokeBelow01) return VehicleDamageStage.BlackSmoke;
            if (share < r.WhiteSmokeBelow01) return VehicleDamageStage.WhiteSmoke;
            return VehicleDamageStage.Healthy;
        }

        // Stages only move forward.
        public static VehicleDamageStage Advance(VehicleDamageStage current, VehicleDamageStage next) => next > current ? next : current;

        public static bool Disables(VehicleDamageStage stage) => stage >= VehicleDamageStage.Burning;

        public static float TopSpeedFactor(VehicleDamageStage stage, int flatTyres, VehicleDamageRules r)
        {
            float f = stage >= VehicleDamageStage.BlackSmoke ? r.BlackSmokeTopSpeedFactor : 1f;
            for (int i = 0; i < flatTyres; i++) f *= r.FlatTopSpeedFactor;
            return f;
        }

        public static PartState PartStateFor(float health, VehicleDamageRules r)
        {
            if (health <= 0f) return PartState.Fallen;
            return health < r.PartHealth * r.PartDangleBelow01 ? PartState.Dangling : PartState.Fixed;
        }

        // Glass: hits so far -> 0 whole, 1 cracked, 2 shattered.
        public static int GlassState(int hits, VehicleDamageRules r) => hits <= 0 ? 0 : hits >= Math.Max(1, r.GlassHitsToShatter) ? 2 : 1;

        // Steering pull in degrees (right positive) from the flat tyres. Car wheel order FL, FR, RL, RR; two wheels: none.
        public static float SteerPull(int flatMask, int wheels, VehicleDamageRules r)
        {
            if (wheels < 4) return 0f;
            float pull = 0f;
            if ((flatMask & 1) != 0) pull -= r.FlatFrontPullDeg;
            if ((flatMask & 2) != 0) pull += r.FlatFrontPullDeg;
            if ((flatMask & 4) != 0) pull -= r.FlatRearPullDeg;
            if ((flatMask & 8) != 0) pull += r.FlatRearPullDeg;
            return pull;
        }

        public static int CountBits(int mask)
        {
            int n = 0;
            for (; mask != 0; mask &= mask - 1) n++;
            return n;
        }

        // Two bits per part in one int.
        public static PartState GetPart(int packed, int part) => (PartState)((packed >> (part * 2)) & 3);
        public static int SetPart(int packed, int part, PartState state) => (packed & ~(3 << (part * 2))) | ((int)state << (part * 2));

        // Two bits per pane in one int (0 whole, 1 cracked, 2 shattered).
        public static int GetPane(int packed, int pane) => (packed >> (pane * 2)) & 3;
        public static int SetPane(int packed, int pane, int state) => (packed & ~(3 << (pane * 2))) | ((state & 3) << (pane * 2));

        // How far in a vertex at `distance` from a dent's centre goes: smooth falloff to 0 at the radius.
        public static float DentFalloff(float distance, float radius)
        {
            if (radius <= 0f || distance >= radius) return 0f;
            float t = 1f - distance / radius;
            return t * t * (3f - 2f * t);
        }

        // Slot of the oldest decal to reuse once the cap is reached (a ring).
        public static int DecalSlot(int placedSoFar, int cap) => cap <= 0 ? 0 : placedSoFar % cap;

        // The ray (unit direction) against a sphere: distance along the ray to where it enters, or -1.
        public static float RaySphere(float ox, float oy, float oz, float dx, float dy, float dz, float cx, float cy, float cz, float radius)
        {
            float lx = cx - ox, ly = cy - oy, lz = cz - oz;
            float along = lx * dx + ly * dy + lz * dz;
            float d2 = lx * lx + ly * ly + lz * lz - along * along;
            float r2 = radius * radius;
            if (d2 > r2) return -1f;
            float t = along - (float)Math.Sqrt(r2 - d2);
            if (t < 0f) t = along + (float)Math.Sqrt(r2 - d2) >= 0f ? 0f : -1f;
            return t;
        }
    }
}

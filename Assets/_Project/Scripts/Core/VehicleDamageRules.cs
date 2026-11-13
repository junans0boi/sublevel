using System;

namespace Game.Core
{
    // Where a hit on a vehicle landed (#90, ADR-067): the zone picks the multiplier on the vehicle's health.
    public enum VehicleHitZone { Body = 0, Engine = 1, Roof = 2, Glass = 3, Tyre = 4 }

    // The vehicle's condition as everyone sees it: smoke, fire, then the wreck (#90, ADR-067, ADR-074).
    public enum VehicleDamageStage { Healthy = 0, WhiteSmoke = 1, BlackSmoke = 2, Burning = 3, Wrecked = 4 }

    // Detachable body parts (#90): only models built with them as separate meshes have them (prefab children
    // Model/Part_<name>, VehicleModels). Doors are part of every model's body shell: none detach.
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
        public float DecalSize = 0.11f;         // a hole with its bare-metal rim, readable at 5-8 m
        public float DeformScale = 1f;
        // Sized to read at 6 m with the normals bent along (VehicleDamageVisual): a bullet a 20 cm dimple, a blast a 1.4 m cave-in.
        public float DentBulletRadius = 0.2f;
        public float DentBulletDepth = 0.022f;   // at DentBulletRefDamage, x0.5..x2 with the weapon's damage
        public float DentBulletRefDamage = 30f;
        public float DentImpactRadiusPerMps = 0.05f; // radius = speed x this, capped
        public float DentImpactMaxRadius = 0.9f;
        public float DentImpactDepthPerMps = 0.01f;
        public float DentBlastRadius = 1.4f;
        public float DentBlastDepth = 0.16f;
        public float DentMaxDepth = 0.22f;       // no vertex goes further in than this in total
        public int DentHistory = 48;             // kept by the server for late joiners
        public float PartDangleDeg = 14f;        // a loose part hangs open / down this far
        public float PartDespawnSeconds = 20f;   // a fallen part lies on the ground this long
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

        // The fire has burned long enough: the vehicle explodes.
        public static bool BurnedOut(float burningSeconds, VehicleDamageRules r) => burningSeconds >= r.BurnSeconds;

        // A shot on a tyre: true when this one made it flat (it was not flat before).
        public static bool HitTyre(ref float tyreHealth, float damage)
        {
            if (tyreHealth <= 0f) return false;
            tyreHealth = Math.Max(0f, tyreHealth - Math.Max(0f, damage));
            return tyreHealth <= 0f;
        }

        // Per wheel grip multiplier and corner drop from the flat tyres (bit i = wheel i flat).
        public static void WheelEffects(int flatMask, int wheels, VehicleDamageRules r, float[] grip, float[] drop)
        {
            for (int i = 0; i < wheels && i < grip.Length && i < drop.Length; i++)
            {
                bool flat = (flatMask & (1 << i)) != 0;
                grip[i] = flat ? r.FlatGrip : 1f;
                drop[i] = flat ? r.FlatDropMeters : 0f;
            }
        }

        // The pane a shot enters the cabin through, in the vehicle's local space: the face of the box (x, z bounds;
        // z toward the nose) the ray enters by, if it enters between the belt line and the roof. -1 if none.
        // Panes: 0 windshield, 1 rear, 2 left front, 3 left rear, 4 right front, 5 right rear.
        public static int PaneHit(float ox, float oy, float oz, float dx, float dy, float dz,
            float minX, float maxX, float minZ, float maxZ, float beltY, float roofY)
        {
            float tNear = float.NegativeInfinity, tFar = float.PositiveInfinity;
            int face = -1;
            if (!Slab(ox, dx, minX, maxX, 0, ref tNear, ref tFar, ref face)) return -1;
            if (!Slab(oy, dy, beltY, roofY, 1, ref tNear, ref tFar, ref face)) return -1;
            if (!Slab(oz, dz, minZ, maxZ, 2, ref tNear, ref tFar, ref face)) return -1;
            if (tNear > tFar || tFar < 0f || tNear < 0f) return -1;
            float cz = (minZ + maxZ) * 0.5f;
            float hz = oz + dz * tNear;
            switch (face)
            {
                case 2: return dz < 0f ? 0 : 1;                   // entered by the nose face (moving back) or the tail face
                case 0: return dx > 0f ? (hz >= cz ? 2 : 3) : (hz >= cz ? 4 : 5);
                default: return -1;                               // through the roof or the floor of the band
            }
        }

        // One axis of a ray against a box: narrows [tNear, tFar]; `face` = the axis the ray entered by last.
        static bool Slab(float o, float d, float min, float max, int axis, ref float tNear, ref float tFar, ref int face)
        {
            if (Math.Abs(d) < 1e-8f) return o >= min && o <= max;
            float t1 = (min - o) / d, t2 = (max - o) / d;
            if (t1 > t2) { float t = t1; t1 = t2; t2 = t; }
            if (t1 > tNear) { tNear = t1; face = axis; }
            if (t2 < tFar) tFar = t2;
            return tNear <= tFar;
        }

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

        // A collision dent: none at or below CollisionDamageMinSpeed, then radius and depth growing with the speed.
        public static bool ImpactDent(float speed, VehicleDamageRules r, out float radius, out float depth)
        {
            radius = depth = 0f;
            if (speed <= r.CollisionDamageMinSpeed) return false;
            radius = Math.Min(speed * r.DentImpactRadiusPerMps, r.DentImpactMaxRadius);
            depth = speed * r.DentImpactDepthPerMps * r.DeformScale;
            return depth > 0f;
        }

        // How far one dent moves a vertex already pushed in by `pushedSoFar`: never past DentMaxDepth in total.
        public static float DentPush(float pushedSoFar, float depth, float falloff, VehicleDamageRules r) =>
            Math.Max(0f, Math.Min(depth * falloff, r.DentMaxDepth - pushedSoFar));

        // Slot of the oldest decal to reuse once the cap is reached (a ring).
        public static int DecalSlot(int placedSoFar, int cap) => cap <= 0 ? 0 : placedSoFar % cap;

        // A ring of `cap` records after `placedSoFar` writes: how many are kept, and the slot of the k-th oldest.
        public static int HistoryCount(int placedSoFar, int cap) => Math.Max(0, Math.Min(placedSoFar, cap));
        public static int HistorySlot(int placedSoFar, int cap, int k) =>
            cap <= 0 ? 0 : placedSoFar <= cap ? k : (placedSoFar + k) % cap;

        // A bullet's dimple: deeper for heavier rounds (x0.5..x2 of the reference damage's depth).
        public static void BulletDent(float weaponDamage, VehicleDamageRules r, out float radius, out float depth)
        {
            float scale = r.DentBulletRefDamage > 0f ? Math.Max(0.5f, Math.Min(2f, weaponDamage / r.DentBulletRefDamage)) : 1f;
            radius = r.DentBulletRadius;
            depth = r.DentBulletDepth * r.DeformScale * scale;
        }

        // Damage on a detachable part: its state after (fallen stays fallen).
        public static PartState HitPart(ref float health, float damage, VehicleDamageRules r)
        {
            health = Math.Max(0f, health - Math.Max(0f, damage));
            return PartStateFor(health, r);
        }

        // Every part in one state, packed.
        public static int AllParts(PartState state)
        {
            int packed = 0;
            for (int p = 0; p < PartCount; p++) packed = SetPart(packed, p, state);
            return packed;
        }

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

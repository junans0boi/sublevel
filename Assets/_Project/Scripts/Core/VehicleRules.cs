using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Vehicles (ADR-065 (5)): a generic four-seat car and a two-seat motorcycle on one framework. Seat 0 is the
    // driver / rider; on the car 1 is the front passenger, 2 and 3 the rear seats (left, right); on the motorcycle 1 is
    // the pillion. Never reorder: seat numbers travel over the network and the keys Ctrl+1..4 pick them.
    public enum SeatRole { Driver = 0, Passenger = 1 }

    // What a seated occupant wants to do with their hands.
    public enum HandUse { Weapon = 0, Throwable = 1, Punch = 2 }

    // The vehicle types on the one vehicle framework. Never reorder (stored in scenes and prefabs).
    public enum VehicleKind { Car = 0, Motorcycle = 1 }

    // Loaded from the "Vehicles" (car) and "Motorcycles" blocks of rules.json: one class, one block per vehicle type.
    // The seat and weapon rules are the same for every type; the two-wheeler fields only matter to motorcycles.
    [Serializable]
    public class VehicleRules
    {
        // The rules in use. Defaults until the data loader replaces them.
        public static VehicleRules Current = new VehicleRules();
        public static VehicleRules Motorcycle = MotorcycleDefaults();

        public static VehicleRules For(VehicleKind kind) => kind == VehicleKind.Motorcycle ? Motorcycle : Current;

        // The motorcycle's numbers, used until the data loader replaces them (the "Motorcycles" block sets the same).
        public static VehicleRules MotorcycleDefaults() => new VehicleRules
        {
            SeatCount = 2, MassKg = 230f, MaxSpeedKmh = 105f, BoostMaxKmh = 120f, ReverseMaxKmh = 8f, EngineAccel = 7.5f,
            BrakeDecel = 8f, SteerMaxDeg = 30f, SteerAtTopSpeedDeg = 4f, SuspensionTravel = 0.25f, SpringStrength = 14000f,
            SpringDamper = 1300f, WheelRadius = 0.31f, TyreGrip = 1.1f, CentreOfMassHeight = 0.55f,
            SpawnCount = 4, RightingSeconds = 2f, RightingLiftMeters = 0.6f, DrownDepth = 0.6f, HandbrakeDecel = 6f,
            SteerDegPerSecond = 130f, SteerReturnDegPerSecond = 180f,
        };

        // ---- Seats ----
        public int SeatCount = 4;
        public float EnterRange = 3f;          // metres from the player's feet to a door point
        public float EnterSeconds = 0.6f;      // standing to seated (and back on the way out)
        public float SwitchSeconds = 0.5f;     // moving to another free seat; no weapon use meanwhile
        public float SeatedSpreadFactor = 1.35f; // a seated shooter's cone, instead of the moving / airborne factors

        // ---- Driving (arcade raycast wheels) ----
        public float MassKg = 1350f;
        public float MaxSpeedKmh = 90f;        // on roads and any built surface
        public float ReverseMaxKmh = 25f;
        public float OffRoadSpeedFactor = 0.85f; // top speed on open terrain (not a road)
        public float BoostMaxKmh = 105f;       // Left Shift
        public float BoostAccelFactor = 1.25f;
        public float LaunchAccelFactor = 1.5f; // W+Shift+Space held, then Space let go: full pull, no fade
        public float LaunchSeconds = 1.2f;
        public float EngineAccel = 5.5f;       // m/s² from a standstill; fades to 0 at the top speed
        public float ReverseAccelFactor = 0.7f;
        public float BrakeDecel = 9f;          // pressing the other way while moving
        public float CoastDecel = 0.8f;        // nothing pressed
        public float OverSpeedDecel = 3f;      // above the top speed of the surface (left the road fast)
        public float HandbrakeDecel = 7f;      // Space: the rear wheels lock
        public float HandbrakeRearGrip = 0.35f; // share of the rear wheels' side grip left with the handbrake on
        public float AbandonedBrakeDecel = 4f; // nobody in the car: it rolls on with the brakes on
        public float NoDriverDecel = 0.5f;     // passengers but no driver (the driver moved seats): rolls on, slight drag
        public float HoldSpeed = 0.4f;         // below this (m/s) with no throttle the tyres hold the car still
        public float SteerMaxDeg = 32f;        // at a standstill
        public float SteerAtTopSpeedDeg = 6f;  // at the top speed
        public float SteerDegPerSecond = 110f;
        public float SteerReturnDegPerSecond = 160f;
        public float InputStaleSeconds = 0.5f; // driver input older than this counts as nothing pressed

        // ---- Suspension and tyres ----
        public float SuspensionTravel = 0.32f; // from the top of the suspension to full extension (m)
        public float SpringStrength = 32000f;  // N/m per wheel
        public float SpringDamper = 3200f;     // N/(m/s) per wheel
        public float WheelRadius = 0.34f;
        public float TyreGrip = 1.05f;         // friction coefficient: the most a tyre pushes is grip x its load
        public float SideGripPerSecond = 12f;  // how fast the tyres take out sideways sliding (1/s)
        public float CentreOfMassHeight = 0.38f;

        // ---- Flipping, water ----
        public float FlippedUpDot = 0.35f;     // the roof axis this close to level or below counts as flipped
        public float RightingDelaySeconds = 5f;
        public float RightingSeconds = 2.5f;   // a full half turn back onto the wheels
        public float RightingLiftMeters = 1.2f;
        public float DrownDepth = 0.8f;        // water this deep over the ground under the car stops it
        public float DrownedDrag = 3f;

        // ---- Getting out at speed, crashes ----
        public float CollisionMinSpeed = 4f;   // m/s along the contact normal: VehicleState.Impact is raised from here
        public float ExitSafeKmh = 30f;        // getting out faster than this hurts like a fall
        public float ExitLethalKmh = 65f;      // and from this speed on it kills

        // ---- Running people over (#112, ADR-084) ----
        public float RoadkillMinKmh = 10f;            // closing speed below this only bumps
        public float RoadkillDamagePerKmh = 2.5f;     // per km/h above RoadkillMinKmh, for a vehicle of RoadkillReferenceMassKg
        public float RoadkillReferenceMassKg = 1350f; // damage scales with the square root of mass over this
        public float RoadkillShoveShare = 0.8f;       // of the closing speed given to the person, times the mass share
        public float RoadkillMaxShove = 12f;          // m/s
        public float RoadkillLiftShare = 0.35f;       // the upward part of the shove

        // ---- Two wheels (motorcycle) ----
        public float MaxLeanDeg = 35f;         // the model leans into turns: steer share x speed share x this
        public float LeanFullSpeedKmh = 50f;
        public float LeanDegPerSecond = 90f;
        public float WheelieMaxDeg = 8f;       // nose up under hard acceleration (shown, not simulated)
        public float WheelieFullAccel = 6f;    // m/s² at which the wheelie is full
        public float KickstandRollDeg = 12f;   // stopped with nobody on it: it rests on its stand
        public float StabiliseStrength = 60f;  // 1/s²: keeps the frame upright on the ground (the lean is the model's)
        public float StabiliseDamping = 12f;   // 1/s
        public float AirPitchAccelDeg = 300f;  // deg/s² from Space (nose up) / Ctrl (nose down) with both wheels off the ground
        public float AirRollAccelDeg = 300f;   // deg/s² from E (right) / Q (left)
        public float CrashImpactSpeed = 9f;    // m/s along the contact normal: the riders are thrown off
        public float CrashLandAngleDeg = 45f;  // landing tilted further than this from the ground crashes
        public float CrashRecoverSeconds = 3f; // after a crash it is left to fall over before the slow auto-righting
        public float AstrideThighDeg = 60f;    // rider's pose: thighs forward,
        public float AstrideApartDeg = 14f;    // apart,
        public float AstrideShinDeg = 10f;     // shins back from vertical,
        public float AstrideArmDeg = 72f;      // hands forward to the bars

        // ---- Spawning ----
        public int SpawnCount = 6;
        public float ParkingSpacingMeters = 40f;

        // ---- Seated pose (CombatantAnimation, no clips) ----
        public float SeatedThighDeg = 82f;     // thighs forward from hanging to about level
        public float SeatedShinDeg = -18f;     // shins from vertical, negative = feet forward
        public float SeatedHipDrop = 0.02f;    // the model is lowered this much (m)
        public float SeatedHipBack = 0.08f;    // and moved back into the seat (m)
        public float SeatedTorsoBackDeg = 6f;
        public float DriverArmDeg = 55f;       // upper arms forward to the steering wheel
        public float DriverArmInDeg = 12f;
        public float PassengerArmDeg = 20f;    // empty hands rest on the lap
    }

    // Pure seat and driving rules: no Unity types, so they run in edit-mode tests.
    public static class Vehicles
    {
        public const int NoSeat = -1;
        public const int DriverSeat = 0;

        public static SeatRole RoleOf(int seat) => seat == DriverSeat ? SeatRole.Driver : SeatRole.Passenger;

        public static bool IsValidSeat(int seat, int seatCount) => seat >= 0 && seat < seatCount;

        // F at a car: nothing when every door is farther than `range` or every seat is taken; otherwise the driver's
        // seat if it is free, else the free seat whose door is nearest. `occupants[i]` < 0 means seat i is free;
        // `doorDistance[i]` is the distance to seat i's door.
        public static int ChooseEnterSeat(IReadOnlyList<int> occupants, IReadOnlyList<float> doorDistance, float range)
        {
            if (occupants == null || doorDistance == null) return NoSeat;
            int count = Math.Min(occupants.Count, doorDistance.Count);
            bool near = false;
            for (int i = 0; i < count; i++)
                if (doorDistance[i] <= range) near = true;
            if (!near) return NoSeat;
            if (count > DriverSeat && occupants[DriverSeat] < 0) return DriverSeat;
            int best = NoSeat;
            for (int i = 0; i < count; i++)
                if (occupants[i] < 0 && (best == NoSeat || doorDistance[i] < doorDistance[best])) best = i;
            return best;
        }

        // Ctrl+1..4: only into a seat that exists, is another one, and is free.
        public static bool CanSwitch(IReadOnlyList<int> occupants, int from, int to)
        {
            if (occupants == null) return false;
            return IsValidSeat(from, occupants.Count) && IsValidSeat(to, occupants.Count) && from != to && occupants[to] < 0;
        }

        // What a seat allows (ADR-065 (5)): the driver only fires a sidearm; every other seat fires anything and
        // throws; nobody punches from a seat. `seat` < 0 means not in a vehicle: everything is allowed.
        public static bool CanUse(int seat, HandUse use, WeaponCategory category = WeaponCategory.Primary)
        {
            if (seat < 0) return true;
            if (use == HandUse.Punch) return false;
            if (RoleOf(seat) == SeatRole.Driver) return use == HandUse.Weapon && category == WeaponCategory.Sidearm;
            return true;
        }

        // The same for a weapon; no weapon (empty hands) is a punch, and so is a melee weapon (ADR-073).
        public static bool CanUseWeapon(int seat, WeaponDef weapon) =>
            weapon == null || weapon.IsMelee ? CanUse(seat, HandUse.Punch) : CanUse(seat, HandUse.Weapon, weapon.Category);

        public static float Kmh(float kmh) => kmh / 3.6f;
        public static float ToKmh(float metersPerSecond) => metersPerSecond * 3.6f;

        // Forward top speed (m/s) on the current surface.
        public static float TopSpeed(bool offRoad, VehicleRules r) => TopSpeed(offRoad, false, r);
        public static float TopSpeed(bool offRoad, bool boost, VehicleRules r) => Kmh(boost ? r.BoostMaxKmh : r.MaxSpeedKmh) * (offRoad ? r.OffRoadSpeedFactor : 1f);
        public static float ReverseTopSpeed(bool offRoad, VehicleRules r) => Kmh(r.ReverseMaxKmh) * (offRoad ? r.OffRoadSpeedFactor : 1f);

        // Acceleration along the car's forward axis (m/s²) for one physics step of `dt` seconds with a driver.
        // Throttle +1 drives forward (or brakes out of reverse), -1 brakes and then reverses, 0 coasts. Braking and
        // coasting never carry the car through a standstill within one step.
        // `accelFactor` scales the engine's pull (boost); braking is not affected.
        public static float DriveAccel(float forwardSpeed, float throttle, float topSpeed, float reverseTop, float dt, VehicleRules r, float accelFactor = 1f)
        {
            throttle = Math.Max(-1f, Math.Min(1f, throttle));
            float a;
            if (throttle > 0.01f)
            {
                if (forwardSpeed < -0.5f) a = r.BrakeDecel * throttle;
                else if (forwardSpeed < topSpeed) a = r.EngineAccel * accelFactor * throttle * Fade(Math.Max(0f, forwardSpeed) / Math.Max(0.1f, topSpeed));
                else a = -Math.Min(r.OverSpeedDecel, (forwardSpeed - topSpeed) / Math.Max(1e-4f, dt));
            }
            else if (throttle < -0.01f)
            {
                float t = -throttle;
                if (forwardSpeed > 0.5f) a = -r.BrakeDecel * t;
                else if (-forwardSpeed < reverseTop) a = -r.EngineAccel * r.ReverseAccelFactor * t * Fade(Math.Max(0f, -forwardSpeed) / Math.Max(0.1f, reverseTop));
                else a = Math.Min(r.OverSpeedDecel, (-forwardSpeed - reverseTop) / Math.Max(1e-4f, dt));
            }
            else a = -Math.Sign(forwardSpeed) * r.CoastDecel;
            return NoThroughZero(a, forwardSpeed, dt);
        }

        // Slows the car toward a standstill by up to `decel` (m/s²) without reversing it.
        public static float BrakeAccel(float forwardSpeed, float decel, float dt) =>
            NoThroughZero(-Math.Sign(forwardSpeed) * decel, forwardSpeed, dt);

        // An acceleration against the motion that would pass zero speed within this step only brings it to rest.
        // A body parked on its springs creeps by hairs (1e-10 m/s either way): that is a standstill, not motion.
        static float NoThroughZero(float a, float speed, float dt)
        {
            if (dt <= 0f || a * speed >= 0f || Math.Abs(speed) < StandstillSpeed) return a;
            if (Math.Abs(a) * dt > Math.Abs(speed)) return -speed / dt;
            return a;
        }

        // 1 at a standstill, falling steeply near the top speed (so the car gets close to it in reasonable time).
        const float StandstillSpeed = 1e-3f; // m/s

        static float Fade(float fraction)
        {
            fraction = Math.Max(0f, Math.Min(1f, fraction));
            return 1f - fraction * fraction * fraction;
        }

        // How far the front wheels may turn at this speed: full lock when slow, little at the top speed.
        public static float SteerLimit(float speed, float topSpeed, VehicleRules r)
        {
            float f = topSpeed <= 0f ? 0f : Math.Min(1f, Math.Abs(speed) / topSpeed);
            return r.SteerMaxDeg + (r.SteerAtTopSpeedDeg - r.SteerMaxDeg) * f;
        }

        // Spring and damper push of one wheel (N), never pulling the car down.
        public static float SuspensionForce(float compression, float compressionSpeed, VehicleRules r) =>
            Math.Max(0f, compression * r.SpringStrength + compressionSpeed * r.SpringDamper);

        // Distance from the top of the suspension to the wheel centre with the vehicle resting on level ground.
        public static float RestLength(VehicleRules r, int wheels = 4)
        {
            float squeeze = r.MassKg * 9.81f / Math.Max(1, wheels) / Math.Max(1f, r.SpringStrength);
            return Math.Max(0.05f, r.SuspensionTravel - squeeze);
        }

        // Flipped: the roof axis's upward part is this small (or negative).
        public static bool IsFlipped(float upDotWorldUp, VehicleRules r) => upDotWorldUp < r.FlippedUpDot;

        // Getting out at speed: nothing up to ExitSafeKmh, then rising with the square of the excess to 100 just
        // below ExitLethalKmh; from ExitLethalKmh on it kills.
        public const float LethalDamage = 1000f;
        public static float ExitDamage(float speedKmh, VehicleRules r)
        {
            if (speedKmh <= r.ExitSafeKmh) return 0f;
            if (speedKmh >= r.ExitLethalKmh) return LethalDamage;
            float t = (speedKmh - r.ExitSafeKmh) / Math.Max(1e-3f, r.ExitLethalKmh - r.ExitSafeKmh);
            return 100f * t * t;
        }

        // ---- Two wheels ----

        // How far the motorcycle's model leans (degrees, positive = right) at this speed and steering share (-1..1).
        public static float LeanDeg(float speed, float steerShare, VehicleRules r)
        {
            float speedShare = Math.Min(1f, Math.Abs(speed) / Math.Max(0.1f, Kmh(r.LeanFullSpeedKmh)));
            float lean = Math.Max(-1f, Math.Min(1f, steerShare)) * speedShare * r.MaxLeanDeg;
            return speed < 0f ? -lean : lean;
        }

        // Nose-up angle shown for a forward acceleration (m/s²).
        public static float WheelieDeg(float accel, VehicleRules r) =>
            Math.Max(0f, Math.Min(1f, accel / Math.Max(0.1f, r.WheelieFullAccel))) * r.WheelieMaxDeg;

        // Air control: angular accelerations (deg/s²) about the vehicle's right axis (positive = nose down, Unity's
        // turn direction) and forward axis (positive = roll left, Unity's), from pitch input (+1 nose up, Space;
        // -1 nose down, Ctrl) and roll input (+1 right, E; -1 left, Q).
        public static void AirControl(float pitchInput, float rollInput, VehicleRules r, out float aboutRight, out float aboutForward)
        {
            pitchInput = Math.Max(-1f, Math.Min(1f, pitchInput));
            rollInput = Math.Max(-1f, Math.Min(1f, rollInput));
            aboutRight = -pitchInput * r.AirPitchAccelDeg;
            aboutForward = -rollInput * r.AirRollAccelDeg;
        }

        // Riders are thrown off by an impact this hard, or by landing this far off the ground's slope.
        public static bool Crashes(float impactSpeed, VehicleRules r) => impactSpeed >= r.CrashImpactSpeed;
        public static bool BadLanding(float tiltDeg, VehicleRules r) => tiltDeg > r.CrashLandAngleDeg;

        // Running someone over (#112, ADR-084): damage from the closing speed (m/s) and the vehicle's mass. Nothing
        // below RoadkillMinKmh, and nothing when the driver is on the victim's team (they are still pushed). With nobody
        // driving `driverTeam` is Teams.None: it hurts anyone.
        public const float PersonMassKg = 80f;
        public static float RoadkillDamage(float closingSpeed, float massKg, int driverTeam, int victimTeam, VehicleRules r)
        {
            float kmh = ToKmh(closingSpeed);
            if (kmh < r.RoadkillMinKmh || Teams.Same(driverTeam, victimTeam)) return 0f;
            float mass = (float)Math.Sqrt(Math.Max(0f, massKg) / Math.Max(1f, r.RoadkillReferenceMassKg));
            return (kmh - r.RoadkillMinKmh) * r.RoadkillDamagePerKmh * mass;
        }

        // How fast the person is pushed away (m/s): a share of the closing speed, more from a heavier vehicle.
        public static float RoadkillShove(float closingSpeed, float massKg, VehicleRules r)
        {
            if (ToKmh(closingSpeed) < r.RoadkillMinKmh) return 0f;
            float share = Math.Max(0f, massKg) / (Math.Max(0f, massKg) + PersonMassKg);
            return Math.Min(r.RoadkillMaxShove, closingSpeed * r.RoadkillShoveShare * share);
        }

        // Water deeper than DrownDepth over the ground under the car stops it.
        public static bool IsDrowned(float waterLevel, float groundY, VehicleRules r) => waterLevel - groundY > r.DrownDepth;
    }

    // A one-bit-per-cell map of where the roads are, square from (0, 0), for the "on a road?" question at run time
    // (the roads have no colliders: the ground under them is the terrain).
    public static class RoadMask
    {
        public static byte[] Build(int cells, float cellSize, Func<float, float, bool> isRoad)
        {
            if (cells <= 0 || cellSize <= 0f || isRoad == null) return new byte[0];
            var bits = new byte[(cells * cells + 7) / 8];
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                    if (isRoad((x + 0.5f) * cellSize, (z + 0.5f) * cellSize))
                    {
                        int i = z * cells + x;
                        bits[i >> 3] |= (byte)(1 << (i & 7));
                    }
            return bits;
        }

        // False outside the map or with no map.
        public static bool Test(byte[] bits, int cells, float cellSize, float x, float z)
        {
            if (bits == null || cells <= 0 || cellSize <= 0f || x < 0f || z < 0f) return false;
            int cx = (int)(x / cellSize), cz = (int)(z / cellSize);
            if (cx >= cells || cz >= cells) return false;
            int i = cz * cells + cx;
            if ((i >> 3) >= bits.Length) return false;
            return (bits[i >> 3] & (1 << (i & 7))) != 0;
        }
    }

    public struct ParkingSpot
    {
        public float X, Z, YawDeg;
    }

    // Where the cars stand when a match starts: on the right shoulder of a road, facing along it, near a compound
    // (a building lot), on level dry ground clear of buildings, elevators and other roads, spread over the map.
    public static class VehicleParking
    {
        public const float StepMeters = 12f;
        public const float ShoulderMeters = 1.6f;   // from the road edge to the car's centre line
        public const float HalfLength = 2.2f, HalfWidth = 0.95f;
        public const float NearLotMeters = 45f, ClearLotMeters = 4f, ClearElevatorMeters = 15f, EdgeMeters = 20f;
        public const float MaxGroundStep = 0.5f;

        public static List<ParkingSpot> Pick(WorldPlan plan, int count, float spacing)
        {
            var chosen = new List<ParkingSpot>();
            if (plan == null || count <= 0) return chosen;
            var candidates = Candidates(plan);
            if (candidates.Count == 0) return chosen;
            // Farthest-point sampling from the first candidate: deterministic and spread out.
            chosen.Add(candidates[0]);
            while (chosen.Count < count)
            {
                int best = -1;
                float bestSqr = spacing * spacing;
                for (int i = 0; i < candidates.Count; i++)
                {
                    float near = float.MaxValue;
                    for (int c = 0; c < chosen.Count; c++)
                    {
                        float dx = candidates[i].X - chosen[c].X, dz = candidates[i].Z - chosen[c].Z;
                        near = Math.Min(near, dx * dx + dz * dz);
                    }
                    if (near > bestSqr) { bestSqr = near; best = i; }
                }
                if (best < 0) break;
                chosen.Add(candidates[best]);
            }
            return chosen;
        }

        public static List<ParkingSpot> Candidates(WorldPlan plan)
        {
            var found = new List<ParkingSpot>();
            foreach (var road in plan.Roads)
            {
                if (road.Kind == RoadKind.Path) continue;
                int n = Math.Min(road.X.Length, road.Z.Length);
                // Distance into the current segment of the next sample.
                float next = StepMeters * 0.5f;
                for (int p = 0; p + 1 < n; p++)
                {
                    float ax = road.X[p], az = road.Z[p], bx = road.X[p + 1], bz = road.Z[p + 1];
                    float dx = bx - ax, dz = bz - az;
                    float length = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (length < 1e-3f) continue;
                    dx /= length;
                    dz /= length;
                    // The right of travel along +Z is +X: (dz, -dx).
                    float offset = road.Width * 0.5f + ShoulderMeters;
                    for (; next < length; next += StepMeters)
                    {
                        float x = ax + dx * next + dz * offset, z = az + dz * next - dx * offset;
                        if (Fits(plan, x, z, dx, dz))
                            found.Add(new ParkingSpot { X = x, Z = z, YawDeg = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI) });
                    }
                    next -= length;
                }
            }
            return found;
        }

        static bool Fits(WorldPlan plan, float x, float z, float dx, float dz)
        {
            if (x < EdgeMeters || z < EdgeMeters || x > plan.Size - EdgeMeters || z > plan.Size - EdgeMeters) return false;
            // Level, dry ground under all four corners.
            float lo = float.MaxValue, hi = float.MinValue;
            for (int corner = 0; corner < 4; corner++)
            {
                float f = (corner & 1) == 0 ? HalfLength : -HalfLength;
                float r = (corner & 2) == 0 ? HalfWidth : -HalfWidth;
                float h = plan.Height(x + dx * f + dz * r, z + dz * f - dx * r);
                lo = Math.Min(lo, h);
                hi = Math.Max(hi, h);
            }
            if (hi - lo > MaxGroundStep || lo < plan.SeaLevel + 0.8f) return false;
            // Off every road surface (a junction or a parallel road).
            if (plan.RoadDistance(x, z) < HalfWidth + 0.3f) return false;
            bool nearLot = false;
            foreach (var lot in plan.Lots)
            {
                float d = lot.Distance(x, z);
                if (d < ClearLotMeters + HalfLength) return false;
                if (d < NearLotMeters) nearLot = true;
            }
            if (!nearLot) return false;
            foreach (var e in plan.Elevators)
            {
                float ex = e.X - x, ez = e.Z - z;
                if (ex * ex + ez * ez < ClearElevatorMeters * ClearElevatorMeters) return false;
            }
            return true;
        }
    }
}

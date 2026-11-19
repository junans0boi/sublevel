using System;

namespace Game.Core
{
    [Serializable]
    public class LocomotionParams
    {
        // Empty hands, a pistol or a melee weapon (ADR-095, PUBG's table): running 4.7, sprinting 6.3 m/s.
        public float WalkSpeed = 4.7f;
        public float SprintSpeed = 6.3f;
        public float JumpSpeed = 4.8f;
        public float Gravity = -14f;
        public float GroundedStick = -2f;
        // A long gun in hand slows walking and sprinting by its kind (ADR-095).
        public float SmgMoveFactor = 0.977f, RifleMoveFactor = 0.954f, MarksmanMoveFactor = 0.952f, ShotgunMoveFactor = 0.939f;
        // Slow walk (Ctrl held, ADR-065 (3)): this speed standing at the hip, whatever is in the hands; no sprinting.
        public float SlowWalkSpeed = 1.7f;
        public float SlowWalkFootstepVolume = 0.25f; // footsteps this much quieter
        // Starting and stopping take a moment (#111, ADR-083): ground speed changes at most this fast (m/s per second).
        // 0 to a run in about 0.15 s, to a sprint in about 0.25 s; a stop from a run in about 0.1 s.
        public float Acceleration = 30f;
        public float Deceleration = 45f;
    }

    // How the legs keep up when the character moves faster than its clips were animated for (ADR-048).
    [Serializable]
    public class GaitRules
    {
        public float LegPaceFollow = 0.5f; // share of the missing speed made up by playing the legs faster; the rest is foot slide
        public float MaxLegPace = 1.75f;
        // The character has no sidestep clips: the legs turn toward the way it moves and run forward or backward,
        // while the upper body keeps facing the aim (ADR-049).
        public float MaxLegTurnDeg = 60f;
        public float LegTurnDegPerSecond = 360f;
        public float LegTurnMinSpeed = 0.5f;   // slower than this the legs face forward
    }

    // Which way the legs point relative to where the character faces, and whether they run forward or backward.
    public class LegHeading
    {
        // Past sideways by this much before the legs flip between forward and backward running, so that moving
        // exactly sideways does not flicker between the two.
        const float FlipMarginDeg = 10f;

        public float TurnDeg { get; private set; }   // eased; right positive
        public bool Backward { get; private set; }   // keeps its last value while standing

        // `vx`, `vz`: velocity in the character's own frame (x right, z forward).
        public void Tick(float vx, float vz, float dt, GaitRules rules)
        {
            float target = 0f;
            if (vx * vx + vz * vz >= rules.LegTurnMinSpeed * rules.LegTurnMinSpeed)
            {
                float angle = (float)(Math.Atan2(vx, vz) * 180.0 / Math.PI); // 0 = forward, 90 = right
                float away = Math.Abs(angle);
                Backward = Backward ? away > 90f - FlipMarginDeg : away > 90f + FlipMarginDeg;
                if (Backward) angle += angle > 0f ? -180f : 180f;
                target = Math.Max(-rules.MaxLegTurnDeg, Math.Min(rules.MaxLegTurnDeg, angle));
            }
            float step = rules.LegTurnDegPerSecond * dt;
            TurnDeg = TurnDeg < target ? Math.Min(target, TurnDeg + step) : Math.Max(target, TurnDeg - step);
        }
    }

    // How a long gun is held in third person (ADR-083). The character file has only one-handed pistol clips, so the
    // hold is posed in code: the weapon is placed relative to the right shoulder, the right hand on the grip and the
    // left on the handguard by arm IK. Low ready (muzzle down, turned in across the body) whenever not aiming, more
    // so sprinting; shouldered and level when aiming or firing. Angles: pitch positive = muzzle down, yaw positive =
    // muzzle right, roll positive = top of the weapon to the right. Offsets: the right hand (the grip) from the right
    // shoulder joint, meters in the character's axes (x right, y up, z forward). The character's arms are short (about
    // 0.4 m shoulder to wrist), so the grip stays well inside that and the torso turns right to bring the left
    // shoulder forward, as a shooter stands bladed; a long stock then reaches back past the shoulder.
    [Serializable]
    public class RifleHoldRules
    {
        public float RaiseSeconds = 0.15f; // low ready to shouldered
        public float LowerSeconds = 0.3f;  // and back
        public float IdlePitchDeg = 24f, IdleYawDeg = -14f, IdleRollDeg = -8f;
        public float MovePitchDeg = 32f, MoveYawDeg = -18f, MoveRollDeg = -10f;
        public float SprintPitchDeg = 58f, SprintYawDeg = -30f, SprintRollDeg = -18f;
        public float IdleGripX = -0.07f, IdleGripY = -0.15f, IdleGripZ = 0.21f;
        public float SprintGripX = -0.12f, SprintGripY = -0.2f, SprintGripZ = 0.15f;
        // Shouldered (#121, ADR-096): the grip high enough that the sights are at the eyes, the stock in the shoulder pocket.
        public float AimGripX = -0.05f, AimGripY = 0.06f, AimGripZ = 0.24f;
        // The weapon follows the look pitch (degrees, up positive): all of it shouldered, this share at low ready;
        // the chest takes TorsoPitchShare of what is followed. Beyond MaxLookPitchDeg the weapon stays put.
        public float LowPitchFollow = 0.5f, TorsoPitchShare = 0.4f, MaxLookPitchDeg = 70f;
        public float LowTwistDeg = 15f, AimTwistDeg = 25f; // torso turned right (left shoulder forward)
        public float MoveFullSpeed = 4f;   // m/s at which the moving low ready is fully shown
        // Body: torso pitched forward with speed (more sprinting), rolled into a sidestep, leaning into a start.
        public float RunTorsoPitchDeg = 5f, SprintTorsoPitchDeg = 12f;
        public float StrafeRollDegPerMps = 1.2f, MaxStrafeRollDeg = 6f;
        public float AccelPitchDegPerMps2 = 0.4f, MaxAccelPitchDeg = 6f;
        // Weapon bob over a stride (two steps per locomotion cycle): down at each step, side to side once per cycle.
        public float BobDownMeters = 0.025f, BobSideMeters = 0.018f, BobRollDeg = 3f;
        // A shot kicks the weapon back and the muzzle up for a moment.
        public float KickBackMeters = 0.05f, KickPitchDeg = 5f, KickSeconds = 0.12f;
        // Sprinting: how much of the left hand's grip is let go (0 keeps it on the handguard, 1 swings free) (#222).
        public float SprintSupportRelease = 0f; // player report 2026-11-19: the freed left arm stuck out ahead with an open hand; both hands stay on the gun

        // A hand gun in both hands (#218): closer in and higher than a rifle, muzzle only slightly down at low ready.
        public static RifleHoldRules Pistol() => new RifleHoldRules
        {
            IdlePitchDeg = 20f, IdleYawDeg = -4f, IdleRollDeg = 0f,
            MovePitchDeg = 26f, MoveYawDeg = -6f, MoveRollDeg = 0f,
            SprintPitchDeg = 38f, SprintYawDeg = -10f, SprintRollDeg = -4f,
            IdleGripX = -0.04f, IdleGripY = -0.13f, IdleGripZ = 0.28f,
            SprintGripX = -0.07f, SprintGripY = -0.16f, SprintGripZ = 0.2f,
            AimGripX = -0.03f, AimGripY = 0.04f, AimGripZ = 0.32f,
            LowTwistDeg = 8f, AimTwistDeg = 12f,
            BobDownMeters = 0.015f, BobSideMeters = 0.01f, BobRollDeg = 1.5f,
            KickBackMeters = 0.03f, KickPitchDeg = 8f, SprintSupportRelease = 0f,
        };
    }

    // Where a long gun is: angles (degrees), grip offset (meters) and torso twist (degrees) as RifleHoldRules describes them.
    public struct RifleHoldPose
    {
        public float Pitch, Yaw, Roll, X, Y, Z, Twist;

        public static RifleHoldPose Lerp(RifleHoldPose a, RifleHoldPose b, float t) => new RifleHoldPose
        {
            Pitch = a.Pitch + (b.Pitch - a.Pitch) * t, Yaw = a.Yaw + (b.Yaw - a.Yaw) * t, Roll = a.Roll + (b.Roll - a.Roll) * t,
            X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, Z = a.Z + (b.Z - a.Z) * t, Twist = a.Twist + (b.Twist - a.Twist) * t,
        };
    }

    public static class RifleHold
    {
        // One frame of raising (toward 1) or lowering (toward 0) the weapon; linear, each way at its own speed.
        public static float ReadyStep(float ready, bool raised, float dt, RifleHoldRules r)
        {
            if (raised) return r.RaiseSeconds <= 0f ? 1f : Math.Min(1f, ready + dt / r.RaiseSeconds);
            return r.LowerSeconds <= 0f ? 0f : Math.Max(0f, ready - dt / r.LowerSeconds);
        }

        // The hold for a ground speed (m/s), how much of a sprint (0..1) and how far raised (0..1).
        public static RifleHoldPose Pose(float speed, float sprint, float ready, RifleHoldRules r)
        {
            var idle = new RifleHoldPose { Pitch = r.IdlePitchDeg, Yaw = r.IdleYawDeg, Roll = r.IdleRollDeg, X = r.IdleGripX, Y = r.IdleGripY, Z = r.IdleGripZ, Twist = r.LowTwistDeg };
            var move = idle;
            move.Pitch = r.MovePitchDeg; move.Yaw = r.MoveYawDeg; move.Roll = r.MoveRollDeg;
            var sprinting = new RifleHoldPose { Pitch = r.SprintPitchDeg, Yaw = r.SprintYawDeg, Roll = r.SprintRollDeg, X = r.SprintGripX, Y = r.SprintGripY, Z = r.SprintGripZ, Twist = r.LowTwistDeg };
            var aim = new RifleHoldPose { X = r.AimGripX, Y = r.AimGripY, Z = r.AimGripZ, Twist = r.AimTwistDeg };
            float moving = r.MoveFullSpeed <= 0f ? 1f : Clamp01(speed / r.MoveFullSpeed);
            var low = RifleHoldPose.Lerp(RifleHoldPose.Lerp(idle, move, moving), sprinting, Clamp01(sprint));
            return RifleHoldPose.Lerp(low, aim, Clamp01(ready));
        }

        // How much of the look pitch (degrees, up positive) the weapon follows at `ready` (0 low ready .. 1 shouldered).
        public static float FollowedPitch(float lookPitch, float ready, RifleHoldRules r)
        {
            float look = Math.Max(-r.MaxLookPitchDeg, Math.Min(r.MaxLookPitchDeg, lookPitch));
            return look * (r.LowPitchFollow + (1f - r.LowPitchFollow) * Clamp01(ready));
        }

        // Forward pitch of the torso (degrees): with speed, more sprinting, plus a lean into speeding up (back when stopping).
        public static float TorsoPitch(float speed, float sprint, float forwardAccel, RifleHoldRules r)
        {
            float moving = r.MoveFullSpeed <= 0f ? 1f : Clamp01(speed / r.MoveFullSpeed);
            float lean = Math.Max(-r.MaxAccelPitchDeg, Math.Min(r.MaxAccelPitchDeg, forwardAccel * r.AccelPitchDegPerMps2));
            return r.RunTorsoPitchDeg * moving + (r.SprintTorsoPitchDeg - r.RunTorsoPitchDeg) * Clamp01(sprint) + lean;
        }

        // Roll of the torso into a sidestep (degrees, positive = top to the right), `sideSpeed` m/s, right positive.
        public static float StrafeRoll(float sideSpeed, RifleHoldRules r) =>
            Math.Max(-r.MaxStrafeRollDeg, Math.Min(r.MaxStrafeRollDeg, sideSpeed * r.StrafeRollDegPerMps));

        // Weapon bob at `phase` (0..1 of a locomotion cycle = two steps), scaled by `amount` (0 standing .. 1 running):
        // down (meters, at each step), side (meters, right positive), roll (degrees).
        public static void Bob(float phase, float amount, RifleHoldRules r, out float down, out float side, out float roll)
        {
            double a = phase * 2.0 * Math.PI;
            float step = (float)(0.5 - 0.5 * Math.Cos(2.0 * a)); // two dips per cycle, one per step
            down = r.BobDownMeters * amount * step;
            side = r.BobSideMeters * amount * (float)Math.Sin(a);
            roll = r.BobRollDeg * amount * (float)Math.Sin(a);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    // There is no jump clip (ADR-048): the air state holds a still pose and the jump itself is posed on top of it
    // in code — a push at take-off, knees tucked up through the air, and a knee bend that absorbs the landing.
    [Serializable]
    public class JumpRules
    {
        // Moments (0..1) of the clip the air state holds.
        public float RisePhase = 0.25f;
        public float FallPhase = 0.375f;
        public float LandPhase = 0.4f;
        public float PoseSpeed = 3f;           // vertical speed (m/s) at which the rise or fall moment is fully shown
        public float LandSeconds = 0.12f;      // the air state is held this long after touching down before moving on
        public float GroundProbeMeters = 0.35f; // airborne when there is no ground this close under the feet; more than a stair step, so walking down steps is not a fall

        // Take-off push: toes pointed, back arched, legs straight.
        public float TakeoffSeconds = 0.1f;
        public float TakeoffToeDeg = 30f;
        public float TakeoffArchDeg = 6f;
        // Tuck: thighs raised forward and shins folded back, fully at the top of the jump.
        public float TuckThighDeg = 68f;
        public float TuckShinDeg = 95f;
        public float TuckSpeed = 4f;           // vertical speed (m/s, up or down) at which the legs are stretched out again
        public float FallTuck = 0.2f;          // share of the tuck kept while rising or falling fast
        public float TuckPerSecond = 10f;      // how fast the shown tuck follows
        public float TorsoPitchDeg = 8f;       // forward, at full tuck
        public float ArmLiftDeg = 35f;         // empty hands only: arms out sideways at full tuck
        // Landing: the knees bend and the body sinks, then eases back up.
        public float LandAttackSeconds = 0.06f;
        public float LandAbsorbSeconds = 0.25f; // whole absorb, attack included
        public float LandBendDeg = 28f;        // thigh forward; the shin folds back twice this, keeping the feet under the hips
        public float LandDropMeters = 0.09f;
        public float LandPitchDeg = 8f;
    }

    // Falling hurts (ADR-053): nothing up to a safe drop, then more with every metre, lethal around the second height.
    [Serializable]
    public class FallRules
    {
        // The rules in use. Defaults until the data loader replaces them.
        public static FallRules Current = new FallRules();

        public float SafeMeters = 3f;
        public float LethalMeters = 12f;
        public float LethalDamage = 100f;
        // Movement belongs to the owner (ADR-029), so the owner reports its falls. The server believes a report
        // only up to the drop it watched itself plus this much, and waits this long for its own view to catch up.
        public float ReportToleranceMeters = 1f;
        public float ReportWindowSeconds = 1f;
    }

    // Looking around without turning the character (free look, Alt held). The view orbits all the way round
    // (ADR-065 (2)); the pitch keeps its normal limits.
    [Serializable]
    public class FreeLookRules
    {
        public static FreeLookRules Current = new FreeLookRules();

        public float ReturnDegPerSecond = 540f;   // fastest the view swings back when the key is released
        public float ReturnEasePerSecond = 10f;   // the swing back slows down over its last part
    }

    // Measures a drop: from the highest point reached off the ground down to where the ground was met again.
    public class FallTracker
    {
        bool _inAir;
        float _peak;

        public bool InAir => _inAir;

        // Call every frame with the current height. Returns the metres dropped on the frame of landing, else 0.
        public float Tick(bool grounded, float height)
        {
            if (!grounded)
            {
                if (!_inAir || height > _peak) _peak = height;
                _inAir = true;
                return 0f;
            }
            if (!_inAir) return 0f;
            _inAir = false;
            return Math.Max(0f, _peak - height);
        }

        // How far below its highest point a fall still in progress is.
        public float DroppedSoFar(float height) => _inAir ? Math.Max(0f, _peak - height) : 0f;

        // A teleport is not a fall.
        public void Reset() => _inAir = false;
    }

    public static class Locomotion
    {
        // Damage for dropping `meters`: none up to the safe height, then rising evenly, lethal at the lethal height
        // (and more beyond it).
        public static float FallDamage(float meters, FallRules rules)
        {
            if (!(meters > rules.SafeMeters)) return 0f; // also false for NaN
            if (rules.LethalMeters <= rules.SafeMeters) return rules.LethalDamage;
            return (meters - rules.SafeMeters) / (rules.LethalMeters - rules.SafeMeters) * rules.LethalDamage;
        }

        // The drop the server accepts from an owner's report: never more than what it watched plus the tolerance.
        public static float AcceptedFall(float reportedMeters, float observedMeters, FallRules rules)
        {
            if (!(reportedMeters > 0f) || float.IsInfinity(reportedMeters)) return 0f;
            return Math.Min(reportedMeters, Math.Max(0f, observedMeters) + rules.ReportToleranceMeters);
        }

        // Playback speed for the legs when moving at `speed` with a clip animated for `clipSpeed`.
        public static float LegPace(float speed, float clipSpeed, GaitRules rules)
        {
            if (clipSpeed <= 0f || speed <= clipSpeed) return 1f;
            return Math.Min(rules.MaxLegPace, 1f + (speed / clipSpeed - 1f) * rules.LegPaceFollow);
        }

        // Which moment of the run cycle to show in the air: the rise pose going up, the fall pose coming down.
        public static float AirPhase(float verticalSpeed, JumpRules rules)
        {
            float t = rules.PoseSpeed <= 0f ? (verticalSpeed > 0f ? 1f : 0f) : Math.Min(1f, Math.Max(0f, (verticalSpeed + rules.PoseSpeed) / (2f * rules.PoseSpeed)));
            return rules.FallPhase + (rules.RisePhase - rules.FallPhase) * t;
        }

        // How far the knees are tucked up (0..1) for a vertical speed: all the way at the top of a jump, least
        // while rising or falling fast.
        public static float JumpTuck(float verticalSpeed, JumpRules rules)
        {
            if (rules.TuckSpeed <= 0f) return 1f;
            float fast = Math.Min(1f, Math.Abs(verticalSpeed) / rules.TuckSpeed);
            return 1f + (rules.FallTuck - 1f) * fast;
        }

        // A short reaction `t` seconds after its start: rises evenly to 1 over `attack`, then eases back to 0 by `total`.
        public static float Pulse(float t, float attack, float total)
        {
            if (t <= 0f || t >= total) return 0f;
            if (t < attack) return t / attack;
            float back = (total - t) / (total - attack);
            return back * back * (3f - 2f * back);
        }

        // The share of the full walking or sprinting speed allowed by the aim state, leaning, stance and what is in
        // the hands (`carryFactor`, CarryFactor). The owner moves with it; anything checking the owner's speed must use
        // the same product. Slow walking replaces the weapon's factor with the slow walk's share of the walking speed.
        public static float MoveSpeedFactor(float aimFactor, float leanFactor, float stanceFactor, float carryFactor, LocomotionParams p, bool slowWalk = false)
        {
            float hands = slowWalk ? (p.WalkSpeed > 0f ? p.SlowWalkSpeed / p.WalkSpeed : 1f) : carryFactor;
            return aimFactor * leanFactor * stanceFactor * hands;
        }

        // How much the weapon in hand slows walking and sprinting (ADR-095). There is no kind field: a primary is told
        // apart by its pellets and calibre. Nothing, a pistol or a melee weapon: 1.
        public static float CarryFactor(WeaponDef weapon, LocomotionParams p)
        {
            if (weapon == null || weapon.Category != WeaponCategory.Primary) return 1f;
            if (weapon.Pellets > 1) return p.ShotgunMoveFactor;
            return weapon.Ammo == AmmoType.Pistol9 ? p.SmgMoveFactor : weapon.Ammo == AmmoType.Rifle762 ? p.MarksmanMoveFactor : p.RifleMoveFactor;
        }

        // Free look: the view's turn away from the facing after adding `deltaDeg`, kept in -180..180 so the swing
        // back always takes the short way round.
        public static float WrapFreeYaw(float freeYaw, float deltaDeg)
        {
            float v = (freeYaw + deltaDeg) % 360f;
            if (v >= 180f) v -= 360f;
            else if (v < -180f) v += 360f;
            return v;
        }

        // One frame of the swing back toward 0: eased (fast at first, gentle at the end) but never faster than
        // the rule's top speed, and never past 0.
        public static float FreeLookReturn(float freeDeg, float dt, FreeLookRules rules)
        {
            float ease = Math.Abs(freeDeg) * (1f - (float)Math.Exp(-rules.ReturnEasePerSecond * dt));
            float step = Math.Min(rules.ReturnDegPerSecond * dt, Math.Max(ease, 30f * dt));
            return Math.Abs(freeDeg) <= step ? 0f : freeDeg - Math.Sign(freeDeg) * step;
        }

        // World-space planar velocity from stick input (x = right, y = forward) relative to a yaw heading.
        public static void PlanarVelocity(float moveX, float moveY, float yawDeg, bool sprint, LocomotionParams p, out float vx, out float vz, float speedFactor = 1f)
        {
            double len = Math.Sqrt(moveX * moveX + moveY * moveY);
            if (len < 1e-4) { vx = 0; vz = 0; return; }
            if (len > 1) { moveX = (float)(moveX / len); moveY = (float)(moveY / len); }
            double yaw = yawDeg * Math.PI / 180.0;
            double sin = Math.Sin(yaw), cos = Math.Cos(yaw);
            // Sprint only applies when moving forward.
            float speed = (sprint && moveY > 0.1f ? p.SprintSpeed : p.WalkSpeed) * speedFactor;
            vx = (float)(moveX * cos + moveY * sin) * speed;
            vz = (float)(moveY * cos - moveX * sin) * speed;
        }

        // One frame of the planar velocity (vx, vz) moving toward the wanted one: speeding up at most at Acceleration,
        // slowing down (including a change of direction's lost part) at most at Deceleration. 0 disables either limit.
        public static void Accelerate(ref float vx, ref float vz, float wantX, float wantZ, float dt, LocomotionParams p)
        {
            float dx = wantX - vx, dz = wantZ - vz;
            double gap = Math.Sqrt(dx * dx + dz * dz);
            if (gap < 1e-6) { vx = wantX; vz = wantZ; return; }
            // Speeding up when the wanted speed is higher than the current one; otherwise it is slowing or turning.
            bool faster = wantX * wantX + wantZ * wantZ > vx * vx + vz * vz;
            float rate = faster ? p.Acceleration : p.Deceleration;
            if (rate <= 0f || gap <= rate * dt) { vx = wantX; vz = wantZ; return; }
            float k = (float)(rate * dt / gap);
            vx += dx * k;
            vz += dz * k;
        }

        public static float VerticalVelocity(float current, bool grounded, bool jump, float dt, LocomotionParams p)
        {
            if (grounded)
                return jump ? p.JumpSpeed : p.GroundedStick;
            return current + p.Gravity * dt;
        }
    }
}

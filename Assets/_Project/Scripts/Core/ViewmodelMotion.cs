using System;

namespace Game.Core
{
    // How the first-person weapon (and the arms holding it) moves on top of its hip or sights spot (#217):
    // walking bob, turn sway, sprint carry, jump lift, landing dip, breathing and the shot kick. Camera space:
    // x right, y up, z forward (meters); angles in degrees with Unity's Euler signs (pitch down +, yaw right +).
    public struct ViewmodelInput
    {
        public float Dt;
        public float Speed;          // horizontal m/s
        public float RunSpeed;       // m/s that counts as full stride
        public float Sprint01;       // 0..1 sprinting
        public float Aim01;          // 0 hip .. 1 sights
        public bool Airborne;
        public float VerticalSpeed;  // m/s, up positive
        public float LookYawRate;    // deg/s the view turned right
        public float LookPitchRate;  // deg/s the view tipped down
    }

    public struct ViewmodelPose
    {
        public float X, Y, Z, PitchDeg, YawDeg, RollDeg;
    }

    public sealed class ViewmodelMotion
    {
        public const float BobY = 0.011f, BobX = 0.008f, BobRollDeg = 1.2f;
        public const float SprintDown = 0.085f, SprintPitchDeg = 24f, SprintYawDeg = -16f, SprintRollDeg = 14f;
        const float AimBobShare = 0.12f, AimSwayShare = 0.3f;

        float _phase, _move, _sprint, _breath, _land, _landV, _lift, _kick, _swayYaw, _swayPitch;
        bool _wasAirborne;
        float _fallSpeed;

        public float Phase => _phase;

        // The gun's recoil settles back in about a tenth of a second.
        public void Kick() => _kick = 1f;

        public ViewmodelPose Step(in ViewmodelInput i)
        {
            float dt = Math.Min(i.Dt, 0.05f);
            if (dt <= 0f) return default;
            float aim = Clamp01(i.Aim01);
            float run = Math.Max(0.1f, i.RunSpeed);
            float moveTarget = i.Airborne ? 0f : Clamp01(i.Speed / run);
            _move += (moveTarget - _move) * (1f - (float)Math.Exp(-8f * dt));
            _sprint += (Clamp01(i.Sprint01) - _sprint) * (1f - (float)Math.Exp(-7f * dt));
            // Steps per second grow with speed; the bob is one cycle per two steps.
            _phase += dt * (1.5f + 2.4f * _move + 0.8f * _sprint) * 2f * (float)Math.PI * 0.5f;
            _breath += dt * 1.25f * 2f * (float)Math.PI;

            // Landing: the fall speed on the frame the feet touch kicks a spring down.
            if (i.Airborne) _fallSpeed = Math.Max(_fallSpeed, -i.VerticalSpeed);
            if (_wasAirborne && !i.Airborne) _landV -= Math.Min(_fallSpeed, 10f) * 0.1f;
            if (!i.Airborne) _fallSpeed = 0f;
            _wasAirborne = i.Airborne;
            _landV += (-90f * _land - 10f * _landV) * dt;
            _land += _landV * dt;
            _lift += ((i.Airborne ? 1f : 0f) - _lift) * (1f - (float)Math.Exp(-9f * dt));
            _kick = Math.Max(0f, _kick - dt / 0.1f);

            // Turning drags the gun the other way and it follows the view back.
            float swayGain = 1f - (1f - AimSwayShare) * aim;
            _swayYaw = Clamp(_swayYaw - i.LookYawRate * dt * 0.06f * swayGain, -4f, 4f);
            _swayPitch = Clamp(_swayPitch - i.LookPitchRate * dt * 0.05f * swayGain, -3f, 3f);
            float decay = (float)Math.Exp(-9f * dt);
            _swayYaw *= decay; _swayPitch *= decay;

            float bob = _move * (1f - (1f - AimBobShare) * aim) * (1f + 0.5f * _sprint);
            float s1 = (float)Math.Sin(_phase), s2 = (float)Math.Sin(_phase * 2f);
            float breath = (float)Math.Sin(_breath) * (1f - 0.8f * _move) * (1f - 0.4f * aim);
            // Sprint carry only at the hip: aiming brings the gun up whatever the legs do.
            float carry = _sprint * (1f - aim);
            var p = new ViewmodelPose
            {
                X = s1 * BobX * bob + _swayYaw * 0.004f + carry * 0.03f,
                // s2 squared, not |s2|: the same dip per step without the corner that jolted the weapon each step (WEAPON_MOTION_AUDIT #5).
                Y = -s2 * s2 * BobY * bob + breath * 0.0022f + _land - carry * SprintDown + _lift * 0.018f - _kick * 0.004f,
                Z = -_kick * 0.035f - carry * 0.02f,
                PitchDeg = -_kick * 2.6f + _swayPitch + breath * 0.18f + carry * SprintPitchDeg - _lift * 2f + _land * 30f,
                YawDeg = _swayYaw + s1 * 0.5f * bob + carry * SprintYawDeg,
                RollDeg = s1 * BobRollDeg * bob + carry * SprintRollDeg - _swayYaw * 0.5f,
            };
            return p;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
    }
}

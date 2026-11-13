using System;

namespace Game.Core
{
    // How low the body is held (ADR-053). Prone has rules and an input but no pose yet (it needs a crawl clip).
    public enum Stance { Stand = 0, Crouch = 1, Prone = 2 }

    // What one stance changes. Loaded from data.
    [Serializable]
    public class StancePose
    {
        public float EyeDropMeters;          // the eyes sit this far below the upright eye height
        public float CapsuleHeight = 1.8f;   // height of the collision capsule, feet staying where they are
        public float MoveFactor = 1f;
        public float SpreadFactor = 1f;
        public float FootstepVolumeFactor = 1f;
    }

    [Serializable]
    public class StanceRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static StanceRules Current = new StanceRules();

        public StancePose Stand = new StancePose();
        // The capsule is low enough to pass a 1.2 m gap with the character controller's skin on both ends.
        public StancePose Crouch = new StancePose { EyeDropMeters = 0.65f, CapsuleHeight = 1.0f, MoveFactor = 0.55f, SpreadFactor = 0.7f, FootstepVolumeFactor = 0.4f };
        public StancePose Prone = new StancePose { EyeDropMeters = 1.3f, CapsuleHeight = 0.7f, MoveFactor = 0.25f, SpreadFactor = 0.45f, FootstepVolumeFactor = 0.2f };
        public bool ProneEnabled = false;    // off until there is a pose for it
        public float ChangePerSecond = 5f;   // getting down or up takes 1 / this seconds
        public float HeadClearanceMeters = 0.05f; // free space needed above the raised head before standing up

        // There is no crouch clip: the knees are bent in code and the model is lowered by what that takes off
        // the legs, so the feet stay on the ground. Angles are measured from vertical: the thigh points down and
        // forward, the shin down and back, so the knee flexes by their sum. 58 + 50 = 108 degrees of flexion is
        // an interior knee angle of 72 and never beyond the 110 a real knee folds to; both thighs and shins have
        // about the same length, so the feet land 4 cm ahead of the hips and the hips come down about 0.37 m.
        public float CrouchThighDeg = 58f;
        public float CrouchShinDeg = 50f;
        public float CrouchTorsoPitchDeg = 10f; // forward, empty hands only (a weapon pose keeps the weapon level)
        // Crouch-walking keeps the walk clip's leg swing and bends the knees by this share of the standing crouch
        // (a full bend plus a stride would fold the legs), reached at CrouchWalkFullSpeed m/s.
        public float CrouchWalkBend = 0.65f;
        public float CrouchWalkFullSpeed = 1.5f;
        public float CrouchMinLegPace = 0.4f;   // slowest the walk clip is played while crouch-walking

        public StancePose For(Stance stance) => stance == Stance.Crouch ? Crouch : stance == Stance.Prone ? Prone : Stand;
    }

    public static class Stances
    {
        public static float EyeHeight(float uprightEyeHeight, Stance stance, StanceRules rules) =>
            uprightEyeHeight - rules.For(stance).EyeDropMeters;

        public static float MoveSpeed(float uprightSpeed, Stance stance, StanceRules rules) =>
            uprightSpeed * rules.For(stance).MoveFactor;

        // The stance after this frame's input. The crouch and prone keys toggle; jumping or starting to sprint
        // gets up instead. `canRise`: there is head room for the taller capsule; without it the stance stays.
        public static Stance Next(Stance current, bool crouchPressed, bool pronePressed, bool jump, bool sprint, bool canRise, StanceRules rules)
        {
            var wanted = current;
            if (crouchPressed) wanted = current == Stance.Crouch ? Stance.Stand : Stance.Crouch;
            else if (pronePressed && rules.ProneEnabled) wanted = current == Stance.Prone ? Stance.Stand : Stance.Prone;
            if (current != Stance.Stand && (jump || sprint)) wanted = Stance.Stand;
            if (wanted == Stance.Prone && !rules.ProneEnabled) wanted = current;
            bool rises = rules.For(wanted).CapsuleHeight > rules.For(current).CapsuleHeight;
            return rises && !canRise ? current : wanted;
        }

        // How far the hips come down when both knees bend by `kneeDeg` (thigh forward, shin back by the same
        // angle from upright) on legs `legLength` long.
        public static float KneeBendDrop(float legLength, float kneeDeg) =>
            CrouchHipDrop(legLength, kneeDeg, kneeDeg);

        // Hips lowered when the thigh points `thighDeg` forward and the shin `shinBackDeg` back of vertical, with
        // the thigh and the shin each half of `legLength`: the ankle sat legLength/2 * (cos t + cos s) under the hip.
        public static float CrouchHipDrop(float legLength, float thighDeg, float shinBackDeg) =>
            legLength * 0.5f * (2f - Cos(thighDeg) - Cos(shinBackDeg));

        // How much the knee folds: the angle between the shin's line and the thigh's line continued through the knee.
        public static float KneeFlexion(float thighDeg, float shinBackDeg) => thighDeg + shinBackDeg;

        // Share of the standing crouch bend shown at `walkBlend` (0 = still, 1 = walking fast): from 1 down to `walkBend`.
        public static float CrouchBendScale(float walkBlend, float walkBend) =>
            1f + (walkBend - 1f) * Math.Min(1f, Math.Max(0f, walkBlend));

        // Playback speed of the walk clip while crouched: in proportion to how much slower than the clip the body
        // moves (so the feet do not skate), never below `minPace`, never above 1.
        public static float CrouchLegPace(float speed, float clipSpeed, float minPace)
        {
            if (clipSpeed <= 0f) return 1f;
            return Math.Min(1f, Math.Max(minPace, speed / clipSpeed));
        }

        static float Cos(float deg) => (float)Math.Cos(deg * Math.PI / 180.0);
    }
}

using System;

namespace Game.Core
{
    // How low the body is held (ADR-053, prone ADR-058). There are no crouch or crawl clips: the poses are made in code.
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
        // Speeds (ADR-095): one factor puts running, sprinting and slow walking within 3% of PUBG's 3.4, 4.8 and 1.3 m/s.
        public StancePose Crouch = new StancePose { EyeDropMeters = 0.65f, CapsuleHeight = 1.0f, MoveFactor = 0.745f, SpreadFactor = 0.5f, FootstepVolumeFactor = 0.4f };
        // Lying down: eyes 0.4 m over the ground (upright eyes 1.6), crawling at 0.255 * 4.7 = 1.2 m/s. The capsule
        // stays at least as tall as it is wide (the character controller's limit: height >= 2 * radius, here 0.7).
        public StancePose Prone = new StancePose { EyeDropMeters = 1.2f, CapsuleHeight = 0.7f, MoveFactor = 0.255f, SpreadFactor = 0.2f, FootstepVolumeFactor = 0.1f };
        public bool ProneEnabled = true;
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

        // Lying on the belly facing the aim. The whole model is pitched forward about the character's right axis
        // (a positive turn tips the top of the body forward and down), with the feet as the pivot and the model
        // then moved back so the head ends up where the eyes (and the capsule) are. The chest and head are
        // turned back up by ProneHeadLiftDeg, and the arms by what is left of the pitch so a held weapon stays level.
        public float ProneModelPitchDeg = 85f;
        public float ProneHeadLiftDeg = 40f;
        public float ProneBodyHeight = 0.15f;      // the spine line lies this high over the ground (the body is about 0.3 m thick)
        public float ProneModelBackShift = 1.35f;  // metres the model is moved back from the capsule so its head sits under the eyes
        public float ProneMinLegPace = 0.3f;       // slowest the walk clip is played while crawling

        public StancePose For(Stance stance) => stance == Stance.Crouch ? Crouch : stance == Stance.Prone ? Prone : Stand;
    }

    public static class Stances
    {
        public static float EyeHeight(float uprightEyeHeight, Stance stance, StanceRules rules) =>
            uprightEyeHeight - rules.For(stance).EyeDropMeters;

        public static float MoveSpeed(float uprightSpeed, Stance stance, StanceRules rules) =>
            uprightSpeed * rules.For(stance).MoveFactor;

        // The stance after this frame's input. The crouch and prone keys toggle; jumping gets up instead, and so
        // does sprinting from lying down (crouched, it sprints crouched, ADR-095). `canRise`: there is head room for the taller capsule; without it the stance stays.
        public static Stance Next(Stance current, bool crouchPressed, bool pronePressed, bool jump, bool sprint, bool canRise, StanceRules rules)
        {
            var wanted = current;
            if (crouchPressed) wanted = current == Stance.Crouch ? Stance.Stand : Stance.Crouch;
            else if (pronePressed && rules.ProneEnabled) wanted = current == Stance.Prone ? Stance.Stand : Stance.Prone;
            if (current != Stance.Stand && (jump || (sprint && current == Stance.Prone))) wanted = Stance.Stand;
            if (wanted == Stance.Prone && !rules.ProneEnabled) wanted = current;
            bool rises = rules.For(wanted).CapsuleHeight > rules.For(current).CapsuleHeight;
            return rises && !canRise ? current : wanted;
        }

        // Does going from one stance to the other raise the top of the body?
        public static bool Rises(Stance from, Stance to, StanceRules rules) =>
            rules.For(to).CapsuleHeight > rules.For(from).CapsuleHeight;

        // How far the arms are turned back when the body is pitched down and the chest lifted, so that what the
        // arms were holding level stays level: the pitch less the head lift.
        public static float ProneArmCounterDeg(StanceRules rules) => rules.ProneModelPitchDeg - rules.ProneHeadLiftDeg;

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

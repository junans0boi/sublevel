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

        // The crouch is clips (UAL bare-handed ADR-255, Mixamo rifle kneel and crouch walks ADR-536), no code knee bend.
        public float CrouchMinLegPace = 0.4f;   // slowest the walk clip is played while easing into the crouch

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

        // How far the hips come down when both knees bend by `kneeDeg` (thigh forward, shin back by the same
        // angle from upright) on legs `legLength` long.
        public static float KneeBendDrop(float legLength, float kneeDeg) =>
            CrouchHipDrop(legLength, kneeDeg, kneeDeg);

        // Hips lowered when the thigh points `thighDeg` forward and the shin `shinBackDeg` back of vertical, with
        // the thigh and the shin each half of `legLength`: the ankle sat legLength/2 * (cos t + cos s) under the hip.
        public static float CrouchHipDrop(float legLength, float thighDeg, float shinBackDeg) =>
            legLength * 0.5f * (2f - Cos(thighDeg) - Cos(shinBackDeg));

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

using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The seated pose on the vehicle models (#89), on top of PoseSeated:
    // - Empty hands at the driver's seat reach the model's own grips (steering wheel rim, handlebar ends) with a
    //   two-bone arm reach (upper arm aimed, elbow bent to the distance, elbow turned down and out), and the fingers
    //   close round the rim or bar. The fixed arm angles of PoseSeated stay as the start (and the fallback without
    //   grips), so every model lines up without its own arm numbers.
    // - Astride, both feet point along the machine (the idle clip turns the left foot out, which splays it off the peg).
    public partial class CombatantAnimation
    {
        const float GripCurlDeg = 70f;          // each finger joint, closed round a rim or a bar
        const float ThumbCurlDeg = 35f;
        const float ElbowOutShare = 0.6f;       // elbows point down and this much outward
        const float FootOutDeg = 4f;            // astride, the feet stay this little turned out

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };

        readonly Transform[] _forearm = new Transform[2], _wrist = new Transform[2];
        readonly Transform[][] _fingers = new Transform[2][];
        readonly Transform[] _thumb = new Transform[2];
        readonly Transform[] _toe = new Transform[2];
        bool _gripBonesLooked;

        // Called at the end of PoseSeated. `free` is how much of the empty-hands pose shows.
        void PoseVehicleHands(float free, bool astride, float seated)
        {
            if (!_gripBonesLooked) FindGripBones();
            if (astride) KeepFeetForward(seated);
            if (!_driverPose || free <= 0.001f) return;
            var seat = VehicleSeat.SeatOf(_combatant);
            if (seat == null || !seat.HasGrips) return;
            var left = seat.GripLeft.position;
            var right = seat.GripRight.position;
            // The bar each hand closes round: a handlebar runs grip to grip; a steering wheel's rim runs round its
            // column (the grips' parent turns about its local Z), so at each grip it is the rim's tangent.
            var wheel = seat.GripLeft.parent;
            Vector3 axisL, axisR;
            if (astride || wheel == null) axisL = axisR = (right - left).normalized;
            else
            {
                axisL = Vector3.Cross(wheel.forward, left - wheel.position).normalized;
                axisR = Vector3.Cross(wheel.forward, right - wheel.position).normalized;
            }
            Grip(0, ArmLeft, left, axisL, free);
            Grip(1, ArmRight, right, axisR, free);
        }

        // #102: the palm, not the wrist bone, goes onto the grip, and the hand turns so its knuckle line lies along
        // the bar: the arm reaches to the wrist point that puts the knuckles just past the bar, then the hand is
        // pointed on along the arm (square to the bar) and twisted so the fingers curl round it.
        const float PalmShare = 0.8f; // of wrist -> middle knuckle: where the bar sits in the closed hand

        void Grip(int side, int upperIndex, Vector3 grip, Vector3 axis, float weight)
        {
            var wrist = _wrist[side];
            var fingers = _fingers[side];
            var upper = _bones[upperIndex];
            if (wrist == null || upper == null || fingers == null || fingers[3] == null || axis.sqrMagnitude < 0.5f)
            {
                Reach(side, upperIndex, grip, weight);
                return;
            }
            float palm = Vector3.Distance(wrist.position, fingers[3].position) * PalmShare;
            var along = Vector3.ProjectOnPlane(grip - upper.position, axis);
            if (along.sqrMagnitude < 1e-6f) along = transform.forward;
            along.Normalize();
            Reach(side, upperIndex, grip - along * palm, weight, false);

            // Point the hand on along the arm, square to the bar...
            var pointing = fingers[3].position - wrist.position;
            var turn = Quaternion.FromToRotation(pointing, Vector3.Slerp(pointing.normalized, along, weight));
            wrist.rotation = turn * wrist.rotation;
            // ...then about that line until the knuckles (index -> little finger) lie along the bar, whichever way
            // round is nearer the clip's hand.
            if (fingers[9] != null)
            {
                var line = (fingers[3].position - wrist.position).normalized;
                var knuckles = Vector3.ProjectOnPlane(fingers[9].position - fingers[0].position, line);
                var bar = Vector3.ProjectOnPlane(axis, line);
                if (Vector3.Dot(knuckles, bar) < 0f) bar = -bar;
                if (knuckles.sqrMagnitude > 1e-8f && bar.sqrMagnitude > 1e-8f)
                    wrist.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(knuckles, bar, line) * weight, line) * wrist.rotation;
            }
            CloseHand(side, weight);
        }

        void FindGripBones()
        {
            _gripBonesLooked = true;
            for (int side = 0; side < 2; side++)
            {
                var upper = _bones[side == 0 ? ArmLeft : ArmRight];
                string suffix = side == 0 ? ".L" : ".R";
                _forearm[side] = Deep(upper, "LowerArm" + suffix);
                _wrist[side] = Deep(upper, "Wrist" + suffix);
                _fingers[side] = new Transform[FingerNames.Length * 3];
                for (int f = 0; f < FingerNames.Length; f++)
                    for (int j = 0; j < 3; j++)
                        _fingers[side][f * 3 + j] = Deep(_wrist[side], FingerNames[f] + (j + 1) + suffix);
                _thumb[side] = Deep(_wrist[side], "Thumb2" + suffix);
                var foot = _bones[side == 0 ? FootLeft : FootRight];
                _toe[side] = foot != null && foot.childCount > 0 ? foot.GetChild(0) : null;
            }
        }

        static Transform Deep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = Deep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        // Upper arm, forearm and wrist reach `target` (blended in by `weight`).
        void Reach(int side, int upperIndex, Vector3 target, float weight, bool close = true)
        {
            var upper = _bones[upperIndex];
            var fore = _forearm[side];
            var wrist = _wrist[side];
            if (upper == null || fore == null || wrist == null) return;
            var s = upper.position;
            float a = Vector3.Distance(s, fore.position), b = Vector3.Distance(fore.position, wrist.position);
            if (a < 1e-4f || b < 1e-4f) return;
            target = Vector3.Lerp(wrist.position, target, weight);
            float d = Mathf.Clamp(Vector3.Distance(s, target), Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);

            // Elbow: bend to the interior angle that puts the wrist `d` from the shoulder.
            var e = fore.position;
            var upperDir = e - s;
            var foreDir = wrist.position - e;
            var bendAxis = Vector3.Cross(upperDir, foreDir);
            if (bendAxis.sqrMagnitude < 1e-8f) bendAxis = Vector3.Cross(upperDir, -transform.up);
            if (bendAxis.sqrMagnitude < 1e-8f) bendAxis = transform.right;
            bendAxis.Normalize();
            float current = Vector3.Angle(-upperDir, foreDir);
            float wanted = Mathf.Acos(Mathf.Clamp((a * a + b * b - d * d) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            fore.rotation = Quaternion.AngleAxis(current - wanted, bendAxis) * fore.rotation;

            // Upper arm: shoulder-to-wrist onto shoulder-to-target.
            var aim = Quaternion.FromToRotation(wrist.position - s, target - s);
            aim.ToAngleAxis(out float aimDeg, out var aimAxis);
            if (aimDeg > 180f) aimDeg -= 360f;
            if (Mathf.Abs(aimDeg) > 1e-3f && aimAxis.sqrMagnitude > 0.5f) Turn(upperIndex, aimDeg, aimAxis);

            // Then about that line, so the elbow points down and out instead of wherever the clip left it.
            var line = (target - s).normalized;
            var elbowOff = Vector3.ProjectOnPlane(fore.position - s, line);
            var outward = side == 0 ? -transform.right : transform.right;
            var pole = Vector3.ProjectOnPlane(-transform.up + outward * ElbowOutShare, line);
            if (elbowOff.sqrMagnitude > 1e-6f && pole.sqrMagnitude > 1e-6f)
                Turn(upperIndex, Vector3.SignedAngle(elbowOff, pole, line), line);

            if (close) CloseHand(side, weight);
        }

        // The fingers curl across the palm: about the knuckle line (index to little finger).
        void CloseHand(int side, float weight)
        {
            var fingers = _fingers[side];
            if (fingers == null || fingers[0] == null || fingers[9] == null) return;
            var across = fingers[0].position - fingers[9].position; // little finger -> index
            if (across.sqrMagnitude < 1e-8f) return;
            across.Normalize();
            // Right hand: index on the thumb side; the same turn closes both hands once mirrored.
            float sign = side == 0 ? 1f : -1f;
            for (int i = 0; i < fingers.Length; i++)
                if (fingers[i] != null) fingers[i].rotation = Quaternion.AngleAxis(sign * GripCurlDeg * weight, across) * fingers[i].rotation;
            if (_thumb[side] != null)
                _thumb[side].rotation = Quaternion.AngleAxis(sign * ThumbCurlDeg * weight, across) * _thumb[side].rotation;
        }

        // Toes along the machine (a little out), about the body's up.
        void KeepFeetForward(float seated)
        {
            for (int side = 0; side < 2; side++)
            {
                var foot = _bones[side == 0 ? FootLeft : FootRight];
                var toe = _toe[side];
                if (foot == null || toe == null) continue;
                var along = Vector3.ProjectOnPlane(toe.position - foot.position, transform.up);
                if (along.sqrMagnitude < 1e-8f) continue;
                float outDeg = side == 0 ? -FootOutDeg : FootOutDeg;
                float off = Vector3.SignedAngle(along, Quaternion.AngleAxis(outDeg, transform.up) * transform.forward, transform.up);
                Turn(side == 0 ? FootLeft : FootRight, off * seated, transform.up);
            }
        }
    }
}

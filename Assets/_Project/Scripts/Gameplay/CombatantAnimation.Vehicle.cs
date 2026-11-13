using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The seated pose on the vehicle models (#89, #102, U2-U6), on top of PoseSeated:
    // - Driver, empty hands: the palm (not the wrist bone) goes onto the model's grips (steering wheel rim, handlebar
    //   ends), the knuckle line along the rim's tangent or the bar, the fingers closed round it. When the grips are
    //   out of reach the torso leans forward until they are not (a rider leans much further than a driver).
    //   A steering wheel turns up to 150 degrees, the hands follow it only to ±HandFollowDeg (they stay about a quarter
    //   to three), then the rim slides through them (the arms never cross).
    // - Passengers, empty hands: palms down on the thighs, so the arms stay inside the cabin.
    // - Hips onto the seat's measured cushion point (SeatN/Cushion, ADR-077) when the model has one.
    // - Astride: the feet onto the pegs (Seat0/PegLeft, PegRight), the knees towards the knee points on the tank
    //   (KneeLeft, KneeRight); without them the fixed seated leg angles, feet along the machine.
    // - In a car the upper body sways out of a turn; everyone's head looks where the player looks (the local player)
    //   or straight ahead (others: the look is not replicated while seated), so a leaning rider keeps the head up.
    public partial class CombatantAnimation
    {
        const float GripCurlDeg = 70f;          // each finger joint, closed round a rim or a bar
        const float RestCurlDeg = 15f;          // each finger joint, a hand lying on a thigh
        const float ThumbCurlDeg = 35f;
        const float ElbowOutShare = 0.6f;       // elbows point down and this much outward
        const float FootOutDeg = 4f;            // astride, the feet stay this little turned out
        const float PalmShare = 0.8f;           // of wrist -> middle knuckle: where the bar sits in the closed hand
        const float PalmDepth = 0.03f;          // the bar's centre this far off the hand's bone line, on the palm side
        const float ReachShare = 0.94f;         // of the full arm length: the elbows stay a little bent (car)
        const float BikeReachShare = 0.99f;     // a rider's arms are near straight before the body leans
        const float CarMaxLeanDeg = 15f, BikeMaxLeanDeg = 20f;
        const float BikeBaseLeanDeg = 0f;       // only what the reach needs
        public const float HandFollowDeg = 30f; // U5: the hands stay about a quarter to three, the rim slides past
        const float ThighHandShare = 0.6f;      // hip -> knee: where a resting hand lies
        const float ThighTop = 0.08f;           // the hand lies this far over the thigh bone
        const float AnkleOverSole = 0.08f, AnkleBehindBall = 0.08f;
        const float SwayDegPerAccel = 0.6f, SwayMaxDeg = 8f, SwayEasePerSecond = 4f;
        const float HeadYawMaxDeg = 70f, HeadPitchMaxDeg = 35f;

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };

        readonly Transform[] _forearm = new Transform[2], _wrist = new Transform[2];
        readonly Transform[][] _fingers = new Transform[2][];
        readonly Transform[] _thumb = new Transform[2];
        readonly Transform[] _toe = new Transform[2];
        readonly Transform[] _fingerTip = new Transform[2];
        Transform _head;
        Quaternion _headBind;   // the head's rotation relative to the body's facing, as the prefab was saved
        bool _gripBonesLooked;
        VehicleSeat _anchorsOf;
        readonly Transform[] _pegs = new Transform[2], _knees = new Transform[2];
        Transform _cushion;
        Vehicle _swayOf;
        float _swayHeading, _sway;
        Vector3 _swayPosition;

        // Test hooks: each hand's palm centre and middle fingertip as the seated pose left them (a test coroutine
        // resumes before LateUpdate, when the bones still show the clip), and the torso's up then.
        readonly Vector3[] _palmShown = new Vector3[2], _tipShown = new Vector3[2];
        public Vector3 PalmPoint(int side) => _palmShown[side];
        public Vector3 FingerTip(int side) => _tipShown[side];
        public Vector3 TorsoUpShown { get; private set; }

        // Where the posed head's eyes are (the seated eye view on a motorcycle follows the rider's forward lean).
        public bool HeadEye(out Vector3 eye)
        {
            eye = _head != null ? _head.position + transform.up * HeadEyeUp + transform.forward * HeadEyeForward : transform.position;
            return _head != null && _seated > 0.99f;
        }
        const float HeadEyeUp = 0.08f, HeadEyeForward = 0.1f;

        Vector3 PalmNow(int side) =>
            _wrist[side] != null && _fingers[side] != null && _fingers[side][3] != null
                ? Vector3.Lerp(_wrist[side].position, _fingers[side][3].position, PalmShare) : transform.position;

        // Called at the end of PoseSeated. `free` is how much of the empty-hands pose shows.
        void PoseVehicleHands(float free, bool astride, float seated)
        {
            if (!_gripBonesLooked) FindGripBones();
            var seat = VehicleSeat.SeatOf(_combatant);
            if (astride && !PlaceFeetOnPegs(seat, seated)) KeepFeetForward(seated);
            SwayInTurns(seat, astride, seated);

            if (free > 0.001f)
            {
                if (_driverPose && seat != null && seat.HasGrips) HoldGrips(seat, astride, free);
                else if (!_driverPose) RestHandsOnThighs(free);
            }
            LookAhead(seated);
            for (int side = 0; side < 2; side++)
            {
                _palmShown[side] = PalmNow(side);
                _tipShown[side] = _fingerTip[side] != null ? _fingerTip[side].position : _palmShown[side];
            }
            if (_bones[Torso] != null) TorsoUpShown = _bones[Torso].up;
        }

        // ---- Driver ----

        void HoldGrips(VehicleSeat seat, bool astride, float free)
        {
            var left = seat.GripLeft.position;
            var right = seat.GripRight.position;
            var wheel = seat.GripLeft.parent;
            bool rim = !astride && wheel != null;
            Vector3 axisL, axisR, wantL, wantR;
            if (rim)
            {
                // U5: the hands follow a turning wheel only so far; past that the rim slides through them.
                var rig = wheel.GetComponentInParent<VehicleRig>();
                float shown = rig != null ? rig.SteeringShownDeg : 0f;
                float back = HandWheelDeg(shown) - shown;
                if (Mathf.Abs(back) > 1e-3f)
                {
                    var undo = Quaternion.AngleAxis(back, wheel.forward);
                    left = wheel.position + undo * (left - wheel.position);
                    right = wheel.position + undo * (right - wheel.position);
                }
                // The rim runs round the column (the wheel's Z): at each grip it is the rim's tangent, and the palm
                // presses on it from outside, facing the hub.
                axisL = Vector3.Cross(wheel.forward, left - wheel.position).normalized;
                axisR = Vector3.Cross(wheel.forward, right - wheel.position).normalized;
                wantL = wheel.position - left;
                wantR = wheel.position - right;
            }
            else
            {
                axisL = axisR = (right - left).normalized;
                // Over the bar from behind: the palms face forward and down.
                wantL = wantR = transform.forward - transform.up;
            }

            // Out of reach: lean the torso forward until both palms get there with the elbows a little bent.
            var torso = _bones[Torso];
            Transform upL = _bones[ArmLeft], upR = _bones[ArmRight];
            if (torso != null && upL != null && upR != null && _forearm[0] != null && _wrist[0] != null)
            {
                // A rider starts from upright (the clip's own forward hunch taken out), so the lean is only what the
                // reach needs: a sport / dirt bike more, a cruiser less.
                if (astride)
                {
                    var torsoUp = Vector3.ProjectOnPlane(torso.up, transform.right);
                    if (torsoUp.sqrMagnitude > 1e-6f) Turn(Torso, -Vector3.SignedAngle(transform.up, torsoUp, transform.right) * free, transform.right);
                }
                float arm = (Vector3.Distance(upL.position, _forearm[0].position) + Vector3.Distance(_forearm[0].position, _wrist[0].position)) * (astride ? BikeReachShare : ReachShare);
                float palm = PalmLength(0);
                float lean = LeanToReach(torso.position, transform.right, upL.position, left, upR.position, right, arm + palm,
                    astride ? BikeMaxLeanDeg : CarMaxLeanDeg);
                if (astride) lean = Mathf.Max(lean, BikeBaseLeanDeg);
                Turn(Torso, lean * free, transform.right);
            }
            Grip(0, ArmLeft, left, axisL, wantL, GripCurlDeg, free);
            Grip(1, ArmRight, right, axisR, wantR, GripCurlDeg, free);
        }

        // U5: how far the hands turn with a steering wheel turned `wheelDeg`.
        public static float HandWheelDeg(float wheelDeg) => Mathf.Clamp(wheelDeg, -HandFollowDeg, HandFollowDeg);

        // The forward lean (degrees about `axis` through `pivot`, 0..maxDeg) that brings both shoulders within `reach`
        // of their targets; maxDeg when even that is not enough.
        public static float LeanToReach(Vector3 pivot, Vector3 axis, Vector3 shoulderL, Vector3 targetL, Vector3 shoulderR, Vector3 targetR, float reach, float maxDeg)
        {
            float Short(float deg)
            {
                var turn = Quaternion.AngleAxis(deg, axis);
                return Mathf.Max(Vector3.Distance(pivot + turn * (shoulderL - pivot), targetL), Vector3.Distance(pivot + turn * (shoulderR - pivot), targetR)) - reach;
            }
            if (Short(0f) <= 0f) return 0f;
            if (Short(maxDeg) > 0f) return maxDeg;
            float lo = 0f, hi = maxDeg;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Short(mid) > 0f) lo = mid; else hi = mid;
            }
            return hi;
        }

        // ---- Passengers ----

        // U3: palms down on the thighs (between hip and knee, on top), fingers a little curled.
        void RestHandsOnThighs(float free)
        {
            for (int side = 0; side < 2; side++)
            {
                Transform thigh = _bones[side == 0 ? ThighLeft : ThighRight], knee = _bones[side == 0 ? ShinLeft : ShinRight];
                if (thigh == null || knee == null) continue;
                var spot = Vector3.Lerp(thigh.position, knee.position, ThighHandShare) + transform.up * ThighTop;
                Grip(side, side == 0 ? ArmLeft : ArmRight, spot, transform.right, -transform.up, RestCurlDeg, free);
            }
        }

        // ---- Hands ----

        float PalmLength(int side) =>
            _wrist[side] != null && _fingers[side] != null && _fingers[side][3] != null
                ? Vector3.Distance(_wrist[side].position, _fingers[side][3].position) * PalmShare : 0.08f;

        // The hand's frame for holding a bar along `bar`, reached from `toBar` (shoulder -> bar), palm towards `palmWant`:
        // `line` wrist -> knuckles (square to the bar), `knuckles` little finger -> index finger (along the bar),
        // `palm` out of the palm. Right hand: palm = line x knuckles; left hand mirrored.
        public static void HandFrame(Vector3 toBar, Vector3 bar, Vector3 palmWant, bool right, out Vector3 line, out Vector3 knuckles, out Vector3 palm)
        {
            bar.Normalize();
            line = Vector3.ProjectOnPlane(toBar, bar);
            if (line.sqrMagnitude < 1e-8f) line = Vector3.ProjectOnPlane(Vector3.forward, bar);
            line.Normalize();
            palm = Vector3.Cross(line, bar).normalized;
            if (Vector3.Dot(palm, palmWant) < 0f) palm = -palm;
            knuckles = right ? Vector3.Cross(palm, line) : Vector3.Cross(line, palm);
        }

        // #102 / U2: the arm reaches to the wrist point that puts the palm on `grip` (bar centre a little off the bone
        // line on the palm side), the hand is turned to HandFrame, then the fingers curl round towards the palm.
        void Grip(int side, int upperIndex, Vector3 grip, Vector3 bar, Vector3 palmWant, float curlDeg, float weight)
        {
            var wrist = _wrist[side];
            var fingers = _fingers[side];
            var upper = _bones[upperIndex];
            if (wrist == null || upper == null || fingers == null || fingers[3] == null || fingers[0] == null || fingers[9] == null
                || bar.sqrMagnitude < 1e-6f)
            {
                Reach(side, upperIndex, grip, weight);
                return;
            }
            HandFrame(grip - upper.position, bar, palmWant, side == 1, out var line, out var knuckles, out var palm);
            Reach(side, upperIndex, grip - line * PalmLength(side) - palm * PalmDepth, weight);

            var lineNow = fingers[3].position - wrist.position;
            var knucklesNow = Vector3.ProjectOnPlane(fingers[0].position - fingers[9].position, lineNow);
            if (lineNow.sqrMagnitude > 1e-8f && knucklesNow.sqrMagnitude > 1e-8f)
            {
                var turn = Quaternion.LookRotation(line, knuckles) * Quaternion.Inverse(Quaternion.LookRotation(lineNow, knucklesNow));
                wrist.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * wrist.rotation;
            }
            CloseHand(side, curlDeg * weight);
        }

        // The fingers curl about the knuckle line, the way that brings the tips towards the palm (measured, so it
        // holds for either hand whatever the rig's bone axes).
        void CloseHand(int side, float curlDeg)
        {
            var fingers = _fingers[side];
            var wrist = _wrist[side];
            if (fingers == null || wrist == null || fingers[0] == null || fingers[9] == null || fingers[3] == null) return;
            var line = (fingers[3].position - wrist.position).normalized;
            var across = Vector3.ProjectOnPlane(fingers[0].position - fingers[9].position, line); // little -> index
            if (across.sqrMagnitude < 1e-8f) return;
            across.Normalize();
            var palm = side == 1 ? Vector3.Cross(line, across) : Vector3.Cross(across, line);
            float sign = Vector3.Dot(Vector3.Cross(across, line), palm) > 0f ? 1f : -1f;
            for (int i = 0; i < fingers.Length; i++)
                if (fingers[i] != null) fingers[i].rotation = Quaternion.AngleAxis(sign * curlDeg, across) * fingers[i].rotation;
            if (_thumb[side] != null)
                _thumb[side].rotation = Quaternion.AngleAxis(sign * ThumbCurlDeg * curlDeg / GripCurlDeg, across) * _thumb[side].rotation;
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
                // In this rig the "1" bones of all fingers (and the thumb) start together at the root of the hand (they
                // are the palm bones): the knuckles are the "2" bones, and 2..4 are the joints that curl.
                for (int f = 0; f < FingerNames.Length; f++)
                    for (int j = 0; j < 3; j++)
                        _fingers[side][f * 3 + j] = Deep(_wrist[side], FingerNames[f] + (j + 2) + suffix);
                _thumb[side] = Deep(_wrist[side], "Thumb2" + suffix);
                var last = Deep(_wrist[side], "Middle4" + suffix);
                _fingerTip[side] = last != null && last.childCount > 0 ? last.GetChild(0) : last;
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

        // Upper arm, forearm and wrist reach `target` (blended in by `weight`), elbow down and out.
        void Reach(int side, int upperIndex, Vector3 target, float weight)
        {
            var upper = _bones[upperIndex];
            var fore = _forearm[side];
            var wrist = _wrist[side];
            if (upper == null || fore == null || wrist == null) return;
            var outward = side == 0 ? -transform.right : transform.right;
            TwoBone(upper, upperIndex, fore, -1, wrist, Vector3.Lerp(wrist.position, target, weight), -transform.up + outward * ElbowOutShare);
        }

        // Two-bone reach: `mid` bends so `end` is as far from `upper` as `target`, then `upper` aims at it and turns
        // about that line so the middle joint points towards `pole`. Bones with an index go through Turn (undone next
        // frame when no clip re-poses them).
        void TwoBone(Transform upper, int upperIndex, Transform mid, int midIndex, Transform end, Vector3 target, Vector3 pole)
        {
            var s = upper.position;
            float a = Vector3.Distance(s, mid.position), b = Vector3.Distance(mid.position, end.position);
            if (a < 1e-4f || b < 1e-4f) return;
            float d = Mathf.Clamp(Vector3.Distance(s, target), Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);

            var upperDir = mid.position - s;
            var lowerDir = end.position - mid.position;
            var bendAxis = Vector3.Cross(upperDir, lowerDir);
            if (bendAxis.sqrMagnitude < 1e-8f) bendAxis = Vector3.Cross(upperDir, pole);
            if (bendAxis.sqrMagnitude < 1e-8f) bendAxis = transform.right;
            bendAxis.Normalize();
            float current = Vector3.Angle(-upperDir, lowerDir);
            float wanted = Mathf.Acos(Mathf.Clamp((a * a + b * b - d * d) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            Rotate(mid, midIndex, current - wanted, bendAxis);

            var aim = Quaternion.FromToRotation(end.position - s, target - s);
            aim.ToAngleAxis(out float aimDeg, out var aimAxis);
            if (aimDeg > 180f) aimDeg -= 360f;
            if (Mathf.Abs(aimDeg) > 1e-3f && aimAxis.sqrMagnitude > 0.5f) Rotate(upper, upperIndex, aimDeg, aimAxis);

            var line = (target - s).normalized;
            var midOff = Vector3.ProjectOnPlane(mid.position - s, line);
            var poleOff = Vector3.ProjectOnPlane(pole, line);
            if (midOff.sqrMagnitude > 1e-6f && poleOff.sqrMagnitude > 1e-6f)
                Rotate(upper, upperIndex, Vector3.SignedAngle(midOff, poleOff, line), line);
        }

        void Rotate(Transform bone, int index, float degrees, Vector3 axis)
        {
            if (index >= 0) Turn(index, degrees, axis);
            else bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        // ---- Legs ----

        void FindSeatAnchors(VehicleSeat seat)
        {
            if (seat == _anchorsOf) return;
            _anchorsOf = seat;
            _cushion = seat != null ? seat.transform.Find("Cushion") : null;
            var r = VehicleSeat.RulesOf(_combatant);
            _cushionShift = Vector3.down * r.SeatedHipDrop + Vector3.back * r.SeatedHipBack;
            for (int side = 0; side < 2; side++)
            {
                _pegs[side] = seat != null ? seat.transform.Find(side == 0 ? "PegLeft" : "PegRight") : null;
                _knees[side] = seat != null ? seat.transform.Find(side == 0 ? "KneeLeft" : "KneeRight") : null;
            }
        }

        // The model's offset (in the body's space) that rests the hips on the cushion point. Measured here after the
        // pose and applied in Update with the rest of the model's placement, not moved in LateUpdate: the model's
        // place is reset every Update, and anything that looks before LateUpdate (a capture) would see it jump back.
        Vector3 _cushionShift;
        const float CushionShiftMax = 0.6f;

        Vector3 SeatedShift(Vector3 fromRules) => _cushion != null ? _cushionShift : fromRules;

        void SitOnCushion(float seated)
        {
            if (!_gripBonesLooked) FindGripBones();
            FindSeatAnchors(VehicleSeat.SeatOf(_combatant));
            if (_cushion == null || _hips == null || _animator == null || seated < 0.99f) return;
            var parent = _animator.transform.parent;
            var error = _cushion.position - _hips.position;
            _cushionShift = Vector3.ClampMagnitude(_cushionShift + (parent != null ? parent.InverseTransformVector(error) : error), CushionShiftMax);
        }

        // U4: the feet on the pegs the model marks, knees forward and in towards the knee points. False when unmarked.
        bool PlaceFeetOnPegs(VehicleSeat seat, float seated)
        {
            FindSeatAnchors(seat);
            if (_pegs[0] == null || _pegs[1] == null || seated < 0.001f) return false;
            for (int side = 0; side < 2; side++)
            {
                int thighIndex = side == 0 ? ThighLeft : ThighRight, shinIndex = side == 0 ? ShinLeft : ShinRight;
                Transform thigh = _bones[thighIndex], shin = _bones[shinIndex], foot = _bones[side == 0 ? FootLeft : FootRight];
                if (thigh == null || shin == null || foot == null || shin.childCount == 0) continue;
                var ankle = shin.GetChild(0);
                var target = _pegs[side].position + transform.up * AnkleOverSole - transform.forward * AnkleBehindBall;
                var shinBefore = shin.rotation;
                var footFromAnkle = foot.position - ankle.position;
                var inward = side == 0 ? transform.right : -transform.right;
                var pole = _knees[side] != null ? _knees[side].position - thigh.position : transform.forward + transform.up * 0.3f + inward * 0.4f;
                TwoBone(thigh, thighIndex, shin, shinIndex, ankle, Vector3.Lerp(ankle.position, target, seated), pole);
                var swing = shin.rotation * Quaternion.Inverse(shinBefore);
                if (!_footMoved[side])
                {
                    _footOwnPosition[side] = foot.localPosition;
                    _footMoved[side] = true;
                }
                foot.rotation = swing * foot.rotation;
                foot.position = ankle.position + swing * footFromAnkle;
                _footLeftAtPosition[side] = foot.localPosition;
            }
            KeepFeetForward(seated);
            return true;
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

        // ---- Body and head ----

        // U6: in a car the upper body sways out of a turn with the sideways acceleration (speed x turn rate, measured
        // from the vehicle's own motion so it works on every side). A rider leans with the bike instead.
        void SwayInTurns(VehicleSeat seat, bool astride, float seated)
        {
            var vehicle = seat != null ? seat.GetComponentInParent<Vehicle>() : null;
            var flat = vehicle != null ? Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up) : Vector3.zero;
            if (vehicle == null || flat.sqrMagnitude < 1e-4f || Time.deltaTime <= 0f) { _swayOf = null; _sway = 0f; return; }
            float heading = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            var position = vehicle.transform.position;
            float wanted = 0f;
            if (_swayOf == vehicle && !astride)
            {
                float turnRate = Mathf.DeltaAngle(_swayHeading, heading) * Mathf.Deg2Rad / Time.deltaTime;
                float speed = Vector3.Distance(position, _swayPosition) / Time.deltaTime;
                wanted = Mathf.Clamp(turnRate * speed * SwayDegPerAccel, -SwayMaxDeg, SwayMaxDeg);
            }
            _swayOf = vehicle;
            _swayHeading = heading;
            _swayPosition = position;
            _sway = Mathf.Lerp(_sway, wanted, 1f - Mathf.Exp(-SwayEasePerSecond * Time.deltaTime));
            // Turning right (positive yaw) throws the body left: a positive turn about the forward axis.
            Turn(Torso, _sway * seated, transform.forward);
        }

        // U4 / U6: the head turns to the view (the local player) or straight ahead, whatever the torso lean.
        void LookAhead(float seated)
        {
            if (_head == null) return;
            var look = transform.forward;
            var me = PlayerController.Local;
            if (me != null && me.gameObject == gameObject)
            {
                var local = Quaternion.Inverse(transform.rotation) * (Quaternion.Euler(-me.ViewPitch, me.ViewYaw, 0f) * Vector3.forward);
                float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -HeadYawMaxDeg, HeadYawMaxDeg);
                float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -HeadPitchMaxDeg, HeadPitchMaxDeg);
                look = transform.rotation * Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            }
            var wanted = Quaternion.LookRotation(look, transform.up) * _headBind;
            _head.rotation = Quaternion.Slerp(_head.rotation, wanted, seated);
        }

        // The head bone and its rotation relative to the body's facing in the saved pose (facing transform.forward).
        void FindHead()
        {
            _head = Deep(_spine, "Head");
            if (_head == null) return;
            _headBind = Quaternion.Inverse(transform.rotation) * _head.rotation;
        }
    }
}

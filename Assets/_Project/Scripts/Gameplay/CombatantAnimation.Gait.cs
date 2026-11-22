using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Body motion the clips do not have (CHARACTER_AUDIT #231 #234 #235 #225 #228): idle breathing and sway, foot steps
    // when turning on the spot, planting the feet on the ground under them (slopes, crouching, leaning), a crawl, and a
    // stride in a running jump. All bone turns on top of the pose, like the jump and crouch (see CombatantAnimation.cs).
    public partial class CombatantAnimation
    {
        const float BreathSeconds = 3.8f, BreathPitchDeg = 1.6f, SwayYawDeg = 0.7f, SwaySeconds = 7.3f;
        const float TurnStepMinDegPerSecond = 20f, TurnStepFullDegPerSecond = 90f, TurnStepHz = 1.3f;
        const float TurnThighDeg = 24f, TurnShinDeg = 34f, TurnStepStillSpeed = 0.5f;
        const float PlantRayUp = 0.5f, PlantRayLength = 1.1f, PlantMaxRaise = 0.3f, PlantMaxLower = 0.4f, PlantMaxBelowOrigin = 0.3f, PlantContact = 0.03f, PlantMaxSplit = 0.12f;
        const float PlantTiltMaxDeg = 35f; // the slope limit's walkable range (ADR-195: 35 deg walkable, 40 hard)
        const float PlantFadeHeight = 0.05f, // was 0.12: low Mixamo sidestep swings were pulled down and dragged over the floor (ADR-391)
                   PlantSmoothPerSecond = 16f, PlantMinMove = 0.004f;
        const float CrawlMetersPerCycle = 1.1f, CrawlRollDeg = 5f, CrawlKneeDeg = 25f;
        const float JumpStrideThighDeg = 28f, JumpStrideShinDeg = 20f, JumpStrideSpeed = 3.5f;

        float _plantDrop;                          // how far the pelvis is lowered so the lower foot reaches the ground (m, eased)
        readonly float[] _plant = new float[2];   // how far each foot is moved up (m, negative = down), eased
        readonly float[] _stepShown = new float[2]; // 0..1 how much each foot is up in a turn-on-the-spot step
        readonly RaycastHit[] _groundHits = new RaycastHit[8];
        float _lastYaw, _yawRate, _turnShown, _turnPhase;
        float _crawlPhase;
        float _breathSeed = -1f;

        // Turn-on-the-spot stepping shown, 0..1; the breath of this frame (-1..1); how far the feet were moved to the ground (m).
        public float TurnStepShown => _turnShown;
        public float Breath { get; private set; }
        public float PlantDrop => _plantDrop; // how far the pelvis is lowered so the lower foot reaches a slope (m)
        public float FootPlantLeft => _plant[0];
        public float FootPlantRight => _plant[1];
        public float CrawlPhase => _crawlPhase;
        // Pose review: foot height and ground found under a foot. Formatted on demand; PlantFeet built both strings every frame.
        public string PlantDebug(int side) => $"footY {_plantFootY[side]:F3} groundDelta {_plantGroundDelta[side]:F3}";
        readonly float[] _plantFootY = new float[2], _plantGroundDelta = new float[2];
        // PlantFeet's per-foot scratch, kept: it ran every frame per close combatant and allocated four arrays each time.
        readonly float[] _plantWant = new float[2], _plantAbove = new float[2];
        readonly float?[] _plantUnder = new float?[2];
        readonly Vector3[] _plantNormal = new Vector3[2];

        // Pure: how far (0..1) a body turning at `yawRateDegPerSecond` while nearly still steps its feet.
        public static float TurnStepAmount(float yawRateDegPerSecond, float speed)
        {
            if (speed > TurnStepStillSpeed) return 0f;
            return Mathf.Clamp01(Mathf.InverseLerp(TurnStepMinDegPerSecond, TurnStepFullDegPerSecond, Mathf.Abs(yawRateDegPerSecond)));
        }

        // Pure: breath over time, -1..1, each combatant with its own phase.
        public static float BreathAt(float time, float seed) => Mathf.Sin((time + seed) * Mathf.PI * 2f / BreathSeconds);

        // Pure: the extra move a foot gets so it rests on ground `groundDelta` above where the clip has it: only
        // while it is near the ground (or standing still), within the leg's reach.
        public static float PlantMove(float footHeight, float restHeight, float groundDelta, bool still)
        {
            float want = restHeight + groundDelta;
            float height = footHeight - want;
            float weight = still ? 1f : 1f - Mathf.Clamp01(height / PlantFadeHeight);
            return Mathf.Clamp(-height, -PlantMaxLower, PlantMaxRaise) * weight;
        }

        void TrackGait()
        {
            if (_breathSeed < 0f) _breathSeed = (GetInstanceID() & 1023) * 0.0137f;
            float dt = Time.deltaTime;
            float yaw = transform.eulerAngles.y;
            if (dt > 0f)
            {
                float rate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
                _yawRate = Mathf.Lerp(_yawRate, rate, 1f - Mathf.Exp(-10f * dt));
            }
            _lastYaw = yaw;
            var v = _motion.LocalVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            bool ground = !_combatant.Dead.Value && !_motion.Airborne && _prone < 0.3f && _seated < 0.1f && _parkour < 0.1f && _swimAmount < 0.1f && LedgePose < 0.1f;
            float want = ground ? TurnStepAmount(_yawRate, speed) : 0f;
            _turnShown = Mathf.MoveTowards(_turnShown, want, 6f * dt);
            if (_turnShown > 0.01f) _turnPhase += dt * TurnStepHz * Mathf.PI * 2f;
            else _turnPhase = 0f;
            _stepShown[0] = _turnShown * Mathf.Max(0f, Mathf.Sin(_turnPhase));
            _stepShown[1] = _turnShown * Mathf.Max(0f, -Mathf.Sin(_turnPhase));
            if (_prone > 0.5f && speed > 0.05f) _crawlPhase += speed * dt / CrawlMetersPerCycle * Mathf.PI * 2f;
        }

        // LateUpdate, after the stance and jump leg turns, before the arms are placed.
        void PoseGait()
        {
            if (_combatant.Dead.Value) { Breath = 0f; return; }
            var v = _motion.LocalVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            float still = 1f - Mathf.Clamp01(speed / 1.5f);

            // Breathing: the chest rises and falls a little, the shoulders drift; weapon and hands ride the torso. Not far away (ADR-233).
            Breath = BreathAt(Time.time, _breathSeed);
            float calm = still * (1f - _seated) * (1f - _swimAmount) * (_lod >= 2 ? 0f : 1f);
            Turn(Torso, Breath * BreathPitchDeg * calm, transform.right);
            Turn(Torso, Mathf.Sin((Time.time + _breathSeed) * Mathf.PI * 2f / SwaySeconds) * SwayYawDeg * calm, transform.up);

            // Turning on the spot: each foot lifts and comes down in turn.
            if (_turnShown > 0.01f)
            {
                var legRight = Quaternion.AngleAxis(_legTurnShown, transform.up) * transform.right;
                for (int side = 0; side < 2; side++)
                {
                    float up = _stepShown[side];
                    if (up < 0.01f) continue;
                    PoseLeg(side, side == 0 ? ThighLeft : ThighRight, side == 0 ? ShinLeft : ShinRight, side == 0 ? FootLeft : FootRight,
                        -TurnThighDeg * up, TurnShinDeg * up, legRight);
                }
            }

            // A running jump: one leg forward, one back, the way a stride goes through the air.
            if (_inAir && !_jumpClips && _prone < 0.1f && _seated < 0.1f && !OnLedge) // the jump clips stride themselves (ADR-537)
            {
                float k = Mathf.Clamp01(speed / JumpStrideSpeed) * (1f - 0.5f * _tuck);
                var right = transform.right;
                PoseLeg(0, ThighLeft, ShinLeft, FootLeft, -JumpStrideThighDeg * k, JumpStrideShinDeg * k, right);
                PoseLeg(1, ThighRight, ShinRight, FootRight, JumpStrideThighDeg * 0.6f * k, JumpStrideShinDeg * 1.5f * k, right);
            }

            // Crawling: the body rolls from side to side and the knees take turns drawing up.
            if (_prone > 0.3f)
            {
                float moving = Mathf.Clamp01(speed / 0.4f) * _prone;
                float s = Mathf.Sin(_crawlPhase);
                Turn(Torso, s * CrawlRollDeg * moving, _animator.transform.up);
                PoseLeg(0, ThighLeft, ShinLeft, FootLeft, 0f, CrawlKneeDeg * Mathf.Max(0f, s) * moving, transform.right);
                PoseLeg(1, ThighRight, ShinRight, FootRight, 0f, CrawlKneeDeg * Mathf.Max(0f, -s) * moving, transform.right);
            }

            PlantFeet(speed);
        }

        // Puts each foot on the ground under it (foot IK, ADR-189, ADR-232): planted feet follow slopes and steps, both feet stand on the
        // ground when still (crouching and leaning no longer leave one in the air). Two-bone leg turns reach the foot; legs that are already
        // straight cannot reach down, so the pelvis (the whole model) is lowered by what the lower foot needs and the other foot is raised
        // by that much more. The ground under each foot is a cast per foot; the foot is tilted to the ground's slope.
        void PlantFeet(float speed)
        {
            if (_footLeft == null || _footRight == null || _lod >= 1 || _prone > 0.3f || _seated > 0.1f || _parkour > 0.1f || _swimAmount > 0.1f || _inAir || LedgePose > 0.1f)
            {
                _plant[0] = _plant[1] = 0f;
                // Leaving the ground the body eases back up (WEAPON_MOTION_AUDIT #3): put back at once it jumped up in one frame at take-off.
                float keep = _inAir && _lod == 0 && _prone <= 0.3f && _seated <= 0.1f && _swimAmount <= 0.1f && LedgePose <= 0.1f
                    ? Mathf.Lerp(_plantDrop, 0f, 1f - Mathf.Exp(-PlantSmoothPerSecond * Time.deltaTime)) : 0f;
                if (_plantDrop != keep) { _animator.transform.position += transform.up * (_plantDrop - keep); _plantDrop = keep; }
                return;
            }
            bool still = speed < 0.4f;
            var want = _plantWant;
            var above = _plantAbove;
            var under = _plantUnder;
            var normal = _plantNormal;
            // Each ankle belongs `rest` above the ground under it (a bit more on a slope). The target is measured against the capsule's own height:
            // the capsule hovers by its skin width and rests on its side on a steep slope, so the origin is not on the ground and a cast under the
            // origin would not say where the soles are. A cast that finds something far below the origin missed the floor (a layer it skips would
            // let it through): it is limited to a step, so it never sinks the body (player report "walks buried", ADR-232).
            for (int side = 0; side < 2; side++)
            {
                var foot = _bones[side == 0 ? FootLeft : FootRight];
                float footY = transform.InverseTransformPoint(foot.position).y;
                under[side] = GroundUnder(foot.position, out normal[side]);
                float groundDelta = under[side].HasValue
                    ? Mathf.Max(under[side].Value - transform.position.y, -PlantMaxBelowOrigin) + _footRestHeight * (1f / Mathf.Max(0.7f, normal[side].y) - 1f)
                    : 0f;
                _plantFootY[side] = footY; _plantGroundDelta[side] = groundDelta;
                above[side] = footY - (_footRestHeight + groundDelta); // how far this ankle is over where it belongs
                want[side] = PlantMove(footY, _footRestHeight, groundDelta, still) * (1f - _stepShown[side]);
            }
            // The pelvis goes down by the distance from the capsule's origin to the lowest ground under a foot: a planted ankle is `rest` over
            // its ground in the clips, so that is how far the body has to come down for it to stand there (the hover of the capsule on flat
            // ground, more where one foot is downhill or on a lower step). From the geometry alone: a foot in the air, or one the clip
            // lifted a little, cannot pull the body down a bit more every stride.
            float needLow = float.MaxValue, needHigh = 0f; // distance from the origin down to the nearer and the further ground under a foot
            for (int side = 0; side < 2; side++)
                if (under[side].HasValue)
                {
                    float d = Mathf.Max(0f, transform.position.y - under[side].Value);
                    needLow = Mathf.Min(needLow, d); needHigh = Mathf.Max(needHigh, d);
                }
            if (needLow == float.MaxValue) needLow = 0f;
            // The far foot may float a little rather than the body sinking into the near step: the pelvis comes down no more than PlantMaxSplit
            // below what the nearer ground needs (a leg folded for a stair cannot be folded further).
            float dropTarget = Mathf.Clamp(Mathf.Min(needHigh, needLow + PlantMaxSplit), 0f, PlantMaxLower);
            float k = 1f - Mathf.Exp(-PlantSmoothPerSecond * Time.deltaTime);
            float newDrop = Mathf.Lerp(_plantDrop, dropTarget, k);
            float extra = newDrop - _plantDrop;
            _animator.transform.position -= transform.up * extra; // feet and everything else follow at once, so the moves below add `extra`
            _plantDrop = newDrop;
            for (int side = 0; side < 2; side++)
            {
                var shin = _bones[side == 0 ? ShinLeft : ShinRight];
                var foot = _bones[side == 0 ? FootLeft : FootRight];
                if (shin == null || foot == null || shin.childCount == 0) continue;
                _plant[side] = Mathf.Lerp(_plant[side], want[side] + extra, k);
                if (Mathf.Abs(_plant[side]) >= PlantMinMove)
                {
                    var ankle = shin.GetChild(0);
                    var shinBefore = shin.rotation;
                    var footFromAnkle = foot.position - ankle.position;
                    int thighIndex = side == 0 ? ThighLeft : ThighRight, shinIndex = side == 0 ? ShinLeft : ShinRight, footIndex = side == 0 ? FootLeft : FootRight;
                    if (_bones[thighIndex] == null) continue;
                    TwoBone(_bones[thighIndex], thighIndex, shin, shinIndex, ankle, ankle.position + transform.up * _plant[side], transform.forward);
                    if (!foot.IsChildOf(shin)) // a Humanoid foot hangs under the shin and follows it by itself
                    {
                        var swing = shin.rotation * Quaternion.Inverse(shinBefore);
                        if (!_boneTurned[footIndex]) { _boneOwn[footIndex] = foot.localRotation; _boneTurned[footIndex] = true; }
                        if (!_footMoved[side]) { _footOwnPosition[side] = foot.localPosition; _footMoved[side] = true; }
                        foot.rotation = swing * foot.rotation;
                        foot.position = ankle.position + swing * footFromAnkle;
                        _footLeftAtPosition[side] = foot.localPosition;
                    }
                }
                // Sole flat on the ground's slope while standing; a stride keeps its own foot angles.
                if (still && normal[side].y > 0.5f)
                {
                    var local = transform.InverseTransformDirection(normal[side]);
                    // About the right axis a positive turn takes up (0,1,0) to (0,cos,sin): the angle that puts the sole's up on the ground's normal.
                    Turn(side == 0 ? FootLeft : FootRight, Mathf.Clamp(Mathf.Atan2(local.z, local.y) * Mathf.Rad2Deg, -PlantTiltMaxDeg, PlantTiltMaxDeg), transform.right);
                }
            }
        }

        // Height of the ground under a point (nearest hit below, not this combatant), or null; `normal` is its slope.
        float? GroundUnder(Vector3 at) => GroundUnder(at, out _);

        float? GroundUnder(Vector3 at, out Vector3 normal)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * PlantRayUp, Vector3.down, _groundHits, PlantRayLength, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            normal = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                var h = _groundHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<Combatant>() != null) continue;
                if (h.normal.y < 0.5f) continue;
                if (h.point.y > best) { best = h.point.y; normal = h.normal; }
            }
            return float.IsNegativeInfinity(best) ? (float?)null : best;
        }
    }
}

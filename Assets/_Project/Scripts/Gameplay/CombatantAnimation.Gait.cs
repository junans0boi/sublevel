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
        const float PlantRayUp = 0.5f, PlantRayLength = 1.1f, PlantMaxRaise = 0.3f, PlantMaxLower = 0.25f;
        const float PlantFadeHeight = 0.12f, PlantSmoothPerSecond = 16f, PlantMinMove = 0.004f;
        const float CrawlMetersPerCycle = 1.1f, CrawlRollDeg = 5f, CrawlKneeDeg = 25f;
        const float JumpStrideThighDeg = 28f, JumpStrideShinDeg = 20f, JumpStrideSpeed = 3.5f;

        readonly float[] _plant = new float[2];   // how far each foot is moved up (m, negative = down), eased
        readonly float[] _stepShown = new float[2]; // 0..1 how much each foot is up in a turn-on-the-spot step
        readonly RaycastHit[] _groundHits = new RaycastHit[8];
        float _lastYaw, _yawRate, _turnShown, _turnPhase;
        float _crawlPhase;
        float _breathSeed = -1f;

        // Turn-on-the-spot stepping shown, 0..1; the breath of this frame (-1..1); how far the feet were moved to the ground (m).
        public float TurnStepShown => _turnShown;
        public float Breath { get; private set; }
        public float FootPlantLeft => _plant[0];
        public float FootPlantRight => _plant[1];
        public float CrawlPhase => _crawlPhase;
        public readonly string[] PlantDebug = new string[2]; // pose review: foot height and ground found under each foot

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

            // Breathing: the chest rises and falls a little, the shoulders drift; weapon and hands ride the torso.
            Breath = BreathAt(Time.time, _breathSeed);
            float calm = still * (1f - _seated) * (1f - _swimAmount);
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
            if (_inAir && _prone < 0.1f && _seated < 0.1f && !OnLedge)
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

        // Puts each foot on the ground under it: planted feet follow slopes and steps, and both feet stand on the ground
        // when still (crouching and leaning no longer leave one in the air). Two-bone leg turn, the foot bone follows.
        void PlantFeet(float speed)
        {
            if (_footLeft == null || _footRight == null || _prone > 0.3f || _seated > 0.1f || _parkour > 0.1f || _swimAmount > 0.1f || _inAir || LedgePose > 0.1f)
            {
                _plant[0] = _plant[1] = 0f;
                return;
            }
            bool still = speed < 0.4f;
            for (int side = 0; side < 2; side++)
            {
                var shin = _bones[side == 0 ? ShinLeft : ShinRight];
                var foot = _bones[side == 0 ? FootLeft : FootRight];
                if (shin == null || foot == null || shin.childCount == 0) continue;
                float footY = transform.InverseTransformPoint(foot.position).y;
                // Relative to the ground under the character's own origin, which is where the clip's rest height was measured.
                float groundDelta = GroundUnder(foot.position) is float g && GroundUnder(transform.position) is float g0 ? g - g0 : 0f;
                PlantDebug[side] = $"footY {footY:F3} groundDelta {groundDelta:F3}";
                float move = PlantMove(footY, _footRestHeight, groundDelta, still) * (1f - _stepShown[side]);
                _plant[side] = Mathf.Lerp(_plant[side], move, 1f - Mathf.Exp(-PlantSmoothPerSecond * Time.deltaTime));
                if (Mathf.Abs(_plant[side]) < PlantMinMove) continue;
                var ankle = shin.GetChild(0);
                var shinBefore = shin.rotation;
                var footFromAnkle = foot.position - ankle.position;
                int thighIndex = side == 0 ? ThighLeft : ThighRight, shinIndex = side == 0 ? ShinLeft : ShinRight, footIndex = side == 0 ? FootLeft : FootRight;
                if (_bones[thighIndex] == null) continue;
                TwoBone(_bones[thighIndex], thighIndex, shin, shinIndex, ankle, ankle.position + transform.up * _plant[side], transform.forward);
                if (foot.IsChildOf(shin)) continue; // a Humanoid foot hangs under the shin and follows it by itself
                var swing = shin.rotation * Quaternion.Inverse(shinBefore);
                if (!_boneTurned[footIndex]) { _boneOwn[footIndex] = foot.localRotation; _boneTurned[footIndex] = true; }
                if (!_footMoved[side]) { _footOwnPosition[side] = foot.localPosition; _footMoved[side] = true; }
                foot.rotation = swing * foot.rotation;
                foot.position = ankle.position + swing * footFromAnkle;
                _footLeftAtPosition[side] = foot.localPosition;
            }
        }

        // Height of the ground under a point (nearest hit below, not this combatant), or null.
        float? GroundUnder(Vector3 at)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * PlantRayUp, Vector3.down, _groundHits, PlantRayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _groundHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<Combatant>() != null) continue;
                if (h.normal.y < 0.5f) continue;
                best = Mathf.Max(best, h.point.y);
            }
            return float.IsNegativeInfinity(best) ? (float?)null : best;
        }
    }
}

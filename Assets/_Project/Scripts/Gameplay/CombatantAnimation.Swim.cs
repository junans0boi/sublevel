using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Swimming (ADR-112, #184/#147), no clip: the body lies as in the crawl (the stance is replicated as prone) but the
    // legs flutter-kick and the arms crawl-stroke over the water, rate tied to the speed; treading water when still.
    // Diving drops the head. Every peer reads the water from the body's position (CombatantSwim), so nothing is sent.
    // The weapon is put away by the server while swimming (CombatantSwim), so the hands are empty.
    public partial class CombatantAnimation
    {
        CombatantSwim _swimBody;
        float _swimAmount, _diveAmount, _effort, _strokePhase, _kickPhase;

        // 0..1 how much of the swim pose is shown (tests, captures).
        public float SwimPose => _swimAmount;
        public float DivePose => _diveAmount;
        public float StrokePhase => _strokePhase;

        void TrackSwim(bool dead)
        {
            if (_swimBody == null) _swimBody = GetComponent<CombatantSwim>();
            var rules = SwimRules.Current;
            float dt = Time.deltaTime;
            bool swimming = !dead && _swimBody != null && _swimBody.InWater;
            _swimAmount = Mathf.MoveTowards(_swimAmount, swimming ? 1f : 0f, rules.PoseBlendPerSecond * dt);
            _diveAmount = Mathf.MoveTowards(_diveAmount, swimming && _swimBody.HeadUnder ? 1f : 0f, rules.PoseBlendPerSecond * dt);
            if (_swimAmount <= 0f) { _effort = 0f; return; }
            var v = _motion.LocalVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            _effort = Mathf.MoveTowards(_effort, Swim.Effort(speed, GameData.Rules.Locomotion.SprintSpeed, rules), 3f * dt);
            _strokePhase += Swim.StrokeHz(_effort, rules) * dt;
            _kickPhase += Swim.KickHz(_effort, rules) * dt;
            _strokePhase -= Mathf.Floor(_strokePhase);
            _kickPhase -= Mathf.Floor(_kickPhase);
        }

        // LateUpdate, after the animator and the crawl's pose: straight legs kicking, arms stroking.
        void PoseSwim()
        {
            if (_swimAmount <= 0.001f) return;
            var rules = SwimRules.Current;
            float s = _swimAmount;
            // Head down a little while diving (the crawl lifts it by ProneHeadLiftDeg; a positive turn tips it forward).
            // Level in the water: most of the crawl's chest lift comes off (the head stays a little up), then the dive's dip.
            Turn(Torso, (rules.LevelDeg + rules.DivePitchDeg * _diveAmount) * s, transform.right);
            // A little roll with each stroke.
            Turn(Torso, 10f * Mathf.Sin(_strokePhase * 2f * Mathf.PI) * _effort * s, transform.forward);
            // Legs: the idle pose's straight legs kick; the shin follows its thigh a little late.
            float kickScale = Mathf.Lerp(0.45f, 1f, _effort) * s;
            for (int side = 0; side < 2; side++)
            {
                float thigh = Swim.KickThigh(_kickPhase, side, rules) * kickScale;
                float lag = Swim.KickThigh(_kickPhase - 0.15f, side, rules) * kickScale;
                PoseLeg(side, side == 0 ? ThighLeft : ThighRight, side == 0 ? ShinLeft : ShinRight, side == 0 ? FootLeft : FootRight,
                    thigh, 0.5f * (lag - thigh) + 8f * s, transform.right);
            }
            // Arms: over the top and back under; treading, held forward and sculling. A little out from the body.
            for (int side = 0; side < 2; side++)
            {
                float stroke = Swim.ArmAngle(_strokePhase, side);
                float tread = Swim.ArmIdleAngle(_strokePhase * 2f, side);
                float angle = Mathf.LerpAngle(tread, stroke, Mathf.Clamp01(_effort * 2f));
                int arm = side == 0 ? ArmLeft : ArmRight;
                Turn(arm, angle * s, transform.right);
                Turn(arm, (side == 0 ? -12f : 12f) * s, transform.forward);
            }
        }
    }
}

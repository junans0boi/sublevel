using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Swimming (ADR-112, #184/#147; clips since #236): the UAL Swim_Idle/Fwd clips (Swim state of the Base layer, speed-blended) lie flat
    // and stroke; the stance is replicated as prone, but the model is not pitched. The dive still dips the head in code.
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
            // Legs and arms are the UAL swim clips (Swim state, #236); only the dive's head dip is still turned here.
            Turn(Torso, rules.DivePitchDeg * _diveAmount * s, transform.right);
        }
    }
}

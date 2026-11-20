using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The bungee jumper's body (ADR-498, #419): head first over the edge, head-down with the arms spread through the fall, the bounce and
    // the hang, upright again for the last metres onto the pad (BungeePose). Every peer reads it from the body's place over the pad, so
    // nothing is sent. The whole model turns over at the hips; the cord (BungeeCordView) is tied to where the ankles end up.
    public partial class CombatantAnimation
    {
        const float BungeeArmSpreadDeg = 75f;

        float _bungee;   // 0 = upright, 1 = head-down
        static Transform _island;

        public float BungeeFlip => _bungee;

        // The feet's height above the landing pad, when this body is on the bungee cord (on the island, over the pad).
        public static bool OnBungeeRope(Vector3 feet, out float height)
        {
            height = 0f;
            if (!WaitingHud.OnIsland) return false;
            if (_island == null) { var go = GameObject.Find("WaitingIsland"); if (go == null) return false; _island = go.transform; }
            var pad = WaitingIsland.BungeePad;
            height = feet.y - _island.position.y;
            return BungeePose.OnRope(feet.x - WaitingIsland.WorldX(pad.x), feet.z - WaitingIsland.WorldZ(pad.z), height);
        }

        void TrackBungee(bool dead)
        {
            _bungee = !dead && OnBungeeRope(transform.position, out float h) ? BungeePose.Flip(h) : 0f;
        }

        // Where the cord is tied: between the two ankle (foot) bones as the model is posed this frame; false without a rigged model.
        public bool TryAnkles(out Vector3 at)
        {
            at = default;
            if (_footLeft == null || _footRight == null) return false;
            at = (_footLeft.position + _footRight.position) * 0.5f;
            return true;
        }

        // After the model's own turn and place are set for this frame: turn it over about the hips (forward = head first).
        void TurnOverForBungee()
        {
            if (_bungee <= 0.001f) return;
            var model = _animator.transform;
            var turn = Quaternion.AngleAxis(180f * _bungee, Vector3.right);
            var pivot = Vector3.up * BungeePose.PivotHeight;
            model.localPosition = pivot + turn * (model.localPosition - pivot);
            model.localRotation = turn * model.localRotation;
        }

        // Arms spread wide (about the model's own forward axis a positive turn swings a hanging arm to its right).
        void PoseBungee()
        {
            if (_bungee <= 0.001f) return;
            var forward = _animator.transform.forward;
            float spread = BungeeArmSpreadDeg * Mathf.Clamp01(_bungee * 2f) * (1f - Mathf.Max(_upperWeight, _rifle));
            Turn(ArmLeft, -spread, forward);
            Turn(ArmRight, spread, forward);
        }
    }
}

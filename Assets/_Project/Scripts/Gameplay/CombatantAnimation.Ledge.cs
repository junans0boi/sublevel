using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Hanging from a high edge and pulling up onto it (#115, ADR-087), no clip. Hanging: both palms on the edge (the
    // two-bone reach and grip of the seated pose, CombatantAnimation.Vehicle.cs), the legs hanging with the knees a
    // little bent. The pull-up keeps the hands where they hold while the body rises past them (the elbows bend, then
    // push down), the torso tips forward over the top and a knee comes up onto it; the hands let go as the feet
    // arrive. It follows the replicated parkour state and the body's place, so every side shows the same.
    public partial class CombatantAnimation
    {
        const float LedgeHandsApart = 0.2f;    // each palm this far from the middle, along the edge
        const float LedgeHandsOnTop = 0.05f;   // and this far onto the top
        const float HangThighDeg = 10f, HangShinDeg = 20f;
        const float ClimbKneeDeg = 80f, ClimbShinDeg = 70f, ClimbTorsoDeg = 30f;

        float _ledge;               // 0..1: how much of the ledge pose is shown
        ParkourMove _ledgeMove;
        Vector3 _ledgeEdge;         // the middle of the held edge
        float _ledgeClimbAt;
        CharacterController _ledgeBody;

        bool OnLedge => _stance != null && !_combatant.Dead.Value && Game.Core.Parkour.IsLedge(_stance.ParkourMove);
        // How much of the hang / pull-up pose is shown, 0..1.
        public float LedgePose => _ledge;

        // Hanging, the body stays still under the edge: where the hands hold follows it (a remote body may still be
        // arriving). From the start of the pull-up the hold stays put while the body rises.
        Vector3 EdgeOverBody(ParkourRules r)
        {
            if (_ledgeBody == null) _ledgeBody = GetComponent<CharacterController>();
            float radius = _ledgeBody != null ? _ledgeBody.radius : 0.35f;
            return transform.position + Vector3.up * r.HangDrop + transform.forward * (radius + r.HangGap);
        }

        void PoseLedge()
        {
            var r = ParkourRules.Current;
            var move = OnLedge ? _stance.ParkourMove : ParkourMove.None;
            if (move == ParkourMove.Hang) _ledgeEdge = EdgeOverBody(r);
            else if (move == ParkourMove.LedgeClimb && _ledgeMove != ParkourMove.LedgeClimb)
            {
                _ledgeClimbAt = Time.time;
                if (_ledgeMove != ParkourMove.Hang) _ledgeEdge = EdgeOverBody(r);
            }
            if (move != ParkourMove.None || _ledge <= 0f) _ledgeMove = move;
            _ledge = Mathf.MoveTowards(_ledge, move != ParkourMove.None ? 1f : 0f, r.PosePerSecond * Time.deltaTime);
            if (_ledge <= 0.001f) return;

            float w = _ledge * _ledge * (3f - 2f * _ledge);
            // Fading out after the pull-up the pose stays at its end (t = 1); after letting go, at the hang.
            float t = _ledgeMove == ParkourMove.LedgeClimb ? Mathf.Clamp01((Time.time - _ledgeClimbAt) / Mathf.Max(0.05f, r.LedgeClimbSeconds)) : 0f;
            float knee = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.25f) / 0.6f)); // up onto the edge, then straight to stand
            float over = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.15f) / 0.75f));
            float letGo = Mathf.Clamp01((t - 0.7f) / 0.25f);
            float hands = w * (1f - letGo * letGo * (3f - 2f * letGo));

            // Same turn directions as the seated legs: a negative turn about the right axis lifts a thigh forward, the
            // shin turns back by the thigh's angle plus its own. The left knee comes up onto the top, the right less.
            float thighL = HangThighDeg + ClimbKneeDeg * knee, shinL = HangShinDeg + ClimbShinDeg * knee;
            float thighR = HangThighDeg + ClimbKneeDeg * 0.4f * knee, shinR = HangShinDeg + ClimbShinDeg * 0.4f * knee;
            // The hips come down by what the lower (right) leg's bend takes off it, so that foot stays on the path
            // instead of the folded legs hanging over the top (the model is placed again every Update).
            _animator.transform.position += Vector3.down * Stances.CrouchHipDrop(_legLength, thighR * w, shinR * w);
            Turn(Torso, ClimbTorsoDeg * over * w, transform.right);
            PoseLeg(0, ThighLeft, ShinLeft, FootLeft, -thighL * w, (thighL + shinL) * w, transform.right);
            PoseLeg(1, ThighRight, ShinRight, FootRight, -thighR * w, (thighR + shinR) * w, transform.right);

            if (hands <= 0.001f) return;
            if (!_gripBonesLooked) FindGripBones();
            for (int side = 0; side < 2; side++)
            {
                var hold = _ledgeEdge + transform.right * (side == 0 ? -LedgeHandsApart : LedgeHandsApart) + transform.forward * LedgeHandsOnTop;
                // Palms towards the wall, the fingers curled over the edge.
                Grip(side, side == 0 ? ArmLeft : ArmRight, hold, transform.right, transform.forward, GripCurlDeg, hands);
            }
        }
    }
}

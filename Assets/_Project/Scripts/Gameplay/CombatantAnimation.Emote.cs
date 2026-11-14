using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Emotes (ADR-131): the pose of Emotes.PoseAt turned onto the arms, torso and knees after the animator, on top of
    // whatever the base layer plays. Only for empty hands on foot and standing; a weapon pose, lying, a seat or a
    // vault wins. Each side starts the clock when it first sees the emote id change, so the pose is the same
    // everywhere to within the network delay.
    public partial class CombatantAnimation
    {
        byte _emoteSeen;
        float _emoteStartedAt;

        // How far into its emote this body is (seconds), for tests and captures; negative when none plays.
        public float EmoteTime => _emoteSeen != 0 ? Time.time - _emoteStartedAt : -1f;

        void PoseEmote()
        {
            byte id = _combatant.EmoteId.Value;
            if (id != _emoteSeen)
            {
                _emoteSeen = id;
                _emoteStartedAt = Time.time;
            }
            if (id == 0 || _combatant.Dead.Value || _prone > 0.3f || _seated > 0.01f || _parkour > 0.01f || _upperWeight > 0.3f || _crouch > 0.3f) return;
            var pose = Emotes.PoseAt((Emote)id, Time.time - _emoteStartedAt);
            // About the forward axis a positive turn swings a hanging arm out to the right; about the right axis a
            // negative turn swings it forward.
            Turn(ArmRight, pose.RightOut, transform.forward);
            Turn(ArmLeft, -pose.LeftOut, transform.forward);
            Turn(ArmRight, -pose.RightFwd, transform.right);
            Turn(ArmLeft, -pose.LeftFwd, transform.right);
            Turn(Torso, pose.TorsoPitch, transform.right);
            Turn(Torso, -pose.TorsoRoll, transform.forward);
            Turn(Torso, pose.TorsoYaw, transform.up);
            if (pose.Bend > 0.01f)
            {
                var legRight = transform.right;
                PoseLeg(0, ThighLeft, ShinLeft, FootLeft, -pose.Bend, pose.Bend * 2f, legRight);
                PoseLeg(1, ThighRight, ShinRight, FootRight, -pose.Bend, pose.Bend * 2f, legRight);
                Turn(FootLeft, -pose.Bend, legRight);
                Turn(FootRight, -pose.Bend, legRight);
            }
        }
    }
}

using UnityEngine;

namespace Game.Tests
{
    // Humanoid bones of a spawned combatant (ADR-230): tests never look bones up by name.
    public static class RigBones
    {
        public static Transform Bone(Component combatant, HumanBodyBones bone) =>
            combatant.GetComponentInChildren<Animator>().GetBoneTransform(bone);
    }
}

using System;
using UnityEngine;

namespace Game.Gameplay
{
    // Roster character data and bone/socket references (docs/research/16_s3_s4_interface.md §2, §4).
    // S4 reads these; nothing looks bones up by name at runtime. Built by Editor/CharacterRoster.
    [Serializable]
    public struct HumanoidSockets
    {
        public Transform Hips, Spine, Chest, UpperChest, Neck, Head;
        public Transform LeftUpperArm, LeftLowerArm, LeftHand, RightUpperArm, RightLowerArm, RightHand;
        public Transform LeftUpperLeg, LeftLowerLeg, LeftFoot, RightUpperLeg, RightLowerLeg, RightFoot;
        public Transform WeaponSocket, HeadSocket, BackSocket, EyePoint;
    }

    [Serializable]
    public class CharacterVariant
    {
        public string Name;
        public Material[] Materials; // one per renderer material slot, in Renderers order
    }

    public class CharacterDef : MonoBehaviour
    {
        public string Id;
        public float Height = 1.8f, EyeHeight = 1.6f, HandSizeScale = 1f;
        public Vector3 WeaponSocketOffsetPos, WeaponSocketOffsetEuler;
        public float[] LodDistances = { 0f, 12f, 30f, 60f }; // metres where Mesh LOD 0..3 are intended (documentation; selection is Unity's)
        public bool HasHelmetMask;
        public int StartLod; // first Mesh LOD used (LOD0 above 15k triangles is skipped)
        public HumanoidSockets Sockets;
        public Animator Animator;
        public Renderer[] Renderers;
        public CharacterVariant[] Variants;

        public void ApplyVariant(int index)
        {
            var v = Variants[index % Variants.Length];
            int i = 0;
            foreach (var r in Renderers)
            {
                var n = r.sharedMaterials.Length;
                var mats = new Material[n];
                for (int s = 0; s < n; s++) mats[s] = v.Materials[i++];
                r.sharedMaterials = mats;
            }
        }
    }
}

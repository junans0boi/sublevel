using System.Collections.Generic;
using Game.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // ADR-395 (#196): every combatant prefab of the 15-model roster carries a baked ragdoll (TestBoxBuilder.Build).
    public class RagdollRigTests
    {
        static readonly string[] Models =
        {
            "MixamoSwat", "MixamoBrian", "MixamoLouise", "MixamoJames", "MixamoMegan", "MixamoKate", "MixamoJosh", "MixamoAdam",
            "MixamoMartha", "MixamoAlex", "MixamoSteve", "MixamoSwatGuy", "MixamoGasMask", "UbcMale", "UbcFemale",
        };

        [Test]
        public void EveryRosterPrefab_HasASaneRagdoll()
        {
            foreach (var kind in new[] { "Player_", "Bot_" })
                foreach (var id in Models)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/M1/{kind}{id}.prefab");
                    Assert.IsNotNull(prefab, kind + id + " (run TestBoxBuilder.Build)");
                    var rig = prefab.GetComponentInChildren<CombatantRagdoll>(true);
                    Assert.IsNotNull(rig, kind + id + ": no ragdoll");
                    Assert.IsTrue(rig.HasRig, kind + id);
                    Assert.AreEqual(11, rig.PartCount, kind + id);
                    var bones = new HashSet<Transform>();
                    float mass = 0f;
                    for (int i = 0; i < rig.PartCount; i++)
                    {
                        var p = rig.Part(i);
                        Assert.IsNotNull(p.Bone, $"{kind}{id} part {i}");
                        Assert.IsTrue(bones.Add(p.Bone), $"{kind}{id} part {i}: bone used twice");
                        Assert.IsTrue(i == 0 ? p.Parent == -1 : p.Parent >= 0 && p.Parent < i, $"{kind}{id} part {i}: parent {p.Parent}");
                        if (i > 0) Assert.IsTrue(p.Bone.IsChildOf(rig.Part(p.Parent).Bone), $"{kind}{id} part {i} hangs under its parent");
                        Assert.Less(p.LowTwist, p.HighTwist + 1e-3f, $"{kind}{id} part {i}: twist range");
                        // Sizes in metres (the bone's scale applied): no zero or giant shapes.
                        float s = Mathf.Abs(p.Bone.lossyScale.x);
                        float r = p.Size.x * s;
                        Assert.That(r, Is.InRange(0.03f, 0.6f), $"{kind}{id} part {i}: size {r}");
                        mass += p.Mass;
                    }
                    Assert.That(mass, Is.InRange(60f, 80f), kind + id + " total mass");
                }
        }
    }
}

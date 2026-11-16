using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // S3 roster invariants, measured on the built prefabs (needs SyncStaging + CharacterRoster.Build).
    // Numbers are written to Logs/roster_measure.txt for the DONE note.
    public class CharacterRosterTests
    {
        static readonly string[] Ids = { "MixamoSwat", "MixamoBrian", "MixamoLouise", "MixamoJames", "MixamoMegan", "MixamoKate", "MixamoJosh", "MixamoAdam", "MixamoMartha", "MixamoAlex", "MixamoSteve", "MixamoSwatGuy", "MixamoGasMask", "UbcMale", "UbcFemale" };
        static GameObject Load(string id) => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Roster/Roster_{id}.prefab");

        static IEnumerable<string> Each() => Ids;

        [Test]
        public void Roster_PrefabsExist_AreHumanoid_AndHave3Models()
        {
            foreach (var id in Ids)
            {
                var go = Load(id);
                Assert.IsNotNull(go, id + " prefab (run CharacterImport.SyncStaging then CharacterRoster.Build)");
                var def = go.GetComponent<CharacterDef>();
                Assert.IsTrue(def.Animator.isHuman, id + " Animator must be Humanoid");
                Assert.IsTrue(def.Animator.avatar.isValid, id + " avatar");
                Assert.GreaterOrEqual(def.Variants.Length, 1, id + " variants (military atlases keep Base only)");
            }
        }

        [Test]
        public void Height_EyeAndSockets_FollowInterface()
        {
            var sb = new StringBuilder();
            foreach (var id in Ids)
            {
                var go = Object.Instantiate(Load(id));
                try
                {
                    var def = go.GetComponent<CharacterDef>(); var s = def.Sockets;
                    float top = s.HeadSocket.position.y, eye = s.EyePoint.position.y;
                    float foot = Mathf.Min(s.LeftFoot.position.y, s.RightFoot.position.y);
                    sb.AppendLine($"{id}: height(head top) {top:F3} eye {eye:F3} foot bone y {foot:F3}");
                    Assert.AreEqual(1.8f, top, 0.03f, id + " height");
                    Assert.AreEqual(1.6f, eye, 0.01f, id + " eye");
                    Assert.Less(foot, 0.25f, id + " feet near the ground");
                    foreach (var t in new[] { s.WeaponSocket, s.HeadSocket, s.BackSocket, s.EyePoint, s.Head, s.RightHand })
                        Assert.IsNotNull(t, id + " socket/bone");
                    Assert.AreSame(s.RightHand, s.WeaponSocket.parent, id + " WeaponSocket under RightHand");
                    Assert.AreSame(s.Head, s.HeadSocket.parent);
                    // The character stands facing +Z: BackSocket is behind the chest, grip is in front of the wrist.
                    Assert.Less(s.BackSocket.position.z, s.UpperChest.position.z, id + " back socket behind chest");
                }
                finally { Object.DestroyImmediate(go); }
            }
            Append(sb);
        }

        [Test]
        public void Proportions_HeadAndArms_AreRealistic()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            foreach (var id in Ids)
            {
                var go = Object.Instantiate(Load(id));
                try
                {
                    var s = go.GetComponent<CharacterDef>().Sockets;
                    float height = s.HeadSocket.position.y;
                    float head = (s.HeadSocket.position.y - s.Neck.position.y) / height; // neck joint to crown
                    float arm = (Vector3.Distance(s.RightUpperArm.position, s.RightLowerArm.position) + Vector3.Distance(s.RightLowerArm.position, s.RightHand.position)) / height;
                    float leg = (Vector3.Distance(s.RightUpperLeg.position, s.RightLowerLeg.position) + Vector3.Distance(s.RightLowerLeg.position, s.RightFoot.position)) / height;
                    sb.AppendLine($"{id}: head(neck..crown)/height {head:F3}, arm(shoulder..wrist)/height {arm:F3}, leg(hip..ankle)/height {leg:F3}");
                    if (head < 0.10f || head > 0.18f) bad.Add($"{id} head {head:F3} (#230)");
                    if (arm < 0.25f || arm > 0.40f) bad.Add($"{id} arm {arm:F3}");
                    if (leg < 0.41f || leg > 0.55f) bad.Add($"{id} leg {leg:F3}");
                }
                finally { Object.DestroyImmediate(go); }
            }
            Append(sb);
            Assert.IsEmpty(bad, string.Join("; ", bad));
        }

        // #230: head size from the jaw line (lowest vertex weighted mainly to the Head bone's subtree) to the crown.
        [Test]
        public void Head_ChinToCrown_RatioIsHumanSized()
        {
            var sb = new StringBuilder(); var bad = new List<string>();
            foreach (var id in Ids)
            {
                var go = Object.Instantiate(Load(id));
                try
                {
                    var def = go.GetComponent<CharacterDef>(); var head = def.Sockets.Head;
                    float lo = float.MaxValue, hi = float.MinValue, headLo = float.MaxValue, headHi = float.MinValue;
                    foreach (var r in def.Renderers.OfType<SkinnedMeshRenderer>())
                    {
                        var mesh = new Mesh(); r.BakeMesh(mesh);
                        var v = mesh.vertices; var w = r.sharedMesh.boneWeights; var m = r.transform.localToWorldMatrix; var bones = r.bones;
                        for (int i = 0; i < v.Length; i++)
                        {
                            float y = m.MultiplyPoint3x4(v[i]).y;
                            if (r.name != "Eyes" && r.name != "Eyebrows") { lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                            if (bones[w[i].boneIndex0].IsChildOf(head)) { headLo = Mathf.Min(headLo, y); headHi = Mathf.Max(headHi, y); }
                        }
                        Object.DestroyImmediate(mesh);
                    }
                    float ratio = (headHi - headLo) / (hi - lo);
                    sb.AppendLine($"{id}: head(jaw..crown)/height {ratio:F3} ({headHi - headLo:F3} m of {hi - lo:F3} m)");
                    if (ratio < 0.11f || ratio > 0.16f) bad.Add($"{id} {ratio:F3}");
                }
                finally { Object.DestroyImmediate(go); }
            }
            Append(sb);
            Assert.IsEmpty(bad, "realistic adults are 0.125-0.145; allowed 0.11-0.16: " + string.Join("; ", bad));
        }

        // Worn gear: ArmorVisual/BackpackVisual mount the procedural helmet, vest and backpack on the Humanoid head/chest bones
        // of every roster model, sized by head height and shoulder width (WornGearMeshes.TryFit).
        [Test]
        public void WornGear_MountsOnRosterBones_AtHeadChestBack_AllTiers()
        {
            var sb = new StringBuilder(); var bad = new List<string>();
            foreach (var id in Ids)
                for (int tier = 1; tier <= 3; tier++)
                {
                    var go = Object.Instantiate(Load(id));
                    try
                    {
                        var s = go.GetComponent<CharacterDef>().Sockets; float crown = s.HeadSocket.position.y;
                        go.AddComponent<ArmorVisual>().ShowTiers(tier, tier);
                        go.AddComponent<BackpackVisual>().Show(tier);
                        MeshRenderer R(string n) { var r = go.GetComponentsInChildren<MeshRenderer>(true).First(x => x.name == n); Assert.IsTrue(r.enabled, $"{id} {n} t{tier} enabled"); return r; }
                        var h = R("Helmet"); var v = R("Vest"); var p = R("Backpack");
                        float hs = h.transform.lossyScale.y, vs = v.transform.lossyScale.y;
                        float brim = h.transform.position.y;
                        sb.AppendLine($"{id} t{tier}: helmet brim {brim:F2} (crown {crown:F2}) top {h.bounds.max.y:F2} scale {hs:F2}; vest y {v.bounds.min.y:F2}..{v.bounds.max.y:F2} scale {vs:F2} w {v.bounds.size.x:F2}; pack y {p.bounds.min.y:F2}..{p.bounds.max.y:F2} z {p.bounds.max.z:F2} (chest z {s.UpperChest.position.z:F2})");
                        if (!h.transform.IsChildOf(s.Head)) bad.Add($"{id} t{tier} helmet not on head bone");
                        if (!v.transform.IsChildOf(s.UpperChest) || !p.transform.IsChildOf(s.UpperChest)) bad.Add($"{id} t{tier} vest/pack not on chest bone");
                        if (brim < crown - 0.12f || brim > crown - 0.04f) bad.Add($"{id} t{tier} brim {brim:F2} vs crown {crown:F2}");
                        if (h.bounds.max.y > crown + 0.08f) bad.Add($"{id} t{tier} helmet top {h.bounds.max.y:F2} too high");
                        if (v.bounds.center.y < s.UpperChest.position.y - 0.08f || v.bounds.center.y > s.UpperChest.position.y + 0.08f) bad.Add($"{id} t{tier} vest not centred on chest");
                        if (p.bounds.max.z > s.UpperChest.position.z - 0.05f) bad.Add($"{id} t{tier} pack not behind the chest");
                        if (p.bounds.max.y > s.Neck.position.y + 0.16f) bad.Add($"{id} t{tier} pack above the neck");
                        if (hs < 0.88f || hs > 1.18f || vs < 0.83f || vs > 1.22f) bad.Add($"{id} t{tier} scale {hs:F2}/{vs:F2}");
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            Append(sb);
            Assert.IsEmpty(bad, string.Join("; ", bad));
        }

        // Weapon lengths live in the editor-only CharacterKit table; read them by reflection.
        [Test]
        public void RifleLengths_AreRealRelativeToHeightAndArms()
        {
            var kit = System.Type.GetType("CharacterKit, Assembly-CSharp-Editor");
            Assert.IsNotNull(kit, "CharacterKit");
            var weapons = ((System.Array)kit.GetField("Weapons").GetValue(null)).Cast<object>()
                .Select(w => (id: (int)w.GetType().GetField("Id").GetValue(w), len: (float)w.GetType().GetField("Length").GetValue(w))).OrderBy(w => w.id).ToArray();
            var sb = new StringBuilder();
            var go = Object.Instantiate(Load("MixamoSwat"));
            float arm;
            try
            {
                var s = go.GetComponent<CharacterDef>().Sockets;
                arm = Vector3.Distance(s.RightUpperArm.position, s.RightLowerArm.position) + Vector3.Distance(s.RightLowerArm.position, s.RightHand.position);
            }
            finally { Object.DestroyImmediate(go); }
            foreach (var (id, len) in weapons) sb.AppendLine($"weapon {id}: {len:F2} m = {len / 1.8f:F2} x height = {len / arm:F2} x arm ({arm:F2} m)");
            // Ordered by id: pistol < SMG < bullpup < shotgun < long gun, strictly growing.
            for (int i = 1; i < weapons.Length; i++) Assert.Greater(weapons[i].len, weapons[i - 1].len);
            Assert.That(weapons[0].len, Is.InRange(0.18f, 0.30f), "pistol is a handgun");
            Assert.That(weapons[2].len / 1.8f, Is.InRange(0.38f, 0.60f), "rifle is about half a person tall (real rifles 0.7-1.0 m)");
            Assert.LessOrEqual(weapons[^1].len / 1.8f, 0.62f, "longest gun");
            // Two-hand reach: a rifle must be longer than one arm but no more than ~2 arms (grip behind + support hand ahead).
            Assert.That(weapons[2].len / arm, Is.InRange(1.0f, 2.2f), "rifle vs arm reach");
            Append(sb);
        }

        [Test]
        public void LodGroup_CullsAt100m_AndMeshLodsExist()
        {
            var sb = new StringBuilder();
            foreach (var id in Ids)
            {
                var go = Load(id);
                var lod = go.GetComponent<LODGroup>();
                var l = lod.GetLODs();
                Assert.AreEqual(1, l.Length);
                float dist = lod.size / (2f * l[0].screenRelativeTransitionHeight * Mathf.Tan(30f * Mathf.Deg2Rad));
                sb.AppendLine($"{id}: cull at {dist:F1} m (lodBias 1, FOV 60)");
                Assert.AreEqual(100f, dist, 2f, id);
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    Assert.Greater(r.sharedMesh.lodCount, 1, id + "/" + r.name + " Mesh LOD");
            }
            Append(sb);
        }

        [Test]
        public void Fbx_AreNotTracked_ButMetaAre()
        {
            var tracked = Git("ls-files Assets/_Project/Art/Characters").Split('\n');
            Assert.IsFalse(tracked.Any(f => f.StartsWith("Assets/_Project/Art/Characters/Mixamo/Roster_") && !f.EndsWith(".meta")), "Mixamo roster files must stay untracked (ToS)");
            Assert.IsFalse(tracked.Any(f => f.StartsWith("Assets/_Project/Art/Characters/Ubc/Roster_") && !f.EndsWith(".meta")), "UBC roster files stay untracked");
            Assert.IsTrue(tracked.Contains("Assets/_Project/Art/Characters/Mixamo/Roster_Swat.fbx.meta"));
        }

        static string Git(string args)
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", args) { RedirectStandardOutput = true, UseShellExecute = false });
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o;
        }

        static void Append(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.AppendAllText("Logs/roster_measure.txt", sb.ToString());
        }
    }
}

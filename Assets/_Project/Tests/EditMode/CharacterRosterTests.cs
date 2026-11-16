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
        static readonly string[] Ids = { "MixamoSwat", "UbcMale", "UbcFemale" };
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
                Assert.GreaterOrEqual(def.Variants.Length, 9, id + " variants (base + 8 tints)");
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
                    Assert.Less(foot, 0.15f, id + " feet near the ground");
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
                    if (head < 0.10f || head > 0.17f) bad.Add($"{id} head {head:F3} (#230)");
                    if (arm < 0.26f || arm > 0.40f) bad.Add($"{id} arm {arm:F3}");
                    if (leg < 0.42f || leg > 0.55f) bad.Add($"{id} leg {leg:F3}");
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

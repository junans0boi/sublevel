using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// S3 (#255): measures a staged Mixamo character FBX as a Humanoid without leaving it in the project.
// The FBX is copied to a throwaway folder, imported, measured, then the folder is deleted (nothing to commit).
// Usage: tools/unity.sh run CharacterProbe.Run
public static class CharacterProbe
{
    const string Source = "AssetStaging/mixamo/Swat_Mixamo_Tpose.fbx";
    const string TempDir = "Assets/_S3Probe";
    const string Report = "Logs/s3_probe.txt";

    public static void Run()
    {
        var sb = new StringBuilder();
        try { Measure(sb); }
        catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        finally
        {
            AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.Refresh();
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, sb.ToString());
        Debug.Log(sb.ToString());
    }

    static void Measure(StringBuilder sb)
    {
        if (!File.Exists(Source)) { sb.AppendLine("missing " + Source); return; }
        AssetDatabase.DeleteAsset(TempDir);
        Directory.CreateDirectory(TempDir);
        var dest = TempDir + "/Probe.fbx";
        File.Copy(Source, dest, true);
        AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);

        var importer = (ModelImporter)AssetImporter.GetAtPath(dest);
        sb.AppendLine($"default import: animationType={importer.animationType} useFileScale={importer.useFileScale} globalScale={importer.globalScale} fileScale={importer.fileScale}");
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(dest).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine($"avatar: {(avatar == null ? "NONE" : $"isValid={avatar.isValid} isHuman={avatar.isHuman}")}");
        sb.AppendLine("humanDescription errors: " + (importer.importedTakeInfos.Length >= 0 ? "see Editor.log 'Avatar'" : ""));
        var missing = importer.humanDescription.human.Where(h => string.IsNullOrEmpty(h.boneName)).Select(h => h.humanName).ToArray();
        sb.AppendLine($"mapped human bones: {importer.humanDescription.human.Length}, empty names: {missing.Length}");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dest);
        var go = Object.Instantiate(prefab);
        var anim = go.GetComponentInChildren<Animator>();
        sb.AppendLine($"animator: {(anim == null ? "NONE" : $"avatarValid={anim.avatar != null && anim.avatar.isValid} isHuman={anim.isHuman}")}");

        // Mesh stats
        int tris = 0, verts = 0;
        Bounds all = default; bool first = true;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var m = r.sharedMesh;
            int t = 0; for (int s = 0; s < m.subMeshCount; s++) t += (int)m.GetIndexCount(s) / 3;
            tris += t; verts += m.vertexCount;
            var baked = new Mesh(); r.BakeMesh(baked);
            var b = new Bounds(r.transform.TransformPoint(baked.vertices[0]), Vector3.zero);
            foreach (var v in baked.vertices) b.Encapsulate(r.transform.TransformPoint(v));
            Object.DestroyImmediate(baked);
            sb.AppendLine($"mesh {r.name}: tris={t} verts={m.vertexCount} boundsY=[{b.min.y:F3},{b.max.y:F3}] height={b.size.y:F3} mats={r.sharedMaterials.Length}");
            if (first) { all = b; first = false; } else all.Encapsulate(b);
        }
        sb.AppendLine($"TOTAL tris={tris} verts={verts}");
        float height = all.size.y;
        sb.AppendLine($"overall height={height:F3} m  (min y {all.min.y:F3}, max y {all.max.y:F3}, width x {all.size.x:F3})");

        importer.ExtractTextures(TempDir);
        AssetDatabase.Refresh();
        foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { TempDir }))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
            sb.AppendLine($"texture {tex.name}: {tex.width}x{tex.height}");
        }
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            foreach (var m in r.sharedMaterials) sb.AppendLine($"material {r.name}/{m.name} shader={m.shader.name}");

        // Humanoid bones
        if (anim != null && anim.isHuman)
        {
            Vector3 P(HumanBodyBones b) { var tr = anim.GetBoneTransform(b); return tr == null ? Vector3.zero : tr.position; }
            float D(HumanBodyBones a, HumanBodyBones b) => Vector3.Distance(P(a), P(b));
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var headMesh = go.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name.ToLower().Contains("head"));
            float headH = headMesh != null ? HeadExtent(headMesh) : 0f;
            sb.AppendLine($"hips y={P(HumanBodyBones.Hips).y:F3} head bone y={P(HumanBodyBones.Head).y:F3} neck y={P(HumanBodyBones.Neck).y:F3}");
            sb.AppendLine($"head mesh height={headH:F3} m = {headH / height:P1} of body ({(headH > 0 ? height / headH : 0):F2} heads tall); headTop={(headMesh != null ? HeadTop(headMesh) : 0):F3}");
            sb.AppendLine($"eye height estimate (head bone y + 0.08)={(P(HumanBodyBones.Head).y - all.min.y + 0.08f):F3} m");
            sb.AppendLine($"leg (hips->foot) = {D(HumanBodyBones.Hips, HumanBodyBones.LeftFoot):F3}, upper leg={D(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg):F3}, lower leg={D(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot):F3}, foot->sole={(P(HumanBodyBones.LeftFoot).y - all.min.y):F3}");
            float upper = D(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), fore = D(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            var hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            float handLen = hand.GetComponentsInChildren<Transform>().Max(t => Vector3.Distance(t.position, hand.position));
            sb.AppendLine($"arm: upper={upper:F3} fore={fore:F3} hand(wrist->farthest finger bone)={handLen:F3} total shoulder->fingertip~{upper + fore + handLen:F3}");
            sb.AppendLine($"shoulder width={D(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm):F3} spine(hips->neck)={D(HumanBodyBones.Hips, HumanBodyBones.Neck):F3}");
            sb.AppendLine($"unity bone count mapped: {System.Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Count(b => b != HumanBodyBones.LastBone && anim.GetBoneTransform(b) != null)}/{(int)HumanBodyBones.LastBone}");
            // Rifle lengths currently used by CharacterKit, relative to this body.
            foreach (var w in CharacterKit.Weapons)
                sb.AppendLine($"weapon {w.File} length {w.Length:F2} m = {w.Length / height:P0} of height, {w.Length / (upper + fore + handLen):P0} of arm");
        }
        Object.DestroyImmediate(go);
    }

    static float HeadTop(SkinnedMeshRenderer r) { var m = new Mesh(); r.BakeMesh(m); float y = m.vertices.Max(v => r.transform.TransformPoint(v).y); Object.DestroyImmediate(m); return y; }
    static float HeadExtent(SkinnedMeshRenderer r) { var m = new Mesh(); r.BakeMesh(m); var ys = m.vertices.Select(v => r.transform.TransformPoint(v).y).ToArray(); Object.DestroyImmediate(m); return ys.Max() - ys.Min(); }
}

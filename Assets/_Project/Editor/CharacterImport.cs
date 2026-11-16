using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// S3 (#255, ADR-192): brings staged Mixamo character FBX files into the project as Humanoid models with Unity's
// built-in Mesh LOD. The FBX and its textures stay out of git (.gitignore); their .meta files are committed so GUIDs
// are stable. Re-run on a fresh clone to restore: tools/unity.sh run CharacterImport.SyncStaging
public static class CharacterImport
{
    const string StagingDir = "AssetStaging/mixamo";
    public const string RosterDir = "Assets/_Project/Art/Characters/Mixamo";
    public const string Prefix = "Roster_";
    const string Report = "Logs/s3_import.txt";

    public static void SyncStaging()
    {
        var sb = new StringBuilder();
        if (!Directory.Exists(StagingDir)) { sb.AppendLine("MISSING " + StagingDir); Finish(sb, 1); return; }
        foreach (var src in Directory.GetFiles(StagingDir, "*.fbx"))
        {
            var name = Prefix + Path.GetFileNameWithoutExtension(src).Replace("_Mixamo_Tpose", "").Replace("_Tpose", "") + ".fbx";
            var dest = RosterDir + "/" + name;
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(src).Length)
            {
                File.Copy(src, dest, true);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
            }
            Configure(dest, sb);
            Stats(dest, sb);
        }
        SyncUniversal(sb);
        Finish(sb, 0);
    }

    // Quaternius Universal Base Characters (CC0, ledger C-0003): Humanoid-compatible FBX with separate texture files.
    // Source folder is outside the repo (sublevel_staging); override with SUBLEVEL_UBC_DIR. Same rule as Mixamo:
    // the FBX/PNG stay untracked, their .meta (stable GUIDs) are committed.
    public const string UbcRosterDir = "Assets/_Project/Art/Characters/Ubc";
    static string UbcSource() => System.Environment.GetEnvironmentVariable("SUBLEVEL_UBC_DIR")
        ?? "/Volumes/WorkSpace/Project/sublevel_staging/QuaterniusUniversal/BaseCharacters";

    static void SyncUniversal(StringBuilder sb)
    {
        var dir = UbcSource();
        if (!Directory.Exists(dir)) { sb.AppendLine("UBC source missing (skipped): " + dir); return; }
        Directory.CreateDirectory(UbcRosterDir);
        var copied = false;
        foreach (var src in Directory.GetFiles(dir, "*.fbx").Concat(Directory.GetFiles(dir + "/Textures", "*.png")))
        {
            var name = Path.GetFileName(src);
            var dest = UbcRosterDir + "/" + (name.StartsWith("T_") ? Prefix + name.Substring(2) : Prefix + name.Replace("_FullBody", ""));
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(src).Length) { File.Copy(src, dest, true); copied = true; }
        }
        if (copied) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var fbx in Directory.GetFiles(UbcRosterDir, Prefix + "*.fbx"))
        {
            var path = fbx.Replace('\\', '/');
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.generateMeshLods = true;
            imp.SaveAndReimport();
            sb.AppendLine(path + ": Humanoid, meshLods=True");
            Stats(path, sb);
        }
    }

    static void Configure(string path, StringBuilder sb)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.generateMeshLods = true;
        imp.SaveAndReimport();
        // Textures are embedded in the FBX: extract once (next to it, prefixed) so the materials can sample them.
        var prefix = Path.GetFileNameWithoutExtension(path);
        if (!AssetDatabase.FindAssets("t:Texture2D " + prefix + "_", new[] { RosterDir }).Any())
        {
            // The committed .meta files carry the GUIDs the materials refer to; extraction would mint new ones.
            // Unity drops orphan .meta files at startup, so the committed copy is read from git.
            var guids = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var line in Git("ls-files " + RosterDir + "/" + prefix + "_*.png.meta").Split('\n', System.StringSplitOptions.RemoveEmptyEntries))
                guids[Path.GetFileName(line).Replace(".meta", "")] = Regex.Match(Git("show HEAD:" + line), @"guid: (\w+)").Groups[1].Value;
            imp.ExtractTextures(RosterDir);
            AssetDatabase.Refresh();
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { RosterDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var n = Path.GetFileName(p);
                if (n.StartsWith(Prefix) || !(n.StartsWith("Soldier_") || n.StartsWith(prefix.Replace(Prefix, "")))) continue;
                AssetDatabase.RenameAsset(p, prefix + "_" + Path.GetFileNameWithoutExtension(n));
            }
            AssetDatabase.SaveAssets();
            foreach (var kv in guids)
            {
                var meta = RosterDir + "/" + kv.Key + ".meta";
                if (File.Exists(meta)) File.WriteAllText(meta, new Regex(@"guid: \w+").Replace(File.ReadAllText(meta), "guid: " + kv.Value, 1));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
        sb.AppendLine($"{path}: Humanoid, meshLods={imp.generateMeshLods} maxMeshLod={imp.maximumMeshLod}");
    }

    static string Git(string args)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", args)
            { RedirectStandardOutput = true, UseShellExecute = false });
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    public static void Stats(string path, StringBuilder sb)
    {
        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine($"avatar: {(avatar == null ? "NONE" : $"isValid={avatar.isValid} isHuman={avatar.isHuman}")}");
        var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var anim = go.GetComponentInChildren<Animator>();
        sb.AppendLine($"animator isHuman={anim != null && anim.isHuman}");
        int total0 = 0; var perLod = new int[8];
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var m = r.sharedMesh;
            var line = new StringBuilder($"mesh {r.name}: verts={m.vertexCount} lodCount={m.lodCount}");
            for (int l = 0; l < Mathf.Max(1, m.lodCount); l++)
            {
                int idx = 0;
                for (int s = 0; s < m.subMeshCount; s++) idx += m.lodCount > 0 ? (int)m.GetLod(s, l).indexCount : (int)m.GetIndexCount(s);
                line.Append($" LOD{l}={idx / 3}tris");
                if (l < perLod.Length) perLod[l] += idx / 3;
            }
            total0 += (int)Enumerable.Range(0, m.subMeshCount).Sum(s => m.GetIndexCount(s)) / 3;
            sb.AppendLine(line.ToString());
        }
        sb.AppendLine($"TOTAL tris (all submesh indices) = {total0}; per LOD: " + string.Join(", ", perLod.Select((v, i) => $"LOD{i}={v}").Where(s => !s.EndsWith("=0"))));
        Object.DestroyImmediate(go);
    }

    static void Finish(StringBuilder sb, int code)
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, sb.ToString());
        Debug.Log(sb.ToString());
        if (code != 0) EditorApplication.Exit(code);
    }
}

using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Fails builds when an asset folder is missing from docs/ASSET_LEDGER.md or lacks LICENSE.txt (ADR-027).
public class AssetValidator : IPreprocessBuildWithReport
{
    const string LedgerPath = "docs/ASSET_LEDGER.md";
    static readonly string[] Roots = { "Assets/_Project/Art", "Assets/_Project/Audio" };

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        var errors = Validate();
        if (errors.Count > 0) throw new BuildFailedException("[AssetValidator] " + string.Join("; ", errors));
    }

    // Asset unit: <root>/<category>/<asset>/
    static List<string> AssetFolders()
    {
        var folders = new List<string>();
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var category in Directory.GetDirectories(root))
                foreach (var asset in Directory.GetDirectories(category))
                    folders.Add(asset.Replace('\\', '/'));
        }
        return folders;
    }

    public static List<string> Validate()
    {
        var errors = new List<string>();
        var folders = AssetFolders();
        foreach (var folder in AssetLedger.FindUnlisted(File.ReadAllText(LedgerPath), folders))
            errors.Add("not in ASSET_LEDGER: " + folder);
        foreach (var folder in folders)
            if (!File.Exists(folder + "/LICENSE.txt"))
                errors.Add("LICENSE.txt missing: " + folder);
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var category in Directory.GetDirectories(root))
                foreach (var file in Directory.GetFiles(category))
                    if (!file.EndsWith(".meta"))
                        errors.Add("file outside an asset folder: " + file.Replace('\\', '/'));
        }
        return errors;
    }

    // tools/unity.sh run AssetValidator.ValidateOrExit
    public static void ValidateOrExit()
    {
        var errors = Validate();
        foreach (var e in errors) Debug.LogError("[AssetValidator] " + e);
        Debug.Log($"[AssetValidator] {errors.Count} problem(s)");
        EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }
}

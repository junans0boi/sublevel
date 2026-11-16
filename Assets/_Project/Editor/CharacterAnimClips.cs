using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Animation track (ADR-230): the Mixamo rifle clips are imported as Humanoid (no bake, ADR-187) so every roster model
// plays the same clips. Avatar = Roster_Swat's, clips are shared. tools/unity.sh run CharacterAnimClips.Reimport
public static class CharacterAnimClips
{
    public const string Dir = "Assets/_Project/Art/Characters/Mixamo";
    public const string AvatarSource = Dir + "/Roster_Swat.fbx";
    // Locomotion / hold clips loop; everything else plays once (reload, draw, death, prone/fire bursts are triggered).
    static readonly string[] Loops =
    {
        "RifleIdle", "RifleWalk", "RifleRun", "RifleWalkBack", "RifleSideStep", "RifleCrouchWalk", "RifleCrouchWalkBack",
        "RifleKneelIdle", "Swat_RifleAimingIdle_skin", "FiringRifleStand", "ProneFiringRifle",
    };
    // Clips that stay at the start pose of the file's own root (they are in place already); all of them bake root rotation and height.
    public static readonly string[] Names =
    {
        "RifleIdle", "RifleWalk", "RifleRun", "RifleWalkBack", "RifleSideStep", "RifleCrouchWalk", "RifleCrouchWalkBack", "RifleKneelIdle",
        "Swat_RifleAimingIdle_skin", "FiringRifleStand", "ProneFiringRifle", "ProneReloadingRifle", "ReloadingRifleStand", "ReloadRifleCrouch",
        "RiflePullOut", "RiflePutAway", "RifleDeath",
    };

    // Quaternius Universal Animation Library 1 (CC0, ledger C-0004): the bare-handed set the Mixamo rifle clips lack (ADR-255).
    // The FBX stays out of git like the roster models (.gitignore); its .meta is committed. Source folder: SUBLEVEL_UBC_DIR's sibling UAL1.
    public const string UalPath = "Assets/_Project/Art/Characters/Ual/UAL1_Standard.fbx";
    static string UalSource() => System.Environment.GetEnvironmentVariable("SUBLEVEL_UAL_DIR")
        ?? "/Volumes/WorkSpace/Project/sublevel_staging/QuaterniusUniversal/UAL1";
    // Source clip name in the file -> our clip name (prefix U_). Loops: walk/jog/idle only. Hit_Chest is read as an additive pose (frame 0 = reference).
    public static readonly string[] UalNames = { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Punch_Jab", "Punch_Cross", "Hit_Chest", "Crouch_Idle_Loop", "Crouch_Fwd_Loop" };
    static readonly string[] UalLoops = { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Crouch_Idle_Loop", "Crouch_Fwd_Loop" };

    public static void SyncUal()
    {
        var src = UalSource() + "/UAL1_Standard.fbx";
        if (!File.Exists(UalPath))
        {
            if (!File.Exists(src)) throw new FileNotFoundException("UAL1 source missing: " + src);
            Directory.CreateDirectory(Path.GetDirectoryName(UalPath));
            File.Copy(src, UalPath);
            AssetDatabase.ImportAsset(UalPath, ImportAssetOptions.ForceSynchronousImport);
        }
        var imp = (ModelImporter)AssetImporter.GetAtPath(UalPath);
        bool ok = imp.animationType == ModelImporterAnimationType.Human && imp.clipAnimations.Length == UalNames.Length;
        if (ok) foreach (var n in UalNames) ok &= LoadClip("U_" + n) != null;
        if (ok) return;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; // its own skeleton; the clips retarget to every roster model
        imp.importAnimation = true;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        var list = new System.Collections.Generic.List<ModelImporterClipAnimation>();
        foreach (var d in imp.defaultClipAnimations)
        {
            string n = d.name.Substring(d.name.IndexOf('|') + 1);
            if (!UalNames.Contains(n)) continue;
            var c = d;
            c.name = "U_" + n;
            c.loopTime = c.loopPose = UalLoops.Contains(n);
            c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = false;
            c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
            if (n == "Hit_Chest") { c.hasAdditiveReferencePose = true; c.additiveReferencePoseFrame = 0f; }
            list.Add(c);
        }
        imp.clipAnimations = list.ToArray();
        imp.SaveAndReimport();
    }

    public static Avatar SwatAvatar() =>
        AssetDatabase.LoadAllAssetsAtPath(AvatarSource).OfType<Avatar>().FirstOrDefault();

    // Reimports only when a clip is not Humanoid yet (CharacterKit.Prepare calls this every build).
    public static void EnsureImported()
    {
        var avatar = SwatAvatar();
        if (avatar == null || !avatar.isHuman) throw new System.Exception("Roster_Swat avatar missing or not Humanoid (run CharacterImport.SyncStaging)");
        SyncUal();
        foreach (var n in Names)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath($"{Dir}/{n}.fbx");
            if (imp.animationType != ModelImporterAnimationType.Human || imp.sourceAvatar != avatar || LoadClip(n) == null) Configure($"{Dir}/{n}.fbx", n, avatar);
        }
    }

    public static void Reimport()
    {
        var sb = new StringBuilder();
        var avatar = SwatAvatar();
        if (avatar == null || !avatar.isHuman) { Debug.LogError("Roster_Swat avatar missing or not humanoid (run CharacterImport.SyncStaging)"); EditorApplication.Exit(1); return; }
        SyncUal();
        foreach (var n in Names) sb.AppendLine(Configure($"{Dir}/{n}.fbx", n, avatar));
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/anim_clips.txt", sb.ToString());
        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }

    static string Configure(string path, string name, Avatar avatar)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        imp.sourceAvatar = avatar;
        imp.importAnimation = true;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        var clips = imp.defaultClipAnimations;
        if (clips.Length == 0) return path + ": NO CLIP";
        var c = clips[0];
        c.name = name;
        c.loopTime = Loops.Contains(name);
        c.loopPose = c.loopTime;
        // Root motion is discarded at runtime (applyRootMotion=false); rotation and height stay as authored so crouch / prone keep their level.
        c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = false;
        c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
        imp.clipAnimations = new[] { c };
        imp.SaveAndReimport();
        var clip = LoadClip(name);
        return $"{path}: human={imp.animationType} clip={clip?.name} len={clip?.length:F2}s loop={clip?.isLooping} humanMotion={clip?.humanMotion}";
    }

    // Clips named U_* come from the UAL file, the rest from their own Mixamo file.
    public static AnimationClip LoadClip(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(name.StartsWith("U_") ? UalPath : $"{Dir}/{name}.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
}

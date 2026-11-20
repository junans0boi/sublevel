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

    // Mixamo strafe and diagonal runs (ledger C-0011, ADR-391): Humanoid on the Swat avatar like the rifle clips above, but the FBX files
    // stay out of git (no redistribution of the originals): restored from AssetStaging/Mixamo/Strafe; their .meta files are committed.
    public const string StrafeDir = Dir + "/Strafe";
    const string StrafeStaging = "AssetStaging/Mixamo/Strafe";
    public static readonly string[] StrafeNames =
    {
        "Rifle_RunLeft", "Rifle_RunRight", "Rifle_RunForwardLeft", "Rifle_RunForwardRight", "Rifle_RunBackwardLeft", "Rifle_RunBackwardRight",
        "Rifle_WalkStrafeLeft", "Rifle_WalkStrafeRight", "Rifle_AimStrafeLeft", "Rifle_AimStrafeRight",
        "Unarmed_RunStrafeLeft", "Unarmed_RunStrafeRight", "Unarmed_WalkStrafeLeft", "Unarmed_WalkStrafeRight",
    };
    static bool IsStrafe(string name) => System.Array.IndexOf(StrafeNames, name) >= 0;

    static void SyncStrafe(Avatar avatar, StringBuilder log)
    {
        Directory.CreateDirectory(StrafeDir);
        foreach (var n in StrafeNames)
        {
            string path = $"{StrafeDir}/{n}.fbx";
            if (!File.Exists(path))
            {
                string src = $"{StrafeStaging}/{n}.fbx";
                if (!File.Exists(src)) throw new FileNotFoundException("Mixamo strafe clip missing (ledger C-0011): " + src);
                File.Copy(src, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (log != null || imp.animationType != ModelImporterAnimationType.Human || imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || LoadClip(n) == null)
            {
                string line = Configure(path, n, null);
                log?.AppendLine(line);
            }
        }
    }

    // Quaternius Universal Animation Library 1 (CC0, ledger C-0004): the bare-handed set the Mixamo rifle clips lack (ADR-255).
    // The FBX stays out of git like the roster models (.gitignore); its .meta is committed. Source folder: SUBLEVEL_UBC_DIR's sibling UAL1.
    public const string UalPath = "Assets/_Project/Art/Characters/Ual/UAL1_Standard.fbx";
    static string UalSource() => System.Environment.GetEnvironmentVariable("SUBLEVEL_UAL_DIR")
        ?? "/Volumes/WorkSpace/Project/sublevel_staging/QuaterniusUniversal/UAL1";
    // Source clip name in the file -> our clip name (prefix U_). Loops: the cycles and the airborne Jump_Loop (ADR-537). Hit_Chest is read as an additive pose (frame 0 = reference).
    public static readonly string[] UalNames = { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Punch_Jab", "Punch_Cross", "Hit_Chest", "Crouch_Idle_Loop", "Crouch_Fwd_Loop", "Swim_Idle_Loop", "Swim_Fwd_Loop", "Sprint_Loop", "Jump_Start", "Jump_Loop", "Jump_Land" };
    static readonly string[] UalLoops = { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Crouch_Idle_Loop", "Crouch_Fwd_Loop", "Swim_Idle_Loop", "Swim_Fwd_Loop", "Jump_Loop" };

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
        bool ok = imp.animationType == ModelImporterAnimationType.Human && imp.clipAnimations.Length == UalNames.Length && imp.clipAnimations.All(c => !c.keepOriginalOrientation);
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
            // Not the file's orientation: UAL's forward axis is not the model's, which turned the body round (player report, ADR-255): body orientation.
            c.keepOriginalOrientation = false; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
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
        SyncStrafe(avatar, null);
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
        SyncStrafe(avatar, sb);
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
        // The strafe clips were taken on another Mixamo character (Ch35, no eye bones): the Swat avatar does not match its hierarchy, so
        // each builds its own; Humanoid retargets them to every roster model all the same.
        if (avatar != null) { imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; imp.sourceAvatar = avatar; }
        else imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = true;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        var clips = imp.defaultClipAnimations;
        if (clips.Length == 0) return path + ": NO CLIP";
        var c = clips[0];
        c.name = name;
        c.loopTime = Loops.Contains(name) || IsStrafe(name);
        c.loopPose = c.loopTime;
        // Root motion is discarded at runtime (applyRootMotion=false); rotation and height stay as authored so crouch / prone keep their level.
        c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = false;
        c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
        // Another character's file (strafes, own avatar): its root height is in that body's units and sank the feet 5-9 cm into the floor;
        // the height is taken from the feet instead.
        if (avatar == null) { c.keepOriginalPositionY = false; c.heightFromFeet = true; }
        imp.clipAnimations = new[] { c };
        imp.SaveAndReimport();
        var clip = LoadClip(name);
        return $"{path}: human={imp.animationType} clip={clip?.name} len={clip?.length:F2}s loop={clip?.isLooping} humanMotion={clip?.humanMotion}";
    }

    // Clips named U_* come from the UAL file, the rest from their own Mixamo file.
    public static AnimationClip LoadClip(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(name.StartsWith("U_") ? UalPath : IsStrafe(name) ? $"{StrafeDir}/{name}.fbx" : $"{Dir}/{name}.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
}

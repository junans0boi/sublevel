using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    // The Standalone scripting backend is shared by mac and win, so each build sets it explicitly.
    // win uses Mono until the windows-il2cpp module is installed.
    public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Build/mac/Sublevel.app", ScriptingImplementation.IL2CPP);
    // Review captures only: a quicker Mono build beside the real one, so a running Build/mac copy is left alone.
    public static void BuildMacReview() => Build(BuildTarget.StandaloneOSX, "Build/review/Sublevel.app", ScriptingImplementation.Mono2x);
    public static void BuildWin() =>Build(BuildTarget.StandaloneWindows64, "Build/win/Sublevel.exe", ScriptingImplementation.Mono2x);

    // Dedicated server (#168, ADR-127): the normal standalone player, started with `--dedicated -batchmode -nographics`
    // (docs/DEDICATED_SERVER.md). Mono so it builds quickly; the same build serves every platform's clients.
    [MenuItem("Build/Dedicated Server (mac)")]
    public static void BuildServerMenu() => BuildServerMac();
    public static void BuildServerMac() => Build(BuildTarget.StandaloneOSX, "Build/server/SublevelServer.app", ScriptingImplementation.Mono2x);
    public static void BuildServerWin() => Build(BuildTarget.StandaloneWindows64, "Build/server-win/SublevelServer.exe", ScriptingImplementation.Mono2x);

    // Oracle Always Free ARM server (#381, docs/research/linux_arm64_server_build.md): Linux ARM64, Server subtarget.
    public static void BuildServerLinuxArm64()
    {
        EditorUserBuildSettings.SetPlatformSettings("Linux64", "Architecture", "ARM64");
        Debug.Log($"[BuildScript] Linux Architecture setting = {EditorUserBuildSettings.GetPlatformSettings("Linux64", "Architecture")}");
        Build(BuildTarget.StandaloneLinux64, "Build/server-linux-arm64/SublevelServer", ScriptingImplementation.IL2CPP, server: true);
    }

    static void Build(BuildTarget target, string output, ScriptingImplementation backend, bool server = false)
    {
        try
        {
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, backend);
            if (server) PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Server, backend);
            EditorUserBuildSettings.standaloneBuildSubtarget = server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player;
            var options = new BuildPlayerOptions
            {
                scenes = Array.ConvertAll(EditorBuildSettings.scenes, s => s.path),
                locationPathName = output,
                target = target,
                subtarget = (int)(server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
                options = BuildOptions.None,
            };
            var summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"[BuildScript] {target}: {summary.result}, {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime}");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}

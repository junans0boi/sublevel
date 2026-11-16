using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;

// Cuts the Freesound sequences in AssetStaging/audio into single clips (#285, ADR-183). Run headless:
//   tools/unity.sh run SoundCutTool.Run
// Sources are decoded by Unity (copied to a throwaway Assets/_StagingImport folder, imported as PCM, read, deleted), so
// FLAC/MP3/AIFF/M4A work without ffmpeg. Output WAVs are mono 16-bit at the source rate under Assets/_Project/Audio/.
// Onsets are found by level (SoundCuts.FindOnsets); nobody has listened to the result yet, so the tool prints a table
// of every clip it wrote for a human to check.
public static class SoundCutTool
{
    const string Staging = "AssetStaging/audio/";
    const string Temp = "Assets/_StagingImport";
    public const string StepsRoot = "Assets/_Project/Audio/Foley/Freesound/Resources/Steps/";

    struct Cut
    {
        public string Folder;   // Resources/Steps/<Folder>
        public string Prefix;   // clip name stem: step_<folder>[_run]
        public int Id;          // Freesound id (file name starts with "<id>__")
        public int Count;       // clips to keep
        public float Pre, Length, Fade;
        public float Gap, Threshold;
        public float FromSec, ToSec; // part of the file to search (ToSec 0 = to the end)
    }

    static Cut S(string folder, int id, int count = 6, float gap = 0.3f, float thr = 0.25f, float len = 0.45f, float from = 0f, float to = 0f, string prefix = null) =>
        new Cut { Folder = folder, Prefix = prefix ?? "step_" + folder, Id = id, Count = count, Pre = 0.03f, Length = len, Fade = 0.012f, Gap = gap, Threshold = thr, FromSec = from, ToSec = to };

    // One row per surface (docs/research/13_sound_wiring_design.md §1). Carpet, MetalGrate, Glass and TinRoof use another
    // surface's clips (SurfaceSounds fallbacks).
    static readonly Cut[] Cuts =
    {
        S("concrete", 813622), S("metal", 698697), S("wood", 546362, thr: 0.12f, gap: 0.25f), S("grass", 556042, thr: 0.2f), S("dirt", 349082, thr: 0.1f, gap: 0.25f),
        S("water", 531566, count: 6, thr: 0.05f, gap: 0.2f), S("gravel", 556002), S("asphalt", 877222, to: 40f), S("tile", 555868, from: 0f, to: 12f),
        S("rock", 558472), S("rock", 558471, prefix: "step_rock_run"), S("sand", 564893, thr: 0.1f, gap: 0.25f), S("mud", 548384),
        S("deck", 636080), S("metalstairs", 451863, gap: 0.4f, len: 0.6f),
    };

    public static void Run()
    {
        var report = new List<string>();
        Directory.CreateDirectory(Temp);
        try
        {
            foreach (var cut in Cuts)
            {
                try { RunOne(cut, report); }
                catch (System.Exception e) { report.Add($"FAILED {cut.Folder} id {cut.Id}: {e.GetType().Name} {e.Message}"); AssetDatabase.DeleteAsset(Temp + "/" + Path.GetFileName(FindSource(cut.Id) ?? "")); }
            }
        }
        finally
        {
            AssetDatabase.DeleteAsset(Temp);
            AssetDatabase.Refresh();
        }
        Debug.Log("[SoundCutTool]\n" + string.Join("\n", report));
    }

    static void RunOne(Cut cut, List<string> report)
    {
        string src = FindSource(cut.Id);
        if (src == null) { report.Add($"MISSING source id {cut.Id}"); return; }
        string temp = Temp + "/" + Path.GetFileName(src);
        File.Copy(src, temp, true);
        AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(temp) as AudioImporter;
        if (importer == null) { report.Add($"NOT AN AUDIO IMPORT {Path.GetFileName(src)} ({AssetImporter.GetAtPath(temp)?.GetType().Name ?? "no importer"})"); AssetDatabase.DeleteAsset(temp); return; }
        var settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = true;
        importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(temp);
        if (clip == null) { report.Add($"NOT IMPORTED {src}"); return; }

        var data = new float[clip.samples * clip.channels];
        clip.GetData(data, 0);
        int rate = clip.frequency;
        int from = Mathf.Clamp((int)(cut.FromSec * rate), 0, data.Length);
        int to = cut.ToSec > 0f ? Mathf.Clamp((int)(cut.ToSec * rate), from, data.Length) : data.Length;
        var part = new float[to - from];
        System.Array.Copy(data, from, part, 0, part.Length);

        var onsets = SoundCuts.FindOnsets(part, rate, cut.Gap, cut.Threshold);
        string dir = StepsRoot + cut.Folder;
        Directory.CreateDirectory(dir);
        int written = 0;
        // Keep the first `Count` onsets (a take starts with its cleanest steps; the tail is where the recordist stops).
        for (int i = 0; i < onsets.Count && written < cut.Count; i++)
        {
            var slice = SoundCuts.Slice(part, rate, onsets[i], cut.Pre, cut.Length, cut.Fade, 0.8f);
            if (slice.Length < rate * 0.1f) continue;
            written++;
            File.WriteAllBytes($"{dir}/{cut.Prefix}_{written:00}.wav", SoundCuts.WriteWav16(slice, rate));
        }
        report.Add($"{cut.Folder,-12} id {cut.Id,-7} {Path.GetFileName(src),-60} onsets {onsets.Count,3} written {written} rate {rate} src {part.Length / (float)rate:0.0}s");
        AssetDatabase.DeleteAsset(temp);
    }

    static string FindSource(int id)
    {
        // Prefer a .wav next to the original: Unity cannot import .m4a, so those were converted once with macOS afconvert.
        string found = null;
        foreach (var f in Directory.GetFiles(Staging, id + "__*"))
        {
            if (f.EndsWith(".crdownload")) continue;
            if (f.EndsWith(".wav")) return f;
            found = found ?? f;
        }
        return found;
    }
}

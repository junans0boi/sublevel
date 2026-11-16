// DRAFT for #284 (T1). Not in Assets/ on purpose (no Unity run yet; an uncompiled file would break other sessions' batch runs).
// Goes to Assets/_Project/Scripts/Core/SurfaceSounds.cs (S5 owns it) once S1 has appended Mud, Deck, MetalStairs, TinRoof to SurfaceId.
using System;

namespace Game.Core
{
    // What each surface sounds like (docs/research/13_sound_wiring_design.md, ADR-183): the Resources folder of its step
    // clips, how loud the step rings, and which surface's clips stand in while it has none of its own. Pure numbers;
    // CombatantAudio plays them. FootSurface (six kinds) stays for bot ears; clip choice uses SurfaceId.
    public static class SurfaceSounds
    {
        public struct Entry
        {
            public SurfaceId Surface;
            public string Folder;      // Resources/Steps/<Folder>; lowercase, no spaces
            public float Loudness;     // 1 = a standing run on concrete (SoundRules.SurfaceLoudness scale)
            public SurfaceId Fallback; // clips of this surface play when Folder has none; == Surface when it has its own
            public float FallbackGain; // volume of the stand-in clips (Carpet plays Wood at 0.6)
        }

        static Entry E(SurfaceId s, string folder, float loud, SurfaceId? fallback = null, float gain = 1f) =>
            new Entry { Surface = s, Folder = folder, Loudness = loud, Fallback = fallback ?? s, FallbackGain = gain };

        public static readonly Entry[] Table =
        {
            E(SurfaceId.Concrete, "concrete", 1.00f),
            E(SurfaceId.Metal, "metal", 1.30f),
            E(SurfaceId.Wood, "wood", 1.15f),
            E(SurfaceId.Grass, "grass", 0.75f),
            E(SurfaceId.Dirt, "dirt", 0.90f),
            E(SurfaceId.Water, "water", 1.20f),
            E(SurfaceId.Gravel, "gravel", 1.25f),
            E(SurfaceId.Asphalt, "asphalt", 1.00f),
            E(SurfaceId.Tile, "tile", 1.10f),
            E(SurfaceId.Carpet, "carpet", 0.70f, SurfaceId.Wood, 0.6f),
            E(SurfaceId.MetalGrate, "metalgrate", 1.25f, SurfaceId.Metal),
            E(SurfaceId.Glass, "glass", 1.15f, SurfaceId.Tile),
            E(SurfaceId.Rock, "rock", 1.00f),
            E(SurfaceId.Sand, "sand", 0.80f),
            // appended by S1 (ADR-183): numbers 14..17
            E(SurfaceId.Mud, "mud", 0.80f),
            E(SurfaceId.Deck, "deck", 1.15f),
            E(SurfaceId.MetalStairs, "metalstairs", 1.35f),
            E(SurfaceId.TinRoof, "tinroof", 1.40f, SurfaceId.Metal),
        };

        public static Entry Of(SurfaceId id)
        {
            foreach (var e in Table) if (e.Surface == id) return e;
            return Table[0]; // unknown id: neutral concrete, like the tag-less collider fallback
        }

        public static float Loudness(SurfaceId id) => Of(id).Loudness;

        // The clip folder to load for a surface: its own, or the stand-in's when `hasOwnClips` says it has none.
        public static string StepFolder(SurfaceId id, Func<string, bool> hasClips)
        {
            var e = Of(id);
            if (hasClips("Steps/" + e.Folder)) return "Steps/" + e.Folder;
            return "Steps/" + Of(e.Fallback).Folder;
        }
    }
}

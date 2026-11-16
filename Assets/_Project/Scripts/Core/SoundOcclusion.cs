using System;

namespace Game.Core
{
    // How a sound loses level and highs on its way through floors and walls (ADR-251). Pure numbers: the probe in
    // Gameplay counts what lies between source and ear, this turns the counts into a gain and a low-pass cutoff.
    public readonly struct Occlusion
    {
        public readonly float Gain, CutoffHz;
        public Occlusion(float gain, float cutoffHz) { Gain = gain; CutoffHz = cutoffHz; }
        public static readonly Occlusion Open = new Occlusion(1f, SoundOcclusion.OpenCutoff);
    }

    public static class SoundOcclusion
    {
        public const float OpenCutoff = 22000f, MinCutoff = 500f;
        // One floor between: -6 dB and the highs gone (22 kHz -> about 2 kHz). One wall: -3 dB, highs rounded off.
        public const float FloorGain = 0.5f, WallGain = 0.7f, FloorCutoffFactor = 0.09f, WallCutoffFactor = 0.55f;
        // Walls beyond this many add nothing more (the sound is then mostly flanking paths).
        public const int MaxWalls = 3, MaxFloors = 3;
        // Steel stairs carry sound between floors: a step on them is only half as muffled by the floors it crosses.
        public const float StairsFloorFactor = 0.5f;
        // Cost limits (design 13 §6): beyond ProbeRange only the distance filter applies, a source re-probes every
        // RefreshSeconds, and all sources together cast at most RaysPerFrame rays a frame.
        public const float ProbeRange = 60f, RefreshSeconds = 0.25f;
        public const int RaysPerFrame = 8;
        // A surface this flat (|normal.y|) is a floor or ceiling, otherwise a wall.
        public const float FloorNormalY = 0.7f;
        // Two hits closer than this along the ray are one barrier (a wall made of two colliders).
        public const float MergeMeters = 0.4f;

        public static bool IsFloor(float normalY) => Math.Abs(normalY) >= FloorNormalY;

        public static bool ShouldProbe(float meters) => meters <= ProbeRange;

        public static Occlusion Of(int floors, int walls, bool throughStairs = false)
        {
            floors = Math.Min(Math.Max(floors, 0), MaxFloors);
            walls = Math.Min(Math.Max(walls, 0), MaxWalls);
            if (floors == 0 && walls == 0) return Occlusion.Open;
            float f = throughStairs ? floors * StairsFloorFactor : floors;
            float gain = (float)(Math.Pow(FloorGain, f) * Math.Pow(WallGain, walls));
            float cutoff = (float)(OpenCutoff * Math.Pow(FloorCutoffFactor, f) * Math.Pow(WallCutoffFactor, walls));
            return new Occlusion(gain, Math.Max(MinCutoff, cutoff));
        }

        // Frame-wise ray allowance shared by every source.
        public sealed class Budget
        {
            int _frame = -1, _used;

            public bool TryTake(int frame, int rays = 1)
            {
                if (frame != _frame) { _frame = frame; _used = 0; }
                if (_used + rays > RaysPerFrame) return false;
                _used += rays;
                return true;
            }
        }
    }
}

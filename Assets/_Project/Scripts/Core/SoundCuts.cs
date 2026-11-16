using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Cutting recorded sequences into single clips (ADR-183, #285). Pure numbers so EditMode can test it; the Editor tool
    // (SoundCutTool) decodes the source files and writes the WAVs. There is no ffmpeg or sox on the dev machine.
    public static class SoundCuts
    {
        // Sample indices where a new sound starts: the envelope (smoothed |x|, ~5 ms) rises above `thresholdRatio` x its
        // peak after at least `minGapSec` below half that level. A quiet recording still works because the level is
        // relative to the file's own peak, but a loud one-off (a door slam in a footstep take) hides the steps beside it.
        public static List<int> FindOnsets(float[] mono, int rate, float minGapSec, float thresholdRatio)
        {
            var onsets = new List<int>();
            if (mono == null || mono.Length == 0 || rate <= 0) return onsets;
            int win = Math.Max(1, rate / 200);
            var env = new float[mono.Length / win + 1];
            for (int i = 0; i < mono.Length; i++) env[i / win] = Math.Max(env[i / win], Math.Abs(mono[i]));
            float peak = 0f;
            foreach (var e in env) peak = Math.Max(peak, e);
            if (peak < 1e-5f) return onsets;
            float high = peak * thresholdRatio, low = high * 0.5f;
            int gapFrames = Math.Max(1, (int)(minGapSec * rate / win));
            int quiet = gapFrames; // starts "rested": a take that opens with a hit still counts it
            for (int f = 0; f < env.Length; f++)
            {
                if (env[f] >= high && quiet >= gapFrames)
                {
                    onsets.Add(f * win);
                    quiet = 0;
                }
                else if (env[f] < low) quiet++;
                else quiet = 0;
            }
            return onsets;
        }

        // `lengthSec` of audio starting `preSec` before the onset, faded in/out over `fadeSec`, scaled so its peak is `peak`.
        public static float[] Slice(float[] mono, int rate, int onset, float preSec, float lengthSec, float fadeSec, float peak)
        {
            int start = Math.Max(0, onset - (int)(preSec * rate));
            int count = Math.Min(mono.Length - start, (int)(lengthSec * rate));
            if (count <= 0) return new float[0];
            var clip = new float[count];
            Array.Copy(mono, start, clip, 0, count);
            int fade = Math.Min(count / 2, Math.Max(1, (int)(fadeSec * rate)));
            for (int i = 0; i < fade; i++)
            {
                float g = (float)i / fade;
                clip[i] *= g;
                clip[count - 1 - i] *= g;
            }
            float max = 0f;
            foreach (var s in clip) max = Math.Max(max, Math.Abs(s));
            if (max > 1e-6f)
            {
                float k = peak / max;
                for (int i = 0; i < count; i++) clip[i] *= k;
            }
            return clip;
        }

        // Mono 16-bit PCM WAV bytes.
        public static byte[] WriteWav16(float[] mono, int rate)
        {
            int dataBytes = mono.Length * 2;
            var b = new byte[44 + dataBytes];
            void Str(int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
            void Int(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
            void Short(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
            Str(0, "RIFF"); Int(4, 36 + dataBytes); Str(8, "WAVE"); Str(12, "fmt "); Int(16, 16);
            Short(20, 1); Short(22, 1); Int(24, rate); Int(28, rate * 2); Short(32, 2); Short(34, 16);
            Str(36, "data"); Int(40, dataBytes);
            for (int i = 0; i < mono.Length; i++)
            {
                int v = (int)Math.Round(Math.Max(-1f, Math.Min(1f, mono[i])) * 32767f);
                Short(44 + i * 2, v);
            }
            return b;
        }
    }
}

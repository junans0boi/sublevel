using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #285: the cutting maths behind the footstep and shot clips (pure numbers, no files).
    public class SoundCutsTests
    {
        const int Rate = 48000;

        // 4 short bursts 0.5 s apart in near-silence.
        static float[] Bursts(int n, float gapSec, float amp = 0.6f)
        {
            var x = new float[(int)(Rate * (n * gapSec + 0.5f))];
            var rnd = new Random(1);
            for (int i = 0; i < x.Length; i++) x[i] = (float)(rnd.NextDouble() - 0.5) * 0.004f;
            for (int k = 0; k < n; k++)
                for (int i = 0; i < Rate / 20; i++) x[(int)(k * gapSec * Rate) + i] = amp * (float)Math.Sin(i * 0.3) * (1f - i / (Rate / 20f));
            return x;
        }

        [Test]
        public void Finds_each_burst_once()
        {
            var onsets = SoundCuts.FindOnsets(Bursts(4, 0.5f), Rate, 0.25f, 0.3f);
            Assert.AreEqual(4, onsets.Count);
            for (int k = 0; k < 4; k++) Assert.AreEqual(k * 0.5f * Rate, onsets[k], Rate * 0.01f);
        }

        [Test]
        public void A_closer_burst_inside_the_gap_is_not_a_second_onset()
        {
            Assert.AreEqual(1, SoundCuts.FindOnsets(Bursts(2, 0.1f), Rate, 0.25f, 0.3f).Count);
        }

        [Test]
        public void Silence_has_no_onsets()
        {
            Assert.AreEqual(0, SoundCuts.FindOnsets(new float[Rate], Rate, 0.25f, 0.3f).Count);
            Assert.AreEqual(0, SoundCuts.FindOnsets(null, Rate, 0.25f, 0.3f).Count);
        }

        [Test]
        public void A_slice_has_the_asked_length_peak_and_faded_ends()
        {
            var x = Bursts(2, 0.5f);
            var clip = SoundCuts.Slice(x, Rate, Rate / 2, 0.02f, 0.3f, 0.01f, 0.8f);
            Assert.AreEqual((int)(0.3f * Rate), clip.Length);
            float peak = 0f;
            foreach (var s in clip) peak = Math.Max(peak, Math.Abs(s));
            Assert.AreEqual(0.8f, peak, 1e-3f);
            Assert.AreEqual(0f, clip[0], 1e-6f);
            Assert.AreEqual(0f, clip[clip.Length - 1], 1e-6f);
        }

        [Test]
        public void A_slice_past_the_end_is_cut_short_and_never_negative()
        {
            var x = new float[1000];
            for (int i = 0; i < x.Length; i++) x[i] = 0.5f;
            Assert.AreEqual(300, SoundCuts.Slice(x, Rate, 700, 0f, 1f, 0.001f, 0.5f).Length);
            Assert.AreEqual(0, SoundCuts.Slice(x, Rate, 5000, 0f, 1f, 0.001f, 0.5f).Length);
        }

        [Test]
        public void The_wav_has_a_valid_16_bit_mono_header()
        {
            var wav = SoundCuts.WriteWav16(new[] { 0f, 0.5f, -0.5f, 1f }, 44100);
            Assert.AreEqual(44 + 8, wav.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual(44100, BitConverter.ToInt32(wav, 24));
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 22));
            Assert.AreEqual(16, BitConverter.ToInt16(wav, 34));
            Assert.AreEqual(8, BitConverter.ToInt32(wav, 40));
            Assert.AreEqual(32767, BitConverter.ToInt16(wav, 50));
        }
    }
}

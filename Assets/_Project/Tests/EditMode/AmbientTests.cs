using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests
{
    // Which environment beds play where (#194, ADR-151) and that the synthesised clips are sound, plus the flower
    // patches of the meadows (#193).
    public class AmbientTests
    {
        static AmbientInput Out(float hour, WeatherKind weather = WeatherKind.Clear, RegionKind region = RegionKind.Town) =>
            new AmbientInput { Hour = hour, Weather = weather, Region = region };

        [Test]
        public void Day_HasBirdsAndNoCrickets_NightTheOpposite()
        {
            var day = AmbientRules.Mix(Out(12f));
            var night = AmbientRules.Mix(Out(23.5f));
            Assert.Greater(day.BirdsPerMinute, 3f);
            Assert.AreEqual(0f, day.Crickets, 1e-4f);
            Assert.AreEqual(0f, night.BirdsPerMinute, 1e-4f);
            Assert.Greater(night.Crickets, 0.15f);
        }

        [Test]
        public void Fog_QuietsBirds_AndRaisesWindAndSway()
        {
            var clear = AmbientRules.Mix(Out(12f, WeatherKind.Clear, RegionKind.Mountain));
            var fog = AmbientRules.Mix(Out(12f, WeatherKind.Fog, RegionKind.Mountain));
            Assert.Less(fog.BirdsPerMinute, clear.BirdsPerMinute);
            Assert.Greater(fog.Wind, clear.Wind);
            Assert.Greater(fog.WindBoost, clear.WindBoost);
        }

        [Test]
        public void Indoors_CutsWindBirdsAndCrickets_AndAddsRoomTone()
        {
            var open = AmbientRules.Mix(Out(12f));
            var inside = AmbientRules.Mix(new AmbientInput { Hour = 12f, Region = RegionKind.Town, Indoors = true });
            Assert.Less(inside.Wind, open.Wind * 0.3f);
            Assert.AreEqual(0f, inside.BirdsPerMinute);
            Assert.Greater(inside.RoomTone, 0.3f);
            Assert.AreEqual(0f, open.RoomTone);
        }

        [Test]
        public void Shore_BringsTheWater_AndTheMountainIsWindiest()
        {
            Assert.AreEqual(0f, AmbientRules.Mix(Out(12f)).Water);
            Assert.Greater(AmbientRules.Mix(new AmbientInput { Hour = 12f, Shore = 1f }).Water, 0.4f);
            Assert.Greater(AmbientRules.Mix(Out(12f, region: RegionKind.Mountain)).Wind, AmbientRules.Mix(Out(12f, region: RegionKind.Factory)).Wind);
        }

        [Test]
        public void Gunfire_DucksEveryBed_ButNeverToSilence()
        {
            var quiet = AmbientRules.Mix(new AmbientInput { Hour = 12f, Region = RegionKind.Mountain, Duck = 0f });
            var loud = AmbientRules.Mix(new AmbientInput { Hour = 12f, Region = RegionKind.Mountain, Duck = 1f });
            Assert.AreEqual(quiet.Wind * (1f - AmbientRules.MaxDuck), loud.Wind, 1e-4f);
            Assert.Greater(loud.Wind, 0f);
            Assert.Less(loud.BirdVolume, quiet.BirdVolume);
        }

        [Test]
        public void Day_FadesThroughDawnAndDusk()
        {
            Assert.AreEqual(0f, AmbientRules.Day(3f));
            Assert.AreEqual(1f, AmbientRules.Day(12f));
            Assert.That(AmbientRules.Day(6.5f), Is.InRange(0.2f, 0.8f));
            Assert.AreEqual(0f, AmbientRules.Day(22f));
        }

        [Test]
        public void SynthesisedClips_AreAudible_Bounded_AndLoopWithoutAJump()
        {
            foreach (var (name, data) in new[] { ("wind", AmbientBeds.SynthWind()), ("crickets", AmbientBeds.SynthCrickets()), ("water", AmbientBeds.SynthWater()), ("room", AmbientBeds.SynthRoom()) })
            {
                Assert.IsTrue(data.All(v => !float.IsNaN(v) && System.Math.Abs(v) <= 1f), name + " has samples out of range");
                float rms = (float)System.Math.Sqrt(data.Sum(v => v * v) / data.Length);
                Assert.Greater(rms, 0.02f, name + " is silent");
                // The step from the last sample to the first is no bigger than the loudest step inside the clip.
                float seam = System.Math.Abs(data[0] - data[data.Length - 1]);
                float maxStep = 0f;
                for (int i = 1; i < data.Length; i++) maxStep = System.Math.Max(maxStep, System.Math.Abs(data[i] - data[i - 1]));
                Assert.LessOrEqual(seam, maxStep + 1e-4f, name + " has a seam");
                WriteWav("Logs/ambient/" + name + ".wav", data);
            }
        }

        // #290/#291 (ADR-540): the CC0 recordings cut by tools/sound_ambient.json load, and the loops have no seam: the
        // step from the last sample to the first is no bigger than the typical (RMS) step inside the clip.
        const string RecordedDir = "Assets/_Project/Audio/Foley/Freesound/Resources/Sfx/";

        [Test]
        public void RecordedClips_Load()
        {
            foreach (var name in new[] { "amb_wind_loop", "amb_crickets_loop", "fire_crackle_loop", "amb_bird_01", "amb_bird_02", "amb_bird_03", "amb_bird_04",
                         "geiger_click_01", "geiger_click_02" })
                Assert.IsNotNull(UnityEngine.Resources.Load<UnityEngine.AudioClip>("Sfx/" + name), name);
            Assert.IsNotNull(UnityEngine.Resources.Load<UnityEngine.AudioClip>(ZoneBanner.SirenClip), "siren");
            Assert.IsNotNull(UnityEngine.Resources.Load<UnityEngine.AudioClip>(ZoneBanner.ChimeClip), "chime");
        }

        [Test]
        public void RecordedLoops_AreLong_AndLoopWithoutAJump()
        {
            foreach (var name in new[] { "amb_wind_loop", "amb_crickets_loop", "fire_crackle_loop" })
            {
                var bytes = System.IO.File.ReadAllBytes(RecordedDir + name + ".wav");
                int n = (bytes.Length - 44) / 2; // tools/sound_cut.py: 44-byte header, 16-bit mono 44.1 kHz
                Assert.Greater(n, 44100 * 10, name + " is shorter than 10 s");
                int S(int i) => System.BitConverter.ToInt16(bytes, 44 + 2 * i);
                double stepSq = 0;
                for (int i = 1; i < n; i++) stepSq += (double)(S(i) - S(i - 1)) * (S(i) - S(i - 1));
                double stepRms = System.Math.Sqrt(stepSq / (n - 1));
                Assert.LessOrEqual(System.Math.Abs(S(0) - S(n - 1)), stepRms, name + " has a seam");
            }
        }

        [Test]
        public void Birds_AreShortSongsOfDifferentLength()
        {
            var birds = AmbientBeds.SynthBirds();
            Assert.GreaterOrEqual(birds.Length, 4);
            foreach (var b in birds)
            {
                Assert.That(b.Length, Is.InRange(Rate(0.3f), Rate(1.5f)));
                Assert.Greater(b.Max(), 0.5f);
            }
            Assert.Greater(birds.Select(b => b.Length).Distinct().Count(), 2);
        }

        // For a person to listen to (Logs/ambient): 16-bit mono 22.05 kHz.
        static void WriteWav(string path, float[] data)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length * 2);
            foreach (float v in data) w.Write((short)(System.Math.Max(-1f, System.Math.Min(1f, v)) * 32767f));
        }

        static int Rate(float seconds) => (int)(seconds * 22050f);

        [Test]
        public void MeadowFlowers_GrowInPatches_NotEverywhere()
        {
            const int res = 64;
            var map = new byte[res * res];
            for (int i = 0; i < map.Length; i++) map[i] = Vegetation.EncodeGrass(1f, false);
            var all = new List<GrassClump>();
            var cell = new List<GrassClump>();
            for (int cz = 0; cz < 8; cz++)
                for (int cx = 0; cx < 8; cx++)
                {
                    Vegetation.ScatterGrass(map, res, 2f, cx, cz, 11, cell);
                    all.AddRange(cell);
                }
            int flowers = all.Count(c => c.Flower);
            Assert.Greater(flowers, 20, "a meadow has some flowers");
            Assert.Less(flowers, all.Count * 0.3f, "but it is mostly grass");
            Assert.IsTrue(all.Where(c => c.Flower).All(c => !c.Tall));
        }
    }
}

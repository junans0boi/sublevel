using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Cat = Game.Gameplay.SpatialAudio.Category;

namespace Game.Tests
{
    // #106 / ADR-086: every world sound is 3D with a per-category distance falloff that reaches silence.
    public class SpatialAudioTests
    {
        [TestCase(Cat.Body, 40f)]
        [TestCase(Cat.Gunshot, 750f)]
        [TestCase(Cat.Explosion, 800f)]
        [TestCase(Cat.Vehicle, 150f)]
        [TestCase(Cat.Elevator, 60f)]
        [TestCase(Cat.Impact, 60f)]
        public void Category_is_silent_from_its_max_distance(Cat category, float max)
        {
            Assert.AreEqual(max, SpatialAudio.For(category).Max);
            Assert.AreEqual(1f, SpatialAudio.Gain(category, SpatialAudio.For(category).Min));
            Assert.AreEqual(0f, SpatialAudio.Gain(category, max));
            Assert.AreEqual(0f, SpatialAudio.Gain(category, max * 3f));
        }

        [Test]
        public void Gain_falls_steadily_with_distance()
        {
            foreach (Cat category in System.Enum.GetValues(typeof(Cat)))
            {
                float last = 2f;
                for (float d = 0f; d <= SpatialAudio.For(category).Max; d += 1f)
                {
                    float g = SpatialAudio.Gain(category, d);
                    Assert.LessOrEqual(g, last, $"{category} at {d} m");
                    last = g;
                }
            }
        }

        [Test]
        public void Close_and_far_gunshots_differ_clearly()
        {
            float near = SpatialAudio.Gain(Cat.Gunshot, 5f), far = SpatialAudio.Gain(Cat.Gunshot, 200f);
            Assert.AreEqual(1f, near);
            Assert.Less(far, near * 0.1f, "200 m is at least 20 dB below 5 m");
            Assert.Greater(far, 0.01f, "a shot 200 m away is still heard");
            Assert.Greater(SpatialAudio.Gain(Cat.Gunshot, 600f), 0f, "and one 600 m away");
            Assert.AreEqual(0f, SpatialAudio.Gain(Cat.Body, 45f), "footsteps 45 m away are not heard");
            Assert.Greater(SpatialAudio.Gain(Cat.Body, 15f), 0.05f, "footsteps 15 m away are");
        }

        [Test]
        public void Far_gunshots_lose_their_highs()
        {
            Assert.AreEqual(22000f, SpatialAudio.DistantCutoff(10f), 1f);
            Assert.Less(SpatialAudio.DistantCutoff(300f), 8000f);
            Assert.AreEqual(1200f, SpatialAudio.DistantCutoff(750f), 1f);
        }

        [Test]
        public void Rolloff_curve_follows_the_gain()
        {
            foreach (Cat category in System.Enum.GetValues(typeof(Cat)))
            {
                var r = SpatialAudio.For(category);
                var curve = SpatialAudio.Curve(category);
                foreach (float d in new[] { 1f, r.Min, r.Min * 2f, r.Max * 0.25f, r.Max * 0.5f, r.Max * 0.9f, r.Max })
                    Assert.AreEqual(SpatialAudio.Gain(category, d), curve.Evaluate(d / r.Max), 0.02f, $"{category} at {d} m");
            }
        }

        [Test]
        public void Apply_makes_a_source_fully_3D_with_the_custom_falloff()
        {
            var go = new GameObject("Source");
            try
            {
                var source = SpatialAudio.Apply(go.AddComponent<AudioSource>(), Cat.Vehicle);
                Assert.AreEqual(1f, source.spatialBlend);
                Assert.AreEqual(AudioRolloffMode.Custom, source.rolloffMode);
                Assert.AreEqual(150f, source.maxDistance);
                Assert.AreEqual(0f, source.dopplerLevel);
                var curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                Assert.AreEqual(0f, curve.Evaluate(1f), 0.001f);
                Assert.Greater(curve.Evaluate(0.1f), curve.Evaluate(0.5f));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

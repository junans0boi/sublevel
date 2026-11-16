using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #247 T6 connection: RegionSpec.Style "Ridge" puts MountainTerrain under a mountain region of the generated world.
    // The default spec keeps the original radial relief (Style ""), so every other test is untouched.
    public class RidgeStyleWorldTests
    {
        static WorldPlan Make(string style)
        {
            var spec = WorldSpec.CreateDefault();
            spec.Regions[0].Style = style;
            return WorldGenerator.Generate(spec);
        }

        static List<float> Slopes(WorldPlan plan)
        {
            var region = plan.Regions[0];
            const int n = 256; const float cell = 4f;
            var h = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) h[z, x] = plan.Height(x * cell, z * cell);
            var slopes = new List<float>();
            for (int z = 1; z < n - 1; z++)
                for (int x = 1; x < n - 1; x++)
                {
                    float px = x * cell, pz = z * cell;
                    if (plan.RegionAt(px, pz) != 0 || plan.IsWater(px, pz)) continue;
                    float dx = px - region.CentreX, dz = pz - region.CentreZ;
                    if (dx * dx + dz * dz > 240f * 240f) continue; // the massif itself, not the border ridges and the shore
                    float gx = (h[z, x + 1] - h[z, x - 1]) / (2 * cell), gz = (h[z + 1, x] - h[z - 1, x]) / (2 * cell);
                    slopes.Add((float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180.0 / Math.PI));
                }
            return slopes;
        }

        [Test]
        public void The_default_spec_keeps_the_radial_mountain()
        {
            Assert.AreEqual("", WorldSpec.CreateDefault().Regions[0].Style);
        }

        [Test]
        public void A_ridge_world_passes_every_world_rule()
        {
            Assert.IsEmpty(WorldRules.Validate(Make("Ridge")));
        }

        [Test]
        public void The_ridge_mountain_is_walkable_where_the_radial_one_was_not()
        {
            var ridge = Slopes(Make("Ridge"));
            var radial = Slopes(Make(""));
            float Walkable(List<float> s) => 100f * s.Count(v => v <= 35f) / s.Count;
            float Wall(List<float> s) => 100f * s.Count(v => v > 38f) / s.Count;
            Assert.GreaterOrEqual(Walkable(ridge), 88f, "ridge: walkable share");
            Assert.LessOrEqual(Wall(ridge), 6f, "ridge: share steeper than 38 degrees");
            Assert.Greater(Walkable(ridge), Walkable(radial) + 15f, "the radial mountain was at about 66%");
        }

        [Test]
        public void A_ridge_world_is_the_same_every_time_and_differs_from_the_radial_one()
        {
            var a = Make("Ridge"); var b = Make("Ridge"); var radial = Make("");
            int differs = 0;
            for (int i = 0; i < 200; i++)
            {
                float x = 300f + (i * 37) % 450, z = 600f + (i * 61) % 380;
                Assert.AreEqual(a.Height(x, z), b.Height(x, z));
                if (Math.Abs(a.Height(x, z) - radial.Height(x, z)) > 1f) differs++;
            }
            Assert.Greater(differs, 50);
        }
    }
}

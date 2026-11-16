using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // The C# pollution core against pollution_sim.html on the same island (C2, 12 m cells, default preset): same
    // orders, same front arrival times, same match length. The fixture was exported from the simulator by
    // headless Chrome (see the header of PollutionSimFixture.txt). When the real map replaces the C2 sketch
    // (S1), keep this test on the fixture: it pins the algorithm, not the map.
    public class PollutionSimParityTests
    {
        sealed class Fixture
        {
            public PollutionGrid Grid;
            public Dictionary<int, ushort[]> Arrive = new Dictionary<int, ushort[]>();
            public Dictionary<int, (int[] order, float end)> Ends = new Dictionary<int, (int[], float)>();
        }

        static Fixture _fx;

        static string Dir([CallerFilePath] string p = "") => Path.GetDirectoryName(p);

        static Fixture Load()
        {
            if (_fx != null) return _fx;
            var fx = new Fixture();
            string reg = null, riv = null, road = null, slope = null;
            int w = 0, h = 0;
            foreach (var line in File.ReadLines(Path.Combine(Dir(), "PollutionSimFixture.txt")))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int sp = line.IndexOf(' ');
                string key = line.Substring(0, sp), rest = line.Substring(sp + 1);
                if (key == "size") { var p = rest.Split(' '); w = int.Parse(p[0]); h = int.Parse(p[1]); }
                else if (key == "reg") reg = rest;
                else if (key == "riv") riv = rest;
                else if (key == "road") road = rest;
                else if (key == "slope") slope = rest;
                else if (key.StartsWith("arrive"))
                {
                    var a = new ushort[w * h];
                    for (int i = 0; i < a.Length; i++) a[i] = ushort.Parse(rest.Substring(i * 4, 4), NumberStyles.HexNumber);
                    fx.Arrive[int.Parse(key.Substring(6))] = a;
                }
                else if (key == "end")
                {
                    var p = rest.Split(' ');
                    fx.Ends[int.Parse(p[0])] = (p[1].Select(c => c - '0').ToArray(), float.Parse(p[2], CultureInfo.InvariantCulture));
                }
            }
            var g = new PollutionGrid(w, h, 12f);
            for (int i = 0; i < w * h; i++)
            {
                g.Region[i] = reg[i] == '~' ? PollutionGrid.Sea : (byte)(reg[i] - '0');
                g.River[i] = riv[i] == '1';
                g.Road[i] = (byte)(road[i] - '0');
                g.SlopeDeg[i] = int.Parse(slope.Substring(i * 3, 3), NumberStyles.HexNumber) / 10f;
            }
            fx.Grid = g;
            return _fx = fx;
        }

        static readonly int[][] Adj = { new[] { 3, 1, 4 }, new[] { 0, 4, 2 }, new[] { 1 }, new[] { 0, 4, 5 }, new[] { 0, 3, 1, 5 }, new[] { 3, 4 } };

        [Test]
        public void Front_ArrivalTimes_MatchTheSimulator()
        {
            var fx = Load();
            foreach (int seed in new[] { 1, 2 })
            {
                var tl = new PollutionTimeline(fx.Grid, PollutionOrder.Plan(6, Adj, seed), new PollutionRules());
                var sim = fx.Arrive[seed];
                float worst = 0f; int bad = 0, compared = 0;
                for (int i = 0; i < sim.Length; i++)
                {
                    if (sim[i] == 0xFFFF) { Assert.Greater(tl.ArriveMinutes[i], 1e8f, "cell " + i + " seed " + seed); continue; }
                    float d = Math.Abs(tl.ArriveMinutes[i] * 60f - sim[i]);
                    compared++;
                    if (d > worst) worst = d;
                    if (d > 3f) bad++; // seconds: the simulator keeps 32-bit floats, the fixture rounds to a second
                }
                Assert.Greater(compared, 20000, "land and river cells compared");
                Assert.AreEqual(0, bad, "seed " + seed + ": cells off by more than 3 s (worst " + worst + " s)");
            }
        }

        [Test]
        public void MatchLength_MatchesTheSimulator_ForTheSampledSeeds()
        {
            var fx = Load();
            foreach (int seed in new[] { 1, 2, 3, 10, 25, 50, 100, 150, 200 })
            {
                var order = PollutionOrder.Plan(6, Adj, seed);
                CollectionAssert.AreEqual(fx.Ends[seed].order, order, "order, seed " + seed);
                var tl = new PollutionTimeline(fx.Grid, order, new PollutionRules());
                Assert.AreEqual(fx.Ends[seed].end, tl.EndMinutes(), 0.05f, "match length (min), seed " + seed);
            }
        }

        [Test]
        public void MatchLength_AcrossSeeds_IsTwentyToTwentyFiveMinutes_AndAveragesTheSimulatorValue()
        {
            var fx = Load();
            double sum = 0; int n = 0;
            for (int seed = 1; seed <= 200; seed += 5)
            {
                var tl = new PollutionTimeline(fx.Grid, PollutionOrder.Plan(6, Adj, seed), new PollutionRules());
                float end = tl.EndMinutes();
                Assert.That(end, Is.InRange(20f, 25f), "seed " + seed);
                sum += end; n++;
            }
            double simMean = 0; int m = 0;
            for (int seed = 1; seed <= 200; seed += 5) { simMean += fx.Ends[seed].end; m++; }
            Assert.AreEqual(simMean / m, sum / n, 0.05);
            Assert.That((float)(sum / n), Is.InRange(21.5f, 22.8f), "the 200-seed mean in the notes is 22.1");
        }
    }
}

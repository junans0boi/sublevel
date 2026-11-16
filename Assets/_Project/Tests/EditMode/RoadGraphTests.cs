using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // #248 T7 / WP3: the C2 road network of docs/bible/data/roads.json as a graph: connected, no single point of failure
    // on the paved roads, eight loops, and the known list of sections steeper than 12 degrees that the redesign must fix.
    public class RoadGraphTests
    {
        [System.Serializable] class Row { public string name, cls, sw, bridge, over; public float len, gmax; }
        [System.Serializable] class Part { public string type, title; public Row[] rows; }
        [System.Serializable] class Doc { public Part[] parts; }

        static RoadGraph Load()
        {
            string path = Path.Combine(Application.dataPath, "../docs/bible/data/roads.json");
            var doc = JsonUtility.FromJson<Doc>(File.ReadAllText(path));
            var rows = doc.parts.First(p => p.title != null && p.title.StartsWith("도로 구간")).rows;
            var graph = new RoadGraph();
            foreach (var r in rows)
            {
                var ends = r.name.Split(new[] { " - " }, System.StringSplitOptions.None);
                graph.Add(new RoadEdge
                {
                    A = ends[0].Trim(), B = ends[1].Trim(), Length = r.len, MaxGradeDeg = r.gmax,
                    Class = r.cls == "국도" ? RoadClass.Trunk : r.cls == "포장" ? RoadClass.Paved : RoadClass.Dirt,
                    Bridge = !string.IsNullOrEmpty(r.bridge) && !r.bridge.Contains("여울"), Ford = !string.IsNullOrEmpty(r.bridge) && r.bridge.Contains("여울"),
                    Switchback = !string.IsNullOrEmpty(r.sw),
                });
            }
            return graph;
        }

        static bool Paved(RoadEdge e) => e.Class != RoadClass.Dirt;

        [Test]
        public void The_network_has_22_nodes_29_roads_and_8_loops()
        {
            var g = Load();
            Assert.AreEqual(22, g.Nodes.Count);
            Assert.AreEqual(29, g.Edges.Count);
            Assert.AreEqual(8, g.Cycles());
            Assert.IsTrue(g.IsConnected());
        }

        [Test]
        public void The_paved_roads_connect_every_town_and_survive_the_loss_of_any_one_road()
        {
            var g = Load();
            Assert.IsTrue(g.IsConnected(Paved), "trunk and paved roads alone are one network");
            Assert.IsEmpty(g.Bridges(Paved), "no single paved road is a point of failure");
        }

        [Test]
        public void Only_the_three_hamlets_depend_on_dirt_tracks()
        {
            var g = Load();
            CollectionAssert.AreEquivalent(new[] { "갯말", "솔밭", "하동" }, g.OnlyOnDirt());
            // ...and of those only the coast hamlet hangs on a single track
            var all = g.Bridges().Select(e => e.ToString()).ToList();
            CollectionAssert.AreEqual(new[] { "갯말 - 교차로 (Dirt)" }, all);
        }

        [Test]
        public void Five_bridges_one_ford_and_the_known_steep_sections()
        {
            var g = Load();
            Assert.AreEqual(5, g.Edges.Count(e => e.Bridge));
            Assert.AreEqual(1, g.Edges.Count(e => e.Ford));
            // roads.json measured 9 sections over 12 degrees; the redesign (WP3) must bring this to zero
            var steep = g.Steeper(12f).Select(e => e.ToString()).OrderBy(s => s).ToList();
            Assert.AreEqual(9, steep.Count, string.Join("; ", steep));
        }

        [Test]
        public void A_loss_of_connection_is_found()
        {
            var g = new RoadGraph();
            g.Add(new RoadEdge { A = "a", B = "b" });
            g.Add(new RoadEdge { A = "b", B = "c" });
            g.Add(new RoadEdge { A = "x", B = "y" });
            Assert.IsFalse(g.IsConnected());
            Assert.AreEqual(2, g.Components());
            Assert.AreEqual(3, g.Bridges().Count, "every edge of a forest is a bridge");
            g.Add(new RoadEdge { A = "c", B = "a" });
            Assert.AreEqual(1, g.Cycles());
            Assert.AreEqual(1, g.Bridges().Count, "only x - y remains a bridge");
        }

        [Test]
        public void A_road_joins_two_different_named_nodes()
        {
            var g = new RoadGraph();
            Assert.Throws<System.ArgumentException>(() => g.Add(new RoadEdge { A = "a", B = "a" }));
            Assert.Throws<System.ArgumentException>(() => g.Add(new RoadEdge { A = "", B = "b" }));
        }

        [Test]
        public void Grade_is_measured_over_a_30_metre_window()
        {
            // flat, then a 20% climb (11.3 degrees) over 40 m, then flat
            var d = Enumerable.Range(0, 21).Select(i => i * 10f).ToList();
            var h = d.Select(x => x < 50f ? 0f : x < 90f ? (x - 50f) * 0.2f : 8f).ToList();
            Assert.AreEqual(11.3f, RoadGraph.MaxGradeDeg(d, h), 0.3f);
            Assert.AreEqual(0f, RoadGraph.MaxGradeDeg(d, d.Select(_ => 5f).ToList()));
            Assert.Throws<System.ArgumentException>(() => RoadGraph.MaxGradeDeg(d, h.Take(5).ToList()));
        }
    }
}

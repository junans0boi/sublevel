using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    public enum RoadClass { Trunk, Paved, Dirt }

    // One road section between two junctions or towns (docs/bible/data/roads.json, ADR-168).
    public sealed class RoadEdge
    {
        public string A, B;
        public RoadClass Class;
        public float Length;            // m
        public float MaxGradeDeg;       // steepest 30 m window
        public bool Bridge, Ford, Switchback;
        public override string ToString() => $"{A} - {B} ({Class})";
    }

    // The road network as a graph of towns, junctions and passes (WP3, #248). Pure data and graph questions: is everything
    // connected, does it survive the loss of any one road, how many loops, which sections are too steep.
    public sealed class RoadGraph
    {
        readonly List<RoadEdge> edges = new List<RoadEdge>();
        readonly HashSet<string> nodes = new HashSet<string>();

        public IReadOnlyList<RoadEdge> Edges => edges;
        public IReadOnlyCollection<string> Nodes => nodes;

        public void Add(RoadEdge edge)
        {
            if (edge == null || string.IsNullOrEmpty(edge.A) || string.IsNullOrEmpty(edge.B) || edge.A == edge.B)
                throw new ArgumentException("a road joins two different named nodes");
            edges.Add(edge);
            nodes.Add(edge.A); nodes.Add(edge.B);
        }

        // Connected components of the graph made of the edges `include` accepts, over the nodes those edges touch.
        public int Components(Func<RoadEdge, bool> include = null) => Components(include, null);

        int Components(Func<RoadEdge, bool> include, RoadEdge without)
        {
            var all = edges.Where(e => include == null || include(e)).ToList();   // the nodes count even when the edge that reached them is gone
            var parent = new Dictionary<string, string>();
            string Find(string x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var e in all) { parent[e.A] = e.A; parent[e.B] = e.B; }
            foreach (var e in all) if (e != without) parent[Find(e.A)] = Find(e.B);
            // Find compresses paths (writes to `parent`), so walk a copy of the keys: Mono throws if the dictionary changes while it is enumerated.
            return parent.Keys.ToList().Select(Find).Distinct().Count();
        }

        public bool IsConnected(Func<RoadEdge, bool> include = null) => Components(include) <= 1;

        // Roads whose loss splits what `include` accepts into two or more parts.
        public List<RoadEdge> Bridges(Func<RoadEdge, bool> include = null)
        {
            int baseline = Components(include);
            return edges.Where(e => (include == null || include(e)) && Components(include, e) > baseline).ToList();
        }

        // Independent loops: edges - nodes + components (the cycle rank).
        public int Cycles(Func<RoadEdge, bool> include = null)
        {
            var use = edges.Where(e => include == null || include(e)).ToList();
            var touched = new HashSet<string>(use.SelectMany(e => new[] { e.A, e.B }));
            return use.Count - touched.Count + Components(include);
        }

        public IEnumerable<RoadEdge> Steeper(float maxGradeDeg) => edges.Where(e => e.MaxGradeDeg > maxGradeDeg);

        // Nodes that only dirt tracks reach (hamlets), given the paved network.
        public List<string> OnlyOnDirt()
        {
            var paved = new HashSet<string>(edges.Where(e => e.Class != RoadClass.Dirt).SelectMany(e => new[] { e.A, e.B }));
            return nodes.Where(n => !paved.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        // Steepest grade in degrees over any `window` metres along a profile (distance along the road, height), as the design sheet measures it.
        public static float MaxGradeDeg(IReadOnlyList<float> distance, IReadOnlyList<float> height, float window = 30f)
        {
            if (distance == null || height == null || distance.Count != height.Count) throw new ArgumentException("one height per distance");
            float best = 0f;
            for (int i = 0; i < distance.Count; i++)
                for (int j = i + 1; j < distance.Count; j++)
                {
                    float run = distance[j] - distance[i];
                    if (run < window * 0.5f) continue;
                    float grade = (float)(Math.Atan(Math.Abs(height[j] - height[i]) / run) * 180.0 / Math.PI);
                    if (grade > best) best = grade;
                    if (run > window) break;
                }
            return best;
        }
    }
}

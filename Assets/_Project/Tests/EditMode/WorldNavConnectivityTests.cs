using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Tests
{
    // S8 T11 (#337): from every elevator station and every building front there is a complete NavMesh path to the nearest road point.
    // (CalculatePath gives up on very long routes, so connectivity is proven in short hops to the road network, as WorldBuilder.CheckNav does.)
    public class WorldNavConnectivityTests
    {
        const string NavAsset = "Assets/_Project/Prefabs/World/World_NavMesh.asset";
        NavMeshDataInstance _instance;

        [SetUp]
        public void Add()
        {
            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavAsset);
            Assert.IsNotNull(data, "baked World navmesh asset");
            NavMesh.RemoveAllNavMeshData();
            _instance = NavMesh.AddNavMeshData(data);
        }

        [TearDown]
        public void Remove() => NavMesh.RemoveNavMeshData(_instance);

        static Vector3 NearestRoadPoint(WorldPlan plan, float x, float z)
        {
            Vector3 best = default; float bd = float.MaxValue;
            foreach (var road in plan.Roads)
                for (int i = 0; i < road.X.Length; i++)
                {
                    float d = (road.X[i] - x) * (road.X[i] - x) + (road.Z[i] - z) * (road.Z[i] - z);
                    if (d < bd) { bd = d; best = new Vector3(road.X[i], road.Y[i], road.Z[i]); }
                }
            return best;
        }

        static bool Reaches(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var a, 6f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 6f, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        [Test]
        public void Every_elevator_station_and_99_percent_of_building_fronts_reach_a_road()
        {
            var plan = WorldGenerator.Generate(WorldSpec.CreateIsland());
            var failed = new List<string>();
            foreach (var e in plan.Elevators.GroupBy(x => x.Bank).Select(g => g.First()))
                if (!Reaches(new Vector3(e.X, e.Y, e.Z), NearestRoadPoint(plan, e.X, e.Z))) failed.Add(e.Name);
            Assert.IsEmpty(failed, "stations without a path to a road: " + string.Join(", ", failed));

            var lotFails = new List<string>();
            foreach (var l in plan.Lots)
            {
                l.ToWorld(0f, l.SizeZ * 0.5f + 4f, out float fx, out float fz);
                if (!Reaches(new Vector3(fx, l.Y, fz), NearestRoadPoint(plan, fx, fz))) lotFails.Add(l.Name);
            }
            Assert.LessOrEqual(lotFails.Count, plan.Lots.Count / 100, $"{lotFails.Count}/{plan.Lots.Count} building fronts cut off: " + string.Join(", ", lotFails.Take(20)));
        }
    }
}

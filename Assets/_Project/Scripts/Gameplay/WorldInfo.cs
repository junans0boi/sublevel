using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // What the generated outdoor map (ADR-050/052) tells the systems that run on it: regions, elevators, the passes
    // between regions, convergence points and sub-zones. Written by WorldBuilder when the scene is generated.
    public class WorldInfo : MonoBehaviour
    {
        [Serializable]
        public struct Region
        {
            public int Index;
            public string Id;           // "A".."D"
            public string Name;         // display name (Korean)
            public RegionKind Kind;
            public Vector3 Centre;      // the spec's centre, on the ground
            public Vector3 Hub;         // centre compound: every trunk road of the region starts here
        }

        [Serializable]
        public struct Elevator
        {
            public string Name;
            public int Region;          // index into Regions
            public string RegionId;
            public int SubZone;         // index into SubZones, or -1
            public Vector3 Position;    // centre of the station on the ground
            public float FacingDeg;     // the way out (0 = +Z, 90 = +X)
            public Vector3 Spawn;       // where a player appears (feet)
            public float SpawnYawDeg;
        }

        [Serializable]
        public struct Pass
        {
            public string Name;
            public int RegionA, RegionB;
            public Vector3 Position;
            public float YawDeg;        // from RegionA into RegionB
            public float Width;
        }

        [Serializable]
        public struct Point
        {
            public string Name;
            public int Region;
            public Vector3 Position;
            public float Radius;        // convergence point: open ground; sub-zone: flattened yard
        }

        [SerializeField] float _size;
        [SerializeField] float _seaLevel;
        [SerializeField] int _seed;
        [SerializeField] Region[] _regions = new Region[0];
        [SerializeField] Elevator[] _elevators = new Elevator[0];
        [SerializeField] Pass[] _passes = new Pass[0];
        [SerializeField] Point[] _convergencePoints = new Point[0];
        [SerializeField] Point[] _subZones = new Point[0];

        public static WorldInfo Current { get; private set; }

        public float Size => _size;                 // the map is the square 0..Size on X and Z
        public float SeaLevel => _seaLevel;
        public int Seed => _seed;
        public IReadOnlyList<Region> Regions => _regions;
        public IReadOnlyList<Elevator> Elevators => _elevators;
        public IReadOnlyList<Pass> Passes => _passes;
        public IReadOnlyList<Point> ConvergencePoints => _convergencePoints;
        public IReadOnlyList<Point> SubZones => _subZones;

        // Editor builder only.
        public void Fill(float size, float seaLevel, int seed, Region[] regions, Elevator[] elevators, Pass[] passes, Point[] convergencePoints, Point[] subZones)
        {
            _size = size;
            _seaLevel = seaLevel;
            _seed = seed;
            _regions = regions ?? new Region[0];
            _elevators = elevators ?? new Elevator[0];
            _passes = passes ?? new Pass[0];
            _convergencePoints = convergencePoints ?? new Point[0];
            _subZones = subZones ?? new Point[0];
        }

        public bool TryRegion(string id, out Region region)
        {
            foreach (var r in _regions)
                if (r.Id == id) { region = r; return true; }
            region = default;
            return false;
        }

        public List<Elevator> ElevatorsIn(int region)
        {
            var found = new List<Elevator>();
            foreach (var e in _elevators)
                if (e.Region == region) found.Add(e);
            return found;
        }

        public List<Point> ConvergencePointsIn(int region)
        {
            var found = new List<Point>();
            foreach (var p in _convergencePoints)
                if (p.Region == region) found.Add(p);
            return found;
        }

        // The passes that lead out of a region: what a closure has to block.
        public List<Pass> PassesOf(int region)
        {
            var found = new List<Pass>();
            foreach (var p in _passes)
                if (p.RegionA == region || p.RegionB == region) found.Add(p);
            return found;
        }

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }
    }
}

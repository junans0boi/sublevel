using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Which building each loot spawn point belongs to (ADR-291): the spawn lies inside the footprint of exactly one lot
    // (spike 2026-11-17: 0 of 1262 misplaced; by nearest centre 5 would be). Built once at server start from the world
    // plan and the spawn coordinates, no scene change. A bot picks a building, then walks its spawn list.
    public sealed class BuildingLootIndex
    {
        public const float InsideMeters = 0.05f;   // footprint distance that still counts as inside (rounding)

        readonly IReadOnlyList<BuildingLot> _lots;
        readonly List<int>[] _spawns;
        readonly List<float[]>[] _doors;
        readonly int[] _lotOf;
        readonly float[] _sx, _sy, _sz;

        public int Unassigned { get; private set; }
        public int Ambiguous { get; private set; }

        public BuildingLootIndex(IReadOnlyList<BuildingLot> lots, IReadOnlyList<float> spawnX, IReadOnlyList<float> spawnZ, IReadOnlyList<float> spawnY = null)
        {
            _lots = lots;
            _sx = new float[spawnX.Count]; _sy = new float[spawnX.Count]; _sz = new float[spawnX.Count];
            for (int i = 0; i < _sx.Length; i++) { _sx[i] = spawnX[i]; _sz[i] = spawnZ[i]; _sy[i] = spawnY != null ? spawnY[i] : 0f; }
            _spawns = new List<int>[lots.Count];
            _doors = new List<float[]>[lots.Count];
            for (int b = 0; b < lots.Count; b++) { _spawns[b] = new List<int>(); _doors[b] = new List<float[]>(); }
            _lotOf = new int[spawnX.Count];
            // Lots are far apart next to the footprint size: only lots whose bounding circle holds the point are tested.
            var grid = new PointGrid(64f);
            var cx = new float[lots.Count]; var cz = new float[lots.Count];
            float maxRadius = 0f;
            for (int b = 0; b < lots.Count; b++)
            {
                cx[b] = lots[b].X; cz[b] = lots[b].Z;
                maxRadius = Math.Max(maxRadius, 0.5f * (float)Math.Sqrt(lots[b].SizeX * lots[b].SizeX + lots[b].SizeZ * lots[b].SizeZ));
            }
            grid.Rebuild(cx, cz, lots.Count);
            var near = new List<int>();
            for (int s = 0; s < spawnX.Count; s++)
            {
                _lotOf[s] = -1;
                grid.Query(spawnX[s], spawnZ[s], maxRadius + 1f, near);
                int found = 0;
                foreach (int b in near)
                    if (lots[b].Distance(spawnX[s], spawnZ[s]) <= InsideMeters) { if (found++ == 0) { _lotOf[s] = b; _spawns[b].Add(s); } }
                if (found == 0) Unassigned++;
                else if (found > 1) Ambiguous++;
            }
        }

        public int BuildingCount => _lots.Count;
        public int SpawnCount => _sx.Length;

        public void SpawnAt(int spawn, out float x, out float y, out float z) { x = _sx[spawn]; y = _sy[spawn]; z = _sz[spawn]; }

        // The building whose footprint holds this ground point, or -1 (a scan of all lots: for statistics, not per frame).
        public int BuildingAt(float x, float z)
        {
            for (int b = 0; b < _lots.Count; b++) if (_lots[b].Distance(x, z) <= InsideMeters) return b;
            return -1;
        }

        // The building spawn `spawn` stands in, or -1.
        public int BuildingOf(int spawn) => spawn >= 0 && spawn < _lotOf.Length ? _lotOf[spawn] : -1;

        // Spawn indices of building `building`, ascending.
        public IReadOnlyList<int> SpawnsOf(int building) => _spawns[building];

        public BuildingKind KindOf(int building) => _lots[building].Kind;

        public void CentreOf(int building, out float x, out float z) { x = _lots[building].X; z = _lots[building].Z; }

        // How far (ground plane) a point is from the footprint of the building: 0 inside.
        public float DistanceTo(int building, float x, float z) => _lots[building].Distance(x, z);

        // The doors a bot can walk in by. The world plan does not hold them (the building generator does), so the
        // server fills them from the same generation the door system uses.
        public void AddDoor(int building, float x, float y, float z) => _doors[building].Add(new[] { x, y, z });

        public int DoorCount(int building) => _doors[building].Count;

        public void DoorAt(int building, int index, out float x, out float y, out float z)
        {
            var d = _doors[building][index];
            x = d[0]; y = d[1]; z = d[2];
        }
    }
}

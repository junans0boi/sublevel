using System.Collections.Generic;
using Game.Core;
using UnityEngine;

// The race circuit of the waiting island (ADR-505, #438): the pit behind the gate in the island's east wall and the sealed hall east of it,
// dressed like a real circuit: tarmac with white edge lines, red/white kerbs in every corner, grass run-off along the straights and gravel
// traps outside the corners, tyre walls in front of the corner barriers, a start-light gantry, grid slots, a grandstand and braking boards.
// Procedural boxes and cylinders from the shared materials (no new assets). Positions come from Core/RaceTrack so the server and the tests
// agree with the builder. Children of the island root: island-local metres, y = 0 at the deck.
public static partial class WorldBuilder
{
    // URP lights an object with at most 4 additional lights: the long pieces are cut into 40 m cells, each a mesh of its own.
    sealed class RaceTiles
    {
        const float Cell = 40f;
        readonly Dictionary<(int, int), WorldMeshes.BoxBatch> _cells = new Dictionary<(int, int), WorldMeshes.BoxBatch>();
        readonly Vector2 _tile;
        public RaceTiles(Vector2 tile) { _tile = tile; }
        WorldMeshes.BoxBatch At(Vector3 c)
        {
            var k = (Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell));
            if (!_cells.TryGetValue(k, out var b)) _cells[k] = b = new WorldMeshes.BoxBatch(_tile);
            return b;
        }
        public void Add(Vector3 c, Vector3 s) => At(c).Add(c, s);
        public void Add(Vector3 c, Vector3 s, Quaternion r) => At(c).Add(c, s, r);
        public void Cylinder(Vector3 foot, float radius, float height) => At(foot).AddCylinder(foot, radius, height, 8);
        // A flat slab over [x0, x1] x [z0, z1], top at `top`, cut along the cell lines.
        public void Slab(float x0, float x1, float z0, float z1, float top, float thickness)
        {
            for (float x = x0; x < x1 - 0.01f; x = Mathf.Min(x1, (Mathf.Floor(x / Cell) + 1f) * Cell))
                for (float z = z0; z < z1 - 0.01f; z = Mathf.Min(z1, (Mathf.Floor(z / Cell) + 1f) * Cell))
                {
                    float xe = Mathf.Min(x1, (Mathf.Floor(x / Cell) + 1f) * Cell), ze = Mathf.Min(z1, (Mathf.Floor(z / Cell) + 1f) * Cell);
                    Add(new Vector3((x + xe) * 0.5f, top - thickness * 0.5f, (z + ze) * 0.5f), new Vector3(xe - x, thickness, ze - z));
                }
        }
        public void Emit(Transform parent, WorldMeshes.Store store, string name, Material material)
        {
            int i = 0;
            foreach (var b in _cells.Values) AddWaitingMesh(parent, store, name + i++, b, material, false);
        }
    }

    // The centre line moved `offset` metres to the left of the driving direction (negative = right), one point per RaceTrack.Path sample.
    static List<Vector3> RaceLoop(float offset)
    {
        var path = RaceTrack.Path; int n = path.Count;
        var pts = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            var prev = path[(i + n - 1) % n]; var p = path[i]; var next = path[(i + 1) % n];
            var d = new Vector2(next.X - prev.X, next.Z - prev.Z).normalized;      // the tangent at the sample
            pts.Add(new Vector3(p.X - d.y * offset, 0f, p.Z + d.x * offset));       // + the left normal (-dz, dx) x offset
        }
        return pts;
    }

    static void BuildRaceTrack(Transform island, WorldMeshes.Store store)
    {
        var root = Child(island, "RaceTrack");
        var cols = Child(root, "Colliders");
        var concrete = WorldMaterials.ConcreteDark; var asphalt = WorldMaterials.Asphalt; var wallMat = WorldMaterials.Plaster(0);
        var roofMat = WorldMaterials.SteelRoof; var steel = WorldMaterials.PaintedMetal; var red = WorldMaterials.RaceRed; var white = WorldMaterials.RaceWhite;
        var navy = WorldMaterials.Accent(4); var green = WorldMaterials.RaceGrass; var gravel = WorldMaterials.DirtRoad; var rubber = WorldMaterials.Wire;
        var floor = new RaceTiles(concrete.Tile); var road = new RaceTiles(asphalt.Tile); var walls = new RaceTiles(wallMat.Tile); var roof = new RaceTiles(roofMat.Tile);
        var rails = new RaceTiles(steel.Tile); var grass = new RaceTiles(green.Tile); var traps = new RaceTiles(gravel.Tile); var tyres = new RaceTiles(rubber.Tile);
        var lines = new RaceTiles(white.Tile); var kerbRed = new RaceTiles(red.Tile); var navyBits = new RaceTiles(navy.Tile);

        float H = RaceTrack.HallHeight, x0 = RaceTrack.HallX0, x1 = RaceTrack.HallX1, z0 = RaceTrack.HallZ0, z1 = RaceTrack.HallZ1;
        float pz0 = RaceTrack.PitZ0, pz1 = RaceTrack.PitZ1, px1 = RaceTrack.PitX1;
        // The pit's side walls start at the island's wall, or at the bollard line where that is further west, so no car slips round them.
        float rim0 = Mathf.Min(RimX(pz0) - 0.5f, RaceTrack.BollardX), rim1 = Mathf.Min(RimX(pz1) - 0.5f, RaceTrack.BollardX), rimMin = Mathf.Min(rim0, rim1);

        // Floor: the pit (from the island's rim) and the hall, top at the deck; plain box colliders.
        floor.Slab(rimMin, x0, pz0, pz1, -0.005f, WaitingDeckThickness);   // a hair under the island's deck where they overlap
        floor.Slab(x0, x1, z0, z1, 0f, WaitingDeckThickness);
        RaceCollider(cols, new Vector3((rimMin + x0) * 0.5f, -0.5f, (pz0 + pz1) * 0.5f), new Vector3(x0 - rimMin, 1f, pz1 - pz0), Quaternion.identity);
        RaceCollider(cols, new Vector3((x0 + x1) * 0.5f, -0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 1f, z1 - z0), Quaternion.identity);

        // Walls and roof: the hall is sealed like the island (the sea and the fog stay outside). The west wall leaves the pit open; the pit's
        // side walls are full height between the island and the hall, a 3 m pit wall inside the hall.
        float wt = 0.5f;
        RaceWall(walls, cols, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), H, wt);
        RaceWall(walls, cols, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), H, wt);
        RaceWall(walls, cols, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), H, wt);
        RaceWall(walls, cols, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, pz0), H, wt);
        RaceWall(walls, cols, new Vector3(x0, 0f, pz1), new Vector3(x0, 0f, z1), H, wt);
        RaceWall(walls, cols, new Vector3(rim0, 0f, pz0), new Vector3(x0, 0f, pz0), H, wt);
        RaceWall(walls, cols, new Vector3(rim1, 0f, pz1), new Vector3(x0, 0f, pz1), H, wt);
        RaceWall(walls, cols, new Vector3(x0, 0f, pz0), new Vector3(px1, 0f, pz0), 3f, wt);
        RaceWall(walls, cols, new Vector3(x0, 0f, pz1), new Vector3(px1, 0f, pz1), 3f, wt);
        roof.Slab(rimMin, x0, pz0 - wt, pz1 + wt, H + 0.4f, 0.4f);
        roof.Slab(x0, x1, z0, z1, H + 0.4f, 0.4f);
        for (float x = x0 + 12f; x < x1; x += 12f) rails.Add(new Vector3(x, H - 0.3f, (z0 + z1) * 0.5f), new Vector3(0.4f, 0.6f, z1 - z0));   // roof beams

        // The track, segment by segment along the centre line.
        var path = RaceTrack.Path; int n = path.Count;
        float half = RaceTrack.Width * 0.5f, edge = half + RaceTrack.Runoff;
        var mid = RaceLoop(0f); var left = RaceLoop(edge + 0.25f); var right = RaceLoop(-edge - 0.25f);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 a = mid[i], b = mid[j], m = (a + b) * 0.5f;
            var yaw = Yaw(a, b); float len = Vector3.Distance(a, b);
            bool arc = path[i].Radius > 0f && path[j].Radius > 0f; int turn = arc ? path[i].Turn : 0;
            var side = -Vector3.Cross(Vector3.up, (b - a).normalized);       // the left of the driving direction, as RaceTrack's offsets
            // On a bend the outer edge is longer than the centre line: stretch the pieces so they meet there (they overlap on the inside;
            // alternate heights keep the overlaps from flickering).
            float stretch = arc ? 1f + edge / path[i].Radius : 1f;
            road.Add(m + Vector3.up * (0.02f + (i & 1) * 0.004f), new Vector3(RaceTrack.Width + 0.4f, 0.04f, len * (arc ? 1f + half / path[i].Radius : 1f) + 0.25f), yaw);
            foreach (int sgn in new[] { 1, -1 })
            {
                // white edge line; red/white kerbs in the corners; run-off: gravel outside a corner, grass elsewhere
                lines.Add(m + side * sgn * (half - 0.35f) + Vector3.up * (0.047f + (i & 1) * 0.002f), new Vector3(0.2f, 0.012f, len + 0.3f), yaw);
                if (arc) (i % 2 == 0 ? kerbRed : lines).Add(m + side * sgn * (half - 0.6f) + Vector3.up * 0.07f, new Vector3(1.2f, 0.06f, len + 0.05f), yaw);
                bool outside = arc && sgn == -turn;
                (outside ? traps : grass).Add(m + side * sgn * (half + RaceTrack.Runoff * 0.5f + 0.1f) + Vector3.up * (0.012f + (i & 1) * 0.002f),
                    new Vector3(RaceTrack.Runoff + 0.2f, 0.024f, len * stretch + 0.2f), yaw);
            }
            // Barriers both sides (the main straight's west one is open beside the pit); outside a corner a tyre wall stands in front of it,
            // and the corner's collider covers the tyres too.
            for (int k = 0; k < 2; k++)
            {
                var ea = k == 0 ? left[i] : right[i]; var eb = k == 0 ? left[j] : right[j]; var outward = k == 0 ? side : -side;
                if (ea.x > px1 - 1f && ea.x < px1 + 2f && Mathf.Abs(ea.x - eb.x) < 0.01f && Mathf.Max(ea.z, eb.z) > pz0 && Mathf.Min(ea.z, eb.z) < pz1)
                {
                    // the pit opening: only the parts of this piece north and south of it
                    float hi = Mathf.Max(ea.z, eb.z), lo = Mathf.Min(ea.z, eb.z);
                    if (hi - pz1 > 0.1f) RaceBarrier(rails, cols, new Vector3(ea.x, 0f, hi), new Vector3(ea.x, 0f, pz1), 0f, outward);
                    if (pz0 - lo > 0.1f) RaceBarrier(rails, cols, new Vector3(ea.x, 0f, pz0), new Vector3(ea.x, 0f, lo), 0f, outward);
                    continue;
                }
                bool tyreSide = arc && (k == 0 ? 1 : -1) == -turn;
                RaceBarrier(rails, cols, ea, eb, tyreSide ? 0.9f : 0f, outward);
                if (tyreSide) tyres.Cylinder((ea + eb) * 0.5f - outward * 0.65f, 0.38f, 0.95f);
            }
        }

        // Start/finish: a chequered band across the main straight and a start-light gantry over it (five red lights); staggered grid slots behind.
        float mx = RaceTrack.MainX, lz = RaceTrack.LineZ;
        for (int c = 0; c < 12; c++)
            for (int r = 0; r < 2; r++)
                ((c + r) % 2 == 0 ? lines : navyBits).Add(new Vector3(mx - 5.5f + c, 0.055f, lz - 0.5f + r), new Vector3(1f, 0.02f, 1f));
        foreach (float gx in new[] { mx - half - 2.5f, mx + half + 2.5f })
        {
            rails.Add(new Vector3(gx, 3.5f, lz), new Vector3(0.5f, 7f, 0.5f));
            RaceCollider(cols, new Vector3(gx, 3.5f, lz), new Vector3(0.5f, 7f, 0.5f), Quaternion.identity);
        }
        rails.Add(new Vector3(mx, 6.8f, lz), new Vector3(RaceTrack.Width + 5.5f, 0.6f, 0.6f));
        for (int l = 0; l < 5; l++) kerbRed.Add(new Vector3(mx - 3.2f + l * 1.6f, 6.1f, lz), new Vector3(0.8f, 0.8f, 0.3f));
        for (int g = 0; g < 6; g++)
        {
            float gz = lz + 8f + g * 8f, gx = mx + (g % 2 == 0 ? -2.8f : 2.8f);
            lines.Add(new Vector3(gx, 0.055f, gz), new Vector3(3f, 0.012f, 0.18f));
            lines.Add(new Vector3(gx - 1.45f, 0.055f, gz + 0.6f), new Vector3(0.12f, 0.012f, 1.2f)); lines.Add(new Vector3(gx + 1.45f, 0.055f, gz + 0.6f), new Vector3(0.12f, 0.012f, 1.2f));
        }

        // A grandstand between the hall's west wall and the main straight, north of the pit: stepped rows of seats facing the track.
        float standFront = mx - edge - 0.25f - 2.4f;
        for (int row = 0; row < 6; row++)
        {
            float sx = standFront - 0.65f - row * 1.3f, top = 0.5f + row * 0.6f;
            if (sx - 0.65f < x0 + 0.3f) break;
            floor.Add(new Vector3(sx, top * 0.5f, -72f), new Vector3(1.3f, top, 70f));
            navyBits.Add(new Vector3(sx + 0.2f, top + 0.22f, -72f), new Vector3(0.5f, 0.44f, 70f));
        }
        // Braking boards before the hairpin (3, 2, 1 stripes), behind the barrier on the outside of the main straight.
        for (int k = 3; k >= 1; k--)
        {
            float bz = -112f + 22f * k, bx = mx - edge - 1.2f;
            rails.Add(new Vector3(bx, 0.9f, bz), new Vector3(0.12f, 1.8f, 0.12f));
            lines.Add(new Vector3(bx + 0.08f, 2.2f, bz), new Vector3(0.06f, 1.2f, 1.6f));
            for (int st = 0; st < k; st++) navyBits.Add(new Vector3(bx + 0.12f, 2.2f, bz - 0.5f + st * 0.5f), new Vector3(0.04f, 0.9f, 0.18f));
        }

        // The pit: grid boxes where the cars stand, and the bollards between it and the island (people pass between the posts, cars cannot, ADR-508).
        foreach (var (gx, gz, _) in RaceTrack.GridSpots())
        {
            lines.Add(new Vector3(gx, 0.012f, gz - 1.15f), new Vector3(5f, 0.02f, 0.12f)); lines.Add(new Vector3(gx, 0.012f, gz + 1.15f), new Vector3(5f, 0.02f, 0.12f));
            lines.Add(new Vector3(gx - 2.5f, 0.012f, gz), new Vector3(0.12f, 0.02f, 2.3f));
        }
        for (float z = pz0 + 0.6f; z <= pz1 - 0.5f; z += 1.25f)
        {
            rails.Add(new Vector3(RaceTrack.BollardX, 0.5f, z), new Vector3(0.22f, 1f, 0.22f));
            kerbRed.Add(new Vector3(RaceTrack.BollardX, 0.85f, z), new Vector3(0.24f, 0.12f, 0.24f));
            RaceCollider(cols, new Vector3(RaceTrack.BollardX, 0.5f, z), new Vector3(0.22f, 1f, 0.22f), Quaternion.identity);
        }

        floor.Emit(root, store, "Floor", concrete.Material); road.Emit(root, store, "Road", asphalt.Material); walls.Emit(root, store, "Wall", wallMat.Material);
        roof.Emit(root, store, "Roof", roofMat.Material); rails.Emit(root, store, "Rail", steel.Material); grass.Emit(root, store, "Grass", green.Material);
        traps.Emit(root, store, "Gravel", gravel.Material); tyres.Emit(root, store, "Tyres", rubber.Material); lines.Emit(root, store, "Paint", white.Material);
        kerbRed.Emit(root, store, "Red", red.Material); navyBits.Emit(root, store, "Navy", navy.Material);

        // Lamps over the track about every 45 m of it, one over the pit (no shadows, like the island's).
        var lamps = new List<Vector3> { new Vector3(44f, 12f, (pz0 + pz1) * 0.5f) };
        float along = 0f, next = 0f;
        for (int i = 0; i < n; i++)
        {
            if (along >= next) { lamps.Add(new Vector3(path[i].X, 15f, path[i].Z)); next = along + 45f; }
            along += Vector3.Distance(mid[i], mid[(i + 1) % n]);
        }
        for (int i = 0; i < lamps.Count; i++)
        {
            var go = new GameObject("RaceLamp" + i); go.transform.SetParent(root, false); go.transform.localPosition = lamps[i];
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 36f; l.intensity = i == 0 ? 60f : 130f; l.shadows = LightShadows.None;
            l.color = i % 2 == 0 ? new Color(1f, 0.9f, 0.75f) : new Color(0.85f, 0.92f, 1f);
        }
        Debug.Log($"[WorldBuilder] race circuit: lap {RaceTrack.LapLength:F0} m, {n} samples, {RaceTrack.Cars} grid spots, {cols.childCount} colliders, {lamps.Count} lamps");
    }

    // The island wall's inner face at local z (the pit's side walls start there).
    static float RimX(float z) => Mathf.Sqrt(Mathf.Max(0f, WaitingIsland.Radius * WaitingIsland.Radius - z * z));

    // Is the island's wall cut here (the gate to the pit, ADR-505): segment and band pieces whose middle is in the pit's z range on the east side.
    static bool InRaceGate(float lx, float lz, float inset = 0f) => lx > 0f && lz > RaceTrack.PitZ0 + inset && lz < RaceTrack.PitZ1 - inset;

    static Quaternion Yaw(Vector3 a, Vector3 b) { var d = b - a; return Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f); }

    static void RaceWall(RaceTiles walls, Transform cols, Vector3 a, Vector3 b, float height, float thickness)
    {
        float len = Vector3.Distance(a, b); if (len < 0.05f) return;
        int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 40f));
        for (int i = 0; i < pieces; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)pieces), q = Vector3.Lerp(a, b, (i + 1) / (float)pieces);
            walls.Add((p + q) * 0.5f + Vector3.up * height * 0.5f, new Vector3(thickness, height, Vector3.Distance(p, q) + thickness), Yaw(p, q));
        }
        RaceCollider(cols, (a + b) * 0.5f + Vector3.up * height * 0.5f, new Vector3(thickness, height, len + thickness), Yaw(a, b));
    }

    // A 1 m steel barrier from a to b; `inset` widens its collider towards the track by that much (over a tyre wall), `outward` points away from the track.
    static void RaceBarrier(RaceTiles rails, Transform cols, Vector3 a, Vector3 b, float inset, Vector3 outward)
    {
        var size = new Vector3(0.5f, 1f, Vector3.Distance(a, b) + 0.12f);
        rails.Add((a + b) * 0.5f + Vector3.up * 0.5f, size, Yaw(a, b));
        RaceCollider(cols, (a + b) * 0.5f - outward * inset * 0.5f + Vector3.up * 0.5f, new Vector3(0.5f + inset, 1f, size.z), Yaw(a, b));
    }

    static void RaceCollider(Transform parent, Vector3 centre, Vector3 size, Quaternion rotation)
    {
        var go = new GameObject("Box"); go.transform.SetParent(parent, false); go.transform.localPosition = centre; go.transform.localRotation = rotation;
        go.AddComponent<BoxCollider>().size = size; go.isStatic = true;
    }
}

using Game.Core;
using UnityEngine;

// The waiting island (ADR-381, #414): a sealed 80 m deck far out in the sea of the World scene. Everything is procedural boxes
// and cylinders from the shared materials (no new assets). Positions come from Core/WaitingIsland so the server and the tests
// agree with the builder. Frame: the island's centre at WaitingIsland.CentreX/Z on the sea plane.
public static partial class WorldBuilder
{
    // URP lights an object with at most 4 additional lights, so a hall-sized mesh would only ever see the 4 nearest lamps to its centre:
    // the big pieces are cut into angular sectors, each a mesh of its own that the lamps near it can light.
    sealed class WaitingSectors
    {
        public const int Count = 8;
        public readonly WorldMeshes.BoxBatch[] Batches = new WorldMeshes.BoxBatch[Count];
        public WaitingSectors(Vector2 tile) { for (int i = 0; i < Count; i++) Batches[i] = new WorldMeshes.BoxBatch(tile); }
        WorldMeshes.BoxBatch Pick(Vector3 c) { float a = Mathf.Atan2(c.z, c.x); if (a < 0f) a += Mathf.PI * 2f; return Batches[Mathf.Min(Count - 1, (int)(a / (Mathf.PI * 2f) * Count))]; }
        public void Add(Vector3 c, Vector3 size) => Pick(c).Add(c, size);
        public void Add(Vector3 c, Vector3 size, Quaternion r) => Pick(c).Add(c, size, r);
        public void AddCylinder(Vector3 foot, float radius, float height, int sides) => Batches[0].AddCylinder(foot, radius, height, sides);
        public int Boxes { get { int n = 0; foreach (var b in Batches) n += b.Boxes; return n; } }
    }

    const float WaitingWallHeight = 20f, WaitingDeckThickness = 1.2f;
    const float WaitingDomeRise = 60f;   // ADR-387: the dome clears the 60 m bungee tower by ~8 m above it, the apex is 80 m
    const int WaitingWallSegments = 40, WaitingDomeRings = 6;

    static void BuildWaitingIsland(WorldPlan plan, Transform parent, WorldMeshes.Store store)
    {
        var root = Child(parent, "WaitingIsland");
        float cx = WaitingIsland.CentreX, cz = WaitingIsland.CentreZ;
        float deckY = plan.SeaLevel + WaitingIsland.FloorOffset;      // top of the deck
        root.position = new Vector3(cx, deckY, cz);                    // children are in island-local metres, y = 0 at the deck

        var concrete = WorldMaterials.ConcreteDark; var steel = WorldMaterials.PaintedMetal; var rust = WorldMaterials.Plaster(0);   // 'rust' = the wall panels: a light plaster so the hall is not black
        var planks = WorldMaterials.PlanksDark; var accent = WorldMaterials.Accent(1); var navy = WorldMaterials.Accent(4); var gold = WorldMaterials.Accent(2);
        var deck = new WorldMeshes.BoxBatch(concrete.Tile); var frame = new WaitingSectors(steel.Tile);
        var wall = new WaitingSectors(rust.Tile); var dome = new WaitingSectors(WorldMaterials.SteelRoof.Tile); var wood = new WorldMeshes.BoxBatch(planks.Tile); var trim = new WorldMeshes.BoxBatch(accent.Tile); var board = new WorldMeshes.BoxBatch(navy.Tile); var band = new WorldMeshes.BoxBatch(gold.Tile);

        float R = WaitingIsland.Radius;
        // Deck: a 24-gon disc of slabs + a skirt down into the sea, so no water shows through.
        deck.AddCylinder(new Vector3(0f, -WaitingDeckThickness, 0f), R, WaitingDeckThickness, 48);
        deck.AddCylinder(new Vector3(0f, -WaitingDeckThickness - 6f, 0f), R - 1f, 6f, 48);
        // A walkway ring of planks near the rim and a plank apron in front of the lift.
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f, r = WaitingIsland.WalkRadius + 1.6f;
            wood.Add(new Vector3(Mathf.Cos(a) * r, 0.03f, Mathf.Sin(a) * r), new Vector3(2.6f, 0.06f, 5.2f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
        }
        var el = WaitingIsland.Elevator;
        wood.Add(new Vector3(el.x, 0.03f, el.z - 2f), new Vector3(10f, 0.06f, 8f));

        // Wall: a ring of tall panels all round (the sea and the fog stay outside) with steel columns between them.
        for (int i = 0; i < WaitingWallSegments; i++)
        {
            float a0 = i * Mathf.PI * 2f / WaitingWallSegments, a1 = (i + 1) * Mathf.PI * 2f / WaitingWallSegments, am = (a0 + a1) * 0.5f;
            float chord = 2f * R * Mathf.Sin((a1 - a0) * 0.5f) + 0.2f;
            var rot = Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f);
            wall.Add(new Vector3(Mathf.Cos(am) * (R - 0.3f), WaitingWallHeight * 0.5f, Mathf.Sin(am) * (R - 0.3f)), new Vector3(chord, WaitingWallHeight, 0.4f), rot);
            frame.Add(new Vector3(Mathf.Cos(a0) * (R - 0.5f), WaitingWallHeight * 0.5f, Mathf.Sin(a0) * (R - 0.5f)), new Vector3(0.6f, WaitingWallHeight, 0.6f));
        }
        // An amber band round the hall at chest height: the one bright colour of the room besides the lamps.
        for (int i = 0; i < WaitingWallSegments; i++)
        {
            float am = (i + 0.5f) * Mathf.PI * 2f / WaitingWallSegments, r = R - 0.65f, chord = 2f * R * Mathf.Sin(Mathf.PI / WaitingWallSegments) + 0.1f;
            band.Add(new Vector3(Mathf.Cos(am) * r, 1.5f, Mathf.Sin(am) * r), new Vector3(chord, 0.7f, 0.1f), Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f));
        }
        // A band of trim lights round the wall at head height.
        for (int i = 0; i < WaitingWallSegments; i++)
        {
            float am = (i + 0.5f) * Mathf.PI * 2f / WaitingWallSegments, r = R - 0.7f;
            trim.Add(new Vector3(Mathf.Cos(am) * r, 3.2f, Mathf.Sin(am) * r), new Vector3(2.8f, 0.25f, 0.15f), Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f));
        }
        // Dome: rings of tilted panels closing the top, a steel hoop under each ring.
        for (int ring = 0; ring < WaitingDomeRings; ring++)
        {
            float t0 = ring / (float)WaitingDomeRings, t1 = (ring + 1) / (float)WaitingDomeRings;
            float p0 = t0 * Mathf.PI * 0.5f, p1 = t1 * Mathf.PI * 0.5f, pm = (p0 + p1) * 0.5f;
            float rr0 = R * Mathf.Cos(p0), rr1 = R * Mathf.Cos(p1), rm = (rr0 + rr1) * 0.5f;
            float h0 = WaitingWallHeight + WaitingDomeRise * Mathf.Sin(p0), h1 = WaitingWallHeight + WaitingDomeRise * Mathf.Sin(p1), hm = (h0 + h1) * 0.5f;
            float slope = Mathf.Atan2(h1 - h0, rr0 - rr1) * Mathf.Rad2Deg;      // panel rises towards the middle
            int n = WaitingWallSegments;   // the same number on every ring; the inner ones overlap, so no gap shows between panels
            for (int i = 0; i < n; i++)
            {
                float am = (i + 0.5f) * Mathf.PI * 2f / n;
                float chord = 2f * rr0 * Mathf.Sin(Mathf.PI / n) + 0.5f, len = Vector2.Distance(new Vector2(rr0, h0), new Vector2(rr1, h1)) + 0.3f;
                var rot = Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f) * Quaternion.Euler(slope, 0f, 0f);   // outwards goes down: the panel rises towards the middle
                dome.Add(new Vector3(Mathf.Cos(am) * rm, hm, Mathf.Sin(am) * rm), new Vector3(chord, 0.35f, len + 0.4f), rot);
            }
            // A steel hoop under the ring's upper edge: a ring of short beams (a capped cylinder here was a solid disc over the hall).
            if (rr1 > 1f)
                for (int i = 0; i < n; i++)
                {
                    float am = (i + 0.5f) * Mathf.PI * 2f / n, chord = 2f * rr1 * Mathf.Sin(Mathf.PI / n) + 0.1f;
                    frame.Add(new Vector3(Mathf.Cos(am) * (rr1 - 0.3f), h1 - 0.35f, Mathf.Sin(am) * (rr1 - 0.3f)), new Vector3(chord, 0.3f, 0.3f), Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f));
                }
        }

        // Bungee tower (ADR-386/387): four legs and cross bracing up to a railed platform at BungeeCurve.Top, an open steel-cage lift
        // on its north side (exposed shaft, the car is its own object the clients move), and a railed bridge west to the jump board,
        // which hangs right over the landing pad. Colliders are plain boxes (the blob meshes cannot make mesh colliders).
        var t = WaitingIsland.Tower; float tr = 3.2f, th = BungeeCurve.Top, half = WaitingIsland.TopHalf;
        var cols = Child(root, "BungeeColliders");
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -tr : tr, sz = (i & 2) == 0 ? -tr : tr;
            frame.Add(new Vector3(t.x + sx, th * 0.5f, t.z + sz), new Vector3(0.6f, th, 0.6f));
            WaitingCollider(cols, new Vector3(t.x + sx, th * 0.5f, t.z + sz), new Vector3(0.6f, th, 0.6f));
        }
        for (float y = 3f; y < th - 1f; y += 5f)
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2; float s = (side & 1) == 0 ? -tr : tr;
                frame.Add(alongX ? new Vector3(t.x, y, t.z + s) : new Vector3(t.x + s, y, t.z), alongX ? new Vector3(tr * 2f, 0.25f, 0.25f) : new Vector3(0.25f, 0.25f, tr * 2f));
                // a diagonal in each bay below the platform: the tower reads as a lattice from below
                if (y + 5f > th - 0.5f) continue;
                float a = Mathf.Atan2(5f, tr * 2f) * Mathf.Rad2Deg, len = Mathf.Sqrt(25f + tr * tr * 4f);
                frame.Add(alongX ? new Vector3(t.x, y + 2.5f, t.z + s) : new Vector3(t.x + s, y + 2.5f, t.z),
                    alongX ? new Vector3(len, 0.18f, 0.18f) : new Vector3(0.18f, 0.18f, len), alongX ? Quaternion.Euler(0f, 0f, a) : Quaternion.Euler(a, 0f, 0f));
            }
        deck.Add(new Vector3(t.x, th - 0.25f, t.z), new Vector3(half * 2f, 0.5f, half * 2f));        // the platform, its floor at th
        WaitingCollider(cols, new Vector3(t.x, th - 0.25f, t.z), new Vector3(half * 2f, 0.5f, half * 2f));
        deck.Add(new Vector3(t.x, 0.15f, t.z), new Vector3(tr * 2f + 4f, 0.3f, tr * 2f + 4f));       // a footing under the tower
        // Railings round the platform: open only to the lift landing (north) and the bridge (west).
        float liftGap = WaitingIsland.LiftShaftHalf, bridgeGap = WaitingIsland.BridgeHalfWidth;
        WaitingRail(trim, cols, new Vector3(t.x - half, th, t.z - half), new Vector3(t.x + half, th, t.z - half));                        // south
        WaitingRail(trim, cols, new Vector3(t.x + half, th, t.z - half), new Vector3(t.x + half, th, t.z + half));                        // east
        WaitingRail(trim, cols, new Vector3(t.x - half, th, t.z + half), new Vector3(WaitingIsland.Lift.x - liftGap, th, t.z + half));    // north, beside the lift
        WaitingRail(trim, cols, new Vector3(WaitingIsland.Lift.x + liftGap, th, t.z + half), new Vector3(t.x + half, th, t.z + half));
        WaitingRail(trim, cols, new Vector3(t.x - half, th, t.z - half), new Vector3(t.x - half, th, WaitingIsland.BungeePad.z - bridgeGap)); // west, beside the bridge
        WaitingRail(trim, cols, new Vector3(t.x - half, th, WaitingIsland.BungeePad.z + bridgeGap), new Vector3(t.x - half, th, t.z + half));
        // The bridge to the jump board: railed both sides, open at the end (the only way down besides the lift).
        float bx0 = WaitingIsland.BridgeEndX, bx1 = WaitingIsland.BridgeStartX, bz = WaitingIsland.BungeePad.z;
        wood.Add(new Vector3((bx0 + bx1) * 0.5f, th - 0.1f, bz), new Vector3(bx1 - bx0, 0.2f, bridgeGap * 2f));
        WaitingCollider(cols, new Vector3((bx0 + bx1) * 0.5f, th - 0.1f, bz), new Vector3(bx1 - bx0, 0.2f, bridgeGap * 2f));
        frame.Add(new Vector3((bx0 + bx1) * 0.5f, th - 0.45f, bz), new Vector3(bx1 - bx0, 0.5f, 0.3f));    // the girder under it
        WaitingRail(trim, cols, new Vector3(WaitingIsland.BungeePad.x + WaitingIsland.JumpBoardRadius, th, bz - bridgeGap), new Vector3(bx1, th, bz - bridgeGap));
        WaitingRail(trim, cols, new Vector3(WaitingIsland.BungeePad.x + WaitingIsland.JumpBoardRadius, th, bz + bridgeGap), new Vector3(bx1, th, bz + bridgeGap));
        var pad = WaitingIsland.BungeePad;
        band.Add(new Vector3(pad.x, th + 0.01f, pad.z), new Vector3(WaitingIsland.JumpBoardRadius * 2f, 0.04f, bridgeGap * 2f));     // the jump board: amber
        trim.Add(new Vector3(pad.x, WaitingIsland.PadTop * 0.5f, pad.z), new Vector3(WaitingIsland.BungeePadRadius * 2f, WaitingIsland.PadTop, WaitingIsland.BungeePadRadius * 2f)); // the landing pad below

        // The lift: an exposed shaft of four posts and hoops, a landing grate at the top between the shaft and the platform, and the car.
        var lp = WaitingIsland.Lift; float sh = WaitingIsland.LiftShaftHalf;
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -sh : sh, sz = (i & 2) == 0 ? -sh : sh;
            frame.Add(new Vector3(lp.x + sx, (th + 4f) * 0.5f, lp.z + sz), new Vector3(0.3f, th + 4f, 0.3f));
        }
        for (float y = 6f; y < th + 4f; y += 6f)
        {
            frame.Add(new Vector3(lp.x, y, lp.z + sh), new Vector3(sh * 2f, 0.15f, 0.15f)); frame.Add(new Vector3(lp.x, y, lp.z - sh), new Vector3(sh * 2f, 0.15f, 0.15f));
            frame.Add(new Vector3(lp.x + sh, y, lp.z), new Vector3(0.15f, 0.15f, sh * 2f)); frame.Add(new Vector3(lp.x - sh, y, lp.z), new Vector3(0.15f, 0.15f, sh * 2f));
        }
        frame.Add(new Vector3(lp.x, th + 4f, lp.z), new Vector3(sh * 2f + 0.4f, 0.4f, sh * 2f + 0.4f));          // the head of the shaft
        float landZ0 = t.z + half, landZ1 = lp.z + sh;
        wood.Add(new Vector3(lp.x, th - 0.1f, (landZ0 + landZ1) * 0.5f), new Vector3(sh * 2f, 0.2f, landZ1 - landZ0)); // the top landing
        WaitingCollider(cols, new Vector3(lp.x, th - 0.1f, (landZ0 + landZ1) * 0.5f), new Vector3(sh * 2f, 0.2f, landZ1 - landZ0));
        WaitingRail(trim, cols, new Vector3(lp.x - sh, th, landZ0), new Vector3(lp.x - sh, th, landZ1));
        WaitingRail(trim, cols, new Vector3(lp.x + sh, th, landZ0), new Vector3(lp.x + sh, th, landZ1));
        WaitingRail(trim, cols, new Vector3(lp.x - sh, th, landZ1), new Vector3(lp.x + sh, th, landZ1));
        deck.Add(new Vector3(lp.x, WaitingIsland.LiftSillTop * 0.5f, lp.z), new Vector3(sh * 2f + 1f, WaitingIsland.LiftSillTop, sh * 2f + 1f)); // the ground sill: flush, under the car floor
        BuildBungeeLiftCar(root, store, WaitingLook.Stainless, accent.Material, WaitingLook.Tread, steel.Tile, accent.Tile, planks.Tile);   // ADR-495: the island's own look

        BuildWaitingRange(root, frame, wall, trim, board, band);

        // The big board: a dark panel on a steel frame on the north rim, facing the middle (T6 draws the text).
        var bd = WaitingIsland.Board;
        board.Add(new Vector3(bd.x, 7f, bd.z - 0.4f), new Vector3(WaitingIsland.BoardWidth + 1f, WaitingIsland.BoardHeight + 1f, 0.5f));
        frame.Add(new Vector3(bd.x - WaitingIsland.BoardWidth * 0.5f - 0.3f, 4.5f, bd.z), new Vector3(0.6f, 9f, 0.6f));
        frame.Add(new Vector3(bd.x + WaitingIsland.BoardWidth * 0.5f + 0.3f, 4.5f, bd.z), new Vector3(0.6f, 9f, 0.6f));
        trim.Add(new Vector3(bd.x, 7f + WaitingIsland.BoardHeight * 0.5f + 0.6f, bd.z - 0.1f), new Vector3(WaitingIsland.BoardWidth + 1.4f, 0.3f, 0.3f));
        trim.Add(new Vector3(bd.x, 7f - WaitingIsland.BoardHeight * 0.5f - 0.6f, bd.z - 0.1f), new Vector3(WaitingIsland.BoardWidth + 1.4f, 0.3f, 0.3f));

        // The lift doors on the south rim: a frame and a lintel (the countdown ends here, the ride starts as at every station).
        frame.Add(new Vector3(el.x - 3.4f, 2.4f, el.z + 3f), new Vector3(0.6f, 4.8f, 0.6f));
        frame.Add(new Vector3(el.x + 3.4f, 2.4f, el.z + 3f), new Vector3(0.6f, 4.8f, 0.6f));
        frame.Add(new Vector3(el.x, 4.9f, el.z + 3f), new Vector3(7.4f, 0.6f, 0.6f));
        trim.Add(new Vector3(el.x, 4.5f, el.z + 2.7f), new Vector3(6.2f, 0.12f, 0.12f));

        AddWaitingMesh(root, store, "Deck", deck, concrete.Material, false);
        for (int i = 0; i < WaitingSectors.Count; i++) AddWaitingMesh(root, store, "Frame" + i, frame.Batches[i], WaitingLook.Steel, false);
        for (int i = 0; i < WaitingSectors.Count; i++) { AddWaitingMesh(root, store, "Wall" + i, wall.Batches[i], WaitingLook.Panel, false); AddWaitingMesh(root, store, "Dome" + i, dome.Batches[i], WaitingLook.Dome, false); }
        AddWaitingMesh(root, store, "Planks", wood, planks.Material, false);
        AddWaitingMesh(root, store, "Trim", trim, WaitingLook.Steel, false);
        AddWaitingMesh(root, store, "BoardBack", board, WaitingLook.Steel, false);
        AddWaitingMesh(root, store, "Band", band, WaitingLook.Accent, false);

        // A deck-level collider, so people stand on it even if the mesh collider cooks thin; the walkable area is the server's clamp anyway.
        var floor = new GameObject("DeckCollider"); floor.transform.SetParent(root, false);
        floor.AddComponent<BoxCollider>().size = new Vector3(R * 2f, 1f, R * 2f);
        floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        floor.isStatic = true;

        // Interior light: the sun does not reach under the dome.
        var lightGo = new GameObject("IslandLight"); lightGo.transform.SetParent(root, false); lightGo.transform.localPosition = new Vector3(0f, 14f, 0f);
        var light = lightGo.AddComponent<Light>(); light.type = LightType.Point; light.range = 60f; light.intensity = 260f; light.color = new Color(1f, 0.93f, 0.82f); light.shadows = LightShadows.None;
        // A lamp over the tower top: the platform, the bridge and the board read from the deck and from up there.
        var topLamp = new GameObject("TowerTopLight"); topLamp.transform.SetParent(root, false); topLamp.transform.localPosition = new Vector3(WaitingIsland.Tower.x - 4f, BungeeCurve.Top + 6f, WaitingIsland.Tower.z + 2f);
        var tl = topLamp.AddComponent<Light>(); tl.type = LightType.Point; tl.range = 26f; tl.intensity = 90f; tl.color = new Color(1f, 0.9f, 0.75f); tl.shadows = LightShadows.None;
        // Lamps round the hall at head height: warm, so the plaster reads as a lit amusement hall, not a dark shed.
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            var lamp = new GameObject("Lamp" + i); lamp.transform.SetParent(root, false); lamp.transform.localPosition = new Vector3(Mathf.Cos(a) * 24f, 9f, Mathf.Sin(a) * 24f);
            var l = lamp.AddComponent<Light>(); l.type = LightType.Point; l.range = 34f; l.intensity = 110f; l.shadows = LightShadows.None;
            l.color = i % 2 == 0 ? new Color(1f, 0.82f, 0.55f) : new Color(0.7f, 0.85f, 1f);
        }
        Debug.Log($"[WorldBuilder] waiting island at ({cx:F0}, {cz:F0}), radius {R:F0} m: {deck.Boxes + frame.Boxes + wall.Boxes + wood.Boxes + trim.Boxes} boxes");
    }

    // The shooting range (ADR-501, #421): per booth a waist-high counter, a roof over the stand, lane walls west to the island's wall, a
    // backstop and two targets with the TrainingDummy's body: a 1.8 m capsule collider marked RangeTarget (the server scores hits on it),
    // drawn as boxes in the shared batches (no new draw calls, no new assets). Geometry from Core/WaitingRange.
    static void BuildWaitingRange(Transform root, WaitingSectors frame, WaitingSectors wall, WorldMeshes.BoxBatch trim, WorldMeshes.BoxBatch board, WorldMeshes.BoxBatch band)
    {
        var range = Child(root, "Range");
        float wallR = WaitingIsland.Radius - 0.6f;
        for (int i = 0; i < WaitingIsland.Booths.Length; i++)
        {
            var b = WaitingIsland.Booths[i];
            float counterX = b.x + WaitingRange.CounterX, width = WaitingRange.HalfWidth * 2f, lane = WaitingRange.LaneHalf * 2f;
            frame.Add(new Vector3(counterX, 0.55f, b.z), new Vector3(0.5f, 1.1f, width));
            band.Add(new Vector3(counterX, 1.12f, b.z), new Vector3(0.62f, 0.04f, width + 0.1f));
            WaitingCollider(range, new Vector3(counterX, 0.6f, b.z), new Vector3(0.5f, 1.2f, width));
            float standMid = b.x + (WaitingRange.StandFront + WaitingRange.StandBack) * 0.5f, standLen = WaitingRange.StandBack - WaitingRange.StandFront;
            frame.Add(new Vector3(standMid, 3.3f, b.z), new Vector3(standLen + 0.6f, 0.2f, lane + 0.3f));
            trim.Add(new Vector3(b.x + WaitingRange.StandBack + 0.25f, 3.0f, b.z), new Vector3(0.1f, 0.3f, lane + 0.3f));
            foreach (float side in new[] { -1f, 1f })
            {
                float z = b.z + side * WaitingRange.LaneHalf;
                float x0 = b.x + WaitingRange.StandBack + 0.3f, x1 = -Mathf.Sqrt(wallR * wallR - z * z);   // from the stand's back to the island's wall
                var c = new Vector3((x0 + x1) * 0.5f, 1.6f, z); var size = new Vector3(x0 - x1, 3.2f, 0.25f);
                wall.Add(c, size);
                WaitingCollider(range, c, size);
            }
            board.Add(new Vector3(WaitingRange.TargetX - 1.6f, 1.6f, b.z), new Vector3(0.3f, 3.2f, lane - 0.3f));   // backstop
            for (int t = 0; t < WaitingRange.TargetsPerBooth; t++)
            {
                var p = WaitingRange.Target(i, t);
                frame.Add(new Vector3(p.x, 0.35f, p.z), new Vector3(0.1f, 0.7f, 0.1f));          // post
                band.Add(new Vector3(p.x, 1.15f, p.z), new Vector3(0.18f, 0.9f, 0.56f));         // body: gold, so it reads against the dark backstop
                band.Add(new Vector3(p.x, 1.74f, p.z), new Vector3(0.18f, 0.28f, 0.26f));        // head
                trim.Add(new Vector3(p.x + 0.1f, 1.25f, p.z), new Vector3(0.03f, 0.22f, 0.22f)); // the mark on its chest
                var go = new GameObject($"Target{i}_{t}");
                go.transform.SetParent(range, false);
                go.transform.localPosition = new Vector3(p.x, 0f, p.z);
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.height = WaitingRange.TargetHeight; capsule.radius = WaitingRange.TargetRadius; capsule.center = new Vector3(0f, WaitingRange.TargetHeight * 0.5f, 0f);
                go.AddComponent<Game.Net.RangeTarget>().Booth = i;
                go.isStatic = true;
            }
        }
    }

    // A railing from a to b on a floor at a.y: a top rail at 1.1 m and a mid rail (visual), one collider box 1.2 m high.
    static void WaitingRail(WorldMeshes.BoxBatch trim, Transform cols, Vector3 a, Vector3 b)
    {
        var mid = (a + b) * 0.5f; var d = b - a; float len = d.magnitude; if (len < 0.05f) return;
        bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
        var size = alongX ? new Vector3(len, 0.08f, 0.08f) : new Vector3(0.08f, 0.08f, len);
        trim.Add(mid + Vector3.up * 1.1f, size); trim.Add(mid + Vector3.up * 0.55f, size);
        for (float u = 0f; u <= len + 0.01f; u += 1.5f) trim.Add(a + d / len * u + Vector3.up * 0.55f, new Vector3(0.08f, 1.1f, 0.08f));
        WaitingCollider(cols, mid + Vector3.up * 0.6f, alongX ? new Vector3(len, 1.2f, 0.15f) : new Vector3(0.15f, 1.2f, len));
    }

    static void WaitingCollider(Transform parent, Vector3 centre, Vector3 size)
    {
        var go = new GameObject("Box"); go.transform.SetParent(parent, false); go.transform.localPosition = centre;
        go.AddComponent<BoxCollider>().size = size; go.isStatic = true;
    }

    // The cage car of the bungee lift (ADR-387): a floor, corner posts, a roof and open bar sides so the riders see out; "Doors" (bars
    // across both openings) shows only while it moves. Not static: WaitingHud moves it on the clients. Its side and gate
    // colliders, the shaft's post colliders and fence and the hoist drive are added at runtime by Net/LiftHoist (ADR-496).
    static void BuildBungeeLiftCar(Transform root, WorldMeshes.Store store, Material steel, Material accent, Material planks, Vector2 steelTile, Vector2 accentTile, Vector2 planksTile)
    {
        var lp = WaitingIsland.Lift; float h = BungeeLift.HalfSize, H = 2.6f;
        var car = Child(root, "BungeeLiftCar"); car.localPosition = new Vector3(lp.x, 0f, lp.z);
        var bars = new WorldMeshes.BoxBatch(steelTile); var floor = new WorldMeshes.BoxBatch(planksTile); var lamp = new WorldMeshes.BoxBatch(accentTile); var doors = new WorldMeshes.BoxBatch(steelTile);
        floor.Add(new Vector3(0f, WaitingIsland.CarFloorTop * 0.5f, 0f), new Vector3(h * 2f, WaitingIsland.CarFloorTop, h * 2f));
        bars.Add(new Vector3(0f, H, 0f), new Vector3(h * 2f + 0.1f, 0.12f, h * 2f + 0.1f));
        for (int i = 0; i < 4; i++) { float sx = (i & 1) == 0 ? -h : h, sz = (i & 2) == 0 ? -h : h; bars.Add(new Vector3(sx, H * 0.5f, sz), new Vector3(0.12f, H, 0.12f)); }
        foreach (float sx in new[] { -h, h })                     // east and west sides: bars, open between
        {
            bars.Add(new Vector3(sx, 1.05f, 0f), new Vector3(0.05f, 0.05f, h * 2f));
            for (float z = -h + 0.7f; z < h - 0.1f; z += 0.7f) bars.Add(new Vector3(sx, H * 0.5f, z), new Vector3(0.03f, H, 0.03f));   // thin and wide apart: the view is the point
        }
        lamp.Add(new Vector3(0f, H - 0.12f, 0f), new Vector3(1.2f, 0.06f, 0.3f));
        foreach (float sz in new[] { -h, h })                     // the gates north (ground side) and south (platform side)
            for (float x = -h + 0.56f; x < h - 0.1f; x += 0.56f) doors.Add(new Vector3(x, H * 0.5f, sz), new Vector3(0.03f, H - 0.2f, 0.03f));
        AddWaitingMesh(car, store, "Bars", bars, steel, false); AddWaitingMesh(car, store, "Floor", floor, planks, false); AddWaitingMesh(car, store, "Lamp", lamp, accent, false);
        var doorRoot = Child(car, "Doors");
        AddWaitingMesh(doorRoot, store, "Gates", doors, steel, false);
        foreach (var r in car.GetComponentsInChildren<Transform>(true)) r.gameObject.isStatic = false;
        var l = new GameObject("CarLight"); l.transform.SetParent(car, false); l.transform.localPosition = new Vector3(0f, H - 0.3f, 0f);
        var light = l.AddComponent<Light>(); light.type = LightType.Point; light.range = 6f; light.intensity = 6f; light.shadows = LightShadows.None;
    }

    static void AddWaitingMesh(Transform parent, WorldMeshes.Store store, string name, WorldMeshes.BoxBatch batch, Material material, bool collide) // shortcut: blob meshes are not readable for MeshCollider; the deck BoxCollider + the server clamp keep people in
    {
        if (batch.Boxes == 0) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mesh = store.AddBlob(batch.ToMesh("WaitingIsland_" + name));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
    }
}

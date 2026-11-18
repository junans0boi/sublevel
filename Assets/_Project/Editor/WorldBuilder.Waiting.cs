using Game.Core;
using UnityEngine;

// The waiting island (ADR-381, #414): a sealed 80 m deck far out in the sea of the World scene. Everything is procedural boxes
// and cylinders from the shared materials (no new assets). Positions come from Core/WaitingIsland so the server and the tests
// agree with the builder. Frame: the island's centre at WaitingIsland.CentreX/Z on the sea plane.
public static partial class WorldBuilder
{
    const float WaitingWallHeight = 15f, WaitingDeckThickness = 1.2f;
    const int WaitingWallSegments = 40, WaitingDomeRings = 4;

    static void BuildWaitingIsland(WorldPlan plan, Transform parent, WorldMeshes.Store store)
    {
        var root = Child(parent, "WaitingIsland");
        float cx = WaitingIsland.CentreX, cz = WaitingIsland.CentreZ;
        float deckY = plan.SeaLevel + WaitingIsland.FloorOffset;      // top of the deck
        root.position = new Vector3(cx, deckY, cz);                    // children are in island-local metres, y = 0 at the deck

        var concrete = WorldMaterials.ConcreteDark; var steel = WorldMaterials.PaintedMetal; var rust = WorldMaterials.RustyMetal;
        var planks = WorldMaterials.PlanksDark; var accent = WorldMaterials.Accent(1);
        var deck = new WorldMeshes.BoxBatch(concrete.Tile); var frame = new WorldMeshes.BoxBatch(steel.Tile);
        var wall = new WorldMeshes.BoxBatch(rust.Tile); var wood = new WorldMeshes.BoxBatch(planks.Tile); var trim = new WorldMeshes.BoxBatch(accent.Tile);

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
            float h0 = WaitingWallHeight + R * 0.55f * Mathf.Sin(p0), h1 = WaitingWallHeight + R * 0.55f * Mathf.Sin(p1), hm = (h0 + h1) * 0.5f;
            float slope = Mathf.Atan2(h1 - h0, rr0 - rr1) * Mathf.Rad2Deg;      // panel rises towards the middle
            int n = Mathf.Max(6, Mathf.RoundToInt(WaitingWallSegments * Mathf.Cos(pm)));
            for (int i = 0; i < n; i++)
            {
                float am = (i + 0.5f) * Mathf.PI * 2f / n;
                float chord = 2f * rm * Mathf.Sin(Mathf.PI / n) + 0.3f, len = Vector2.Distance(new Vector2(rr0, h0), new Vector2(rr1, h1)) + 0.3f;
                var rot = Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f) * Quaternion.Euler(-slope, 0f, 0f);
                wall.Add(new Vector3(Mathf.Cos(am) * rm, hm, Mathf.Sin(am) * rm), new Vector3(chord, 0.35f, len), rot);
            }
            frame.AddCylinder(new Vector3(0f, h1 - 0.15f, 0f), Mathf.Max(0.6f, rr1), 0.3f, 32);
        }

        // Bungee tower: four legs, cross bracing, a jump platform on top (T7 drives the jump).
        var t = WaitingIsland.Tower; float tr = 3.2f, th = 24f;
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -tr : tr, sz = (i & 2) == 0 ? -tr : tr;
            frame.Add(new Vector3(t.x + sx, th * 0.5f, t.z + sz), new Vector3(0.5f, th, 0.5f));
        }
        for (float y = 3f; y < th; y += 4f)
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2; float s = (side & 1) == 0 ? -tr : tr;
                frame.Add(alongX ? new Vector3(t.x, y, t.z + s) : new Vector3(t.x + s, y, t.z), alongX ? new Vector3(tr * 2f, 0.25f, 0.25f) : new Vector3(0.25f, 0.25f, tr * 2f));
            }
        deck.Add(new Vector3(t.x, th, t.z), new Vector3(tr * 2f + 3f, 0.5f, tr * 2f + 3f));          // the jump platform
        trim.Add(new Vector3(t.x, th + 1.0f, t.z + tr + 1.5f), new Vector3(tr * 2f + 3f, 0.12f, 0.12f));
        deck.Add(new Vector3(t.x, 0.15f, t.z), new Vector3(tr * 2f + 4f, 0.3f, tr * 2f + 4f));       // a pad under it

        // Booths of the shooting range (2nd stage): a counter, a back wall and a roof each.
        foreach (var b in WaitingIsland.Booths)
        {
            frame.Add(new Vector3(b.x, 1.0f, b.z), new Vector3(2.4f, 2f, 1.2f));
            wall.Add(new Vector3(b.x - 1.8f, 1.6f, b.z), new Vector3(0.3f, 3.2f, 5f));
            frame.Add(new Vector3(b.x - 0.4f, 3.3f, b.z), new Vector3(4.4f, 0.2f, 5.2f));
            trim.Add(new Vector3(b.x + 1.2f, 3.0f, b.z), new Vector3(0.1f, 0.3f, 5.2f));
        }

        // The big board: a dark panel on a steel frame on the north rim, facing the middle (T6 draws the text).
        var bd = WaitingIsland.Board;
        wall.Add(new Vector3(bd.x, 7f, bd.z - 0.4f), new Vector3(WaitingIsland.BoardWidth + 1f, WaitingIsland.BoardHeight + 1f, 0.5f));
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
        AddWaitingMesh(root, store, "Frame", frame, steel.Material, false);
        AddWaitingMesh(root, store, "Wall", wall, rust.Material, false);
        AddWaitingMesh(root, store, "Planks", wood, planks.Material, false);
        AddWaitingMesh(root, store, "Trim", trim, accent.Material, false);

        // A deck-level collider, so people stand on it even if the mesh collider cooks thin; the walkable area is the server's clamp anyway.
        var floor = new GameObject("DeckCollider"); floor.transform.SetParent(root, false);
        floor.AddComponent<BoxCollider>().size = new Vector3(R * 2f, 1f, R * 2f);
        floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        floor.isStatic = true;

        // Interior light: the sun does not reach under the dome.
        var lightGo = new GameObject("IslandLight"); lightGo.transform.SetParent(root, false); lightGo.transform.localPosition = new Vector3(0f, 14f, 0f);
        var light = lightGo.AddComponent<Light>(); light.type = LightType.Point; light.range = 70f; light.intensity = 4f; light.color = new Color(1f, 0.93f, 0.82f); light.shadows = LightShadows.None;
        Debug.Log($"[WorldBuilder] waiting island at ({cx:F0}, {cz:F0}), radius {R:F0} m: {deck.Boxes + frame.Boxes + wall.Boxes + wood.Boxes + trim.Boxes} boxes");
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

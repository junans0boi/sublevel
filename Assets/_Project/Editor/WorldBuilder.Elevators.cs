using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using static EditorFields;
using UnityEngine;
using UnityEngine.Rendering;

// The elevator shafts and cars under the stations (ADR-065 (1)): the match starts at the bottom of a shaft about
// 13 m deep and the car brings everyone up to the station floor while the doors are shut. Everything here is in
// a station's spawn frame: origin = the centre of the car bay on the station floor, +Z = the way out.
public static partial class WorldBuilder
{
    // The shaft's inside: the bay's width, from the bay's back wall to just behind the station door (which sinks
    // into the front wall of the shaft when it opens).
    const float ShaftHalfWidth = Buildings.ElevatorBay / 2f;
    const float ShaftBack = -Buildings.ElevatorBay / 2f, ShaftFront = 1.32f, ShaftWall = 0.2f;
    const float RibEvery = 2f, LampEvery = 3f, ShaftLightDepth1 = 1.6f, ShaftLightDepth2 = 5.2f;

    // The car: walls 5 cm inside the shaft, an inner door with a window strip at eye height so the shaft can be
    // seen sliding past on the way up.
    const float CarHalfWidth = 1.40f, CarBack = -1.40f, CarFront = 1.22f, CarSkin = 0.05f, CarHeight = 2.6f;
    const float CarFloorThickness = 0.15f, CarCeiling = 0.1f;
    const float CarDoorHalfWidth = 1.12f, CarDoorTop = 2.32f, CarDoorZ0 = 1.10f, CarDoorZ1 = 1.15f;
    const float CarWindowBottom = 1.28f, CarWindowTop = 1.76f;
    const float CarDoorDrop = 2.5f;

    // The slab opening in the building's own frame (the plan's spawn point is the bay centre).
    static (float x0, float x1, float z0, float z1) ShaftOpening(BuildingPlan plan) =>
        (plan.SpawnX - ShaftHalfWidth, plan.SpawnX + ShaftHalfWidth, plan.SpawnZ + ShaftBack, plan.SpawnZ + ShaftFront);

    // A box with a rectangular hole (in x and z) through it, as up to four boxes around the hole.
    static List<(Vector3 center, Vector3 size)> SplitAround(Vector3 center, Vector3 size, (float x0, float x1, float z0, float z1) hole)
    {
        float minX = center.x - size.x / 2f, maxX = center.x + size.x / 2f;
        float minZ = center.z - size.z / 2f, maxZ = center.z + size.z / 2f;
        float hx0 = Mathf.Max(minX, hole.x0), hx1 = Mathf.Min(maxX, hole.x1);
        float hz0 = Mathf.Max(minZ, hole.z0), hz1 = Mathf.Min(maxZ, hole.z1);
        var parts = new List<(Vector3 center, Vector3 size)>();
        if (hx0 >= hx1 || hz0 >= hz1)
        {
            parts.Add((center, size));
            return parts;
        }
        void Part(float x0, float x1, float z0, float z1)
        {
            if (x1 - x0 < 0.005f || z1 - z0 < 0.005f) return;
            parts.Add((new Vector3((x0 + x1) / 2f, center.y, (z0 + z1) / 2f), new Vector3(x1 - x0, size.y, z1 - z0)));
        }
        Part(minX, hx0, minZ, maxZ);
        Part(hx1, maxX, minZ, maxZ);
        Part(hx0, hx1, minZ, hz0);
        Part(hx0, hx1, hz1, maxZ);
        return parts;
    }

    static void AddBox(WorldMeshes.BoxBatch batch, float x0, float x1, float y0, float y1, float z0, float z1, bool skipBottom = false) =>
        batch.Add(new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f), new Vector3(x1 - x0, y1 - y0, z1 - z0), skipBottom);

    static void AddCollider(GameObject go, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f);
        box.size = new Vector3(x1 - x0, y1 - y0, z1 - z0);
    }

    static MeshRenderer AddMesh(Transform parent, string name, WorldMeshes.BoxBatch batch, Material material, WorldMeshes.Store store,
        ShadowCastingMode shadows, bool isStatic)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(batch.ToMesh(name));
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows;
        go.isStatic = isStatic;
        return renderer;
    }

    static Light AddLamp(Transform parent, string name, Vector3 localPosition, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(1f, 0.86f, 0.68f);
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        return light;
    }

    // The shaft under station `index` (static concrete, no collider: nobody walks in it, and riders are carried by
    // the car's own colliders) and its car. Not navmesh sources: the shaft is not walkable.
    static void AddElevatorShaft(Transform parent, int index, Placed station, WorldMeshes.Store store)
    {
        var rules = new DeploymentRules(); // the build uses the decided defaults, never a test's replacement
        float depth = rules.ShaftDepth;
        var rotation = Quaternion.Euler(0f, station.SpawnYaw, 0f);
        var shaft = new GameObject($"ElevatorShaft{index:00}");
        shaft.transform.SetParent(parent, false);
        shaft.transform.SetPositionAndRotation(station.Spawn, rotation);
        shaft.isStatic = true;

        float top = -Buildings.FloorTop; // the underside of the station slab
        float bottom = -depth;
        float x0 = -ShaftHalfWidth, x1 = ShaftHalfWidth, z0 = ShaftBack, z1 = ShaftFront, w = ShaftWall;

        var concrete = WorldMaterials.ShaftConcrete;
        var walls = new WorldMeshes.BoxBatch(concrete.Tile);
        AddBox(walls, x0 - w, x0, bottom - w, top, z0 - w, z1 + w);   // left
        AddBox(walls, x1, x1 + w, bottom - w, top, z0 - w, z1 + w);   // right
        AddBox(walls, x0, x1, bottom - w, top, z0 - w, z0);           // back
        AddBox(walls, x0, x1, bottom - w, top, z1, z1 + w);           // front (the station door sinks into it)
        AddBox(walls, x0, x1, bottom - w, bottom, z0, z1);            // pit floor
        // Colliders for the walls and the pit as a safety net only: riders are carried, and the terrain over the
        // shaft is opened, so nothing would otherwise stop a body that slipped out of a car.
        AddCollider(shaft, x0 - w, x0, bottom - w, top, z0 - w, z1 + w);
        AddCollider(shaft, x1, x1 + w, bottom - w, top, z0 - w, z1 + w);
        AddCollider(shaft, x0, x1, bottom - w, top, z0 - w, z0);
        AddCollider(shaft, x0, x1, bottom - w, top, z1, z1 + w);
        AddCollider(shaft, x0, x1, bottom - w, bottom, z0, z1);

        // Steel rib beams every 2 m and dark formwork seams halfway between them, on the walls the car passes
        // closest to (the front one is what the window shows).
        var metal = WorldMaterials.PaintedMetal;
        var ribs = new WorldMeshes.BoxBatch(metal.Tile);
        var seams = new WorldMeshes.BoxBatch(Vector2.one);
        const float ribOut = 0.045f, ribHalf = 0.07f, seamOut = 0.006f, seamHalf = 0.012f;
        for (float y = -RibEvery; y > bottom + 0.3f; y -= RibEvery)
        {
            AddBox(ribs, x0, x1, y - ribHalf, y + ribHalf, z1 - ribOut, z1);
            AddBox(ribs, x0, x0 + ribOut, y - ribHalf, y + ribHalf, z0, z1);
            AddBox(ribs, x1 - ribOut, x1, y - ribHalf, y + ribHalf, z0, z1);
            float s = y + RibEvery / 2f;
            if (s < top - 0.2f)
            {
                AddBox(seams, x0, x1, s - seamHalf, s + seamHalf, z1 - seamOut, z1);
                AddBox(seams, x0, x0 + seamOut, s - seamHalf, s + seamHalf, z0, z1);
                AddBox(seams, x1 - seamOut, x1, s - seamHalf, s + seamHalf, z0, z1);
            }
        }

        // Guide rails down both sides and the counterweight's two cables down the back.
        var rusty = WorldMaterials.RustyMetal;
        var rails = new WorldMeshes.BoxBatch(rusty.Tile);
        AddBox(rails, x0, x0 + 0.06f, bottom, top, -0.05f, 0.05f);
        AddBox(rails, x1 - 0.06f, x1, bottom, top, -0.05f, 0.05f);
        AddBox(seams, -0.26f, -0.235f, bottom, top, z0 + 0.025f, z0 + 0.05f);
        AddBox(seams, 0.235f, 0.26f, bottom, top, z0 + 0.025f, z0 + 0.05f);

        // Small lamps every 3 m on the front wall, left and right of the middle in turn, in a dark housing.
        var lampMaterial = WorldMaterials.ShaftLamp();
        var lamps = new WorldMeshes.BoxBatch(Vector2.one);
        int n = 0;
        for (float y = -LampEvery / 2f; y > bottom + 0.4f; y -= LampEvery, n++)
        {
            float x = (n % 2 == 0 ? -1f : 1f) * 0.55f;
            AddBox(seams, x - 0.2f, x + 0.2f, y - 0.075f, y + 0.075f, z1 - 0.02f, z1);
            AddBox(lamps, x - 0.16f, x + 0.16f, y - 0.045f, y + 0.045f, z1 - 0.04f, z1 - 0.02f);
        }

        AddMesh(shaft.transform, "Walls", walls, concrete.Material, store, ShadowCastingMode.On, true);
        AddMesh(shaft.transform, "Ribs", ribs, metal.Material, store, ShadowCastingMode.Off, true);
        AddMesh(shaft.transform, "Rails", rails, rusty.Material, store, ShadowCastingMode.Off, true);
        AddMesh(shaft.transform, "Seams", seams, WorldMaterials.DoorSeam(), store, ShadowCastingMode.Off, true);
        AddMesh(shaft.transform, "Lamps", lamps, lampMaterial, store, ShadowCastingMode.Off, true);

        // The counterweight hangs at the bottom while the car is at the top; ElevatorCar runs it the other way.
        var weight = new WorldMeshes.BoxBatch(rusty.Tile);
        AddBox(weight, -0.4f, 0.4f, -0.7f, 0.7f, -0.03f, 0.03f);
        var counterweight = AddMesh(shaft.transform, "Counterweight", weight, rusty.Material, store, ShadowCastingMode.Off, false).transform;
        counterweight.localPosition = new Vector3(0f, bottom + 0.3f + 0.7f, z0 + 0.06f);

        // Real light only near the top (the car brings its own lamp down below); none of them cast shadows.
        var lights = new List<Light>
        {
            AddLamp(shaft.transform, "ShaftLight1", new Vector3(0f, -ShaftLightDepth1, z1 - 0.4f), 4.5f, 1.4f),
            AddLamp(shaft.transform, "ShaftLight2", new Vector3(0f, -ShaftLightDepth2, z1 - 0.4f), 4.5f, 1.4f),
        };

        AddElevatorCar(parent, index, station, rotation, store, lights, counterweight);
    }

    static void AddElevatorCar(Transform parent, int index, Placed station, Quaternion rotation, WorldMeshes.Store store,
        List<Light> lights, Transform counterweight)
    {
        var car = new GameObject($"ElevatorCar{index:00}");
        car.transform.SetParent(parent, false);
        car.transform.SetPositionAndRotation(station.Spawn, rotation); // at the top: its floor is the station floor
        car.isStatic = false;
        var body = car.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;

        float x0 = -CarHalfWidth, x1 = CarHalfWidth, z0 = CarBack, z1 = CarFront, s = CarSkin, h = CarHeight;
        var steel = WorldMaterials.Door();
        var metal = WorldMaterials.PaintedMetal;
        var rusty = WorldMaterials.RustyMetal;
        var shell = new WorldMeshes.BoxBatch(Vector2.one);
        var floor = new WorldMeshes.BoxBatch(metal.Tile);
        var trim = new WorldMeshes.BoxBatch(rusty.Tile);
        var lamp = new WorldMeshes.BoxBatch(Vector2.one);

        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(car.transform, false);
        void Solid(WorldMeshes.BoxBatch batch, float a0, float a1, float b0, float b1, float c0, float c1)
        {
            AddBox(batch, a0, a1, b0, b1, c0, c1);
            AddCollider(colliders, a0, a1, b0, b1, c0, c1);
        }

        Solid(floor, x0, x1, -CarFloorThickness, 0f, z0, z1);
        Solid(shell, x0, x0 + s, 0f, h, z0, z1);                      // left wall
        Solid(shell, x1 - s, x1, 0f, h, z0, z1);                      // right wall
        Solid(shell, x0, x1, 0f, h, z0, z0 + s);                      // back wall
        Solid(shell, x0, x1, h, h + CarCeiling, z0, z1);              // ceiling
        Solid(shell, x0, -CarDoorHalfWidth + 0.02f, 0f, h, z1 - s, z1);   // front, left of the door
        Solid(shell, CarDoorHalfWidth - 0.02f, x1, 0f, h, z1 - s, z1);    // front, right of the door
        Solid(shell, x0, x1, CarDoorTop, h, z1 - s, z1);              // above the door
        // Handrails on three walls, a crosshead on the roof and the ceiling lamp.
        AddBox(trim, -1.0f, 1.0f, 0.92f, 0.97f, z0 + s, z0 + s + 0.05f);
        AddBox(trim, x0 + s, x0 + s + 0.05f, 0.92f, 0.97f, -0.9f, 0.6f);
        AddBox(trim, x1 - s - 0.05f, x1 - s, 0.92f, 0.97f, -0.9f, 0.6f);
        AddBox(trim, -1.2f, 1.2f, h + CarCeiling, h + CarCeiling + 0.15f, -0.1f, 0.1f);
        AddBox(lamp, -0.5f, 0.5f, h - 0.03f, h, -0.7f, 0.2f);

        AddMesh(car.transform, "Shell", shell, steel, store, ShadowCastingMode.On, false);
        AddMesh(car.transform, "Floor", floor, metal.Material, store, ShadowCastingMode.Off, false);
        AddMesh(car.transform, "Trim", trim, rusty.Material, store, ShadowCastingMode.Off, false);
        AddMesh(car.transform, "Lamp", lamp, WorldMaterials.ShaftLamp(), store, ShadowCastingMode.Off, false);
        lights.Add(AddLamp(car.transform, "CarLight", new Vector3(0f, h - 0.25f, -0.2f), 4.5f, 1.6f));

        // The inner door: two panels with the window strip between them, an amber frame round the strip and a
        // centre seam on the inside. It sinks under the car floor when the doors open.
        var door = new GameObject("CarDoor");
        door.transform.SetParent(car.transform, false);
        float d0 = -CarDoorHalfWidth, d1 = CarDoorHalfWidth;
        var panels = new WorldMeshes.BoxBatch(Vector2.one);
        var stripe = new WorldMeshes.BoxBatch(Vector2.one);
        var seam = new WorldMeshes.BoxBatch(Vector2.one);
        AddBox(panels, d0, d1, 0f, CarWindowBottom, CarDoorZ0, CarDoorZ1);
        AddBox(panels, d0, d1, CarWindowTop, CarDoorTop, CarDoorZ0, CarDoorZ1);
        AddCollider(door, d0, d1, 0f, CarWindowBottom, CarDoorZ0, CarDoorZ1);
        AddCollider(door, d0, d1, CarWindowTop, CarDoorTop, CarDoorZ0, CarDoorZ1);
        AddBox(stripe, d0, d1, CarWindowBottom - 0.04f, CarWindowBottom, CarDoorZ0 - 0.006f, CarDoorZ0);
        AddBox(stripe, d0, d1, CarWindowTop, CarWindowTop + 0.04f, CarDoorZ0 - 0.006f, CarDoorZ0);
        AddBox(seam, -0.006f, 0.006f, 0f, CarWindowBottom - 0.04f, CarDoorZ0 - 0.004f, CarDoorZ0);
        AddBox(seam, -0.006f, 0.006f, CarWindowTop + 0.04f, CarDoorTop, CarDoorZ0 - 0.004f, CarDoorZ0);
        AddMesh(door.transform, "Panels", panels, steel, store, ShadowCastingMode.On, false);
        AddMesh(door.transform, "Stripe", stripe, WorldMaterials.DoorStripe(), store, ShadowCastingMode.Off, false);
        AddMesh(door.transform, "Seam", seam, WorldMaterials.DoorSeam(), store, ShadowCastingMode.Off, false);

        var component = car.AddComponent<ElevatorCar>();
        Set(component, "_station", p => p.intValue = index);
        Set(component, "_door", p => p.objectReferenceValue = door.transform);
        Set(component, "_doorDrop", p => p.floatValue = CarDoorDrop);
        SetArray(component, "_lights", lights.ToArray());
        Set(component, "_counterweight", p => p.objectReferenceValue = counterweight);
        car.AddComponent<ElevatorAudio>();
    }

    // Terrain holes over the shafts (ADR-065 (1)): the ground's surface would otherwise cut through the car as it
    // comes up. Only cells wholly inside a station's footprint are cut (the slab and its skirt cover them from
    // above), so no hole ever shows outside a station.
    static int CutShaftHoles(TerrainData data, WorldPlan plan, List<Placed> stations)
    {
        int res = data.holesResolution;
        if (res <= 0 || stations.Count == 0) return 0;
        float cell = plan.Size / res;
        var solid = data.GetHoles(0, 0, res, res);
        int cut = 0;
        foreach (var station in stations)
        {
            var toLocal = Quaternion.Inverse(station.Rotation);
            float hw = station.Plan.Width / 2f - 0.05f, hd = station.Plan.Depth / 2f - 0.05f;
            var spawnLocal = toLocal * (station.Spawn - station.Origin);
            // The shaft's inside in the building frame.
            float sx0 = spawnLocal.x - ShaftHalfWidth, sx1 = spawnLocal.x + ShaftHalfWidth;
            float sz0 = spawnLocal.z + ShaftBack, sz1 = spawnLocal.z + ShaftFront;
            float reach = Mathf.Max(station.Plan.Width, station.Plan.Depth);
            int i0 = Mathf.Max(0, Mathf.FloorToInt((station.Origin.x - reach) / cell)), i1 = Mathf.Min(res - 1, Mathf.CeilToInt((station.Origin.x + reach) / cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((station.Origin.z - reach) / cell)), j1 = Mathf.Min(res - 1, Mathf.CeilToInt((station.Origin.z + reach) / cell));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    bool inside = true, touches = false;
                    float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        var corner = new Vector3((i + (k & 1)) * cell, 0f, (j + (k >> 1)) * cell);
                        var p = toLocal * (corner - station.Origin);
                        if (Mathf.Abs(p.x) > hw || Mathf.Abs(p.z) > hd) inside = false;
                        minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                        minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                    }
                    touches = maxX > sx0 && minX < sx1 && maxZ > sz0 && minZ < sz1;
                    if (!inside || !touches || !solid[j, i]) continue;
                    solid[j, i] = false;
                    cut++;
                }
            }
        }
        data.SetHoles(0, 0, solid);
        return cut;
    }
}

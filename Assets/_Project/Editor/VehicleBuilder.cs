using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Object;
using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The vehicles of ADR-065 (5) on the CC-BY models (#89): five cars and two motorcycles, each its own prefab on the
// shared Vehicle framework, and the generic pack's bodies as static parked / wrecked props in the World's towns.
// VehicleModels takes the models apart and measures them; this file puts the parts on prefabs (Model child, wheel
// pivots with Spin children, seats with doors and grips, colliders, VehicleRig) and the prefabs into the scenes.
//
// BuildPrefabs saves Prefabs/M1/Vehicles/<model>.prefab (call before the spawnable prefab list is rebuilt);
// AddToTestBox / AddToWorld put a VehicleSpawner (and the World's props) on the match object of the scene being built.
// UpdateScenes rebuilds just the vehicles in the saved TestBox and World scenes (tools/unity.sh run VehicleBuilder.UpdateScenes).
public static class VehicleBuilder
{
    // Spawn mix order: cars, then motorcycles. The first car and the first motorcycle are the test box's test vehicles.
    public static readonly string[] Cars = { "FairheavenLT80", "FairheavenSW84", "LightbodyLifted85", "Shvan92", "GoblinV12" };
    public static readonly string[] Motorcycles = { "DirtBike", "RoadMotorcycle" };
    public static string PrefabPath(string name) => VehicleModels.OutDir + "/" + name + ".prefab";
    static string PropPath(string name) => VehicleModels.OutDir + "/Props/" + name + ".prefab";
    static string MeshPath(string name) => VehicleModels.OutDir + "/" + name + "_Meshes.asset";
    const string PropRootName = "ParkedCars";
    const float PropShare = 0.55f;          // of the parking candidates near town lots left after the vehicles: props
    const int MaxProps = 28;
    const float PropSpacing = 26f;

    // ---- Prefabs ----

    public static void BuildPrefabs()
    {
        Directory.CreateDirectory(VehicleModels.OutDir);
        Directory.CreateDirectory(VehicleModels.OutDir + "/Props");
        RemovePlaceholders();
        var physics = PhysicsMaterialAsset();
        foreach (var parts in VehicleModels.BuildCars()) BuildPrefab(parts, physics);
        foreach (var parts in VehicleModels.BuildBikes()) BuildPrefab(parts, physics);
        BuildProps();
        AssetDatabase.SaveAssets();
    }

    // The code-made placeholders of #86 (Car, Motorcycle and their meshes and materials).
    static void RemovePlaceholders()
    {
        foreach (var path in new[]
        {
            TestBoxBuilder.PrefabDir + "/Car.prefab", TestBoxBuilder.PrefabDir + "/Motorcycle.prefab", TestBoxBuilder.PrefabDir + "/Vehicle_Meshes.asset",
        })
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        foreach (var name in new[] { "Paint", "Glass", "Plastic", "Interior", "Lamp", "TailLamp", "Plate", "Tyre", "Rim" })
        {
            var path = $"{TestBoxBuilder.PrefabDir}/Materials/Vehicle_{name}.mat";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        }
    }

    static void SaveMeshes(string name, IEnumerable<Mesh> meshes)
    {
        var path = MeshPath(name);
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        var list = meshes.Where(m => m != null).ToList();
        AssetDatabase.CreateAsset(list[0], path);
        for (int i = 1; i < list.Count; i++) AssetDatabase.AddObjectToAsset(list[i], path);
    }

    static GameObject NewRoot(string name, VehicleRules rules, out Vehicle vehicle, out Transform model)
    {
        var root = new GameObject(name);
        var body = root.AddComponent<Rigidbody>();
        body.mass = rules.MassKg;
        body.isKinematic = true; // Vehicle makes it dynamic on the side that simulates
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        root.AddComponent<NetworkObject>();
        var nt = root.AddComponent<NetworkTransform>();
        nt.SetSynchronizeScale(false);
        root.AddComponent<VehicleState>();
        vehicle = root.AddComponent<Vehicle>();
        root.AddComponent<VehicleFuel>();  // ADR-075: tank, horn, engine sound
        root.AddComponent<VehicleHorn>();
        root.AddComponent<Game.Gameplay.VehicleAudio>();
        root.AddComponent<VehicleDamage>();   // #90, ADR-074: health, tyres, glass, fire, explosion
        root.AddComponent<Game.Gameplay.VehicleDamageVisual>();
        model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        return root;
    }

    static GameObject Drawn(string name, Transform parent, Vector3 position, Quaternion rotation, Mesh mesh, Material[] materials)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        if (mesh == null) return go;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return go;
    }

    static VehicleSeat SeatAt(Transform parent, int index, Vector3 eye, Vector3 doorLocal)
    {
        var seat = new GameObject("Seat" + index);
        seat.transform.SetParent(parent, false);
        seat.transform.localPosition = eye;
        var door = new GameObject("Door").transform;
        door.SetParent(seat.transform, false);
        door.localPosition = doorLocal - eye;
        var component = seat.AddComponent<VehicleSeat>();
        component.Configure(index, door);
        return component;
    }

    static void BuildPrefab(VehicleModels.Parts p, PhysicsMaterial physics)
    {
        bool bike = p.Kind == VehicleKind.Motorcycle;
        var meshes = new List<Mesh> { p.Body, p.Steering, p.Swingarm };
        meshes.AddRange(p.Wheels);
        meshes.AddRange(p.WheelStatic);
        SaveMeshes(p.Name, meshes);

        var rules = VehicleFit(p).Apply(VehicleRules.For(p.Kind));
        var root = NewRoot(p.Name, rules, out var vehicle, out var model);
        Drawn("Body", model, Vector3.zero, Quaternion.identity, p.Body, p.BodyMaterials);

        // Colliders. Car: the lower body up to the belt line and the roof, the cabin between them open (shots reach
        // the occupants through the windows). Motorcycle: the frame only.
        var colliders = new GameObject("Colliders").transform;
        colliders.SetParent(root.transform, false);
        var b = p.BodyBounds;
        if (bike)
        {
            float bottom = p.WheelRadius * 0.9f;
            AddBox(colliders, "Frame", new Vector3(0f, (bottom + b.max.y - 0.25f) * 0.5f, b.center.z),
                new Vector3(Mathf.Min(0.4f, b.size.x), b.max.y - 0.25f - bottom, b.size.z * 0.75f), physics);
        }
        else
        {
            float bottom = p.WheelRadius * 0.85f;
            float belt = p.HasGlass ? p.GlassBounds.min.y + 0.04f : b.min.y + b.size.y * 0.6f;
            AddBox(colliders, "Chassis", new Vector3(b.center.x, (bottom + belt) * 0.5f, b.center.z), new Vector3(b.size.x - 0.06f, belt - bottom, b.size.z - 0.04f), physics);
            if (p.HasGlass)
            {
                var g = p.GlassBounds;
                float roofTop = Mathf.Max(g.max.y, b.max.y - 0.02f);
                AddBox(colliders, "Roof", new Vector3(g.center.x, roofTop - 0.05f, g.center.z), new Vector3(g.size.x * 0.92f, 0.1f, g.size.z * 0.7f), physics);
            }
        }

        // Wheels: under the root on a car (it does not lean), under the Model on a motorcycle.
        int n = p.WheelCentres.Length;
        var pivots = new Transform[n];
        var spins = new Transform[n];
        var front = new bool[n];
        string[] names = bike ? new[] { "Wheel_Front", "Wheel_Rear" } : new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        var wheelParent = bike ? model : root.transform;
        for (int i = 0; i < n; i++)
        {
            var pivot = new GameObject(names[i]).transform;
            pivot.SetParent(wheelParent, false);
            pivot.localPosition = p.WheelCentres[i];
            var spin = Drawn("Spin", pivot, Vector3.zero, Quaternion.identity, p.Wheels[i], p.WheelMaterials[i]).transform;
            if (p.WheelStatic[i] != null) Drawn("Brake", pivot, Vector3.zero, Quaternion.identity, p.WheelStatic[i], p.WheelStaticMaterials[i]);
            pivots[i] = pivot;
            spins[i] = spin;
            front[i] = bike ? i == 0 : i < 2;
        }

        // Steering part and grips (children of it, so the hands turn with it).
        var steering = Drawn(bike ? "Handlebar" : "SteeringWheel", model, p.SteeringPivot, p.SteeringRotation, p.Steering, p.SteeringMaterials).transform;
        var gripLeft = new GameObject("GripLeft").transform;
        gripLeft.SetParent(steering, false);
        gripLeft.localPosition = p.Grips[0];
        var gripRight = new GameObject("GripRight").transform;
        gripRight.SetParent(steering, false);
        gripRight.localPosition = p.Grips[1];

        Transform swing = null;
        if (p.Swingarm != null) swing = Drawn("Swingarm", model, p.SwingPivot, Quaternion.identity, p.Swingarm, p.SwingarmMaterials).transform;

        // Seats: car seats under the root, motorcycle seats under the Model (they lean with it). Doors beside each seat;
        // on a motorcycle the rider gets off on the left, the pillion on the right.
        var seatParent = bike ? model : root.transform;
        var seats = new VehicleSeat[p.SeatEyes.Length];
        for (int i = 0; i < seats.Length; i++)
        {
            var eye = p.SeatEyes[i];
            float side = bike ? (i == 0 ? -1f : 1f) : Mathf.Sign(eye.x == 0f ? -1f : eye.x);
            seats[i] = SeatAt(seatParent, i, eye, new Vector3(side * p.DoorOut, 0f, eye.z + (bike ? 0f : 0.1f)));
        }
        seats[0].ConfigureGrips(gripLeft, gripRight);

        float wheelbase = Mathf.Abs(p.WheelCentres[0].z - p.WheelCentres[n - 1].z);
        vehicle.Configure(p.Kind, model, seats, pivots, spins, front, wheelbase, Mathf.Max(-b.min.z, b.max.z));
        vehicle.ConfigureFit(VehicleFit(p), p.CentreOfMassZ);
        root.AddComponent<VehicleRig>().Configure(vehicle, steering, p.SteeringRatio, swing, 1, swing != null ? p.WheelCentres[1] : Vector3.zero);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(p.Name));
        Object.DestroyImmediate(root);
        Debug.Log($"[VehicleBuilder] saved {PrefabPath(p.Name)}: {p.Body.triangles.Length / 3} body tris, wheel r {p.WheelRadius:F3}, wheelbase {wheelbase:F2}, " +
            $"body {b.size}, seats {string.Join(" ", p.SeatEyes.Select(e => e.ToString("F2")))}");
    }

    static VehicleFit VehicleFit(VehicleModels.Parts p) =>
        new VehicleFit { MassKg = p.MassKg, WheelRadius = p.WheelRadius, CentreOfMassHeight = p.CentreOfMassHeight };

    static void AddBox(Transform parent, string name, Vector3 centre, Vector3 size, PhysicsMaterial material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.center = centre;
        box.size = size;
        box.sharedMaterial = material;
    }

    // Low friction so a vehicle on its side or roof slides instead of sticking; no bounce.
    static PhysicsMaterial PhysicsMaterialAsset()
    {
        var path = TestBoxBuilder.PrefabDir + "/Materials/Vehicle_Body.asset";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (mat == null)
        {
            mat = new PhysicsMaterial("Vehicle_Body");
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.dynamicFriction = 0.25f;
        mat.staticFriction = 0.3f;
        mat.bounciness = 0f;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        mat.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Static props: the mesh, a box collider round it, marked static (batched, part of the navmesh carve on rebuild).
    static void BuildProps()
    {
        var props = VehicleModels.BuildProps();
        var meshPath = MeshPath("Props");
        if (AssetDatabase.LoadMainAssetAtPath(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(props[0].Mesh, meshPath);
        for (int i = 1; i < props.Count; i++) AssetDatabase.AddObjectToAsset(props[i].Mesh, meshPath);
        foreach (var prop in props)
        {
            var go = new GameObject("Prop_" + prop.Name);
            go.AddComponent<MeshFilter>().sharedMesh = prop.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = prop.Materials;
            var box = go.AddComponent<BoxCollider>();
            box.center = prop.Bounds.center;
            box.size = prop.Bounds.size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.ContributeGI);
            PrefabUtility.SaveAsPrefabAsset(go, PropPath(prop.Name));
            Object.DestroyImmediate(go);
        }
        Debug.Log($"[VehicleBuilder] {props.Count} prop prefabs ({string.Join(", ", props.Select(p => $"{p.Name} {p.Mesh.triangles.Length / 3}t"))})");
    }

    // ---- Scene wiring ----

    static NetworkObject[] LoadPrefabs()
    {
        var names = Cars.Concat(Motorcycles).ToArray();
        var prefabs = new NetworkObject[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            prefabs[i] = AssetDatabase.LoadAssetAtPath<NetworkObject>(PrefabPath(names[i]));
            if (prefabs[i] == null) throw new FileNotFoundException("vehicle prefab missing (run VehicleBuilder.BuildPrefabs first): " + PrefabPath(names[i]));
        }
        return prefabs;
    }

    static int Model(string name) => Array.IndexOf(Cars.Concat(Motorcycles).ToArray(), name);

    static Transform Spot(MatchDirector director, int index, Vector3 position, float yawDeg)
    {
        var spot = new GameObject("VehicleSpot" + index).transform;
        spot.SetParent(director.transform, false);
        spot.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDeg, 0f));
        return spot;
    }

    // The test box: the first car and the first motorcycle facing east along the open strips south of the spawn wall
    // (z = -27 and -21.5; the vehicle tests drive them east), the other five parked facing north in the south-east corner.
    public static VehicleSpawner AddToTestBox(MatchDirector director)
    {
        var layout = new (string model, Vector3 at, float yaw)[]
        {
            (Cars[0], new Vector3(-22f, 0.3f, -27f), 90f),
            (Motorcycles[0], new Vector3(-22f, 0.3f, -21.5f), 90f),
            (Cars[1], new Vector3(18.5f, 0.3f, -25.5f), 0f),
            (Cars[2], new Vector3(22.5f, 0.3f, -25.5f), 0f),
            (Cars[3], new Vector3(26.6f, 0.3f, -25.5f), 0f),
            (Cars[4], new Vector3(19f, 0.3f, -19f), 0f),
            (Motorcycles[1], new Vector3(23.5f, 0.3f, -19f), 0f),
        };
        var spots = new Transform[layout.Length];
        var models = new int[layout.Length];
        for (int i = 0; i < layout.Length; i++)
        {
            spots[i] = Spot(director, i, layout[i].at, layout[i].yaw);
            models[i] = Model(layout[i].model);
        }
        var spawner = director.gameObject.AddComponent<VehicleSpawner>();
        spawner.Configure(LoadPrefabs(), spots, models, false, 0f, 0f, 0, null);
        EditorUtility.SetDirty(spawner);
        return spawner;
    }

    // The outdoor map: the cars' and motorcycles' SpawnCount parked on road shoulders near compounds (VehicleParking),
    // the car models in turn and the two motorcycles in turn, alternating cars and motorcycles so both spread over the
    // map; the sea level; a 4 m road map so the vehicles know when they are off the road; and the generic pack's
    // bodies parked or wrecked on further shoulders near town lots. `height` gives the ground height.
    public static VehicleSpawner AddToWorld(MatchDirector director, WorldPlan plan, Func<float, float, float> height)
    {
        const float cellSize = 4f;
        var car = new VehicleRules();
        var bike = VehicleRules.MotorcycleDefaults();
        var picked = VehicleParking.Pick(plan, car.SpawnCount + bike.SpawnCount, car.ParkingSpacingMeters);
        var spots = new List<Transform>();
        var models = new List<int>();
        int cars = 0, bikes = 0;
        for (int i = 0; i < picked.Count; i++)
        {
            var p = picked[i];
            bool wantBike = bikes < bike.SpawnCount && (cars >= car.SpawnCount || i % 2 == 1);
            models.Add(Model(wantBike ? Motorcycles[bikes % Motorcycles.Length] : Cars[cars % Cars.Length]));
            if (wantBike) bikes++; else cars++;
            spots.Add(Spot(director, i, new Vector3(p.X, GroundUnder(p, height) + 0.25f, p.Z), p.YawDeg));
        }
        int cells = Mathf.CeilToInt(plan.Size / cellSize);
        var roads = RoadMask.Build(cells, cellSize, (x, z) => plan.RoadDistance(x, z) < 0.5f);
        var spawner = director.gameObject.AddComponent<VehicleSpawner>();
        spawner.Configure(LoadPrefabs(), spots.ToArray(), models.ToArray(), true, plan.SeaLevel, cellSize, cells, roads);
        EditorUtility.SetDirty(spawner);
        int props = PlaceProps(plan, height, picked);
        Debug.Log($"[VehicleBuilder] {cars} cars + {bikes} motorcycles parked (asked {car.SpawnCount} + {bike.SpawnCount}), {props} parked/wrecked props, road map {cells}x{cells}");
        return spawner;
    }

    // On the highest corner of the footprint, so the vehicle settles onto its wheels.
    static float GroundUnder(ParkingSpot p, Func<float, float, float> height)
    {
        float yaw = p.YawDeg * Mathf.Deg2Rad;
        float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw);
        float ground = float.MinValue;
        for (int c = 0; c < 4; c++)
        {
            float f = (c & 1) == 0 ? VehicleParking.HalfLength : -VehicleParking.HalfLength;
            float r = (c & 2) == 0 ? VehicleParking.HalfWidth : -VehicleParking.HalfWidth;
            ground = Mathf.Max(ground, height(p.X + fx * f + fz * r, p.Z + fz * f - fx * r));
        }
        return ground;
    }

    static float LowestUnder(ParkingSpot p, Func<float, float, float> height)
    {
        float yaw = p.YawDeg * Mathf.Deg2Rad;
        float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw);
        float ground = float.MaxValue;
        for (int c = 0; c < 5; c++)
        {
            float f = c == 4 ? 0f : (c & 1) == 0 ? VehicleParking.HalfLength : -VehicleParking.HalfLength;
            float r = c == 4 ? 0f : (c & 2) == 0 ? VehicleParking.HalfWidth : -VehicleParking.HalfWidth;
            ground = Mathf.Min(ground, height(p.X + fx * f + fz * r, p.Z + fz * f - fx * r));
        }
        return ground;
    }

    // Parked cars (and a wreck in every three) on shoulders near town lots, away from the vehicle spawns.
    static int PlaceProps(WorldPlan plan, Func<float, float, float> height, List<ParkingSpot> taken)
    {
        var prefabs = new List<GameObject>();
        var wrecks = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { VehicleModels.OutDir + "/Props" }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            (go.name.EndsWith("_Wreck") ? wrecks : prefabs).Add(go);
        }
        if (prefabs.Count == 0) return 0;
        prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        wrecks.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var root = new GameObject(PropRootName).transform;
        var candidates = VehicleParking.Candidates(plan).Where(c => InTown(plan, c)).ToList();
        var chosen = new List<ParkingSpot>();
        var avoid = new List<ParkingSpot>(taken);
        var rng = new System.Random(plan.Seed ^ 0x5eed);
        int limit = Mathf.Min(MaxProps, Mathf.RoundToInt(candidates.Count * PropShare));
        foreach (var c in candidates.OrderBy(_ => rng.Next()))
        {
            if (chosen.Count >= limit) break;
            if (avoid.Any(o => Sq(o.X - c.X) + Sq(o.Z - c.Z) < PropSpacing * PropSpacing * (chosen.Contains(o) ? 1f : 0.35f))) continue;
            chosen.Add(c);
            avoid.Add(c);
        }
        for (int i = 0; i < chosen.Count; i++)
        {
            var c = chosen[i];
            bool wreck = i % 3 == 2 && wrecks.Count > 0;
            var prefab = wreck ? wrecks[i % wrecks.Count] : prefabs[i % prefabs.Count];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(root, true);
            // Parked along the road (either way round); a wreck at an angle.
            float yaw = c.YawDeg + (i % 2 == 0 ? 0f : 180f) + (wreck ? (float)(rng.NextDouble() * 50.0 - 25.0) : 0f);
            float y = wreck ? LowestUnder(c, height) : GroundUnder(c, height);
            go.transform.SetPositionAndRotation(new Vector3(c.X, y, c.Z), Quaternion.Euler(0f, yaw, 0f));
            go.name = $"{prefab.name}_{i:00}";
        }
        return chosen.Count;
    }

    static float Sq(float v) => v * v;

    static bool InTown(WorldPlan plan, ParkingSpot c)
    {
        foreach (var lot in plan.Lots)
            if (lot.Distance(c.X, c.Z) < VehicleParking.NearLotMeters && lot.Region >= 0 && lot.Region < plan.Regions.Count
                && (plan.Regions[lot.Region].Kind == RegionKind.Town || plan.Regions[lot.Region].Kind == RegionKind.Factory))
                return true;
        return false;
    }

    // ---- Rebuilding just the vehicles in the saved scenes ----

    // tools/unity.sh run VehicleBuilder.UpdateScenes: new prefabs, the spawnable list, and the TestBox's and World's
    // spawners and props replaced (nothing else in either scene is touched).
    public static void UpdateScenes()
    {
        try
        {
            BuildPrefabs();
            RebuildSpawnablePrefabs();
            UpdateScene(TestBoxBuilder.ScenePath, director => AddToTestBox(director));
            UpdateScene(WorldBuilder.ScenePath, director =>
            {
                var plan = WorldGenerator.Generate(LoadWorldSpec());
                var terrain = Object.FindAnyObjectByType<Terrain>();
                Func<float, float, float> height = (x, z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                AddToWorld(director, plan, height);
            });
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[VehicleBuilder] FAILED: " + e.Message);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    // Prefabs only (no scenes): tools/unity.sh run VehicleBuilder.BuildPrefabsOnly
    public static void BuildPrefabsOnly()
    {
        try { BuildPrefabs(); }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    static void UpdateScene(string path, Action<MatchDirector> add)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var director = Object.FindAnyObjectByType<MatchDirector>();
        if (director == null) throw new Exception("no MatchDirector in " + path);
        foreach (var s in director.GetComponents<VehicleSpawner>()) Object.DestroyImmediate(s);
        for (int i = director.transform.childCount - 1; i >= 0; i--)
        {
            var child = director.transform.GetChild(i);
            if (child.name.StartsWith("VehicleSpot", StringComparison.Ordinal)) Object.DestroyImmediate(child.gameObject);
        }
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == PropRootName) Object.DestroyImmediate(go);
        add(director);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[VehicleBuilder] updated " + path);
    }

    static WorldSpec LoadWorldSpec()
    {
        var text = Resources.Load<TextAsset>("GameData/world");
        var spec = text != null ? JsonUtility.FromJson<WorldSpec>(text.text) : null;
        return spec != null && spec.Regions != null && spec.Regions.Length > 0 ? spec : WorldSpec.CreateDefault();
    }

    // Fish-Net's prefab collection generator is internal (same call as TestBoxBuilder's).
    static void RebuildSpawnablePrefabs()
    {
        var generator = typeof(NetworkManager).Assembly.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator", true);
        var method = generator.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        method.Invoke(null, new object[] { null, true, true });
    }
}

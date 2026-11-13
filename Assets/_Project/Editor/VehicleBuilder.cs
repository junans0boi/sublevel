using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Component.Transforming;
using FishNet.Object;
using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// The vehicles of ADR-065 (5), built in code (no downloaded model: the CC0 kits are cartoon/low-poly and no fitting
// CC0 model with a direct download was found). Placeholder-grade bodies on the shared Vehicle framework, meant to be
// replaced under each prefab's "Model" child by a model builder later (docs/handoff/VEHICLE_API.md):
// - Car: a generic compact crossover — bevelled extruded body with cut wheel arches, a tapered glass cabin with
//   pillars, bumpers, lamps, mirrors, a visible interior, separate wheels (tyre, rim, five spokes).
// - Motorcycle: a generic standard bike — tube frame, tank, saddle, engine block, fork, handlebar, headlight,
//   exhaust, two spoked wheels.
// No brand, badge, logo or recognisable model shape; plates are blank. Materials: URP Lit with the project's CC0
// PBR sets (PaintedMetal014 normal on the paint, Metal022 normal on the rims) and plain colours.
//
// BuildPrefabs saves Prefabs/M1/Car.prefab and Motorcycle.prefab (call before the spawnable prefab list is rebuilt);
// AddToTestBox / AddToWorld put a VehicleSpawner on the match object of the scene being built.
public static class VehicleBuilder
{
    public const string CarName = "Car", MotorcycleName = "Motorcycle";
    public static string PrefabPath(string name) => TestBoxBuilder.PrefabDir + "/" + name + ".prefab";
    static string MaterialDir => TestBoxBuilder.PrefabDir + "/Materials";
    static string MeshPath => TestBoxBuilder.PrefabDir + "/Vehicle_Meshes.asset";
    const string TextureDir = "Assets/_Project/Art/Materials";

    // Body submeshes / materials, in this order.
    const int Paint = 0, Glass = 1, Plastic = 2, Interior = 3, Lamp = 4, TailLamp = 5, Plate = 6, BodyMaterials = 7;
    // Wheel submeshes.
    const int Tyre = 0, RimMetal = 1, RimDark = 2, WheelMaterials = 3;

    // Car dimensions (m). Origin on the ground between the axles, +Z forward, +X right.
    const float HalfWidth = 0.9f, Bevel = 0.035f;
    const float HalfTrack = 0.775f, HalfWheelbase = 1.3f, ArchRadius = 0.43f;
    const float TyreHalfWidth = 0.11f;
    const float HalfLength = 2.15f;
    const float BeltY = 1.0f;
    static readonly Vector3[] SeatEyes =
    {
        new Vector3(-0.38f, 1.32f, -0.15f), new Vector3(0.38f, 1.32f, -0.15f),
        new Vector3(-0.38f, 1.32f, -1.05f), new Vector3(0.38f, 1.32f, -1.05f),
    };
    const float DoorOut = 1.5f; // door points this far out from the centre line

    // Motorcycle dimensions.
    const float BikeHalfWheelbase = 0.72f, BikeTyreHalfWidth = 0.06f, BikeHalfLength = 1.1f;
    static readonly Vector3[] BikeSeatEyes = { new Vector3(0f, 1.58f, -0.18f), new Vector3(0f, 1.66f, -0.62f) };
    const float BikeSideOut = 0.9f; // rider gets off on the left, pillion on the right

    // ---- Prefabs ----

    public static void BuildPrefabs()
    {
        Directory.CreateDirectory(MaterialDir);
        var mats = Materials();
        var car = new VehicleRules();
        var bike = VehicleRules.MotorcycleDefaults();
        var bodyMesh = BuildBody();
        var leftWheel = BuildWheel(-1f, car.WheelRadius, TyreHalfWidth, "Car_Wheel_Left");
        var rightWheel = BuildWheel(1f, car.WheelRadius, TyreHalfWidth, "Car_Wheel_Right");
        var bikeMesh = BuildBikeBody();
        var bikeWheel = BuildWheel(1f, bike.WheelRadius, BikeTyreHalfWidth, "Bike_Wheel");
        SaveMeshes(bodyMesh, leftWheel, rightWheel, bikeMesh, bikeWheel);
        BuildCarPrefab(car, mats, bodyMesh, leftWheel, rightWheel);
        BuildMotorcyclePrefab(bike, mats, bikeMesh, bikeWheel);
    }

    // The root every vehicle prefab shares: physics body, network identity and sync, the extension state, the
    // Vehicle itself, and an empty "Model" child for the visuals.
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
        model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        return root;
    }

    static Transform Wheel(Transform parent, string name, Vector3 centre, Mesh mesh, CarMaterials mats, out Transform spin)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = centre;
        var go = new GameObject("Spin");
        go.transform.SetParent(pivot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { mats.Tyre, mats.Rim, mats.Plastic };
        spin = go.transform;
        return pivot;
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

    static NetworkObject Save(GameObject root, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
        Object.DestroyImmediate(root);
        Debug.Log($"[VehicleBuilder] saved {PrefabPath(name)}");
        return prefab.GetComponent<NetworkObject>();
    }

    static NetworkObject BuildCarPrefab(VehicleRules rules, CarMaterials mats, Mesh bodyMesh, Mesh leftWheel, Mesh rightWheel)
    {
        var root = NewRoot(CarName, rules, out var vehicle, out var model);
        model.gameObject.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
        model.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { mats.Paint, mats.Glass, mats.Plastic, mats.Interior, mats.Lamp, mats.TailLamp, mats.Plate };

        // Colliders: the lower body up to the belt line and a thin roof. The cabin between them is open, so shots
        // from the seats leave through the windows and shots from outside reach the occupants through them.
        var physics = PhysicsMaterialAsset();
        var colliders = new GameObject("Colliders").transform;
        colliders.SetParent(root.transform, false);
        AddBox(colliders, "Chassis", new Vector3(0f, 0.655f, -0.02f), new Vector3(HalfWidth * 2f, 0.69f, HalfLength * 2f), physics);
        AddBox(colliders, "Roof", new Vector3(0f, 1.535f, -0.6f), new Vector3(1.4f, 0.08f, 1.75f), physics);

        // Wheels FL, FR, RL, RR under the root (the car's model does not lean).
        var pivots = new Transform[4];
        var spins = new Transform[4];
        var names = new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        for (int i = 0; i < 4; i++)
        {
            bool left = i == Vehicle.FrontLeft || i == Vehicle.RearLeft;
            bool front = i == Vehicle.FrontLeft || i == Vehicle.FrontRight;
            pivots[i] = Wheel(root.transform, names[i], new Vector3(left ? -HalfTrack : HalfTrack, rules.WheelRadius, front ? HalfWheelbase : -HalfWheelbase),
                left ? leftWheel : rightWheel, mats, out spins[i]);
        }

        var seats = new VehicleSeat[SeatEyes.Length];
        for (int i = 0; i < SeatEyes.Length; i++)
            seats[i] = SeatAt(root.transform, i, SeatEyes[i], new Vector3(Mathf.Sign(SeatEyes[i].x) * DoorOut, 0f, SeatEyes[i].z + 0.1f));
        vehicle.Configure(VehicleKind.Car, model, seats, pivots, spins, new[] { true, true, false, false }, HalfWheelbase * 2f, HalfLength);
        return Save(root, CarName);
    }

    // The motorcycle: everything drawn, the wheels and the seats sit under the Model so they lean and wheelie with it.
    // The collider is the frame's box only: riders have no body around them.
    static NetworkObject BuildMotorcyclePrefab(VehicleRules rules, CarMaterials mats, Mesh bikeMesh, Mesh wheel)
    {
        var root = NewRoot(MotorcycleName, rules, out var vehicle, out var model);
        var visual = new GameObject("Body");
        visual.transform.SetParent(model, false);
        visual.AddComponent<MeshFilter>().sharedMesh = bikeMesh;
        visual.AddComponent<MeshRenderer>().sharedMaterials = new[] { mats.Paint, mats.Glass, mats.Plastic, mats.Interior, mats.Lamp, mats.TailLamp, mats.Plate };

        var physics = PhysicsMaterialAsset();
        var colliders = new GameObject("Colliders").transform;
        colliders.SetParent(root.transform, false);
        AddBox(colliders, "Frame", new Vector3(0f, 0.66f, 0f), new Vector3(0.36f, 0.5f, 1.6f), physics);

        var pivots = new Transform[2];
        var spins = new Transform[2];
        pivots[0] = Wheel(model, "Wheel_Front", new Vector3(0f, rules.WheelRadius, BikeHalfWheelbase), wheel, mats, out spins[0]);
        pivots[1] = Wheel(model, "Wheel_Rear", new Vector3(0f, rules.WheelRadius, -BikeHalfWheelbase), wheel, mats, out spins[1]);

        var seats = new[]
        {
            SeatAt(model, 0, BikeSeatEyes[0], new Vector3(-BikeSideOut, 0f, BikeSeatEyes[0].z)),
            SeatAt(model, 1, BikeSeatEyes[1], new Vector3(BikeSideOut, 0f, BikeSeatEyes[1].z)),
        };
        vehicle.Configure(VehicleKind.Motorcycle, model, seats, pivots, spins, new[] { true, false }, BikeHalfWheelbase * 2f, BikeHalfLength);
        return Save(root, MotorcycleName);
    }

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
        var path = MaterialDir + "/Vehicle_Body.asset";
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

    static void SaveMeshes(params Mesh[] meshes)
    {
        if (AssetDatabase.LoadMainAssetAtPath(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(meshes[0], MeshPath);
        for (int i = 1; i < meshes.Length; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshPath);
        AssetDatabase.SaveAssets();
    }

    // ---- Scene wiring ----

    static NetworkObject[] LoadPrefabs()
    {
        var names = new[] { CarName, MotorcycleName }; // by VehicleKind
        var prefabs = new NetworkObject[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            prefabs[i] = AssetDatabase.LoadAssetAtPath<NetworkObject>(PrefabPath(names[i]));
            if (prefabs[i] == null) throw new FileNotFoundException("vehicle prefab missing (run TestBoxBuilder.Build first): " + PrefabPath(names[i]));
        }
        return prefabs;
    }

    static Transform Spot(MatchDirector director, int index, Vector3 position, float yawDeg)
    {
        var spot = new GameObject("VehicleSpot" + index).transform;
        spot.SetParent(director.transform, false);
        spot.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDeg, 0f));
        return spot;
    }

    // The test box: one car and one motorcycle on the south side, both facing east along open strips south of the
    // spawn wall (the car at z = -27, the motorcycle at z = -21.5).
    public static VehicleSpawner AddToTestBox(MatchDirector director)
    {
        var spots = new[]
        {
            Spot(director, 0, new Vector3(-22f, 0.3f, -27f), 90f),
            Spot(director, 1, new Vector3(-22f, 0.3f, -21.5f), 90f),
        };
        var spawner = director.gameObject.AddComponent<VehicleSpawner>();
        spawner.Configure(LoadPrefabs(), spots, new[] { (int)VehicleKind.Car, (int)VehicleKind.Motorcycle }, false, 0f, 0f, 0, null);
        EditorUtility.SetDirty(spawner);
        return spawner;
    }

    // The outdoor map: the cars' and motorcycles' SpawnCount (6 + 4) parked on road shoulders near compounds
    // (VehicleParking, alternating types so both spread over the map), the sea level, and a 4 m road map so the
    // vehicles know when they are off the road. `height` gives the ground height.
    public static VehicleSpawner AddToWorld(MatchDirector director, WorldPlan plan, Func<float, float, float> height)
    {
        const float cellSize = 4f;
        var car = new VehicleRules();
        var bike = VehicleRules.MotorcycleDefaults();
        var picked = VehicleParking.Pick(plan, car.SpawnCount + bike.SpawnCount, car.ParkingSpacingMeters);
        var spots = new List<Transform>();
        var kinds = new List<int>();
        int cars = 0, bikes = 0;
        for (int i = 0; i < picked.Count; i++)
        {
            var p = picked[i];
            // On the highest corner, a little above, so the vehicle settles onto its wheels.
            float yaw = p.YawDeg * Mathf.Deg2Rad;
            float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw);
            float ground = float.MinValue;
            for (int c = 0; c < 4; c++)
            {
                float f = (c & 1) == 0 ? VehicleParking.HalfLength : -VehicleParking.HalfLength;
                float r = (c & 2) == 0 ? VehicleParking.HalfWidth : -VehicleParking.HalfWidth;
                ground = Mathf.Max(ground, height(p.X + fx * f + fz * r, p.Z + fz * f - fx * r));
            }
            bool wantBike = bikes < bike.SpawnCount && (cars >= car.SpawnCount || i % 2 == 1);
            if (wantBike) bikes++; else cars++;
            kinds.Add((int)(wantBike ? VehicleKind.Motorcycle : VehicleKind.Car));
            spots.Add(Spot(director, i, new Vector3(p.X, ground + 0.25f, p.Z), p.YawDeg));
        }
        int cells = Mathf.CeilToInt(plan.Size / cellSize);
        var roads = RoadMask.Build(cells, cellSize, (x, z) => plan.RoadDistance(x, z) < 0.5f);
        var spawner = director.gameObject.AddComponent<VehicleSpawner>();
        spawner.Configure(LoadPrefabs(), spots.ToArray(), kinds.ToArray(), true, plan.SeaLevel, cellSize, cells, roads);
        EditorUtility.SetDirty(spawner);
        Debug.Log($"[VehicleBuilder] {cars} cars + {bikes} motorcycles parked (asked {car.SpawnCount} + {bike.SpawnCount}), road map {cells}x{cells}");
        return spawner;
    }

    // ---- Motorcycle body ----

    // Tube frame, tank, saddle (rider and pillion), engine block, fork, handlebar with grips, headlight, tail lamp,
    // exhaust, fenders. Colours: paint on tank and fenders, dark plastic on frame and saddle, rim metal parts in
    // Plastic / Interior / Lamp materials of the shared set.
    static Mesh BuildBikeBody()
    {
        var kit = new MeshKit(BodyMaterials);
        float r = VehicleRules.MotorcycleDefaults().WheelRadius;
        var frontAxle = new Vector3(0f, r, BikeHalfWheelbase);
        var rearAxle = new Vector3(0f, r, -BikeHalfWheelbase);
        var head = new Vector3(0f, 1.02f, 0.52f);       // steering head
        var seatFront = new Vector3(0f, 0.86f, 0.05f);
        var seatRear = new Vector3(0f, 0.92f, -0.78f);
        // Frame tubes: backbone, down tube, cradle, rear subframe, swingarm.
        Beam(kit, Plastic, head, seatFront + new Vector3(0f, -0.05f, 0f), 0.06f, Vector3.up);
        Beam(kit, Plastic, head + new Vector3(0f, -0.08f, 0f), new Vector3(0f, 0.42f, 0.22f), 0.055f, Vector3.up);
        Beam(kit, Plastic, new Vector3(0f, 0.42f, 0.22f), new Vector3(0f, 0.42f, -0.3f), 0.05f, Vector3.up);
        Beam(kit, Plastic, seatFront + new Vector3(0f, -0.05f, 0f), seatRear + new Vector3(0f, -0.1f, 0f), 0.04f, Vector3.up);
        for (int side = -1; side <= 1; side += 2)
            Beam(kit, Plastic, new Vector3(side * 0.08f, 0.45f, -0.25f), rearAxle + new Vector3(side * 0.08f, 0f, 0f), 0.045f, Vector3.up);
        // Engine block and exhaust.
        RoundedBox(kit, Interior, new Vector3(0f, 0.5f, -0.02f), new Vector3(0.3f, 0.3f, 0.42f), 0.05f);
        Beam(kit, Plastic, new Vector3(0.12f, 0.42f, 0.15f), new Vector3(0.17f, 0.38f, -0.55f), 0.07f, Vector3.up);
        Beam(kit, Plastic, new Vector3(0.17f, 0.38f, -0.55f), new Vector3(0.17f, 0.43f, -0.95f), 0.09f, Vector3.up);
        // Tank and saddle.
        RoundedBox(kit, Paint, new Vector3(0f, 0.98f, 0.3f), new Vector3(0.3f, 0.2f, 0.5f), 0.07f);
        RoundedBox(kit, Interior, new Vector3(0f, 0.88f, -0.18f), new Vector3(0.28f, 0.08f, 0.5f), 0.03f);
        RoundedBox(kit, Interior, new Vector3(0f, 0.95f, -0.62f), new Vector3(0.26f, 0.08f, 0.36f), 0.03f);
        // Fork, handlebar, grips, headlight.
        for (int side = -1; side <= 1; side += 2)
            Beam(kit, Plastic, head + new Vector3(side * 0.08f, 0.04f, 0f), frontAxle + new Vector3(side * 0.08f, 0f, 0f), 0.04f, Vector3.up);
        var bar = head + new Vector3(0f, 0.16f, -0.06f);
        Beam(kit, Plastic, bar + new Vector3(-0.36f, 0.02f, -0.05f), bar + new Vector3(0.36f, 0.02f, -0.05f), 0.03f, Vector3.up);
        Beam(kit, Plastic, head + new Vector3(0f, 0.04f, 0f), bar, 0.04f, Vector3.up);
        RoundedBox(kit, Lamp, head + new Vector3(0f, -0.04f, 0.14f), new Vector3(0.18f, 0.16f, 0.1f), 0.04f);
        RoundedBox(kit, TailLamp, seatRear + new Vector3(0f, 0.02f, -0.22f), new Vector3(0.14f, 0.06f, 0.05f), 0.02f);
        // Fenders over the wheels.
        RoundedBox(kit, Paint, frontAxle + new Vector3(0f, r + 0.06f, 0.02f), new Vector3(0.16f, 0.04f, 0.5f), 0.015f);
        RoundedBox(kit, Paint, rearAxle + new Vector3(0f, r + 0.1f, -0.05f), new Vector3(0.18f, 0.04f, 0.45f), 0.015f);
        Box(kit, Plate, rearAxle + new Vector3(0f, r + 0.12f, -0.3f), Quaternion.Euler(-20f, 0f, 0f), new Vector3(0.18f, 0.12f, 0.01f));
        return kit.ToMesh("Bike_Body");
    }

    // ---- Materials ----

    struct CarMaterials
    {
        public Material Paint, Glass, Plastic, Interior, Lamp, TailLamp, Plate, Tyre, Rim;
    }

    static CarMaterials Materials()
    {
        var paint = Lit("Vehicle_Paint", new Color(0.21f, 0.28f, 0.32f), 0.45f, 0.62f);
        paint.SetTexture("_BumpMap", SceneLook.Load<Texture2D>($"{TextureDir}/PaintedMetal014/PaintedMetal014_normal.jpg"));
        paint.SetFloat("_BumpScale", 0.12f);
        paint.EnableKeyword("_NORMALMAP");
        var rim = Lit("Vehicle_Rim", new Color(0.62f, 0.63f, 0.65f), 0.85f, 0.55f);
        rim.SetTexture("_BumpMap", SceneLook.Load<Texture2D>($"{TextureDir}/Metal022/Metal022_normal.jpg"));
        rim.SetFloat("_BumpScale", 0.2f);
        rim.EnableKeyword("_NORMALMAP");
        return new CarMaterials
        {
            Paint = paint,
            Glass = GlassMaterial(),
            Plastic = Lit("Vehicle_Plastic", new Color(0.07f, 0.07f, 0.075f), 0f, 0.25f),
            Interior = Lit("Vehicle_Interior", new Color(0.1f, 0.1f, 0.11f), 0f, 0.1f),
            Lamp = Lit("Vehicle_Lamp", new Color(0.85f, 0.87f, 0.9f), 0.2f, 0.95f),
            TailLamp = Lit("Vehicle_TailLamp", new Color(0.45f, 0.04f, 0.03f), 0f, 0.8f),
            Plate = Lit("Vehicle_Plate", new Color(0.78f, 0.78f, 0.76f), 0f, 0.35f),
            Tyre = Lit("Vehicle_Tyre", new Color(0.05f, 0.05f, 0.05f), 0f, 0.15f),
            Rim = rim,
        };
    }

    static Material LitAsset(string name)
    {
        var path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        return mat;
    }

    static Material Lit(string name, Color color, float metallic, float smoothness)
    {
        var mat = LitAsset(name);
        mat.SetColor("_BaseColor", color);
        mat.SetTexture("_BaseMap", null);
        mat.SetTexture("_MetallicGlossMap", null);
        mat.DisableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Tinted, see-through, glossy. The transparent setup is done by hand (normally the material inspector does it).
    static Material GlassMaterial()
    {
        var mat = Lit("Vehicle_Glass", new Color(0.06f, 0.08f, 0.1f, 0.55f), 0f, 0.95f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off); // the inside of the cabin is seen through the glass too
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---- Body ----

    static Mesh BuildBody()
    {
        var kit = new MeshKit(BodyMaterials);

        // Lower body: a side profile extruded across the car, edges bevelled, arches cut for the wheels. The belt
        // edge (top of the doors, between windscreen and tailgate) is the interior's floor-level lid.
        var lower = new List<Vector2>
        {
            Zy(2.02f, 0.32f), Zy(2.12f, 0.42f), Zy(2.15f, 0.62f), Zy(2.09f, 0.78f), Zy(1.96f, 0.86f), Zy(1.30f, 0.95f), Zy(1.08f, 0.99f),
            Zy(-1.90f, 1.00f), Zy(-2.08f, 0.96f), Zy(-2.15f, 0.80f), Zy(-2.14f, 0.55f), Zy(-2.06f, 0.34f), Zy(-1.76f, 0.30f),
        };
        AddArch(lower, -HalfWheelbase);
        lower.Add(Zy(-0.84f, 0.30f));
        lower.Add(Zy(0.84f, 0.30f));
        AddArch(lower, HalfWheelbase);
        lower.Add(Zy(1.76f, 0.30f));
        const int lidEdge = 6; // (1.08, 0.99) -> (-1.90, 1.00)
        Extrude(kit, lower, -HalfWidth, HalfWidth, Bevel, edge => edge == lidEdge ? Interior : Paint, Paint);

        Cabin(kit);

        // Bumpers, skirts, grille, lamps, plates, mirrors, handles.
        RoundedBox(kit, Plastic, new Vector3(0f, 0.46f, 2.07f), new Vector3(1.78f, 0.3f, 0.2f), 0.05f);
        RoundedBox(kit, Plastic, new Vector3(0f, 0.45f, -2.08f), new Vector3(1.78f, 0.26f, 0.18f), 0.05f);
        for (int side = -1; side <= 1; side += 2)
        {
            RoundedBox(kit, Plastic, new Vector3(side * 0.885f, 0.36f, 0f), new Vector3(0.06f, 0.12f, 1.62f), 0.02f);
            // Head lamps and tail lamps at the corners.
            RoundedBox(kit, Lamp, new Vector3(side * 0.66f, 0.76f, 2.07f), new Vector3(0.36f, 0.1f, 0.08f), 0.02f);
            RoundedBox(kit, TailLamp, new Vector3(side * 0.72f, 0.86f, -2.12f), new Vector3(0.26f, 0.12f, 0.06f), 0.02f);
            // Mirrors on short stalks at the front of the side windows.
            Box(kit, Plastic, new Vector3(side * 0.93f, 1.06f, 0.92f), Quaternion.identity, new Vector3(0.1f, 0.03f, 0.05f));
            RoundedBox(kit, Paint, new Vector3(side * 1.0f, 1.09f, 0.9f), new Vector3(0.1f, 0.11f, 0.15f), 0.025f);
            // Door handles.
            Box(kit, Plastic, new Vector3(side * 0.905f, 0.9f, -0.35f), Quaternion.identity, new Vector3(0.02f, 0.03f, 0.16f));
            Box(kit, Plastic, new Vector3(side * 0.905f, 0.9f, -1.3f), Quaternion.identity, new Vector3(0.02f, 0.03f, 0.16f));
            // Door gaps: thin dark lines on the sides.
            Box(kit, Plastic, new Vector3(side * 0.902f, 0.66f, 0.62f), Quaternion.identity, new Vector3(0.006f, 0.62f, 0.012f));
            Box(kit, Plastic, new Vector3(side * 0.902f, 0.66f, -0.6f), Quaternion.identity, new Vector3(0.006f, 0.62f, 0.012f));
            Box(kit, Plastic, new Vector3(side * 0.902f, 0.66f, -1.78f), Quaternion.identity, new Vector3(0.006f, 0.62f, 0.012f));
        }
        RoundedBox(kit, Plastic, new Vector3(0f, 0.7f, 2.1f), new Vector3(0.9f, 0.14f, 0.06f), 0.02f); // grille
        Box(kit, Plate, new Vector3(0f, 0.47f, 2.175f), Quaternion.identity, new Vector3(0.48f, 0.11f, 0.01f));
        Box(kit, Plate, new Vector3(0f, 0.68f, -2.16f), Quaternion.identity, new Vector3(0.48f, 0.11f, 0.01f));

        Interior(kit);
        return kit.ToMesh("Car_Body");
    }

    static Vector2 Zy(float z, float y) => new Vector2(z, y);

    // A wheel arch over the axle at `centreZ`, from its rear end to its front end (the profile's bottom runs rear to front).
    static void AddArch(List<Vector2> points, float centreZ)
    {
        const int steps = 10;
        const float centreY = 0.34f;
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.PI * (1f - (float)i / steps);
            points.Add(Zy(centreZ + Mathf.Cos(a) * ArchRadius, centreY + Mathf.Sin(a) * ArchRadius));
        }
    }

    // The glasshouse: windscreen, side windows and tailgate glass in one tapered shape (narrower at the roof), a
    // body-colour roof panel, pillars and roof rails on top.
    static void Cabin(MeshKit kit)
    {
        var profile = new List<Vector2> { Zy(1.06f, 0.99f), Zy(0.22f, 1.5f), Zy(-1.45f, 1.55f), Zy(-1.98f, 1.04f) };
        float Half(float y) => Mathf.Lerp(0.86f, 0.68f, Mathf.InverseLerp(BeltY, 1.55f, y));
        Vector3 At(int i, float side) => new Vector3(side * Half(profile[i].y), profile[i].y, profile[i].x);
        int[] subs = { Glass, Paint, Glass };
        for (int i = 0; i < 3; i++)
        {
            int j = i + 1;
            var e = profile[j] - profile[i];
            // A counterclockwise (z, y) edge faces out along (e.y, -e.x): in 3D (0, -e.x, e.y).
            kit.Quad(subs[i], At(i, -1f), At(j, -1f), At(j, 1f), At(i, 1f), new Vector3(0f, -e.x, e.y));
        }
        // Side windows: the profile at each side (a flat, slightly inclined quadrilateral).
        for (int side = -1; side <= 1; side += 2)
        {
            var hint = new Vector3(side, 0f, 0f);
            kit.Tri(Glass, At(0, side), At(1, side), At(2, side), hint);
            kit.Tri(Glass, At(0, side), At(2, side), At(3, side), hint);
            // Pillars: A along the windscreen edge, B between the doors, C/D along the tailgate edge; rails on the roof.
            Beam(kit, Paint, At(0, side) + hint * 0.01f, At(1, side) + hint * 0.01f, 0.07f, hint);
            Beam(kit, Plastic, new Vector3(side * Half(1.0f), 1.0f, -0.58f) + hint * 0.012f, new Vector3(side * Half(1.52f), 1.52f, -0.62f) + hint * 0.012f, 0.09f, hint);
            Beam(kit, Paint, At(2, side) + hint * 0.01f, At(3, side) + hint * 0.01f, 0.16f, hint);
            Beam(kit, Plastic, new Vector3(side * 0.6f, 1.575f, 0.1f), new Vector3(side * 0.6f, 1.6f, -1.38f), 0.035f, Vector3.up);
        }
    }

    // A square beam of width `w` from a to b, its flat side facing `facing`.
    static void Beam(MeshKit kit, int sub, Vector3 a, Vector3 b, float w, Vector3 facing)
    {
        var dir = b - a;
        if (dir.sqrMagnitude < 1e-6f) return;
        var up = Vector3.Cross(dir.normalized, facing.normalized).sqrMagnitude > 1e-4f ? facing
            : Mathf.Abs(dir.normalized.y) < 0.9f ? Vector3.up : Vector3.forward;
        Box(kit, sub, (a + b) * 0.5f, Quaternion.LookRotation(dir, up), new Vector3(w, w, dir.magnitude));
    }

    // What shows through the glass: the dashboard, the steering wheel, the seat backs and headrests.
    static void Interior(MeshKit kit)
    {
        RoundedBox(kit, Interior, new Vector3(0f, 1.04f, 0.86f), new Vector3(1.6f, 0.1f, 0.36f), 0.03f);
        // Steering wheel in front of the driver, tilted toward them: a ring of short beams, a hub and a column.
        var centre = new Vector3(SeatEyes[0].x, 1.12f, SeatEyes[0].z + 0.47f);
        var tilt = Quaternion.Euler(-62f, 0f, 0f); // the wheel's axis points back and up at the driver
        const int segments = 16;
        const float radius = 0.17f;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            var p0 = centre + tilt * new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
            var p1 = centre + tilt * new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
            Beam(kit, Plastic, p0, p1, 0.025f, tilt * Vector3.up);
        }
        Box(kit, Plastic, centre, tilt, new Vector3(0.09f, 0.03f, 0.09f));
        Beam(kit, Plastic, centre, centre + tilt * Vector3.down * 0.25f, 0.05f, Vector3.up);
        foreach (var eye in SeatEyes)
        {
            RoundedBox(kit, Interior, new Vector3(eye.x, 1.1f, eye.z - 0.3f), new Vector3(0.48f, 0.22f, 0.1f), 0.03f);
            RoundedBox(kit, Interior, new Vector3(eye.x, 1.33f, eye.z - 0.27f), new Vector3(0.24f, 0.15f, 0.09f), 0.03f);
            Box(kit, Plastic, new Vector3(eye.x - 0.06f, 1.23f, eye.z - 0.28f), Quaternion.identity, new Vector3(0.015f, 0.08f, 0.015f));
            Box(kit, Plastic, new Vector3(eye.x + 0.06f, 1.23f, eye.z - 0.28f), Quaternion.identity, new Vector3(0.015f, 0.08f, 0.015f));
        }
    }

    // ---- Wheels ----

    // One wheel, its outer face toward `side` (-1 left, +1 right): a tyre with rounded shoulders, and inside its outer
    // sidewall a recessed rim (bright barrel and lip, dark centre disk) with five bright spokes and a hub.
    static Mesh BuildWheel(float side, float radius, float hw, string name)
    {
        var kit = new MeshKit(WheelMaterials);
        const int segments = 28;
        const float shoulder = 0.03f, hub = 0.06f;
        float rimRadius = radius * 0.74f;
        // Tread and shoulders.
        Ring(kit, Tyre, radius, radius, -hw + 0.02f, hw - 0.02f, segments, false);
        Ring(kit, Tyre, radius, radius - shoulder, hw - 0.02f, hw, segments, false);
        Ring(kit, Tyre, radius - shoulder, radius, -hw, -hw + 0.02f, segments, false);
        // Sidewalls: the outer one leaves the rim open, the inner one is closed.
        float outer = side * hw;
        RingFace(kit, Tyre, rimRadius, radius - shoulder, outer, segments, side);
        Disk(kit, Tyre, radius - shoulder, -outer, segments, -side);
        // The rim: a barrel from the sidewall in to the recessed centre disk, a lip at the sidewall.
        float recess = side * hw * 0.5f;
        Ring(kit, RimMetal, rimRadius, rimRadius, Mathf.Min(recess, outer), Mathf.Max(recess, outer), segments, true);
        RingFace(kit, RimMetal, rimRadius - 0.015f, rimRadius + 0.004f, outer + side * 0.002f, segments, side);
        Disk(kit, RimDark, rimRadius, recess, segments, side);
        for (int i = 0; i < 5; i++)
        {
            var rot = Quaternion.AngleAxis(i * 72f, Vector3.right);
            Box(kit, RimMetal, new Vector3(side * hw * 0.8f, 0f, 0f) + rot * new Vector3(0f, rimRadius * 0.55f, 0f), rot, new Vector3(0.03f, rimRadius * 0.76f, 0.045f));
        }
        Ring(kit, RimMetal, hub, hub, Mathf.Min(recess, outer), Mathf.Max(recess, outer), 16, false);
        Disk(kit, RimMetal, hub, outer, 16, side);
        return kit.ToMesh(name);
    }

    // A band around the X axis from x0 (radius r0) to x1 (radius r1), smooth around; its normals point away from
    // the axis, or toward it when `inward` (the inside of a rim barrel).
    static void Ring(MeshKit kit, int sub, float r0, float r1, float x0, float x1, int segments, bool inward)
    {
        float facing = inward ? -1f : 1f;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            var d0 = new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0));
            var d1 = new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1));
            var a = new Vector3(x0, 0f, 0f) + d0 * r0;
            var b = new Vector3(x0, 0f, 0f) + d1 * r0;
            var c = new Vector3(x1, 0f, 0f) + d1 * r1;
            var d = new Vector3(x1, 0f, 0f) + d0 * r1;
            kit.QuadSmooth(sub, a, b, c, d, d0 * facing, d1 * facing, d1 * facing, d0 * facing);
        }
    }

    // A flat annulus in the plane x = `x` between two radii, facing `side` along X.
    static void RingFace(MeshKit kit, int sub, float inner, float outer, float x, int segments, float side)
    {
        var n = new Vector3(side, 0f, 0f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            var d0 = new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0));
            var d1 = new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1));
            var p = new Vector3(x, 0f, 0f);
            kit.Quad(sub, p + d0 * inner, p + d1 * inner, p + d1 * outer, p + d0 * outer, n);
        }
    }

    // A flat disk in the plane x = `x`, facing `side` along X.
    static void Disk(MeshKit kit, int sub, float radius, float x, int segments, float side)
    {
        var n = new Vector3(side, 0f, 0f);
        var centre = new Vector3(x, 0f, 0f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            kit.Tri(sub, centre, centre + new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0)) * radius, centre + new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1)) * radius, n);
        }
    }

    // ---- Shapes ----

    // A box with its edges along X bevelled (a chamfered profile extruded across X, its side edges bevelled too).
    static void RoundedBox(MeshKit kit, int sub, Vector3 centre, Vector3 size, float bevel)
    {
        float hz = size.z * 0.5f, hy = size.y * 0.5f;
        float b = Mathf.Min(bevel, Mathf.Min(hz, hy) * 0.9f);
        var profile = new List<Vector2>
        {
            Zy(centre.z + hz, centre.y - hy + b), Zy(centre.z + hz, centre.y + hy - b), Zy(centre.z + hz - b, centre.y + hy),
            Zy(centre.z - hz + b, centre.y + hy), Zy(centre.z - hz, centre.y + hy - b), Zy(centre.z - hz, centre.y - hy + b),
            Zy(centre.z - hz + b, centre.y - hy), Zy(centre.z + hz - b, centre.y - hy),
        };
        Extrude(kit, profile, centre.x - size.x * 0.5f, centre.x + size.x * 0.5f, Mathf.Min(b, size.x * 0.45f), null, sub, sub);
    }

    // A plain box turned by `rotation`.
    static void Box(MeshKit kit, int sub, Vector3 centre, Quaternion rotation, Vector3 size)
    {
        var h = size * 0.5f;
        Vector3 P(float x, float y, float z) => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
        var right = rotation * Vector3.right;
        var up = rotation * Vector3.up;
        var forward = rotation * Vector3.forward;
        kit.Quad(sub, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), right);
        kit.Quad(sub, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), -right);
        kit.Quad(sub, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), up);
        kit.Quad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -up);
        kit.Quad(sub, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), forward);
        kit.Quad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -forward);
    }

    // Extrudes a side profile (points as (z, y)) across X from x0 to x1. The outer `bevel` at each side is a slanted
    // band from the full profile to the profile inset by `bevel`, and the side caps use the inset profile.
    // `edgeSub(i)` picks the submesh of the band along edge i -> i+1 (null: `defaultSub`); `capSub` is the caps'.
    static void Extrude(MeshKit kit, List<Vector2> source, float x0, float x1, float bevel, Func<int, int> edgeSub, int defaultSub, int capSub = Paint)
    {
        var p = new List<Vector2>(source);
        bool reversed = SignedArea(p) < 0f;
        if (reversed) p.Reverse();
        var inset = Offset(p, bevel);
        int n = p.Count;
        float inner0 = x0 + bevel, inner1 = x1 - bevel;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            // Edge numbers refer to the profile as given.
            int sourceEdge = reversed ? (n - 2 - i + n) % n : i;
            int sub = edgeSub != null ? edgeSub(sourceEdge) : defaultSub;
            var e = p[j] - p[i];
            var outward = new Vector3(0f, -e.x, e.y); // counterclockwise (z, y): out is (e.y, -e.x) in (z, y)
            kit.Quad(sub, P(inner0, p[i]), P(inner0, p[j]), P(inner1, p[j]), P(inner1, p[i]), outward);
            kit.Quad(sub, P(x0, inset[i]), P(x0, inset[j]), P(inner0, p[j]), P(inner0, p[i]), outward.normalized + Vector3.left);
            kit.Quad(sub, P(inner1, p[i]), P(inner1, p[j]), P(x1, inset[j]), P(x1, inset[i]), outward.normalized + Vector3.right);
        }
        var tris = Triangulate(inset);
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            kit.Tri(capSub, P(x0, inset[tris[t]]), P(x0, inset[tris[t + 1]]), P(x0, inset[tris[t + 2]]), Vector3.left);
            kit.Tri(capSub, P(x1, inset[tris[t]]), P(x1, inset[tris[t + 1]]), P(x1, inset[tris[t + 2]]), Vector3.right);
        }
    }

    static Vector3 P(float x, Vector2 zy) => new Vector3(x, zy.y, zy.x);

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static float SignedArea(List<Vector2> p)
    {
        float area = 0f;
        for (int i = 0; i < p.Count; i++) area += Cross(p[i], p[(i + 1) % p.Count]);
        return area * 0.5f;
    }

    // Moves every point of a counterclockwise polygon `d` inward (mitred, the mitre limited at sharp corners).
    static List<Vector2> Offset(List<Vector2> p, float d)
    {
        int n = p.Count;
        var result = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            var prev = p[(i + n - 1) % n];
            var next = p[(i + 1) % n];
            var e0 = (p[i] - prev).normalized;
            var e1 = (next - p[i]).normalized;
            var n0 = new Vector2(-e0.y, e0.x); // inward of a counterclockwise edge
            var n1 = new Vector2(-e1.y, e1.x);
            var m = (n0 + n1);
            if (m.sqrMagnitude < 1e-8f) m = n0;
            m.Normalize();
            float scale = d / Mathf.Max(0.35f, Vector2.Dot(m, n0));
            result.Add(p[i] + m * scale);
        }
        return result;
    }

    // Ear clipping of a simple counterclockwise polygon; indices in threes.
    static List<int> Triangulate(List<Vector2> poly)
    {
        var result = new List<int>();
        var idx = new List<int>();
        for (int i = 0; i < poly.Count; i++) idx.Add(i);
        int guard = 0;
        while (idx.Count > 3 && guard++ < 10000)
        {
            bool clipped = false;
            for (int k = 0; k < idx.Count; k++)
            {
                int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                if (Cross(b - a, c - b) <= 1e-7f) continue; // a reflex or flat corner is not an ear
                bool blocked = false;
                for (int m = 0; m < idx.Count && !blocked; m++)
                {
                    int im = idx[m];
                    if (im == i0 || im == i1 || im == i2) continue;
                    blocked = InTriangle(poly[im], a, b, c);
                }
                if (blocked) continue;
                result.Add(i0);
                result.Add(i1);
                result.Add(i2);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                Debug.LogWarning("[VehicleBuilder] a cap polygon could not be fully triangulated");
                break;
            }
        }
        if (idx.Count == 3)
        {
            result.Add(idx[0]);
            result.Add(idx[1]);
            result.Add(idx[2]);
        }
        return result;
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(b - a, p - a) > 0f && Cross(c - b, p - b) > 0f && Cross(a - c, p - c) > 0f;

    // Collects triangles per submesh with their own (flat or given) normals and box-projected UVs (1 m per tile).
    sealed class MeshKit
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int>[] _triangles;

        public MeshKit(int submeshes)
        {
            _triangles = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) _triangles[i] = new List<int>();
        }

        // A flat triangle facing `outward` (its winding is turned round if it faces the other way).
        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, outward) < 0f)
            {
                (b, c) = (c, b);
                n = -n;
            }
            n.Normalize();
            Add(sub, a, b, c, n, n, n);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Tri(sub, a, b, c, outward);
            Tri(sub, a, c, d, outward);
        }

        // A quad with a normal per corner (smooth shading); the winding follows the average normal.
        public void QuadSmooth(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            var outward = na + nb + nc + nd;
            TriSmooth(sub, a, b, c, na, nb, nc, outward);
            TriSmooth(sub, a, c, d, na, nc, nd, outward);
        }

        void TriSmooth(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, outward) < 0f)
            {
                (b, c) = (c, b);
                (nb, nc) = (nc, nb);
            }
            Add(sub, a, b, c, na.normalized, nb.normalized, nc.normalized);
        }

        void Add(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            int i = _vertices.Count;
            var face = Vector3.Cross(b - a, c - a).normalized;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _normals.Add(na);
            _normals.Add(nb);
            _normals.Add(nc);
            _uvs.Add(Uv(a, face));
            _uvs.Add(Uv(b, face));
            _uvs.Add(Uv(c, face));
            _triangles[sub].Add(i);
            _triangles[sub].Add(i + 1);
            _triangles[sub].Add(i + 2);
        }

        static Vector2 Uv(Vector3 p, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return new Vector2(p.z, p.y);
            if (ay >= az) return new Vector2(p.x, p.z);
            return new Vector2(p.x, p.y);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _triangles.Length;
            for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}

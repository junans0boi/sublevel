using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Generates the training range scene (#153): a shooting lane to the north (targets at 10/25/50/100/200 m), a movement
// course to the east (low vault, crawl gap, 2.3 m ledge wall, stairs, swimming pit), the item table to the west, the
// vehicle pad and the door house to the south. Primitive boxes only (no external assets); the textures are the test
// box's CC0 sets. The match around it is MatchKit's host-run one without bots. Run headless:
//   tools/unity.sh run TrainingRangeBuilder.Build
// Needs the prefabs of TestBoxBuilder and VehicleBuilder to exist (they are committed).
public static class TrainingRangeBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/TrainingRange.unity";
    const string MaterialDir = "Assets/_Project/Prefabs/M2/Materials";

    // Ground: one slab with a hole for the swimming pit.
    const float X0 = -60f, X1 = 120f, Z0 = -60f, Z1 = 235f, Thick = 3.5f;
    const float PitX0 = 70f, PitX1 = 94f, PitZ0 = -21f, PitZ1 = -7f, PitFloor = -2.5f, WaterY = -0.15f;
    static readonly float[] LaneMeters = { 10f, 25f, 50f, 100f, 200f };
    const float CourseZ = -14f;
    const float DoorW = 1.3f, DoorH = 2.3f;

    static Transform _root;

    public static void Build()
    {
        Directory.CreateDirectory(MaterialDir);
        SceneLook.PrepareTextures();
        var post = RenderSetup.Apply();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneLook.SetupSky();
        SceneLook.AddLights();
        SceneLook.AddPostVolume(post);

        _root = new GameObject("Range").transform;
        var range = _root.gameObject.AddComponent<TrainingRange>();
        Ground();
        Lane();
        Course();
        range.HasPool = true;
        range.PoolCentre = new Vector2((PitX0 + PitX1) / 2f, (PitZ0 + PitZ1) / 2f);
        range.PoolSize = new Vector2(PitX1 - PitX0, PitZ1 - PitZ0);
        range.PoolSurfaceY = WaterY;
        range.Loot = ItemTable();
        range.DoorSpots = House();
        range.DoorWidth = DoorW;
        range.DoorHeight = DoorH;
        var pad = Group("VehiclePad");
        FlatBox(pad, "Asphalt", new Vector3(0f, 0.01f, -33f), new Vector3(30f, 0.02f, 14f), Flat("Asphalt", new Color(0.13f, 0.13f, 0.14f)), false);
        Sign("차량", new Vector3(0f, 3f, -33f), 1.2f, pad);
        Sign("스폰", new Vector3(0f, 2.4f, -3f), 1.1f);
        Sign("<- 아이템 테이블", new Vector3(-8f, 2.4f, 0f), 1f);
        Sign("움직임 코스 ->", new Vector3(8f, 2.4f, -6f), 1f);
        Sign("차량 / 문 집  (남쪽)", new Vector3(0f, 2.4f, -14f), 1f);
        Sign("사격장  (북쪽)", new Vector3(-9f, 2.4f, 8f), 1f);

        var director = MatchKit.AddMatch(new[] { MatchKit.At(0f, 0.1f, 0f, 0f) }, new MatchKit.Spawn[0], new MatchKit.Spawn[0]);
        VehicleBuilder.AddParked(director,
            (VehicleBuilder.Cars[0], new Vector3(-6f, 0.3f, -33f), 90f),
            (VehicleBuilder.Motorcycles[0], new Vector3(6f, 0.3f, -33f), 90f));

        var cam = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cam.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 400f;
        SceneLook.EnablePost(camera);
        cam.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(0f, 35f, -75f);
        cam.transform.LookAt(new Vector3(25f, 0f, 20f));

        EditorSceneManager.SaveScene(scene, ScenePath);
        SceneBuilder.RegisterBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("[TrainingRangeBuilder] Saved " + ScenePath);
        EditorApplication.Exit(0);
    }

    // ---- helpers ----

    static Material Flat(string name, Color color)
    {
        var path = $"{MaterialDir}/Training_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(_root, false);
        return t;
    }

    // Solid textured box between two corners.
    static GameObject Solid(Transform parent, string name, Vector3 min, Vector3 max, Material mat, float tile = 2.5f) =>
        SceneLook.WorldBox(name, parent, (min + max) * 0.5f, max - min, mat, tile);

    static GameObject FlatBox(Transform parent, string name, Vector3 centre, Vector3 size, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = centre;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    static void Sign(string text, Vector3 at, float size, Transform parent = null)
    {
        var go = new GameObject("Sign");
        go.transform.SetParent(parent != null ? parent : _root, false);
        go.transform.position = at;
        var label = go.AddComponent<TrainingLabel>();
        label.Text = text;
        label.Size = size;
        label.Color = new Color(1f, 0.82f, 0.4f);
    }

    // ---- areas ----

    static void Ground()
    {
        var g = Group("Ground");
        var floor = SceneLook.Floor;
        float y0 = -Thick, y1 = 0f;
        Solid(g, "GroundWest", new Vector3(X0, y0, Z0), new Vector3(PitX0, y1, Z1), floor);
        Solid(g, "GroundEast", new Vector3(PitX1, y0, Z0), new Vector3(X1, y1, Z1), floor);
        Solid(g, "GroundPitSouth", new Vector3(PitX0, y0, Z0), new Vector3(PitX1, y1, PitZ0), floor);
        Solid(g, "GroundPitNorth", new Vector3(PitX0, y0, PitZ1), new Vector3(PitX1, y1, Z1), floor);
        // The perimeter and the backstop behind the 200 m targets.
        var wall = SceneLook.Wall;
        Solid(g, "WallSouth", new Vector3(X0, 0f, Z0 - 1f), new Vector3(X1, 6f, Z0), wall, 3f);
        Solid(g, "WallWest", new Vector3(X0 - 1f, 0f, Z0), new Vector3(X0, 6f, Z1 + 1f), wall, 3f);
        Solid(g, "WallEast", new Vector3(X1, 0f, Z0), new Vector3(X1 + 1f, 6f, Z1 + 1f), wall, 3f);
        Solid(g, "Backstop", new Vector3(X0, 0f, Z1), new Vector3(X1, 14f, Z1 + 2f), wall, 3f);
    }

    static void Lane()
    {
        var g = Group("Lane");
        var paint = Flat("LanePaint", new Color(0.85f, 0.85f, 0.8f));
        FlatBox(g, "FiringLine", new Vector3(0f, 0.011f, 2f), new Vector3(14f, 0.02f, 0.2f), paint, false);
        var orange = Flat("TargetOrange", new Color(0.92f, 0.45f, 0.12f));
        var steel = Flat("TargetSteel", new Color(0.55f, 0.6f, 0.65f));
        var flag = Flat("TargetFlag", new Color(0.95f, 0.8f, 0.15f));
        var pole = Flat("Pole", new Color(0.2f, 0.2f, 0.22f));
        foreach (float m in LaneMeters)
        {
            FlatBox(g, "Line" + m, new Vector3(0f, 0.011f, m), new Vector3(14f, 0.02f, 0.15f), paint, false);
            // Mannequin: torso, head, legs.
            var dummy = new GameObject("Dummy" + m);
            dummy.transform.SetParent(g, false);
            dummy.transform.position = new Vector3(0f, 0f, m);
            dummy.AddComponent<TrainingTarget>().Meters = (int)m;
            FlatBox(dummy.transform, "Torso", new Vector3(0f, 1.15f, m), new Vector3(0.5f, 0.6f, 0.25f), orange);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(dummy.transform, false);
            head.transform.position = new Vector3(0f, 1.62f, m);
            head.transform.localScale = Vector3.one * 0.24f;
            head.GetComponent<Renderer>().sharedMaterial = orange;
            FlatBox(dummy.transform, "LegL", new Vector3(-0.12f, 0.42f, m), new Vector3(0.18f, 0.84f, 0.2f), orange);
            FlatBox(dummy.transform, "LegR", new Vector3(0.12f, 0.42f, m), new Vector3(0.18f, 0.84f, 0.2f), orange);
            // Steel plate on a post to the right.
            var plate = new GameObject("Plate" + m);
            plate.transform.SetParent(g, false);
            plate.transform.position = new Vector3(3f, 0f, m);
            plate.AddComponent<TrainingTarget>().Meters = (int)m;
            FlatBox(plate.transform, "Post", new Vector3(3f, 0.6f, m), new Vector3(0.08f, 1.2f, 0.08f), pole);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            disc.transform.SetParent(plate.transform, false);
            disc.transform.position = new Vector3(3f, 1.35f, m);
            disc.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(0.5f, 0.03f, 0.5f);
            disc.GetComponent<Renderer>().sharedMaterial = steel;
            // A tall flag and the distance on a board to the left, so far targets can be found.
            FlatBox(g, "FlagPole" + m, new Vector3(-3f, 1.75f, m), new Vector3(0.08f, 3.5f, 0.08f), pole, false);
            FlatBox(g, "Flag" + m, new Vector3(-3f, 3.0f, m), new Vector3(1.2f, 1.0f, 0.04f), flag, false);
            Sign($"{(int)m} m", new Vector3(0f, 2.5f, m), 1f + m / 60f, g);
        }
    }

    // The movement course along x at z = CourseZ, between two low walls.
    static void Course()
    {
        var g = Group("Course");
        var plates = SceneLook.Plates;
        var panel = SceneLook.Panel;
        var wall = SceneLook.Wall;
        var accent = SceneLook.Accent;
        const float zLo = -21.5f, zHi = -6.5f, x0 = 14f, x1 = PitX1 + 1f;
        Solid(g, "SideSouth", new Vector3(x0, 0f, zLo - 0.5f), new Vector3(x1, 3f, zLo), wall, 3f);
        Solid(g, "SideNorth", new Vector3(x0, 0f, zHi), new Vector3(x1, 3f, zHi + 0.5f), wall, 3f);
        Solid(g, "EndWall", new Vector3(x1, 0f, zLo - 0.5f), new Vector3(x1 + 0.5f, 3f, zHi + 0.5f), wall, 3f);
        Sign("움직임 코스", new Vector3(x0 + 1.5f, 3.2f, CourseZ), 1f, g);

        // 1. Low box to vault: 0.9 m high, 0.5 m deep, the whole width of the lane.
        Solid(g, "VaultBox", new Vector3(23.75f, 0f, zLo), new Vector3(24.25f, 0.9f, zHi), accent, 1.2f);
        Sign("넘기", new Vector3(24f, 1.9f, CourseZ), 0.8f, g);
        // 2. A wall with a 1.2 m high crawl gap: walk through it crouched.
        const float wallX0 = 33.8f, wallX1 = 34.2f;
        Solid(g, "GapSouth", new Vector3(wallX0, 0f, zLo), new Vector3(wallX1, 2.6f, CourseZ - 1.1f), panel);
        Solid(g, "GapNorth", new Vector3(wallX0, 0f, CourseZ + 1.1f), new Vector3(wallX1, 2.6f, zHi), panel);
        Solid(g, "GapLintel", new Vector3(wallX0, 1.2f, CourseZ - 1.1f), new Vector3(wallX1, 2.6f, CourseZ + 1.1f), panel);
        Sign("앉아서 통과", new Vector3(34f, 3.2f, CourseZ), 0.8f, g);
        // 3. A 2.3 m wall to jump at and climb over the edge of.
        Solid(g, "LedgeWall", new Vector3(43.5f, 0f, zLo), new Vector3(45f, 2.3f, zHi), plates, 0.5f);
        Sign("점프해 턱 잡기 (2.3 m)", new Vector3(44f, 3.4f, CourseZ), 0.8f, g);
        // 4. Stairs up to a platform: 8 steps of 0.2 m, 0.35 m deep (the controller steps 0.3 m).
        for (int i = 0; i < 8; i++)
            Solid(g, "Step" + i, new Vector3(52f + i * 0.35f, 0f, CourseZ - 2f), new Vector3(52.35f + i * 0.35f, 0.2f * (i + 1), CourseZ + 2f), plates, 0.5f);
        Solid(g, "StairPlatform", new Vector3(54.8f, 0f, CourseZ - 2f), new Vector3(58.8f, 1.6f, CourseZ + 2f), plates, 0.5f);
        Sign("계단", new Vector3(54f, 3.0f, CourseZ), 0.8f, g);
        // 5. The swimming pit: the ground has a hole 2.5 m deep with water to just under the rim, and a ramp out.
        Solid(g, "PitFloor", new Vector3(PitX0, PitFloor - 1f, PitZ0), new Vector3(PitX1, PitFloor, PitZ1), plates, 0.5f);
        const float rampTopX = PitX1, rampBottomX = PitX1 - 6f;
        float angle = Mathf.Atan2(-PitFloor, rampTopX - rampBottomX) * Mathf.Rad2Deg;
        float length = Mathf.Sqrt(6f * 6f + PitFloor * PitFloor) + 1f;
        var ramp = Solid(g, "PitRamp", Vector3.zero, new Vector3(length, 0.5f, PitZ1 - PitZ0), plates, 0.5f);
        var mid = new Vector3((rampTopX + rampBottomX) / 2f, PitFloor / 2f, (PitZ0 + PitZ1) / 2f);
        var up = new Vector3(-Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad), 0f);
        ramp.transform.SetPositionAndRotation(mid - up * 0.25f, Quaternion.Euler(0f, 0f, angle));
        Sign("수영장", new Vector3((PitX0 + PitX1) / 2f, 2.2f, CourseZ), 1f, g);
    }

    // A table along z at x = -14.5 with three rows of everything the checklist needs.
    static TrainingLoot[] ItemTable()
    {
        var g = Group("ItemTable");
        const float tx = -14.5f, top = 0.9f;
        FlatBox(g, "Table", new Vector3(tx, top / 2f, 0.5f), new Vector3(3.6f, top, 16.4f), Flat("Table", new Color(0.30f, 0.32f, 0.34f)));
        Sign("아이템 테이블", new Vector3(tx, 3.1f, 0.5f), 1.2f, g);
        var loot = new List<TrainingLoot>();
        void Put(float row, int slot, LootEntry e)
        {
            var p = new GameObject("Loot" + loot.Count);
            p.transform.SetParent(g, false);
            p.transform.position = new Vector3(tx + row, top + 0.3f, -7f + slot);
            var l = p.AddComponent<TrainingLoot>();
            l.Entry = e;
            loot.Add(l);
        }
        LootEntry Gun(int id, int mag) => new LootEntry { Kind = LootKind.Weapon, WeaponId = id, Amount = mag };
        LootEntry Ammo(AmmoType t, int n) => new LootEntry { Kind = LootKind.Ammo, Ammo = t, Amount = n };
        LootEntry Of(LootKind k, int n = 1) => new LootEntry { Kind = k, Amount = n };
        LootEntry Part(int id) => new LootEntry { Kind = LootKind.Optic, Optic = id };
        LootEntry Armor(LootKind k, int tier) => new LootEntry { Kind = k, Tier = tier };

        // Front row (x = -13.5): guns with their ammo, armour and the bag.
        const float front = 1f, mid = 0f, back = -1f;
        Put(front, 0, Gun(3, 30)); Put(front, 1, Ammo(AmmoType.Rifle556, 120));
        Put(front, 2, Gun(5, 5)); Put(front, 3, Ammo(AmmoType.Rifle762, 40));
        Put(front, 4, Gun(2, 30)); Put(front, 5, Gun(1, 12)); Put(front, 6, Ammo(AmmoType.Pistol9, 90));
        Put(front, 7, Gun(4, 6)); Put(front, 8, Ammo(AmmoType.Shotgun12, 30));
        Put(front, 10, Armor(LootKind.Helmet, 2)); Put(front, 11, Armor(LootKind.Vest, 2)); Put(front, 12, Armor(LootKind.Backpack, 2));
        // Middle row: scopes (2x, 4x, 8x, 15x) and the muzzle, grip, stock and magazine parts.
        Put(mid, 0, Part(3)); Put(mid, 1, Part(5)); Put(mid, 2, Part(7)); Put(mid, 3, Part(8));
        Put(mid, 5, Part(9)); Put(mid, 6, Part(11)); Put(mid, 7, Part(12)); Put(mid, 8, Part(15)); Put(mid, 9, Part(16));
        // Back row: healing, boosts, throwables.
        Put(back, 0, Of(LootKind.Bandage, 5)); Put(back, 1, Of(LootKind.FirstAidKit)); Put(back, 2, Of(LootKind.Medkit));
        Put(back, 3, Of(LootKind.Drink, 2)); Put(back, 4, Of(LootKind.Pill, 2)); Put(back, 5, Of(LootKind.Syringe));
        Put(back, 7, Of(LootKind.Grenade, 3)); Put(back, 8, Of(LootKind.Smoke, 2)); Put(back, 9, Of(LootKind.Flash, 2)); Put(back, 10, Of(LootKind.Molotov, 2));
        return loot.ToArray();
    }

    // A 8 x 8 m house south-west with a door in the north wall and one in the south wall. Returns the door spots.
    static Transform[] House()
    {
        var g = Group("DoorHouse");
        var wall = SceneLook.Wall;
        const float cx = -36f, cz = -36f, half = 4f, t = 0.3f, h = 2.8f;
        float lo = cz - half, hi = cz + half;
        // Walls east and west whole; north and south with the door opening in the middle.
        Solid(g, "WallW", new Vector3(cx - half - t, 0f, lo - t), new Vector3(cx - half, h, hi + t), wall, 3f);
        Solid(g, "WallE", new Vector3(cx + half, 0f, lo - t), new Vector3(cx + half + t, h, hi + t), wall, 3f);
        foreach (var (name, z0, z1) in new[] { ("N", hi, hi + t), ("S", lo - t, lo) })
        {
            Solid(g, "Wall" + name + "L", new Vector3(cx - half, 0f, z0), new Vector3(cx - DoorW / 2f, h, z1), wall, 3f);
            Solid(g, "Wall" + name + "R", new Vector3(cx + DoorW / 2f, 0f, z0), new Vector3(cx + half, h, z1), wall, 3f);
            Solid(g, "Wall" + name + "Top", new Vector3(cx - DoorW / 2f, DoorH, z0), new Vector3(cx + DoorW / 2f, h, z1), wall, 3f);
        }
        Solid(g, "Roof", new Vector3(cx - half - t, h, lo - t), new Vector3(cx + half + t, h + 0.3f, hi + t), SceneLook.Panel, 3f);
        Sign("문 집", new Vector3(cx, 4.4f, hi + 1f), 1f, g);
        var spots = new List<Transform>();
        foreach (var (name, z) in new[] { ("DoorNorth", hi + t / 2f), ("DoorSouth", lo - t / 2f) })
        {
            var s = new GameObject(name).transform;
            s.SetParent(g, false);
            s.SetPositionAndRotation(new Vector3(cx, 0f, z), Quaternion.identity);
            spots.Add(s);
        }
        return spots.ToArray();
    }
}

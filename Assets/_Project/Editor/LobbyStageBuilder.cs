using System.IO;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The lobby's 3D stage inside the MainMenu scene (ADR-247): an elevator car built with the same recipe and the same
// WorldMaterials as Editor/WorldBuilder.Elevators.cs (shell, floor, rusty trim, ceiling lamp, steel door frame), made wide
// enough for a four-person party, in front of a concrete station wall with the car's door opening; the party's roster
// characters are spawned into it at run time by LobbyStage.
public static class LobbyStageBuilder
{
    const string MeshDir = "Assets/_Project/Scenes/Lobby";
    // The game's car is 2.80 wide x 2.62 deep x 2.6 high with a 2.24 door. Here: wider so four people stand across it.
    const float HalfWidth = 2.2f, Back = -1.6f, Front = 1.2f, Skin = 0.05f, Height = 2.8f, FloorThick = 0.15f, CeilThick = 0.1f, DoorHalf = 1.95f, DoorTop = 2.45f;

    public static LobbyStage Build(Camera camera)
    {
        Directory.CreateDirectory(MeshDir);
        WorldMaterials.Prepare();
        var root = new GameObject("LobbyStage");
        var stage = root.AddComponent<LobbyStage>();

        var steel = WorldMaterials.Door();
        var metal = WorldMaterials.PaintedMetal;
        var rusty = WorldMaterials.RustyMetal;
        var concrete = WorldMaterials.ConcreteDark;
        var shell = new WorldMeshes.BoxBatch(Vector2.one);
        var floor = new WorldMeshes.BoxBatch(metal.Tile);
        var trim = new WorldMeshes.BoxBatch(rusty.Tile);
        var lamp = new WorldMeshes.BoxBatch(Vector2.one);
        var station = new WorldMeshes.BoxBatch(concrete.Tile);
        var hall = new WorldMeshes.BoxBatch(WorldMaterials.Concrete.Tile);
        var stripe = new WorldMeshes.BoxBatch(Vector2.one);

        float x0 = -HalfWidth, x1 = HalfWidth, z0 = Back, z1 = Front, s = Skin, h = Height;
        void Box(WorldMeshes.BoxBatch b, float a0, float a1, float b0, float b1, float c0, float c1) =>
            b.Add(new Vector3((a0 + a1) / 2f, (b0 + b1) / 2f, (c0 + c1) / 2f), new Vector3(a1 - a0, b1 - b0, c1 - c0));

        // The car (same parts as AddElevatorCar).
        Box(floor, x0, x1, -FloorThick, 0f, z0, z1);
        Box(shell, x0, x0 + s, 0f, h, z0, z1);
        Box(shell, x1 - s, x1, 0f, h, z0, z1);
        Box(shell, x0, x1, 0f, h, z0, z0 + s);
        Box(shell, x0, x1, h, h + CeilThick, z0, z1);
        Box(shell, x0, -DoorHalf, 0f, h, z1 - s, z1);
        Box(shell, DoorHalf, x1, 0f, h, z1 - s, z1);
        Box(shell, x0, x1, DoorTop, h, z1 - s, z1);
        Box(trim, x0 + s, x0 + s + 0.05f, 0.92f, 0.97f, -1.2f, 0.6f);
        Box(trim, x1 - s - 0.05f, x1 - s, 0.92f, 0.97f, -1.2f, 0.6f);
        Box(trim, -1.6f, 1.6f, h + CeilThick, h + CeilThick + 0.15f, -0.1f, 0.1f);
        Box(lamp, -0.9f, 0.9f, h - 0.03f, h, -0.9f, 0.4f);
        // Floor indicator lamps over the opening and a thin strip of light in the door gap on the floor.
        for (int i = -2; i <= 2; i++) Box(lamp, i * 0.22f - 0.06f, i * 0.22f + 0.06f, DoorTop + 0.12f, DoorTop + 0.2f, z1 - s - 0.02f, z1 - s);
        Box(lamp, -DoorHalf, DoorHalf, 0f, 0.012f, z1 - s - 0.16f, z1 - s - 0.1f);
        // The door frame's amber stripe along the opening's top and sides (the game's DoorStripe material).
        Box(stripe, -DoorHalf, DoorHalf, DoorTop - 0.05f, DoorTop, z1 - s - 0.006f, z1 - s);
        Box(stripe, -DoorHalf, -DoorHalf + 0.05f, 0f, DoorTop, z1 - s - 0.006f, z1 - s);
        Box(stripe, DoorHalf - 0.05f, DoorHalf, 0f, DoorTop, z1 - s - 0.006f, z1 - s);

        // The station wall round the car's opening and the hall floor in front of it.
        float wallZ0 = z1, wallZ1 = z1 + 0.4f, wx = 14f, wy = 14f;
        Box(station, -wx, x0 + 0.01f, 0f, wy, wallZ0, wallZ1);
        Box(station, x1 - 0.01f, wx, 0f, wy, wallZ0, wallZ1);
        Box(station, x0, x1, h + CeilThick, wy, wallZ0, wallZ1);
        Box(hall, -wx, wx, -FloorThick, 0f, z1, 16f);

        Mesh Save(WorldMeshes.BoxBatch b, string name)
        {
            var mesh = b.ToMesh(name);
            string path = $"{MeshDir}/Lobby_{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        void Part(string name, WorldMeshes.BoxBatch b, Material m, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Save(b, name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows;
            go.isStatic = true;
        }
        Part("Shell", shell, steel, ShadowCastingMode.On);
        Part("Floor", floor, metal.Material, ShadowCastingMode.Off);
        Part("Trim", trim, rusty.Material, ShadowCastingMode.Off);
        Part("Lamp", lamp, WorldMaterials.ShaftLamp(), ShadowCastingMode.Off);
        Part("Stripe", stripe, WorldMaterials.DoorStripe(), ShadowCastingMode.Off);
        Part("StationWall", station, concrete.Material, ShadowCastingMode.On);
        Part("HallFloor", hall, WorldMaterials.Concrete.Material, ShadowCastingMode.Off);

        // Light: the car's own ceiling lamp (as in the game) plus soft fills from the front and a rim from behind so the
        // models read; no shadows except the key.
        AddLamp(root.transform, "CarLight", new Vector3(0f, h - 0.25f, -0.2f), 8f, 2.2f);
        AddLamp(root.transform, "FillLeft", new Vector3(-2.6f, 2.2f, 3.4f), 9f, 1.6f);
        AddLamp(root.transform, "FillRight", new Vector3(2.6f, 2.2f, 3.4f), 9f, 1.6f);
        AddLamp(root.transform, "Rim", new Vector3(0f, 2.4f, -1.2f), 5f, 1.4f);
        // A soft key from the hall (the only shadow caster), a wash down onto the floor and warm rims from behind each side.
        AddSpot(root.transform, "Key", new Vector3(3.4f, 3.6f, 7.5f), new Vector3(0f, 1.0f, 0.2f), 38f, 16f, 7f, true);
        AddSpot(root.transform, "FloorWash", new Vector3(0f, h - 0.1f, 0.4f), new Vector3(0f, 0f, 0.4f), 120f, 4f, 3.2f, false);
        AddSpot(root.transform, "RimLeft", new Vector3(2.0f, 2.5f, -1.3f), new Vector3(0.6f, 1.4f, 0.8f), 60f, 6f, 5f, false);
        AddSpot(root.transform, "RimRight", new Vector3(-2.0f, 2.5f, -1.3f), new Vector3(-0.6f, 1.4f, 0.8f), 60f, 6f, 5f, false);

        // Roster prefabs in PlayerAppearance.ModelIds order and the rifle template.
        stage.RosterPrefabs = new GameObject[PlayerAppearance.ModelIds.Length];
        for (int i = 0; i < stage.RosterPrefabs.Length; i++)
            stage.RosterPrefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Roster/Roster_{PlayerAppearance.ModelIds[i]}.prefab");
        var rifle = CharacterKit.InstantiateWeapon(CharacterKit.Weapons[2]);
        rifle.name = "RifleTemplate";
        rifle.transform.SetParent(root.transform, false);
        rifle.SetActive(false);
        stage.RifleTemplate = rifle;
        stage.IdleController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Prefabs/M1_5/Character.controller");
        AddPost(root.transform);
        AddDust(root.transform);

        // Cameras: the party seen from the front (in the hall, looking into the car), and a closeup for the 외형 screen.
        var lobby = new GameObject("CameraLobby").transform;
        lobby.SetParent(root.transform, false);
        lobby.position = new Vector3(0f, 1.9f, 14.2f);
        lobby.rotation = Quaternion.LookRotation(new Vector3(0f, 0.38f, 0f) - lobby.position);
        var close = new GameObject("CameraCloseup").transform;
        close.SetParent(root.transform, false);
        close.position = new Vector3(0f, 1.2f, 5.2f);
        close.rotation = Quaternion.LookRotation(new Vector3(0f, 1.0f, 0.35f) - close.position);
        stage.CameraLobby = lobby; stage.CameraCloseup = close;
        stage.Camera = camera;
        camera.transform.SetPositionAndRotation(lobby.position, lobby.rotation);
        camera.fieldOfView = 28f;
        camera.cullingMask = -1;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f);
        // A dark flat ambient: the lobby is lit by the car's lamp and the fills below, not by a sky.
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.13f);
        RenderSettings.fog = false;
        return stage;
    }

    static void AddSpot(Transform parent, string name, Vector3 pos, Vector3 target, float angle, float range, float intensity, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.rotation = Quaternion.LookRotation(target - pos);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * 0.5f; l.range = range * 4f; l.intensity = intensity;
        l.color = new Color(1f, 0.93f, 0.82f);
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }

    // The lobby's own post: bloom on the lamps, a gentle vignette, a touch more contrast, ACES.
    static void AddPost(Transform parent)
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(0.85f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.6f);
        var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.32f); vig.smoothness.Override(0.5f);
        var ca = profile.Add<ColorAdjustments>(true); ca.contrast.Override(14f); ca.saturation.Override(-6f); ca.postExposure.Override(0.1f);
        var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
        string path = MeshDir + "/LobbyPost.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(profile, path);
        var go = new GameObject("LobbyPost");
        go.transform.SetParent(parent, false);
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 10f; volume.weight = 1f;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
    }

    // A few slow motes of dust in the hall light.
    static void AddDust(Transform parent)
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 2.5f);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true; main.startLifetime = 9f; main.startSpeed = 0.03f; main.startSize = 0.018f; main.maxParticles = 140;
        main.startColor = new Color(1f, 0.93f, 0.8f, 0.4f); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 14f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(6f, 3f, 5f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f); vel.y = new ParticleSystem.MinMaxCurve(0.0f, 0.04f); vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var r = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f); mat.SetFloat("_SrcBlend", 5f); mat.SetFloat("_DstBlend", 10f); mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3000;
        string path = MeshDir + "/LobbyDust.mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mat, path);
        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    static void AddLamp(Transform parent, string name, Vector3 pos, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.range = range; l.intensity = intensity;
        l.color = new Color(1f, 0.90f, 0.76f); l.shadows = LightShadows.None;
    }
}

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
        var shell = new WorldMeshes.BoxBatch(new Vector2(2.5f, 2.5f));
        var floor = new WorldMeshes.BoxBatch(metal.Tile);
        var trim = new WorldMeshes.BoxBatch(rusty.Tile);
        var lamp = new WorldMeshes.BoxBatch(Vector2.one);
        var station = new WorldMeshes.BoxBatch(new Vector2(2.5f, 2.5f));
        var hall = new WorldMeshes.BoxBatch(WorldMaterials.Concrete.Tile);
        var stripe = new WorldMeshes.BoxBatch(Vector2.one);
        var seams = new WorldMeshes.BoxBatch(Vector2.one);
        var rivets = new WorldMeshes.BoxBatch(rusty.Tile);
        var ribs = new WorldMeshes.BoxBatch(metal.Tile);

        float x0 = -HalfWidth, x1 = HalfWidth, z0 = Back, z1 = Front, s = Skin, h = Height;
        // The pipeline lights additional lights per vertex, so a big flat quad shows a diagonal seam between its triangles:
        // big surfaces are cut into 0.5 m boxes so the light varies smoothly across them.
        void Split(WorldMeshes.BoxBatch b, float a0, float a1, float b0, float b1, float c0, float c1, float step = 0.5f)
        {
            int na = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / step)), nb = Mathf.Max(1, Mathf.CeilToInt((b1 - b0) / step)), nc = Mathf.Max(1, Mathf.CeilToInt((c1 - c0) / step));
            for (int i = 0; i < na; i++)
                for (int j = 0; j < nb; j++)
                    for (int k = 0; k < nc; k++)
                        b.Add(new Vector3(a0 + (i + 0.5f) * (a1 - a0) / na, b0 + (j + 0.5f) * (b1 - b0) / nb, c0 + (k + 0.5f) * (c1 - c0) / nc),
                            new Vector3((a1 - a0) / na, (b1 - b0) / nb, (c1 - c0) / nc));
        }
        void Box(WorldMeshes.BoxBatch b, float a0, float a1, float b0, float b1, float c0, float c1) =>
            b.Add(new Vector3((a0 + a1) / 2f, (b0 + b1) / 2f, (c0 + c1) / 2f), new Vector3(a1 - a0, b1 - b0, c1 - c0));

        // The car (same parts as AddElevatorCar).
        Split(floor, x0, x1, -FloorThick, 0f, z0, z1);
        Box(shell, x0, x0 + s, 0f, h, z0, z1);
        Box(shell, x1 - s, x1, 0f, h, z0, z1);
        Box(shell, x0, x1, 0f, h, z0, z0 + s);
        Box(shell, x0, x1, h, h + CeilThick, z0, z1);
        Box(shell, x0, -DoorHalf, 0f, h, z1 - s, z1);
        Box(shell, DoorHalf, x1, 0f, h, z1 - s, z1);
        Box(shell, x0, x1, DoorTop, h, z1 - s, z1);
        Box(trim, -1.6f, 1.6f, h + CeilThick, h + CeilThick + 0.15f, -0.1f, 0.1f);
        Box(lamp, -0.9f, 0.9f, h - 0.03f, h, -0.9f, 0.4f);
        // Floor indicator lamps over the opening and a thin strip of light in the door gap on the floor.
        for (int i = -2; i <= 2; i++) Box(lamp, i * 0.22f - 0.06f, i * 0.22f + 0.06f, DoorTop + 0.12f, DoorTop + 0.2f, z1 - s - 0.02f, z1 - s);
        Box(lamp, -DoorHalf, DoorHalf, 0f, 0.012f, z1 - s - 0.16f, z1 - s - 0.1f);
        // Wall detail for depth: pilasters (they catch light and cast shadow), dark panel seams, rows of rivets.
        // The door frame's amber stripe along the opening's top and sides (the game's DoorStripe material).
        Box(stripe, -DoorHalf, DoorHalf, DoorTop - 0.05f, DoorTop, z1 - s - 0.006f, z1 - s);

        // The station wall round the car's opening and the hall floor in front of it.
        float wallZ0 = z1, wallZ1 = z1 + 0.4f, wx = 14f, wy = 14f;
        Box(station, -wx, x0 + 0.01f, 0f, wy, wallZ0, wallZ1);
        Box(station, x1 - 0.01f, wx, 0f, wy, wallZ0, wallZ1);
        Box(station, x0, x1, h + CeilThick, wy, wallZ0, wallZ1);
        Split(hall, -wx, wx, -FloorThick, 0f, z1, 16f, 1.5f);

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
            go.isStatic = false; // static batching / baked-GI paths add nothing here and only player-build risk
            r.receiveGI = ReceiveGI.LightProbes; // ambient from the flat ambient, never from lightmaps that were not baked
        }
        // Wall: a plain URP Lit with a hand-made tileable noise (value noise, our own) so the steel is not one flat colour.
        // (The game's concrete on tiled boxes shows facets with the pipeline's per-vertex lights, so it is not used here.)
        var noise = new Texture2D(128, 128, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Repeat };
        var rnd = new System.Random(7);
        var grid = new float[16 * 16];
        for (int gi = 0; gi < grid.Length; gi++) grid[gi] = (float)rnd.NextDouble();
        float V(int gx, int gy) => grid[((gy % 16) + 16) % 16 * 16 + ((gx % 16) + 16) % 16];
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float n = 0f, amp = 0.5f; int freq = 4;
                for (int o = 0; o < 3; o++)
                {
                    float fx = x / 128f * freq, fy = y / 128f * freq; int ix = (int)fx, iy = (int)fy; float tx = fx - ix, ty = fy - iy;
                    float a0 = Mathf.Lerp(V(ix, iy), V(ix + 1, iy), tx), a1 = Mathf.Lerp(V(ix, iy + 1), V(ix + 1, iy + 1), tx);
                    n += Mathf.Lerp(a0, a1, ty) * amp; amp *= 0.5f; freq *= 2;
                }
                float v = 0.74f + (n - 0.5f) * 0.4f;
                noise.SetPixel(x, y, new Color(v, v, v * 1.03f));
            }
        noise.Apply();
        File.WriteAllBytes(MeshDir + "/LobbyWallNoise.png", noise.EncodeToPNG());
        Object.DestroyImmediate(noise);
        AssetDatabase.ImportAsset(MeshDir + "/LobbyWallNoise.png");
        var wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wallMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MeshDir + "/LobbyWallNoise.png"));
        wallMat.SetColor("_BaseColor", new Color(0.50f, 0.54f, 0.60f)); wallMat.SetFloat("_Smoothness", 0.2f); wallMat.SetFloat("_Metallic", 0.1f);
        AssetDatabase.DeleteAsset(MeshDir + "/LobbyWall.mat");
        AssetDatabase.CreateAsset(wallMat, MeshDir + "/LobbyWall.mat");
        wallMat = AssetDatabase.LoadAssetAtPath<Material>(MeshDir + "/LobbyWall.mat");
        Part("Shell", shell, wallMat, ShadowCastingMode.On);
        var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        floorMat.SetColor("_BaseColor", new Color32(11, 15, 20, 255)); floorMat.SetFloat("_Smoothness", 0f); floorMat.SetFloat("_Metallic", 0f); floorMat.SetFloat("_SpecularHighlights", 0f); floorMat.SetFloat("_EnvironmentReflections", 0f); floorMat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); floorMat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        AssetDatabase.DeleteAsset(MeshDir + "/LobbyFloor.mat");
        AssetDatabase.CreateAsset(floorMat, MeshDir + "/LobbyFloor.mat");
        floorMat = AssetDatabase.LoadAssetAtPath<Material>(MeshDir + "/LobbyFloor.mat");
        Part("Floor", floor, floorMat, ShadowCastingMode.Off);
        Part("Trim", trim, rusty.Material, ShadowCastingMode.Off);
        Part("Lamp", lamp, WorldMaterials.ShaftLamp(), ShadowCastingMode.Off);
        Part("Stripe", stripe, WorldMaterials.DoorStripe(), ShadowCastingMode.Off);
        Part("StationWall", station, wallMat, ShadowCastingMode.On);
        Part("HallFloor", hall, floorMat, ShadowCastingMode.Off);

        // Light: the car's own ceiling lamp (as in the game) plus soft fills from the front and a rim from behind so the
        // models read; no shadows except the key.
        AddLamp(root.transform, "CarLight", new Vector3(0f, h - 0.25f, -0.2f), 8f, 1.6f);
        // Art direction: a warm key (4500 K) from the camera's left at 45 degrees, cool #5FA8FF rims from behind and above
        // on both sides at about 30 degrees, a dark flat ambient. No other light.
        AddPoint(root.transform, "Key", new Vector3(-1.9f, 2.4f, 1.3f), 7f, 2.6f, new Color(1f, 0.82f, 0.62f)); // warm key from the camera's left, soft falloff (a spot leaves a hard cone edge on the wall)
        // A soft wash on the back and side walls so the car reads as a room, not a void.
        AddPoint(root.transform, "WallWash", new Vector3(0.6f, 2.5f, 0.2f), 7f, 1.8f, new Color(0.78f, 0.84f, 0.95f));
        AddSpot(root.transform, "RimLeft", new Vector3(1.9f, 2.7f, -1.3f), new Vector3(0.7f, 0.3f, 0.5f), 20f, 4.2f, 2.4f, false, new Color(0.37f, 0.66f, 1f));
        AddSpot(root.transform, "RimRight", new Vector3(-1.9f, 2.7f, -1.3f), new Vector3(-0.7f, 0.3f, 0.5f), 20f, 4.2f, 2.4f, false, new Color(0.37f, 0.66f, 1f));

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

        // Cameras: the party seen from the front (in the hall, looking into the car), and a closeup for the 외형 screen.
        var lobby = new GameObject("CameraLobby").transform;
        lobby.SetParent(root.transform, false);
        lobby.position = new Vector3(0f, 1.35f, 6.2f);
        lobby.rotation = Quaternion.LookRotation(new Vector3(0f, 0.95f, 0f) - lobby.position);
        var close = new GameObject("CameraCloseup").transform;
        close.SetParent(root.transform, false);
        close.position = new Vector3(0f, 1.0f, 5.6f);
        close.rotation = Quaternion.LookRotation(new Vector3(0f, 0.95f, 0.35f) - close.position);
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
        RenderSettings.ambientLight = new Color(0.15f, 0.15f, 0.16f);
        RenderSettings.fog = false;
        RenderRosterThumbs(stage, camera);
        return stage;
    }

    // 96 x 128 (rendered at 2x) head-and-shoulders pictures of the 15 roster models under the lobby's own lights, saved for the
    // 외형 screen: Resources/Ui/RosterThumbs/<ModelId>.png.
    static void RenderRosterThumbs(LobbyStage stage, Camera lobbyCamera)
    {
        const string dir = "Assets/_Project/Resources/Ui/RosterThumbs";
        Directory.CreateDirectory(dir);
        var camGo = new GameObject("ThumbCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(16, 21, 28, 255);
        cam.fieldOfView = 20f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f; cam.enabled = false;
        camGo.transform.position = stage.transform.TransformPoint(new Vector3(0f, 1.6f, 2.0f));
        camGo.transform.rotation = Quaternion.LookRotation(stage.transform.TransformPoint(new Vector3(0f, 1.55f, 0f)) - camGo.transform.position);
        var rt = new RenderTexture(192, 256, 24) { antiAliasing = 4 };
        for (int i = 0; i < PlayerAppearance.ModelIds.Length; i++)
        {
            var prefab = stage.RosterPrefabs[i];
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stage.transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 8f, 0f);
            var def = go.GetComponent<CharacterDef>();
            if (def != null) { def.ApplyVariant(0); if (def.Animator != null) def.Animator.enabled = false; LobbyStage.PoseCrossed(def); }
            cam.targetTexture = rt;
            cam.Render(); cam.Render(); // the first render after a change can come out unlit
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(192, 256, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 192, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes($"{dir}/{PlayerAppearance.ModelIds[i]}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        AssetDatabase.Refresh();
    }

    static void AddPoint(Transform parent, string name, Vector3 pos, float range, float intensity, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.range = range; l.intensity = intensity; l.color = color; l.shadows = LightShadows.None;
    }

    static void AddSpot(Transform parent, string name, Vector3 pos, Vector3 target, float angle, float range, float intensity, bool shadows, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.rotation = Quaternion.LookRotation(target - pos);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * 0.5f; l.range = range * 4f; l.intensity = intensity;
        l.color = color;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }

    // The lobby's own post: bloom on the lamps, a gentle vignette, a touch more contrast, ACES.
    static void AddPost(Transform parent)
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.4f); bloom.scatter.Override(0.6f);
        var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.3f); vig.smoothness.Override(0.5f);
        var ca = profile.Add<ColorAdjustments>(true); ca.contrast.Override(15f); ca.saturation.Override(-10f); ca.postExposure.Override(0.1f);
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
        main.loop = true; main.startLifetime = 9f; main.startSpeed = 0.03f; main.startSize = 0.006f; main.maxParticles = 90;
        main.startColor = new Color(1f, 0.93f, 0.8f, 0.4f); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 8f;
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

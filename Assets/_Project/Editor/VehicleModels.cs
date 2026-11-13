using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// The CC-BY vehicle models (#89) made into the parts the Vehicle framework drives. Each model is instantiated,
// turned to face +Z with +Y up, and taken apart by node name (or, for the road motorcycle whose parts are unnamed,
// by bounds): the static parts are merged into one body mesh (a submesh per material), each wheel into its own mesh
// centred on its axle, the steering wheel / handlebar into one mesh about its steering axis. Badge meshes (parody
// maker logos) and the shipped US plates are left out; plain quads with the code-drawn Korean plate take the plates'
// place. The merged meshes are saved under Prefabs/M1/Vehicles (generated, derived from the models: ASSET_LEDGER).
//
// Everything that ties the model to the framework is measured from the model: wheel centres and radius (resting on
// y = 0, the axles' midpoint at the origin), the steering wheel's centre and tilt (the driver's eye and the grips
// follow from it), the body and glass bounds (the colliders: the lower body up to the belt line and the roof).
// Per-model numbers are only what cannot be measured: mass, seat layout, small nudges found from the review shots.
public static class VehicleModels
{
    public const string ArtDir = "Assets/_Project/Art/Vehicles";
    public static string OutDir => TestBoxBuilder.PrefabDir + "/Vehicles";
    static string MaterialDir => OutDir + "/Materials";

    public enum Role { Drop, Paint, Trim, Lights, Glass, Interior, Tyre, Plate, Body, Chrome, Dark, Seat }

    // What the builder hands to VehicleBuilder: the parts in the vehicle's own space.
    public sealed class Parts
    {
        public string Name;
        public VehicleKind Kind;
        public Mesh Body;
        public Material[] BodyMaterials;
        public Vector3[] WheelCentres;         // FL, FR, RL, RR / front, rear
        public Mesh[] Wheels;                  // spinning part of each wheel, about its centre
        public Material[][] WheelMaterials;
        public Mesh[] WheelStatic;             // steered but not spinning (brake callipers), about the wheel centre
        public Material[][] WheelStaticMaterials;
        public float WheelRadius;
        public Vector3 SteeringPivot;          // the steering part's pivot
        public Quaternion SteeringRotation;    // its local Z = the turning axis
        public Mesh Steering;                  // null: no separate steering mesh (grips still placed)
        public Material[] SteeringMaterials;
        public float SteeringRatio;            // turn of the steering part per degree of front wheel angle
        public Vector3[] Grips;                // left, right, in the steering pivot's space
        public Vector3 SwingPivot;             // motorcycle swingarm pivot (rear axle end follows the wheel)
        public Mesh Swingarm;
        public Material[] SwingarmMaterials;
        public Bounds BodyBounds, GlassBounds;
        public bool HasGlass;
        public Vector3[] SeatEyes;
        public float DoorOut;
        public float MassKg, CentreOfMassHeight, CentreOfMassZ;
    }

    // ---- Materials ----

    static Texture2D Tex(string folder, string file)
    {
        var path = $"{ArtDir}/{folder}/{file}";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) throw new FileNotFoundException("vehicle texture missing", path);
        return tex;
    }

    public static Material Lit(string name, Color color, float metallic, float smoothness, Texture2D baseMap = null, Texture2D normal = null, float normalScale = 1f)
    {
        Directory.CreateDirectory(MaterialDir);
        var path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetTexture("_BaseMap", baseMap);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", normalScale);
        if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Alpha clipped by the base map's alpha (grilles, mesh).
    static Material Cutout(Material mat, float cutoff)
    {
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", cutoff);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Tinted, see-through, glossy: the cabin shows through. Set up by hand (normally the material inspector does it).
    public static Material Glass(string name, Color tint, Texture2D baseMap = null)
    {
        var mat = Lit(name, tint, 0f, 0.95f, baseMap);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material PlateMaterial() => Lit("Vehicle_Plate_KR", Color.white, 0f, 0.45f, VehicleArt.Plate());

    // ---- Model specs ----

    sealed class Spec
    {
        public string Name, Folder, File;
        public VehicleKind Kind = VehicleKind.Car;
        public Vector3 Euler;
        public float Scale = 1f;
        public Regex Drop, Wheel, WheelStatic, Steering, Plate, Glass;
        public Func<string, Role> RoleOf;
        public Dictionary<Role, Material> Materials;
        public Func<Material, Material> Remap;  // instead of RoleOf / Materials: the model's material in, ours out (null drops it)
        public float MassKg, CentreOfMassHeight, CentreOfMassZ;
        public int Seats = 4;
        public float EyeUp = 0.36f, EyeBack = 0.56f, RearBack = 0.85f, RearUp = 0.03f;
        public Vector3 FrontNudge, RearNudge;
        public Vector3 SteeringGuess;          // no steering mesh: where its centre is
        public float SteeringGuessRadius = 0.18f;
        public float SteeringTiltDeg = -1f;    // < 0: from the steering mesh's bounds
    }

    static readonly Regex Badges = new Regex("Badges|Numberplate|clearcoat|car_shadow|^Object0", RegexOptions.IgnoreCase);

    // Daniel Zhabotinsky's four share one material scheme.
    static Role DanielRole(string material)
    {
        if (material.IndexOf("Transperent", StringComparison.OrdinalIgnoreCase) >= 0) return Role.Glass;
        if (material.IndexOf("Lights_and_Glass", StringComparison.OrdinalIgnoreCase) >= 0) return Role.Lights;
        if (material.IndexOf("bodymat", StringComparison.OrdinalIgnoreCase) >= 0) return Role.Paint;
        if (material.IndexOf("UCB_BOTTOM", StringComparison.OrdinalIgnoreCase) >= 0) return Role.Trim;
        if (material.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0) return Role.Interior;
        if (material.IndexOf("Tire", StringComparison.OrdinalIgnoreCase) >= 0) return Role.Tyre;
        return Role.Drop; // the badge and plate sheets
    }

    static Dictionary<Role, Material> DanielMaterials(string name, string folder, Color paint, string paintTex, string paintNormal, string trimTex, string trimNormal,
        string lightsTex, string interiorTex, string tyreTex, string tyreNormal)
    {
        var lights = Tex(folder, lightsTex);
        return new Dictionary<Role, Material>
        {
            [Role.Paint] = Lit(name + "_Paint", paint, 0.35f, 0.62f, Tex(folder, paintTex), paintNormal != null ? Tex(folder, paintNormal) : null, 0.6f),
            [Role.Trim] = Lit(name + "_Trim", Color.white, 0.25f, 0.4f, Tex(folder, trimTex), trimNormal != null ? Tex(folder, trimNormal) : null),
            [Role.Lights] = Lit(name + "_Lights", Color.white, 0.1f, 0.8f, lights),
            [Role.Glass] = Glass(name + "_Glass", new Color(1f, 1f, 1f, 0.42f), lights),
            [Role.Interior] = interiorTex != null ? Lit(name + "_Interior", Color.white, 0f, 0.2f, Tex(folder, interiorTex)) : Lit(name + "_Interior", new Color(0.13f, 0.13f, 0.14f), 0f, 0.2f),
            [Role.Tyre] = Lit(name + "_Tyre", Color.white, 0.1f, 0.3f, Tex(folder, tyreTex), tyreNormal != null ? Tex(folder, tyreNormal) : null),
            [Role.Plate] = PlateMaterial(),
        };
    }

    static Spec Daniel(string name, string file, float massKg, Dictionary<Role, Material> materials) => new Spec
    {
        Name = name, Folder = name, File = file, Euler = new Vector3(0f, 90f, 0f), MassKg = massKg,
        Drop = Badges, Wheel = new Regex("Wheel(Stock|Hubcaps)"), Steering = new Regex("Steering_Wheel"), Plate = new Regex("Numberplate"),
        RoleOf = DanielRole, Materials = materials,
    };

    static IEnumerable<Spec> CarSpecs()
    {
        var lt80 = Daniel("FairheavenLT80", "FairheavenLT80.fbx", 1250f, DanielMaterials("FairheavenLT80", "FairheavenLT80", new Color(0.46f, 0.1f, 0.08f),
            "Generic_bodymat_dif.jpg", null, "UCB_BOTTOM_GT_Dif.png", "UCB_BOTTOM_GT_NRM.png", "UCB_Lights_and_Glass_Dif.jpg", "UCB_BOTTOM_GT_Dif.png", "RB1c_Tire_1k_Dif.jpg", null));
        yield return lt80;
        var sw84 = Daniel("FairheavenSW84", "FairheavenSW84.fbx", 1350f, DanielMaterials("FairheavenSW84", "FairheavenSW84", new Color(0.66f, 0.6f, 0.47f),
            "Generic_bodymat_dif.png", "Generic_bodymat_NRM.png", "UCB_BOTTOM_GT_Dif.png", "UCB_BOTTOM_GT_NRM.png", "UCB_Lights_and_Glass_Dif.jpg", "UCB_BOTTOM_GT_Dif.png", "RB1c_Tire_1k_Dif.jpg", null));
        yield return sw84;
        var lifted = Daniel("LightbodyLifted85", "LightbodyLifted85.fbx", 1900f, DanielMaterials("LightbodyLifted85", "LightbodyLifted85", new Color(0.2f, 0.28f, 0.19f),
            "Generic_bodymat_dif.jpg", null, "UCB_BOTTOM_DIF.jpg", null, "UCB_Lights_and_Glass_Dif.jpg", "UCB_Interiors_1_Dif.jpg", "RO1a_Tire_dif.jpg", "RO1a_Tire_nrm.jpg"));
        lifted.CentreOfMassHeight = 0.62f;
        lifted.RearBack = 1.65f; // the two rear places are in the bed
        lifted.RearUp = 0.05f;
        yield return lifted;
        var shvan = Daniel("Shvan92", "Shvan92.fbx", 1750f, DanielMaterials("Shvan92", "Shvan92", new Color(0.78f, 0.78f, 0.75f),
            "Generic_bodymat_dif.jpg", null, "UCB_BOTTOM_DIF.jpg", null, "UCB_Lights_and_Glass_Dif.jpg", "UCB_Interiors_1_Dif.jpg", "RB1c_Tire_1k_Dif.jpg", null));
        shvan.CentreOfMassHeight = 0.6f;
        shvan.RearBack = 1.05f;
        yield return shvan;

        var goblinFolder = "GoblinV12";
        var albedo = VehicleArt.GoblinAlbedo();
        yield return new Spec
        {
            Name = "GoblinV12", Folder = goblinFolder, File = "GoblinV12.fbx", Euler = new Vector3(0f, 0f, 180f), MassKg = 1450f, Seats = 2,
            CentreOfMassHeight = 0.32f,
            Drop = Badges, Wheel = new Regex("car_wheel"), WheelStatic = new Regex("car_brake"), Plate = null,
            RoleOf = m => m == "car_body" ? Role.Body : m == "car_glass" ? Role.Glass : m == "car_tire" ? Role.Tyre : Role.Drop,
            Materials = new Dictionary<Role, Material>
            {
                [Role.Body] = Cutout(Lit("GoblinV12_Body", Color.white, 0.45f, 0.7f, albedo), 0.4f),
                [Role.Glass] = Glass("GoblinV12_Glass", new Color(0.05f, 0.06f, 0.07f, 0.5f)),
                [Role.Tyre] = Lit("GoblinV12_Tyre", new Color(0.09f, 0.09f, 0.09f), 0f, 0.3f, null, Tex(goblinFolder, "tire_normal.png")),
            },
            SteeringGuess = new Vector3(-0.38f, 0.72f, 0.18f), SteeringTiltDeg = 22f,
        };
    }

    // ---- Building ----

    public static List<Parts> BuildCars()
    {
        var list = new List<Parts>();
        foreach (var spec in CarSpecs()) list.Add(BuildCar(spec));
        return list;
    }

    sealed class Piece
    {
        public Renderer Renderer;
        public Mesh Mesh;
        public Bounds Bounds;
        public string Name;
    }

    static List<Piece> Instantiate(Spec spec, out GameObject root, string keepUnder = null)
    {
        var path = $"{ArtDir}/{spec.Folder}/{spec.File}";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) throw new FileNotFoundException("vehicle model missing", path);
        root = new GameObject("src_" + spec.Name);
        var model = (GameObject)Object.Instantiate(asset, root.transform);
        model.transform.localRotation = Quaternion.Euler(spec.Euler);
        model.transform.localScale = Vector3.one * spec.Scale;
        var pieces = new List<Piece>();
        foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (keepUnder != null && !UnderNamed(r.transform, keepUnder)) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            pieces.Add(new Piece { Renderer = r, Mesh = mf.sharedMesh, Bounds = r.bounds, Name = r.name });
        }
        return pieces;
    }

    static bool UnderNamed(Transform t, string name)
    {
        for (; t != null; t = t.parent) if (t.name == name) return true;
        return false;
    }

    static Bounds Union(IEnumerable<Piece> pieces)
    {
        bool first = true;
        var b = new Bounds();
        foreach (var p in pieces)
        {
            if (first) { b = p.Bounds; first = false; }
            else b.Encapsulate(p.Bounds);
        }
        return b;
    }

    static Parts BuildCar(Spec spec)
    {
        var pieces = Instantiate(spec, out var root);
        try
        {
            var wheels = new List<Piece>();
            var wheelStatic = new List<Piece>();
            var steering = new List<Piece>();
            var plates = new List<Piece>();
            var body = new List<Piece>();
            foreach (var p in pieces)
            {
                if (spec.Plate != null && spec.Plate.IsMatch(p.Name)) { plates.Add(p); continue; }
                if (spec.Drop != null && spec.Drop.IsMatch(p.Name)) continue;
                if (spec.Wheel != null && spec.Wheel.IsMatch(p.Name)) wheels.Add(p);
                else if (spec.WheelStatic != null && spec.WheelStatic.IsMatch(p.Name)) wheelStatic.Add(p);
                else if (spec.Steering != null && spec.Steering.IsMatch(p.Name)) steering.Add(p);
                else body.Add(p);
            }
            if (wheels.Count < 4) throw new Exception($"{spec.Name}: found {wheels.Count} wheel parts");

            // Wheels by quadrant around their middle: FL, FR, RL, RR.
            var mid = Union(wheels).center;
            int Quadrant(Vector3 c) => (c.z >= mid.z ? 0 : 2) + (c.x < mid.x ? 0 : 1);
            var groups = new List<Piece>[4];
            for (int i = 0; i < 4; i++) groups[i] = new List<Piece>();
            foreach (var w in wheels) groups[Quadrant(w.Bounds.center)].Add(w);
            var staticGroups = new List<Piece>[4];
            for (int i = 0; i < 4; i++) staticGroups[i] = new List<Piece>();
            foreach (var w in wheelStatic) staticGroups[Quadrant(w.Bounds.center)].Add(w);
            var centres = new Vector3[4];
            float radius = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (groups[i].Count == 0) throw new Exception($"{spec.Name}: wheel {i} missing");
                var b = Union(groups[i]);
                centres[i] = b.center;
                radius += Mathf.Max(b.size.y, b.size.z) * 0.5f / 4f;
            }
            float groundY = centres.Average(c => c.y) - radius;
            var shift = new Vector3(-(centres.Average(c => c.x)), -groundY, -(centres.Average(c => c.z)));
            for (int i = 0; i < 4; i++)
            {
                centres[i] += shift;
                centres[i].y = radius; // all four resting on y = 0
            }
            var toVehicle = Matrix4x4.Translate(shift);

            var parts = new Parts { Name = spec.Name, Kind = spec.Kind, WheelRadius = radius, WheelCentres = centres, MassKg = spec.MassKg,
                CentreOfMassHeight = spec.CentreOfMassHeight, CentreOfMassZ = spec.CentreOfMassZ };

            // Body (with the plates as quads).
            var combiner = new Combiner();
            var glass = new Bounds();
            bool hasGlass = false;
            foreach (var p in body)
            {
                var m = toVehicle * p.Renderer.localToWorldMatrix;
                var mats = p.Renderer.sharedMaterials;
                for (int s = 0; s < p.Mesh.subMeshCount && s < mats.Length; s++)
                {
                    var mat = Pick(spec, mats[s], out var role);
                    if (mat == null) continue;
                    combiner.Add(p.Mesh, s, mat, m);
                    if (role == Role.Glass && !(spec.Plate != null && spec.Plate.IsMatch(p.Name)))
                    {
                        var gb = p.Bounds;
                        gb.center += shift;
                        if (!hasGlass) { glass = gb; hasGlass = true; } else glass.Encapsulate(gb);
                    }
                }
            }
            var plateMat = PlateMaterial();
            foreach (var p in plates)
            {
                var b = p.Bounds;
                b.center += shift;
                var normal = b.center.z > 0f ? Vector3.forward : Vector3.back;
                combiner.AddQuad(b.center + normal * (b.extents.z + 0.004f), Vector3.right * b.size.x, Vector3.up * Mathf.Min(b.size.y, b.size.x * 0.5f), normal, plateMat);
            }
            parts.Body = combiner.ToMesh(spec.Name + "_Body", out parts.BodyMaterials);
            parts.BodyBounds = parts.Body.bounds;
            parts.GlassBounds = glass;
            parts.HasGlass = hasGlass;

            // Wheels, each about its own centre.
            parts.Wheels = new Mesh[4];
            parts.WheelMaterials = new Material[4][];
            parts.WheelStatic = new Mesh[4];
            parts.WheelStaticMaterials = new Material[4][];
            for (int i = 0; i < 4; i++)
            {
                var about = Matrix4x4.Translate(-centres[i]) * toVehicle;
                parts.Wheels[i] = CombineGroup(groups[i], about, spec, $"{spec.Name}_Wheel{i}", out parts.WheelMaterials[i]);
                if (staticGroups[i].Count > 0)
                    parts.WheelStatic[i] = CombineGroup(staticGroups[i], about, spec, $"{spec.Name}_Brake{i}", out parts.WheelStaticMaterials[i]);
            }

            // Steering wheel: its centre, its column tilted back from upright by `tilt`; the column (the pivot's Z) runs
            // from the driver forward and down to the dash.
            Vector3 centre;
            float wheelR, tilt;
            if (steering.Count > 0)
            {
                var sb = Union(steering);
                sb.center += shift;
                centre = sb.center;
                wheelR = sb.size.x * 0.5f;
                tilt = spec.SteeringTiltDeg >= 0f ? spec.SteeringTiltDeg
                    : Mathf.Asin(Mathf.Clamp((sb.size.z - 0.04f) / Mathf.Max(0.05f, sb.size.x), 0f, 0.8f)) * Mathf.Rad2Deg;
            }
            else
            {
                centre = spec.SteeringGuess;
                wheelR = spec.SteeringGuessRadius;
                tilt = spec.SteeringTiltDeg;
            }
            float t = tilt * Mathf.Deg2Rad;
            var column = new Vector3(0f, -Mathf.Sin(t), Mathf.Cos(t));
            var faceUp = new Vector3(0f, Mathf.Cos(t), Mathf.Sin(t));
            parts.SteeringPivot = centre;
            parts.SteeringRotation = Quaternion.LookRotation(column, faceUp);
            parts.SteeringRatio = -3f; // a right turn turns the wheel clockwise as the driver sees it
            if (steering.Count > 0)
            {
                var about = Matrix4x4.TRS(centre, parts.SteeringRotation, Vector3.one).inverse * toVehicle;
                parts.Steering = CombineGroup(steering, about, spec, spec.Name + "_Steering", out parts.SteeringMaterials);
            }
            // Hands at a quarter to three, on the rim's near face.
            parts.Grips = new[] { new Vector3(-wheelR * 0.93f, wheelR * 0.3f, -0.015f), new Vector3(wheelR * 0.93f, wheelR * 0.3f, -0.015f) };

            // Seats: the driver's eyes above and behind the wheel, the passenger beside, the rear row behind.
            var eye = new Vector3(centre.x, centre.y + spec.EyeUp, centre.z - spec.EyeBack) + spec.FrontNudge;
            float roof = parts.BodyBounds.max.y;
            eye.y = Mathf.Min(eye.y, roof - 0.12f);
            var eyes = new List<Vector3> { eye, new Vector3(-eye.x, eye.y, eye.z) };
            if (spec.Seats > 2)
            {
                var rear = new Vector3(eye.x, Mathf.Min(eye.y + spec.RearUp, roof - 0.1f), eye.z - spec.RearBack) + spec.RearNudge;
                eyes.Add(rear);
                eyes.Add(new Vector3(-rear.x, rear.y, rear.z));
            }
            parts.SeatEyes = eyes.ToArray();
            parts.DoorOut = parts.BodyBounds.extents.x + 0.65f;
            return parts;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Material Pick(Spec spec, Material source, out Role role)
    {
        role = Role.Drop;
        if (source == null) return null;
        if (spec.Remap != null)
        {
            var mat = spec.Remap(source);
            if (mat != null) role = Role.Body;
            return mat;
        }
        role = spec.RoleOf(source.name);
        return role != Role.Drop && spec.Materials.TryGetValue(role, out var picked) ? picked : null;
    }

    static Mesh CombineGroup(IEnumerable<Piece> pieces, Matrix4x4 about, Spec spec, string name, out Material[] materials)
    {
        var c = new Combiner();
        foreach (var p in pieces)
        {
            var m = about * p.Renderer.localToWorldMatrix;
            var mats = p.Renderer.sharedMaterials;
            for (int s = 0; s < p.Mesh.subMeshCount && s < mats.Length; s++)
            {
                var mat = Pick(spec, mats[s], out _);
                if (mat == null) continue;
                c.Add(p.Mesh, s, mat, m);
            }
        }
        return c.ToMesh(name, out materials);
    }

    // ---- Motorcycles ----

    public static List<Parts> BuildBikes() => new List<Parts> { BuildDirtBike(), BuildRoadBike() };

    // Danillo Ribeiro's dirt bike: the red one of the seven colour sets. Named parts: dirt_bike (frame, bodywork),
    // motor, swingarm > rear_wheel, throttle (handlebar, number plate) > fork > front_wheel.
    static Parts BuildDirtBike()
    {
        const string folder = "DirtBike";
        var body = Lit("DirtBike_Body", Color.white, 0.2f, 0.5f, Tex(folder, "03Red_basecolor.png"));
        var spec = new Spec { Name = "DirtBike", Folder = folder, File = "DirtBike.fbx", Kind = VehicleKind.Motorcycle, Euler = new Vector3(0f, 180f, 0f),
            RoleOf = _ => Role.Body, Materials = new Dictionary<Role, Material> { [Role.Body] = body }, MassKg = 190f, CentreOfMassHeight = 0.55f };
        var pieces = Instantiate(spec, out var root, "dirt_bike red");
        try
        {
            Piece Named(string n) => pieces.First(p => p.Name == n || p.Name.StartsWith(n + ".", StringComparison.Ordinal));
            var front = Named("front_wheel");
            var rear = Named("rear_wheel");
            var fork = Named("fork");
            var bar = Named("throttle");
            var swing = Named("swingarm");
            var bodyParts = new[] { Named("dirt_bike red"), Named("motor") };
            return BuildBike(spec, root, bodyParts, new[] { front }, new[] { rear }, new[] { bar, fork }, swing, 27f,
                grips: null, saddle: bodyParts[0].Bounds, eyeUp: 0.78f, eyeForward: 0.12f);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Animanyarty's road motorcycle: about 50 unnamed parts (Circle.*, Cube.*, Cylinder.*, Plane.*), so the parts are
    // sorted by bounds: the spinning wheel parts sit on an axle and are round across Y and Z; the steering parts are
    // the handlebar materials and everything ahead of the fork line or up at the bars; the rest is the body.
    static Parts BuildRoadBike()
    {
        const string folder = "RoadMotorcycle";
        var tyre = Lit("RoadMotorcycle_Tyre", Color.white, 0f, 0.3f, Tex(folder, "wheels_difuse.png"), Tex(folder, "wheels_normal.png"));
        var made = new Dictionary<string, Material>();
        // The model's materials are plain colours (no texture is assigned in the file): kept as they are, with
        // metal and gloss by kind. "Mishelin" is the file's tyre material: our tyre sheet (no lettering on it).
        Material Remap(Material source)
        {
            string n = source.name;
            if (n.StartsWith("Mishelin")) return tyre;
            if (made.TryGetValue(n, out var mat)) return mat;
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.gray;
            color.a = 1f;
            bool chrome = n.StartsWith("Material.076") || n.StartsWith("Material.064") || n.StartsWith("Mirorr") || n.StartsWith("Material.061");
            bool paint = n.StartsWith("Moto 2") || n == "Material.006" || n == "Material.002";
            float metallic = chrome ? 0.9f : paint ? 0.35f : 0.1f;
            float smooth = chrome ? 0.8f : paint ? 0.72f : n.StartsWith("Leather") || n.StartsWith("Ruchki") ? 0.25f : 0.45f;
            mat = Lit("RoadMotorcycle_" + Regex.Replace(n, "[^A-Za-z0-9]", "_"), color, metallic, smooth);
            made[n] = mat;
            return mat;
        }
        var spec = new Spec { Name = "RoadMotorcycle", Folder = folder, File = "RoadMotorcycle.fbx", Kind = VehicleKind.Motorcycle, Scale = 12.5f,
            MassKg = 230f, CentreOfMassHeight = 0.55f, Remap = Remap };
        var pieces = Instantiate(spec, out var root);
        try
        {
            // Axles: the two tyres (the biggest round parts with the tyre material).
            var tyres = pieces.Where(p => p.Renderer.sharedMaterials.Any(m => m != null && m.name.StartsWith("Mishelin")) && p.Bounds.size.y > 0.4f)
                .OrderByDescending(p => p.Bounds.center.z).ToList();
            if (tyres.Count < 2) throw new Exception("RoadMotorcycle: tyres not found");
            var frontAxle = tyres[0].Bounds.center;
            var rearAxle = tyres[tyres.Count - 1].Bounds.center;
            float r = tyres[0].Bounds.size.y * 0.5f;
            bool OnAxle(Piece p, Vector3 axle)
            {
                var b = p.Bounds;
                float off = new Vector2(b.center.y - axle.y, b.center.z - axle.z).magnitude;
                float round = Mathf.Abs(b.size.y - b.size.z) / Mathf.Max(1e-3f, Mathf.Max(b.size.y, b.size.z));
                return off < 0.06f && round < 0.25f && b.size.y < r * 2.3f;
            }
            var frontWheel = pieces.Where(p => OnAxle(p, frontAxle)).ToList();
            var rearWheel = pieces.Where(p => OnAxle(p, rearAxle)).ToList();
            float barY = frontAxle.y + r * 2.0f;
            bool Steers(Piece p)
            {
                if (frontWheel.Contains(p) || rearWheel.Contains(p)) return false;
                var c = p.Bounds.center;
                bool handle = p.Renderer.sharedMaterials.Any(m => m != null && (m.name.StartsWith("Ruchki") || m.name.StartsWith("Mirorr")));
                if (handle) return c.y > barY * 0.75f;
                // Ahead of the fork line (raked back from the front axle), or up at the bars and forward of the tank.
                float lineZ = frontAxle.z - (c.y - frontAxle.y) * Mathf.Tan(25f * Mathf.Deg2Rad);
                return c.z > lineZ - 0.05f || (c.y > barY * 0.95f && c.z > frontAxle.z - 0.75f);
            }
            var steer = pieces.Where(Steers).ToList();
            var body = pieces.Where(p => !frontWheel.Contains(p) && !rearWheel.Contains(p) && !steer.Contains(p)).ToList();
            var saddle = pieces.Where(p => p.Renderer.sharedMaterials.Any(m => m != null && m.name.StartsWith("Leather"))).Select(p => p.Bounds).FirstOrDefault();
            return BuildBike(spec, root, body, frontWheel, rearWheel, steer, null, 25f, grips: null, saddle: saddle, eyeUp: 0.74f, eyeForward: 0.1f);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Shared by both motorcycles: wheels resting on y = 0 with the axles' midpoint at the origin; the steering part
    // turns about the rake axis through the front axle; the swingarm (if any) about its front end.
    static Parts BuildBike(Spec spec, GameObject root, IEnumerable<Piece> body, IList<Piece> frontWheel, IList<Piece> rearWheel, IEnumerable<Piece> steer,
        Piece swing, float rakeDeg, Vector3[] grips, Bounds saddle, float eyeUp, float eyeForward)
    {
        // Axle centres from the tyres (the widest wheel part).
        var fb = Union(frontWheel);
        var rb = Union(rearWheel);
        float radius = (Mathf.Max(fb.size.y, fb.size.z) + Mathf.Max(rb.size.y, rb.size.z)) * 0.25f;
        var shift = new Vector3(-(fb.center.x + rb.center.x) * 0.5f, -((fb.center.y + rb.center.y) * 0.5f - radius), -(fb.center.z + rb.center.z) * 0.5f);
        var toVehicle = Matrix4x4.Translate(shift);
        var front = fb.center + shift;
        var rear = rb.center + shift;
        front.y = rear.y = radius;
        var parts = new Parts { Name = spec.Name, Kind = VehicleKind.Motorcycle, WheelRadius = radius, WheelCentres = new[] { front, rear },
            MassKg = spec.MassKg, CentreOfMassHeight = spec.CentreOfMassHeight };

        var c = new Combiner();
        foreach (var p in body) AddPiece(c, p, toVehicle, spec);
        parts.Body = c.ToMesh(spec.Name + "_Body", out parts.BodyMaterials);
        parts.BodyBounds = parts.Body.bounds;

        parts.Wheels = new[]
        {
            CombineGroup(frontWheel, Matrix4x4.Translate(-front) * toVehicle, spec, spec.Name + "_Wheel0", out var fm),
            CombineGroup(rearWheel, Matrix4x4.Translate(-rear) * toVehicle, spec, spec.Name + "_Wheel1", out var rm),
        };
        parts.WheelMaterials = new[] { fm, rm };
        parts.WheelStatic = new Mesh[2];
        parts.WheelStaticMaterials = new Material[2][];

        // Steering about the rake axis: the pivot's Z points up and back along the fork.
        float rake = rakeDeg * Mathf.Deg2Rad;
        var axis = new Vector3(0f, Mathf.Cos(rake), -Mathf.Sin(rake));
        parts.SteeringPivot = front;
        parts.SteeringRotation = Quaternion.LookRotation(axis, Vector3.forward);
        parts.SteeringRatio = 1f;
        var pivotInverse = Matrix4x4.TRS(front, parts.SteeringRotation, Vector3.one).inverse;
        var steerList = steer.ToList();
        parts.Steering = CombineGroup(steerList, pivotInverse * toVehicle, spec, spec.Name + "_Steering", out parts.SteeringMaterials);

        // Grips: the outer ends of the bar, at the top of the steering parts, a hand's width in from the ends.
        var sb = Union(steerList);
        sb.center += shift;
        float half = Mathf.Min(0.36f, sb.extents.x - 0.05f);
        var barTop = new Vector3(0f, sb.max.y - 0.07f, 0f);
        // Of the steering parts near the top, the rearmost: the bar sits behind the head.
        var nearTop = steerList.Select(p => { var b = p.Bounds; b.center += shift; return b; }).Where(b => b.max.y > sb.max.y - 0.2f).ToList();
        float barZ = nearTop.Count > 0 ? nearTop.Min(b => b.center.z) : sb.center.z;
        var gripLeft = new Vector3(-half, barTop.y, barZ);
        var gripRight = new Vector3(half, barTop.y, barZ);
        // The road bike's "Ruchki" (grip) material sits on the whole handlebar piece and a small clamp: the mirrors stand
        // higher than the bar and pulled the hands up to them, so the hands go at the clamp's height instead.
        var gripPieces = steerList.Where(p => p.Renderer.sharedMaterials.Any(m => m != null && m.name.StartsWith("Ruchki"))).Select(p => { var b = p.Bounds; b.center += shift; return b; }).ToList();
        if (grips == null && gripPieces.Count > 0 && spec.Name == "RoadMotorcycle")
        {
            float gy = gripPieces.Min(b => b.center.y) + 0.03f, gz = gripPieces.Min(b => b.center.z);
            gripLeft = new Vector3(-half, gy, gz);
            gripRight = new Vector3(half, gy, gz);
        }
        parts.Grips = grips ?? new[] { pivotInverse.MultiplyPoint3x4(gripLeft), pivotInverse.MultiplyPoint3x4(gripRight) };

        if (swing != null)
        {
            var sw = swing.Bounds;
            sw.center += shift;
            parts.SwingPivot = new Vector3(0f, sw.center.y + sw.extents.y * 0.35f, sw.max.z - 0.04f);
            parts.Swingarm = CombineGroup(new[] { swing }, Matrix4x4.Translate(-parts.SwingPivot) * toVehicle, spec, spec.Name + "_Swingarm", out parts.SwingarmMaterials);
        }

        // Rider's eyes above the saddle's front half; the pillion behind and a little higher.
        var seat = saddle;
        seat.center += shift;
        float seatTop = seat.size.sqrMagnitude > 0f ? seat.max.y : 0.9f;
        float seatZ = seat.size.sqrMagnitude > 0f ? seat.center.z : -0.2f;
        parts.SeatEyes = new[]
        {
            new Vector3(0f, seatTop + eyeUp, seatZ + eyeForward),
            new Vector3(0f, seatTop + eyeUp + 0.08f, seatZ + eyeForward - 0.42f),
        };
        parts.DoorOut = 0.9f;
        return parts;
    }

    static void AddPiece(Combiner c, Piece p, Matrix4x4 toVehicle, Spec spec)
    {
        var m = toVehicle * p.Renderer.localToWorldMatrix;
        var mats = p.Renderer.sharedMaterials;
        for (int s = 0; s < p.Mesh.subMeshCount && s < mats.Length; s++)
        {
            var mat = Pick(spec, mats[s], out _);
            if (mat == null) continue;
            c.Add(p.Mesh, s, mat, m);
        }
    }

    // ---- Parked and wrecked cars (comrade1280's generic pack: bodies only, static props) ----

    public sealed class Prop
    {
        public string Name;
        public Mesh Mesh;
        public Material[] Materials;
        public Bounds Bounds;
        public bool Wreck;
    }

    // Each body of the pack with its four wheels, turned to face +Z and resting on y = 0; also a wrecked copy of each
    // (no wheels, sunk on the ground, rusty dark paint).
    public static List<Prop> BuildProps()
    {
        const string folder = "PassengerCarPack";
        var colours = new Dictionary<string, string>
        {
            ["Compact"] = "CompactBlue.png", ["Coupe"] = "CoupeBlue.png", ["Hatchback"] = "HatchbackYellow.png", ["minivan"] = "MinivanRed.png",
            ["Offroad"] = "OffroadGreen.png", ["Pickup"] = "PickupGreen.png", ["Sedan"] = "SedanYellow.png", ["Sport"] = "SportRed.png",
            ["SUV"] = "SUVBlack.png", ["Wagon"] = "WagonBlue.png",
        };
        var lights = Lit("CarPack_Lights", Color.white, 0.1f, 0.8f, Tex(folder, "lights.jpg"));
        var glassMat = Lit("CarPack_Glass", new Color(0.05f, 0.07f, 0.08f), 0.2f, 0.92f);
        var rust = new Color(0.36f, 0.27f, 0.22f);
        var spec = new Spec { Name = "CarPack", Folder = folder, File = "PassengerCarPack.fbx", RoleOf = _ => Role.Drop, Materials = new Dictionary<Role, Material>() };
        var pieces = Instantiate(spec, out var root);
        var props = new List<Prop>();
        try
        {
            var bodies = pieces.Where(p => p.Name.EndsWith("Body", StringComparison.OrdinalIgnoreCase) || p.Name.EndsWith("body", StringComparison.Ordinal)).ToList();
            var wheels = pieces.Where(p => p.Name.StartsWith("Wheel", StringComparison.Ordinal)).ToList();
            foreach (var b in bodies)
            {
                string key = colours.Keys.FirstOrDefault(k => b.Name.StartsWith(k, StringComparison.OrdinalIgnoreCase));
                if (key == null) continue;
                var paintTex = Tex(folder, colours[key]);
                var paint = Lit("CarPack_" + key, Color.white, 0.3f, 0.6f, paintTex);
                var paintWreck = Lit("CarPack_" + key + "_Wreck", rust, 0.15f, 0.15f, paintTex);
                // Its wheels: the four nearest wheel parts within a car's length.
                var mine = wheels.Where(w => Vector3.Distance(Flat(w.Bounds.center), Flat(b.Bounds.center)) < 3.2f)
                    .OrderBy(w => Vector3.Distance(Flat(w.Bounds.center), Flat(b.Bounds.center))).Take(4).ToList();
                // The body's own frame without its yaw-turned placement: the pack lays the cars out in a ring.
                var yaw = Quaternion.Euler(0f, b.Renderer.transform.eulerAngles.y, 0f);
                var place = Matrix4x4.TRS(new Vector3(b.Bounds.center.x, 0f, b.Bounds.center.z), yaw, Vector3.one).inverse;
                foreach (bool wreck in new[] { false, true })
                {
                    var c = new Combiner();
                    var mats = b.Renderer.sharedMaterials;
                    var m = place * b.Renderer.localToWorldMatrix;
                    if (wreck) m = Matrix4x4.TRS(new Vector3(0f, -0.22f, 0f), Quaternion.Euler(1.5f, 0f, -2.5f), Vector3.one) * m;
                    for (int s = 0; s < b.Mesh.subMeshCount && s < mats.Length; s++)
                    {
                        string n = mats[s] != null ? mats[s].name : "";
                        var mat = n.StartsWith("Glass") ? glassMat : n.StartsWith("Optics") ? lights : wreck ? paintWreck : paint;
                        c.Add(b.Mesh, s, mat, m);
                    }
                    if (!wreck)
                        foreach (var w in mine)
                        {
                            var wm = WheelMaterial(folder, w.Name);
                            var wmats = w.Renderer.sharedMaterials;
                            for (int s = 0; s < w.Mesh.subMeshCount; s++)
                            {
                                string n = s < wmats.Length && wmats[s] != null ? wmats[s].name : "";
                                c.Add(w.Mesh, s, n.StartsWith("Body") ? paint : wm, place * w.Renderer.localToWorldMatrix);
                            }
                        }
                    var mesh = c.ToMesh($"Prop_{key}{(wreck ? "_Wreck" : "")}", out var propMats);
                    // Facing: the pack's cars run along their local length; make it +Z.
                    props.Add(new Prop { Name = key + (wreck ? "_Wreck" : ""), Mesh = mesh, Materials = propMats, Bounds = mesh.bounds, Wreck = wreck });
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
        return props;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static Material WheelMaterial(string folder, string wheelName)
    {
        // Wheel_A .. Wheel_H; the pack has no F sheet (wheel_1 is it).
        char letter = wheelName.Length > 6 ? wheelName[6] : 'A';
        string file = letter == 'A' ? "Wheel_A_Diffuse.png" : letter == 'F' ? "wheel_1_Diffuse.png" : $"wheel_{letter}_Diffuse.png";
        return Lit("CarPack_Wheel_" + letter, Color.white, 0.3f, 0.4f, Tex(folder, file));
    }

    // ---- Mesh combining ----

    // Merges mesh parts into one mesh, a submesh per material. Positions and normals are moved into the target space
    // (a mirrored part has its triangles turned round); tangents are recomputed at the end.
    public sealed class Combiner
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Material> _mats = new List<Material>();
        readonly List<List<int>> _tris = new List<List<int>>();
        readonly Dictionary<(Mesh, Matrix4x4), int> _added = new Dictionary<(Mesh, Matrix4x4), int>();

        List<int> Sub(Material mat)
        {
            int i = _mats.IndexOf(mat);
            if (i < 0)
            {
                _mats.Add(mat);
                _tris.Add(new List<int>());
                i = _mats.Count - 1;
            }
            return _tris[i];
        }

        public void Add(Mesh mesh, int sub, Material mat, Matrix4x4 m)
        {
            if (mesh == null || mat == null) return;
            if (!_added.TryGetValue((mesh, m), out int start))
            {
                start = _v.Count;
                _added[(mesh, m)] = start;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var uvs = mesh.uv;
                var nm = m.inverse.transpose;
                for (int i = 0; i < verts.Length; i++)
                {
                    _v.Add(m.MultiplyPoint3x4(verts[i]));
                    _n.Add(i < normals.Length ? nm.MultiplyVector(normals[i]).normalized : Vector3.up);
                    _uv.Add(i < uvs.Length ? uvs[i] : Vector2.zero);
                }
            }
            bool flip = m.determinant < 0f;
            var tris = mesh.GetTriangles(sub);
            var list = Sub(mat);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                list.Add(start + tris[i]);
                list.Add(start + (flip ? tris[i + 2] : tris[i + 1]));
                list.Add(start + (flip ? tris[i + 1] : tris[i + 2]));
            }
        }

        // A quad of the given full extents facing `normal`, the texture's top along `up`.
        public void AddQuad(Vector3 centre, Vector3 right, Vector3 up, Vector3 normal, Material mat)
        {
            // Seen from the front (against the normal) the texture reads left to right.
            var side = Vector3.Cross(up.normalized, normal).normalized * right.magnitude;
            int start = _v.Count;
            _v.Add(centre - side * 0.5f - up * 0.5f); _uv.Add(new Vector2(0f, 0f));
            _v.Add(centre + side * 0.5f - up * 0.5f); _uv.Add(new Vector2(1f, 0f));
            _v.Add(centre + side * 0.5f + up * 0.5f); _uv.Add(new Vector2(1f, 1f));
            _v.Add(centre - side * 0.5f + up * 0.5f); _uv.Add(new Vector2(0f, 1f));
            for (int i = 0; i < 4; i++) _n.Add(normal);
            var list = Sub(mat);
            var face = Vector3.Cross(_v[start + 1] - _v[start], _v[start + 2] - _v[start]);
            bool ccw = Vector3.Dot(face, normal) < 0f; // Unity's front faces wind clockwise as seen
            if (ccw) list.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            else list.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        public Mesh ToMesh(string name, out Material[] materials)
        {
            var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = _tris.Count;
            for (int i = 0; i < _tris.Count; i++) mesh.SetTriangles(_tris[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            materials = _mats.ToArray();
            return mesh;
        }
    }
}

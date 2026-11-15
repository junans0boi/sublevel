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
        public Mesh[] Detachable;              // per VehiclePart (null = the model has no such separate part), vehicle space
        public Material[][] DetachableMaterials;
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
        public Vector3[] SeatCushions;         // per seat: where the hips rest (top of the cushion)
        public Vector3[] Pegs, Knees;          // motorcycle: left, right foot peg / knee point on the tank
        public Bounds Cabin;                   // car: the inside of the cabin (occupants' arms stay in it)
        public float[] WheelRadii;             // each wheel's own radius (WheelRadius is their mean, for the physics)
        public float RearTop;                  // car: top of the solid body behind the glass (a van's cargo box), 0 = none
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

    // The Goblin's single glass shell is seen through twice (both sides drawn): darker and less mirror-like, or the
    // sky's reflection on both layers washed it milky white.
    static Material GoblinGlass()
    {
        var mat = Glass("GoblinV12_Glass", new Color(0.03f, 0.035f, 0.04f, 0.62f));
        mat.SetFloat("_Smoothness", 0.7f);
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
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
        // Detachable parts by node name, indexed by VehiclePart (null entry = none); their inner faces go with them.
        public Regex[] Detachable;
        public Func<string, Role> RoleOf;
        public Dictionary<Role, Material> Materials;
        public Func<Material, Material> Remap;  // instead of RoleOf / Materials: the model's material in, ours out (null drops it)
        public float MassKg, CentreOfMassHeight, CentreOfMassZ;
        public int Seats = 4;
        public Vector3 SteeringGuess;          // no steering mesh: where its centre is
        public float SteeringGuessRadius = 0.18f;
        public float SteeringTiltDeg = -1f;    // < 0: from the steering mesh's bounds
        // Seats, measured from the interior mesh in the model's turned frame (VehicleBuilder.Measure prints the
        // profiles): z of the front face of the seat back at head height, y of the cushion top. Rear < -50 = no row.
        public float FrontBackZ, FrontCushionY, RearBackZ = -99f, RearCushionY;
    }

    // The character (Player.prefab, measured with VehicleBuilder.Measure): shoulder joint below and beside the eye,
    // the arm's reach (upper arm 0.165 + forearm 0.225 + to the palm), and the seated hip below the eye.
    public const float ShoulderDown = 0.24f, ShoulderSide = 0.15f, ArmReach = 0.42f, GripReach = 0.40f;
    public const float HeadAboveCushion = 1.03f; // measured: VehiclePosePlayTests.EveryCar_DriversHeadStaysUnderTheRoof
    public const float SeatedEyeUp = 0.76f;   // eye above the cushion top, sitting upright
    const float HipUp = 0.1f, HipToEye = 0.73f, HipToShoulder = 0.50f;

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
        Detachable = new[] { Ci("_Hood"), Ci("trunk_?door"), Ci("Bumper_front"), Ci("Bumper_rear") },
    };

    static Regex Ci(string pattern) => new Regex(pattern, RegexOptions.IgnoreCase);

    static IEnumerable<Spec> CarSpecs()
    {
        var lt80 = Daniel("FairheavenLT80", "FairheavenLT80.fbx", 1250f, DanielMaterials("FairheavenLT80", "FairheavenLT80", new Color(0.46f, 0.1f, 0.08f),
            "Generic_bodymat_dif.jpg", null, "UCB_BOTTOM_GT_Dif.png", "UCB_BOTTOM_GT_NRM.png", "UCB_Lights_and_Glass_Dif.jpg", "UCB_BOTTOM_GT_Dif.png", "RB1c_Tire_1k_Dif.jpg", null));
        // Sedan: front buckets (back face -0.25, cushion 0.50), rear bench (back face -1.00, cushion 0.50).
        lt80.FrontBackZ = -0.25f; lt80.FrontCushionY = 0.50f; lt80.RearBackZ = -1.00f; lt80.RearCushionY = 0.50f;
        yield return lt80;
        var sw84 = Daniel("FairheavenSW84", "FairheavenSW84.fbx", 1350f, DanielMaterials("FairheavenSW84", "FairheavenSW84", new Color(0.66f, 0.6f, 0.47f),
            "Generic_bodymat_dif.png", "Generic_bodymat_NRM.png", "UCB_BOTTOM_GT_Dif.png", "UCB_BOTTOM_GT_NRM.png", "UCB_Lights_and_Glass_Dif.jpg", "UCB_BOTTOM_GT_Dif.png", "RB1c_Tire_1k_Dif.jpg", null));
        sw84.Detachable[(int)VehiclePart.Trunk] = null; // the tailgate carries the rear window (kept on the body for the glass)
        sw84.FrontBackZ = -0.25f; sw84.FrontCushionY = 0.50f; sw84.RearBackZ = -1.00f; sw84.RearCushionY = 0.50f; // the sedan's interior
        yield return sw84;
        var lifted = Daniel("LightbodyLifted85", "LightbodyLifted85.fbx", 1900f, DanielMaterials("LightbodyLifted85", "LightbodyLifted85", new Color(0.2f, 0.28f, 0.19f),
            "Generic_bodymat_dif.jpg", null, "UCB_BOTTOM_DIF.jpg", null, "UCB_Lights_and_Glass_Dif.jpg", "UCB_Interiors_1_Dif.jpg", "RO1a_Tire_dif.jpg", "RO1a_Tire_nrm.jpg"));
        lifted.CentreOfMassHeight = 0.62f;
        // Regular cab: the bench back at 0.00, cushion 0.79, and only 0.3 m to the cab's back wall (-0.45): no rear
        // seats, and nobody rides in the bed (ADR-077).
        lifted.Seats = 2; lifted.FrontBackZ = 0.00f; lifted.FrontCushionY = 0.79f;
        yield return lifted;
        var shvan = Daniel("Shvan92", "Shvan92.fbx", 1750f, DanielMaterials("Shvan92", "Shvan92", new Color(0.78f, 0.78f, 0.75f),
            "Generic_bodymat_dif.jpg", null, "UCB_BOTTOM_DIF.jpg", null, "UCB_Lights_and_Glass_Dif.jpg", "UCB_Interiors_1_Dif.jpg", "RB1c_Tire_1k_Dif.jpg", null));
        shvan.CentreOfMassHeight = 0.6f;
        // Panel van: two front seats (back 0.85, cushion 0.84); behind them a bare cargo floor (0.56) to the doors.
        shvan.Seats = 2; shvan.FrontBackZ = 0.85f; shvan.FrontCushionY = 0.84f;
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
                [Role.Glass] = GoblinGlass(),
                [Role.Tyre] = Lit("GoblinV12_Tyre", new Color(0.09f, 0.09f, 0.09f), 0f, 0.3f, null, Tex(goblinFolder, "tire_normal.png")),
            },
            // No steering wheel in the model (the dash is bare, 0.76-0.80 high from z 0.65): one is made here, in front
            // of the driver's seat (back face -0.22, cushion 0.45) and under the dash.
            SteeringGuess = new Vector3(-0.38f, 0.66f, 0.33f), SteeringGuessRadius = 0.16f, SteeringTiltDeg = 24f,
            FrontBackZ = -0.22f, FrontCushionY = 0.45f,
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
            var detachable = new List<Piece>[VehicleDamageMath.PartCount];
            for (int i = 0; i < detachable.Length; i++) detachable[i] = new List<Piece>();
            int PartOf(string name)
            {
                for (int i = 0; spec.Detachable != null && i < spec.Detachable.Length; i++)
                    if (spec.Detachable[i] != null && spec.Detachable[i].IsMatch(name)) return i;
                return -1;
            }
            foreach (var p in pieces)
            {
                if (spec.Plate != null && spec.Plate.IsMatch(p.Name)) { plates.Add(p); continue; }
                if (spec.Drop != null && spec.Drop.IsMatch(p.Name)) continue;
                int part = PartOf(p.Name);
                if (part >= 0) detachable[part].Add(p);
                else if (spec.Wheel != null && spec.Wheel.IsMatch(p.Name)) wheels.Add(p);
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
            var radii = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (groups[i].Count == 0) throw new Exception($"{spec.Name}: wheel {i} missing");
                var b = Union(groups[i]);
                centres[i] = b.center;
                radii[i] = Mathf.Max(b.size.y, b.size.z) * 0.5f;
            }
            float radius = radii.Average();
            // Every tyre on the ground: the ground is under the lowest tyre bottoms (their mean).
            float groundY = Enumerable.Range(0, 4).Average(i => centres[i].y - radii[i]);
            var shift = new Vector3(-(centres.Average(c => c.x)), -groundY, -(centres.Average(c => c.z)));
            for (int i = 0; i < 4; i++)
            {
                centres[i] += shift;
                centres[i].y = radius; // all four resting on y = 0
            }
            var toVehicle = Matrix4x4.Translate(shift);

            var parts = new Parts { Name = spec.Name, Kind = spec.Kind, WheelRadius = radius, WheelRadii = radii, WheelCentres = centres, MassKg = spec.MassKg,
                CentreOfMassHeight = spec.CentreOfMassHeight, CentreOfMassZ = spec.CentreOfMassZ };

            // Body (with the plates as quads), and each detachable part (hood, trunk lid, bumpers) as its own mesh in the
            // same space (#90: they fall off; a plate goes with the part it sits on).
            var combiner = new Combiner();
            var partCombiners = new Combiner[VehicleDamageMath.PartCount];
            var partBounds = new Bounds?[VehicleDamageMath.PartCount];
            var glassVerts = new List<Vector3>();
            var bodyVerts = new List<Vector3>();
            void AddInto(Piece p, Combiner into)
            {
                var m = toVehicle * p.Renderer.localToWorldMatrix;
                var mats = p.Renderer.sharedMaterials;
                for (int s = 0; s < p.Mesh.subMeshCount && s < mats.Length; s++)
                {
                    var mat = Pick(spec, mats[s], out var role);
                    if (mat == null) continue;
                    into.Add(p.Mesh, s, mat, m);
                    var verts = p.Mesh.vertices;
                    var list = role == Role.Glass ? glassVerts : bodyVerts;
                    foreach (int i in p.Mesh.GetTriangles(s)) list.Add(m.MultiplyPoint3x4(verts[i]));
                }
            }
            foreach (var p in body) AddInto(p, combiner);
            for (int i = 0; i < detachable.Length; i++)
            {
                if (detachable[i].Count == 0) continue;
                partCombiners[i] = new Combiner();
                foreach (var p in detachable[i]) AddInto(p, partCombiners[i]);
                var pb = Union(detachable[i]);
                pb.center += shift;
                partBounds[i] = pb;
            }
            var plateMat = PlateMaterial();
            foreach (var p in plates)
            {
                var b = p.Bounds;
                b.center += shift;
                var normal = b.center.z > 0f ? Vector3.forward : Vector3.back;
                var onto = combiner;
                for (int i = 0; i < partBounds.Length; i++)
                {
                    if (partBounds[i] == null) continue;
                    var grown = partBounds[i].Value;
                    grown.Expand(0.1f);
                    if (grown.Contains(b.center)) { onto = partCombiners[i]; break; }
                }
                onto.AddQuad(b.center + normal * (b.extents.z + 0.004f), Vector3.right * b.size.x, Vector3.up * Mathf.Min(b.size.y, b.size.x * 0.5f), normal, plateMat);
            }
            parts.Body = combiner.ToMesh(spec.Name + "_Body", out parts.BodyMaterials);
            // The paint's long edges split so a dent has vertices to bend (ADR-074 (7)).
            spec.Materials.TryGetValue(Role.Paint, out var paint);
            parts.Body = Subdivide(parts.Body, parts.BodyMaterials, paint, DentEdge);
            parts.Detachable = new Mesh[VehicleDamageMath.PartCount];
            parts.DetachableMaterials = new Material[VehicleDamageMath.PartCount][];
            for (int i = 0; i < partCombiners.Length; i++)
                if (partCombiners[i] != null)
                {
                    parts.Detachable[i] = partCombiners[i].ToMesh($"{spec.Name}_{(VehiclePart)i}", out parts.DetachableMaterials[i]);
                    parts.Detachable[i] = Subdivide(parts.Detachable[i], parts.DetachableMaterials[i], paint, DentEdge);
                }
            // The body's bounds take the parts in (bumpers are the ends of the car: colliders, seats, doors).
            var whole = parts.Body.bounds;
            foreach (var d in parts.Detachable) if (d != null) whole.Encapsulate(d.bounds);
            parts.BodyBounds = whole;
            // The windows, not the lamp lenses (the same "glass" material): glass above 55% of the body's height;
            // their length from the upper half of that (the side windows and the screens' tops).
            var bb = parts.BodyBounds;
            float lampTop = bb.min.y + bb.size.y * 0.55f;
            var windows = glassVerts.Where(v => v.y >= lampTop).ToList();
            parts.HasGlass = windows.Count > 0;
            if (parts.HasGlass)
            {
                float top = windows.Max(v => v.y);
                var upper = windows.Where(v => v.y >= (lampTop + top) * 0.5f).ToList();
                var min = new Vector3(windows.Min(v => v.x), windows.Min(v => v.y), upper.Min(v => v.z));
                var max = new Vector3(windows.Max(v => v.x), top, upper.Max(v => v.z));
                parts.GlassBounds = new Bounds((min + max) * 0.5f, max - min);
                // A van's cargo box: behind the windows the body stands well above the belt line all along (the median of
                // the 10 cm slices' tops), not just a roll bar or a spoiler.
                var tops = bodyVerts.Where(v => v.z < min.z - 0.1f && Mathf.Abs(v.x) < bb.extents.x * 0.8f)
                    .GroupBy(v => Mathf.FloorToInt(v.z * 10f)).Select(g => g.Max(v => v.y)).OrderBy(y => y).ToList();
                float rearTop = tops.Count > 0 ? tops[tops.Count / 2] : 0f;
                parts.RearTop = rearTop > min.y + 0.3f ? rearTop : 0f;
            }

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
                centre = spec.SteeringGuess + shift;
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
            else
            {
                parts.Steering = SteeringWheelMesh(spec.Name + "_Steering", wheelR);
                var wheelMat = Lit(spec.Name + "_SteeringWheel", new Color(0.07f, 0.07f, 0.075f), 0.1f, 0.35f);
                wheelMat.SetFloat("_Cull", (float)CullMode.Off); // made here: either winding shows
                parts.SteeringMaterials = new[] { wheelMat };
            }
            // Hands at a quarter to three, on the rim's near face.
            parts.Grips = new[] { new Vector3(-wheelR * 0.93f, wheelR * 0.3f, -0.015f), new Vector3(wheelR * 0.93f, wheelR * 0.3f, -0.015f) };

            // Seats on the measured cushions: the hips on the cushion a quarter metre ahead of the seat back, the eyes
            // upright above (under the roof). The driver sits as far forward as the arms need to hold the rim at a
            // quarter to three with the elbows a little bent (GripReach); the others sit back.
            float roofInside = (parts.HasGlass ? parts.GlassBounds.max.y : bb.max.y) - 0.04f;
            float seatX = Mathf.Abs(centre.x);
            // The seated body's helmet top is HeadAboveCushion over the cushion (the pose rests the hips on it): in a low roof
            // the hips sink into the seat until the head is under the roof's top (it showed through, #155).
            Vector3 Cushion(float x, float backZ, float cushionY) => new Vector3(x, Mathf.Min(cushionY + shift.y, bb.max.y - HeadAboveCushion - 0.03f), backZ + shift.z + 0.25f);
            Vector3 Eye(Vector3 cushion) => new Vector3(cushion.x, Mathf.Min(cushion.y + SeatedEyeUp, roofInside - 0.10f), cushion.z - 0.15f);
            var cushions = new List<Vector3> { Cushion(centre.x, spec.FrontBackZ, spec.FrontCushionY), Cushion(-centre.x, spec.FrontBackZ, spec.FrontCushionY) };
            if (spec.Seats > 2)
            {
                cushions.Add(Cushion(centre.x, spec.RearBackZ, spec.RearCushionY));
                cushions.Add(Cushion(-centre.x, spec.RearBackZ, spec.RearCushionY));
            }
            var eyes = cushions.Select(Eye).ToList();
            var steerTo = Matrix4x4.TRS(centre, parts.SteeringRotation, Vector3.one);
            var driver = eyes[0];
            foreach (int g in new[] { 0, 1 })
            {
                var grip = steerTo.MultiplyPoint3x4(parts.Grips[g]);
                var shoulder = driver + new Vector3(g == 0 ? -ShoulderSide : ShoulderSide, -ShoulderDown, 0f);
                float dx = grip.x - shoulder.x, dy = grip.y - shoulder.y;
                float along = Mathf.Sqrt(Mathf.Max(0.01f, GripReach * GripReach - dx * dx - dy * dy));
                driver.z = Mathf.Max(driver.z, grip.z - along);
            }
            driver.z = Mathf.Min(driver.z, centre.z - 0.25f); // never through the wheel
            eyes[0] = driver;
            parts.SeatEyes = eyes.ToArray();
            parts.SeatCushions = cushions.ToArray();
            // The cabin: between the windows' sides, from below the cushions to the roof, from behind the rearmost
            // seat back to the dash.
            float backMost = (spec.Seats > 2 ? spec.RearBackZ : spec.FrontBackZ) + shift.z - 0.15f;
            float cabinHalf = parts.HasGlass ? parts.GlassBounds.extents.x : bb.extents.x - 0.1f;
            var cabinMin = new Vector3(-cabinHalf, cushions.Min(c => c.y) - 0.35f, backMost);
            var cabinMax = new Vector3(cabinHalf, roofInside, centre.z + 0.35f);
            parts.Cabin = new Bounds((cabinMin + cabinMax) * 0.5f, cabinMax - cabinMin);
            parts.DoorOut = parts.BodyBounds.extents.x + 0.65f;
            return parts;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // A plain steering wheel in its pivot's space (the rim in the XY plane, the column along -Z behind it): a round
    // rim, three spokes and a hub. For a model without one (GoblinV12).
    static Mesh SteeringWheelMesh(string name, float radius)
    {
        const int Around = 32, Tube = 8;
        float tube = radius * 0.11f;
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var tris = new List<int>();
        void Ring(Func<float, float, Vector3> at, Func<float, float, Vector3> normal, int rows, int cols, bool closeRows)
        {
            int start = v.Count;
            for (int i = 0; i <= rows; i++)
                for (int j = 0; j <= cols; j++)
                {
                    float a = i / (float)rows, b = j / (float)cols;
                    v.Add(at(a, b));
                    n.Add(normal(a, b));
                }
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    int k = start + i * (cols + 1) + j;
                    tris.AddRange(new[] { k, k + cols + 1, k + 1, k + 1, k + cols + 1, k + cols + 2 });
                }
        }
        Vector3 Round(float a) => new Vector3(Mathf.Cos(a * Mathf.PI * 2f), Mathf.Sin(a * Mathf.PI * 2f), 0f);
        Vector3 TubeDir(Vector3 radial, float b) => radial * Mathf.Cos(b * Mathf.PI * 2f) + Vector3.forward * Mathf.Sin(b * Mathf.PI * 2f);
        Ring((a, b) => Round(a) * radius + TubeDir(Round(a), b) * tube, (a, b) => TubeDir(Round(a), b), Around, Tube, true);
        // Spokes: left, right and down, from the hub to the rim.
        foreach (float deg in new[] { 0f, 180f, 270f })
        {
            var dir = new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad), 0f);
            var side = Vector3.Cross(dir, Vector3.forward);
            float r0 = radius * 0.22f, r1 = radius;
            Ring((a, b) => dir * Mathf.Lerp(r0, r1, a) + (side * Mathf.Cos(b * Mathf.PI * 2f) * 2.2f + Vector3.forward * Mathf.Sin(b * Mathf.PI * 2f)) * tube * 0.55f,
                (a, b) => side * Mathf.Cos(b * Mathf.PI * 2f) + Vector3.forward * Mathf.Sin(b * Mathf.PI * 2f), 1, Tube, true);
        }
        // Hub: a short cylinder along the column, capped toward the driver.
        float hub = radius * 0.24f;
        Ring((a, b) => Round(b) * hub + Vector3.forward * Mathf.Lerp(-tube * 1.4f, tube * 2.5f, a), (a, b) => Round(b), 1, 16, true);
        int centreIndex = v.Count;
        v.Add(new Vector3(0f, 0f, -tube * 1.4f)); n.Add(Vector3.back);
        for (int j = 0; j <= 16; j++) { v.Add(Round(j / 16f) * hub + Vector3.forward * -tube * 1.4f); n.Add(Vector3.back); }
        for (int j = 0; j < 16; j++) tris.AddRange(new[] { centreIndex, centreIndex + 1 + j, centreIndex + 2 + j });
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    const float DentEdge = 0.15f; // no painted edge longer than this (a blast dent is 1.4 m across, a bullet's 0.4 m)

    // Splits every edge of the `paint` triangles longer than `maxEdge` at its middle, pass after pass, in every triangle
    // that has it (whatever its material): an edge is known by its two end positions, so the triangles on both sides of
    // a UV seam or a material border split alike (no cracks when the dent moves the new vertex). Null paint: unchanged.
    static Mesh Subdivide(Mesh mesh, Material[] materials, Material paint, float maxEdge)
    {
        if (paint == null || mesh == null) return mesh;
        var v = new List<Vector3>(mesh.vertices);
        var n = new List<Vector3>(mesh.normals);
        var uv = new List<Vector2>(mesh.uv);
        var subs = new List<List<int>>();
        for (int s = 0; s < mesh.subMeshCount; s++) subs.Add(new List<int>(mesh.GetTriangles(s)));
        Vector3Int Key(int i) => Vector3Int.RoundToInt(v[i] * 2000f);
        (Vector3Int, Vector3Int) Edge(int a, int b)
        {
            Vector3Int ka = Key(a), kb = Key(b);
            return ka.x < kb.x || (ka.x == kb.x && (ka.y < kb.y || (ka.y == kb.y && ka.z <= kb.z))) ? (ka, kb) : (kb, ka);
        }
        float max2 = maxEdge * maxEdge;
        for (int pass = 0; pass < 8; pass++)
        {
            var marked = new HashSet<(Vector3Int, Vector3Int)>();
            for (int s = 0; s < subs.Count; s++)
            {
                if (s >= materials.Length || materials[s] != paint) continue;
                var t = subs[s];
                for (int i = 0; i + 2 < t.Count; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = t[i + k], b = t[i + (k + 1) % 3];
                        if ((v[a] - v[b]).sqrMagnitude > max2) marked.Add(Edge(a, b));
                    }
            }
            if (marked.Count == 0) break;
            var mids = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (mids.TryGetValue(key, out int m)) return m;
                m = v.Count;
                v.Add((v[a] + v[b]) * 0.5f);
                n.Add((n[a] + n[b]).normalized);
                uv.Add((uv[a] + uv[b]) * 0.5f);
                mids[key] = m;
                return m;
            }
            for (int s = 0; s < subs.Count; s++)
            {
                var t = subs[s];
                var o = new List<int>(t.Count * 2);
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    int[] c = { t[i], t[i + 1], t[i + 2] };
                    bool[] m = new bool[3];
                    int count = 0;
                    for (int k = 0; k < 3; k++) if (m[k] = marked.Contains(Edge(c[k], c[(k + 1) % 3]))) count++;
                    // Rotate so the cases read the same: one split edge is a-b; two split edges leave c-a whole.
                    int rot = 0;
                    if (count == 1) rot = m[0] ? 0 : m[1] ? 1 : 2;
                    else if (count == 2) rot = (!m[0] ? 1 : !m[1] ? 2 : 0);
                    int a = c[rot], b = c[(rot + 1) % 3], d = c[(rot + 2) % 3];
                    switch (count)
                    {
                        case 0: o.AddRange(c); break;
                        case 1: { int ab = Mid(a, b); o.AddRange(new[] { a, ab, d, ab, b, d }); break; }
                        case 2: { int ab = Mid(a, b), bd = Mid(b, d); o.AddRange(new[] { ab, b, bd, a, ab, bd, a, bd, d }); break; }
                        default: { int ab = Mid(a, b), bd = Mid(b, d), da = Mid(d, a); o.AddRange(new[] { a, ab, da, ab, b, bd, da, bd, d, ab, bd, da }); break; }
                    }
                }
                subs[s] = o;
            }
        }
        var result = new Mesh { name = mesh.name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        result.SetVertices(v);
        result.SetNormals(n);
        result.SetUVs(0, uv);
        result.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) result.SetTriangles(subs[s], s);
        result.RecalculateBounds();
        result.RecalculateTangents();
        return result;
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
            // Measured from the mesh (model frame; VehicleBuilder.Measure): the bar ends' grips, the saddle's top where
            // the rider sits (forward, just behind the tank), the foot pegs (the lowest parts standing out sideways) and
            // the tank's sides at knee height.
            var bodyVerts = bodyParts.SelectMany(Verts).ToList();
            var grips = BarGrips(Verts(bar));
            const float seatZ = -0.15f;
            var saddleTop = bodyVerts.Where(v => Mathf.Abs(v.x - CentreX(front, rear)) < 0.08f && Mathf.Abs(v.z - seatZ) < 0.06f).Max(v => v.y);
            var cushion = new Vector3(CentreX(front, rear), saddleTop, seatZ);
            var pegs = Sides(bodyVerts.Where(v => Mathf.Abs(v.x - cushion.x) > 0.13f && v.y < Ground(front, rear) + 0.55f), cushion.x);
            var knees = Sides(bodyVerts.Where(v => v.y > saddleTop - 0.2f && v.y < saddleTop - 0.05f && v.z > seatZ + 0.25f && v.z < seatZ + 0.45f), cushion.x, outermost: true);
            return BuildBike(spec, root, bodyParts, new[] { front }, new[] { rear }, new[] { bar, fork }, swing, 27f, grips, cushion, pegs, knees, leanMinDeg: 25f);
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
            // Measured from the mesh (VehicleBuilder.Measure): the solo saddle ("Leather"), the handlebar (the widest
            // "Ruchki" part), the floorboards (the low "Ruchki" part: a cruiser's feet go forward onto them) and the
            // tank ("Moto 2.002").
            bool Mat(Piece p, string prefix) => p.Renderer.sharedMaterials.Any(m => m != null && m.name.StartsWith(prefix));
            var saddle = pieces.First(p => Mat(p, "Leather")).Bounds;
            var cushion = new Vector3(saddle.center.x, saddle.max.y, saddle.center.z + 0.05f);
            var handlebar = pieces.Where(p => Mat(p, "Ruchki")).OrderByDescending(p => p.Bounds.size.x).First();
            var grips = BarGrips(Verts(handlebar));
            var boards = pieces.Where(p => Mat(p, "Ruchki") && p.Bounds.center.y < frontAxle.y).OrderBy(p => p.Bounds.center.y).First().Bounds;
            var pegs = new[] { new Vector3(boards.center.x - boards.extents.x * 0.6f, boards.max.y, boards.center.z), new Vector3(boards.center.x + boards.extents.x * 0.6f, boards.max.y, boards.center.z) };
            var tank = pieces.First(p => Mat(p, "Moto 2.002")).Bounds;
            var knees = new[] { new Vector3(tank.min.x, tank.center.y, tank.min.z + 0.12f), new Vector3(tank.max.x, tank.center.y, tank.min.z + 0.12f) };
            return BuildBike(spec, root, body, frontWheel, rearWheel, steer, null, 25f, grips, cushion, pegs, knees, leanMinDeg: 8f);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static List<Vector3> Verts(Piece p)
    {
        var m = p.Renderer.localToWorldMatrix;
        return p.Mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToList();
    }

    static float CentreX(Piece front, Piece rear) => (front.Bounds.center.x + rear.Bounds.center.x) * 0.5f;
    static float Ground(Piece front, Piece rear) => Mathf.Min(front.Bounds.min.y, rear.Bounds.min.y);

    // Where the hands go on a handlebar: on the bar a little inboard of its ends (|x| 0.26..0.34 from its middle,
    // or the outer quarter of a narrower bar), left then right; the mean of the bar's own vertices there.
    static Vector3[] BarGrips(List<Vector3> bar)
    {
        float mid = (bar.Min(v => v.x) + bar.Max(v => v.x)) * 0.5f;
        float half = (bar.Max(v => v.x) - bar.Min(v => v.x)) * 0.5f;
        float lo = Mathf.Min(0.26f, half * 0.7f), hi = Mathf.Min(0.34f, half);
        Vector3 Mean(float side)
        {
            var at = bar.Where(v => (v.x - mid) * side >= lo && (v.x - mid) * side <= hi).ToList();
            return new Vector3(at.Average(v => v.x), at.Average(v => v.y), at.Average(v => v.z));
        }
        return new[] { Mean(-1f), Mean(1f) };
    }

    // Left and right points of a set (their mean on each side of `x`; or the outermost points' height and length).
    static Vector3[] Sides(IEnumerable<Vector3> points, float x, bool outermost = false)
    {
        var list = points.ToList();
        Vector3 One(float side)
        {
            var at = list.Where(v => (v.x - x) * side > 0f).ToList();
            if (at.Count == 0) return new Vector3(x + side * 0.2f, 0f, 0f);
            if (!outermost) return new Vector3(at.Average(v => v.x), at.Average(v => v.y), at.Average(v => v.z));
            float far = at.Max(v => (v.x - x) * side);
            return new Vector3(x + side * far, at.Average(v => v.y), at.Average(v => v.z));
        }
        return new[] { One(-1f), One(1f) };
    }

    // Shared by both motorcycles: wheels resting on y = 0 with the axles' midpoint at the origin; the steering part
    // turns about the rake axis through the front axle; the swingarm (if any) about its front end.
    // `grips`, `cushion`, `pegs` and `knees` are measured points in the model frame (left, right).
    static Parts BuildBike(Spec spec, GameObject root, IEnumerable<Piece> body, IList<Piece> frontWheel, IList<Piece> rearWheel, IEnumerable<Piece> steer,
        Piece swing, float rakeDeg, Vector3[] grips, Vector3 cushion, Vector3[] pegs, Vector3[] knees, float leanMinDeg)
    {
        // Axle centres from the tyres (the widest wheel part); each wheel's own radius, both on the ground.
        var fb = Union(frontWheel);
        var rb = Union(rearWheel);
        float rf = Mathf.Max(fb.size.y, fb.size.z) * 0.5f, rr = Mathf.Max(rb.size.y, rb.size.z) * 0.5f;
        float radius = (rf + rr) * 0.5f;
        float ground = ((fb.center.y - rf) + (rb.center.y - rr)) * 0.5f;
        var shift = new Vector3(-(fb.center.x + rb.center.x) * 0.5f, -ground, -(fb.center.z + rb.center.z) * 0.5f);
        var toVehicle = Matrix4x4.Translate(shift);
        var front = fb.center + shift;
        var rear = rb.center + shift;
        // Both axles on the centre line (the model's are a few millimetres apart: the steering axis leaned sideways).
        front.x = rear.x = 0f;
        front.y = rear.y = radius;
        var parts = new Parts { Name = spec.Name, Kind = VehicleKind.Motorcycle, WheelRadius = radius, WheelRadii = new[] { rf, rr }, WheelCentres = new[] { front, rear },
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

        // Grips: measured on the bar, made symmetric about the centre line (the hands mirror each other).
        var gl = grips[0] + shift;
        var gr = grips[1] + shift;
        float gx = (gr.x - gl.x) * 0.5f, gy = (gl.y + gr.y) * 0.5f, gz = (gl.z + gr.z) * 0.5f;
        var gripLeft = new Vector3(-gx, gy, gz);
        var gripRight = new Vector3(gx, gy, gz);
        parts.Grips = new[] { pivotInverse.MultiplyPoint3x4(gripLeft), pivotInverse.MultiplyPoint3x4(gripRight) };

        if (swing != null)
        {
            var sw = swing.Bounds;
            sw.center += shift;
            parts.SwingPivot = new Vector3(0f, sw.center.y + sw.extents.y * 0.35f, sw.max.z - 0.04f);
            parts.Swingarm = CombineGroup(new[] { swing }, Matrix4x4.Translate(-parts.SwingPivot) * toVehicle, spec, spec.Name + "_Swingarm", out parts.SwingarmMaterials);
        }

        // The rider on the measured saddle point, leaning forward from the hips (at least `leanMinDeg`) until the
        // shoulders are within GripReach of the grips; the head up. The pillion behind on the same saddle.
        var seat = cushion + shift;
        seat.x = 0f;
        var hip = seat + Vector3.up * HipUp;
        float lean = leanMinDeg;
        for (; lean < 55f; lean += 1f)
        {
            var up = new Vector3(0f, Mathf.Cos(lean * Mathf.Deg2Rad), Mathf.Sin(lean * Mathf.Deg2Rad));
            var shoulder = hip + up * HipToShoulder;
            float far = Mathf.Max(Vector3.Distance(shoulder + Vector3.left * ShoulderSide, gripLeft), Vector3.Distance(shoulder + Vector3.right * ShoulderSide, gripRight));
            if (far <= GripReach) break;
        }
        var lift = new Vector3(0f, Mathf.Cos(lean * Mathf.Deg2Rad), Mathf.Sin(lean * Mathf.Deg2Rad));
        var pillionSeat = seat + new Vector3(0f, 0.03f, -0.42f);
        parts.SeatEyes = new[] { hip + lift * HipToEye, pillionSeat + Vector3.up * (HipUp + HipToEye) };
        parts.SeatCushions = new[] { seat, pillionSeat };
        Vector3[] Mirror(Vector3[] pair)
        {
            var l = pair[0] + shift; var r = pair[1] + shift;
            float x = (Mathf.Abs(l.x) + Mathf.Abs(r.x)) * 0.5f, y = (l.y + r.y) * 0.5f, z = (l.z + r.z) * 0.5f;
            return new[] { new Vector3(-x, y, z), new Vector3(x, y, z) };
        }
        parts.Pegs = Mirror(pegs);
        parts.Knees = Mirror(knees);
        Debug.Log($"[VehicleModels] {spec.Name}: lean {lean:F0} deg, cushion {seat:F2}, grips {gripLeft:F2} {gripRight:F2}, pegs {parts.Pegs[0]:F2} {parts.Pegs[1]:F2}, knees {parts.Knees[0]:F2}");
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

    // Diagnostics: seat profiles (highest interior point per 5 cm along z in each seat column), steering and bike
    // bars / pegs, in the model's turned frame. tools/unity.sh run VehicleBuilder.Measure
    public static void Measure()
    {
        foreach (var spec in CarSpecs())
        {
            var pieces = Instantiate(spec, out var root);
            try
            {
                var inside = pieces.Where(p => p.Name.Contains("Interior") || p.Name == "car_body").ToList();
                var steer = pieces.Where(p => spec.Steering != null && spec.Steering.IsMatch(p.Name)).ToList();
                float sx = steer.Count > 0 ? Union(steer).center.x : spec.SteeringGuess.x;
                var vs = new List<Vector3>();
                foreach (var p in inside) { var m = p.Renderer.localToWorldMatrix; vs.AddRange(p.Mesh.vertices.Select(v => m.MultiplyPoint3x4(v))); }
                var wheels = pieces.Where(p => spec.Wheel.IsMatch(p.Name)).ToList();
                float ground = wheels.Min(w => w.Bounds.min.y);
                Debug.Log($"[Measure] {spec.Name} steering x {sx:F2} {(steer.Count > 0 ? Union(steer).center.ToString("F2") : "guess")} ground {ground:F2} interior {Union(inside).min.ToString("F2")}..{Union(inside).max.ToString("F2")}");
                foreach (float col in new[] { sx, -sx })
                foreach (float cap in new[] { 1.25f, 1.0f })
                {
                    var sb = new System.Text.StringBuilder();
                    for (float z = -2.4f; z < 2.4f; z += 0.05f)
                    {
                        var band = vs.Where(v => Mathf.Abs(v.x - col) < 0.12f && v.z >= z && v.z < z + 0.05f && v.y < ground + cap).ToList();
                        if (band.Count == 0) continue;
                        sb.Append($" {z:F2}:{band.Max(v => v.y) - ground:F2}");
                    }
                    Debug.Log($"[Measure] {spec.Name} col {col:F2} cap {cap} top(z:y) {sb}");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
        MeasureDirtBike();
        MeasureRoadBike();
    }

    static void MeasureRoadBike()
    {
        var spec = new Spec { Name = "RoadMotorcycle", Folder = "RoadMotorcycle", File = "RoadMotorcycle.fbx", Kind = VehicleKind.Motorcycle, Scale = 12.5f, Remap = m => m };
        var pieces = Instantiate(spec, out var root);
        try
        {
            var tyres = pieces.Where(p => p.Renderer.sharedMaterials.Any(m => m != null && m.name.StartsWith("Mishelin")) && p.Bounds.size.y > 0.4f).OrderByDescending(p => p.Bounds.center.z).ToList();
            float ground = tyres.Min(t => t.Bounds.min.y), cx = tyres.Average(t => t.Bounds.center.x);
            Debug.Log($"[Measure] Road tyres " + string.Join(" ", tyres.Select(t => t.Bounds.center.ToString("F3") + " r" + t.Bounds.extents.y.ToString("F3"))) + $" ground {ground:F3}");
            foreach (var p in pieces)
            {
                var b = p.Bounds;
                Debug.Log($"[Measure] Road piece {p.Name} [{string.Join(",", p.Renderer.sharedMaterials.Select(m => m != null ? m.name : "-"))}] c {(b.center - new Vector3(cx, ground, 0f)).ToString("F2")} s {b.size.ToString("F2")}");
            }
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void MeasureDirtBike()
    {
        var spec = new Spec { Name = "DirtBike", Folder = "DirtBike", File = "DirtBike.fbx", Kind = VehicleKind.Motorcycle, Euler = new Vector3(0f, 180f, 0f),
            RoleOf = _ => Role.Body, Materials = new Dictionary<Role, Material>() };
        var pieces = Instantiate(spec, out var root, "dirt_bike red");
        try
        {
            Piece Named(string n) => pieces.First(q => q.Name == n || q.Name.StartsWith(n + ".", StringComparison.Ordinal));
            var frame = Named("dirt_bike red");
            float cx = (Named("front_wheel").Bounds.center.x + Named("rear_wheel").Bounds.center.x) * 0.5f;
            float ground = Mathf.Min(Named("front_wheel").Bounds.min.y, Named("rear_wheel").Bounds.min.y);
            Debug.Log($"[Measure] DirtBike cx {cx:F3} ground {ground:F3} front {Named("front_wheel").Bounds.center.ToString("F3")} r {Named("front_wheel").Bounds.extents.y:F3} rear {Named("rear_wheel").Bounds.center.ToString("F3")} r {Named("rear_wheel").Bounds.extents.y:F3}");
            List<Vector3> Verts(Piece p) { var m = p.Renderer.localToWorldMatrix; return p.Mesh.vertices.Select(v => m.MultiplyPoint3x4(v) - new Vector3(cx, ground, 0f)).ToList(); }
            var bar = Verts(Named("throttle"));
            foreach (float side in new[] { -1f, 1f })
            {
                var end = bar.Where(v => v.x * side > 0.25f).ToList();
                if (end.Count > 0) Debug.Log($"[Measure] DirtBike bar end {side}: c {new Vector3(end.Average(v => v.x), end.Average(v => v.y), end.Average(v => v.z)).ToString("F3")} |x|max {end.Max(v => Mathf.Abs(v.x)):F3}");
            }
            var body = Verts(frame).Concat(Verts(Named("motor"))).ToList();
            var sb = new System.Text.StringBuilder();
            for (float z = -1f; z < 1f; z += 0.05f)
            {
                var band = body.Where(v => Mathf.Abs(v.x) < 0.08f && v.z >= z && v.z < z + 0.05f).ToList();
                if (band.Count > 0) sb.Append($" {z:F2}:{band.Max(v => v.y):F2}");
            }
            Debug.Log($"[Measure] DirtBike top(z:y){sb}");
            var wide = body.Where(v => Mathf.Abs(v.x) > 0.13f && v.y < 0.55f).ToList();
            foreach (var g in wide.GroupBy(v => Mathf.RoundToInt(v.z * 10f)).OrderBy(g => g.Key))
                Debug.Log($"[Measure] DirtBike wide z {g.Key / 10f:F1}: n {g.Count()} y {g.Min(v => v.y):F2}..{g.Max(v => v.y):F2} |x| {g.Max(v => Mathf.Abs(v.x)):F2}");
            var tank = body.Where(v => v.y > 0.7f).ToList();
            if (tank.Count > 0) Debug.Log($"[Measure] DirtBike upper |x| by z: " + string.Join(" ", tank.GroupBy(v => Mathf.RoundToInt(v.z * 10f)).OrderBy(g => g.Key).Select(g => $"{g.Key / 10f:F1}:{g.Max(v => Mathf.Abs(v.x)):F2}")));
        }
        finally { Object.DestroyImmediate(root); }
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

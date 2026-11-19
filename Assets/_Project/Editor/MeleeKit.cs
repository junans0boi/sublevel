using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using static EditorFields;

// Melee weapons in the builders (ADR-073): the code-built models (MeleeMeshes) in the hand, on the ground and in the
// icons, their three materials, and the first-person and sound components of a combatant.
public static class MeleeKit
{
    const string FoleyDir = "Assets/_Project/Audio/Foley/KenneyImpactSounds/";
    // Where the grip sits ahead of the wrist bone, in the aiming pose (metres).
    const float GripForward = 0.05f;

    // Length of arrays indexed by weapon id that also hold the melee weapons.
    public static int Slots
    {
        get
        {
            int max = 0;
            foreach (int id in MeleeMeshes.Ids) max = Mathf.Max(max, id);
            return max + 1;
        }
    }

    public static bool IsChop(int id) => id == MeleeMeshes.Crowbar || id == MeleeMeshes.Pan;

    // ---- Materials: plain lit colours, no textures ----

    static Material Lit(string name, Color color, float metallic, float smoothness)
    {
        var path = $"{SceneLook.MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Metal, grip, paint (MeleeMeshes' sub-mesh order): worn steel, dark brown handle, muted oxide red.
    public static Material[] Materials => new[]
    {
        Lit("Melee_Metal", new Color(0.56f, 0.57f, 0.58f), 0.85f, 0.45f),
        Lit("Melee_Grip", new Color(0.17f, 0.12f, 0.09f), 0f, 0.25f),
        Lit("Melee_Paint", new Color(0.42f, 0.14f, 0.11f), 0.3f, 0.35f),
    };

    // One melee weapon object, mesh built (for measuring); call Clear on its MeleeModel before saving a prefab.
    public static GameObject Instantiate(int weaponId, Material[] materials)
    {
        var go = new GameObject("Melee" + weaponId);
        var model = go.AddComponent<MeleeModel>();
        model.Setup(weaponId, materials);
        model.Build();
        return go;
    }

    // Turned so a fist grip holds it in the aiming pose (character facing +Z, forearm forward, palm in): the handle
    // stands up through the fist; a slashing blade has its edge to the left (it leads the sweep), a chopping head
    // faces forward (it leads the chop). With the arm hanging, the handle then points forward.
    // The edge is turned halfway to the front (#219): straight to the left, the flat faced forward and back, so with the arm hanging the
    // blade was edge-on to a side view and read as a needle; now the carried blade shows most of its width from the side and the sweep
    // still leads with the edge.
    static Quaternion InHand(int id) => IsChop(id) ? Quaternion.identity : Quaternion.LookRotation((Vector3.left + Vector3.forward).normalized, Vector3.up);

    // Lying on the ground: blades and the bar on their flat side, the pan on its bottom.
    static Quaternion OnGround(int id) => id == MeleeMeshes.Pan
        ? Quaternion.LookRotation(Vector3.down, Vector3.forward) : Quaternion.LookRotation(Vector3.right, Vector3.forward);

    // CharacterKit.AddTo: every melee model on the hand, hidden, with a fit for WeaponVisual (MeleeVisual places it
    // at the eye itself).
    public static void AttachToHand(Transform hand, GameObject[] models, WeaponVisual.Fit[] fits)
    {
        var materials = Materials;
        foreach (int id in MeleeMeshes.Ids)
        {
            var go = Instantiate(id, materials);
            go.transform.rotation = InHand(id);
            go.transform.position = hand.position + Vector3.forward * GripForward;
            var bounds = SceneLook.WorldBounds(go);
            fits[id] = new WeaponVisual.Fit { Rotation = go.transform.rotation, CenterOffset = bounds.center - go.transform.position, Size = bounds.size };
            go.GetComponent<MeleeModel>().Clear();
            go.transform.SetParent(hand, true);
            go.SetActive(false);
            models[id] = go;
        }
    }

    // TestBoxBuilder.BuildLootPrefab: the melee models lying across the pickup point, hidden.
    public static void AddLootModels(Transform parent, GameObject[] models)
    {
        var materials = Materials;
        foreach (int id in MeleeMeshes.Ids)
        {
            var go = Instantiate(id, materials);
            go.transform.rotation = OnGround(id);
            go.transform.position -= SceneLook.WorldBounds(go).center;
            go.GetComponent<MeleeModel>().Clear();
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            models[id] = go;
        }
    }

    // TestBoxBuilder.AddCharacter: first-person swing placement and swing sounds.
    public static void Wire(GameObject combatant)
    {
        combatant.AddComponent<MeleeVisual>();
        var sourceGo = new GameObject("MeleeAudio");
        sourceGo.transform.SetParent(combatant.transform, false);
        sourceGo.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        var source = sourceGo.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.minDistance = 1.5f;
        source.maxDistance = 30f;
        var audio = combatant.AddComponent<MeleeAudio>();
        Set(audio, "_source", p => p.objectReferenceValue = source);
        SetArray(audio, "_impacts", new Object[]
        {
            SceneLook.Load<AudioClip>(FoleyDir + "impactPunch_heavy_000.ogg"),
            SceneLook.Load<AudioClip>(FoleyDir + "impactPunch_heavy_001.ogg"),
            SceneLook.Load<AudioClip>(FoleyDir + "impactPunch_heavy_002.ogg"),
        });
        var rings = new Object[Slots];
        rings[MeleeMeshes.Pan] = SceneLook.Load<AudioClip>(FoleyDir + "impactBell_heavy_000.ogg");
        SetArray(audio, "_ringByWeapon", rings);
    }

    // ItemIconBuilder: how a melee model lies in its icon, seen by a camera on +X looking toward -X (image right is
    // +Z): the handle to the left and the head to the right; blades show their flat side, the pan its bottom a
    // little from below.
    public static Quaternion IconRotation(int id) => id == MeleeMeshes.Pan
        ? Quaternion.LookRotation(new Vector3(0.6f, -0.8f, 0f), Vector3.forward)
        : Quaternion.LookRotation(Vector3.down, Vector3.forward);
}

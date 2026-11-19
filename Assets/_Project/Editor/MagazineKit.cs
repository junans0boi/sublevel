using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using UnityEditor;
using UnityEngine;

// The magazine as a piece of its own on every magazine-fed long gun (#200, ADR-396), made when the prefabs are built so
// nothing is split at run time and the models stay non-readable in a build. The gun body's mesh is searched for its
// magazine island (Core MagazineSplit); the body without it and the magazine are saved as mesh assets next to the model
// (<model folder>/Split/) and the weapon gets a "Magazine" child. Guns whose magazine is not a separate island get a
// block at the magazine well instead (Fallbacks, reported). The model is readable only while it is read.
public static class MagazineKit
{
    // Which guns got a block instead of their own magazine, after the last build (logged by Apply).
    public static readonly SortedSet<int> Fallbacks = new SortedSet<int>();
    static readonly HashSet<int> Logged = new HashSet<int>();
    static readonly Dictionary<Mesh, (Mesh body, Mesh mag)> Done = new Dictionary<Mesh, (Mesh, Mesh)>();

    // Guns whose magazine sits behind the grip (bullpups), and the one whose well stands right over it.
    static readonly int[] Behind = { 3, 13 };
    static readonly Dictionary<int, float> MinGap = new Dictionary<int, float> { { 10, -0.02f } };
    // No magazine at all: the break-action shotgun.
    static readonly int[] None = { 16 };

    // `gun` stands fitted: muzzle along `facing`'s forward, the right hand's grip at `grip`.
    public static void Apply(GameObject gun, CharacterKit.WeaponModel weapon, Vector3 grip, Quaternion facing)
    {
        if (weapon.Length < 0.4f || System.Array.IndexOf(None, weapon.Id) >= 0) return; // pistols keep theirs in the grip
        if (gun.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Magazine")) return; // built with a block already
        MeshFilter body = null;
        foreach (var f in gun.GetComponentsInChildren<MeshFilter>(true))
            if (f.sharedMesh != null && (body == null || f.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = f;
        if (body == null) return;
        var source = body.sharedMesh;
        if (!Done.TryGetValue(source, out var split))
        {
            split = Split(source, body.transform, grip, facing, weapon.Id);
            Done[source] = split;
        }
        var materials = body.GetComponent<MeshRenderer>().sharedMaterials;
        if (split.mag == null)
        {
            if (Fallbacks.Add(weapon.Id)) Debug.Log("[MagazineKit] weapon " + weapon.Id + ": no magazine island, a block at the well instead");
            Block(gun, materials[0], grip, facing, System.Array.IndexOf(Behind, weapon.Id) >= 0);
            return;
        }
        if (Logged.Add(weapon.Id)) Debug.Log("[MagazineKit] weapon " + weapon.Id + ": magazine split off its mesh");
        body.sharedMesh = split.body;
        var go = new GameObject("Magazine");
        go.transform.SetParent(body.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = split.mag;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
    }

    // A box at the magazine well, the size of a rifle magazine, like CharacterKit's Part blocks.
    static void Block(GameObject gun, Material material, Vector3 grip, Quaternion facing, bool behind)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(block.GetComponent<Collider>());
        block.name = "Magazine";
        block.GetComponent<Renderer>().sharedMaterial = material;
        block.transform.SetPositionAndRotation(grip + facing * new Vector3(0f, -0.07f, behind ? -0.12f : 0.09f), facing);
        block.transform.SetParent(gun.transform, true);
        var s = gun.transform.lossyScale;
        block.transform.localScale = new Vector3(0.028f / s.x, 0.1f / s.y, 0.055f / s.z);
    }

    static (Mesh body, Mesh mag) Split(Mesh source, Transform at, Vector3 grip, Quaternion facing, int id)
    {
        string path = AssetDatabase.GetAssetPath(source);
        if (!Copies.TryGetValue(path + "|" + source.name, out var mesh)) return (null, null);
        {
            var verts = mesh.vertices;
            var fit = new System.Numerics.Vector3[verts.Length];
            var toFit = Quaternion.Inverse(facing);
            for (int i = 0; i < verts.Length; i++)
            {
                var p = toFit * (at.TransformPoint(verts[i]) - grip);
                fit[i] = new System.Numerics.Vector3(p.x, p.y, p.z);
            }
            var subs = new int[mesh.subMeshCount][];
            for (int s = 0; s < subs.Length; s++) subs[s] = mesh.GetTriangles(s);
            var isMag = MagazineSplit.Find(fit, subs.SelectMany(t => t).ToArray(), System.Array.IndexOf(Behind, id) >= 0,
                MinGap.TryGetValue(id, out var gap) ? gap : MagazineSplit.MinGripGap);
            if (isMag == null) return (null, null);
            var keep = new int[subs.Length][]; var take = new int[subs.Length][];
            int t0 = 0;
            for (int s = 0; s < subs.Length; s++)
            {
                var k = new List<int>(); var m = new List<int>();
                for (int t = 0; t < subs[s].Length; t += 3)
                    (isMag[t0 + t / 3] ? m : k).AddRange(new[] { subs[s][t], subs[s][t + 1], subs[s][t + 2] });
                t0 += subs[s].Length / 3;
                keep[s] = k.ToArray(); take[s] = m.ToArray();
            }
            string dir = Path.GetDirectoryName(path).Replace('\\', '/') + "/Split";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), "Split");
            string name = Path.GetFileNameWithoutExtension(path);
            return (Save(Compact(mesh, keep, name + "_Body"), dir + "/" + name + "_Body.asset"),
                    Save(Compact(mesh, take, name + "_Mag"), dir + "/" + name + "_Mag.asset"));
        }
    }

    // Readable copies of every gun body mesh, read before any gun is placed: importing a model again resets the
    // instances of it already in the scene, so all of them are opened (readable for the moment, AssetImportRules),
    // copied and closed first.
    static readonly Dictionary<string, Mesh> Copies = new Dictionary<string, Mesh>();
    static bool _prepared;

    public static void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        foreach (var weapon in CharacterKit.Weapons)
        {
            string path = CharacterKit.WeaponDir + weapon.File;
            var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            bool open = meshes.Any(m => !m.isReadable);
            if (open) { AssetImportRules.ReadableWhileSplitting = path; AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
            try
            {
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
                    if (m.isReadable) Copies[path + "|" + m.name] = Object.Instantiate(m);
            }
            finally
            {
                if (open) { AssetImportRules.ReadableWhileSplitting = null; AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
            }
        }
    }

    // Only the vertices these triangles use, every channel the model has.
    static Mesh Compact(Mesh src, int[][] subs, string name)
    {
        var map = new Dictionary<int, int>();
        var order = new List<int>();
        foreach (var sub in subs) foreach (int i in sub) if (!map.ContainsKey(i)) { map[i] = order.Count; order.Add(i); }
        T[] Pick<T>(T[] all) => all != null && all.Length == src.vertexCount ? order.Select(i => all[i]).ToArray() : null;
        var mesh = new Mesh { name = name };
        mesh.vertices = Pick(src.vertices);
        var normals = Pick(src.normals); if (normals != null) mesh.normals = normals;
        var tangents = Pick(src.tangents); if (tangents != null) mesh.tangents = tangents;
        var colors = Pick(src.colors32); if (colors != null) mesh.colors32 = colors;
        for (int ch = 0; ch < 4; ch++)
        {
            var uv = new List<Vector2>();
            src.GetUVs(ch, uv);
            if (uv.Count == src.vertexCount) mesh.SetUVs(ch, order.Select(i => uv[i]).ToList());
        }
        mesh.subMeshCount = subs.Length;
        for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s].Select(i => map[i]).ToArray(), s);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Into the asset at `path`, keeping its GUID when it already exists (the prefabs reference it).
    static Mesh Save(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }
}

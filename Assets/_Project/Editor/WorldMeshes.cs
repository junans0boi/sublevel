using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Mesh helpers of the outdoor map builder: the height field the terrain is made from, boxes combined into one
// mesh per material, road strips, and the asset the generated meshes are stored in.
public static class WorldMeshes
{
    // The heightmap as the terrain will draw it: bilinear between samples, in metres.
    public sealed class HeightField
    {
        readonly float[,] _heights01; // [z, x], 0..1
        readonly int _res;
        readonly float _size, _maxHeight, _cell;

        public HeightField(float[,] heights01, float size, float maxHeight)
        {
            _heights01 = heights01;
            _res = heights01.GetLength(0);
            _size = size;
            _maxHeight = maxHeight;
            _cell = size / (_res - 1);
        }

        public int Resolution => _res;
        public float Size => _size;

        // Height at a heightmap sample.
        public float At(int ix, int iz) => _heights01[Mathf.Clamp(iz, 0, _res - 1), Mathf.Clamp(ix, 0, _res - 1)] * _maxHeight;

        public float Height(float x, float z)
        {
            Sample(x, z, out float height, out _);
            return height;
        }

        // Height and steepness (rise over run) at a world position.
        public void Sample(float x, float z, out float height, out float slopeTan)
        {
            float fx = Mathf.Clamp(x / _cell, 0f, _res - 1), fz = Mathf.Clamp(z / _cell, 0f, _res - 1);
            int ix = Mathf.Min((int)fx, _res - 2), iz = Mathf.Min((int)fz, _res - 2);
            float tx = fx - ix, tz = fz - iz;
            float h00 = _heights01[iz, ix], h10 = _heights01[iz, ix + 1], h01 = _heights01[iz + 1, ix], h11 = _heights01[iz + 1, ix + 1];
            height = Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz) * _maxHeight;
            float gx = Mathf.Lerp(h10 - h00, h11 - h01, tz) * _maxHeight / _cell;
            float gz = Mathf.Lerp(h01 - h00, h11 - h10, tx) * _maxHeight / _cell;
            slopeTan = Mathf.Sqrt(gx * gx + gz * gz);
        }
    }

    // Generated meshes live outside the scene (which would otherwise carry every vertex as text). Meshes added with Add go
    // into one YAML asset (the few vegetation meshes that components reference directly). Meshes added with AddBlob, the
    // ones that only fill MeshFilters (buildings, roads, elevators, sea), go into World_Meshes.bytes (#240): Finish writes
    // that file and empties those filters, and WorldMeshBank builds the meshes again when the scene loads.
    // (The project serializes assets as YAML, so every vertex byte in an asset is two hex characters; ADR-160.)
    public sealed class Store
    {
        readonly string _path, _blobPath;
        bool _created;
        readonly List<Mesh> _blobMeshes = new List<Mesh>();

        public int Count { get; private set; }
        public long Triangles { get; private set; }
        public int BlobCount => _blobMeshes.Count;

        public Store(string path, string blobPath)
        {
            _path = path;
            _blobPath = blobPath;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        }

        public Mesh Add(Mesh mesh)
        {
            if (!_created)
            {
                AssetDatabase.CreateAsset(mesh, _path);
                _created = true;
            }
            else AssetDatabase.AddObjectToAsset(mesh, _path);
            Count++;
            for (int i = 0; i < mesh.subMeshCount; i++) Triangles += mesh.GetIndexCount(i) / 3;
            return mesh;
        }

        // For a mesh that is only ever assigned to a MeshFilter below the root given to Finish.
        public Mesh AddBlob(Mesh mesh)
        {
            _blobMeshes.Add(mesh);
            Count++;
            for (int i = 0; i < mesh.subMeshCount; i++) Triangles += mesh.GetIndexCount(i) / 3;
            return mesh;
        }

        // Writes the blob file, empties the filters that use its meshes and puts a WorldMeshBank on `root`. Call just before saving the scene.
        public void Finish(GameObject root)
        {
            var index = new Dictionary<Mesh, int>();
            var records = new List<MeshRecord>(_blobMeshes.Count);
            foreach (var mesh in _blobMeshes) { index[mesh] = records.Count; records.Add(ToRecord(mesh)); }
            var bytes = WorldMeshBlob.Write(records);
            System.IO.File.WriteAllBytes(_blobPath, bytes);
            AssetDatabase.ImportAsset(_blobPath, ImportAssetOptions.ForceSynchronousImport);
            var blob = AssetDatabase.LoadAssetAtPath<TextAsset>(_blobPath);

            var filters = new List<MeshFilter>();
            var meshIndex = new List<int>();
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || !index.TryGetValue(f.sharedMesh, out int i)) continue;
                filters.Add(f); meshIndex.Add(i);
                f.sharedMesh = null;
            }
            var bank = root.GetComponent<WorldMeshBank>() ?? root.AddComponent<WorldMeshBank>();
            bank.Set(blob, filters.ToArray(), meshIndex.ToArray());
            Debug.Log($"[WorldMeshes] {records.Count} meshes in {_blobPath}: {bytes.Length / 1048576.0:F1} MB, {filters.Count} mesh filters left empty for WorldMeshBank");
        }

        static MeshRecord ToRecord(Mesh mesh)
        {
            var attrs = mesh.GetVertexAttributes();
            var record = new MeshRecord
            {
                Name = mesh.name, VertexCount = mesh.vertexCount, Index32 = mesh.indexFormat == IndexFormat.UInt32,
                Attributes = new int[attrs.Length * 4],
            };
            int streams = 0;
            for (int i = 0; i < attrs.Length; i++)
            {
                record.Attributes[i * 4] = (int)attrs[i].attribute; record.Attributes[i * 4 + 1] = (int)attrs[i].format;
                record.Attributes[i * 4 + 2] = attrs[i].dimension; record.Attributes[i * 4 + 3] = attrs[i].stream;
                streams = Mathf.Max(streams, attrs[i].stream + 1);
            }
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var d = data[0];
                record.Streams = new byte[streams][];
                for (int s = 0; s < streams; s++) record.Streams[s] = d.GetVertexData<byte>(s).ToArray();
                record.Indices = d.GetIndexData<byte>().ToArray();
            }
            record.SubMeshes = new int[mesh.subMeshCount * 6];
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var sub = mesh.GetSubMesh(i);
                record.SubMeshes[i * 6] = sub.indexStart; record.SubMeshes[i * 6 + 1] = sub.indexCount; record.SubMeshes[i * 6 + 2] = (int)sub.topology;
                record.SubMeshes[i * 6 + 3] = sub.baseVertex; record.SubMeshes[i * 6 + 4] = sub.firstVertex; record.SubMeshes[i * 6 + 5] = sub.vertexCount;
            }
            var b = mesh.bounds;
            record.Bounds = new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z };
            return record;
        }
    }

    // Axis-aligned boxes in one local frame, combined into a single mesh. Texture coordinates come from the
    // position in that frame, so the pattern keeps its size and runs on across neighbouring boxes of one wall.
    public sealed class BoxBatch
    {
        static readonly (Vector3 n, Vector3 r, Vector3 u)[] Faces =
        {
            (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.right, Vector3.back),
            (Vector3.forward, Vector3.left, Vector3.up), (Vector3.back, Vector3.right, Vector3.up),
            (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.back, Vector3.up),
        };

        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<Vector4> _road = new List<Vector4>(); // second UV set for Game/WorldRoad: across, along (m), lines, half width (m)
        readonly List<Vector2> _ends = new List<Vector2>(); // third UV set: metres from the road's start and to its end
        readonly List<int> _triangles = new List<int>();
        readonly Vector2 _tile;

        public BoxBatch(Vector2 metersPerTile)
        {
            _tile = new Vector2(Mathf.Max(metersPerTile.x, 0.01f), Mathf.Max(metersPerTile.y, 0.01f));
        }

        public int Boxes { get; private set; }

        // Faces in the order up, down, +Z, -Z, +X, -X; bit f of skipFaces leaves face f out (a side that lies against a wall or the floor).
        public void Add(Vector3 center, Vector3 size, bool skipBottom = false, int skipFaces = 0)
        {
            var h = size * 0.5f;
            for (int f = 0; f < Faces.Length; f++)
            {
                if (skipBottom && f == 1) continue;
                if ((skipFaces >> f & 1) != 0) continue;
                var (n, r, u) = Faces[f];
                float halfR = Mathf.Abs(Vector3.Dot(r, h)), halfU = Mathf.Abs(Vector3.Dot(u, h));
                var c = center + Vector3.Scale(n, h);
                int first = _vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    float sr = i == 1 || i == 2 ? 1f : -1f, su = i >= 2 ? 1f : -1f;
                    var p = c + r * (sr * halfR) + u * (su * halfU);
                    _vertices.Add(p);
                    _normals.Add(n);
                    _uvs.Add(new Vector2(Vector3.Dot(p, r) / _tile.x, Vector3.Dot(p, u) / _tile.y));
                }
                _triangles.Add(first); _triangles.Add(first + 2); _triangles.Add(first + 1);
                _triangles.Add(first); _triangles.Add(first + 3); _triangles.Add(first + 2);
            }
            Boxes++;
        }

        // A box turned about its own centre (set dressing placed in world space). The pattern follows the box.
        public void Add(Vector3 center, Vector3 size, Quaternion rotation, bool skipBottom = false, int skipFaces = 0)
        {
            int from = _vertices.Count;
            Add(Vector3.zero, size, skipBottom, skipFaces);
            for (int i = from; i < _vertices.Count; i++)
            {
                _vertices[i] = center + rotation * _vertices[i];
                _normals[i] = rotation * _normals[i];
                _uvs[i] += new Vector2(center.x + center.z, center.y) / _tile.x; // neighbours do not share a pattern
            }
        }

        // An upright cylinder standing on `foot` (barrels, poles); the texture wraps once around.
        public void AddCylinder(Vector3 foot, float radius, float height, int sides = 10)
        {
            int first = _vertices.Count;
            float around = 2f * Mathf.PI * radius;
            for (int i = 0; i <= sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < 2; k++)
                {
                    _vertices.Add(foot + n * radius + Vector3.up * (k * height));
                    _normals.Add(n);
                    _uvs.Add(new Vector2(k * height / _tile.x, around * i / sides / _tile.y)); // the grain runs up the cylinder
                }
            }
            for (int i = 0; i < sides; i++)
            {
                int a = first + i * 2, b = a + 1, c = a + 2, d = a + 3;
                _triangles.Add(a); _triangles.Add(b); _triangles.Add(c);
                _triangles.Add(c); _triangles.Add(b); _triangles.Add(d);
            }
            int cap = _vertices.Count;
            _vertices.Add(foot + Vector3.up * height);
            _normals.Add(Vector3.up);
            _uvs.Add(Vector2.zero);
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                _vertices.Add(foot + n * radius + Vector3.up * height);
                _normals.Add(Vector3.up);
                _uvs.Add(new Vector2(n.x, n.z) * radius / _tile.x);
            }
            for (int i = 0; i < sides; i++)
            {
                _triangles.Add(cap); _triangles.Add(cap + 1 + (i + 1) % sides); _triangles.Add(cap + 1 + i);
            }
            Boxes++;
        }

        // A triangular prism (a gable roof, a ridge tent) with its base centre at `center`: size.x across the ridge, size.y
        // the ridge height, size.z along the ridge, turned about `center` by `rotation`. `slopes` the two roof faces, `ends`
        // the two triangles at the gables, so the two can be drawn in different materials.
        public void AddPrism(Vector3 center, Vector3 size, Quaternion rotation, bool slopes, bool ends)
        {
            float a = size.x * 0.5f, h = size.y, l = size.z * 0.5f;
            int from = _vertices.Count;
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
            {
                int first = _vertices.Count;
                foreach (var p in new[] { p0, p1, p2, p3 }) { _vertices.Add(p); _normals.Add(n); }
                _uvs.Add(u0); _uvs.Add(u1); _uvs.Add(u2); _uvs.Add(u3);
                _triangles.Add(first); _triangles.Add(first + 2); _triangles.Add(first + 1);
                _triangles.Add(first); _triangles.Add(first + 3); _triangles.Add(first + 2);
            }
            float slope = Mathf.Sqrt(a * a + h * h);
            if (slopes)
            {
                var nr = new Vector3(h, a, 0f).normalized;
                var nl = new Vector3(-h, a, 0f).normalized;
                float lu = size.z / _tile.x, su = slope / _tile.y;
                Quad(new Vector3(a, 0, -l), new Vector3(0, h, -l), new Vector3(0, h, l), new Vector3(a, 0, l), nr, new Vector2(0, 0), new Vector2(0, su), new Vector2(lu, su), new Vector2(lu, 0));
                Quad(new Vector3(-a, 0, l), new Vector3(0, h, l), new Vector3(0, h, -l), new Vector3(-a, 0, -l), nl, new Vector2(0, 0), new Vector2(0, su), new Vector2(lu, su), new Vector2(lu, 0));
            }
            if (ends)
            {
                for (int e = 0; e < 2; e++)
                {
                    float z = e == 0 ? l : -l, sgn = e == 0 ? 1f : -1f;
                    int first = _vertices.Count;
                    var n = new Vector3(0, 0, sgn);
                    _vertices.Add(new Vector3(-a * sgn, 0, z)); _vertices.Add(new Vector3(a * sgn, 0, z)); _vertices.Add(new Vector3(0, h, z));
                    for (int i = 0; i < 3; i++) { _normals.Add(n); var q = _vertices[first + i]; _uvs.Add(new Vector2(q.x / _tile.x, q.y / _tile.y)); }
                    _triangles.Add(first); _triangles.Add(first + 2); _triangles.Add(first + 1);
                }
            }
            for (int i = from; i < _vertices.Count; i++)
            {
                _vertices[i] = center + rotation * _vertices[i];
                _normals[i] = rotation * _normals[i];
                _uvs[i] += new Vector2(center.x + center.z, center.y) / _tile.x;
            }
            Boxes++;
        }

        public bool Empty => _triangles.Count == 0;

        // Vertex data is stored compact (position 12 + normal 4 + tangent 4 + uv 8 = 28 bytes instead of 48): normals and
        // tangents are unit vectors, so signed bytes are plenty. The tangents come from Unity, then the stream is rewritten.
        public Mesh ToMesh(string name) => ToMesh(name, new[] { this });

        // Half-precision positions and UVs (20 bytes a vertex): only for meshes in a small local frame (a building), where 1.5 cm at 30 m is invisible (ADR-209).
        public static Mesh ToCompactMesh(string name, IReadOnlyList<BoxBatch> batches) => ToMesh(name, batches, true);

        // One mesh with one sub-mesh per batch, in the compact vertex format (ADR-209: a building's materials share one renderer).
        public static Mesh ToMesh(string name, IReadOnlyList<BoxBatch> batches, bool compact = false)
        {
            int total = 0;
            foreach (var b in batches) total += b._vertices.Count;
            var mesh = new Mesh { name = name };
            if (total > 65000) mesh.indexFormat = IndexFormat.UInt32;
            if (compact)
                mesh.SetVertexBufferParams(total,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2));
            else
                mesh.SetVertexBufferParams(total,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            var data = compact ? null : new Packed[total];
            var small = compact ? new PackedHalf[total] : null;
            var indices = new List<int>();
            var starts = new List<int>();
            int offset = 0;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
            foreach (var b in batches)
            {
                foreach (var v in b._vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
                var temp = new Mesh();
                if (b._vertices.Count > 65000) temp.indexFormat = IndexFormat.UInt32;
                temp.SetVertices(b._vertices);
                temp.SetNormals(b._normals);
                temp.SetUVs(0, b._uvs);
                temp.SetTriangles(b._triangles, 0);
                temp.RecalculateTangents();
                var tangents = temp.tangents;
                Object.DestroyImmediate(temp);
                for (int i = 0; i < b._vertices.Count; i++)
                {
                    var n = b._normals[i]; var t = tangents[i];
                    if (compact)
                        small[offset + i] = new PackedHalf
                        {
                            PX = Half(b._vertices[i].x), PY = Half(b._vertices[i].y), PZ = Half(b._vertices[i].z), PW = Half(1f),
                            N0 = Snorm(n.x), N1 = Snorm(n.y), N2 = Snorm(n.z), N3 = 0,
                            T0 = Snorm(t.x), T1 = Snorm(t.y), T2 = Snorm(t.z), T3 = Snorm(t.w),
                            U = Half(b._uvs[i].x), V = Half(b._uvs[i].y),
                        };
                    else
                    data[offset + i] = new Packed
                    {
                        Position = b._vertices[i], Uv = b._uvs[i],
                        N0 = Snorm(n.x), N1 = Snorm(n.y), N2 = Snorm(n.z), N3 = 0,
                        T0 = Snorm(t.x), T1 = Snorm(t.y), T2 = Snorm(t.z), T3 = Snorm(t.w),
                    };
                }
                starts.Add(indices.Count);
                foreach (int tri in b._triangles) indices.Add(tri + offset);
                offset += b._vertices.Count;
            }
            if (compact) mesh.SetVertexBufferData(small, 0, 0, small.Length); else mesh.SetVertexBufferData(data, 0, 0, data.Length);
            mesh.SetIndexBufferParams(indices.Count, total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16);
            if (total > 65000) mesh.SetIndexBufferData(indices, 0, 0, indices.Count); else { var u = new ushort[indices.Count]; for (int i = 0; i < u.Length; i++) u[i] = (ushort)indices[i]; mesh.SetIndexBufferData(u, 0, 0, u.Length); }
            mesh.subMeshCount = batches.Count;
            for (int i = 0; i < batches.Count; i++)
            {
                int end = i + 1 < batches.Count ? starts[i + 1] : indices.Count;
                mesh.SetSubMesh(i, new UnityEngine.Rendering.SubMeshDescriptor(starts[i], end - starts[i]) { bounds = new Bounds((min + max) * 0.5f, max - min) }, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            }
            if (total > 0) mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
            return mesh;
        }

        static ushort Half(float value)
        {
            int bits = System.BitConverter.SingleToInt32Bits(value);
            int sign = (bits >> 16) & 0x8000, exp = ((bits >> 23) & 0xFF) - 127 + 15, mant = bits & 0x7FFFFF;
            if (exp <= 0) return (ushort)sign;
            if (exp >= 31) return (ushort)(sign | 0x7BFF);
            int r = (mant + 0x1000) >> 13;                      // round to nearest
            return (ushort)(sign | ((exp << 10) + r));
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PackedHalf
        {
            public ushort PX, PY, PZ, PW;
            public sbyte N0, N1, N2, N3, T0, T1, T2, T3;
            public ushort U, V;
        }

        static sbyte Snorm(float v) => (sbyte)Mathf.Clamp(Mathf.RoundToInt(v * 127f), -127, 127);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Packed
        {
            public Vector3 Position;
            public sbyte N0, N1, N2, N3, T0, T1, T2, T3;
            public Vector2 Uv;
        }
    }

    // Collision stand-in for many boxes: positions only (12 bytes a vertex), eight vertices and twelve triangles a box.
    public static Mesh BoxCollisionMesh(string name, IReadOnlyList<(Vector3 center, Vector3 size)> boxes)
    {
        var vertices = new List<Vector3>(boxes.Count * 8);
        var triangles = new List<int>(boxes.Count * 36);
        int[] quad = { 0, 2, 1, 0, 3, 2 };
        // corner order: bit0 = +x, bit1 = +y, bit2 = +z; faces as index quads (outward winding checked by the face normals below)
        int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } };
        foreach (var (c, s) in boxes)
        {
            int first = vertices.Count;
            for (int k = 0; k < 8; k++) vertices.Add(c + new Vector3((k & 1) != 0 ? s.x : -s.x, (k & 2) != 0 ? s.y : -s.y, (k & 4) != 0 ? s.z : -s.z) * 0.5f);
            foreach (var f in faces)
                foreach (int q in quad) triangles.Add(first + f[q]);
        }
        var mesh = new Mesh { name = name };
        if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Road surfaces: strips three vertices wide laid on the ground along polylines, combined into one mesh.
    public sealed class StripBatch
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<Vector4> _road = new List<Vector4>(); // second UV set for Game/WorldRoad: across, along (m), lines, half width (m)
        readonly List<Vector2> _ends = new List<Vector2>(); // third UV set: metres from the road's start and to its end
        readonly List<int> _triangles = new List<int>();
        readonly Vector2 _tile;

        public StripBatch(Vector2 metersPerTile)
        {
            _tile = new Vector2(Mathf.Max(metersPerTile.x, 0.01f), Mathf.Max(metersPerTile.y, 0.01f));
        }

        public bool Empty => _triangles.Count == 0;
        public float Length { get; private set; }

        // points: the centre line in the ground plane (x, z), already sampled finely enough to follow the ground.
        public void Add(IReadOnlyList<Vector2> points, float width, HeightField ground, float lift)
        {
            if (points.Count < 2 || width <= 0f) return;
            int first = _vertices.Count;
            float along = 0f;
            var right = Vector2.right;
            for (int i = 0; i < points.Count; i++)
            {
                var tangent = points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)];
                if (tangent.sqrMagnitude > 1e-6f)
                {
                    tangent.Normalize();
                    right = new Vector2(tangent.y, -tangent.x); // forward (0, 1) has right (1, 0)
                }
                if (i > 0) along += Vector2.Distance(points[i - 1], points[i]);
                for (int k = 0; k < 3; k++)
                {
                    var p = points[i] + right * ((k - 1) * width * 0.5f);
                    _vertices.Add(new Vector3(p.x, ground.Height(p.x, p.y) + lift, p.y));
                    _uvs.Add(new Vector2(k * width * 0.5f / _tile.x, along / _tile.y));
                    _road.Add(new Vector4(k - 1, along, width >= 6f ? 1f : 0f, width * 0.5f));
                }
            }
            for (int v = first; v < _road.Count; v++) _ends.Add(new Vector2(_road[v].y, along - _road[v].y));
            for (int i = 0; i < points.Count - 1; i++)
            {
                for (int k = 0; k < 2; k++)
                {
                    int a = first + i * 3 + k, b = a + 1, c = a + 3, d = c + 1;
                    _triangles.Add(a); _triangles.Add(c); _triangles.Add(b);
                    _triangles.Add(b); _triangles.Add(c); _triangles.Add(d);
                }
            }
            Length += along;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _road);
            mesh.SetUVs(2, _ends);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // A flat rectangle facing up, centred on the origin.
    public static Mesh Quad(string name, float sizeX, float sizeZ)
    {
        float hx = sizeX * 0.5f, hz = sizeZ * 0.5f;
        var mesh = new Mesh { name = name };
        mesh.SetVertices(new List<Vector3> { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) });
        mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
        mesh.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}

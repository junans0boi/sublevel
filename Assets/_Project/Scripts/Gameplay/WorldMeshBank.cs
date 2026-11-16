using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // #240: the generated world meshes (buildings, roads, elevators, sea) are stored in World_Meshes.bytes, not in the scene
    // or in a YAML asset. This component builds them when the scene loads and hands them to the mesh filters that
    // WorldBuilder left empty. Runs in the editor too, so an opened World scene looks the same as a played one.
    [ExecuteAlways, DefaultExecutionOrder(-1000)]
    public sealed class WorldMeshBank : MonoBehaviour
    {
        [SerializeField] TextAsset _blob;
        [SerializeField] MeshFilter[] _filters = new MeshFilter[0];
        [SerializeField] int[] _meshIndex = new int[0];

        Mesh[] _built;

        public void Set(TextAsset blob, MeshFilter[] filters, int[] meshIndex)
        {
            _blob = blob; _filters = filters; _meshIndex = meshIndex;
        }

        void OnEnable() => Apply();

        void OnDestroy()
        {
            if (_built == null) return;
            foreach (var m in _built) if (m != null) DestroyImmediate(m);
            _built = null;
        }

        void Apply()
        {
            if (_blob == null || _filters.Length == 0) return;
            if (_built == null)
            {
                var records = WorldMeshBlob.Read(_blob.bytes);
                _built = new Mesh[records.Count];
                for (int i = 0; i < records.Count; i++) _built[i] = Build(records[i]);
            }
            for (int i = 0; i < _filters.Length; i++)
                if (_filters[i] != null) _filters[i].sharedMesh = _built[_meshIndex[i]];
        }

        public static Mesh Build(MeshRecord r)
        {
            var mesh = new Mesh { name = r.Name, hideFlags = HideFlags.DontSave };
            var attributes = new VertexAttributeDescriptor[r.Attributes.Length / 4];
            for (int i = 0; i < attributes.Length; i++)
                attributes[i] = new VertexAttributeDescriptor((VertexAttribute)r.Attributes[i * 4], (VertexAttributeFormat)r.Attributes[i * 4 + 1], r.Attributes[i * 4 + 2], r.Attributes[i * 4 + 3]);
            mesh.SetVertexBufferParams(r.VertexCount, attributes);
            for (int s = 0; s < r.Streams.Length; s++)
                mesh.SetVertexBufferData(r.Streams[s], 0, 0, r.Streams[s].Length, s, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            if (r.Index32)
            {
                var idx = new int[r.Indices.Length / 4];
                System.Buffer.BlockCopy(r.Indices, 0, idx, 0, r.Indices.Length);
                mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt32);
                mesh.SetIndexBufferData(idx, 0, 0, idx.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            else
            {
                var idx = new ushort[r.Indices.Length / 2];
                System.Buffer.BlockCopy(r.Indices, 0, idx, 0, r.Indices.Length);
                mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt16);
                mesh.SetIndexBufferData(idx, 0, 0, idx.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            int subs = r.SubMeshes.Length / 6;
            mesh.subMeshCount = subs;
            var bounds = new Bounds(new Vector3(r.Bounds[0], r.Bounds[1], r.Bounds[2]), new Vector3(r.Bounds[3], r.Bounds[4], r.Bounds[5]));
            for (int i = 0; i < subs; i++)
            {
                int o = i * 6;
                mesh.SetSubMesh(i, new SubMeshDescriptor(r.SubMeshes[o], r.SubMeshes[o + 1], (MeshTopology)r.SubMeshes[o + 2])
                {
                    baseVertex = r.SubMeshes[o + 3], firstVertex = r.SubMeshes[o + 4], vertexCount = r.SubMeshes[o + 5], bounds = bounds,
                }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            mesh.bounds = bounds;
            return mesh;
        }
    }
}

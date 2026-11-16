using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Core
{
    // One mesh as raw numbers: Unity's vertex layout (attribute, format, dimension, stream ids as ints), one byte array per
    // vertex stream, the index buffer and the sub-mesh ranges. Kept free of UnityEngine so it can be tested without Unity.
    public sealed class MeshRecord
    {
        public string Name = "";
        public int VertexCount;
        public int[] Attributes = new int[0];            // 4 ints per attribute: attribute, format, dimension, stream
        public byte[][] Streams = new byte[0][];
        public bool Index32;
        public byte[] Indices = new byte[0];
        public int[] SubMeshes = new int[0];             // 6 ints per sub-mesh: indexStart, indexCount, topology, baseVertex, firstVertex, vertexCount
        public float[] Bounds = new float[6];            // centre xyz, size xyz
    }

    // World_Meshes.bytes (#240): the generated world meshes as a binary file instead of 43 MB of hexadecimal YAML.
    // Layout (little endian): "WMSH", u16 version, i32 meshCount, then per mesh the fields of MeshRecord.
    public static class WorldMeshBlob
    {
        public const ushort Version = 1;
        static readonly byte[] Magic = { (byte)'W', (byte)'M', (byte)'S', (byte)'H' };

        public static byte[] Write(IReadOnlyList<MeshRecord> meshes)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(meshes.Count);
                foreach (var m in meshes)
                {
                    w.Write(m.Name);
                    w.Write(m.VertexCount);
                    w.Write(m.Attributes.Length / 4);
                    foreach (int v in m.Attributes) w.Write(v);
                    w.Write(m.Streams.Length);
                    foreach (var s in m.Streams) { w.Write(s.Length); w.Write(s); }
                    w.Write(m.Index32);
                    w.Write(m.Indices.Length); w.Write(m.Indices);
                    w.Write(m.SubMeshes.Length / 6);
                    foreach (int v in m.SubMeshes) w.Write(v);
                    foreach (float v in m.Bounds) w.Write(v);
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        // A bad or truncated file throws instead of producing empty meshes.
        public static List<MeshRecord> Read(byte[] data)
        {
            if (data == null || data.Length < 10) throw new InvalidDataException("mesh blob too short");
            try
            {
                using (var stream = new MemoryStream(data))
                using (var r = new BinaryReader(stream, Encoding.UTF8))
                {
                    var magic = r.ReadBytes(4);
                    for (int i = 0; i < 4; i++)
                        if (magic[i] != Magic[i]) throw new InvalidDataException("mesh blob: wrong magic");
                    ushort version = r.ReadUInt16();
                    if (version != Version) throw new InvalidDataException($"mesh blob: version {version}, expected {Version}");
                    int count = r.ReadInt32();
                    if (count < 0 || count > 1000000) throw new InvalidDataException("mesh blob: bad mesh count");
                    var list = new List<MeshRecord>(count);
                    for (int k = 0; k < count; k++)
                    {
                        var m = new MeshRecord { Name = r.ReadString(), VertexCount = r.ReadInt32() };
                        m.Attributes = ReadInts(r, r.ReadInt32() * 4);
                        int streams = Checked(r.ReadInt32(), 8);
                        m.Streams = new byte[streams][];
                        for (int s = 0; s < streams; s++) m.Streams[s] = ReadBytes(r, r.ReadInt32());
                        m.Index32 = r.ReadBoolean();
                        m.Indices = ReadBytes(r, r.ReadInt32());
                        m.SubMeshes = ReadInts(r, r.ReadInt32() * 6);
                        m.Bounds = new float[6];
                        for (int i = 0; i < 6; i++) m.Bounds[i] = r.ReadSingle();
                        list.Add(m);
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("mesh blob: trailing bytes");
                    return list;
                }
            }
            catch (EndOfStreamException e) { throw new InvalidDataException("mesh blob truncated", e); }
        }

        static int Checked(int n, int max)
        {
            if (n < 0 || n > max) throw new InvalidDataException("mesh blob: bad length");
            return n;
        }

        static byte[] ReadBytes(BinaryReader r, int n)
        {
            if (n < 0) throw new InvalidDataException("mesh blob: bad length");
            var b = r.ReadBytes(n);
            if (b.Length != n) throw new InvalidDataException("mesh blob truncated");
            return b;
        }

        static int[] ReadInts(BinaryReader r, int n)
        {
            if (n < 0 || n > 1 << 20) throw new InvalidDataException("mesh blob: bad length");
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadInt32();
            return a;
        }
    }
}

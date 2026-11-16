using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #240: the generated world meshes are stored as World_Meshes.bytes, not as hexadecimal YAML.
    public class WorldMeshBlobTests
    {
        static MeshRecord Sample(string name, int seed)
        {
            var rng = new System.Random(seed);
            var vertices = new byte[28 * 12]; rng.NextBytes(vertices);
            var uv1 = new byte[16 * 12]; rng.NextBytes(uv1);
            var idx = new byte[36 * 2]; rng.NextBytes(idx);
            return new MeshRecord
            {
                Name = name, VertexCount = 12,
                Attributes = new[] { 0, 0, 3, 0, 1, 2, 4, 0, 6, 0, 2, 1 },
                Streams = new[] { vertices, uv1 },
                Index32 = false, Indices = idx,
                SubMeshes = new[] { 0, 36, 0, 0, 0, 12 },
                Bounds = new[] { 1f, 2f, 3f, 4f, 5f, 6f },
            };
        }

        static void AssertSame(MeshRecord a, MeshRecord b)
        {
            Assert.AreEqual(a.Name, b.Name);
            Assert.AreEqual(a.VertexCount, b.VertexCount);
            CollectionAssert.AreEqual(a.Attributes, b.Attributes);
            Assert.AreEqual(a.Streams.Length, b.Streams.Length);
            for (int i = 0; i < a.Streams.Length; i++) CollectionAssert.AreEqual(a.Streams[i], b.Streams[i]);
            Assert.AreEqual(a.Index32, b.Index32);
            CollectionAssert.AreEqual(a.Indices, b.Indices);
            CollectionAssert.AreEqual(a.SubMeshes, b.SubMeshes);
            CollectionAssert.AreEqual(a.Bounds, b.Bounds);
        }

        [Test]
        public void Write_then_read_gives_the_same_meshes()
        {
            var meshes = new List<MeshRecord> { Sample("Lot00_House_Shell_Wall", 1), Sample("Road_Trunk", 2), Sample("Sea", 3) };
            var back = WorldMeshBlob.Read(WorldMeshBlob.Write(meshes));
            Assert.AreEqual(3, back.Count);
            for (int i = 0; i < 3; i++) AssertSame(meshes[i], back[i]);
        }

        [Test]
        public void The_same_meshes_give_the_same_bytes()
        {
            var a = WorldMeshBlob.Write(new[] { Sample("a", 7), Sample("b", 8) });
            var b = WorldMeshBlob.Write(new[] { Sample("a", 7), Sample("b", 8) });
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Names_may_be_non_ascii()
        {
            var back = WorldMeshBlob.Read(WorldMeshBlob.Write(new[] { Sample("청람읍_Lot01", 4) }));
            Assert.AreEqual("청람읍_Lot01", back[0].Name);
        }

        [Test]
        public void A_bad_file_throws_instead_of_giving_empty_meshes()
        {
            var good = WorldMeshBlob.Write(new[] { Sample("a", 1) });
            Assert.Throws<InvalidDataException>(() => WorldMeshBlob.Read(null));
            Assert.Throws<InvalidDataException>(() => WorldMeshBlob.Read(new byte[3]));

            var wrongMagic = (byte[])good.Clone(); wrongMagic[0] = (byte)'X';
            Assert.Throws<InvalidDataException>(() => WorldMeshBlob.Read(wrongMagic));

            var wrongVersion = (byte[])good.Clone(); wrongVersion[4] = 99;
            Assert.Throws<InvalidDataException>(() => WorldMeshBlob.Read(wrongVersion));

            Assert.Throws<InvalidDataException>(() => WorldMeshBlob.Read(good.Take(good.Length - 5).ToArray()));
            Assert.Throws<InvalidDataException>(() => WorldMeshBlob.Read(good.Concat(new byte[] { 0 }).ToArray()));
        }
    }
}

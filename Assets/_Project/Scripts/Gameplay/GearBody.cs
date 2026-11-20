using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // A roster character's torso surface (ADR-520), measured in the editor (GearKit.MeasureBodies) at the prefab's standing
    // pose. Frame: the character's axes (+z forward, +y up), origin on the upper-chest bone. On a grid of heights x angles
    // around the torso axis it holds the outer surface radius (clothes included, arms left out) and the skin weights of
    // the torso bones; per height the axis centre. GearFit wraps the worn vest and pack onto this surface and skins them
    // to the same bones, so they sit on the body like clothing and bend with it.
    public class GearBody : ScriptableObject
    {
        public static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        };
        public const int Root = 3; // UpperChest: the frame origin

        public string Id;
        public float Y0, Dy;            // height of row 0, row spacing
        public int Ny, Na;              // rows, angle columns (angle 0 = front, increasing toward +x)
        public float ShoulderTop, Waist, Width;
        public Vector2[] Centre;        // per row: axis x, z
        public float[] Radius;          // Ny * Na
        public float[] Weights;         // Ny * Na * Bones.Length, each cell sums to 1
        public Matrix4x4[] BindPoses;   // per bone: bone.worldToLocal * frame-to-world, at the standing pose

        public static GearBody Load(string id) => string.IsNullOrEmpty(id) ? null : Resources.Load<GearBody>(WornGearMeshes.ResourceDir + "/Bodies/" + id);

        // The bone for slot i on a Humanoid rig; a missing optional bone (UpperChest, shoulders) falls back toward the spine.
        public static Transform Bone(Animator animator, int i)
        {
            for (var b = Bones[i]; ; )
            {
                var t = animator.GetBoneTransform(b);
                if (t != null) return t;
                switch (b)
                {
                    case HumanBodyBones.UpperChest: b = HumanBodyBones.Chest; break;
                    case HumanBodyBones.Chest: b = HumanBodyBones.Spine; break;
                    case HumanBodyBones.LeftShoulder: case HumanBodyBones.RightShoulder: b = HumanBodyBones.UpperChest; break;
                    default: return animator.GetBoneTransform(HumanBodyBones.Hips);
                }
            }
        }

        public float Row(float y) => Mathf.Clamp((y - Y0) / Dy, 0f, Ny - 1);
        public static float Column(float angle, int na) => Mathf.Repeat(angle / (2f * Mathf.PI) * na, na);

        public Vector2 CentreAt(float y)
        {
            float r = Row(y); int a = Mathf.FloorToInt(r), b = Mathf.Min(a + 1, Ny - 1);
            return Vector2.Lerp(Centre[a], Centre[b], r - a);
        }

        public float RadiusAt(float y, float angle) => Sample(Radius, Row(y), Column(angle, Na), Ny, Na);

        // Bilinear on a rows x columns grid that wraps around in columns.
        public static float Sample(float[] grid, float row, float col, int ny, int na)
        {
            int r0 = Mathf.FloorToInt(row), r1 = Mathf.Min(r0 + 1, ny - 1), c0 = Mathf.FloorToInt(col) % na, c1 = (c0 + 1) % na;
            float fr = row - r0, fc = col - Mathf.Floor(col);
            float top = Mathf.Lerp(grid[r0 * na + c0], grid[r0 * na + c1], fc), bottom = Mathf.Lerp(grid[r1 * na + c0], grid[r1 * na + c1], fc);
            return Mathf.Lerp(top, bottom, fr);
        }
    }

    // Wraps a baked vest or pack (WornGearMeshes.Baked) onto a GearBody and skins it to its torso bones. One fitted mesh per
    // body, piece and tier, shared by every character of that body.
    public static class GearFit
    {
        const float VestGap = 0.012f, PackGap = 0.008f;
        // The wrap follows the torso up to a little below the shoulder top; above it (straps over the shoulders) the parts
        // move with that ring and keep their own shape.
        const float WrapBelowShoulder = 0.05f;
        // Where the pack's top sits against the shoulder top, by tier: the small pack below it, the big one above.
        static readonly float[] PackTop = { -0.03f, 0.0f, 0.06f };
        static readonly Dictionary<(string, int, int), Mesh> Cache = new Dictionary<(string, int, int), Mesh>();

        public static bool Wraps(LootKind kind) => kind == LootKind.Vest || kind == LootKind.Backpack;

        public static Mesh For(GearBody body, LootKind kind, int tier)
        {
            tier = Mathf.Clamp(tier, 1, 3);
            var key = (body.Id, (int)kind, tier);
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            var baked = WornGearMeshes.Baked(kind, tier);
            if (baked == null || !baked.isReadable) return null;
            m = Fit(body, kind, tier, baked);
            Cache[key] = m;
            return m;
        }

        // A skinned renderer on the character for a wrapped piece (hidden until a tier is shown). It sits under the animated
        // model, so a copy of the model (InventoryPreview) takes it along with its bones.
        public static SkinnedMeshRenderer AddRenderer(Animator animator, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(animator.transform, false);
            var bones = new Transform[GearBody.Bones.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = GearBody.Bone(animator, i);
            go.layer = bones[GearBody.Root].gameObject.layer;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.bones = bones;
            smr.rootBone = bones[GearBody.Root];
            smr.localBounds = new Bounds(new Vector3(0f, -0.1f, -0.05f), new Vector3(1.2f, 1.2f, 1.2f)); // around the torso in any pose
            smr.meshLodSelectionBias = WornGearMeshes.LodBias;
            smr.enabled = false;
            return smr;
        }

        public static Mesh Fit(GearBody body, LootKind kind, int tier, Mesh baked)
        {
            var src = baked.vertices;
            var b = baked.bounds;
            var v = new Vector3[src.Length];
            var wrap = new bool[src.Length]; // vest: all of it; pack: only what lies in front of its back panel (the shoulder straps)
            float wrapTop = body.ShoulderTop - WrapBelowShoulder;
            // 1) Rough placement in the body frame. Vest: from the waist to just over the shoulder top, centred on the torso
            //    axis. Pack: its back panel (the body-side face at mid height) on the back, its top at the tier's height.
            if (kind == LootKind.Vest)
            {
                float top = body.ShoulderTop + 0.015f, s = (top - body.Waist) / b.size.y;
                var c = body.CentreAt((top + body.Waist) * 0.5f);
                for (int i = 0; i < v.Length; i++)
                {
                    var p = src[i];
                    v[i] = new Vector3((p.x - b.center.x) * s + c.x, (p.y - b.min.y) * s + body.Waist, (p.z - b.center.z) * s + c.y);
                    wrap[i] = true;
                }
            }
            else
            {
                // The back panel: GearKit bakes every pack with its body-side panel on the code model's back face (Shift
                // tuned per pack, straps excluded), so that plane is known.
                float panel = WornGearMeshes.CodeModel(kind, tier).bounds.max.z;
                float top = body.ShoulderTop + PackTop[tier - 1];
                float yMid = top - b.size.y * 0.5f;
                var c = body.CentreAt(Mathf.Min(yMid, wrapTop));
                float back = c.y - body.RadiusAt(Mathf.Min(yMid, wrapTop), Mathf.PI) - PackGap;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = src[i];
                    v[i] = new Vector3(p.x - b.center.x + c.x, p.y - b.max.y + top, p.z - panel + back);
                    wrap[i] = p.z > panel + 0.01f;
                }
            }

            // 2) Radial wrap: per cell of the body grid, the piece's inner surface (smallest radius of its vertices there,
            //    smoothed) moves onto the body surface plus a gap; every vertex keeps its distance from that inner surface,
            //    so pouches, plates and the pack body keep their thickness. Above the wrap top the ring's move is applied.
            int ny = body.Ny, na = body.Na;
            var inner = new float[ny * na];
            for (int i = 0; i < inner.Length; i++) inner[i] = float.MaxValue;
            var polar = new Vector3[v.Length]; // row, column, radius
            for (int i = 0; i < v.Length; i++)
            {
                float y = Mathf.Min(v[i].y, wrapTop);
                var c = body.CentreAt(y);
                float dx = v[i].x - c.x, dz = v[i].z - c.y;
                float row = body.Row(y), col = GearBody.Column(Mathf.Atan2(dx, dz), na);
                polar[i] = new Vector3(row, col, Mathf.Sqrt(dx * dx + dz * dz));
                if (!wrap[i] || v[i].y > wrapTop) continue; // straps over the shoulder do not define the inner surface
                int cell = Mathf.RoundToInt(row) * na + Mathf.RoundToInt(col) % na;
                if (polar[i].z < inner[cell]) inner[cell] = polar[i].z;
            }
            Fill(inner, ny, na);
            Blur(inner, ny, na);
            float gap = kind == LootKind.Vest ? VestGap : PackGap;
            var weights = new BoneWeight[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var q = polar[i];
                weights[i] = Weight(body, q.x, q.y);
                if (!wrap[i]) continue;
                float rIn = GearBody.Sample(inner, q.x, q.y, ny, na), rBody = GearBody.Sample(body.Radius, q.x, q.y, ny, na);
                // Outside the inner surface: same distance from the body; inside it (straps over the shoulders, near the
                // neck): the ring's ratio, so they follow the body's width instead of being pushed out to it.
                float r = q.z >= rIn ? rBody + gap + (q.z - rIn) : q.z * (rBody + gap) / Mathf.Max(rIn, 1e-3f);
                float angle = q.y / na * 2f * Mathf.PI;
                var c = body.CentreAt(Mathf.Min(v[i].y, wrapTop));
                // Only the radial distance changes; the height and angle stay.
                float scale = q.z > 1e-4f ? r / q.z : 1f;
                v[i] = new Vector3(c.x + (v[i].x - c.x) * scale, v[i].y, c.y + (v[i].z - c.y) * scale);
                if (q.z <= 1e-4f) v[i] = new Vector3(c.x + Mathf.Sin(angle) * r, v[i].y, c.y + Mathf.Cos(angle) * r);
            }

            var m = Object.Instantiate(baked);
            m.name = $"{baked.name}_{body.Id}";
            m.vertices = v;
            m.boneWeights = weights;
            m.bindposes = body.BindPoses;
            m.RecalculateBounds();
            return m;
        }

        // The four strongest bones of the nearest cell (rounded), renormalised.
        static BoneWeight Weight(GearBody body, float row, float col)
        {
            int nb = GearBody.Bones.Length, cell = Mathf.RoundToInt(row) * body.Na + Mathf.RoundToInt(col) % body.Na;
            var w = new (int bone, float weight)[nb];
            for (int k = 0; k < nb; k++) w[k] = (k, body.Weights[cell * nb + k]);
            System.Array.Sort(w, (a, b) => b.weight.CompareTo(a.weight));
            float sum = w[0].weight + w[1].weight + w[2].weight + w[3].weight;
            if (sum <= 1e-5f) return new BoneWeight { boneIndex0 = GearBody.Root, weight0 = 1f };
            return new BoneWeight
            {
                boneIndex0 = w[0].bone, weight0 = w[0].weight / sum, boneIndex1 = w[1].bone, weight1 = w[1].weight / sum,
                boneIndex2 = w[2].bone, weight2 = w[2].weight / sum, boneIndex3 = w[3].bone, weight3 = w[3].weight / sum,
            };
        }

        // Empty cells (float.MaxValue) take the nearest filled cell around their row, else the nearest filled row.
        public static void Fill(float[] g, int ny, int na)
        {
            const float Empty = float.MaxValue;
            var o = (float[])g.Clone();
            for (int r = 0; r < ny; r++)
                for (int c = 0; c < na; c++)
                {
                    if (o[r * na + c] != Empty) continue;
                    for (int d = 1; d <= na / 2; d++)
                    {
                        float x = Mathf.Min(o[r * na + (c + d) % na], o[r * na + (c - d + na) % na]);
                        if (x != Empty) { g[r * na + c] = x; break; }
                    }
                }
            var rows = (float[])g.Clone();
            for (int r = 0; r < ny; r++)
            {
                if (rows[r * na] != Empty) continue;
                for (int d = 1; d < ny; d++)
                {
                    int from = r - d >= 0 && rows[(r - d) * na] != Empty ? r - d : r + d < ny && rows[(r + d) * na] != Empty ? r + d : -1;
                    if (from < 0) continue;
                    System.Array.Copy(rows, from * na, g, r * na, na);
                    break;
                }
            }
        }

        // 3x3 box blur (wrapping in columns, clamped in rows).
        public static void Blur(float[] g, int ny, int na)
        {
            var o = (float[])g.Clone();
            for (int r = 0; r < ny; r++)
                for (int c = 0; c < na; c++)
                {
                    float sum = 0f; int n = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        int rr = r + dr; if (rr < 0 || rr >= ny) continue;
                        for (int dc = -1; dc <= 1; dc++) { sum += o[rr * na + (c + dc + na) % na]; n++; }
                    }
                    g[r * na + c] = sum / n;
                }
        }
    }
}

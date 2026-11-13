using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // What a bullet impact looks like per surface (ADR-118, #152): dust colour and size, the flash (bright orange
    // sparks on metal, almost none on soil, none on a body, which shows a red mist), and the mark it leaves.
    public struct ImpactLook
    {
        public Color Dust;
        public float DustSize, Flash;
        public Color FlashColor;
        public Color Mark;   // alpha 0: no mark
        public float MarkSize; // metres across
    }

    public static class ImpactLooks
    {
        static readonly ImpactLook[] Table =
        {
            /* Ground   */ Look(0.45f, 0.36f, 0.26f, 0.85f, 1.3f, 0.35f, 1f, 0.85f, 0.6f, 0.20f, 0.14f, 0.09f, 0.55f, 0.20f),
            /* Concrete */ Look(0.70f, 0.70f, 0.68f, 0.80f, 1.0f, 1.00f, 1f, 0.95f, 0.85f, 0.10f, 0.10f, 0.10f, 0.85f, 0.10f),
            /* Wood     */ Look(0.62f, 0.46f, 0.28f, 0.80f, 0.8f, 0.50f, 1f, 0.80f, 0.50f, 0.12f, 0.08f, 0.04f, 0.85f, 0.09f),
            /* Metal    */ Look(0.75f, 0.75f, 0.75f, 0.50f, 0.5f, 1.80f, 1f, 0.72f, 0.28f, 0.08f, 0.08f, 0.09f, 0.90f, 0.08f),
            /* Glass    */ Look(0.90f, 0.95f, 1.00f, 0.30f, 0.6f, 0.60f, 0.8f, 0.90f, 1.00f, 0f, 0f, 0f, 0f, 0f),
            /* Vehicle  */ Look(0.75f, 0.75f, 0.75f, 0.50f, 0.5f, 1.80f, 1f, 0.72f, 0.28f, 0f, 0f, 0f, 0f, 0f), // cars keep their own holes (VehicleDamage)
            /* Body     */ Look(0.55f, 0.05f, 0.05f, 0.70f, 0.7f, 0.00f, 1f, 0.30f, 0.20f, 0f, 0f, 0f, 0f, 0f),
        };

        static ImpactLook Look(float dr, float dg, float db, float da, float dustSize, float flash, float fr, float fg, float fb,
            float mr, float mg, float mb, float ma, float markSize) => new ImpactLook
        {
            Dust = new Color(dr, dg, db, da), DustSize = dustSize, Flash = flash, FlashColor = new Color(fr, fg, fb, 1f),
            Mark = new Color(mr, mg, mb, ma), MarkSize = markSize,
        };

        public static ImpactLook Of(BulletSurface surface) => Table[Mathf.Clamp((int)surface, 0, Table.Length - 1)];
    }

    // Bullet marks on walls and ground: a ring of small dark discs, the oldest reused first. They live in the scene.
    public static class BulletMarks
    {
        const int Count = 48;
        const float Lift = 0.012f; // off the surface, against flicker

        static Transform _root;
        static MeshRenderer[] _discs;
        static int _next;
        static MaterialPropertyBlock _block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static int Placed { get; private set; }

        public static void Add(Vector3 point, Vector3 normal, in ImpactLook look, Material material)
        {
            if (look.Mark.a <= 0f || material == null) return;
            if (_root == null)
            {
                _root = new GameObject("BulletMarks").transform;
                _discs = new MeshRenderer[Count];
                _block = new MaterialPropertyBlock();
                _next = 0;
            }
            var disc = _discs[_next];
            if (disc == null)
            {
                var go = new GameObject("Mark");
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = CombatFxMeshes.Disc;
                disc = go.AddComponent<MeshRenderer>();
                disc.sharedMaterial = material;
                disc.shadowCastingMode = ShadowCastingMode.Off;
                disc.receiveShadows = false;
                _discs[_next] = disc;
            }
            _next = (_next + 1) % Count;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            var t = disc.transform;
            t.SetPositionAndRotation(point + normal * Lift, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            t.localScale = Vector3.one * look.MarkSize;
            _block.SetColor(BaseColorId, look.Mark);
            disc.SetPropertyBlock(_block);
            disc.enabled = true;
            Placed++;
        }
    }
}

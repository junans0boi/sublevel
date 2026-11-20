using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Every tree and bush of the outdoor map (#71, ADR-070), drawn instanced: a few draw calls per species and
    // level of detail instead of one renderer per tree. Filled by WorldBuilder; the trunks' colliders are separate
    // GameObjects the builder places. Culled per 64 m cell against each camera's frustum, LOD picked per plant from
    // its distance, its size, the camera's zoom and the graphics level (Vegetation.Lod).
    [ExecuteAlways]
    public sealed class VegetationRenderer : MonoBehaviour
    {
        [Serializable]
        public sealed class Lod
        {
            public Mesh Mesh;
            public Material[] Materials;    // one per submesh
            public bool CastShadows;
        }

        [Serializable]
        public sealed class Prototype
        {
            public string Name;
            public PlantKind Kind;
            public Lod[] Lods;              // full detail, cards, billboard (bushes: one)
            public float Height;            // metres at scale 1
        }

        const float CellSize = 64f;

        [SerializeField] Prototype[] _prototypes = new Prototype[0];
        [SerializeField] int[] _prototype = new int[0];             // per plant
        [SerializeField] Vector4[] _placement = new Vector4[0];      // x, y, z of the foot, scale
        [SerializeField] float[] _yaw = new float[0];

        sealed class Cell
        {
            public Bounds Bounds;
            public readonly List<int> Plants = new List<int>();
        }

        Matrix4x4[] _matrices = new Matrix4x4[0];
        readonly List<Cell> _cells = new List<Cell>();
        Bounds _bounds;
        Matrix4x4[][][] _batches;   // [prototype][lod] -> matrices this frame
        int[][] _counts;
        readonly Plane[] _planes = new Plane[6];
        bool _ready;

        public int Count => _placement.Length;
        public IReadOnlyList<Prototype> Prototypes => _prototypes;
        public Vector3 Foot(int plant) => _placement[plant];
        public PlantKind KindOf(int plant) => _prototypes[_prototype[plant]].Kind;
        public int CountOf(PlantKind kind)
        {
            int n = 0;
            for (int i = 0; i < _prototype.Length; i++)
                if (_prototypes[_prototype[i]].Kind == kind) n++;
            return n;
        }

        // Builder entry: the prototypes and one (prototype, foot, scale, yaw) per plant.
        public void Fill(Prototype[] prototypes, int[] prototype, Vector4[] placement, float[] yaw)
        {
            if (prototype.Length != placement.Length || yaw.Length != placement.Length) throw new ArgumentException("one prototype, placement and yaw per plant");
            _prototypes = prototypes;
            _prototype = prototype;
            _placement = placement;
            _yaw = yaw;
            Prepare();
        }

        void OnEnable()
        {
            Prepare();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        void Prepare()
        {
            _ready = false;
            _cells.Clear();
            int n = _placement.Length;
            if (n == 0 || _prototypes.Length == 0) return;
            _matrices = new Matrix4x4[n];
            var grid = new Dictionary<long, Cell>();
            bool first = true;
            for (int i = 0; i < n; i++)
            {
                var p = _placement[i];
                var foot = new Vector3(p.x, p.y, p.z);
                _matrices[i] = Matrix4x4.TRS(foot, Quaternion.Euler(0f, _yaw[i], 0f), Vector3.one * p.w);
                float height = Mathf.Max(1f, _prototypes[_prototype[i]].Height * p.w);
                var box = new Bounds(foot + Vector3.up * (height * 0.5f), new Vector3(height, height, height));
                long key = ((long)Mathf.FloorToInt(p.x / CellSize) << 32) ^ (uint)Mathf.FloorToInt(p.z / CellSize);
                if (!grid.TryGetValue(key, out var cell))
                {
                    grid[key] = cell = new Cell { Bounds = box };
                    _cells.Add(cell);
                }
                cell.Plants.Add(i);
                cell.Bounds.Encapsulate(box);
                if (first) { _bounds = box; first = false; }
                else _bounds.Encapsulate(box);
            }
            _batches = new Matrix4x4[_prototypes.Length][][];
            _counts = new int[_prototypes.Length][];
            for (int p = 0; p < _prototypes.Length; p++)
            {
                int lods = _prototypes[p].Lods != null ? _prototypes[p].Lods.Length : 0;
                _batches[p] = new Matrix4x4[lods][];
                _counts[p] = new int[lods];
                for (int l = 0; l < lods; l++) _batches[p][l] = new Matrix4x4[64];
            }
            _ready = true;
        }

        // ADR-487: in the wood the third-person car camera looks at the car through boughs and bushes. For this camera a plant is not drawn
        // when its crown (flat radius Reach x its height, plus ClearMargin) crosses the flat sight line from the camera to the car (ClearPivot;
        // the car's centre and roof share it seen from above), or holds the camera. A plant past the car (more than ClearPastCar beyond it
        // along the line) is behind the car on screen and stays. Only drawing changes: trunk colliders, cover and physics do not.
        // Set each frame by VehicleCamera, null otherwise.
        public static Camera ClearAround;
        public static Vector3 ClearPivot;
        // Crown radius / height: conifer bottom boughs 0.22..0.26, broadleaf crowns 0.30..0.42, bushes 0.6..1.3 (the widest is low, so the margin covers it).
        public const float CrownReach = 0.28f, BroadleafReach = 0.45f, BushReach = 1f, ClearMargin = 0.5f, ClearPastCar = 1.5f;

        public static bool Cleared(PlantKind kind, float height, Vector3 at, Vector3 eye, Vector3 pivot)
        {
            float reach = (kind == PlantKind.Conifer ? CrownReach : kind == PlantKind.Broadleaf ? BroadleafReach : BushReach) * height + ClearMargin;
            float px = at.x - eye.x, pz = at.z - eye.z, sx = pivot.x - eye.x, sz = pivot.z - eye.z;
            float length = Mathf.Sqrt(sx * sx + sz * sz);
            if (length < 1e-3f) return px * px + pz * pz < reach * reach;
            float along = (px * sx + pz * sz) / length;
            if (along > length + ClearPastCar) return false;
            float t = Mathf.Clamp(along, 0f, length) / length;
            float ox = px - sx * t, oz = pz - sz * t;
            return ox * ox + oz * oz < reach * reach;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!_ready || !VegetationDraw.Draws(cam, gameObject.layer)) return;
            int level = VegetationDraw.Level;
            float zoom = cam.orthographic ? 1f : Vegetation.Zoom(cam.fieldOfView);
            float reach = VegetationDraw.Reach(cam);
            // Casters outside the view still throw shadows into it: inside the shadow distance no cell is skipped.
            float shadowReach = QualitySettings.shadowDistance;
            var eye = cam.transform.position;
            bool clear = cam == ClearAround;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            for (int p = 0; p < _counts.Length; p++) Array.Clear(_counts[p], 0, _counts[p].Length);

            foreach (var cell in _cells)
            {
                float cellDistance = Mathf.Sqrt(cell.Bounds.SqrDistance(eye));
                if (cellDistance > reach) continue;
                if (cellDistance > shadowReach && !GeometryUtility.TestPlanesAABB(_planes, cell.Bounds)) continue;
                foreach (int i in cell.Plants)
                {
                    var at = _placement[i];
                    float distance = Vector3.Distance(eye, new Vector3(at.x, at.y, at.z));
                    if (distance > reach) continue;
                    int p = _prototype[i];
                    var proto = _prototypes[p];
                    if (clear && Cleared(proto.Kind, proto.Height * at.w, at, eye, ClearPivot)) continue;
                    // From straight above (the map pictures) billboards would be edge-on: the card LOD is used instead.
                    int lod = cam.orthographic ? Mathf.Min(1, proto.Lods.Length - 1) : Vegetation.Lod(proto.Kind, distance, at.w, zoom, level);
                    if (lod < 0) continue;
                    lod = Mathf.Min(lod, proto.Lods.Length - 1);
                    VegetationDraw.Append(ref _batches[p][lod], ref _counts[p][lod], _matrices[i]);
                }
            }

            for (int p = 0; p < _prototypes.Length; p++)
            {
                var lods = _prototypes[p].Lods;
                for (int l = 0; l < lods.Length; l++)
                {
                    int count = _counts[p][l];
                    var lod = lods[l];
                    if (count == 0 || lod.Mesh == null || lod.Materials == null) continue;
                    for (int s = 0; s < lod.Materials.Length && s < lod.Mesh.subMeshCount; s++)
                    {
                        if (lod.Materials[s] == null) continue;
                        var rp = new RenderParams(lod.Materials[s])
                        {
                            camera = cam,
                            layer = gameObject.layer,
                            worldBounds = _bounds,
                            shadowCastingMode = lod.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                            receiveShadows = true,
                        };
                        VegetationDraw.Draw(rp, lod.Mesh, s, _batches[p][l], count);
                    }
                }
            }
        }
    }
}

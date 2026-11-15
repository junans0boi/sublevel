using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Grass clumps and wetland reeds round the camera (#71, ADR-070). The builder bakes where grass grows into a byte
    // map (Vegetation.EncodeGrass); here the clumps of each 8 m cell near a camera are scattered from it when first
    // needed (Vegetation.ScatterGrass, seeded, so every machine sees the same grass), set on the terrain, kept in a
    // cache and drawn instanced. Draw distance and density follow the graphics level: none on low.
    [ExecuteAlways]
    public sealed class GrassRenderer : MonoBehaviour
    {
        [SerializeField] TextAsset _map;
        [SerializeField] int _resolution;
        [SerializeField] float _mapCell = Vegetation.GrassMapCell;
        [SerializeField] int _seed;
        [SerializeField] Terrain _terrain;
        [SerializeField] Mesh _short, _tall, _flowerMesh;
        [SerializeField] Material _material, _flowerMaterial;

        // Cells scattered per camera per frame at most, so turning round does not stall a frame.
        const int BuildsPerFrame = 40;
        const int CacheCells = 1600;

        sealed class Cell
        {
            public Matrix4x4[] Short = new Matrix4x4[0], Tall = new Matrix4x4[0], Flower = new Matrix4x4[0];
            public Bounds Bounds;
            public int LastUsed;
        }

        byte[] _bytes;
        readonly Dictionary<long, Cell> _cache = new Dictionary<long, Cell>();
        readonly List<GrassClump> _scratch = new List<GrassClump>(128);
        readonly List<long> _evict = new List<long>();
        readonly Plane[] _planes = new Plane[6];
        Matrix4x4[] _short0 = new Matrix4x4[256], _tall0 = new Matrix4x4[256], _flower0 = new Matrix4x4[64];

        public bool HasMap => _bytes != null && _resolution > 0;

        // Builder entry.
        public void Fill(TextAsset map, int resolution, float mapCell, int seed, Terrain terrain, Mesh shortClump, Mesh tallClump, Material material,
            Mesh flowerClump = null, Material flowerMaterial = null)
        {
            _map = map;
            _resolution = resolution;
            _mapCell = mapCell;
            _seed = seed;
            _terrain = terrain;
            _short = shortClump;
            _tall = tallClump;
            _material = material;
            _flowerMesh = flowerClump;
            _flowerMaterial = flowerMaterial;
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
            _cache.Clear();
            _bytes = _map != null ? _map.bytes : null;
            if (_bytes != null && _bytes.Length != _resolution * _resolution) _bytes = null;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!HasMap || _material == null || _short == null || _tall == null || _terrain == null) return;
            if (!VegetationDraw.Draws(cam, gameObject.layer) || cam.orthographic) return;
            int level = VegetationDraw.Level;
            float radius = Mathf.Min(Vegetation.GrassRadius[level], VegetationDraw.Reach(cam));
            float share = Vegetation.GrassShare[level];
            if (radius <= 0f || share <= 0f) return;

            var eye = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            int frame = Time.frameCount, builds = 0, shortCount = 0, tallCount = 0, flowerCount = 0;
            const float size = Vegetation.GrassCell;
            int x0 = Mathf.FloorToInt((eye.x - radius) / size), x1 = Mathf.FloorToInt((eye.x + radius) / size);
            int z0 = Mathf.FloorToInt((eye.z - radius) / size), z1 = Mathf.FloorToInt((eye.z + radius) / size);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    float nx = Mathf.Clamp(eye.x, cx * size, cx * size + size), nz = Mathf.Clamp(eye.z, cz * size, cz * size + size);
                    float near = Mathf.Sqrt((nx - eye.x) * (nx - eye.x) + (nz - eye.z) * (nz - eye.z));
                    if (near > radius) continue;
                    long key = ((long)cx << 32) ^ (uint)cz;
                    if (!_cache.TryGetValue(key, out var cell))
                    {
                        if (builds >= BuildsPerFrame || cx < 0 || cz < 0) continue;
                        cell = Build(cx, cz);
                        _cache[key] = cell;
                        builds++;
                    }
                    cell.LastUsed = frame;
                    if (cell.Short.Length + cell.Tall.Length + cell.Flower.Length == 0 || !GeometryUtility.TestPlanesAABB(_planes, cell.Bounds)) continue;
                    // The outer part of the circle thins out, so the edge is not a hard line of grass.
                    float mid = Mathf.Sqrt((cx * size + size * 0.5f - eye.x) * (cx * size + size * 0.5f - eye.x) + (cz * size + size * 0.5f - eye.z) * (cz * size + size * 0.5f - eye.z));
                    float thin = share * Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(radius * 0.6f, radius, mid));
                    int s = Mathf.CeilToInt(cell.Short.Length * thin), t = Mathf.CeilToInt(cell.Tall.Length * thin), f = Mathf.CeilToInt(cell.Flower.Length * thin);
                    for (int i = 0; i < f; i++) VegetationDraw.Append(ref _flower0, ref flowerCount, cell.Flower[i]);
                    for (int i = 0; i < s; i++) VegetationDraw.Append(ref _short0, ref shortCount, cell.Short[i]);
                    for (int i = 0; i < t; i++) VegetationDraw.Append(ref _tall0, ref tallCount, cell.Tall[i]);
                }
            }

            var rp = new RenderParams(_material)
            {
                camera = cam,
                layer = gameObject.layer,
                worldBounds = new Bounds(eye, Vector3.one * (radius * 2f + 20f)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
            };
            if (shortCount > 0) VegetationDraw.Draw(rp, _short, 0, _short0, shortCount);
            if (tallCount > 0) VegetationDraw.Draw(rp, _tall, 0, _tall0, tallCount);
            if (flowerCount > 0 && _flowerMesh != null && _flowerMaterial != null)
                VegetationDraw.Draw(new RenderParams(_flowerMaterial) { camera = cam, layer = rp.layer, worldBounds = rp.worldBounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true },
                    _flowerMesh, 0, _flower0, flowerCount);

            if (_cache.Count > CacheCells) Evict(frame);
        }

        Cell Build(int cx, int cz)
        {
            Vegetation.ScatterGrass(_bytes, _resolution, _mapCell, cx, cz, _seed, _scratch);
            var cell = new Cell();
            int shorts = 0, talls = 0, flowers = 0;
            foreach (var c in _scratch) { if (c.Tall) talls++; else if (c.Flower) flowers++; else shorts++; }
            cell.Short = new Matrix4x4[shorts];
            cell.Tall = new Matrix4x4[talls];
            cell.Flower = new Matrix4x4[flowers];
            float low = float.MaxValue, high = float.MinValue;
            int si = 0, ti = 0, fi = 0;
            float baseY = _terrain.GetPosition().y;
            foreach (var c in _scratch)
            {
                // A little into the ground so a clump on a slope shows no gap under its uphill side.
                float y = _terrain.SampleHeight(new Vector3(c.X, 0f, c.Z)) + baseY - 0.05f;
                var m = Matrix4x4.TRS(new Vector3(c.X, y, c.Z), Quaternion.Euler(0f, c.YawDeg, 0f), Vector3.one * c.Scale);
                if (c.Tall) cell.Tall[ti++] = m; else if (c.Flower) cell.Flower[fi++] = m; else cell.Short[si++] = m;
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }
            const float size = Vegetation.GrassCell;
            if (low > high) { low = 0f; high = 0f; }
            cell.Bounds = new Bounds(new Vector3(cx * size + size * 0.5f, (low + high) * 0.5f + 0.75f, cz * size + size * 0.5f),
                new Vector3(size + 2f, high - low + 2.5f, size + 2f));
            return cell;
        }

        void Evict(int frame)
        {
            _evict.Clear();
            foreach (var pair in _cache)
                if (frame - pair.Value.LastUsed > 120) _evict.Add(pair.Key);
            foreach (var key in _evict) _cache.Remove(key);
        }
    }
}

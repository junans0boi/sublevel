using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Client-only hit effects shared by every combatant (ADR-470, #197 #198): bullet holes and blood stains as two
    // batched decal meshes (one draw each, fixed rings, faded and reused), and debris, sparks, splashes and blood as
    // three world-space particle systems fed by Emit (no objects per impact). The pictures are painted in code
    // (ImpactFxTextures), so there is no asset to license. Lives in the scene and is rebuilt after a scene change.
    public static class ImpactFx
    {
        public const int MarkCapacity = 96, BloodCapacity = 64;
        public const int DropMax = 500, ChunkMax = 300, SparkMax = 200;

        static ImpactFxRunner _runner;
        static Material _dustSource, _glowSource;

        // Counters for tests and captures.
        public static int BloodSprays { get; private set; }
        public static int BloodStains { get; private set; }
        public static int Splashes { get; private set; }
        public static int DebrisBursts { get; private set; }

        // The effect materials come from the builder (shaders cannot be looked up in a player build); any view passes them.
        public static void Use(Material dust, Material glow)
        {
            if (_dustSource == null) _dustSource = dust;
            if (_glowSource == null) _glowSource = glow;
        }

        static ImpactFxRunner Runner
        {
            get
            {
                if (_runner != null) return _runner;
                if (_dustSource == null || _glowSource == null) return null;
                _runner = new GameObject("ImpactFx").AddComponent<ImpactFxRunner>();
                _runner.Build(_dustSource, _glowSource);
                return _runner;
            }
        }

        public static int LiveMarks => _runner != null ? _runner.Marks.Live : 0;
        public static int LiveBlood => _runner != null ? _runner.Blood.Live : 0;

        // A bullet hole (#198): its own picture per surface. False when the surface keeps no mark.
        public static bool Mark(Vector3 point, Vector3 normal, BulletSurface surface, in ImpactLook look)
        {
            if (look.Mark.a <= 0f) return false;
            var r = Runner;
            if (r == null) return false;
            int cell = ImpactFxTextures.MarkCell(surface);
            float size = look.MarkSize * ImpactFxTextures.MarkScale * Random.Range(0.85f, 1.15f);
            var tint = new Color(1f, 1f, 1f, Mathf.Clamp01(look.Mark.a + 0.1f));
            r.Marks.Add(point, normal, Vector3.zero, size, cell, tint, 0.012f);
            return true;
        }

        // What flies off the surface: chips, chunks, clods, shards or sparks.
        public static void Debris(Vector3 point, Vector3 normal, Vector3 dir, BulletSurface surface)
        {
            var kind = DebrisRules.Of(surface);
            var r = Runner;
            if (r == null || kind == Game.Core.Debris.None) return;
            int n = DebrisRules.Count(kind, GameSettings.HighEffects);
            var bounce = Vector3.Reflect(dir, normal).normalized;
            for (int i = 0; i < n; i++)
            {
                var d = (normal * 0.6f + bounce * 0.4f + Random.insideUnitSphere * 0.55f).normalized;
                switch (kind)
                {
                    case Game.Core.Debris.Sparks:
                        r.Emit(r.Sparks, point, d * Random.Range(4f, 11f), Random.Range(0.02f, 0.045f), Random.Range(0.12f, 0.3f),
                            Color.Lerp(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.55f, 0.15f), Random.value));
                        break;
                    case Game.Core.Debris.WoodChips:
                        r.Emit(r.Chunks, point, d * Random.Range(2f, 5f), Random.Range(0.025f, 0.06f), Random.Range(0.5f, 0.9f),
                            Color.Lerp(new Color(0.62f, 0.46f, 0.28f), new Color(0.80f, 0.66f, 0.45f), Random.value), true);
                        break;
                    case Game.Core.Debris.ConcreteChunks:
                        r.Emit(r.Chunks, point, d * Random.Range(1.5f, 4.5f), Random.Range(0.02f, 0.05f), Random.Range(0.4f, 0.8f),
                            Color.Lerp(new Color(0.55f, 0.55f, 0.53f), new Color(0.78f, 0.77f, 0.74f), Random.value), true);
                        break;
                    case Game.Core.Debris.DirtClods:
                        r.Emit(i % 2 == 0 ? r.Chunks : r.Drops, point, (d + Vector3.up * 0.5f).normalized * Random.Range(1.5f, 4f),
                            Random.Range(0.03f, 0.07f), Random.Range(0.4f, 0.8f),
                            Color.Lerp(new Color(0.24f, 0.18f, 0.12f), new Color(0.40f, 0.31f, 0.21f), Random.value), true);
                        break;
                    case Game.Core.Debris.GlassShards:
                        r.Emit(r.Chunks, point, d * Random.Range(2f, 5f), Random.Range(0.015f, 0.04f), Random.Range(0.4f, 0.8f),
                            new Color(0.82f, 0.92f, 1f, 0.85f), true);
                        break;
                }
            }
            DebrisBursts++;
        }

        // A shot into water: a short column and a ring of spray (no hole, the bottom is not seen).
        public static void Splash(Vector3 point)
        {
            var r = Runner;
            if (r == null) return;
            int n = DebrisRules.Count(Game.Core.Debris.WaterSplash, GameSettings.HighEffects);
            for (int i = 0; i < n; i++)
            {
                bool column = i < n / 2;
                var flat = Random.insideUnitCircle;
                var v = column
                    ? new Vector3(flat.x * 0.4f, Random.Range(3.5f, 6.5f), flat.y * 0.4f)
                    : new Vector3(flat.x * 2.2f, Random.Range(1.2f, 2.5f), flat.y * 2.2f);
                r.Emit(r.Drops, point + Vector3.up * 0.03f, v, Random.Range(0.06f, column ? 0.16f : 0.1f), Random.Range(0.5f, 0.9f),
                    new Color(0.82f, 0.9f, 0.95f, 0.75f));
            }
            Splashes++;
        }

        // A bullet into a body (#197): a short spray mostly along the shot (the exit side) with a little back toward
        // the shooter, then a stain on the wall behind (Normal only) and on the floor under it.
        public static void Blood(Vector3 point, Vector3 dir, bool head, Transform victim)
        {
            var level = GameSettings.Blood;
            var r = Runner;
            if (r == null || level == BloodLevel.Off) return;
            int n = BloodRules.SprayParticles(level, head, GameSettings.HighEffects);
            for (int i = 0; i < n; i++)
            {
                bool back = i % 4 == 3;
                var d = ((back ? -dir : dir) + Random.insideUnitSphere * 0.45f + Vector3.up * 0.15f).normalized;
                r.Emit(r.Drops, point, d * Random.Range(back ? 1f : 2f, back ? 2.5f : 4.5f), Random.Range(0.025f, 0.06f),
                    Random.Range(0.3f, 0.55f), Color.Lerp(new Color(0.42f, 0.02f, 0.02f, 0.95f), new Color(0.6f, 0.04f, 0.03f, 0.95f), Random.value));
            }
            BloodSprays++;
            if (BloodRules.WallSplatter(level) && RayWorld(point + dir * 0.25f, dir, BloodRules.WallReach, victim, out var wall))
            {
                bool floor = wall.normal.y > 0.7f;
                Stain(wall.point, wall.normal, floor ? Vector3.zero : dir, floor ? ImpactFxTextures.CellSplat : ImpactFxTextures.CellSpatter,
                    Random.Range(0.45f, 0.75f) * (head ? 1.2f : 1f));
            }
            if (BloodRules.FloorSplat(level) && RayWorld(point + dir * 0.4f, Vector3.down, BloodRules.FloorReach, victim, out var under) && under.normal.y > 0.6f)
                Stain(under.point, under.normal, Vector3.zero, ImpactFxTextures.CellSplat, Random.Range(0.25f, 0.45f) * (level == BloodLevel.Low ? 0.7f : 1f));
        }

        // A drop from a hit or knocked body, or the pool it leaves where it went down.
        public static void Drip(Vector3 feet, float size, Transform body)
        {
            if (GameSettings.Blood == BloodLevel.Off) return;
            if (RayWorld(feet + Vector3.up * 0.6f, Vector3.down, 2f, body, out var hit) && hit.normal.y > 0.6f)
                Stain(hit.point, hit.normal, Vector3.zero, ImpactFxTextures.CellPool, size);
        }

        static void Stain(Vector3 point, Vector3 normal, Vector3 along, int cell, float size)
        {
            var r = Runner;
            if (r == null) return;
            r.Blood.Add(point, normal, along, size, cell, Color.white, 0.016f);
            BloodStains++;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[12];

        // The nearest solid thing along the ray that is not a combatant (bodies do not catch their own blood).
        static bool RayWorld(Vector3 from, Vector3 dir, float distance, Transform skip, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(from, dir, Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (Hits[i].distance >= nearest) continue;
                if (skip != null && c.transform.IsChildOf(skip)) continue;
                if (c.GetComponentInParent<Combatant>() != null) continue;
                nearest = Hits[i].distance;
                best = Hits[i];
            }
            return nearest < float.PositiveInfinity;
        }

        // Where a shot from `from` to `to` goes into water, if it does (WaterVolume has no collider).
        public static bool WaterOnPath(Vector3 from, Vector3 to, out Vector3 point)
        {
            point = default;
            foreach (var w in WaterVolume.All)
            {
                if (w == null || !WaterCrossing.TryCross(from.y, to.y, w.SurfaceY, out float t)) continue;
                var p = Vector3.Lerp(from, to, t);
                if (!w.Area.Contains(new Vector2(p.x, p.z))) continue;
                point = p;
                return true;
            }
            return false;
        }
    }

    // One batched mesh of decal quads over a DecalRing: adding a decal writes four vertices, fading rewrites the colours.
    public sealed class DecalBatch
    {
        readonly DecalRing _ring;
        readonly Vector3[] _verts;
        readonly Vector2[] _uvs;
        readonly Color[] _colors;
        readonly Color[] _tint;
        readonly bool[] _alive;
        readonly Mesh _mesh;
        bool _vertsDirty;

        public int Live { get; private set; }

        public DecalBatch(string name, int capacity, float life, float fade, Transform parent, Material material)
        {
            _ring = new DecalRing(capacity, life, fade);
            _verts = new Vector3[capacity * 4];
            _uvs = new Vector2[capacity * 4];
            _colors = new Color[capacity * 4];
            _tint = new Color[capacity];
            _alive = new bool[capacity];
            var tris = new int[capacity * 6];
            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.colors = _colors;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // world space and anywhere on the map
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // `along`: the picture's long side follows it on the surface (a spatter streak); zero = a random turn.
        public void Add(Vector3 point, Vector3 normal, Vector3 along, float size, int cell, Color tint, float lift)
        {
            int s = _ring.Add(Time.time);
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            var x = Vector3.ProjectOnPlane(along, normal);
            if (x.sqrMagnitude < 1e-4f) x = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);
            x = x.normalized * (size * 0.5f);
            var y = Vector3.Cross(normal, x).normalized * (size * 0.5f);
            var c = point + normal * lift;
            int v = s * 4;
            _verts[v] = c - x - y; _verts[v + 1] = c - x + y; _verts[v + 2] = c + x + y; _verts[v + 3] = c + x - y;
            var rect = ImpactFxTextures.CellRect(cell);
            _uvs[v] = new Vector2(rect.xMin, rect.yMin); _uvs[v + 1] = new Vector2(rect.xMin, rect.yMax);
            _uvs[v + 2] = new Vector2(rect.xMax, rect.yMax); _uvs[v + 3] = new Vector2(rect.xMax, rect.yMin);
            _tint[s] = tint;
            for (int k = 0; k < 4; k++) _colors[v + k] = tint;
            _alive[s] = true;
            _vertsDirty = true;
        }

        public void Tick(float now)
        {
            bool colorsDirty = false;
            int live = 0;
            for (int s = 0; s < _alive.Length; s++)
            {
                if (!_alive[s]) continue;
                float a = _ring.Alpha(s, now);
                int v = s * 4;
                if (a <= 0f)
                {
                    _alive[s] = false;
                    _verts[v + 1] = _verts[v + 2] = _verts[v + 3] = _verts[v]; // collapsed: nothing drawn
                    _vertsDirty = true;
                    continue;
                }
                live++;
                if (a >= 1f) continue;
                var c = _tint[s];
                c.a *= a;
                for (int k = 0; k < 4; k++) _colors[v + k] = c;
                colorsDirty = true;
            }
            Live = live;
            if (_vertsDirty)
            {
                _mesh.vertices = _verts;
                _mesh.uv = _uvs;
                colorsDirty = true;
                _vertsDirty = false;
            }
            if (colorsDirty) _mesh.colors = _colors;
        }

        public void Destroy() { if (_mesh != null) Object.Destroy(_mesh); }
    }

    // Owns the decal batches and the particle systems, and ticks the fades.
    public sealed class ImpactFxRunner : MonoBehaviour
    {
        public DecalBatch Marks, Blood;
        public ParticleSystem Drops, Chunks, Sparks;

        public void Build(Material dust, Material glow)
        {
            Marks = new DecalBatch("BulletMarks", ImpactFx.MarkCapacity, 45f, 6f, transform, ImpactFxTextures.DecalMaterial(dust));
            Blood = new DecalBatch("BloodStains", ImpactFx.BloodCapacity, 40f, 8f, transform, ImpactFxTextures.DecalMaterial(dust));
            Drops = NewSystem("Drops", ImpactFxTextures.DotMaterial(dust), ImpactFx.DropMax, 1f, false);
            Chunks = NewSystem("Chunks", ImpactFxTextures.ChunkMaterial(dust), ImpactFx.ChunkMax, 1f, false);
            Sparks = NewSystem("Sparks", ImpactFxTextures.SparkMaterial(glow), ImpactFx.SparkMax, 0.7f, true);
        }

        ParticleSystem NewSystem(string name, Material material, int max, float gravity, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            // Fade out over the last third of each particle's life.
            VehicleDamageVisual.SetColorOverLife(ps, new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (0.65f, 1f), (1f, 0f) });
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (stretch)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.035f;
                renderer.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }

        public void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, float life, Color color, bool spin = false)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
                applyShapeToPosition = false,
            };
            if (spin)
            {
                p.rotation = Random.Range(0f, 360f);
                p.angularVelocity = Random.Range(-720f, 720f);
            }
            ps.Emit(p, 1);
        }

        void LateUpdate()
        {
            float now = Time.time;
            Marks.Tick(now);
            Blood.Tick(now);
        }

        void OnDestroy()
        {
            Marks?.Destroy();
            Blood?.Destroy();
        }
    }
}

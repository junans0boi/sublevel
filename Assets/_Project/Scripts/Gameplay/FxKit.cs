using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    // Small helpers for the environment particles (#205 #206 #207): soft sprites drawn into textures when first needed
    // (no art files) and unlit particle materials cloned from the old rain-streak material (Weather/RainStreak, kept as the template since rain went, ADR-497), so no shader has to be referenced.
    public static class FxKit
    {
        public enum Sprite { Dot, Ring, Leaf, Print, Wing }

        static Material _template;
        static readonly Texture2D[] Textures = new Texture2D[5];
        static readonly Material[] Opaque = new Material[5], Glow = new Material[5];

        public static Texture2D Texture(Sprite sprite)
        {
            var t = Textures[(int)sprite];
            if (t != null) return t;
            const int n = 64;
            t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Fx" + sprite, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    px[y * n + x] = new Color(1f, 1f, 1f, Alpha(sprite, u, v));
                }
            t.SetPixels32(px);
            t.Apply(true, true);
            return Textures[(int)sprite] = t;
        }

        static float Alpha(Sprite sprite, float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            switch (sprite)
            {
                case Sprite.Ring: // a thin bright ring, soft on both sides
                    return Mathf.Clamp01(1f - Mathf.Abs(r - 0.72f) / 0.16f);
                case Sprite.Leaf: // a pointed oval
                    return Mathf.Clamp01((1f - (u * u / 0.22f + v * v)) * 6f);
                case Sprite.Print: // a boot: a broad sole and a narrower heel, tall in v
                {
                    float sole = 1f - (Sq(u / 0.62f) + Sq((v - 0.38f) / 0.58f));
                    float heel = 1f - (Sq(u / 0.5f) + Sq((v + 0.62f) / 0.3f));
                    return Mathf.Clamp01(Mathf.Max(sole, heel) * 5f);
                }
                case Sprite.Wing: // a gull's V with a body: wings sweep up and out, the flap squeezes the width
                {
                    float wing = Mathf.Clamp01((1f - Mathf.Abs(v - (0.7f * Mathf.Abs(u) - 0.3f)) / 0.16f) * 2f) * (Mathf.Abs(u) < 0.95f ? 1f : 0f);
                    float body = Mathf.Clamp01((1f - (Sq(u / 0.12f) + Sq((v + 0.1f) / 0.38f))) * 3f);
                    return Mathf.Max(wing, body);
                }
                default: // Dot
                    return Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r) * 1.6f;
            }
        }

        static float Sq(float x) => x * x;

        // An unlit particle material: alpha-blended, or additive (`glow`) for things that shine in the dark.
        public static Material Material(Sprite sprite, bool glow)
        {
            var cache = glow ? Glow : Opaque;
            if (cache[(int)sprite] != null) return cache[(int)sprite];
            if (_template == null) _template = Resources.Load<Material>("Weather/RainStreak");
            var m = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.name = "Fx" + sprite + (glow ? "Glow" : "");
            m.SetTexture("_BaseMap", Texture(sprite));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", glow ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            return cache[(int)sprite] = m;
        }

        // The object an effect keeps all its parts under (a test or an option can switch the whole effect off with it).
        public static Transform Root(Component host, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(host.transform, false);
            return go.transform;
        }

        // Particle system plumbing every effect shares: world space, looping, no auto-play, drawn without shadows.
        public static ParticleSystem Particles(string name, Transform parent, Material material, int max, ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}

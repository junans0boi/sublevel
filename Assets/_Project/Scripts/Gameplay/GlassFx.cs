using System.Collections.Generic;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // What a window pane breaking looks and sounds like on this peer (ADR-065 (4)): a burst of falling shards in the
    // pane's own glass material and a loud crash. The crash is made in code from shaped noise and short high tones
    // (no sound asset). Hooks itself to GlassPane.Shattered when the game starts.
    public class GlassFx : MonoBehaviour
    {
        const int Shards = 14;
        const float ShardSeconds = 1.4f;
        const float Gravity = 9.81f;
        const float CrashVolume = 1f;
        const float CrashMaxDistance = 60f;
        const int SampleRate = 44100;
        const float CrashSeconds = 0.9f;

        static GlassFx _instance;
        static AudioClip _crash;

        // Counters so tests can tell an effect was shown without looking or listening.
        public static int ShattersShown { get; private set; }
        public static int CrashesPlayed { get; private set; }

        sealed class Shard
        {
            public Transform Transform;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Until;
        }

        readonly List<Shard> _live = new List<Shard>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            ShattersShown = 0;
            CrashesPlayed = 0;
            _instance = null;
            GlassPane.Shattered -= OnShattered;
            GlassPane.Shattered += OnShattered;
        }

        static void OnShattered(GlassPane pane)
        {
            if (pane == null) return;
            if (_instance == null) _instance = new GameObject("GlassFx").AddComponent<GlassFx>(); // goes with the scene
            _instance.Show(pane);
        }

        void Show(GlassPane pane)
        {
            ShattersShown++;
            var bounds = pane.Bounds;
            var material = pane.Material;
            var normal = pane.transform.forward;
            var rng = new System.Random(pane.Id * 7919 + ShattersShown);
            float R() => (float)rng.NextDouble();
            for (int i = 0; i < Shards; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "GlassShard";
                Destroy(go.GetComponent<Collider>());
                if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
                go.transform.SetParent(transform, false);
                var at = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, R()), Mathf.Lerp(bounds.min.y, bounds.max.y, R()), Mathf.Lerp(bounds.min.z, bounds.max.z, R()));
                go.transform.SetPositionAndRotation(at, Random.rotationUniform);
                go.transform.localScale = new Vector3(0.05f + 0.12f * R(), 0.05f + 0.15f * R(), 0.008f);
                float side = R() < 0.5f ? -1f : 1f;
                _live.Add(new Shard
                {
                    Transform = go.transform,
                    Velocity = normal * side * (0.5f + 1.5f * R()) + new Vector3(R() - 0.5f, R() * 0.8f, R() - 0.5f),
                    Spin = new Vector3(R(), R(), R()) * 720f,
                    Until = Time.time + ShardSeconds * (0.6f + 0.4f * R()),
                });
            }
            PlayCrash(bounds.center);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var s = _live[i];
                if (s.Transform == null || Time.time >= s.Until)
                {
                    if (s.Transform != null) Destroy(s.Transform.gameObject);
                    _live.RemoveAt(i);
                    continue;
                }
                s.Velocity += Vector3.down * Gravity * dt;
                s.Transform.position += s.Velocity * dt;
                s.Transform.Rotate(s.Spin * dt, Space.Self);
            }
        }

        void PlayCrash(Vector3 at)
        {
            var go = new GameObject("GlassCrash");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = CrashMaxDistance;
            source.playOnAwake = false;
            source.PlayOneShot(CrashClip(), CrashVolume * GameSettings.EffectsVolume);
            CrashesPlayed++;
            Destroy(go, CrashSeconds + 0.2f);
        }

        // A sharp crash: a burst of noise that dies away fast, with ringing high tones of the pieces on top.
        static AudioClip CrashClip()
        {
            if (_crash != null) return _crash;
            int count = (int)(SampleRate * CrashSeconds);
            var data = new float[count];
            var rng = new System.Random(65);
            float[] tones = { 3150f, 4420f, 5230f, 6870f, 7710f };
            float[] toneStart = { 0f, 0.04f, 0.09f, 0.16f, 0.24f };
            float low = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.35f;           // a little body under the hiss
                float hiss = noise - low * 0.6f;
                float burst = Mathf.Exp(-t * 9f) * (t < 0.004f ? t / 0.004f : 1f);
                float v = hiss * burst * 0.8f;
                for (int k = 0; k < tones.Length; k++)
                {
                    float dt = t - toneStart[k];
                    if (dt < 0f) continue;
                    v += Mathf.Sin(2f * Mathf.PI * tones[k] * dt) * Mathf.Exp(-dt * 14f) * 0.18f;
                }
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            _crash = AudioClip.Create("GlassCrash", count, 1, SampleRate, false);
            _crash.SetData(data, 0);
            return _crash;
        }
    }
}

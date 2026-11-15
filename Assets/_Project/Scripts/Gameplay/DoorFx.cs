using System.Collections.Generic;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // What a door taking a hit and breaking looks and sounds like on this peer (ADR-147): a dull splintering knock per
    // hit, and on the break the panel falling apart into planks that tumble for a moment and vanish. The sounds are made
    // in code from shaped noise (no sound asset, nothing for the ledger); the planks are cubes in the door's own material.
    public class DoorFx : MonoBehaviour
    {
        const float DebrisSeconds = 3.5f;
        const float Gravity = 9.81f;
        const int SampleRate = 44100;

        static DoorFx _instance;
        static AudioClip _knock, _crash;

        // Counters so tests can tell an effect was shown without looking or listening.
        public static int HitsHeard { get; private set; }
        public static int BreaksShown { get; private set; }
        public static int LivePieces => _instance != null ? _instance._live.Count : 0;

        sealed class Plank
        {
            public Transform Transform;
            public Vector3 Velocity, Spin;
            public float Until;
            public bool Down;
        }

        readonly List<Plank> _live = new List<Plank>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] // after Door resets its events
        static void Hook()
        {
            HitsHeard = 0;
            BreaksShown = 0;
            _instance = null;
            _knock = _crash = null;
            Door.Hit -= OnHit;
            Door.Hit += OnHit;
            Door.Broke -= OnBroke;
            Door.Broke += OnBroke;
        }

        static DoorFx Get() => _instance != null ? _instance : _instance = new GameObject("DoorFx").AddComponent<DoorFx>();

        static void OnHit(Door door)
        {
            if (door == null || door.Audio == null) return;
            HitsHeard++;
            door.Audio.PlayOneShot(Knock(), 0.8f * GameSettings.EffectsVolume);
        }

        static void OnBroke(Door door) { if (door != null) Get().Show(door); }

        void Show(Door door)
        {
            BreaksShown++;
            var hinge = door.Hinge;
            float w = door.Width - 0.04f, h = door.Height - 0.03f;
            var material = door.PanelLook;
            var rng = new System.Random(door.Index * 104729 + BreaksShown);
            float R() => (float)rng.NextDouble();
            const int strips = 5;
            for (int i = 0; i < strips; i++)
            {
                // Vertical boards of the panel, each cut in two at a random height (the lower half stays nearer the floor).
                float x0 = w * i / strips, bw = w / strips;
                float cut = h * (0.35f + 0.3f * R());
                for (int half = 0; half < 2; half++)
                {
                    float y0 = half == 0 ? 0f : cut, bh = half == 0 ? cut : h - cut;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "DoorPlank";
                    Destroy(go.GetComponent<Collider>());
                    if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(hinge.TransformPoint(new Vector3(x0 + bw / 2f, y0 + bh / 2f, 0f)), hinge.rotation);
                    go.transform.localScale = new Vector3(bw * 0.95f, bh * 0.95f, 0.06f);
                    // Thrown away from the side the shot came from is not known here: they fall out both ways.
                    float side = R() < 0.5f ? -1f : 1f;
                    _live.Add(new Plank
                    {
                        Transform = go.transform,
                        Velocity = door.Forward * side * (0.6f + 1.8f * R()) + new Vector3(R() - 0.5f, 0.6f + 1.2f * R(), R() - 0.5f),
                        Spin = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 540f,
                        Until = Time.time + DebrisSeconds * (0.7f + 0.3f * R()),
                    });
                }
            }
            var at = door.transform.position + Vector3.up * door.Height * 0.5f;
            var src = new GameObject("DoorCrash");
            src.transform.SetParent(transform, false);
            src.transform.position = at;
            var source = src.AddComponent<AudioSource>();
            SpatialAudio.Apply(source, SpatialAudio.Category.Impact);
            source.playOnAwake = false;
            source.PlayOneShot(Crash(), GameSettings.EffectsVolume);
            Destroy(src, 1.5f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                if (p.Transform == null || Time.time >= p.Until)
                {
                    if (p.Transform != null) Destroy(p.Transform.gameObject);
                    _live.RemoveAt(i);
                    continue;
                }
                if (p.Down) continue; // lying on the floor until its time is up
                p.Velocity += Vector3.down * Gravity * dt;
                var from = p.Transform.position;
                var next = from + p.Velocity * dt;
                // Lands on whatever is under it (a flat cast; the planks have no colliders of their own).
                if (p.Velocity.y < 0f && Physics.Raycast(from, Vector3.down, out var hit, from.y - next.y + 0.03f, ~0, QueryTriggerInteraction.Ignore))
                {
                    next.y = hit.point.y + 0.03f;
                    p.Down = true;
                }
                p.Transform.position = next;
                p.Transform.Rotate(p.Spin * dt, Space.Self);
            }
        }

        // A dull knock: low thump plus a short burst of wood crackle.
        static AudioClip Knock()
        {
            if (_knock != null) return _knock;
            int count = (int)(SampleRate * 0.22f);
            var data = new float[count];
            var rng = new System.Random(147);
            float low = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.12f;
                float thump = Mathf.Sin(2f * Mathf.PI * (95f - 40f * t) * t) * Mathf.Exp(-t * 28f);
                float crackle = (noise - low) * Mathf.Exp(-t * 45f) * (rng.NextDouble() < 0.5 ? 1f : 0.3f);
                data[i] = Mathf.Clamp(thump * 0.8f + crackle * 0.4f, -1f, 1f);
            }
            _knock = AudioClip.Create("DoorKnock", count, 1, SampleRate, false);
            _knock.SetData(data, 0);
            return _knock;
        }

        // The break: a heavy crack, a splintering rattle and a few low knocks of boards landing.
        static AudioClip Crash()
        {
            if (_crash != null) return _crash;
            int count = (int)(SampleRate * 1.1f);
            var data = new float[count];
            var rng = new System.Random(1470);
            float low = 0f;
            float[] lands = { 0.28f, 0.41f, 0.57f, 0.70f };
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.2f;
                float crack = (noise - low * 0.5f) * Mathf.Exp(-t * 14f) * (t < 0.003f ? t / 0.003f : 1f);
                float boom = Mathf.Sin(2f * Mathf.PI * (70f - 30f * Mathf.Min(t, 0.5f)) * t) * Mathf.Exp(-t * 9f);
                float v = crack * 0.7f + boom * 0.6f;
                foreach (float l in lands)
                {
                    float dt = t - l;
                    if (dt > 0f) v += Mathf.Sin(2f * Mathf.PI * 110f * dt) * Mathf.Exp(-dt * 30f) * 0.35f + low * Mathf.Exp(-dt * 40f) * 0.5f;
                }
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            _crash = AudioClip.Create("DoorCrash", count, 1, SampleRate, false);
            _crash.SetData(data, 0);
            return _crash;
        }
    }
}

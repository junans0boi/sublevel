using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The last second of one combatant's positions on the server (#162, ADR-141). Added by Combatant.OnStartServer.
    public class PositionRecorder : MonoBehaviour
    {
        // 240 samples: a second at 240 fps, and more than the 0.3 s rewind cap at any frame rate up to 800.
        public readonly PositionHistory History = new PositionHistory(240);

        void LateUpdate()
        {
            var p = transform.position;
            History.Record(Time.timeAsDouble, p.x, p.y, p.z);
        }
    }

    // Rewinds the other combatants to where a shooter saw them, for the length of one ray test, then puts them back.
    // Doors, glass and vehicles are not rewound (kept simple, ADR-141). Server only.
    public static class LagCompensation
    {
        // Bodies farther from the ray than this (plus what they could have moved) cannot matter.
        const float NearMeters = 3f;
        const float FastestMetersPerSecond = 25f;
        const float MovedEpsilon = 0.01f;

        static readonly List<(Combatant who, Vector3 now, Vector3 then)> Moved = new List<(Combatant, Vector3, Vector3)>();

        // How many ray tests ran with bodies moved, and how many bodies were moved in all (tests, network HUD).
        public static int Rewinds { get; private set; }
        public static int BodiesMoved { get; private set; }

        // Measurement hook (#401, tests only): (shooter, server time now, server time rewound to) for every Rewind call.
        public static Action<Combatant, double, double> Probe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Moved.Clear(); Rewinds = BodiesMoved = 0; Probe = null; }

        public struct Scope : IDisposable
        {
            // Where `c` was put for this test, if it was moved.
            public bool TryRewound(Combatant c, out Vector3 position)
            {
                foreach (var m in Moved) if (m.who == c) { position = m.then; return true; }
                position = default;
                return false;
            }

            public void Dispose()
            {
                if (Moved.Count == 0) return;
                foreach (var m in Moved) if (m.who != null) m.who.transform.position = m.now;
                Moved.Clear();
                Physics.SyncTransforms();
            }
        }

        // Puts every other combatant near the segment from `from` along `dir` for `length` where it stood at server time
        // `at` (Time.timeAsDouble). No-op for at <= 0 or not in the past. Use in a `using` around the ray test only: nothing else may run while bodies are away.
        public static Scope Rewind(Combatant shooter, Vector3 from, Vector3 dir, float length, double at)
        {
            Moved.Clear();
            Probe?.Invoke(shooter, Time.timeAsDouble, at);
            double seconds = Time.timeAsDouble - at;
            if (at <= 0.0 || seconds <= 1e-4) return default;
            float reach = NearMeters + FastestMetersPerSecond * (float)seconds;
            var to = from + dir * length;
            foreach (var c in Combatant.All)
            {
                if (c == null || c == shooter || !c.IsServerInitialized || c.Dead.Value) continue;
                var rec = c.GetComponent<PositionRecorder>();
                if (rec == null || !rec.History.TryAt(at, out float x, out float y, out float z)) continue;
                var now = c.transform.position;
                if (DistanceToSegment(now, from, to) > reach) continue;
                var then = new Vector3(x, y, z);
                if ((then - now).sqrMagnitude < MovedEpsilon * MovedEpsilon) continue;
                Moved.Add((c, now, then));
                c.transform.position = then;
            }
            if (Moved.Count == 0) return default;
            Rewinds++;
            BodiesMoved += Moved.Count;
            Physics.SyncTransforms();
            return new Scope();
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }
    }
}

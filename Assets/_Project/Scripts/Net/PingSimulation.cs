using System;
using FishNet.Managing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Puts the `--simulate-ping` delay (#399, ADR-300) on a CLIENT's outgoing packets through Fish-Net's own latency simulator
    // (it only delays what its side sends, so the round trip grows by the delay). Without the flag nothing is touched.
    // REMOVE BEFORE RELEASE (docs/RISKS_OPEN_QUESTIONS.md).
    public sealed class PingSimulation : MonoBehaviour
    {
        static SimulatedPing? _parsed;
        public static SimulatedPing Options => _parsed ??= Read();

        // Tests: act as if these were on the command line (null = read the real command line again).
        public static void Use(SimulatedPing? options) => _parsed = options;

        NetworkManager _nm;

        static SimulatedPing Read()
        {
            var o = SimulatedPing.Parse(Environment.GetCommandLineArgs());
            if (o.Error != null) Debug.LogError("[simulate-ping] " + o.Error + " - ignored");
            else if (o.Active) Debug.Log("[simulate-ping] +" + o.PingMs + " ms, jitter +-" + o.JitterMs + " ms");
            return o;
        }

        // Call where a client is about to connect. Safe to call again on the same manager.
        public static void Attach(NetworkManager nm)
        {
            if (nm == null || !Options.Active) return;
            var sim = nm.GetComponent<PingSimulation>() ?? nm.gameObject.AddComponent<PingSimulation>();
            sim._nm = nm;
            var latency = nm.TransportManager.LatencySimulator;
            latency.SetLatency(Options.PingMs);
            latency.SetEnabled(true);
        }

        void Update()
        {
            if (_nm == null || Options.JitterMs == 0) return;
            _nm.TransportManager.LatencySimulator.SetLatency(Options.DelayMs(UnityEngine.Random.value));
        }
    }
}

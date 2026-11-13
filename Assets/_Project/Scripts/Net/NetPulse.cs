using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The server's heartbeat (#171, ADR-128): a small numbered message on the unreliable channel, ten times a second.
    // A client counts the numbers that never arrived = packet loss, and reads the server's tick from it.
    public struct NetPulse : IBroadcast { public uint Seq; public uint Tick; }

    public sealed class NetPulseServer : MonoBehaviour
    {
        NetworkManager _nm;
        uint _seq;
        float _next;

        public static void Attach(NetworkManager nm)
        {
            if (nm == null) return;
            var pulse = nm.GetComponent<NetPulseServer>() ?? nm.gameObject.AddComponent<NetPulseServer>();
            pulse._nm = nm;
        }

        void Update()
        {
            if (_nm == null || !_nm.IsServerStarted || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + NetStatusRules.PulseInterval;
            _nm.ServerManager.Broadcast(new NetPulse { Seq = ++_seq, Tick = _nm.TimeManager.Tick }, true, Channel.Unreliable);
        }
    }

    // Client side: ping (the transport's round trip), packet loss and the server tick of this connection.
    public sealed class NetStatusClient : MonoBehaviour
    {
        NetworkManager _nm;
        bool _registered;
        readonly PulseLoss _loss = new PulseLoss();

        public int PingMs => _nm != null && _nm.IsClientStarted ? (int)_nm.TimeManager.RoundTripTime : 0;
        public float Loss => _loss.Loss;
        // Pulses have been coming for long enough to read a loss from.
        public bool HasPulses => _loss.HasData;
        public uint ServerTick { get; private set; }
        public int TickRate => _nm != null ? (int)_nm.TimeManager.TickRate : 0;
        public bool Connected => _nm != null && _nm.IsClientStarted;

        public static NetStatusClient Attach(NetworkManager nm)
        {
            if (nm == null) return null;
            var client = nm.GetComponent<NetStatusClient>() ?? nm.gameObject.AddComponent<NetStatusClient>();
            client._nm = nm;
            return client;
        }

        void Update()
        {
            if (_nm == null) return;
            bool started = _nm.IsClientStarted;
            // The handler goes with the connection: set again after a reconnect.
            if (started && !_registered) { _nm.ClientManager.RegisterBroadcast<NetPulse>(OnPulse); _registered = true; _loss.Clear(); }
            else if (!started && _registered) _registered = false;
        }

        void OnPulse(NetPulse pulse, Channel channel)
        {
            _loss.Add(pulse.Seq, Time.unscaledTime);
            ServerTick = pulse.Tick;
        }

        void OnDestroy()
        {
            if (_nm != null && _registered) _nm.ClientManager.UnregisterBroadcast<NetPulse>(OnPulse);
        }
    }
}

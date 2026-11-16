using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Managing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // One key elevator's state for the panel (WP 7): sent when the phase changes and about once a second while in use.
    public struct KeyStationStatus : IBroadcast
    {
        public int Station;
        public byte Phase;         // KeyPhase
        public float Left;         // seconds left in the phase
        public int Holder;         // ObjectId of the key holder, -1 none
        public int Riders;
        public float TeamCooldown; // seconds left of the holder's team cooldown (0 none)
    }

    // The server side of the key elevators (WP 7, ADR-170/238): wraps KeyElevatorSystem around ElevatorRides' stations.
    // Players ask through KeyHolder's RPCs; the system decides; rides go through ElevatorRides.ServerKeyRide.
    public class KeyElevatorNet : MonoBehaviour
    {
        public static KeyElevatorNet Instance { get; private set; }
        // Client (the host too): the latest state of a station, for the panel.
        public static event Action<KeyStationStatus> ClientStatus;
        public static readonly Dictionary<int, KeyStationStatus> LastStatus = new Dictionary<int, KeyStationStatus>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; ClientStatus = null; LastStatus.Clear(); }

        NetworkManager _network;
        bool _bound;
        KeyElevatorSystem _system;
        int _group;
        float _nextSend;
        readonly List<KeyStationEvent> _events = new List<KeyStationEvent>();
        readonly List<int> _regions = new List<int>();

        bool ServerActive => _network != null && _network.IsServerStarted;
        public KeyElevatorSystem System => _system;

        void Awake() { Instance = this; }
        void Start() { TryBind(); }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_bound && _network != null) _network.ClientManager.UnregisterBroadcast<KeyStationStatus>(OnStatus);
        }

        void TryBind()
        {
            if (_bound) return;
            var all = NetworkManager.Instances;
            if (all.Count == 0) return;
            _network = all[0];
            _bound = true;
            _network.ClientManager.RegisterBroadcast<KeyStationStatus>(OnStatus);
        }

        void OnStatus(KeyStationStatus s, FishNet.Transporting.Channel channel)
        {
            LastStatus[s.Station] = s;
            ClientStatus?.Invoke(s);
        }

        // Built on first use, when the stations exist.
        bool EnsureSystem()
        {
            if (_system != null) return true;
            var rides = ElevatorRides.Instance;
            if (rides == null || rides.StationCount == 0) return false;
            _regions.Clear();
            for (int i = 0; i < rides.StationCount; i++) _regions.Add(rides.RegionOfStation(i));
            _system = new KeyElevatorSystem(_regions, ElevatorCycle.RideSeconds(DeploymentRules.Current), PollutionDirector.Instance != null ? PollutionDirector.Instance.Rules : null);
            return true;
        }

        static int TeamKey(Combatant c) => c.HasTeam ? c.TeamId.Value : -(c.ObjectId + 1); // a solo player is a team of one

        static Combatant Find(int objectId)
        {
            foreach (var c in Combatant.All) if (c != null && c.ObjectId == objectId) return c;
            return null;
        }

        // ---- Server: player input ----

        public bool ServerInsert(Combatant c, int station)
        {
            var pollution = PollutionDirector.Instance;
            var rides = ElevatorRides.Instance;
            if (!ServerActive || c == null || !c.Standing || pollution == null || !pollution.Ready || rides == null || !EnsureSystem()) return false;
            if (station < 0 || station >= _system.Count || rides.StationAt(c.transform.position) != station) return false;
            var holder = c.GetComponent<KeyHolder>();
            if (holder == null || holder.Keys.Value <= 0) return false;
            var r = _system.Insert(station, pollution.Now, c.ObjectId, TeamKey(c), true, pollution.States);
            if (r != KeyResult.Ok) return false;
            holder.ServerConsumeKey();
            _system.Board(station, c.ObjectId);
            SendStatus(station);
            return true;
        }

        public bool ServerClose(Combatant c)
        {
            var pollution = PollutionDirector.Instance;
            if (!ServerActive || c == null || pollution == null || _system == null) return false;
            for (int i = 0; i < _system.Count; i++)
                if (_system.Station(i).HolderId == c.ObjectId && _system.CloseDoors(i, pollution.Now, c.ObjectId) == KeyResult.Ok) { SendStatus(i); return true; }
            return false;
        }

        public bool ServerSelect(Combatant c, int region)
        {
            var pollution = PollutionDirector.Instance;
            var rides = ElevatorRides.Instance;
            if (!ServerActive || c == null || pollution == null || rides == null || _system == null) return false;
            for (int i = 0; i < _system.Count; i++)
            {
                var st = _system.Station(i);
                if (st.HolderId != c.ObjectId || st.Phase != KeyPhase.Selecting) continue;
                var riders = st.Riders.ToArray();
                if (_system.Select(i, pollution.Now, c.ObjectId, region, pollution.States) != KeyResult.Ok) return false;
                int group = ++_group;
                foreach (int id in riders) rides.ServerKeyRide(Find(id), i, region, group);
                SendStatus(i);
                return true;
            }
            return false;
        }

        // ---- Server: time ----

        void Update()
        {
            if (_bound == false) TryBind();
            var pollution = PollutionDirector.Instance;
            if (!ServerActive || _system == null || pollution == null || !pollution.Ready) return;
            double now = pollution.Now;
            var rides = ElevatorRides.Instance;
            for (int i = 0; i < _system.Count; i++)
            {
                var st = _system.Station(i);
                if (st.Phase != KeyPhase.DoorOpen) continue;
                // Whoever stands in the car while the doors are open rides (4 at most); whoever steps out is out.
                foreach (var c in Combatant.All)
                {
                    if (c == null || !c.IsServerInitialized) continue;
                    bool inside = c.Standing && rides.StationAt(c.transform.position) == i;
                    if (inside) _system.Board(i, c.ObjectId);
                    else if (st.IsAboard(c.ObjectId) && c.ObjectId != st.HolderId) _system.Alight(i, c.ObjectId);
                }
            }
            _system.Tick(now, pollution.States, _events);
            foreach (var e in _events)
            {
                if (e.Event == KeyEvent.Voided && e.Refund)
                {
                    var c = Find(e.Holder);
                    var holder = c != null ? c.GetComponent<KeyHolder>() : null;
                    if (holder != null) holder.ServerAdd(1);
                }
                SendStatus(e.Station);
            }
            if (Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 1f;
                for (int i = 0; i < _system.Count; i++) if (_system.Station(i).Phase != KeyPhase.Idle) SendStatus(i);
            }
        }

        void SendStatus(int station)
        {
            var pollution = PollutionDirector.Instance;
            var st = _system.Station(station);
            double now = pollution != null ? pollution.Now : 0.0;
            var s = new KeyStationStatus
            {
                Station = station, Phase = (byte)st.Phase, Left = st.PhaseLeft(now), Holder = st.HolderId, Riders = st.Riders.Count,
                TeamCooldown = st.Phase == KeyPhase.Idle ? 0f : _system.Cooldowns.Left(st.HolderTeam, now),
            };
            LastStatus[station] = s;
            if (ServerActive) _network.ServerManager.Broadcast(s);
        }
    }
}

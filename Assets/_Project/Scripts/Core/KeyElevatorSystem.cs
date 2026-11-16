using System;
using System.Collections.Generic;

namespace Game.Core
{
    public struct KeyStationEvent
    {
        public int Station;
        public KeyEvent Event;
        public int Holder;          // who inserted
        public bool Refund;         // the key goes back to Holder (void use)
        public int From, Arrival;   // RideEnded: region the car left and the region it comes out in
        public int[] Riders;        // RideEnded: who rides
    }

    // All key elevators of a match (WP 7): the stations, the shared team cooldown and the region state they read.
    // The server calls Insert/Board/CloseDoors/Select on player input and Tick every frame.
    public sealed class KeyElevatorSystem
    {
        readonly int[] _region;
        readonly KeyElevatorStation[] _stations;
        public readonly KeyCooldowns Cooldowns = new KeyCooldowns();
        readonly PollutionRules _rules;
        readonly float _rideSeconds;

        public int Count => _stations.Length;
        public KeyElevatorStation Station(int i) => _stations[i];
        public int RegionOf(int station) => _region[station];

        // `stationRegions[i]`: the region of station i (from the map's station list).
        public KeyElevatorSystem(IReadOnlyList<int> stationRegions, float rideSeconds, PollutionRules rules = null)
        {
            _rules = rules ?? PollutionRules.Current;
            _rideSeconds = rideSeconds;
            _region = new int[stationRegions.Count];
            _stations = new KeyElevatorStation[stationRegions.Count];
            for (int i = 0; i < _region.Length; i++) { _region[i] = stationRegions[i]; _stations[i] = new KeyElevatorStation(); }
        }

        PollutionState StateOfStation(int s, IReadOnlyList<PollutionState> states) =>
            _region[s] >= 0 && _region[s] < states.Count ? states[_region[s]] : PollutionState.Safe;

        public KeyResult Insert(int station, double now, int holder, int team, bool hasKey, IReadOnlyList<PollutionState> states) =>
            _stations[station].Insert(now, holder, team, hasKey, Cooldowns, StateOfStation(station, states), _rules);
        public KeyResult Board(int station, int id) => _stations[station].Board(id, _rules);
        public KeyResult Alight(int station, int id) => _stations[station].Alight(id);
        public KeyResult CloseDoors(int station, double now, int id) => _stations[station].CloseDoors(now, id, _rules);
        public KeyResult Select(int station, double now, int id, int region, IReadOnlyList<PollutionState> states) =>
            _stations[station].Select(now, id, region, _region[station], states, _rideSeconds, Cooldowns, _rules);

        // Timeouts for every station; `events` gets what happened (the list is cleared first).
        public void Tick(double now, IReadOnlyList<PollutionState> states, List<KeyStationEvent> events)
        {
            events.Clear();
            for (int i = 0; i < _stations.Length; i++)
            {
                var st = _stations[i];
                int holder = st.HolderId;
                // Read before Tick: a ride end resets the station.
                int dest = st.Destination;
                var riders = st.Phase == KeyPhase.Riding ? st.Riders.ToArray() : null;
                var e = st.Tick(now, _rules);
                if (e == KeyEvent.None) continue;
                var ev = new KeyStationEvent { Station = i, Event = e, Holder = holder };
                if (e == KeyEvent.Voided) ev.Refund = st.KeyRefunded;
                else if (e == KeyEvent.RideEnded)
                {
                    ev.From = _region[i];
                    ev.Arrival = KeyElevatorTravel.ArrivalRegion(_region[i], dest, states);
                    ev.Riders = riders;
                }
                events.Add(ev);
            }
        }
    }
}

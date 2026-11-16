using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // What ZoneDirector used to tell the screens, the supply director, the elevators and the recorder, now answered
    // from the pollution (T10): the same names, so consumers only change the type they ask. A contaminated region
    // reads as ZoneState.Closed; Lockdown and Bombardment no longer exist for it.
    public partial class PollutionDirector
    {
        // A dedicated server's `--region`: the region id (or index) the plan must end in. null = random.
        public static string FinalRegionOverride;

        // The map's regions (names, centres), set by Game.Gameplay from WorldInfo. Kept across sessions.
        public static Func<ZoneGeometry> GeometrySource;
        // Raised once per scene when the director exists.
        public static event Action<PollutionDirector> Started;
        public static event Action<int, ZoneState> ServerRegionStateChanged;
        public static event Action<int, ZoneState> ClientRegionStateChanged;
        public static event Action<int> ServerFinalRevealed;
        public static event Action<int> ClientFinalRevealed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCompatStatics()
        {
            GeometrySource = null; Started = null; FinalRegionOverride = null;
            ServerRegionStateChanged = null; ClientRegionStateChanged = null; ServerFinalRevealed = null; ClientFinalRevealed = null;
        }

        ZoneGeometry _geo;
        ZoneState[] _seen = new ZoneState[0];
        bool _finalSeen;

        public ZoneGeometry Geometry => EnsureGeometry() ? _geo : null;
        public bool ServerReady => _serverRunning;
        public int RegionCount => _match != null ? _match.RegionCount : EnsureGeometry() ? _geo.RegionCount : 0;

        bool EnsureGeometry()
        {
            if (_geo == null && GeometrySource != null)
            {
                var g = GeometrySource();
                if (g != null && g.RegionCount > 0) _geo = g;
            }
            return _geo != null;
        }

        static ZoneState ToZone(PollutionState s) => s == PollutionState.Contaminated ? ZoneState.Closed : s == PollutionState.Warning ? ZoneState.Warning : ZoneState.Safe;

        public ZoneState StateOf(int region) => _match != null && region >= 0 && region < _match.RegionCount ? ToZone(_match.Timeline.StateOf(region, Now)) : ZoneState.Safe;

        public ZoneState StateOf(string regionId) => EnsureGeometry() ? StateOf(_geo.RegionIndexOf(regionId)) : ZoneState.Safe;

        public string RegionNameOf(int region) => EnsureGeometry() && region >= 0 && region < _geo.RegionCount ? _geo.RegionName[region] : "";
        public string RegionIdOf(int region) => EnsureGeometry() && region >= 0 && region < _geo.RegionCount ? _geo.RegionId[region] : "";
        public int RegionIndexAt(Vector3 position) => EnsureGeometry() ? _geo.RegionAt(position.x, position.z) : -1;

        public string RegionIdAt(Vector3 position)
        {
            int r = RegionIndexAt(position);
            return r < 0 ? "" : _geo.RegionId[r];
        }

        public ZoneState StateAt(Vector3 position) => StateOf(RegionIndexAt(position));

        // Seconds until a warned region is confirmed; -1 when it is safe or already confirmed.
        public float SecondsLeft(int region)
        {
            if (_match == null || region < 0 || region >= _match.RegionCount) return -1f;
            return _match.Timeline.StateOf(region, Now) == PollutionState.Warning ? (float)Math.Max(0.0, _match.Timeline.ConfirmAt(region) - Now) : -1f;
        }

        public Vector3 RegionCentre(int region) =>
            EnsureGeometry() && region >= 0 && region < _geo.RegionCount ? new Vector3(_geo.CentreX[region], _geo.CentreY[region], _geo.CentreZ[region]) : Vector3.zero;

        // The final region is named once all the others are confirmed or announced... here: when the one before it is
        // announced (the last warning), so nobody learns it early.
        public bool FinalRegionRevealed => _match != null && _match.RegionCount > 0 && _match.Timeline.StateOf(_match.Timeline.Order[_match.RegionCount - 2 < 0 ? 0 : _match.RegionCount - 2], Now) != PollutionState.Safe;
        public int FinalRegion => FinalRegionRevealed ? _match.FinalRegion : -1;

        // Every region but the final one is confirmed.
        public bool OnlyFinalRegionLeft
        {
            get
            {
                if (_match == null) return false;
                for (int r = 0; r < _match.RegionCount; r++)
                    if (r != _match.FinalRegion && _match.Timeline.StateOf(r, Now) != PollutionState.Contaminated) return false;
                return true;
            }
        }

        public const float DoomedLeadSeconds = 45f;

        // The front has reached this point or will within DoomedLeadSeconds (bots leave, hunters leave it for last).
        public bool IsDoomed(Vector3 p)
        {
            if (_match == null) return false;
            int cell = _match.CellAt(p.x, p.z);
            return !_match.Timeline.Grid.IsSea(cell) && _match.Timeline.ArriveMinutes[cell] * 60.0 <= Now + DoomedLeadSeconds;
        }

        // Where a bot in the gas (or about to be) should go: the middle of the nearest region the front is not at
        // yet; the final region when nothing nearer is open. False when it is not doomed or nowhere is left.
        public bool TryEscapeTarget(Vector3 from, int skip, out Vector3 target, out int pass, out int toRegion)
        {
            target = default; pass = -1; toRegion = -1;
            if (_match == null || !IsDoomed(from)) return false;
            float best = float.MaxValue;
            for (int r = 0; r < _match.RegionCount; r++)
            {
                if (r == skip || _match.Timeline.StateOf(r, Now) == PollutionState.Contaminated) continue;
                var c = RegionCentre(r);
                if (IsDoomed(c)) continue;
                float d = (c.x - from.x) * (c.x - from.x) + (c.z - from.z) * (c.z - from.z);
                if (d < best) { best = d; toRegion = r; target = c; }
            }
            return toRegion >= 0;
        }

        // Region transitions, from the clock: the match is deterministic, so every peer sees the same moments.
        void CompatTick()
        {
            if (_match == null) { if (_seen.Length != 0) { _seen = new ZoneState[0]; _finalSeen = false; } return; }
            int n = _match.RegionCount;
            if (_seen.Length != n) { _seen = new ZoneState[n]; _finalSeen = false; }
            bool server = _network != null && _network.IsServerStarted, client = _network != null && _network.IsClientStarted;
            for (int r = 0; r < n; r++)
            {
                var now = StateOf(r);
                if (now == _seen[r]) continue;
                _seen[r] = now;
                if (server) ServerRegionStateChanged?.Invoke(r, now);
                if (client) ClientRegionStateChanged?.Invoke(r, now);
            }
            if (!_finalSeen && FinalRegionRevealed)
            {
                _finalSeen = true;
                if (server) ServerFinalRevealed?.Invoke(_match.FinalRegion);
                if (client) ClientFinalRevealed?.Invoke(_match.FinalRegion);
            }
        }
    }
}

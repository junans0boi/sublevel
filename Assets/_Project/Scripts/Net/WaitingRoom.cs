using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server -> every client, twice a second while people wait on the island (temporary HUD; the board and HUD of #418 read the same).
    public struct WaitingBroadcast : IBroadcast
    {
        public bool Active;
        public float SecondsLeft;
        public int People;
        public int Total;     // people + bots on the island
        public int Target;    // the match's participants
    }

    // The waiting phase of a dedicated server's match (ADR-382, #415): people spawn on the island (World scene), the countdown
    // (ADR-380) runs, and when it ends everyone is moved into the elevator the deployment gave them and the doors are scheduled as
    // before. Off when the scene has no island (an older World build) or the data switches it off. Server only.
    public sealed class WaitingRoom : MonoBehaviour
    {
        // While set, MoveGuard keeps people inside the island and the match flow does not open the doors yet.
        public static WaitingRoom Current { get; private set; }

        MatchDirector _director;
        NetworkManager _nm;
        WaitingFlow _flow;
        float _deckY;
        float _nextSend;
        readonly Dictionary<Combatant, int> _keys = new Dictionary<Combatant, int>();

        public bool Open => _flow != null && _flow.Phase == WaitingPhase.Waiting;
        public WaitingFlow Flow => _flow;

        public static WaitingRoom TryCreate(MatchDirector director, NetworkManager nm)
        {
            var rules = MatchRules.Current ?? new MatchRules();
            if (!NetSession.Dedicated || !rules.UseWaitingIsland) return null;
            var island = GameObject.Find("WaitingIsland");
            if (island == null) { Debug.LogWarning("[WaitingRoom] the scene has no WaitingIsland (rebuild World): people start in the elevators"); return null; }
            var room = director.gameObject.AddComponent<WaitingRoom>();
            room._director = director; room._nm = nm;
            room._deckY = island.transform.position.y;
            room._flow = new WaitingFlow(rules, DedicatedServer.StartSecondsOverride);
            Current = room;
            Debug.Log($"[WaitingRoom] open: the island at ({WaitingIsland.CentreX:F0}, {WaitingIsland.CentreZ:F0}), countdown {(DedicatedServer.StartSecondsOverride >= 0f ? DedicatedServer.StartSecondsOverride : rules.WaitMaxSeconds):0}s / {rules.WaitFullSeconds:0}s when full");
            return room;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        // A place for the person behind `key` (their connection id): where to stand and which way to face (towards the board).
        public bool TryPlace(int key, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            var spot = _flow.Join(key);
            if (spot == null) return false;
            position = new Vector3(spot.Value.x, _deckY + 0.1f, spot.Value.z);
            var look = new Vector3(WaitingIsland.WorldX(WaitingIsland.Board.x) - position.x, 0f, WaitingIsland.WorldZ(WaitingIsland.Board.z) - position.z);
            rotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
            return true;
        }

        // The body joins the room: its damage is held at zero while the room is open (ADR-383, #416); afterwards the filter it had before decides again.
        public void Register(int key, Combatant combatant)
        {
            if (combatant == null) return;
            _keys[combatant] = key;
            var previous = combatant.ServerDamageFilter;
            combatant.ServerDamageFilter = info =>
            {
                float amount = previous != null ? previous(info) : info.Amount;
                return Open ? WaitingFlow.DamageIn(WaitingPhase.Waiting, amount) : amount;
            };
        }

        void Update()
        {
            if (_flow == null || _flow.Phase != WaitingPhase.Waiting) return;
            // Presence: a person whose connection is gone stays on the island for the reconnect window but does not count.
            foreach (var kv in _keys)
            {
                bool here = kv.Key != null && kv.Key.Owner != null && kv.Key.Owner.IsValid && !kv.Key.Dead.Value;
                if (here) _flow.Join(kv.Value); else _flow.Leave(kv.Value);
            }
            double now = Time.unscaledTimeAsDouble;
            bool deploy = _flow.Tick(now);
            if (Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 0.5f;
                Send(true);
                if (_flow.People > 0) Debug.Log($"[WaitingRoom] {_flow.People} people + {_flow.Bots} bots, {_flow.Last.SecondsLeft:0}s left");
            }
            if (deploy) Deploy();
        }

        void Send(bool active)
        {
            var rules = MatchRules.Current ?? new MatchRules();
            _nm.ServerManager.Broadcast(new WaitingBroadcast
            {
                Active = active, SecondsLeft = active ? _flow.Last.SecondsLeft : 0f, People = _flow.People, Total = _flow.Total, Target = rules.Participants,
            }, true, Channel.Unreliable);
        }

        void Deploy()
        {
            Debug.Log($"[WaitingRoom] countdown over ({_flow.Last.Reason}): {_flow.People} people, {_flow.Last.BotsTotal} bots in all, to the elevators");
            foreach (var kv in _keys)
            {
                if (kv.Key == null) continue;
                int index = _flow.DeploymentIndex(kv.Value);
                _director.ServerDeployFromIsland(kv.Key, index);
            }
            Send(false);
            _director.ServerOpenDoorsAfterWaiting();
            if (Current == this) Current = null;
        }
    }
}

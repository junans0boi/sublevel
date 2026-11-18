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
        public float BungeeBest; // the highest rebound of this room so far in metres (0 = none yet, #419)
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

        // The match's bots wait hidden in their elevator cars; the schedule (ADR-384) moves some of them onto the island, which walk about
        // there, and at the deployment they all go back to their cars. The brain of a held bot is paused (SpawnProtection), the walk is driven here.
        sealed class WaitingBot
        {
            public Combatant Body; public Vector3 HomePosition; public Quaternion HomeRotation;
            public bool OnIsland; public Vector3 Target; public float PickAt;
        }
        readonly List<WaitingBot> _bots = new List<WaitingBot>();
        const float BotWalkSpeed = 1.3f;
        public int BotsOnIsland { get { int n = 0; foreach (var b in _bots) if (b.OnIsland) n++; return n; } }

        public void AddBot(Combatant body, Vector3 home, Quaternion homeRotation)
        {
            if (body == null) return;
            _bots.Add(new WaitingBot { Body = body, HomePosition = home, HomeRotation = homeRotation });
            Register(-1 - _bots.Count, body);
        }

        void DriveBots(CountdownState cd)
        {
            int want = Mathf.Min(_bots.Count, WaitingBotSchedule.BotsAt(cd.Elapsed, _flow.MaxSeconds > 0f ? _flow.MaxSeconds : (MatchRules.Current ?? new MatchRules()).WaitMaxSeconds,
                cd.BotsTotal, _flow.People, (MatchRules.Current ?? new MatchRules()).Participants));
            int onIsland = BotsOnIsland;
            for (int i = 0; i < _bots.Count && onIsland < want; i++)
            {
                var b = _bots[i];
                if (b.OnIsland || b.Body == null) continue;
                var spot = _flow.PlaceBot(i);
                if (spot == null) break;
                b.OnIsland = true; onIsland++;
                b.Body.transform.SetPositionAndRotation(new Vector3(spot.Value.x, _deckY + 0.1f, spot.Value.z), Quaternion.Euler(0f, (i * 137) % 360, 0f));
                b.PickAt = Time.time + 1f + (i % 5);
                b.Target = b.Body.transform.position;
            }
            _flow.SetBots(onIsland);
            float dt = Time.deltaTime;
            foreach (var b in _bots)
            {
                if (!b.OnIsland || b.Body == null) continue;
                var p = b.Body.transform.position;
                if (Time.time >= b.PickAt)
                {
                    // Stroll to a random place of the island (outside the booth and tower pads), then stand a while.
                    float a = Random.value * Mathf.PI * 2f, r = 6f + Random.value * 26f;
                    float tx = WaitingIsland.CentreX + Mathf.Cos(a) * r, tz = WaitingIsland.CentreZ + Mathf.Sin(a) * r;
                    WaitingIsland.Clamp(ref tx, ref tz);
                    b.Target = new Vector3(tx, p.y, tz);
                    b.PickAt = Time.time + 4f + Random.value * 8f;
                }
                var d = b.Target - p; d.y = 0f;
                if (d.sqrMagnitude < 0.04f) continue;
                var step = d.normalized * Mathf.Min(d.magnitude, BotWalkSpeed * dt);
                b.Body.transform.SetPositionAndRotation(p + step, Quaternion.Slerp(b.Body.transform.rotation, Quaternion.LookRotation(d), 6f * dt));
            }
        }

        public float BungeeBest { get; set; }   // set by the bungee (#419), shown on the board
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
                if (kv.Key != null && !kv.Key.IsPlayer) continue; // bots are not people
                bool here = kv.Key != null && kv.Key.Owner != null && kv.Key.Owner.IsValid && !kv.Key.Dead.Value;
                if (here) _flow.Join(kv.Value); else _flow.Leave(kv.Value);
            }
            double now = Time.unscaledTimeAsDouble;
            bool deploy = _flow.Tick(now);
            if (!deploy) DriveBots(_flow.Last);
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
                Active = active, SecondsLeft = active ? _flow.Last.SecondsLeft : 0f, People = _flow.People, Total = _flow.Total, Target = rules.Participants, BungeeBest = BungeeBest,
            }, true, Channel.Unreliable);
        }

        void Deploy()
        {
            Debug.Log($"[WaitingRoom] countdown over ({_flow.Last.Reason}): {_flow.People} people, {_flow.Last.BotsTotal} bots in all, to the elevators");
            foreach (var b in _bots)
                if (b.OnIsland && b.Body != null) b.Body.transform.SetPositionAndRotation(b.HomePosition, b.HomeRotation);
            foreach (var kv in _keys)
            {
                if (kv.Key == null || !kv.Key.IsPlayer) continue;
                int index = _flow.DeploymentIndex(kv.Value);
                _director.ServerDeployFromIsland(kv.Key, index);
            }
            Send(false);
            _director.ServerOpenDoorsAfterWaiting();
            if (Current == this) Current = null;
        }
    }
}

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
        public byte LiftPhase;   // the bungee lift (ADR-490): its LiftPhase, the seconds into it, and the lit call buttons (1 bottom, 2 top)
        public float LiftInto;
        public byte LiftCalls;
    }

    // Client -> server: F at a call button of the bungee lift (ADR-490). The server checks the sender stands at that button.
    public struct LiftCallRequest : IBroadcast
    {
        public bool Top;
    }

    // The waiting phase of a dedicated server's match (ADR-382, #415): people spawn on the island (World scene), the countdown
    // (ADR-380) runs, and when it ends everyone is moved into the elevator the deployment gave them and the doors are scheduled as
    // before. Off when the scene has no island (an older World build) or the data switches it off. Server only.
    public sealed partial class WaitingRoom : MonoBehaviour
    {
        // While set, MoveGuard keeps people inside the island and the match flow does not open the doors yet.
        public static WaitingRoom Current { get; private set; }
        // Tests: a host in the editor runs the waiting phase too (a real server turns it on with NetSession.Dedicated).
        public static bool ForceEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ForceEnabled = false; Current = null; }

        MatchDirector _director;
        NetworkManager _nm;
        WaitingFlow _flow;
        RaceRoom _race;         // the race hall (ADR-505), null in a World built before it
        float _deckY;
        float _nextSend;
        readonly Dictionary<Combatant, int> _keys = new Dictionary<Combatant, int>();

        // The match's bots wait hidden in their elevator cars; the schedule (ADR-384) moves some of them onto the island, which walk about
        // there, and at the deployment they all go back to their cars. The brain of a held bot is paused (SpawnProtection), the walk is driven here.
        sealed class WaitingBot
        {
            public Combatant Body; public Vector3 HomePosition; public Quaternion HomeRotation;
            public bool OnIsland; public Vector3 Target; public float PickAt; public bool ToButton;
            public int Booth = -1; public float ShootUntil, NextShot;   // at the range (WaitingRoom.Range)
        }
        readonly List<WaitingBot> _bots = new List<WaitingBot>();
        const float BotWalkSpeed = 1.3f;
        const float BotCallChance = 0.08f;   // of a stroll: ends at the lift's call button
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
                if (Time.time >= b.PickAt && !PickBooth(b))
                {
                    // Stroll to a random place of the island (outside the booth and tower pads), then stand a while.
                    float a = Random.value * Mathf.PI * 2f, r = 6f + Random.value * 26f;
                    float tx = WaitingIsland.CentreX + Mathf.Cos(a) * r, tz = WaitingIsland.CentreZ + Mathf.Sin(a) * r;
                    // Now and then a bot walks to the lift and presses its call button, through the same server check as a person's F (ADR-490).
                    b.ToButton = Random.value < BotCallChance;
                    if (b.ToButton) { var at = CallButtonWorld(false, _deckY); tx = at.x; tz = at.z + 1f; }
                    WaitingIsland.Clamp(ref tx, ref tz);
                    if (WaitingRange.InLane(tx - WaitingIsland.CentreX, tz - WaitingIsland.CentreZ, 1f)) tx = 2f * WaitingIsland.CentreX - tx;   // not down a range lane
                    b.Target = new Vector3(tx, p.y, tz);
                    b.PickAt = Time.time + 4f + Random.value * 8f;
                }
                if (ShootAtRange(b)) continue;
                var d = b.Target - p; d.y = 0f;
                if (d.sqrMagnitude < 0.04f)
                {
                    if (b.ToButton) { b.ToButton = false; ServerCall(b.Body, false); }
                    continue;
                }
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
            if (!(NetSession.Dedicated || ForceEnabled) || !rules.UseWaitingIsland) return null;
            // --perf bots-only runs (ADR-323): the countdown needs a person to start its clock, so with nobody there the bots would never drop.
            if (LobbyNet.AllowEmptyStart) { Debug.Log("[WaitingRoom] --perf: no island, the bots start in the elevators"); return null; }
            var island = GameObject.Find("WaitingIsland");
            if (island == null) { Debug.LogWarning("[WaitingRoom] the scene has no WaitingIsland (rebuild World): people start in the elevators"); return null; }
            var room = director.gameObject.AddComponent<WaitingRoom>();
            room._director = director; room._nm = nm;
            room._deckY = island.transform.position.y;
            room._flow = new WaitingFlow(rules, DedicatedServer.StartSecondsOverride);
            room._race = RaceRoom.TryCreate(nm, island.transform);
            Current = room;
            nm.ServerManager.RegisterBroadcast<LiftCallRequest>(room.OnCallRequest);
            Debug.Log($"[WaitingRoom] open: the island at ({WaitingIsland.CentreX:F0}, {WaitingIsland.CentreZ:F0}), countdown {(DedicatedServer.StartSecondsOverride >= 0f ? DedicatedServer.StartSecondsOverride : rules.WaitMaxSeconds):0}s / {rules.WaitFullSeconds:0}s when full");
            return room;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_nm != null && _nm.ServerManager != null) _nm.ServerManager.UnregisterBroadcast<LiftCallRequest>(OnCallRequest);
        }

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
            ArmRange(combatant);
            var previous = combatant.ServerDamageFilter;
            combatant.ServerDamageFilter = info =>
            {
                float amount = previous != null ? previous(info) : info.Amount;
                return Open ? WaitingFlow.DamageIn(WaitingPhase.Waiting, amount) : amount;
            };
        }

        // ---- bungee (ADR-386/387, #419): the lift up the tower, the jump off the board, falls put back ----
        readonly BungeeLine _line = new BungeeLine();
        Combatant _jumper;
        Vector3 _pad;                 // the landing pad on the deck (world); the jump board is right above it at BungeeCurve.Top
        float _onBoardSince = -1f;
        const float BoardHoldSeconds = 0.8f;   // standing on the board this long jumps (when the line is free)
        LiftCall _lift;                        // made on the first tick (the room's clock)
        sealed class LiftRider { public Combatant Body; public Vector3 From; public bool Up; }
        readonly List<LiftRider> _riders = new List<LiftRider>();
        readonly Dictionary<Combatant, float> _carriedUntil = new Dictionary<Combatant, float>();
        const float CarryTailSeconds = 1f;     // the owner's ride may end a moment after the server's

        public LiftCall Lift => _lift;
        public int LiftRiders => _riders.Count;

        // Being moved by the lift or the jump: MoveGuard does not judge the speed of it (the server drives the same curve).
        public bool Carries(Combatant c)
        {
            if (c == null) return false;
            if (c == _jumper) return true;
            foreach (var r in _riders) if (r.Body == c) return true;
            return _carriedUntil.TryGetValue(c, out float until) && Time.unscaledTime < until;
        }

        void Release(Combatant c) { if (c != null) _carriedUntil[c] = Time.unscaledTime + CarryTailSeconds; }

        Vector3 LiftWorld(float y) => new Vector3(WaitingIsland.WorldX(WaitingIsland.Lift.x), y, WaitingIsland.WorldZ(WaitingIsland.Lift.z));

        bool InCage(Vector3 p, float floorY)
        {
            var l = LiftWorld(floorY);
            return Mathf.Abs(p.x - l.x) <= BungeeLift.HalfSize && Mathf.Abs(p.z - l.z) <= BungeeLift.HalfSize && Mathf.Abs(p.y - floorY) < 1.5f;
        }

        static void Put(Combatant c, Vector3 at)
        {
            c.transform.position = at;
            if (c.TryGetComponent<MoveGuard>(out var guard)) guard.ServerRelocate(at);
        }

        public static Vector3 CallButtonWorld(bool top, float deckY)
        {
            var b = top ? WaitingIsland.CallTop : WaitingIsland.CallBottom;
            return new Vector3(WaitingIsland.WorldX(b.x), deckY + b.y, WaitingIsland.WorldZ(b.z));
        }

        // Server: `who` pressed the call button (a client's F, a bot, the host). Refused unless they stand at it, alive and waiting.
        public bool ServerCall(Combatant who, bool top)
        {
            if (who == null || who.Dead.Value || !Open || !_keys.ContainsKey(who)) return false;
            var d = CallButtonWorld(top, _deckY) - who.transform.position;
            float rise = d.y; d.y = 0f;
            float reach = InteractRules.Current.ReachMeters + 1.5f;   // the client's F reach + network slack, as the doors
            if (d.sqrMagnitude > reach * reach || rise < -1.5f || rise > 3f) return false;
            if (_lift == null) _lift = new LiftCall(Time.unscaledTimeAsDouble);
            _lift.Call(top, Time.unscaledTimeAsDouble);
            Debug.Log($"[WaitingRoom] {who.name} called the lift ({(top ? "top" : "bottom")})");
            Send(true);   // the light and the ding at once, not at the next half-second
            return true;
        }

        void OnCallRequest(FishNet.Connection.NetworkConnection connection, LiftCallRequest request, Channel channel)
        {
            var body = connection != null && connection.FirstObject != null ? connection.FirstObject.GetComponent<Combatant>() : null;
            ServerCall(body, request.Top);
        }

        bool AnyoneInCage(float floorY)
        {
            foreach (var kv in _keys)
            {
                var c = kv.Key;
                if (c != null && c.IsPlayer && !c.Dead.Value && c != _jumper && InCage(c.transform.position, floorY)) return true;
            }
            return false;
        }

        void TickLift(double now)
        {
            if (_lift == null) _lift = new LiftCall(now);
            var before = _lift.Phase;
            bool occupied = !_lift.Moving && AnyoneInCage(_deckY + (before == LiftPhase.OpenTop ? BungeeCurve.Top : 0f));
            if (_lift.Tick(now, occupied))
            {
                var phase = _lift.Phase;
                // Doors shut: whoever is in the cage rides (at most Capacity); doors open: the ride is over.
                if (phase == LiftPhase.Rising || phase == LiftPhase.Descending)
                {
                    bool up = phase == LiftPhase.Rising;
                    float floorY = _deckY + (up ? 0f : BungeeCurve.Top);
                    var inCage = new List<Combatant>();
                    foreach (var kv in _keys)
                    {
                        var c = kv.Key;
                        if (c == null || !c.IsPlayer || c.Dead.Value || c == _jumper) continue;
                        if (InCage(c.transform.position, floorY)) inCage.Add(c);
                    }
                    foreach (var c in BungeeLift.Board(inCage))
                    {
                        var from = c.transform.position; from.y = floorY + 0.1f;
                        _riders.Add(new LiftRider { Body = c, From = from, Up = up });
                        if (c.TryGetComponent<CombatantStance>(out var stance)) stance.ServerLiftStart(from, up);
                    }
                    if (_riders.Count > 0) Debug.Log($"[WaitingRoom] bungee lift {(up ? "up" : "down")} with {_riders.Count}");
                }
                else
                {
                    foreach (var r in _riders)
                        if (r.Body != null) { Put(r.Body, r.From + Vector3.up * (r.Up ? BungeeLift.Height : -BungeeLift.Height)); Release(r.Body); }
                    _riders.Clear();
                }
            }
            if (_riders.Count == 0 || !_lift.Moving) return;
            float d = BungeeLift.Travelled(_lift.Into(now));
            for (int i = _riders.Count - 1; i >= 0; i--)
            {
                var r = _riders[i];
                if (r.Body == null || r.Body.Owner == null || !r.Body.Owner.IsValid) { _riders.RemoveAt(i); continue; }
                var at = r.From + Vector3.up * (r.Up ? d : -d);
                at.x = r.Body.transform.position.x; at.z = r.Body.transform.position.z;
                if (r.Body.TryGetComponent<MoveGuard>(out var guard)) guard.ServerRelocate(at);
            }
        }

        void TickBungee(double now)
        {
            BungeeBest = _line.Best;
            _pad = new Vector3(WaitingIsland.WorldX(WaitingIsland.BungeePad.x), _deckY, WaitingIsland.WorldZ(WaitingIsland.BungeePad.z));
            TickLift(now);
            if (_line.Active)
            {
                if (_jumper == null || _jumper.Owner == null || !_jumper.Owner.IsValid) { _line.Abort(_line.Jumper, now); _jumper = null; }
                else
                {
                    // The server's own copy follows the same curve; the owner's body is driven by its client (BungeeRide).
                    var at = _pad + Vector3.up * (BungeeCurve.Height(_line.ElapsedOf(now), _line.LowerAt) + 0.1f);
                    if (_jumper.TryGetComponent<MoveGuard>(out var guard)) guard.ServerRelocate(at);
                    if (_line.Tick(now)) { Debug.Log($"[WaitingRoom] bungee over, room best {_line.Best:0.0} m"); RecordJump(_jumper); Release(_jumper); _jumper = null; }
                }
            }
            ReturnFallen();
            if (_line.Active) { _onBoardSince = -1f; return; }   // one at a time: the next waits on the board (Busy), then the cooldown
            Combatant standing = null;
            foreach (var kv in _keys)
            {
                var c = kv.Key;
                if (c == null || !c.IsPlayer || c.Dead.Value) continue;
                var p = c.transform.position;
                var d = p - _pad; d.y = 0f;
                if (d.sqrMagnitude <= WaitingIsland.JumpBoardRadius * WaitingIsland.JumpBoardRadius && p.y > _deckY + BungeeCurve.Top - 1.5f) { standing = c; break; }
            }
            if (standing == null) { _onBoardSince = -1f; return; }
            if (_onBoardSince < 0f) _onBoardSince = Time.unscaledTime;
            if (Time.unscaledTime - _onBoardSince < BoardHoldSeconds) return;
            if (_line.TryStart(standing.ObjectId, now) != BungeeLine.Result.Started) return;
            _jumper = standing;
            _onBoardSince = -1f;
            if (standing.TryGetComponent<CombatantStance>(out var stance)) stance.ServerBungeeStart(_pad);
            Debug.Log($"[WaitingRoom] bungee: {standing.name} jumps");
        }

        // The jumper pressed F while hanging (ADR-498, CombatantStance.CmdBungeeLower): the cord lowers them to the pad now.
        public void ServerBungeeLower(Combatant c)
        {
            if (c == null || c != _jumper) return;
            if (_line.RequestLower(c.ObjectId, Time.unscaledTimeAsDouble)) Debug.Log($"[WaitingRoom] bungee: {c.name} asked to come down at {_line.LowerAt:0.0} s");
        }

        // Someone who left the top floor any way but the jump (or slipped off the bridge) is put back on the deck at once, unhurt.
        void ReturnFallen()
        {
            foreach (var kv in _keys)
            {
                var c = kv.Key;
                if (c == null || !c.IsPlayer || c.Dead.Value || Carries(c)) continue;
                var p = c.transform.position;
                float lx = p.x - WaitingIsland.CentreX, lz = p.z - WaitingIsland.CentreZ;
                bool onTop = WaitingIsland.OnBungeeTop(lx, lz) && p.y > _deckY + BungeeCurve.Top - 2f;
                if (!BungeeLift.FellOff(p.y - _deckY, onTop)) continue;
                var back = new Vector3(_pad.x + 2.5f, _deckY + 0.1f, _pad.z);
                Put(c, back);
                if (c.TryGetComponent<CombatantStance>(out var stance)) stance.ServerCorrect(back);
                Debug.Log($"[WaitingRoom] {c.name} fell from the tower: back on the deck");
            }
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
            if (!deploy) { DriveBots(_flow.Last); TickBungee(now); TickRange(); SendRecords(false); }
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
                LiftPhase = (byte)(_lift != null ? _lift.Phase : LiftPhase.OpenBottom), LiftInto = _lift != null ? _lift.Into(Time.unscaledTimeAsDouble) : 0f,
                LiftCalls = (byte)(_lift == null ? 0 : (_lift.CalledBottom ? 1 : 0) | (_lift.CalledTop ? 2 : 0)),
            }, true, Channel.Unreliable);
        }

        void Deploy()
        {
            if (_line.Active && _jumper != null) _line.Abort(_line.Jumper, Time.unscaledTimeAsDouble); // the departure cuts a jump short; the jumper's own client ends the ride on the corrected place
            _race?.ServerClose();   // out of the race cars before anyone is moved (ADR-511)
            Debug.Log($"[WaitingRoom] countdown over ({_flow.Last.Reason}): {_flow.People} people, {_flow.Last.BotsTotal} bots in all, to the elevators");
            CloseRange();   // the booth guns back and the room's records gone before anyone reaches an elevator (ADR-501/502)
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

using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Sent once by a client after it connected: the token that names this player's session across reconnects.
    public struct SessionHello : IBroadcast { public string Token; }

    // Client side: sends the token as soon as the connection is up (again after every reconnect of the same manager).
    public sealed class SessionHelloSender : MonoBehaviour
    {
        string _token;
        NetworkManager _nm;
        bool _sent;

        public static void Attach(NetworkManager nm, string token)
        {
            if (nm == null || string.IsNullOrEmpty(token)) return;
            var sender = nm.GetComponent<SessionHelloSender>() ?? nm.gameObject.AddComponent<SessionHelloSender>();
            sender._nm = nm;
            sender._token = token;
        }

        void Update()
        {
            if (_nm == null) return;
            if (!_nm.IsClientStarted) { _sent = false; return; }
            var connection = _nm.ClientManager.Connection;
            if (_sent || connection == null || connection.ClientId < 0) return;
            _sent = true;
            _nm.ClientManager.Broadcast(new SessionHello { Token = _token });
        }
    }

    // Server side (#166, ADR-125): when a connection drops, the player's combatant stays in the match for
    // MatchRules.ReconnectSeconds. A client that comes back with the same session token gets it back (health,
    // inventory, stance, place all untouched); when the time is over a bot takes the combatant over.
    public sealed class SessionKeeper : MonoBehaviour
    {
        // How long a new connection may take to tell its token before it is treated as a stranger.
        const double HelloWaitSeconds = 2.0;

        sealed class Waiting { public NetworkConnection Connection; public Action Spawn; public double Deadline; }

        NetworkManager _nm;
        readonly SessionHolds _holds = new SessionHolds();
        readonly Dictionary<int, string> _tokens = new Dictionary<int, string>();
        readonly Dictionary<int, NetworkObject> _held = new Dictionary<int, NetworkObject>();
        readonly List<Waiting> _waiting = new List<Waiting>();

        public int HeldCount => _holds.Count;
        // Connections whose token the server already knows.
        public int SessionCount => _tokens.Count;
        // A combatant of the match left alone by its player and now run by a bot.
        public event Action<Combatant> TakenOver;

        public static SessionKeeper Attach(NetworkManager nm)
        {
            var keeper = nm.GetComponent<SessionKeeper>() ?? nm.gameObject.AddComponent<SessionKeeper>();
            keeper.Bind(nm);
            return keeper;
        }

        // A NetworkManager that outlives a match is bound again by the next one: start clean.
        void Bind(NetworkManager nm)
        {
            Unbind();
            _tokens.Clear();
            _held.Clear();
            _waiting.Clear();
            _holds.Clear();
            _nm = nm;
            nm.ServerManager.RegisterBroadcast<SessionHello>(OnHello, false);
            nm.ServerManager.Objects.OnPreDestroyClientObjects += OnDisconnecting;
        }

        void OnDestroy() => Unbind();

        void Unbind()
        {
            if (_nm == null) return;
            _nm.ServerManager.UnregisterBroadcast<SessionHello>(OnHello);
            _nm.ServerManager.Objects.OnPreDestroyClientObjects -= OnDisconnecting;
            _nm = null;
        }

        double Now => Time.timeAsDouble;

        // ---- Hello and spawn ----

        void OnHello(NetworkConnection conn, SessionHello hello, Channel channel)
        {
            if (string.IsNullOrEmpty(hello.Token)) return;
            // The same session coming back before the server noticed the old line is dead: that line is cut now.
            foreach (var pair in _tokens)
            {
                if (pair.Value != hello.Token || pair.Key == conn.ClientId) continue;
                if (_nm.ServerManager.Clients.TryGetValue(pair.Key, out var old)) old.Disconnect(false);
            }
            _tokens[conn.ClientId] = hello.Token;
        }

        // Called where a loaded client would get a new player. True = handled here (resumed, or waiting for the
        // token); false = the caller spawns a fresh player now.
        public bool Intercept(NetworkConnection conn, Action spawn)
        {
            if (_tokens.TryGetValue(conn.ClientId, out string token))
            {
                if (TryResume(conn, token)) return true;
                if (!OtherLiveHolder(conn, token)) return false;
            }
            else if (_holds.Count == 0) return false;
            _waiting.Add(new Waiting { Connection = conn, Spawn = spawn, Deadline = Now + HelloWaitSeconds });
            return true;
        }

        bool OtherLiveHolder(NetworkConnection conn, string token)
        {
            foreach (var pair in _tokens) if (pair.Value == token && pair.Key != conn.ClientId) return true;
            return false;
        }

        bool TryResume(NetworkConnection conn, string token)
        {
            if (!_holds.TryResume(token, Now, out int id)) return false;
            if (!_held.TryGetValue(id, out var nob) || nob == null || !nob.IsSpawned) return false;
            _held.Remove(id);
            nob.GiveOwnership(conn);
            return true;
        }

        // ---- Drop ----

        void OnDisconnecting(NetworkConnection conn)
        {
            bool known = _tokens.TryGetValue(conn.ClientId, out string token);
            if (!known) { Debug.Log($"[SessionKeeper] connection {conn.ClientId} dropped without a session token, nothing held"); return; }
            _tokens.Remove(conn.ClientId);
            for (int i = _waiting.Count - 1; i >= 0; i--) if (_waiting[i].Connection == conn) _waiting.RemoveAt(i);
            var owned = new List<NetworkObject>(conn.Objects);
            foreach (var nob in owned)
            {
                var combatant = nob != null ? nob.GetComponent<Combatant>() : null;
                if (combatant == null || !combatant.IsPlayer) continue;
                if (combatant.Dead.Value) { Debug.Log($"[SessionKeeper] connection {conn.ClientId} dropped, its combatant is dead, nothing held"); continue; }
                // Unowned from now on: the despawn that follows a disconnect only takes what the connection still owns.
                nob.RemoveOwnership();
                _holds.Begin(token, nob.ObjectId, Now, (MatchRules.Current ?? new MatchRules()).ReconnectSeconds);
                _held[nob.ObjectId] = nob;
                Debug.Log($"[SessionKeeper] connection {conn.ClientId} dropped, combatant {nob.ObjectId} held");
                break;
            }
        }

        // ---- Time ----

        void Update()
        {
            if (_nm == null || !_nm.IsServerStarted) return;
            double now = Now;
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                var w = _waiting[i];
                if (w.Connection == null || !w.Connection.IsActive) { _waiting.RemoveAt(i); continue; }
                bool known = _tokens.TryGetValue(w.Connection.ClientId, out string token);
                bool done = known && TryResume(w.Connection, token);
                if (!done && (now >= w.Deadline || (known && !OtherLiveHolder(w.Connection, token)))) { w.Spawn(); done = true; }
                if (done) _waiting.RemoveAt(i);
            }
            if (_holds.Count == 0) return;
            foreach (int id in _holds.TakeExpired(now))
            {
                if (!_held.TryGetValue(id, out var nob)) continue;
                _held.Remove(id);
                if (nob != null && nob.IsSpawned) HandOverToBot(nob);
            }
        }

        // Game.AI sits above this assembly, so the bot brain is added by name (as MatchDirector does for its points).
        void HandOverToBot(NetworkObject nob)
        {
            var combatant = nob.GetComponent<Combatant>();
            if (combatant == null || combatant.Dead.Value) return;
            var type = Type.GetType("Game.AI.BotController, Game.AI");
            if (type == null) { Debug.LogWarning("[SessionKeeper] no bot brain in this build, the combatant stays where it is"); return; }
            if (nob.GetComponent(type) == null) nob.gameObject.AddComponent(type);
            Debug.Log($"[SessionKeeper] combatant {nob.ObjectId} taken over by a bot");
            TakenOver?.Invoke(combatant);
        }
    }
}

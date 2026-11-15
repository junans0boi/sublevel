using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    public enum MatchOutcome { Playing, Eliminated, Won }

    // One line of the kill feed, sent to every client when a combatant dies.
    public struct KillEntry : IBroadcast
    {
        public int KillerId;        // network object id; -1 when nobody did it (a fall, the zone)
        public int VictimId;
        public string KillerName;
        public string VictimName;
        public int WeaponId;        // 0 when no weapon was involved
        public bool Headshot;
        public byte Cause;          // DamageCause
        public int KillerKills;     // the killer's total after this one
        public int VictimPlacement; // 1 = winner; the first to die gets the total
        public int Alive;
        public int Total;
    }

    // Where the match stands. Sent whenever it changes and to every client that finishes loading.
    public struct MatchStatus : IBroadcast
    {
        public int Alive;
        public int Total;
        public bool Over;
        public int WinnerId;        // -1 while playing, or when nobody was left standing
        public string WinnerName;
        public int WinnerTeam;      // team matches (ADR-072): the winning team; 0 otherwise
    }

    // One combatant's numbers for the result screen (ADR-094): sent when it dies and, for the survivors, when the
    // match is decided. Damage is health taken off others (not teammates, not oneself); seconds since the match began.
    public struct PlayerStats : IBroadcast
    {
        public int Id;
        public float Damage;
        public float Seconds;
        public float Meters;        // ground distance walked, swum or driven
    }

    // A client asking for a fresh match (solo has no revival).
    public struct MatchRestartRequest : IBroadcast
    {
        public byte Unused;
    }

    // The match's score keeping (ADR-053 (5)). The server counts who is alive, who killed whom and in which order
    // combatants died (= placement), and declares the winner when one is left. Clients get it as broadcasts, so
    // this needs no network object of its own: MatchDirector adds it next to itself.
    // Dummies and bots count as combatants like players: the match ends when one is left alive.
    public partial class MatchState : MonoBehaviour
    {
        public const int FeedCapacity = 16;
        const string PlayerName = "플레이어";
        const string DummyName = "훈련 더미 ";

        public static MatchState Instance { get; private set; }
        // Raised once per match when the state exists and the scene has finished loading (screens install here).
        public static event Action<MatchState> Started;
        // Client side: a kill arrived / alive count, winner or match-over flag changed.
        public static event Action<KillEntry> KillPosted;
        public static event Action Changed;

        NetworkManager _network;
        bool _bound;

        // ---- Server state ----
        readonly HashSet<int> _serverDead = new HashSet<int>();
        readonly Dictionary<int, int> _serverKills = new Dictionary<int, int>();
        readonly Dictionary<int, string> _serverNames = new Dictionary<int, string>();
        int _serverSeen = -1, _serverAlive, _serverTotal, _serverPlayers, _serverDummies;
        bool _serverOver;
        readonly Dictionary<int, float> _serverDamage = new Dictionary<int, float>();
        readonly Dictionary<int, float> _serverMeters = new Dictionary<int, float>();
        readonly Dictionary<int, Vector3> _serverLastAt = new Dictionary<int, Vector3>();
        float _serverStart;
        int _serverWinnerId = -1;
        string _serverWinnerName = "";

        // ---- Replicated state (what clients, including the host's own, know) ----
        readonly Dictionary<int, int> _kills = new Dictionary<int, int>();
        readonly Dictionary<int, int> _placements = new Dictionary<int, int>();
        readonly List<KillEntry> _feed = new List<KillEntry>(FeedCapacity);
        readonly Dictionary<int, KillEntry> _deaths = new Dictionary<int, KillEntry>();
        readonly Dictionary<int, PlayerStats> _stats = new Dictionary<int, PlayerStats>();
        readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        public int AliveCount { get; private set; }
        public int TotalCount { get; private set; }
        public bool IsOver { get; private set; }
        // Server: the match has been decided (a dedicated server has no client to read IsOver from, #168).
        public bool ServerOver => _serverOver;
        public int WinnerId { get; private set; } = -1;
        public string WinnerName { get; private set; } = "";
        // Kills received since the match began (the feed itself keeps only the latest FeedCapacity).
        public int KillsPosted { get; private set; }
        // Oldest first.
        public IReadOnlyList<KillEntry> Feed => _feed;

        public int LocalKills => KillsOf(Combatant.LocalPlayer);
        // 0 while the local player is still in the match.
        public int LocalPlacement => PlacementOf(Combatant.LocalPlayer);

        public MatchOutcome LocalOutcome
        {
            get
            {
                int placement = LocalPlacement;
                return placement == 0 ? MatchOutcome.Playing : placement == 1 ? MatchOutcome.Won : MatchOutcome.Eliminated;
            }
        }

        public int KillsOf(Combatant combatant) =>
            combatant != null && _kills.TryGetValue(combatant.ObjectId, out int kills) ? kills : 0;

        public int PlacementOf(Combatant combatant) =>
            PlacementWithTeam(combatant, combatant != null && _placements.TryGetValue(combatant.ObjectId, out int placement) ? placement : 0);

        // Every death of the match by victim id (the feed keeps only the latest): who killed whom, with what.
        public bool TryGetDeath(int victimId, out KillEntry entry) => _deaths.TryGetValue(victimId, out entry);
        // Who killed this combatant; -1 while it lives or when nobody did (the zone, a fall).
        public int KillerOf(int victimId) => _deaths.TryGetValue(victimId, out var e) ? e.KillerId : -1;

        // The name the kill feed gave this combatant ("" until it killed or died).
        public string NameOfId(int id) => _names.TryGetValue(id, out string name) ? name : "";

        // Health this combatant took off others / seconds it lasted; known once it died or the match was decided.
        public float DamageOf(Combatant combatant) => combatant != null && _stats.TryGetValue(combatant.ObjectId, out var s) ? s.Damage : 0f;
        public float MetersOf(Combatant combatant) => combatant != null && _stats.TryGetValue(combatant.ObjectId, out var s) ? s.Meters : 0f;
        public float SecondsOf(Combatant combatant) => combatant != null && _stats.TryGetValue(combatant.ObjectId, out var s) ? s.Seconds : 0f;

        // Server only: health taken off others so far (the server total the result screen shows).
        public float ServerDamageOf(Combatant combatant) =>
            combatant != null && _serverDamage.TryGetValue(combatant.ObjectId, out float d) ? d : 0f;

        // Server only: the name shown for a combatant ("" before the server has seen it).
        public string ServerNameOf(Combatant combatant) =>
            combatant != null && _serverNames.TryGetValue(combatant.ObjectId, out string name) ? name : "";

        // Server only: gives a combatant its name before the first count would name it ("봇 1" for bots).
        public void ServerSetName(Combatant combatant, string name)
        {
            if (combatant == null || string.IsNullOrEmpty(name)) return;
            _serverNames[combatant.ObjectId] = name;
        }

        bool ServerActive => _network != null && _network.IsServerStarted;

        // Started keeps its subscribers: the screens hook it once per play session, possibly before this runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            KillPosted = null;
            Changed = null;
        }

        void Awake()
        {
            Instance = this;
            _serverStart = Time.time;
            Combatant.ServerDied += OnServerDied;
            Combatant.ServerDamaged += OnServerDamaged;
        }

        void Start() => Started?.Invoke(this);

        // Called by MatchDirector before it starts the connections.
        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<KillEntry>(OnKillEntry);
            _network.ClientManager.RegisterBroadcast<MatchStatus>(OnMatchStatus);
            _network.ClientManager.RegisterBroadcast<PlayerStats>(OnPlayerStats);
            _network.ServerManager.RegisterBroadcast<MatchRestartRequest>(OnRestartRequest);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            BindTeam();
        }

        void OnDestroy()
        {
            Combatant.ServerDied -= OnServerDied;
            Combatant.ServerDamaged -= OnServerDamaged;
            if (Instance == this) Instance = null;
            // The manager may already be gone when the scene unloads.
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<KillEntry>(OnKillEntry);
            _network.ClientManager.UnregisterBroadcast<MatchStatus>(OnMatchStatus);
            _network.ClientManager.UnregisterBroadcast<PlayerStats>(OnPlayerStats);
            _network.ServerManager.UnregisterBroadcast<MatchRestartRequest>(OnRestartRequest);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
            UnbindTeam();
        }

        void Update()
        {
            if (!ServerActive) return;
            ServerTrackDistance();
            // Combatants come and go by spawning; deaths arrive through ServerDied.
            if (Combatant.All.Count == _serverSeen) return;
            if (ServerRecount()) ServerSendStatus(null);
        }

        // ---- Server ----

        // Ground distance of the living, summed per frame; a jump over TeleportMeters (deployment, elevator) is not travel.
        const float TeleportMeters = 30f;
        void ServerTrackDistance()
        {
            if (_serverOver) return;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsServerInitialized || _serverDead.Contains(c.ObjectId)) continue;
                var at = c.transform.position;
                if (_serverLastAt.TryGetValue(c.ObjectId, out var last))
                {
                    last.y = at.y;
                    float d = Vector3.Distance(last, at);
                    if (d < TeleportMeters) _serverMeters[c.ObjectId] = (_serverMeters.TryGetValue(c.ObjectId, out float m) ? m : 0f) + d;
                }
                _serverLastAt[c.ObjectId] = at;
            }
        }

        // Names newcomers and counts the living. True when a count changed.
        bool ServerRecount()
        {
            var all = Combatant.All;
            _serverSeen = all.Count;
            int alive = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsServerInitialized) continue;
                ServerName(c);
                if (!_serverDead.Contains(c.ObjectId)) alive++;
            }
            int total = alive + _serverDead.Count;
            if (alive == _serverAlive && total == _serverTotal) return false;
            _serverAlive = alive;
            _serverTotal = total;
            return true;
        }

        string ServerName(Combatant c)
        {
            if (_serverNames.TryGetValue(c.ObjectId, out string name)) return name;
            if (c.IsPlayer)
            {
                _serverPlayers++;
                name = _serverPlayers == 1 ? PlayerName : PlayerName + " " + _serverPlayers;
            }
            else
            {
                _serverDummies++;
                name = DummyName + _serverDummies;
            }
            _serverNames[c.ObjectId] = name;
            return name;
        }

        void OnServerDied(Combatant victim, DamageInfo info)
        {
            if (victim == null || !ServerActive || _serverOver) return;
            // Also names anyone who spawned this very frame.
            ServerRecount();
            if (!_serverDead.Add(victim.ObjectId)) return;
            ServerRecount();

            var killer = info.Attacker != null && info.Attacker != victim ? info.Attacker : null;
            int killerKills = 0;
            if (killer != null)
            {
                _serverKills.TryGetValue(killer.ObjectId, out killerKills);
                killerKills++;
                _serverKills[killer.ObjectId] = killerKills;
            }

            var entry = new KillEntry
            {
                KillerId = killer != null ? killer.ObjectId : -1,
                VictimId = victim.ObjectId,
                KillerName = killer != null ? ServerName(killer) : "",
                VictimName = ServerName(victim),
                WeaponId = info.WeaponId,
                Headshot = info.Cause == DamageCause.Shot && info.Zone == HitZone.Head,
                Cause = (byte)info.Cause,
                KillerKills = killerKills,
                VictimPlacement = ServerVictimPlacement(victim),
                Alive = _serverAlive,
                Total = _serverTotal,
            };
            // The numbers first, so the screen that the entry opens has them.
            ServerSendStats(victim);
            ServerBroadcast(entry);

            // Solo: one left; team modes: one team left (ADR-072).
            if (ServerDecided(out var winner))
            {
                _serverOver = true;
                _serverWinnerId = winner != null ? winner.ObjectId : -1;
                _serverWinnerName = ServerWinnerName(winner);
                var all = Combatant.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].IsServerInitialized && !_serverDead.Contains(all[i].ObjectId)) ServerSendStats(all[i]);
            }
            ServerSendStatus(null);
        }

        void OnServerDamaged(Combatant victim, DamageInfo info, float applied)
        {
            var by = info.Attacker;
            if (by == null || by == victim || !ServerActive || _serverOver) return;
            _serverDamage.TryGetValue(by.ObjectId, out float sum);
            _serverDamage[by.ObjectId] = sum + applied;
        }

        void ServerSendStats(Combatant c) =>
            ServerBroadcast(new PlayerStats { Id = c.ObjectId, Damage = ServerDamageOf(c), Seconds = Time.time - _serverStart, Meters = _serverMeters.TryGetValue(c.ObjectId, out float m) ? m : 0f });

        Combatant ServerLastAlive()
        {
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && c.IsServerInitialized && !_serverDead.Contains(c.ObjectId)) return c;
            }
            return null;
        }

        // To one connection, or (null) to everyone who is authenticated; the others get it once they have loaded.
        void ServerSendStatus(NetworkConnection only)
        {
            if (!ServerActive) return;
            var status = new MatchStatus
            {
                Alive = _serverAlive,
                Total = _serverTotal,
                Over = _serverOver,
                WinnerId = _serverWinnerId,
                WinnerName = _serverWinnerName,
                WinnerTeam = _serverWinnerTeam,
            };
            if (only != null)
            {
                if (only.IsAuthenticated) _network.ServerManager.Broadcast(only, status);
                return;
            }
            foreach (var connection in _network.ServerManager.Clients.Values)
                if (connection.IsAuthenticated) _network.ServerManager.Broadcast(connection, status);
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !ServerActive) return;
            // Everyone hears about a changed count; otherwise only the newcomer needs catching up.
            ServerSendStatus(ServerRecount() ? null : connection);
        }

        void OnRestartRequest(NetworkConnection connection, MatchRestartRequest request, Channel channel)
        {
            if (!ServerActive || MatchDirector.Instance == null) return;
            // Anyone may ask once the match is decided; before that only a player who is out.
            bool allowed = _serverOver;
            var all = Combatant.All;
            for (int i = 0; i < all.Count && !allowed; i++)
            {
                var c = all[i];
                allowed = c != null && c.IsPlayer && c.Owner == connection && _serverDead.Contains(c.ObjectId);
            }
            if (allowed) MatchDirector.Instance.Restart();
        }

        // ---- Clients ----

        // From the result screen: a fresh match. The host restarts directly, a remote client asks the server.
        public void RequestRestart()
        {
            if (ServerActive)
            {
                if (MatchDirector.Instance != null) MatchDirector.Instance.Restart();
                return;
            }
            if (_network != null && _network.IsClientStarted) _network.ClientManager.Broadcast(new MatchRestartRequest());
        }

        void OnKillEntry(KillEntry entry, Channel channel)
        {
            if (entry.KillerId >= 0) _kills[entry.KillerId] = entry.KillerKills;
            _placements[entry.VictimId] = entry.VictimPlacement;
            _deaths[entry.VictimId] = entry;
            _names[entry.VictimId] = entry.VictimName;
            if (entry.KillerId >= 0) _names[entry.KillerId] = entry.KillerName;
            AliveCount = entry.Alive;
            TotalCount = entry.Total;
            if (_feed.Count == FeedCapacity) _feed.RemoveAt(0);
            _feed.Add(entry);
            KillsPosted++;
            KillPosted?.Invoke(entry);
            Changed?.Invoke();
        }

        void OnMatchStatus(MatchStatus status, Channel channel)
        {
            bool winnerPlaced = !status.Over || status.WinnerId < 0 || PlacementOfId(status.WinnerId) == 1;
            if (status.Alive == AliveCount && status.Total == TotalCount && status.Over == IsOver && status.WinnerId == WinnerId && status.WinnerTeam == WinnerTeam && winnerPlaced) return;
            AliveCount = status.Alive;
            TotalCount = status.Total;
            IsOver = status.Over;
            WinnerId = status.WinnerId;
            WinnerName = status.WinnerName ?? "";
            if (status.Over && status.WinnerId >= 0) _placements[status.WinnerId] = 1;
            if (status.Over) ApplyWinnerTeam(status.WinnerTeam);
            Changed?.Invoke();
        }

        void OnPlayerStats(PlayerStats stats, Channel channel)
        {
            _stats[stats.Id] = stats;
            Changed?.Invoke();
        }

        int PlacementOfId(int objectId) => _placements.TryGetValue(objectId, out int placement) ? placement : 0;
    }
}

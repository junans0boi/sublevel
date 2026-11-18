using System;
using FishNet;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Net
{
    // A dedicated server (#168, ADR-127): `--dedicated` on the command line of a normal standalone build run with
    // -batchmode -nographics. It has no window and no local player: it opens the lobby on --port (the guests join
    // it from the main menu as usual), starts the match when everyone is ready or the start timer ran out, runs the
    // match on port+1, and goes back to the lobby when the match is decided, empty, or never reached.
    // Everything it does is logged with a "[Server]" prefix; SIGTERM / SIGINT / closing quits cleanly.
    public sealed class DedicatedServer : MonoBehaviour
    {
        enum StateKind { Lobby, Loading, Match, Returning }

        const string IdleScene = "Bootstrap";
        const string MatchScene = "World";

        public static DedicatedServer Instance { get; private set; }

        ServerOptions _options;
        LobbyNet _lobby;
        StateKind _state;
        float _lobbyRetryAt;
        float _firstJoinAt = -1f;
        float _matchStartedAt;
        bool _everHadPeople;
        float _emptySince = -1f;
        float _overSince = -1f;
        int _lastPeople;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var options = ServerArgs.Parse(Environment.GetCommandLineArgs());
            if (!options.Dedicated) return;
            if (options.Error != null)
            {
                Debug.LogError("[Server] " + options.Error);
                Application.Quit(2);
                return;
            }
            var go = new GameObject("DedicatedServer");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DedicatedServer>();
            Instance._options = options;
        }

        void Start()
        {
            if (_options.Participants > 0)
            {
                // The match is this big; bots fill what people leave empty (BotFill, ADR-241).
                var rules = MatchRules.Current;
                rules.Participants = _options.Participants;
                rules.MaxBots = _options.Participants - 1;
                rules.BotCount = _options.Participants - 1;
            }
            if (_options.MaxPlayers > 0) MatchRules.Current.MaxPlayers = _options.MaxPlayers;
            MatchRules.Current.MaxPlayers = Math.Min(MatchRules.Current.MaxPlayers, MatchRules.Current.MaxHumans);
            ServerPerfProbe.GiveKeys = _options.PerfKeys;
            if (_options.PerfSquad) TeamMatch.Mode = TeamMode.Squad;
            ServerPerfProbe.Seed = _options.PerfSeed;
            ServerPerfProbe.Level = _options.PerfLevel;
            if (_options.PerfSeed >= 0) { MatchDirector.SeedOverride = _options.PerfSeed; MatchDirector.DeploymentSeedOverride = _options.PerfSeed; MatchDirector.LockdownSeedOverride = _options.PerfSeed; }
            if (_options.PerfLevel >= 0) { BotLevels.Level = (BotDifficulty)_options.PerfLevel; BotMix.Pure = true; }
            if (_options.PerfQuick > 1f) PollutionDirector.RulesOverride = PollutionRules.QuickMatch(_options.PerfQuick);
            if (_options.Perf != null) { { ServerPerfProbe.GatherX = _options.GatherX; ServerPerfProbe.GatherZ = _options.GatherZ; } ServerPerfProbe.Start(_options.Perf); LobbyNet.AllowEmptyStart = true; }
            PollutionDirector.FinalRegionOverride = _options.Region;
            ResultsReporter.Start();
            Application.runInBackground = true;
            Application.targetFrameRate = 60; // headless, but the tick should not spin a core
            Debug.Log($"[Server] starting: port {_options.Port}, bots {(_options.Bots < 0 ? "auto" : _options.Bots.ToString())}, " +
                      $"region {(_options.Region ?? "random")}, max players {MatchRules.Current.MaxPlayers}, start timer {_options.StartSeconds}s, version {LobbyNet.CurrentVersion}");
            // Whatever scene the build opened with (the main menu) is not needed here.
            if (SceneManager.GetActiveScene().name != IdleScene && Application.CanStreamedLevelBeLoaded(IdleScene)) SceneManager.LoadScene(IdleScene);
            _lobbyRetryAt = 0f;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        float _lobbyOpenedAt;

        float _nextPump;

        void Update()
        {
            if (Time.unscaledTime >= _nextPump) { _nextPump = Time.unscaledTime + 5f; ResultsReporter.Pump(); }
            switch (_state)
            {
                case StateKind.Lobby: TickLobby(); break;
                case StateKind.Match: TickMatch(); break;
                case StateKind.Returning: TickReturning(); break;
            }
        }

        // ---- Lobby ----

        void TickLobby()
        {
            if (_lobby == null)
            {
                if (Time.unscaledTime < _lobbyRetryAt) return;
                OpenLobby();
                return;
            }
            if (_lobby.Phase == LobbyNet.PhaseKind.Failed)
            {
                Debug.LogWarning("[Server] lobby failed: " + _lobby.FailureText + " (retrying)");
                CloseLobby();
                _lobbyRetryAt = Time.unscaledTime + 2f;
                return;
            }
            if (_lobby.HasStarted) return;
            if (_lobby.QueueActive) return; // somebody asked for a match: the queue's window decides, not the ready/timer flow
            int people = _lobby.PlayerCount;
            if (people != _lastPeople) { Debug.Log($"[Server] lobby: {people} / {_lobby.MaxPlayers} players"); _lastPeople = people; }
            if (people == 0 && _options.Perf != null && Time.unscaledTime - _lobbyOpenedAt > 5f)
            {
                Debug.Log("[Server] --perf: starting a bots-only match");
                _lobby.StartMatch(true);
                return;
            }
            if (people == 0) { _firstJoinAt = -1f; return; }
            if (_firstJoinAt < 0f) _firstJoinAt = Time.unscaledTime;
            if (_lobby.CanStart)
            {
                Debug.Log("[Server] everyone is ready, starting");
                _lobby.StartMatch();
            }
            else if (ServerRules.ShouldAutoStart(_options.StartSeconds, Time.unscaledTime - _firstJoinAt, people))
            {
                Debug.Log("[Server] start timer over, starting without waiting for the rest");
                _lobby.StartMatch(true);
            }
        }

        void OpenLobby()
        {
            _lobby = gameObject.AddComponent<LobbyNet>();
            _lobby.MatchStarting += OnMatchStarting;
            _lobbyOpenedAt = Time.unscaledTime;
            _firstJoinAt = -1f;
            _lastPeople = 0;
            if (_lobby.StartDedicated(_options.Port))
            {
                if (_options.Bots >= 0) _lobby.SetBots(_options.Bots);
                Debug.Log($"[Server] lobby open on port {_options.Port} (bots {(_options.Bots >= 0 ? _options.Bots.ToString() : "auto")}), the match will use port {_options.Port + 1} or the next free one");
            }
        }

        void CloseLobby()
        {
            if (_lobby == null) return;
            _lobby.MatchStarting -= OnMatchStarting;
            _lobby.Shutdown();
            Destroy(_lobby);
            _lobby = null;
        }

        // ---- Match ----

        void OnMatchStarting()
        {
            var start = _lobby.StartInfo;
            if (_lobby.StartMode != TeamMode.Solo || _lobby.QueueMode != TeamMode.Solo) TeamMatch.Mode = _lobby.StartMode;
            Debug.Log($"[Server] match starting: {start.Players} players, {start.Bots} bots, port {start.MatchPort}");
            NetSession.Begin(_lobby);
            NetSession.Dedicated = true;
            CloseLobby();
            _state = StateKind.Loading;
            _matchStartedAt = Time.unscaledTime;
            _everHadPeople = false;
            _emptySince = -1f;
            _overSince = -1f;
            SceneManager.LoadScene(MatchScene);
            _state = StateKind.Match;
        }

        void TickMatch()
        {
            var director = MatchDirector.Instance;
            var network = InstanceFinder.NetworkManager;
            if (director == null || network == null || !network.IsServerStarted) return;
            float now = Time.unscaledTime;
            int clients = network.ServerManager.Clients.Count;
            int held = director.Sessions != null ? director.Sessions.HeldCount : 0;
            bool people = clients > 0 || held > 0;
            if (people && !_everHadPeople) Debug.Log("[Server] match running, first player connected");
            _everHadPeople |= people;
            if (people) _emptySince = -1f;
            else if (_everHadPeople && _emptySince < 0f) { _emptySince = now; Debug.Log("[Server] match is empty"); }
            if (director.State != null && director.State.ServerOver && _overSince < 0f)
            {
                _overSince = now;
                Debug.Log("[Server] match decided");
            }
            // A measurement run has nobody to wait for: it lasts until the match is decided.
            if (_options.Perf != null && _overSince < 0f) return;
            if (ServerRules.ShouldEndMatch(now - _matchStartedAt, _everHadPeople, _emptySince >= 0f ? now - _emptySince : 0f, _overSince >= 0f ? now - _overSince : 0f))
                EndMatch();
        }

        void EndMatch()
        {
            Debug.Log("[Server] match over, back to the lobby");
            NetSession.Reset();
            _state = StateKind.Returning;
            SceneManager.LoadScene(IdleScene);
            _lobbyRetryAt = Time.unscaledTime + 1f;
        }

        void TickReturning()
        {
            // The match scene (its NetworkManager and sockets) is gone once the idle scene is active.
            if (SceneManager.GetActiveScene().name != IdleScene || Time.unscaledTime < _lobbyRetryAt) return;
            _state = StateKind.Lobby;
        }

        // ---- Shutdown ----

        void OnApplicationQuit()
        {
            Debug.Log("[Server] shutting down");
            CloseLobby();
            var network = InstanceFinder.NetworkManager;
            if (network != null) network.ServerManager.StopConnection(true);
        }
    }
}

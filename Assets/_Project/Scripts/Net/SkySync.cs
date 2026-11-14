using System;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server -> clients: what the sky of this match is (ADR-131). Everything else (the hour now, the weather, the rain)
    // is derived from these four numbers with WorldSky, so every peer sees the same sky.
    public struct SkyState : IBroadcast
    {
        public int Seed;
        public byte TimeChoice;   // DayTimeChoice the server's settings gave
        public byte WeatherChoice;
        public float ElapsedSeconds; // match time on the server when this was sent (a late joiner continues from there)
    }

    // Time of day and weather under server authority (#164, ADR-131): the server picks the seed and the choices when
    // it starts (its own settings, or a random match), clients wait for the broadcast; a client that joins late gets
    // the same state with the match time already run. Like DoorSystem it needs no network object.
    // Game.Gameplay's EnvironmentView reads Known / HourNow / Kind and applies them.
    public class SkySync : MonoBehaviour
    {
        public struct Choice
        {
            public DayTimeChoice Time;
            public WeatherChoice Weather;
            public int? Seed;
        }

        public static SkySync Instance { get; private set; }

        // Set by Game.Gameplay (the settings live above this assembly): what the server's settings ask for. Without
        // it the sky is random.
        public static Func<Choice> ChoiceSource;

        NetworkManager _network;
        bool _bound;
        double _origin; // this peer's clock at match time 0

        public bool Known { get; private set; }
        public int Seed { get; private set; }
        public DayTimeChoice TimeChoice { get; private set; }
        public WeatherChoice WeatherChoice { get; private set; }

        public float StartHour => WorldSky.StartHour(TimeChoice, Seed);
        public WeatherKind Kind => WorldSky.PickWeather(WeatherChoice, Seed);
        public float ElapsedSeconds => Known ? (float)(Time.timeAsDouble - _origin) : 0f;
        public float HourNow => WorldSky.HourAt(StartHour, ElapsedSeconds, WorldSky.ClockRuns(TimeChoice));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<SkyState>(OnState);
            _network.ServerManager.OnServerConnectionState -= OnServerState;
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<SkyState>(OnState);
            _network.ServerManager.OnServerConnectionState += OnServerState;
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
        }

        bool ServerActive => _network != null && _network.IsServerStarted;

        // Server: pick the sky. Tests may call it with chosen values.
        public void ServerPick(DayTimeChoice time, WeatherChoice weather, int seed)
        {
            Seed = seed;
            TimeChoice = time;
            WeatherChoice = weather;
            _origin = Time.timeAsDouble;
            Known = true;
        }

        void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started || Known) return;
            var choice = ChoiceSource != null ? ChoiceSource() : default;
            int seed = choice.Seed ?? UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            ServerPick(choice.Time, choice.Weather, seed);
        }

        // A client that finished loading is told the state with the clock already run.
        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !ServerActive || !Known || connection == null || !connection.IsAuthenticated) return;
            _network.ServerManager.Broadcast(connection, new SkyState
            {
                Seed = Seed, TimeChoice = (byte)TimeChoice, WeatherChoice = (byte)WeatherChoice, ElapsedSeconds = ElapsedSeconds,
            });
        }

        // The host knows it already (it picked it).
        void OnState(SkyState state, Channel channel)
        {
            if (ServerActive) return;
            Seed = state.Seed;
            TimeChoice = (DayTimeChoice)state.TimeChoice;
            WeatherChoice = (WeatherChoice)state.WeatherChoice;
            _origin = Time.timeAsDouble - state.ElapsedSeconds;
            Known = true;
        }
    }
}

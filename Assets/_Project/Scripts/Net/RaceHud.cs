using FishNet.Managing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Client: the event race as the server tells it (ADR-514/516/519, #445): the phase and countdown (drawn by Gameplay's RaceHudView),
    // the input lock before GO (SpawnProtection.InputBlocked reads InputLocked), and the R reset of the local driver's car onto the nearest
    // point of the course facing the way of the race (3 s apart; on its own after 5 s upside down or off the course). Nothing here decides
    // the race.
    public sealed class RaceHud : MonoBehaviour
    {
        NetworkManager _nm;
        RaceStandings _last;
        float _at = -10f, _goShownUntil, _nextReset, _stuckSince = -1f;
        RacePhase _phase = RacePhase.Waiting;
        static RaceHud _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _instance = null; }

        public static void Attach(NetworkManager nm)
        {
            if (nm == null || nm.GetComponent<RaceHud>() != null) return;
            var hud = nm.gameObject.AddComponent<RaceHud>();
            hud._nm = nm;
            nm.ClientManager.RegisterBroadcast<RaceStandings>(hud.OnStandings);
            _instance = hud;
        }

        void OnDestroy()
        {
            if (_nm != null) _nm.ClientManager.UnregisterBroadcast<RaceStandings>(OnStandings);
            if (_instance == this) _instance = null;
        }

        void OnStandings(RaceStandings message, FishNet.Transporting.Channel channel)
        {
            var phase = (RacePhase)message.Phase;
            if (phase == RacePhase.Racing && _phase == RacePhase.Countdown) _goShownUntil = Time.unscaledTime + 1.2f;
            _phase = phase; _last = message; _at = Time.unscaledTime;
        }

        public static RacePhase Phase => _instance != null ? _instance._phase : RacePhase.Waiting;
        // The last word from the server (the race order and the clocks in it) and how long ago it came.
        public static RaceStandings Last => _instance != null ? _instance._last : default;
        public static float SinceLast => _instance != null ? Time.unscaledTime - _instance._at : 0f;
        public static float LastAt => _instance != null ? _instance._at : -10f;   // changes with every new word: views rebuild their text then
        // Before GO in an event race the local player cannot drive (or do anything else).
        public static bool InputLocked => _instance != null && Vehicle.EventRace && (_instance._phase == RacePhase.Waiting || _instance._phase == RacePhase.Countdown);
        public static float CountdownNow => _instance == null ? 0f : Mathf.Max(0f, _instance._last.Countdown - (Time.unscaledTime - _instance._at));
        public static bool GoShowing => _instance != null && Time.unscaledTime < _instance._goShownUntil;

        void Update()
        {
            if (!Vehicle.EventRace || RaceDirector.Current == null) return;
            var me = Combatant.LocalPlayer;
            var car = me != null ? VehicleSeat.VehicleOf(me) : null;
            if (car == null || !car.Simulating || InputLocked) { _stuckSince = -1f; _goodS = float.NaN; return; }
            var course = RaceDirector.Current.Info.Course;
            var p = car.transform.position;
            _goodS = course.Follow(_goodS, p.x, p.y, p.z);
            bool fell = course.Fell(_goodS, p.y);   // off the edge of a hill road (#451)
            bool flipped = car.transform.up.y < 0.3f;
            bool off = Mathf.Abs(course.Project(p.x, p.z).offset) > course.Width * 0.5f + course.Runoff + 2f;
            if (fell || flipped || off) { if (_stuckSince < 0f) _stuckSince = Time.unscaledTime; } else _stuckSince = -1f;
            bool asked = _resetAsked; _resetAsked = false;
            bool stuck = _stuckSince >= 0f && Time.unscaledTime - _stuckSince >= (fell ? RaceEvent.FallResetSeconds : RaceEvent.StuckResetSeconds);
            if ((asked || stuck) && Time.unscaledTime >= _nextReset) ResetCar(car, course);
        }

        // R while driving in an event race (VehicleInput reads the key): the car goes back onto the course.
        public static void RequestReset() { if (_instance != null) _instance._resetAsked = true; }
        bool _resetAsked;
        float _goodS = float.NaN;   // where the car was last on a hill road (RaceCourse.Follow)

        public static void ResetCar(Vehicle car, RaceCourse course)
        {
            var p = car.transform.position;
            var (x, y, z, yaw) = course.ResetTo(p.x, p.z, _instance != null ? _instance._goodS : float.NaN);
            car.Place(new Vector3(x, y + 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
            if (_instance != null) { _instance._nextReset = Time.unscaledTime + RaceEvent.ResetCooldownSeconds; _instance._stuckSince = -1f; }
        }
    }
}

using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The car race of the event mode (ADR-512..519, #442): its circuit, grid and race numbers, and the race order. Pure. Coordinates are the
    // Race scene's metres (x east, z south), the start/finish line on the main straight at x = 0.
    public enum RacePhase : byte { Waiting, Countdown, Racing, Finishing, Results, Over }

    // The phases of one race (ADR-514), pure: the server ticks it with the clock, whether everyone is in, and whether every racer is done.
    public sealed class RaceFlow
    {
        readonly double _opened;
        double _countAt = -1, _firstFinishAt = -1, _resultsAt = -1;
        public RacePhase Phase { get; private set; } = RacePhase.Waiting;
        public double GoAt { get; private set; } = -1;

        public RaceFlow(double now) { _opened = now; }

        public bool InputLocked => Phase == RacePhase.Waiting || Phase == RacePhase.Countdown;
        public float CountdownLeft(double now) => Phase == RacePhase.Countdown ? (float)Math.Max(0.0, RaceEvent.CountdownSeconds - (now - _countAt)) : 0f;
        public float SecondsLeft(double now) => Phase == RacePhase.Finishing ? (float)Math.Max(0.0, RaceEvent.AfterWinnerSeconds - (now - _firstFinishAt))
            : Phase == RacePhase.Results ? (float)Math.Max(0.0, RaceEvent.ResultsSeconds - (now - _resultsAt)) : 0f;

        // Someone crossed the finish having done every lap.
        public void FirstFinish(double now) { if (_firstFinishAt < 0) _firstFinishAt = now; }

        public RacePhase Tick(double now, bool everyoneIn, bool everyoneDone)
        {
            switch (Phase)
            {
                case RacePhase.Waiting:
                    if (everyoneIn || now - _opened >= RaceEvent.WaitForLoadSeconds) { Phase = RacePhase.Countdown; _countAt = now; }
                    break;
                case RacePhase.Countdown:
                    if (now - _countAt >= RaceEvent.CountdownSeconds) { Phase = RacePhase.Racing; GoAt = now; }
                    break;
                case RacePhase.Racing:
                case RacePhase.Finishing:
                    if (_firstFinishAt < 0) break;
                    Phase = RacePhase.Finishing;
                    if (everyoneDone || now - _firstFinishAt >= RaceEvent.AfterWinnerSeconds) { Phase = RacePhase.Results; _resultsAt = now; }
                    break;
                case RacePhase.Results:
                    if (now - _resultsAt >= RaceEvent.ResultsSeconds) Phase = RacePhase.Over;
                    break;
            }
            return Phase;
        }
    }

    public static class RaceEvent
    {
        public const int Laps = 2;            // a lap is about 2.3 km, ~2.5 min
        public const int GridSlots = 12;            // two staggered columns behind the line
        public const int Racers = 8;               // people + bots filling the grid (ADR-514)
        public const float CountdownSeconds = 3f;
        public const float WaitForLoadSeconds = 10f;
        public const float AfterWinnerSeconds = 30f;
        public const float ResultsSeconds = 10f;
        public const float ResetCooldownSeconds = 3f;
        public const float StuckResetSeconds = 5f;

        public const float Width = 12f, Runoff = 3f, LineZ = 60f;   // narrow and tight like a kart/RC track: parallel legs 26 m apart

        // An original winding layout (no real track copied, ADR-513, after 준환's references: an RC track's packed switchbacks, a real
        // circuit's many changes of direction, a squiggly road): a long main straight north, a block of five hairpin switchbacks, a
        // twisting run through the middle, a fast sweeper north-east, the east fork (outer fast arc or inner V), the wavy south esses
        // and the final corner. Clockwise, north up.
        public static readonly (float x, float z, float r)[] Corners =
        {
            (0f, 200f, 20f), (0f, -190f, 16f),
            (40f, -190f, 12f), (40f, -100f, 12f), (66f, -100f, 12f), (66f, -190f, 12f), (92f, -190f, 12f), (92f, -100f, 12f),
            (118f, -100f, 12f), (118f, -190f, 12f), (144f, -190f, 12f),
            (150f, -60f, 16f), (70f, -30f, 16f), (60f, 40f, 18f), (150f, 55f, 18f), (175f, -10f, 14f), (215f, 15f, 14f),
            (230f, -110f, 22f), (300f, -180f, 28f), (370f, -150f, 22f), (420f, -40f, 45f), (370f, 70f, 22f),
            (330f, 135f, 18f), (280f, 100f, 18f), (230f, 150f, 18f), (180f, 110f, 18f), (130f, 160f, 18f), (80f, 125f, 18f), (45f, 200f, 20f),
        };
        // The inner cut of the east fork: leaves the main route before the outer arc and joins it after, a sharp V of its own (shorter,
        // slower at the apex: about as quick as the arc).
        public static readonly (float x, float z, float r)[] Fork = { (391.5f, -102.7f, 0f), (382f, -40f, 14f), (391.5f, 22.7f, 0f) };

        // Lap ~2.3 km; flat out on boost (29 m/s) with no corners it would still take ~79 s.
        public static readonly RaceCourse Course = new RaceCourse(Corners, Width, Runoff, 0f, LineZ, 16, 60f, Fork);

        public static List<(float x, float z, float yaw)> GridSpots() => Course.GridSpots(GridSlots);

        // The courses of the event, one scene each (ADR-513); the menu picks one, the scene's RaceDirector names its own.
        public sealed class CourseInfo
        {
            public string Id, Name, Scene; public RaceCourse Course; public int Laps;
        }
        public static readonly CourseInfo[] Courses =
        {
            new CourseInfo { Id = "snake", Name = "스네이크 서킷", Scene = "Race_Snake", Course = Course, Laps = Laps },
        };
        public static CourseInfo ByScene(string scene)
        {
            foreach (var c in Courses) if (c.Scene == scene) return c;
            return null;
        }
        // The scene a lobby's start message names (LobbyStart.Event, ADR-512): course n (1-based), null for the battle royale or an unknown n.
        public static string SceneForLobby(byte evt) => evt >= 1 && evt <= Courses.Length ? Courses[evt - 1].Scene : null;

        public static CourseInfo ById(string id)
        {
            foreach (var c in Courses) if (c.Id == id) return c;
            return null;
        }

        // ---- the race order (ADR-515) ----

        public struct Standing
        {
            public int Key;
            public bool Finished; public double FinishTime;   // finishers in the order they crossed
            public float Progress;                            // the rest by how far round the race (RaceLap.Progress)
        }

        public static int Compare(Standing a, Standing b)
        {
            if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
            if (a.Finished) return a.FinishTime.CompareTo(b.FinishTime);
            return b.Progress.CompareTo(a.Progress);
        }

        public static void Order(List<Standing> list) => list.Sort(Compare);
    }
}

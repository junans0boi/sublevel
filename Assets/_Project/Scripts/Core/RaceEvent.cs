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
        public const float FallResetSeconds = 1f;   // fallen off a hill road (#451): back on it after this

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

        // ---- course 3 "고개" (가칭, E7, #451, ADR-559): an original mountain pass, raced uphill or downhill ----

        // A two-lane road (two 3.5 m lanes and their edges) with a gravel shoulder to the guardrail. North up (z south): from the car park at
        // the foot (south) a run-up north, two sweepers, a block of four hairpins (each a pair of rounded corners 36 m apart, the legs
        // fanning out between them), two more sweepers and the summit straight to the car park at the top. No real road copied.
        public const float PassWidth = 9f, PassRunoff = 1f, PassGrade = 0.09f;
        public static readonly (float x, float z, float r)[] PassCorners =
        {
            (0f, 340f, 0f), (0f, 200f, 45f), (-90f, 120f, 35f), (-40f, 30f, 30f),
            (150f, 0f, 14f), (150f, -36f, 14f), (-150f, -70f, 14f), (-150f, -106f, 14f),
            (150f, -140f, 14f), (150f, -176f, 14f), (-150f, -210f, 14f), (-150f, -246f, 14f),
            (60f, -300f, 30f), (0f, -380f, 35f), (-200f, -430f, 0f),
        };
        public static readonly (float x, float z) PassFoot = (0f, 255f);                // uphill start, downhill finish: 85 m from the foot
        public static readonly (float x, float z) PassTop = (-112.7f, -408.2f);         // uphill finish, downhill start: 90 m from the top
        public static readonly RaceCourse PassUp = new RaceCourse(PassCorners, PassWidth, PassRunoff, PassFoot, PassTop, 12, 40f, PassGrade, true);
        public static readonly RaceCourse PassDown = new RaceCourse(Reversed(PassCorners), PassWidth, PassRunoff, PassTop, PassFoot, 12, 40f, PassGrade, false);

        // The pass loop: up the pass, on past the summit, round the north of the peak and back down the east side of the mountain — a
        // block of three hairpins (legs fanning east-west like the climb's), a run of esses, a long sweeper to the foot — one lap of about
        // 4.9 km, started at the foot's line (the uphill start). The pass's own corners in between, the ends left out (the loop's corners
        // beyond them round off past the pass's run-outs).
        public static readonly (float x, float z, float r)[] PassLoopReturn =
        {
            (-296f, -454f, 30f),   // on the line of the summit straight (0, -380) -> (-200, -430)
            (-260f, -640f, 40f), (120f, -700f, 50f), (260f, -560f, 30f),
            (460f, -540f, 14f), (460f, -504f, 14f), (250f, -470f, 14f), (250f, -434f, 14f), (460f, -400f, 14f), (460f, -364f, 14f),   // hairpins
            (330f, -250f, 35f), (420f, -120f, 35f), (320f, 20f, 35f), (400f, 160f, 35f),                                             // esses
            (340f, 330f, 35f), (150f, 470f, 35f), (0f, 450f, 30f),
        };
        public static readonly RaceCourse PassLoop = new RaceCourse(LoopCorners(), PassWidth, PassRunoff, PassFoot.x, PassFoot.z, 16, 100f, null, PassUp);

        static (float x, float z, float r)[] LoopCorners()
        {
            var list = new System.Collections.Generic.List<(float x, float z, float r)>(PassCorners);
            list.RemoveAt(list.Count - 1); list.RemoveAt(0);
            list.AddRange(PassLoopReturn);
            return list.ToArray();
        }

        static (float x, float z, float r)[] Reversed((float x, float z, float r)[] a) { var b = ((float, float, float)[])a.Clone(); Array.Reverse(b); return b; }

        // The courses of the event, one scene each (ADR-513); the menu picks one, the scene's RaceDirector names its own. The pass's two
        // directions share one scene: the course picked last (Chosen) tells its director which way.
        public sealed class CourseInfo
        {
            public string Id, Name, Scene; public RaceCourse Course; public int Laps;
        }
        public static readonly CourseInfo[] Courses =
        {
            new CourseInfo { Id = "snake", Name = "스네이크 서킷", Scene = "Race_Snake", Course = Course, Laps = Laps },
            new CourseInfo { Id = "pass_up", Name = "고개 업힐", Scene = "Race_Pass", Course = PassUp, Laps = 1 },
            new CourseInfo { Id = "pass_down", Name = "고개 다운힐", Scene = "Race_Pass", Course = PassDown, Laps = 1 },
            new CourseInfo { Id = "pass_loop", Name = "고개 순환", Scene = "Race_Pass", Course = PassLoop, Laps = 1 },
        };
        // The course the menu or the lobby is loading a race scene for (its Id); a scene's director takes it when the scene is its own.
        public static string Chosen;
        public const float FalseStartPenaltySeconds = 5f;   // an open course has no lap to take away: the time instead (ADR-559)
        public static CourseInfo ByScene(string scene)
        {
            foreach (var c in Courses) if (c.Scene == scene) return c;
            return null;
        }
        // The scene a lobby's start message names (LobbyStart.Event, ADR-512): course n (1-based), null for the battle royale or an unknown n.
        public static string SceneForLobby(byte evt) => evt >= 1 && evt <= Courses.Length ? Courses[evt - 1].Scene : null;
        // A sprint's line on the HUD (#451, ADR-559) in place of "LAP 1/1": the metres left to the finish and how far along (s from the
        // start line, held to the run).
        public static string SprintLine(float s, float length)
        {
            float done = Math.Max(0f, Math.Min(length, s));
            int left = (int)Math.Ceiling(length - done), pct = (int)(done / length * 100f);
            return "남은 " + left.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " m · " + pct + "%";
        }

        // A sprint's results column in place of the best lap: the average speed over the run.
        public static string AverageKmh(float length, float seconds) => seconds > 0f ? (length / seconds * 3.6f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " km/h" : "--";

        public static string IdForLobby(byte evt) => evt >= 1 && evt <= Courses.Length ? Courses[evt - 1].Id : null;

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

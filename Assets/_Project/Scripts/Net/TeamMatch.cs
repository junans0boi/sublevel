using System;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The team mode of the matches this process hosts (ADR-072). The main menu sets it before 시작; `-mode solo|duo|
    // squad` on the command line sets it for a launch (captures, autoplay). It stays for restarts and only a new play
    // session (or the menu) changes it, so tests that never touch it always get solo.
    public static class TeamMatch
    {
        public static TeamMode Mode { get; set; } = TeamMode.Solo;

        // Tests only: fill teams in arrival order (player + bot mates), the pre-ADR-332 layout, for tests that need teammates in a one-player match.
        public static bool FillTeamsInOrder { get; set; }

        public static bool IsTeamMode => Mode != TeamMode.Solo;
        public static int TeamSize => TeamRules.Current.SizeOf(Mode);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Mode = TeamMode.Solo;
            FillTeamsInOrder = false;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-mode" && Teams.TryParse(args[i + 1], out var mode)) Mode = mode;
        }
    }
}

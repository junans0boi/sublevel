using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay
{
    // `-scene <name>` on the command line starts the build in that scene instead of the first one (e.g. -scene ZoneD).
    // The first scene of a normal launch is MainMenu; the dev scene picker lives only here, not in the menu UI.
    // Runs once per process, so leaving a match back to the main menu is not redirected again.
    public static class StartScene
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-scene");
            if (i < 0 || i + 1 >= args.Length) return;
            var wanted = args[i + 1];
            if (SceneManager.GetActiveScene().name == wanted) return;
            if (!Application.CanStreamedLevelBeLoaded(wanted))
            {
                Debug.LogError("[StartScene] scene is not in the build: " + wanted);
                return;
            }
            SceneManager.LoadScene(wanted);
        }
    }
}

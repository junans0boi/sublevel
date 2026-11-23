using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Gives the pollution the map (T4): the island plan is generated from the same world.json the builder reads,
    // turned into the pollution grid once per process. Game.Net cannot see the world data, so the director asks
    // through PollutionDirector.GridSource (like ZoneWorld does for the lockdown). Server and clients build the
    // same grid from the same data; the snapshot hash tells if they ever differ.
    public static class PollutionWorld
    {
        const float CellMeters = 12f;
        static PollutionGrid _grid;
        static int _regions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _grid = null; _regions = 0; _pending = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => PollutionDirector.GridSource = Build;

        // #491: the first build took 1.6 s (Mono) on the main thread the frame after the match populated. It is pure Game.Core
        // work, so the menu starts it on a worker thread and Build only waits for what is left.
        static System.Threading.Tasks.Task<(PollutionGrid Grid, int Regions)> _pending;

        // Main thread (Resources). Nothing to do once the grid exists or is on its way.
        public static void Prepare()
        {
            if (_grid != null || _pending != null) return;
            var spec = LoadSpec();
            _pending = System.Threading.Tasks.Task.Run(() => Compute(spec));
        }

        static WorldSpec LoadSpec()
        {
            WorldSpec spec = null;
            var text = Resources.Load<TextAsset>("GameData/world");
            if (text != null) spec = JsonUtility.FromJson<WorldSpec>(text.text);
            if (spec == null || spec.Regions == null || spec.Regions.Length == 0) spec = WorldSpec.CreateDefault();
            return spec;
        }

        static (PollutionGrid, int) Compute(WorldSpec spec)
        {
            var plan = WorldGenerator.Generate(spec);
            return (PollutionGridBuilder.Build(new WorldPlanTerrain(plan), CellMeters), plan.Regions.Count);
        }

        static PollutionGrid Build()
        {
            if (_grid == null)
            {
                var pending = _pending;
                _pending = null;
                var built = pending != null ? pending.Result : Compute(LoadSpec());
                (_grid, _regions) = built;
            }
            PollutionDirector.MapRegionCount = _regions;
            return _grid;
        }
    }
}

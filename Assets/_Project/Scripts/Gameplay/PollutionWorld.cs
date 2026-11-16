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
        static void Reset() { _grid = null; _regions = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => PollutionDirector.GridSource = Build;

        static PollutionGrid Build()
        {
            if (_grid != null) { PollutionDirector.RegionCount = _regions; return _grid; }
            WorldSpec spec = null;
            var text = Resources.Load<TextAsset>("GameData/world");
            if (text != null) spec = JsonUtility.FromJson<WorldSpec>(text.text);
            if (spec == null || spec.Regions == null || spec.Regions.Length == 0) spec = WorldSpec.CreateDefault();
            var plan = WorldGenerator.Generate(spec);
            _grid = PollutionGridBuilder.Build(new WorldPlanTerrain(plan), CellMeters);
            _regions = plan.Regions.Count;
            PollutionDirector.RegionCount = _regions;
            return _grid;
        }
    }
}

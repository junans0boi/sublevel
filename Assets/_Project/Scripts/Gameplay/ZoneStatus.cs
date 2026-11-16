using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // What the HUD shows about the region the local player stands in (ADR-057 "지역 상태 카드"). Refreshed every
    // frame by ZoneBanner; Valid is false before the match has a lockdown or while the player is off the island.
    public struct ZoneStatus
    {
        public bool Valid;
        public int RegionIndex;
        public string RegionId;         // "A".."D"
        public string RegionName;       // Korean display name
        public ZoneState State;
        public float SecondsLeft;       // until this state ends; -1 when it does not (safe, closed)
        public bool HasExit;
        public int ExitPass;            // index into WorldInfo.Passes
        public Vector3 ExitDirection;   // unit vector on the ground from the player toward the nearest way out
        public float ExitDistance;      // metres
        public bool FinalRevealed;
        public string FinalRegionName;  // "" until revealed

        // Written by ZoneBanner; read by the HUD and the minimap.
        public static ZoneStatus Current;

        // 안전 / 경고 / 봉쇄 / 폭격 / 폐쇄
        public static string Label(ZoneState state)
        {
            switch (state)
            {
                case ZoneState.Warning: return "경고";
                case ZoneState.Lockdown: return "봉쇄";
                case ZoneState.Bombardment: return "폭격";
                case ZoneState.Closed: return "폐쇄";
                default: return "안전";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = default;
    }

    // What a camera or the render settings should do about radiation: a fog the local player sees while standing in a
    // region in lockdown or worse. Current is eased frame by frame by ZoneBanner; ZoneFogView applies it to the
    // render settings and restores the scene's fog afterwards.
    public struct ZoneFogState
    {
        public bool Active;     // Density is above the threshold: apply it
        public Color Colour;
        public float Density;   // exponential-squared density (RenderSettings.fogDensity units)
    }

    public static class ZoneFog
    {
        public const float ActiveAbove = 0.0002f;
        const float EaseDensityPerSecond = 0.004f;
        const float EaseColourPerSecond = 1.5f;

        public static ZoneFogState Current;

        // Where the fog is heading for the local player's region state.
        public static float TargetDensity(ZoneState state)
        {
            switch (state)
            {
                case ZoneState.Warning: return 0.0015f;
                case ZoneState.Lockdown: return 0.007f;
                case ZoneState.Bombardment: return 0.009f;
                case ZoneState.Closed: return 0.014f;
                default: return 0f;
            }
        }

        public static Color TargetColour(ZoneState state)
        {
            switch (state)
            {
                case ZoneState.Warning: return new Color(0.78f, 0.62f, 0.25f, 1f);
                case ZoneState.Lockdown: return new Color(0.55f, 0.68f, 0.22f, 1f);
                case ZoneState.Bombardment: return new Color(0.62f, 0.50f, 0.22f, 1f);
                case ZoneState.Closed: return new Color(0.70f, 0.30f, 0.18f, 1f);
                default: return Current.Colour;
            }
        }

        // One frame toward the target of a state (Safe when the player is out of any zone).
        public static void Step(ZoneState state, float deltaTime)
        {
            float target = TargetDensity(state);
            // Coming from no fog at all: start in the right colour instead of fading up from black.
            if (target > 0f && Current.Density <= 0f) Current.Colour = TargetColour(state);
            Current.Density = Mathf.MoveTowards(Current.Density, target, EaseDensityPerSecond * deltaTime);
            if (target > 0f)
                Current.Colour = Color.Lerp(Current.Colour, TargetColour(state), Mathf.Clamp01(EaseColourPerSecond * deltaTime));
            Current.Active = Current.Density > ActiveAbove;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = default;
    }

    // Gives the zone director the map: regions, passes and sub-zones as WorldInfo knows them. Game.Net cannot see
    // WorldInfo, so the director asks through PollutionDirector.GeometrySource.
    public static class ZoneWorld
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { PollutionDirector.GeometrySource = FromWorldInfo; }

        // Null until the world exists.
        public static ZoneGeometry FromWorldInfo()
        {
            var world = WorldInfo.Current;
            if (world == null || world.Regions.Count == 0) return null;
            var g = new ZoneGeometry { Size = world.Size, SeaLevel = world.SeaLevel };
            int n = world.Regions.Count;
            g.RegionId = new string[n]; g.RegionName = new string[n];
            g.CentreX = new float[n]; g.CentreZ = new float[n]; g.CentreY = new float[n];
            for (int i = 0; i < n; i++)
            {
                var r = world.Regions[i];
                g.RegionId[i] = r.Id; g.RegionName[i] = r.Name;
                g.CentreX[i] = r.Centre.x; g.CentreZ[i] = r.Centre.z; g.CentreY[i] = r.Centre.y;
            }
            int p = world.Passes.Count;
            g.PassA = new int[p]; g.PassB = new int[p];
            g.PassX = new float[p]; g.PassY = new float[p]; g.PassZ = new float[p]; g.PassYawDeg = new float[p]; g.PassWidth = new float[p];
            for (int i = 0; i < p; i++)
            {
                var pass = world.Passes[i];
                g.PassA[i] = pass.RegionA; g.PassB[i] = pass.RegionB;
                g.PassX[i] = pass.Position.x; g.PassY[i] = pass.Position.y; g.PassZ[i] = pass.Position.z;
                g.PassYawDeg[i] = pass.YawDeg; g.PassWidth[i] = pass.Width;
            }
            int s = world.SubZones.Count;
            g.SubRegion = new int[s];
            g.SubX = new float[s]; g.SubY = new float[s]; g.SubZ = new float[s]; g.SubRadius = new float[s];
            for (int i = 0; i < s; i++)
            {
                var z = world.SubZones[i];
                g.SubRegion[i] = z.Region; g.SubX[i] = z.Position.x; g.SubY[i] = z.Position.y; g.SubZ[i] = z.Position.z; g.SubRadius[i] = z.Radius;
            }
            return g;
        }
    }
}

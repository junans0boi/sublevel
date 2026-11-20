using System;

namespace Game.Core
{
    // How a footprint looks (alpha 0 = none).
    public struct FootprintLook
    {
        public float Alpha, LifeSeconds, Length, Width;
        public float Dark;   // 0 = lighter patch (pressed grass), 1 = dark print
    }

    // Ground reaction (#205, ADR-163). Pure numbers; GroundFx draws them.
    public static class GroundFxRules
    {
        public const int MaxPrints = 48, MaxRipples = 40;

        // Soft ground keeps a print, the rest (stone, metal, wood, deep water) none.
        public static FootprintLook Footprint(FootSurface surface, bool sprinting)
        {
            float run = sprinting ? 1.25f : 1f;
            switch (surface)
            {
                case FootSurface.Dirt:
                    return new FootprintLook { Alpha = 0.7f, LifeSeconds = 30f, Length = 0.36f * run, Width = 0.17f, Dark = 1f };
                case FootSurface.Grass:
                    return new FootprintLook { Alpha = 0.35f, LifeSeconds = 10f, Length = 0.38f * run, Width = 0.19f, Dark = 0.35f };
                default: return default;
            }
        }

        // Fade over the last third of the life; 0 once gone.
        public static float Fade(float age, float life) => life <= 0f || age >= life ? 0f : Math.Min(1f, (life - age) / (life * 0.33f));

        // Water rings when someone wades: more when moving fast, none when still.
        public static float WadeRingsPerSecond(float speed) => Math.Min(6f, speed * 1.6f);
    }

    public struct WildlifeLevels
    {
        public float Butterflies, Fireflies, LeavesPerSecond, BirdsFlying, FishSplashPerMinute;
    }

    // Small living things in the outdoor map (#206): how many of what, from the hour, weather and place (ties to AmbientRules.Day).
    public static class WildlifeRules
    {
        public const int MaxButterflies = 14, MaxFireflies = 40, MaxBirds = 5;

        public static WildlifeLevels Mix(float hour, WeatherKind weather, RegionKind region, bool indoors, float shore, float windBoost)
        {
            float day = AmbientRules.Day(hour), night = 1f - day;
            float outdoors = indoors ? 0f : 1f;
            float calm = weather == WeatherKind.Fog ? 0.4f : 1f;
            float green = region == RegionKind.Factory ? 0.1f : region == RegionKind.Town ? 0.4f : 1f;
            float wet = region == RegionKind.Wetland ? 1f : 0.45f;
            var o = new WildlifeLevels
            {
                Butterflies = MaxButterflies * day * calm * green * outdoors,
                Fireflies = MaxFireflies * night * calm * wet * green * outdoors,
                // Leaves come off the trees on the wind: more in weather and in the woods.
                LeavesPerSecond = (0.6f + 2.5f * windBoost) * green * outdoors,
                BirdsFlying = MaxBirds * day * calm * green * outdoors,
                FishSplashPerMinute = 5f * Math.Max(0f, Math.Min(1f, shore)) * (night < 0.5f ? 1f : 0.5f) * outdoors,
            };
            return o;
        }
    }
}

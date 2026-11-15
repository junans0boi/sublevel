using System;

namespace Game.Core
{
    // How a footprint looks (alpha 0 = none).
    public struct FootprintLook
    {
        public float Alpha, LifeSeconds, Length, Width;
        public float Dark;   // 0 = lighter patch (pressed grass), 1 = dark wet print
    }

    // Ground reaction and rain on the ground (#205 #207, ADR-163). Pure numbers; GroundFx and EnvironmentView draw them.
    public static class GroundFxRules
    {
        public const int MaxPrints = 48, MaxRipples = 40, MaxSplashesPerFrame = 6;
        public const float WetRiseSeconds = 45f, DryOffSeconds = 240f;

        // Soft ground keeps a print, wet hard ground a short dark one, the rest (metal, wood, deep water) none.
        public static FootprintLook Footprint(FootSurface surface, float wet, bool sprinting)
        {
            wet = Math.Max(0f, Math.Min(1f, wet));
            float run = sprinting ? 1.25f : 1f;
            switch (surface)
            {
                case FootSurface.Dirt:
                    return new FootprintLook { Alpha = 0.7f + 0.25f * wet, LifeSeconds = 30f + 30f * wet, Length = 0.36f * run, Width = 0.17f, Dark = 1f };
                case FootSurface.Grass:
                    return new FootprintLook { Alpha = 0.35f + 0.15f * wet, LifeSeconds = 10f, Length = 0.38f * run, Width = 0.19f, Dark = 0.35f };
                case FootSurface.Concrete:
                    return wet < 0.3f ? default : new FootprintLook { Alpha = 0.3f * wet, LifeSeconds = 5f, Length = 0.28f * run, Width = 0.12f, Dark = 1f };
                default: return default;
            }
        }

        // Fade over the last third of the life; 0 once gone.
        public static float Fade(float age, float life) => life <= 0f || age >= life ? 0f : Math.Min(1f, (life - age) / (life * 0.33f));

        // The wet-ground factor 0..1 moves toward 1 while it rains and back to 0 when it stops.
        public static float Wetness(float wet, bool raining, float dt)
        {
            wet += (raining ? 1f / WetRiseSeconds : -1f / DryOffSeconds) * dt;
            return Math.Max(0f, Math.Min(1f, wet));
        }

        // Ground splashes spawned per second around the listener: a small share of the drops, and none under a roof.
        public static float SplashesPerSecond(float rainPerSecond, float quality, bool indoors) => indoors ? 0f : rainPerSecond * 0.1f * quality;

        // Water rings when someone wades: more when moving fast, none when still.
        public static float WadeRingsPerSecond(float speed) => Math.Min(6f, speed * 1.6f);
    }

    // Lightning and thunder in rain (#207). The flash is instant, the thunder arrives after distance / speed of sound.
    public static class LightningRules
    {
        public const float SpeedOfSound = 343f, MinGap = 9f, MaxGap = 30f, FlashSeconds = 0.55f;

        public static float NextGap(float u) => MinGap + (MaxGap - MinGap) * Clamp(u);
        public static float Distance(float u) => 250f + 1900f * Clamp(u) * Clamp(u);
        public static float ThunderDelay(float distance) => distance / SpeedOfSound;
        // Near thunder is loud and sharp, far thunder soft and low.
        public static float ThunderVolume(float distance) => Clamp(1.15f - distance / 2200f);

        // Light added at `t` seconds after the strike: a strong flash, a dip, a second weaker one, then gone. 0..1.
        public static float Flash(float t)
        {
            if (t < 0f || t >= FlashSeconds) return 0f;
            if (t < 0.07f) return 1f;
            if (t < 0.16f) return 0.25f;
            if (t < 0.26f) return 0.7f * (1f - (t - 0.16f) / 0.1f * 0.4f);
            return 0.42f * (1f - (t - 0.26f) / (FlashSeconds - 0.26f));
        }

        static float Clamp(float u) => Math.Max(0f, Math.Min(1f, u));
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
            bool rain = weather == WeatherKind.Rain, fog = weather == WeatherKind.Fog;
            float outdoors = indoors ? 0f : 1f;
            float calm = rain ? 0f : fog ? 0.4f : 1f;
            float green = region == RegionKind.Factory ? 0.1f : region == RegionKind.Town ? 0.4f : 1f;
            float wet = region == RegionKind.Wetland ? 1f : 0.45f;
            var o = new WildlifeLevels
            {
                Butterflies = MaxButterflies * day * calm * green * outdoors,
                Fireflies = MaxFireflies * night * calm * wet * green * outdoors,
                // Leaves come off the trees on the wind: more in weather and in the woods.
                LeavesPerSecond = (0.6f + 2.5f * windBoost) * green * outdoors * (rain ? 0.4f : 1f),
                BirdsFlying = MaxBirds * day * calm * green * outdoors,
                FishSplashPerMinute = 5f * Math.Max(0f, Math.Min(1f, shore)) * (night < 0.5f ? 1f : 0.5f) * (rain ? 0f : 1f) * outdoors,
            };
            return o;
        }
    }
}

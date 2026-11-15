using System;

namespace Game.Core
{
    // Which environment beds are heard, and how loud, from where the listener is (#194, ADR-151). Pure numbers: the
    // Gameplay layer (AmbientBeds) owns the sound sources and the synthesis.
    public struct AmbientInput
    {
        public float Hour;              // 0..24
        public WeatherKind Weather;
        public RegionKind Region;       // the nearest region
        public bool Indoors;            // a roof overhead
        public float Shore;             // 0..1, how near the waterline is (low ground)
        public float Duck;              // 0..1, how loud gunfire has just been
    }

    public struct AmbientLevels
    {
        public float Wind, Crickets, Water, RoomTone;   // loop volumes 0..1
        public float BirdsPerMinute, BirdVolume;        // spatial one-shots
        public float WindBoost;                         // 0..1: foliage sway in rain and fog
    }

    public static class AmbientRules
    {
        // How much the beds drop at the loudest gunfire (a shot should cut through, not fight the wind).
        public const float MaxDuck = 0.65f;

        static float Smooth(float a, float b, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }

        // 0 at night .. 1 in daylight; dawn 5.5 to 7.5, dusk 18 to 20.
        public static float Day(float hour)
        {
            hour = WorldSky.Wrap(hour);
            return Smooth(5.5f, 7.5f, hour) * (1f - Smooth(18f, 20f, hour));
        }

        public static AmbientLevels Mix(in AmbientInput i)
        {
            float day = Day(i.Hour), night = 1f - day;
            bool rain = i.Weather == WeatherKind.Rain, fog = i.Weather == WeatherKind.Fog;
            float outdoors = i.Indoors ? 0f : 1f;

            // Wind: the mountain is the windiest, the town and factory sheltered; weather adds to it.
            float wind = i.Region == RegionKind.Mountain ? 0.7f : i.Region == RegionKind.Wetland ? 0.5f : 0.35f;
            wind += rain ? 0.2f : fog ? 0.05f : 0f;
            var o = new AmbientLevels { WindBoost = rain ? 0.65f : fog ? 0.25f : 0f };
            o.Wind = Math.Min(1f, wind) * (i.Indoors ? 0.2f : 1f);

            // Birds sing by day, less in the factory and not at all in rain; crickets take over at night and in the
            // wetland, they are silenced by rain.
            float birdy = i.Region == RegionKind.Factory ? 0.3f : i.Region == RegionKind.Town ? 0.6f : 1f;
            float calm = rain ? 0f : fog ? 0.5f : 1f;
            o.BirdsPerMinute = 10f * day * birdy * calm * outdoors;
            o.BirdVolume = 0.55f;
            float cricketRegion = i.Region == RegionKind.Wetland ? 1f : i.Region == RegionKind.Factory ? 0.35f : 0.75f;
            o.Crickets = 0.5f * night * cricketRegion * (rain ? 0.2f : 1f) * (i.Indoors ? 0.12f : 1f);

            o.Water = 0.7f * Math.Max(0f, Math.Min(1f, i.Shore)) * (i.Indoors ? 0.2f : 1f);
            o.RoomTone = i.Indoors ? 0.55f : 0f;

            float duck = 1f - MaxDuck * Math.Max(0f, Math.Min(1f, i.Duck));
            o.Wind *= duck; o.Crickets *= duck; o.Water *= duck; o.RoomTone *= duck;
            o.BirdVolume *= duck;
            return o;
        }
    }
}

// Value noise from a world position and the meadow's dryness, shared by the terrain (WorldTerrainPasses) and the grass blades
// (WorldFoliage) so the blades turn yellow where the ground does (ADR-284).
#ifndef WORLD_GROUND_TINT_INCLUDED
#define WORLD_GROUND_TINT_INCLUDED

float GroundHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

float GroundNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(GroundHash(i), GroundHash(i + float2(1, 0)), f.x), lerp(GroundHash(i + float2(0, 1)), GroundHash(i + float2(1, 1)), f.x), f.y);
}

// 0 lush .. 1 dry: patches a few hundred metres across, drier on the high ground.
half DryAmount(float2 xz, float y)
{
    float broad = GroundNoise(xz / 260.0 + 41.7);
    float mid = GroundNoise(xz / 60.0 + 17.1);
    float high = smoothstep(25.0, 90.0, y - 10.0);
    return (half)saturate(smoothstep(0.42, 0.78, broad) * 0.85 + (mid - 0.5) * 0.35 + high * 0.25);
}

#endif

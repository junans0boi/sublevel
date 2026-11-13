using System;
using System.Numerics;

namespace Game.Core
{
    // What the ground under a footstep sounds like (#120, ADR-092).
    public enum FootSurface { Concrete, Metal, Wood, Grass, Dirt, Water }

    // Sound cues (#120, ADR-092): footsteps by surface and gait, the two gunshot layers by distance, the bullet whiz.
    // Pure numbers; CombatantAudio plays them.
    public static class SoundRules
    {
        // ---- Footsteps ----
        // A standing run on concrete is heard this far (SpatialAudio's Body range); everything else scales from it.
        public const float RunStepRange = 40f;
        public const float MinStepRange = 3f;
        // PUBG ratios: a sprint carries further than a run; crouching and slow walking (CombatantStance's
        // FootstepVolumeFactor: crouch 0.4, prone 0.1, slow walk 0.25) cut it, so a crouched slow walk is ~1/10.
        public const float SprintStepFactor = 1.25f;

        // Hard and hollow floors ring louder, grass and soft dirt muffle.
        public static float SurfaceLoudness(FootSurface surface) => surface switch
        {
            FootSurface.Metal => 1.3f,
            FootSurface.Wood => 1.15f,
            FootSurface.Water => 1.2f,
            FootSurface.Grass => 0.75f,
            FootSurface.Dirt => 0.9f,
            _ => 1f,
        };

        // 1 = a standing run on concrete. stanceFactor is CombatantStance.FootstepVolumeFactor (stance x slow walk).
        public static float StepLoudness(float stanceFactor, bool sprinting, FootSurface surface) =>
            stanceFactor * (sprinting ? SprintStepFactor : 1f) * SurfaceLoudness(surface);

        // Metres at which a step of this loudness falls silent.
        public static float StepRange(float loudness) => Math.Max(MinStepRange, RunStepRange * loudness);

        // Sprinting: faster than halfway between a run and a sprint, at the stance's speed.
        public static bool IsSprint(float planarSpeed, float runSpeed, float sprintSpeed, float stanceMoveFactor) =>
            planarSpeed > 0.5f * (runSpeed + sprintSpeed) * stanceMoveFactor;

        // Resources folder of a surface's step clips. Concrete is the player prefab's own footstep set.
        public static string StepClips(FootSurface surface) => "Steps/" + surface.ToString().ToLowerInvariant();

        // A building's floor by kind, the same materials WorldBuilder paints (wood houses, concrete halls with
        // steel stairs and machines, concrete stations).
        public static FootSurface BuildingSurface(BuildingKind building, SurfaceKind piece)
        {
            switch (building)
            {
                case BuildingKind.House:
                    return piece == SurfaceKind.Roof || piece == SurfaceKind.WallOuter ? FootSurface.Concrete : FootSurface.Wood;
                case BuildingKind.Warehouse:
                case BuildingKind.FactoryHall:
                case BuildingKind.Shed:
                    return piece == SurfaceKind.Stairs || piece == SurfaceKind.Roof || piece == SurfaceKind.Prop ? FootSurface.Metal : FootSurface.Concrete;
                default:
                    return FootSurface.Concrete;
            }
        }

        // The terrain layer under the foot (WorldMaterials: World_Grass, World_Dirt, World_Rock, World_Mud). Water
        // only when the ground is below the sea surface: "Mud" is also the shore and every damp hollow of the
        // plains, not a pool. An unknown or missing layer is the neutral concrete set, never water.
        public static FootSurface TerrainSurface(string layerName, bool underWater)
        {
            if (underWater) return FootSurface.Water;
            var name = layerName ?? "";
            if (name.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0) return FootSurface.Grass;
            if (name.IndexOf("Dirt", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Mud", StringComparison.OrdinalIgnoreCase) >= 0) return FootSurface.Dirt;
            return FootSurface.Concrete; // rock and anything unmapped
        }

        // ---- Gunshots ----
        // The near "crack/body" layer is alone up to NearLayerFull m, the far "thump/echo" layer alone from
        // FarLayerFull m; equal-power crossfade between.
        public const float NearLayerFull = 30f, FarLayerFull = 200f;
        // Hook for suppressors (G7): a suppressed shot sounds like one this many times further away.
        public const float SuppressedDistanceFactor = 4f;

        public static float HeardDistance(float meters, bool suppressed) => suppressed ? meters * SuppressedDistanceFactor : meters;

        public static void ShotLayers(float heardMeters, out float near, out float far)
        {
            float t = (heardMeters - NearLayerFull) / (FarLayerFull - NearLayerFull);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            near = (float)Math.Cos(t * Math.PI * 0.5);
            far = (float)Math.Sin(t * Math.PI * 0.5);
            if (near < 1e-4f) near = 0f;
            if (far < 1e-4f) far = 0f;
        }

        // ---- Bullet whiz ----
        // A round passing within WhizRadius m of the listener whizzes, unless the listener is the shooter (closer
        // than WhizMinFromShooter m to the muzzle) or the round stopped before passing (the hit is its own sound).
        public const float WhizRadius = 3f, WhizMinFromShooter = 6f;

        public static bool Whiz(Vector3 origin, Vector3 end, Vector3 listener, out Vector3 passing)
        {
            passing = origin;
            if (Vector3.Distance(origin, listener) < WhizMinFromShooter) return false;
            var path = end - origin;
            float lengthSq = path.LengthSquared();
            if (lengthSq < 1e-6f) return false;
            float t = Vector3.Dot(listener - origin, path) / lengthSq;
            if (t <= 0f || t >= 1f) return false;
            passing = origin + path * t;
            return Vector3.Distance(passing, listener) <= WhizRadius;
        }
    }
}

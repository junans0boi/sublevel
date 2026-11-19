using System;

namespace Game.Core
{
    // Spent cases (#199): what a weapon throws out of its ejection port and when, how many may lie about, and how one
    // sounds landing on each surface. Pure numbers; CasingFx shows them and plays WeaponSounds.Casing clips.
    public enum CasingKind { Pistol, Rifle, Magnum, Shell }

    // OnShot: straight out with the shot. Bolt: a bolt action throws it out when the bolt is worked, a moment after the
    // shot. OnReload: a break-action shotgun drops its spent shells when it is opened to reload.
    public enum EjectTiming { OnShot, Bolt, OnReload }

    public struct CasingSpec
    {
        public CasingKind Kind;
        public EjectTiming Timing;
        public float DelaySeconds;
    }

    public struct CasingSound
    {
        public float Gain, Pitch, CutoffHz; // Gain 0 = silent (water swallows it)
    }

    public static class CasingRules
    {
        // Live cases at once (oldest reused first) at high and low effects, how long one lies, how near the camera a
        // shooter must be for its cases to be made at all, and how far a case is heard.
        public const int MaxLiveHigh = 40, MaxLiveLow = 12;
        public const float LifeSeconds = 3.5f, ShrinkSeconds = 0.3f, ShowWithinMeters = 35f, HeardMeters = 15f;
        public const float BoltDelaySeconds = 0.55f;
        public const int MaxBounces = 2;
        // Break-action shotguns (the two-barrelled Duo).
        static readonly int[] BreakAction = { 16 };

        public static bool TryGet(int weaponId, out CasingSpec spec)
        {
            spec = default;
            if (!WeaponSounds.TryGet(weaponId, out var gun)) return false;
            spec.Kind = gun.Class switch
            {
                GunClass.Pistol or GunClass.Smg => CasingKind.Pistol,
                GunClass.Sniper => CasingKind.Magnum,
                GunClass.Shotgun => CasingKind.Shell,
                _ => CasingKind.Rifle,
            };
            if (Array.IndexOf(BreakAction, weaponId) >= 0) spec.Timing = EjectTiming.OnReload;
            else if (gun.Class == GunClass.Sniper) { spec.Timing = EjectTiming.Bolt; spec.DelaySeconds = BoltDelaySeconds; }
            return true;
        }

        // Diameter and length (m) of a case, roughly 9 mm, 5.56, .308 and a 12-gauge hull.
        public static void Size(CasingKind kind, out float diameter, out float length)
        {
            switch (kind)
            {
                case CasingKind.Pistol: diameter = 0.0099f; length = 0.019f; break;
                case CasingKind.Magnum: diameter = 0.012f; length = 0.051f; break;
                case CasingKind.Shell: diameter = 0.02f; length = 0.06f; break;
                default: diameter = 0.0096f; length = 0.045f; break;
            }
        }

        // How high the axis of a case lying on its side sits over the ground (its radius).
        public static float RestLift(CasingKind kind) { Size(kind, out float d, out _); return d * 0.5f; }

        // Ejection speed (m/s) out to the right and up; shells drop out lazily, rifles throw hard.
        public static void Throw(CasingKind kind, out float right, out float up)
        {
            switch (kind)
            {
                case CasingKind.Shell: right = 1.1f; up = 0.9f; break;
                case CasingKind.Pistol: right = 1.8f; up = 1.6f; break;
                default: right = 2.4f; up = 1.4f; break;
            }
        }

        // A brass case rings on hard ground, ticks dully on wood, is barely heard in grass, sand or mud, and is swallowed
        // by water. A plastic shotgun hull is lower and duller everywhere.
        public static CasingSound Sound(SurfaceId surface, CasingKind kind)
        {
            CasingSound s = surface switch
            {
                SurfaceId.Water => new CasingSound { Gain = 0f, Pitch = 1f, CutoffHz = 22000f },
                SurfaceId.Grass or SurfaceId.Dirt or SurfaceId.Mud or SurfaceId.Sand or SurfaceId.Carpet =>
                    new CasingSound { Gain = 0.3f, Pitch = 0.9f, CutoffHz = 2500f },
                SurfaceId.Wood or SurfaceId.Deck => new CasingSound { Gain = 0.75f, Pitch = 0.85f, CutoffHz = 6000f },
                SurfaceId.Metal or SurfaceId.MetalGrate or SurfaceId.MetalStairs or SurfaceId.TinRoof =>
                    new CasingSound { Gain = 1.1f, Pitch = 1.08f, CutoffHz = 22000f },
                SurfaceId.Gravel => new CasingSound { Gain = 0.6f, Pitch = 0.95f, CutoffHz = 9000f },
                _ => new CasingSound { Gain = 1f, Pitch = 1f, CutoffHz = 22000f }, // concrete, asphalt, tile, rock, glass
            };
            if (kind == CasingKind.Shell) { s.Gain *= 0.7f; s.Pitch *= 0.62f; s.CutoffHz = Math.Min(s.CutoffHz, 4000f); }
            else if (kind == CasingKind.Magnum) s.Pitch *= 0.88f;
            return s;
        }

        // ---- Ricochets (#288) ----
        // Share of misses that whine off what they hit: hard, angled surfaces most; soft ground, wood, glass and bodies never.
        public static float RicochetChance(BulletSurface surface) => surface switch
        {
            BulletSurface.Metal => 0.6f,
            BulletSurface.Vehicle => 0.45f,
            BulletSurface.Concrete => 0.3f,
            BulletSurface.Ground => 0.08f,
            _ => 0f,
        };
    }
}

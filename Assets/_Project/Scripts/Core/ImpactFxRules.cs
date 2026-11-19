using System;

namespace Game.Core
{
    // Client-only hit effects (ADR-470, #197 #198): nothing here changes what the server decides. Pure, for edit-mode tests.

    // A fixed ring of decals: the oldest is reused first, each lives `life` seconds and fades out over the last `fade`.
    public sealed class DecalRing
    {
        public readonly int Capacity;
        public readonly float Life, Fade;
        readonly float[] _born;
        int _next;

        public DecalRing(int capacity, float life, float fade)
        {
            Capacity = Math.Max(1, capacity);
            Life = life;
            Fade = Math.Max(1e-3f, Math.Min(fade, life));
            _born = new float[Capacity];
            for (int i = 0; i < Capacity; i++) _born[i] = float.NegativeInfinity;
        }

        // The slot the new decal takes.
        public int Add(float now)
        {
            int i = _next;
            _next = (_next + 1) % Capacity;
            _born[i] = now;
            return i;
        }

        // 1 while fresh, falling to 0 over the last `Fade` seconds; 0 for a free or expired slot.
        public float Alpha(int slot, float now)
        {
            float age = now - _born[slot];
            if (float.IsNaN(age) || age < 0f || age >= Life) return 0f;
            float left = Life - age;
            return left >= Fade ? 1f : left / Fade;
        }

        public int Live(float now)
        {
            int n = 0;
            for (int i = 0; i < Capacity; i++) if (Alpha(i, now) > 0f) n++;
            return n;
        }
    }

    // The 피 효과 setting (ADR-470): off for younger players or taste, 적게 = a short spray and floor drops only.
    public enum BloodLevel : byte { Off = 0, Low = 1, Normal = 2 }

    public static class BloodRules
    {
        public const float WallReach = 2.2f;     // m behind the hit a wall still catches the spray
        public const float FloorReach = 2.5f;    // m below the hit the floor is looked for
        public const float TrailSeconds = 6f;    // a hit body drips this long after the last hit
        public const float DripEverySeconds = 0.7f;
        public const float DripMinMeters = 0.35f; // a standing body does not stack drops on one spot

        public static int SprayParticles(BloodLevel level, bool head, bool highEffects)
        {
            if (level == BloodLevel.Off) return 0;
            int n = head ? 12 : 8;
            if (level == BloodLevel.Low) n /= 2;
            if (!highEffects) n /= 2;
            return Math.Max(1, n);
        }

        public static bool WallSplatter(BloodLevel level) => level == BloodLevel.Normal;
        public static bool FloorSplat(BloodLevel level) => level != BloodLevel.Off;

        // Knocked bodies bleed out (ADR-072); a hit one drips for a while.
        public static bool Drips(BloodLevel level, bool knocked, float secondsSinceHit) =>
            level != BloodLevel.Off && (knocked || secondsSinceHit < TrailSeconds);
    }

    // What flies off a surface a bullet hits (#198).
    public enum Debris : byte { None = 0, ConcreteChunks = 1, WoodChips = 2, DirtClods = 3, GlassShards = 4, Sparks = 5, WaterSplash = 6 }

    public static class DebrisRules
    {
        public static Debris Of(BulletSurface s)
        {
            switch (s)
            {
                case BulletSurface.Ground: return Debris.DirtClods;
                case BulletSurface.Concrete: return Debris.ConcreteChunks;
                case BulletSurface.Wood: return Debris.WoodChips;
                case BulletSurface.Metal: case BulletSurface.Vehicle: return Debris.Sparks;
                case BulletSurface.Glass: return Debris.GlassShards;
                default: return Debris.None; // a body bleeds instead (BloodRules)
            }
        }

        // Particles per impact; low effects keep a third so a firefight stays readable but cheap.
        public static int Count(Debris d, bool highEffects)
        {
            int n;
            switch (d)
            {
                case Debris.ConcreteChunks: n = 7; break;
                case Debris.WoodChips: n = 8; break;
                case Debris.DirtClods: n = 7; break;
                case Debris.GlassShards: n = 9; break;
                case Debris.Sparks: n = 12; break;
                case Debris.WaterSplash: n = 18; break;
                default: return 0;
            }
            return highEffects ? n : Math.Max(1, n / 3);
        }
    }

    public static class WaterCrossing
    {
        // Water has no collider (WaterVolume): a shot from height `fromY` to `toY` that goes down through the surface
        // returns the share of the way where it does (0..1).
        public static bool TryCross(float fromY, float toY, float surfaceY, out float t)
        {
            t = 0f;
            if (!(fromY > surfaceY && toY <= surfaceY)) return false;
            t = (fromY - surfaceY) / (fromY - toY);
            return true;
        }
    }

    // Camera trauma (ADR-471, #201): events add trauma (0..1), it drains steadily, and the view moves by trauma squared
    // so small events barely show. View only: the aim is read from the unshaken camera (PlayerController).
    public sealed class CameraShake
    {
        public const float MaxPitchDeg = 2.2f, MaxYawDeg = 1.6f, MaxRollDeg = 1.8f;
        public const float DrainPerSecond = 1.5f;
        public const float BlastFullMeters = 5f, BlastZeroMeters = 32f;

        public float Trauma { get; private set; }

        public void Add(float amount)
        {
            if (amount > 0f) Trauma = Math.Min(1f, Trauma + amount);
        }

        public void Tick(float dt) => Trauma = Math.Max(0f, Trauma - DrainPerSecond * Math.Max(0f, dt));

        // `strength` is the 화면 흔들림 setting (0..1).
        public void Offset(double time, float strength, out float pitch, out float yaw, out float roll)
        {
            float k = Trauma * Trauma * Math.Max(0f, Math.Min(1f, strength));
            pitch = MaxPitchDeg * k * Wave(time, 0);
            yaw = MaxYawDeg * k * Wave(time, 1);
            roll = MaxRollDeg * k * Wave(time, 2);
        }

        // Smooth noise in [-1, 1]: two sines at unrelated rates per axis.
        static float Wave(double t, int axis) =>
            (float)(0.6 * Math.Sin(t * (23.0 + 4.1 * axis) + 1.3 * axis) + 0.4 * Math.Sin(t * (37.0 + 6.7 * axis) + 2.9 * axis));

        // A blast: full trauma (scaled) inside BlastFullMeters, nothing past BlastZeroMeters, linear between.
        public static float Blast(float distance, float scale)
        {
            if (distance >= BlastZeroMeters) return 0f;
            float k = distance <= BlastFullMeters ? 1f : 1f - (distance - BlastFullMeters) / (BlastZeroMeters - BlastFullMeters);
            return 0.9f * k * scale;
        }

        // A landing: nothing for a step down, up to 0.6 for a hard fall.
        public static float Landing(float droppedMeters) =>
            droppedMeters < 1.2f ? 0f : Math.Min(0.6f, 0.15f + (droppedMeters - 1.2f) * 0.1f);

        public static float Hit(bool head) => head ? 0.45f : 0.3f;

        // Our own shot: a pistol barely, a shotgun or a rifle round more.
        public static float OwnShot(float damage, int pellets) =>
            Math.Max(0.04f, Math.Min(0.22f, 0.02f + damage * Math.Max(1, pellets) * 0.0016f));
    }
}

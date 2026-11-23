using System;

namespace Game.Core
{
    // How a bot racer drives (ADR-517, #447), pure: it looks ahead along its route (the main route, or the cut at the fork when it chose
    // that), steers at that point, and holds the speed the corners ahead allow — each corner's grip limit (v = sqrt(mu g r)) times its
    // skill, plus what it can still brake away before it gets there. The server feeds the result into the bot's car as driver input.
    public static class RaceBotDriver
    {
        public const float Grip = 1.05f, Gravity = 9.81f, TopSpeed = 25f;     // the car's tyre grip and 90 km/h (rules.json Vehicles)
        const float PlanBrake = 6f;          // m/s² it counts on to slow down (the car brakes at 9: room to spare)
        const float LookAhead = 80f;         // corners further than this do not matter yet
        const float SteerFullDeg = 25f;      // the heading error that gives full lock

        public static (float x, float z) Aim(RaceCourse c, float x, float z, float s, float speed, bool cut)
        {
            float target = s + 6f + speed * 0.6f;
            if (cut && c.BranchPath.Count > 0 && target > c.ForkS && target < c.JoinS && s > c.ForkS - 40f)
            {
                var b = c.BranchPath;
                for (int i = 0; i < b.Count - 1; i++)
                    if (target >= b[i].S && target <= b[i + 1].S && b[i + 1].S > b[i].S)
                    {
                        float u = (target - b[i].S) / (b[i + 1].S - b[i].S);
                        return (b[i].X + (b[i + 1].X - b[i].X) * u, b[i].Z + (b[i + 1].Z - b[i].Z) * u);
                    }
            }
            var p = c.PointAt(target);
            return (p.x, p.z);
        }

        // The fastest it should be going at distance s, given the corners in the next LookAhead metres.
        public static float TargetSpeed(RaceCourse c, float s, float skill, bool cut = false)
        {
            float best = TopSpeed;
            // A bot taking the cut slows for the cut's corners inside the fork, not the arc's.
            bool viaCut = cut && c.BranchPath.Count > 0;
            foreach (var p in c.Path)
                if (!viaCut || p.S <= c.ForkS || p.S >= c.JoinS) best = Math.Min(best, Allowed(c, p, s, skill));
            if (viaCut) foreach (var p in c.BranchPath) best = Math.Min(best, Allowed(c, p, s, skill));
            return best;
        }

        // How fast it may be going d metres before corner sample p: the corner's speed plus what it can brake away on the way (no limit
        // for a straight sample or one further than LookAhead).
        static float Allowed(RaceCourse c, RaceCourse.Point p, float s, float skill)
        {
            if (p.Radius <= 0f) return TopSpeed;
            float L = c.LapLength, d = c.Open ? p.S - s : ((p.S - s) % L + L) % L;
            if (d < 0f || d > LookAhead) return TopSpeed;
            float corner = (float)Math.Sqrt(Grip * Gravity * p.Radius) * skill;
            return (float)Math.Sqrt(corner * corner + 2f * PlanBrake * d);
        }

        // A bot's skill (ADR-517): 0.80..0.95 of the grip limit, spread evenly over the field from a seed, so every race has quick and slow bots.
        public static float Skill(int index, int bots, int seed)
        {
            var rng = new Random(seed * 7919 + index);
            float slot = bots <= 1 ? 0.5f : index / (float)(bots - 1);
            return 0.80f + 0.15f * Math.Max(0f, Math.Min(1f, slot + ((float)rng.NextDouble() - 0.5f) * 0.2f));
        }

        // Throttle (+1 full, -1 brake) and steer (+1 right) for a car at (x, z) heading `yaw` degrees (Unity's: 0 = +z) at `speed` m/s.
        public static (float throttle, float steer) Drive(RaceCourse c, float x, float z, float yaw, float speed, float skill, bool cut)
        {
            float s = c.Project(x, z).s;
            var (ax, az) = Aim(c, x, z, s, speed, cut);
            float want = (float)(Math.Atan2(ax - x, az - z) * 180.0 / Math.PI);
            float diff = ((want - yaw) % 360f + 540f) % 360f - 180f;
            float steer = Math.Max(-1f, Math.Min(1f, diff / SteerFullDeg));
            float target = TargetSpeed(c, s, skill, cut);
            float throttle = speed > target + 1f ? -1f : speed < target - 0.5f ? 1f : 0.3f;
            return (throttle, steer);
        }
    }
}

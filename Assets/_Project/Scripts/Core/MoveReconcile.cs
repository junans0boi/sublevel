using System;

namespace Game.Core
{
    // The server's check on a body whose owner moves it (ADR-029, #161, ADR-126): how far it can have gone in a
    // while. Past that, the server puts it back where it last was plausible (reconciliation).
    public static class MoveReconcile
    {
        // The fastest push a body takes on top of its own running (roadkill, ADR-085: up to 12 m/s).
        public const float MaxPushSpeed = 12f;
        // Falling is fast; nothing but a fall (or an elevator) moves a body this quickly up or down.
        public const float MaxVerticalSpeed = 60f;
        // Metres of leeway on top of the speed: the position arrives in bursts (loss, a late packet), and a vehicle exit,
        // a vault end or a stair snap shifts the body a little at once.
        public const float SlackMeters = 2.5f;
        // 20 % over the fastest legal sustained speed.
        const float SpeedMargin = 1.2f;

        public static float MaxHorizontalSpeed(LocomotionParams l) => (l.SprintSpeed + MaxPushSpeed) * SpeedMargin;

        // True when moving by (dx, dy, dz) metres in `seconds` is more than a body on foot can do. `excess` = metres
        // beyond the allowed distance (0 when plausible).
        public static bool Implausible(float dx, float dy, float dz, float seconds, LocomotionParams l, out float excess)
        {
            seconds = Math.Max(0f, seconds);
            float flat = (float)Math.Sqrt(dx * dx + dz * dz);
            float flatAllowed = MaxHorizontalSpeed(l) * seconds + SlackMeters;
            float upAllowed = MaxVerticalSpeed * seconds + SlackMeters;
            excess = Math.Max(Math.Max(0f, flat - flatAllowed), Math.Max(0f, Math.Abs(dy) - upAllowed));
            return excess > 0f;
        }
    }
}

using System;

namespace Game.Core
{
    public enum MoveAnim { Idle, Forward, Back, Left, Right, Dead }

    // Picks the body animation from movement relative to where the character faces.
    public static class MoveAnimSelector
    {
        public static MoveAnim Pick(float localX, float localZ, bool dead, float minSpeed)
        {
            if (dead) return MoveAnim.Dead;
            if (localX * localX + localZ * localZ < minSpeed * minSpeed) return MoveAnim.Idle;
            if (Math.Abs(localZ) >= Math.Abs(localX)) return localZ >= 0f ? MoveAnim.Forward : MoveAnim.Back;
            return localX >= 0f ? MoveAnim.Right : MoveAnim.Left;
        }
    }

    // Turns distance walked into footsteps: one step each time a stride length has been covered.
    public class FootstepCadence
    {
        readonly float _stride;
        float _travelled;

        public FootstepCadence(float strideMeters)
        {
            if (strideMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(strideMeters));
            _stride = strideMeters;
        }

        // Returns true when a step lands. Standing still (or airborne) resets the stride so the next step is not early.
        public bool Advance(float distance, bool moving)
        {
            if (!moving) { _travelled = 0f; return false; }
            _travelled += distance;
            if (_travelled < _stride) return false;
            _travelled %= _stride;
            return true;
        }
    }

    // A shotgun reports every pellet as a shot; this collapses reports that arrive together into one event.
    public class ShotGrouper
    {
        readonly double _window;
        double _last = double.NegativeInfinity;

        public ShotGrouper(double windowSeconds) => _window = windowSeconds;

        public bool IsNewShot(double time)
        {
            if (time - _last < _window) return false;
            _last = time;
            return true;
        }
    }
}

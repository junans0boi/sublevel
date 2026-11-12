using System;

namespace Game.Core
{
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

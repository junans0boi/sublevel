using System;

namespace Game.Core
{
    // The smoke grenade (PUBG_GAP 2-3 "연막", batch I): where and how long it hides things.
    [Serializable]
    public class SmokeRules
    {
        // The rules in use. Defaults until the data loader replaces them, so everything runs without a data file.
        public static SmokeRules Current = new SmokeRules();

        public int MaxCarried = 3;
        public float FuseSeconds = 2f;        // counted from the throw
        public float Radius = 5f;             // metres, of the cloud
        public float Seconds = 25f;           // how long the cloud lasts
        public float CentreLiftMeters = 1f;   // the cloud's centre sits this far above where the canister came to rest
        // A line of sight is blocked only when it spends at least this many metres inside a cloud, so two people
        // standing next to each other in the smoke still see one another.
        public float MinChordMeters = 1.5f;
    }

    // The clouds that exist right now, for line-of-sight questions (bot perception). A fixed table, no
    // allocation; the server fills it when a smoke grenade goes off and empties it when the cloud is gone.
    public static class SmokeZones
    {
        public const int Capacity = 16;

        static readonly float[] Cx = new float[Capacity];
        static readonly float[] Cy = new float[Capacity];
        static readonly float[] Cz = new float[Capacity];
        static readonly float[] Radius = new float[Capacity];
        static readonly int[] Ids = new int[Capacity];
        static int _count;
        static int _nextId = 1;

        public static int Count => _count;

        // Registers a cloud and returns its id (never 0). When the table is full the oldest cloud makes room.
        public static int Add(float x, float y, float z, float radius)
        {
            if (_count == Capacity) RemoveAt(0);
            int i = _count++;
            Cx[i] = x;
            Cy[i] = y;
            Cz[i] = z;
            Radius[i] = radius;
            int id = _nextId++;
            if (_nextId <= 0) _nextId = 1;
            Ids[i] = id;
            return id;
        }

        public static bool Remove(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (Ids[i] != id) continue;
                RemoveAt(i);
                return true;
            }
            return false;
        }

        public static void Clear() => _count = 0;

        static void RemoveAt(int index)
        {
            for (int i = index; i < _count - 1; i++)
            {
                Cx[i] = Cx[i + 1];
                Cy[i] = Cy[i + 1];
                Cz[i] = Cz[i + 1];
                Radius[i] = Radius[i + 1];
                Ids[i] = Ids[i + 1];
            }
            _count--;
        }

        // How much of the segment from -> to lies inside the sphere (centre c, radius r), in metres.
        public static float Chord(float fx, float fy, float fz, float tx, float ty, float tz, float cx, float cy, float cz, float r)
        {
            float dx = tx - fx, dy = ty - fy, dz = tz - fz;
            float a = dx * dx + dy * dy + dz * dz;
            if (a < 1e-10f) return 0f;
            float ox = fx - cx, oy = fy - cy, oz = fz - cz;
            float b = 2f * (dx * ox + dy * oy + dz * oz);
            float c = ox * ox + oy * oy + oz * oz - r * r;
            double disc = (double)b * b - 4.0 * a * c;
            if (disc <= 0.0) return 0f;
            double root = Math.Sqrt(disc);
            double t1 = (-b - root) / (2.0 * a), t2 = (-b + root) / (2.0 * a);
            if (t1 < 0.0) t1 = 0.0;
            if (t2 > 1.0) t2 = 1.0;
            if (t2 <= t1) return 0f;
            return (float)((t2 - t1) * Math.Sqrt(a));
        }

        // True when something in a cloud hides the way from one point to the other.
        public static bool Blocks(float fx, float fy, float fz, float tx, float ty, float tz) =>
            Blocks(fx, fy, fz, tx, ty, tz, SmokeRules.Current.MinChordMeters);

        public static bool Blocks(float fx, float fy, float fz, float tx, float ty, float tz, float minChord)
        {
            for (int i = 0; i < _count; i++)
                if (Chord(fx, fy, fz, tx, ty, tz, Cx[i], Cy[i], Cz[i], Radius[i]) >= minChord) return true;
            return false;
        }
    }
}

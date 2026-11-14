using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The marker types of the team's marker wheel (ADR-130). Our own set of six; the order is the wheel's, clockwise
    // from the top.
    public enum PingKind : byte { Go = 0, Here = 1, Enemy = 2, Loot = 3, Danger = 4, Defend = 5 }

    // Wheel geometry and names shared by the marker wheel and the emote wheel (ADR-130, ADR-131), and the ping rules.
    public static class Pings
    {
        public const int KindCount = 6;

        public static string Name(PingKind kind)
        {
            switch (kind)
            {
                case PingKind.Go: return "이동";
                case PingKind.Here: return "여기";
                case PingKind.Enemy: return "적 발견";
                case PingKind.Loot: return "물자";
                case PingKind.Danger: return "위험";
                default: return "방어";
            }
        }

        public static bool IsValid(int kind) => kind >= 0 && kind < KindCount;

        // Segment of a wheel with `count` equal parts for a cursor at (x, y) from its centre (y up): segment 0 is centred
        // straight up and they run clockwise. -1 while the cursor is inside the dead zone.
        public static int SegmentAt(float x, float y, int count, float deadZone)
        {
            if (count <= 0 || x * x + y * y < deadZone * deadZone) return -1;
            float angle = (float)(Math.Atan2(x, y) * 180.0 / Math.PI); // 0 up, clockwise positive
            float part = 360f / count;
            angle = (angle + part * 0.5f + 360f) % 360f;
            return Math.Min(count - 1, (int)(angle / part));
        }

        // A plain "go" or "here" ping put on an enemy is an "enemy spotted" ping.
        public static PingKind Upgrade(PingKind requested, bool enemyNearby) =>
            enemyNearby && (requested == PingKind.Go || requested == PingKind.Here) ? PingKind.Enemy : requested;
    }

    // Server: accepts at most one ping per MinInterval from each owner, so a held key or a script cannot flood the team.
    public class PingLimiter
    {
        readonly Dictionary<int, float> _last = new Dictionary<int, float>();

        public bool TryAccept(int owner, float now, TeamRules rules)
        {
            if (_last.TryGetValue(owner, out var last) && now - last < rules.PingMinIntervalSeconds) return false;
            _last[owner] = now;
            return true;
        }

        public void Forget(int owner) => _last.Remove(owner);
    }

    public struct PingEntry
    {
        public int Id;          // grows with every ping: views tell a new ping from the one they show
        public int Owner;       // ObjectId of the pinger
        public int Slot;        // the pinger's number in the team (colour)
        public PingKind Kind;
        public float X, Y, Z;
        public float Born;      // the receiver's clock
    }

    // What one client holds of the pings its team put down: the newest ones per owner, each living PingLifeSeconds.
    public class PingBoard
    {
        readonly List<PingEntry> _active = new List<PingEntry>();
        int _nextId;

        public IReadOnlyList<PingEntry> Active => _active;
        public int Count => _active.Count;
        // Pings ever added; tests wait for it to grow.
        public int Total => _nextId;

        // The newest MaxPerOwner of an owner stay; an older one makes room.
        public PingEntry Add(int owner, int slot, PingKind kind, float x, float y, float z, float now, TeamRules rules)
        {
            var entry = new PingEntry { Id = ++_nextId, Owner = owner, Slot = slot, Kind = kind, X = x, Y = y, Z = z, Born = now };
            int mine = 0;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Owner != owner) continue;
                if (++mine >= Math.Max(1, rules.PingMaxPerOwner)) _active.RemoveAt(i);
            }
            _active.Add(entry);
            return entry;
        }

        // Drops the pings whose time is over.
        public void Prune(float now, TeamRules rules)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                if (now - _active[i].Born >= rules.PingLifeSeconds) _active.RemoveAt(i);
        }

        public void Clear() => _active.Clear();

        // 1 while fresh, falling to 0 over the last second of its life: for fading.
        public static float Alpha(in PingEntry e, float now, TeamRules rules)
        {
            float left = rules.PingLifeSeconds - (now - e.Born);
            return left <= 0f ? 0f : left >= 1f ? 1f : left;
        }
    }
}

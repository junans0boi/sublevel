using System;
using System.Collections.Generic;

namespace Game.Core
{
    // What a bullet met on the island, and what the range makes of it.
    public enum RangeHit { World, Target, Body }
    public enum RangeShot { Pass, Score, Absorb }   // Pass: the shot goes on as always; Score: a booth target hit; Absorb: it stops, harmless

    // The shooting range of the waiting island (ADR-501, #421): three booths on the west side, each a stand behind a counter and a lane
    // west to two targets (the TrainingDummy's body: a 1.8 m capsule). Pure geometry and rules, shared by the builder, the server and
    // the tests. Coordinates are island-local metres (x east, z south), like WaitingIsland.
    public static class WaitingRange
    {
        // The stand: from the counter (just west of the booth's centre) StandBack metres east, HalfWidth either side of the lane's axis.
        public const float CounterX = -0.6f;          // the counter's middle, from the booth's centre
        public const float StandFront = -0.3f;        // the stand starts at the counter's east face
        public const float StandBack = 2.6f;          // ...and ends here (from the booth's centre)
        public const float HalfWidth = 2.3f;          // half the stand's width (the lane walls stand at LaneHalf)
        public const float LaneHalf = 2.6f;
        public const float LeaveMargin = 0.4f;        // stepping out needs this much more than stepping in
        public const float TargetX = -34.5f;          // the targets' line (island-local x), about 8 m down the lane
        public const float TargetSpread = 1.1f;       // either side of the lane's axis
        public const int TargetsPerBooth = 2;
        public const float TargetHeight = 1.8f, TargetRadius = 0.35f;   // TrainingDummy's capsule

        // Inside the booths no one is hurt either (ADR-383): firing is allowed only in a booth, and only the booth's own gun.
        public static bool MayFire(bool waiting, bool inBooth, bool holdsLoan) => !waiting || (inBooth && holdsLoan);

        // A shot that met `hit` while people wait: targets score from the booth, bodies (people, bots) stop it harmlessly with no hit
        // marker, the rest is the match's usual ballistics. After the island everything passes as before.
        public static RangeShot Judge(bool waiting, bool shooterInBooth, RangeHit hit)
        {
            if (!waiting || hit == RangeHit.World) return RangeShot.Pass;
            return hit == RangeHit.Target && shooterInBooth ? RangeShot.Score : RangeShot.Absorb;
        }

        // The booth whose stand holds this place, or -1. `margin` widens the stand (LeaveMargin when judging someone already in).
        public static int BoothAt(float lx, float lz, float margin = 0f)
        {
            var booths = WaitingIsland.Booths;
            for (int i = 0; i < booths.Length; i++)
            {
                float dx = lx - booths[i].x, dz = lz - booths[i].z;
                if (dx >= StandFront - margin && dx <= StandBack + margin && Math.Abs(dz) <= HalfWidth + margin) return i;
            }
            return -1;
        }

        // In a booth's lane or stand (between its walls, west of the stand's back): nobody spawns or strolls there.
        public static bool InLane(float lx, float lz, float pad = 0f)
        {
            foreach (var b in WaitingIsland.Booths)
                if (lx < b.x + StandBack + pad && Math.Abs(lz - b.z) < LaneHalf + pad) return true;
            return false;
        }

        // Where a shooter stands in booth `booth` (the stand's middle) and where its targets are.
        public static (float x, float z) Stand(int booth) { var b = WaitingIsland.Booths[booth]; return (b.x + (StandFront + StandBack) * 0.5f, b.z); }
        public static (float x, float z) Target(int booth, int i) { var b = WaitingIsland.Booths[booth]; return (TargetX, b.z + (i == 0 ? -TargetSpread : TargetSpread)); }
    }

    public struct RecordRow { public string Name; public int Score; }
    public struct RecordEntry { public string Name; public int Score; public long ReachedAt; }

    // A room's scores (bungee jumps, target hits; ADR-501/502): memory of the island only, never saved, cleared when the island closes.
    public sealed class RoomRecords
    {
        readonly Dictionary<int, RecordEntry> _byKey = new Dictionary<int, RecordEntry>();
        long _seq;

        public int Version { get; private set; }

        public void Add(int key, string name, int points)
        {
            _byKey.TryGetValue(key, out var e);
            if (!string.IsNullOrEmpty(name)) e.Name = name;
            if (points > 0) { e.Score += points; e.ReachedAt = ++_seq; }
            _byKey[key] = e;
            Version++;
        }

        public int ScoreOf(int key) => _byKey.TryGetValue(key, out var e) ? e.Score : 0;
        public List<RecordRow> Top(int n) => Rank(_byKey.Values, n);
        public void Clear() { _byKey.Clear(); Version++; }

        // The best `n` with a score: highest first; on a tie, whoever reached that score first.
        public static List<RecordRow> Rank(IEnumerable<RecordEntry> entries, int n)
        {
            var list = new List<RecordEntry>();
            foreach (var e in entries) if (e.Score > 0) list.Add(e);
            list.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.ReachedAt.CompareTo(b.ReachedAt));
            var top = new List<RecordRow>();
            for (int i = 0; i < list.Count && i < n; i++) top.Add(new RecordRow { Name = string.IsNullOrEmpty(list[i].Name) ? "참가자" : list[i].Name, Score = list[i].Score });
            return top;
        }
    }
}

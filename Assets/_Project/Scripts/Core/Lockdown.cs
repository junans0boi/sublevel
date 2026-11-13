using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Where a region stands in its closing (SPEC 3): safe -> warning -> lockdown -> bombardment -> closed.
    public enum ZoneState : byte { Safe = 0, Warning = 1, Lockdown = 2, Bombardment = 3, Closed = 4 }

    // Tuning of the region lockdown (M3, ADR-050/052/058). Seconds are match time unless said otherwise.
    [Serializable]
    public class LockdownRules
    {
        // The rules in use. Defaults until a data loader (or a test) replaces them.
        public static LockdownRules Current = new LockdownRules();

        // ---- Timeline of one closing ----
        public float FirstWarningSeconds = 300f;        // match time when the first region is announced
        public float WarningSeconds = 45f;              // announced, still open: time to get out
        public float LockdownSeconds = 120f;            // barriers up, radiation, before the missiles
        public float BombardmentSeconds = 45f;          // missiles; afterwards the region is closed
        public float MinSecondsBetweenClosings = 150f;  // between the announcements of two regions

        // ---- Damage ----
        public float RadiationDps = 2f;                 // lockdown and bombardment: health per second, never instant
        public float RadiationGraceSeconds = 4f;        // standing in a harmful zone this long before it hurts
        public float ClosedDps = 20f;                   // closed regions and sub-zones: lethal in seconds
        public float RadiationTickSeconds = 0.5f;       // damage is applied in steps this long
        // Later closings hurt more (#141, ADR-109): RadiationDps times these, spread evenly over the closing order
        // (first region = first entry, the last region and the final region's sub-zones = last entry). 2 -> 11 per second.
        public float[] RadiationStageScale = { 1f, 1.25f, 1.75f, 2.5f, 3.5f, 4.5f, 5.5f };

        // ---- Bombardment ----
        public float BlastIntervalSeconds = 1.6f;       // per bombarded region / sub-zone
        public float BlastWarningSeconds = 3f;          // marker and incoming sound before the blast
        public float BlastInnerRadius = 3f;             // full damage up to here
        public float BlastOuterRadius = 10f;            // nothing from here on
        public float BlastDamage = 70f;
        public float BlastTargetedShare = 0.5f;         // share of blasts aimed near a combatant
        public float BlastScatter = 14f;                // targeted blasts land within this of the combatant

        // ---- Barriers and keys ----
        public float KeyOpenSeconds = 10f;              // a pass stays open this long after a key was used
        public int KeysPerRegion = 2;                   // key pickups placed per region at match start
        public float PassInteractRange = 4f;            // metres from a barrier to use a key

        // ---- Final region (SPEC 3-1) ----
        public int FinalRevealLastClosings = 1;         // the final region is named when this many closings are still to start
        public float SubZoneStartDelaySeconds = 20f;    // after the last region is closed
        public float SubZoneWarningSeconds = 30f;
        public float SubZoneBombardmentSeconds = 30f;
        public float SubZoneSpacingSeconds = 90f;       // between the announcements of two sub-zones
        public int FinalSubZonesMin = 3;                // sub-zones in play in the final region (the last one survives)
        public int FinalSubZonesMax = 4;

        // Multiplier on RadiationDps for a closing `stage01` of the way through the order (0 = first, 1 = last).
        public float RadiationScale(float stage01)
        {
            var t = RadiationStageScale;
            if (t == null || t.Length == 0) return 1f;
            float x = Math.Max(0f, Math.Min(1f, stage01)) * (t.Length - 1);
            int i = Math.Min((int)x, t.Length - 1);
            int j = Math.Min(i + 1, t.Length - 1);
            return t[i] + (t[j] - t[i]) * (x - i);
        }

        // Health per second a combatant standing in a region in this state loses (after the grace period);
        // `stage01` is how late in the closing order the region is.
        public float DamagePerSecond(ZoneState state, float stage01 = 0f)
        {
            switch (state)
            {
                case ZoneState.Lockdown:
                case ZoneState.Bombardment: return RadiationDps * RadiationScale(stage01);
                case ZoneState.Closed: return ClosedDps;
                default: return 0f;
            }
        }

        // A whole match in about six minutes of match time (the default runs about nineteen): the match simulation
        // test and `-autoplay` (#77). The waits are cut hard; the warnings stay long enough for a sprinting bot to
        // reach a pass from most of a region (7 m/s), so a closing is still escaped by walking, not only with keys.
        public static LockdownRules QuickMatch() => new LockdownRules
        {
            FirstWarningSeconds = 45f, WarningSeconds = 40f, LockdownSeconds = 20f, BombardmentSeconds = 10f,
            MinSecondsBetweenClosings = 50f,
            RadiationDps = 4f, RadiationGraceSeconds = 2f, ClosedDps = 20f,
            SubZoneStartDelaySeconds = 10f, SubZoneWarningSeconds = 30f, SubZoneBombardmentSeconds = 10f, SubZoneSpacingSeconds = 40f,
        };

        // The same rules with every timeline value divided, for tests and quick matches.
        public LockdownRules Scaled(float divisor)
        {
            var r = (LockdownRules)MemberwiseClone();
            float d = divisor <= 0f ? 1f : divisor;
            r.FirstWarningSeconds /= d; r.WarningSeconds /= d; r.LockdownSeconds /= d; r.BombardmentSeconds /= d;
            r.MinSecondsBetweenClosings /= d; r.SubZoneStartDelaySeconds /= d; r.SubZoneWarningSeconds /= d;
            r.SubZoneBombardmentSeconds /= d; r.SubZoneSpacingSeconds /= d;
            return r;
        }
    }

    public enum LockdownEventKind : byte { RegionState, FinalRevealed, SubZoneState }

    // One change on the timeline, at an absolute match time.
    public struct LockdownEvent
    {
        public float Time;
        public LockdownEventKind Kind;
        public int Region;      // RegionState: the region; FinalRevealed / SubZoneState: the final region
        public int Index;       // RegionState: closing index; SubZoneState: sub-zone closing step
        public ZoneState State; // RegionState / SubZoneState: the state that begins
    }

    // Everything the lockdown does in one match, decided up front from the match seed.
    public sealed class LockdownPlan
    {
        public LockdownRules Rules;
        public int Seed, RegionCount, FinalRegion;
        public int[] Order = new int[0];                    // regions in the order they close; never the final region
        public float[] WarningAt = new float[0];            // by closing index
        public float[] LockdownAt = new float[0];
        public float[] BombardmentAt = new float[0];
        public float[] ClosedAt = new float[0];
        public float FinalRevealAt;                         // the final region is named from this time on
        public int SubZoneCount;                            // sub-zones the final region has
        public int[] FinalSubZones = new int[0];            // local sub-zone indices in play, in closing order; the last survives
        public int[] SubZoneStep = new int[0];              // by local index: closing step, or -1 (survivor / never closes)
        public float[] SubZoneWarningAt = new float[0];     // by closing step
        public float[] SubZoneBombardmentAt = new float[0];
        public float[] SubZoneClosedAt = new float[0];
        public LockdownEvent[] Events = new LockdownEvent[0]; // sorted by time

        internal int[] ClosingOf = new int[0];

        // Closing index of a region (position in Order), or -1 for the final region.
        public int ClosingIndexOf(int region) => region >= 0 && region < ClosingOf.Length ? ClosingOf[region] : -1;

        // The local sub-zone that survives to the very end: where the last fight happens. -1 when there are none.
        public int ConvergenceSubZone => FinalSubZones.Length > 0 ? FinalSubZones[FinalSubZones.Length - 1] : -1;

        // Match time when the last region is closed (0 when there is nothing to close).
        public float AllRegionsClosedAt => ClosedAt.Length > 0 ? ClosedAt[ClosedAt.Length - 1] : 0f;
    }

    public static class Lockdown
    {
        // ---- Planning ----

        // Plans a match from the shape of the map alone: how many regions, which pairs touch through a pass, how
        // many sub-zones each region has. Deterministic in the seed. The order never contains the final region and,
        // as long as the map is connected, the regions still open stay connected through passes after every closing.
        public static LockdownPlan Plan(int regionCount, IReadOnlyList<int> passFrom, IReadOnlyList<int> passTo, int seed,
            IReadOnlyList<int> subZoneCounts = null, LockdownRules rules = null)
        {
            rules = rules ?? LockdownRules.Current;
            var plan = new LockdownPlan { Rules = rules, Seed = seed, RegionCount = Math.Max(0, regionCount), FinalRegion = -1 };
            int n = plan.RegionCount;
            plan.ClosingOf = new int[n];
            for (int i = 0; i < n; i++) plan.ClosingOf[i] = -1;
            if (n == 0) return plan;

            // The seed is mixed once before use: without it neighbouring seeds (0, 1, 2, ...) drew the final region
            // unevenly (one region final in 14.5 % of seeds 0..199 instead of about 25 %, #77).
            uint rng = Mix(unchecked((uint)seed * 2654435761u + 0x9E3779B9u));
            plan.FinalRegion = NextInt(ref rng, n);

            // Everyone but the final region, shuffled.
            var shuffled = new List<int>(n);
            for (int i = 0; i < n; i++) if (i != plan.FinalRegion) shuffled.Add(i);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = NextInt(ref rng, i + 1);
                int t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t;
            }

            // The first region in that order whose closing keeps the rest connected goes next. A connected map always
            // has one (a leaf of a spanning tree rooted at the final region); a broken map just keeps the shuffle.
            var open = new bool[n];
            for (int i = 0; i < n; i++) open[i] = true;
            bool connected = IsConnected(n, passFrom, passTo, open);
            var order = new int[shuffled.Count];
            for (int step = 0; step < order.Length; step++)
            {
                int pick = -1;
                for (int k = 0; k < shuffled.Count && pick < 0; k++)
                {
                    int c = shuffled[k];
                    open[c] = false;
                    if (!connected || IsConnected(n, passFrom, passTo, open)) pick = k;
                    open[c] = true;
                }
                if (pick < 0) pick = 0;
                order[step] = shuffled[pick];
                open[order[step]] = false;
                shuffled.RemoveAt(pick);
            }
            plan.Order = order;

            int m = order.Length;
            plan.WarningAt = new float[m]; plan.LockdownAt = new float[m]; plan.BombardmentAt = new float[m]; plan.ClosedAt = new float[m];
            float spacing = Math.Max(1f, rules.MinSecondsBetweenClosings);
            for (int i = 0; i < m; i++)
            {
                plan.ClosingOf[order[i]] = i;
                plan.WarningAt[i] = rules.FirstWarningSeconds + i * spacing;
                plan.LockdownAt[i] = plan.WarningAt[i] + rules.WarningSeconds;
                plan.BombardmentAt[i] = plan.LockdownAt[i] + rules.LockdownSeconds;
                plan.ClosedAt[i] = plan.BombardmentAt[i] + rules.BombardmentSeconds;
            }

            // Hidden until FinalRevealLastClosings closings are still to be announced: with the default of 1 that is the
            // last announcement, which names the only region it can be.
            if (m == 0) plan.FinalRevealAt = 0f;
            else plan.FinalRevealAt = plan.WarningAt[Math.Max(0, m - Math.Max(1, rules.FinalRevealLastClosings))];

            PlanSubZones(plan, subZoneCounts, ref rng);
            BuildEvents(plan);
            return plan;
        }

        // The same, from a generated world.
        public static LockdownPlan Plan(WorldPlan world, int seed, LockdownRules rules = null)
        {
            int n = world.Regions.Count;
            var a = new int[world.Passes.Count];
            var b = new int[world.Passes.Count];
            for (int i = 0; i < a.Length; i++) { a[i] = world.Passes[i].RegionA; b[i] = world.Passes[i].RegionB; }
            var counts = new int[n];
            foreach (var z in world.SubZones) if (z.Region >= 0 && z.Region < n) counts[z.Region]++;
            return Plan(n, a, b, seed, counts, rules);
        }

        static void PlanSubZones(LockdownPlan plan, IReadOnlyList<int> subZoneCounts, ref uint rng)
        {
            var rules = plan.Rules;
            int count = subZoneCounts != null && plan.FinalRegion >= 0 && plan.FinalRegion < subZoneCounts.Count
                ? Math.Max(0, subZoneCounts[plan.FinalRegion]) : 0;
            plan.SubZoneCount = count;
            plan.SubZoneStep = new int[count];
            for (int i = 0; i < count; i++) plan.SubZoneStep[i] = -1;
            if (count == 0) return;

            int min = Math.Max(1, rules.FinalSubZonesMin), max = Math.Max(min, rules.FinalSubZonesMax);
            int want = Math.Min(count, min + NextInt(ref rng, max - min + 1));
            var locals = new List<int>(count);
            for (int i = 0; i < count; i++) locals.Add(i);
            for (int i = count - 1; i > 0; i--)
            {
                int j = NextInt(ref rng, i + 1);
                int t = locals[i]; locals[i] = locals[j]; locals[j] = t;
            }
            plan.FinalSubZones = locals.GetRange(0, want).ToArray();

            int steps = Math.Max(0, want - 1);
            plan.SubZoneWarningAt = new float[steps]; plan.SubZoneBombardmentAt = new float[steps]; plan.SubZoneClosedAt = new float[steps];
            float start = plan.AllRegionsClosedAt + rules.SubZoneStartDelaySeconds;
            for (int s = 0; s < steps; s++)
            {
                plan.SubZoneWarningAt[s] = start + s * Math.Max(1f, rules.SubZoneSpacingSeconds);
                plan.SubZoneBombardmentAt[s] = plan.SubZoneWarningAt[s] + rules.SubZoneWarningSeconds;
                plan.SubZoneClosedAt[s] = plan.SubZoneBombardmentAt[s] + rules.SubZoneBombardmentSeconds;
                plan.SubZoneStep[plan.FinalSubZones[s]] = s;
            }
            // Sub-zones that are not in play go down with the first step (nothing is left standing but the survivor).
            if (steps > 0)
                for (int i = 0; i < count; i++)
                    if (i != plan.ConvergenceSubZone && plan.SubZoneStep[i] < 0) plan.SubZoneStep[i] = 0;
        }

        static void BuildEvents(LockdownPlan plan)
        {
            var list = new List<LockdownEvent>();
            for (int i = 0; i < plan.Order.Length; i++)
            {
                int r = plan.Order[i];
                list.Add(new LockdownEvent { Time = plan.WarningAt[i], Kind = LockdownEventKind.RegionState, Region = r, Index = i, State = ZoneState.Warning });
                list.Add(new LockdownEvent { Time = plan.LockdownAt[i], Kind = LockdownEventKind.RegionState, Region = r, Index = i, State = ZoneState.Lockdown });
                list.Add(new LockdownEvent { Time = plan.BombardmentAt[i], Kind = LockdownEventKind.RegionState, Region = r, Index = i, State = ZoneState.Bombardment });
                list.Add(new LockdownEvent { Time = plan.ClosedAt[i], Kind = LockdownEventKind.RegionState, Region = r, Index = i, State = ZoneState.Closed });
            }
            if (plan.FinalRegion >= 0)
                list.Add(new LockdownEvent { Time = plan.FinalRevealAt, Kind = LockdownEventKind.FinalRevealed, Region = plan.FinalRegion, Index = -1 });
            for (int s = 0; s < plan.SubZoneWarningAt.Length; s++)
            {
                list.Add(new LockdownEvent { Time = plan.SubZoneWarningAt[s], Kind = LockdownEventKind.SubZoneState, Region = plan.FinalRegion, Index = s, State = ZoneState.Warning });
                list.Add(new LockdownEvent { Time = plan.SubZoneBombardmentAt[s], Kind = LockdownEventKind.SubZoneState, Region = plan.FinalRegion, Index = s, State = ZoneState.Bombardment });
                list.Add(new LockdownEvent { Time = plan.SubZoneClosedAt[s], Kind = LockdownEventKind.SubZoneState, Region = plan.FinalRegion, Index = s, State = ZoneState.Closed });
            }
            // Stable insertion sort: events at the same time keep the order they were made in.
            for (int i = 1; i < list.Count; i++)
            {
                var e = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].Time > e.Time) { list[j + 1] = list[j]; j--; }
                list[j + 1] = e;
            }
            plan.Events = list.ToArray();
        }

        // ---- Connectivity (SPEC 3-3) ----

        // True when the regions marked open form one piece through the passes (none or one open region counts).
        public static bool IsConnected(int regionCount, IReadOnlyList<int> passFrom, IReadOnlyList<int> passTo, bool[] open)
        {
            int first = -1, total = 0;
            for (int i = 0; i < regionCount; i++)
                if (open[i]) { total++; if (first < 0) first = i; }
            if (total <= 1) return true;
            var seen = new bool[regionCount];
            var stack = new Stack<int>();
            stack.Push(first);
            seen[first] = true;
            int reached = 1;
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                for (int p = 0; p < passFrom.Count; p++)
                {
                    int other = passFrom[p] == at ? passTo[p] : passTo[p] == at ? passFrom[p] : -1;
                    if (other < 0 || other >= regionCount || !open[other] || seen[other]) continue;
                    seen[other] = true;
                    reached++;
                    stack.Push(other);
                }
            }
            return reached == total;
        }

        // Regions people can still walk between at a time: those not yet in lockdown.
        public static bool IsPassable(ZoneState state) => state == ZoneState.Safe || state == ZoneState.Warning;

        // ---- Queries ----

        public static ZoneState StateAt(LockdownPlan plan, int region, float time)
        {
            int i = plan.ClosingIndexOf(region);
            if (i < 0) return ZoneState.Safe;
            if (time < plan.WarningAt[i]) return ZoneState.Safe;
            if (time < plan.LockdownAt[i]) return ZoneState.Warning;
            if (time < plan.BombardmentAt[i]) return ZoneState.Lockdown;
            if (time < plan.ClosedAt[i]) return ZoneState.Bombardment;
            return ZoneState.Closed;
        }

        // State of a sub-zone of the final region (local index). Radiation and missiles follow the same states.
        public static ZoneState SubZoneStateAt(LockdownPlan plan, int local, float time)
        {
            if (local < 0 || local >= plan.SubZoneStep.Length) return ZoneState.Safe;
            int s = plan.SubZoneStep[local];
            if (s < 0) return ZoneState.Safe;
            if (time < plan.SubZoneWarningAt[s]) return ZoneState.Safe;
            if (time < plan.SubZoneBombardmentAt[s]) return ZoneState.Warning;
            if (time < plan.SubZoneClosedAt[s]) return ZoneState.Bombardment;
            return ZoneState.Closed;
        }

        // The time the state of a region (or, with sub-zone true, of a local sub-zone) changes next; -1 when it will not.
        public static float StateEndsAt(LockdownPlan plan, int region, float time, bool subZone = false)
        {
            if (subZone)
            {
                if (region < 0 || region >= plan.SubZoneStep.Length || plan.SubZoneStep[region] < 0) return -1f;
                int s = plan.SubZoneStep[region];
                if (time < plan.SubZoneWarningAt[s]) return plan.SubZoneWarningAt[s];
                if (time < plan.SubZoneBombardmentAt[s]) return plan.SubZoneBombardmentAt[s];
                if (time < plan.SubZoneClosedAt[s]) return plan.SubZoneClosedAt[s];
                return -1f;
            }
            int i = plan.ClosingIndexOf(region);
            if (i < 0) return -1f;
            if (time < plan.WarningAt[i]) return plan.WarningAt[i];
            if (time < plan.LockdownAt[i]) return plan.LockdownAt[i];
            if (time < plan.BombardmentAt[i]) return plan.BombardmentAt[i];
            if (time < plan.ClosedAt[i]) return plan.ClosedAt[i];
            return -1f;
        }

        // The time a region stops being passable (its lockdown begins); infinity for the final region.
        public static float OpenUntil(LockdownPlan plan, int region)
        {
            int i = plan.ClosingIndexOf(region);
            return i < 0 ? float.PositiveInfinity : plan.LockdownAt[i];
        }

        public static bool IsFinalRevealed(LockdownPlan plan, float time) => plan.FinalRegion >= 0 && time >= plan.FinalRevealAt;

        // The final region once it has been named, otherwise -1.
        public static int RevealedFinalRegion(LockdownPlan plan, float time) => IsFinalRevealed(plan, time) ? plan.FinalRegion : -1;

        // The first event strictly after `time`.
        public static bool NextEventAfter(LockdownPlan plan, float time, out LockdownEvent next)
        {
            var events = plan.Events;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].Time > time) { next = events[i]; return true; }
            }
            next = default;
            return false;
        }

        // Fills `result` with the regions that could still be the final one at `time`: those not yet in lockdown.
        // Players only learn this much before the reveal.
        public static void Candidates(LockdownPlan plan, float time, List<int> result)
        {
            result.Clear();
            for (int r = 0; r < plan.RegionCount; r++)
                if (IsPassable(StateAt(plan, r, time))) result.Add(r);
        }

        // Damage falls off linearly between the inner and outer radius of a blast.
        public static float BlastDamageAt(float distance, LockdownRules rules)
        {
            if (distance <= rules.BlastInnerRadius) return rules.BlastDamage;
            if (distance >= rules.BlastOuterRadius || rules.BlastOuterRadius <= rules.BlastInnerRadius) return 0f;
            return rules.BlastDamage * (1f - (distance - rules.BlastInnerRadius) / (rules.BlastOuterRadius - rules.BlastInnerRadius));
        }

        // ---- Small deterministic generator (the same numbers on every platform) ----

        static uint Next(ref uint s)
        {
            unchecked
            {
                s += 0x9E3779B9u;
                return Mix(s);
            }
        }

        static uint Mix(uint z)
        {
            unchecked
            {
                z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
                z = (z ^ (z >> 13)) * 0xC2B2AE35u;
                return z ^ (z >> 16);
            }
        }

        static int NextInt(ref uint s, int bound) => bound <= 1 ? 0 : (int)(Next(ref s) % (uint)bound);
    }

    // The shape of the map the lockdown needs at run time: where the regions, passes and sub-zones are. Built from
    // the generated plan (editor, tests) or from WorldInfo (the running game) and handed to the zone director.
    public sealed class ZoneGeometry
    {
        public float Size = 1024f, SeaLevel = 10f;
        public string[] RegionId = new string[0], RegionName = new string[0];
        public float[] CentreX = new float[0], CentreZ = new float[0], CentreY = new float[0];
        public int[] PassA = new int[0], PassB = new int[0];
        public float[] PassX = new float[0], PassY = new float[0], PassZ = new float[0], PassYawDeg = new float[0], PassWidth = new float[0];
        public int[] SubRegion = new int[0];
        public float[] SubX = new float[0], SubY = new float[0], SubZ = new float[0], SubRadius = new float[0];

        public int RegionCount => RegionId.Length;
        public int PassCount => PassA.Length;
        public int SubZoneTotal => SubRegion.Length;

        // Index of the region whose centre is nearest (regions are the Voronoi cells of their centres), -1 off the map.
        public int RegionAt(float x, float z)
        {
            if (x < 0f || z < 0f || x > Size || z > Size) return -1;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < CentreX.Length; i++)
            {
                float dx = x - CentreX[i], dz = z - CentreZ[i];
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public int RegionIndexOf(string id)
        {
            for (int i = 0; i < RegionId.Length; i++) if (RegionId[i] == id) return i;
            return -1;
        }

        // Sub-zones per region, for planning.
        public int[] SubZoneCounts()
        {
            var counts = new int[RegionCount];
            for (int i = 0; i < SubRegion.Length; i++) if (SubRegion[i] >= 0 && SubRegion[i] < counts.Length) counts[SubRegion[i]]++;
            return counts;
        }

        // Global index of the local-th sub-zone of a region (in array order), or -1.
        public int GlobalSubZone(int region, int local)
        {
            int n = 0;
            for (int i = 0; i < SubRegion.Length; i++)
            {
                if (SubRegion[i] != region) continue;
                if (n == local) return i;
                n++;
            }
            return -1;
        }

        // Local index (within its region) of a global sub-zone.
        public int LocalSubZone(int global)
        {
            if (global < 0 || global >= SubRegion.Length) return -1;
            int n = 0;
            for (int i = 0; i < global; i++) if (SubRegion[i] == SubRegion[global]) n++;
            return n;
        }

        // Global index of the nearest sub-zone of the region the point is in, or -1.
        public int SubZoneAt(float x, float z)
        {
            int region = RegionAt(x, z);
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; region >= 0 && i < SubRegion.Length; i++)
            {
                if (SubRegion[i] != region) continue;
                float dx = x - SubX[i], dz = z - SubZ[i];
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static ZoneGeometry FromPlan(WorldPlan plan)
        {
            var g = new ZoneGeometry { Size = plan.Size, SeaLevel = plan.SeaLevel };
            int n = plan.Regions.Count;
            g.RegionId = new string[n]; g.RegionName = new string[n];
            g.CentreX = new float[n]; g.CentreZ = new float[n]; g.CentreY = new float[n];
            for (int i = 0; i < n; i++)
            {
                var r = plan.Regions[i];
                g.RegionId[i] = r.Id; g.RegionName[i] = r.Name;
                g.CentreX[i] = r.CentreX; g.CentreZ[i] = r.CentreZ; g.CentreY[i] = r.HubY;
            }
            int p = plan.Passes.Count;
            g.PassA = new int[p]; g.PassB = new int[p];
            g.PassX = new float[p]; g.PassY = new float[p]; g.PassZ = new float[p]; g.PassYawDeg = new float[p]; g.PassWidth = new float[p];
            for (int i = 0; i < p; i++)
            {
                var pass = plan.Passes[i];
                g.PassA[i] = pass.RegionA; g.PassB[i] = pass.RegionB;
                g.PassX[i] = pass.X; g.PassY[i] = pass.Y; g.PassZ[i] = pass.Z; g.PassYawDeg[i] = pass.YawDeg; g.PassWidth[i] = pass.Width;
            }
            int s = plan.SubZones.Count;
            g.SubRegion = new int[s];
            g.SubX = new float[s]; g.SubY = new float[s]; g.SubZ = new float[s]; g.SubRadius = new float[s];
            for (int i = 0; i < s; i++)
            {
                var z = plan.SubZones[i];
                g.SubRegion[i] = z.Region; g.SubX[i] = z.X; g.SubY[i] = z.Y; g.SubZ[i] = z.Z; g.SubRadius[i] = z.Radius;
            }
            return g;
        }
    }
}

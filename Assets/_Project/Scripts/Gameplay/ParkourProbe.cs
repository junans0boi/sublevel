using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // A parkour the owner may start now: what, from where to where, and for how long.
    public struct ParkourPlan
    {
        public ParkourMove Move;
        public ObstacleProfile Profile;
        public Vector3 Start, End;   // feet
        public float TopY;           // world height of the top (the sill, for a window)
        public float Seconds;
        public GlassPane Glass;      // an unbroken pane in the window, broken by going through
    }

    // Measures the obstacle in front of a body with ray casts and plans the move Space makes over it (ADR-065 (4)).
    // Rays go forward at a ladder of heights from the feet up; the first free rung above blocked ones is the top
    // (or the sill), a blocked rung above that in the same face makes it a window. The top's height, how deep it
    // goes and the free height over it are then measured with downward and upward rays. The classification
    // itself is Parkour.Classify (pure, tested with literal cases).
    public static class ParkourProbe
    {
        const float FirstRung = 0.1f, RungStep = 0.1f;
        const int Rungs = 24;                 // up to 2.4 m
        const float ScanTop = 2.7f;           // highest point the top is looked for from
        const float FaceSlack = 0.25f;        // a rung this much farther than the face is not the same wall
        const float IntoFace = 0.08f;         // how far past the face the top is measured
        const float DepthStep = 0.1f, MaxDepth = 2.0f;
        const float EdgeDrop = 0.25f;         // the top ends where the surface falls away by this much
        const float LandingMargin = 0.2f;     // past the far side, clear of it
        const float MaxLandingDrop = 2.0f;    // ground farther below than this: the move ends in the air and the body falls
        const float GroundProbe = 0.25f;      // the ground under the feet is looked for this far above and below them

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static readonly Collider[] Overlaps = new Collider[16];
        static readonly bool[] Blocked = new bool[Rungs];
        static readonly float[] Distance = new float[Rungs];

        public static ParkourPlan Plan(Transform body, float radius, float height, float yawDeg, bool crouched, ParkourRules rules)
        {
            var plan = new ParkourPlan { Move = ParkourMove.None, Start = body.position };
            var feet = body.position;
            // Heights are measured from the ground under the feet: a standing character controller floats its skin
            // width (about 8 cm) above it.
            if (Cast(feet + Vector3.up * GroundProbe, Vector3.down, GroundProbe * 2f, body, out float ground)) feet.y += GroundProbe - ground;
            var forward = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            float reach = radius + rules.ProbeMeters;

            for (int i = 0; i < Rungs; i++)
            {
                Blocked[i] = Cast(feet + Vector3.up * Rung(i), forward, reach, body, out float d);
                Distance[i] = d;
            }
            // Nothing standing on the ground right in front: an ordinary jump.
            if (!Blocked[0] && !Blocked[1]) return plan;

            int free = -1;
            float face = float.MaxValue;
            for (int i = 0; i < Rungs; i++)
            {
                if (!Blocked[i]) { free = i; break; }
                face = Mathf.Min(face, Distance[i]);
            }
            var profile = new ObstacleProfile { Blocked = true };
            if (free < 0)
            {
                // A wall all the way up the ladder.
                profile.TopHeight = Rung(Rungs - 1) + RungStep;
                plan.Profile = profile;
                plan.Move = Parkour.Classify(profile, crouched, rules);
                return plan;
            }
            bool lintel = false;
            for (int i = free + 1; i < Rungs; i++)
                if (Blocked[i] && Mathf.Abs(Distance[i] - face) < FaceSlack) { lintel = true; break; }

            // The top: down from just under whatever is over the gap, a little way into the obstacle.
            var into = feet + forward * (face + IntoFace);
            float gapLow = Rung(free);
            float from = ScanTop;
            if (Cast(into + Vector3.up * gapLow, Vector3.up, ScanTop - gapLow, body, out float up)) from = gapLow + up - 0.03f;
            if (!Cast(into + Vector3.up * from, Vector3.down, from + 0.3f, body, out float down)) return plan;
            float top = from - down;
            float opening = Cast(into + Vector3.up * (top + 0.02f), Vector3.up, 3f, body, out float over) ? over + 0.02f : 3f;

            // How deep the top goes: forward along it until the surface falls away.
            float scan = top + Mathf.Clamp(opening - 0.05f, 0.05f, 0.4f);
            float depth = MaxDepth;
            for (float s = IntoFace + DepthStep; s <= MaxDepth; s += DepthStep)
            {
                var p = feet + forward * (face + s) + Vector3.up * scan;
                if (!Cast(p, Vector3.down, scan - top + EdgeDrop, body, out _)) { depth = s - DepthStep * 0.5f; break; }
            }
            // Free height where the body would stand on the top.
            var standOn = feet + forward * (face + radius + 0.15f) + Vector3.up * (top + 0.02f);
            float clearance = Cast(standOn, Vector3.up, 3f, body, out float room) ? room + 0.02f : 3f;

            profile.TopHeight = top;
            profile.Depth = depth;
            profile.Opening = lintel;
            profile.OpeningHeight = opening;
            profile.Clearance = clearance;
            plan.Profile = profile;
            plan.TopY = feet.y + top;
            var move = Parkour.Classify(profile, crouched, rules);
            plan.Move = move;
            if (!Parkour.IsMotion(move)) return plan;

            Vector3 end;
            if (move == ParkourMove.Climb) end = standOn;
            else
            {
                // Over or through: down on the far side, onto whatever is there.
                var far = feet + forward * (face + depth + radius + LandingMargin);
                float start = top + Mathf.Min(0.3f, Mathf.Max(0.05f, opening - 0.1f));
                end = Cast(far + Vector3.up * start, Vector3.down, start + MaxLandingDrop, body, out float drop)
                    ? far + Vector3.up * (start - drop + 0.02f)
                    : far + Vector3.up * Mathf.Min(top, 0.2f);
            }
            if (!Free(end, radius, height, body))
            {
                plan.Move = ParkourMove.Refuse;
                return plan;
            }
            plan.End = end;
            plan.Seconds = Parkour.Duration(move, top, rules);
            if (move == ParkourMove.Window
                && Physics.Raycast(feet + Vector3.up * (top + opening * 0.5f), forward, out var glass, reach + 0.5f, GlassPane.LayerMask, QueryTriggerInteraction.Ignore))
            {
                var pane = glass.collider.GetComponentInParent<GlassPane>();
                if (pane != null && !pane.Broken) plan.Glass = pane;
            }
            return plan;
        }

        static float Rung(int i) => FirstRung + i * RungStep;

        // The nearest thing along the ray that is not the body itself (and not glass or a trigger).
        static bool Cast(Vector3 origin, Vector3 direction, float length, Transform body, out float distance)
        {
            distance = length;
            bool any = false;
            int n = Physics.RaycastNonAlloc(origin, direction, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = Hits[i];
                if (hit.collider.transform.IsChildOf(body) || hit.distance >= distance) continue;
                distance = hit.distance;
                any = true;
            }
            return any;
        }

        // Room for the body standing with its feet at `feet`.
        static bool Free(Vector3 feet, float radius, float height, Transform body)
        {
            float r = Mathf.Max(0.05f, radius - 0.03f);
            var bottom = feet + Vector3.up * (radius + 0.05f);
            var top = feet + Vector3.up * Mathf.Max(height - radius, radius + 0.06f);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!Overlaps[i].transform.IsChildOf(body)) return false;
            return true;
        }
    }
}

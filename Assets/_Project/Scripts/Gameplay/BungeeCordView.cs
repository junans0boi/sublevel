using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The bungee cord (ADR-498, #419), on every client: from its ring at the jump board's lip to the ankles of whoever is on the rope
    // (a body over the landing pad, CombatantAnimation.OnBungeeRope), read from the body's replicated place, so everyone sees the same
    // cord with nothing extra sent. Slack, it hangs in a long loop under the board (the classic J: down from the ring, round, up to
    // the ankles); taut, it is one straight line that gets thinner the more it stretches. One LineRenderer, one draw call.
    public sealed class BungeeCordView : MonoBehaviour
    {
        public const float Width = 0.05f;
        const int Points = 40;
        const float LoopSide = 0.35f;   // the two strands of the slack loop hang this far apart

        LineRenderer _line;
        readonly Vector3[] _points = new Vector3[Points];

        public bool Shown => _line != null && _line.enabled;
        public Combatant Jumper { get; private set; }

        // The ring the cord hangs from: at the board's west lip, just below its top.
        public static Vector3 Anchor(float deckY)
        {
            var pad = WaitingIsland.BungeePad;
            return new Vector3(WaitingIsland.WorldX(pad.x - WaitingIsland.JumpBoardRadius - 0.05f), deckY + BungeeCurve.Top - 0.05f, WaitingIsland.WorldZ(pad.z));
        }

        // The cord's length: the ring to the ankles of a head-down body at the moment the curve's free fall ends.
        public static float Length(float deckY)
        {
            var anchor = Anchor(deckY);
            BungeePose.Ankles(BungeePose.Flip(BungeeCurve.Top - BungeeCurve.Rope), out float up, out _);
            var pad = WaitingIsland.BungeePad;
            var ankles = new Vector3(WaitingIsland.WorldX(pad.x), deckY + BungeeCurve.Top - BungeeCurve.Rope + 0.1f + up, WaitingIsland.WorldZ(pad.z));
            return Vector3.Distance(anchor, ankles);
        }

        // Where the cord is tied on this body: the ankles of the model as BungeePose turns it.
        public static Vector3 Ankles(Transform body, float flip)
        {
            BungeePose.Ankles(flip, out float up, out float forward);
            return body.position + Vector3.up * up + body.forward * forward;
        }

        // The cord's shape into `points` (first = ring, last = ankles); returns the width. `time` makes the slack cord flutter.
        public static float Shape(Vector3 anchor, Vector3 ankles, float length, float time, Vector3[] points)
        {
            int n = points.Length;
            float d = Vector3.Distance(anchor, ankles);
            float slack = length - d;
            if (slack <= 0f)
            {
                for (int i = 0; i < n; i++) points[i] = Vector3.Lerp(anchor, ankles, i / (n - 1f));
                return Width * Mathf.Clamp(Mathf.Sqrt(length / d), 0.55f, 1f);
            }
            // Slack: the fold hangs below the ring by half of the cord plus half of the drop to the ankles (a hanging loop of rope).
            float drop = Mathf.Max(0f, anchor.y - ankles.y);
            float fold = Mathf.Max(drop, 0.5f * (length + drop));
            float r = Mathf.Min(LoopSide * 0.5f, slack * 0.25f);
            // The two strands hang side by side along the board's lip (not under the board, not through the jumper).
            var toward = Vector3.ProjectOnPlane(ankles - anchor, Vector3.up);
            var side = toward.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, toward).normalized : Vector3.forward;
            var bottomA = anchor + Vector3.down * (fold - r);
            var bottomB = bottomA + side * (2f * r);
            // Standing on the board: the cord comes up beside the lip and over its edge to the feet, not up through the planks.
            var knee = ankles.y > anchor.y - 0.5f ? anchor + side * (2f * r) : bottomB;
            float lenA = fold - r, lenArc = Mathf.PI * r, lenB = Vector3.Distance(bottomB, knee), lenC = Vector3.Distance(knee, ankles);
            float total = lenA + lenArc + lenB + lenC;
            float flutter = Mathf.Min(0.25f, slack * 0.02f);
            var across = Vector3.Cross(side, Vector3.up);
            for (int i = 0; i < n; i++)
            {
                float s = total * i / (n - 1f);
                Vector3 p;
                if (s <= lenA) p = anchor + Vector3.down * s;
                else if (s <= lenA + lenArc) { float a = (s - lenA) / r; p = bottomA + side * r + (-side * Mathf.Cos(a) + Vector3.down * Mathf.Sin(a)) * r; }
                else if (s <= lenA + lenArc + lenB) p = Vector3.Lerp(bottomB, knee, (s - lenA - lenArc) / Mathf.Max(lenB, 1e-3f));
                else p = Vector3.Lerp(knee, ankles, (s - lenA - lenArc - lenB) / Mathf.Max(lenC, 1e-3f));
                // The loose rope ripples (not at its two tied ends).
                float ends = Mathf.Sin(Mathf.PI * i / (n - 1f));
                p += across * (flutter * ends * Mathf.Sin(s * 0.45f - time * 7f));
                points[i] = p;
            }
            return Width;
        }

        void Awake()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = Points;
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
            _line.alignment = LineAlignment.View;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "BungeeCord" };
            var color = new Color(0.95f, 0.38f, 0.08f);   // safety orange: it reads against the steel and the rust
            m.SetColor("_BaseColor", color); m.color = color;
            _line.sharedMaterial = m;
            _line.enabled = false;
        }

        void LateUpdate()
        {
            Jumper = null;
            float lowest = float.MaxValue;
            if (WaitingHud.OnIsland)
                foreach (var c in Combatant.All)
                    if (c != null && !c.Dead.Value && CombatantAnimation.OnBungeeRope(c.transform.position, out float h) && h < lowest) { lowest = h; Jumper = c; }
            if (Jumper == null) { _line.enabled = false; return; }
            float deck = transform.position.y;
            var anim = Jumper.GetComponent<CombatantAnimation>();
            float flip = anim != null ? anim.BungeeFlip : BungeePose.Flip(lowest);
            var ankles = anim != null && anim.TryAnkles(out var bones) ? bones : Ankles(Jumper.transform, flip);
            float w = Shape(Anchor(deck), ankles, Length(deck), Time.time, _points);
            _line.SetPositions(_points);
            _line.startWidth = _line.endWidth = w;
            _line.enabled = true;
        }
    }
}

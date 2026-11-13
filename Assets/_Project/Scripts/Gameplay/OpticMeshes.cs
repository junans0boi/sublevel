using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Optic models built in code (ADR-058 (4), ADR-060 (6)(7)(8)): no model asset exists yet, and the two 1x sights
    // are our own designs, not copies of real products (no logos, engravings or markings anywhere).
    //  - Model(def): the optic as an object, in metres. Origin on the rail top under the middle of the mount,
    //    +z toward the muzzle, +y up. Used on the held weapon (OpticVisual), on the ground (LootView) and for icons.
    //  - Eyepiece(zoom): what a magnified scope looks like from the eye (ScopeView), built in the space of the
    //    housing camera (fixed field of view OpticLook.HousingFovDeg) with its pivot on the eye ring face.
    // Three sub-meshes: Metal (dark matte body), Glass (opaque lens faces), Window (see-through sight glass).
    // Meshes are built once per optic and kept for the session.
    public static class OpticMeshes
    {
        public const int Metal = 0, Glass = 1, Window = 2, SubMeshCount = 3;

        // Sight windows in model space: the trapezoid of the reflex sight and the hexagon of the projector sight.
        public const float ReflexWindowY = 0.023f, ReflexWindowZ = 0.008f;
        public const float ProjectorWindowY = 0.0215f, ProjectorWindowZ = 0.021f;

        static readonly Dictionary<int, Mesh> Models = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Mesh> Eyepieces = new Dictionary<int, Mesh>();

        static int Key(OpticKind kind, float zoom) => (int)kind * 10000 + Mathf.RoundToInt(zoom * 10f);

        public static Mesh Model(OpticDef def)
        {
            if (def == null) return null;
            int key = Key(def.Kind, def.Zoom);
            if (Models.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var d = new Draft();
            switch (def.Kind)
            {
                case OpticKind.Dot: Reflex(d); break;
                case OpticKind.Holo: Projector(d); break;
                default: Scope(d, def.Zoom); break;
            }
            mesh = d.ToMesh("Optic" + def.Kind + "_" + def.Zoom.ToString("0.#"));
            Models[key] = mesh;
            return mesh;
        }

        public static Mesh Eyepiece(float zoom)
        {
            int key = Key(OpticKind.Scope, zoom);
            if (Eyepieces.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var d = new Draft();
            BuildEyepiece(d, zoom);
            mesh = d.ToMesh("Eyepiece_" + zoom.ToString("0.#"));
            Eyepieces[key] = mesh;
            return mesh;
        }

        // Middle of what the eye looks through, in model space: the sight window, or the eye lens of a scope.
        public static Vector3 WindowCentre(OpticDef def)
        {
            if (def == null) return Vector3.zero;
            if (def.Kind == OpticKind.Dot) return new Vector3(0f, ReflexWindowY, ReflexWindowZ);
            if (def.Kind == OpticKind.Holo) return new Vector3(0f, ProjectorWindowY, ProjectorWindowZ);
            var body = OpticLook.BodyFor(def.Zoom);
            return new Vector3(0f, body.AxisHeight, -body.Length * 0.5f);
        }

        // ---------------------------------------------------------------- 1x sights

        // "반사 조준 사이트": a low deck on a rail clamp, a trapezoid window in an asymmetric frame (thick left post,
        // thin right post), an emitter hump at the rear and an adjuster knob on the right.
        static void Reflex(Draft d)
        {
            var id = Frame.Identity;
            d.Box(Frame.At(new Vector3(0f, 0.003f, 0f)), new Vector3(0.024f, 0.006f, 0.036f), Metal);              // clamp base
            d.Box(Frame.At(new Vector3(0.0135f, 0.0005f, 0f)), new Vector3(0.005f, 0.009f, 0.022f), Metal);        // clamp jaw
            HexBolt(d, new Vector3(0.016f, 0.001f, 0.004f), Vector3.right, 0.0032f, 0.0035f);
            HexBolt(d, new Vector3(0.016f, 0.001f, -0.006f), Vector3.right, 0.0032f, 0.0035f);
            d.Box(Frame.At(new Vector3(-0.001f, 0.0085f, -0.002f)), new Vector3(0.022f, 0.005f, 0.030f), Metal);   // deck
            d.Box(Frame.At(new Vector3(0f, 0.0135f, -0.0125f)), new Vector3(0.009f, 0.005f, 0.008f), Metal);       // emitter hump
            d.Box(Frame.At(new Vector3(-0.0125f, 0.0095f, -0.007f)), new Vector3(0.002f, 0.003f, 0.006f), Metal);   // left button

            // The window: inner trapezoid; the frame is thicker on the left than on the right.
            var inner = new[]
            {
                new Vector2(-0.0105f, 0.011f), new Vector2(0.0105f, 0.011f), new Vector2(0.0075f, 0.035f), new Vector2(-0.0075f, 0.035f),
            };
            var outer = new[]
            {
                new Vector2(-0.0140f, 0.0098f), new Vector2(0.0123f, 0.0098f), new Vector2(0.0093f, 0.0375f), new Vector2(-0.0110f, 0.0375f),
            };
            d.PolyRing(outer, inner, 0.0055f, 0.0105f, Metal, id);
            // A short visor over the top bar toward the front.
            d.Box(Frame.At(new Vector3(-0.0008f, 0.0372f, 0.0105f)), new Vector3(0.019f, 0.0016f, 0.006f), Metal);
            d.Polygon(inner, ReflexWindowZ, Window, id, true);

            // Adjuster knob on the right of the deck, turning about x: knurled side and a flat cap.
            var knob = Frame.Along(new Vector3(0.0105f, 0.0095f, -0.008f), Vector3.right);
            d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0040f, 0f), new Vector2(0.0040f, 0.0055f), new Vector2(0.0034f, 0.0062f), new Vector2(0f, 0.0062f) }, 20, Metal, knob);
            d.KnurlBand(0.0040f, 0.0005f, 0.0008f, 0.0050f, 12, Metal, knob);
        }

        // "투영 조준 사이트": a long base, a low box hood around a wide hexagonal window, a battery tube offset to the
        // left on a bridge, a clamp with two bolts on the right and two buttons on the back.
        static void Projector(Draft d)
        {
            var id = Frame.Identity;
            d.Box(Frame.At(new Vector3(0f, 0.004f, 0f)), new Vector3(0.030f, 0.008f, 0.062f), Metal);              // base
            d.Box(Frame.At(new Vector3(0.0165f, 0.002f, 0f)), new Vector3(0.004f, 0.009f, 0.030f), Metal);         // clamp jaw
            HexBolt(d, new Vector3(0.0185f, 0.002f, 0.008f), Vector3.right, 0.0032f, 0.003f);
            HexBolt(d, new Vector3(0.0185f, 0.002f, -0.008f), Vector3.right, 0.0032f, 0.003f);

            const float cy = ProjectorWindowY;
            var outer = Hexagon(0.040f, 0.027f, 0.007f, cy);
            var inner = Hexagon(0.032f, 0.019f, 0.006f, cy);
            d.PolyRing(outer, inner, -0.018f, 0.023f, Metal, id);
            // Thin lip around the front of the hood.
            d.PolyRing(Hexagon(0.0415f, 0.0285f, 0.0075f, cy), outer, 0.019f, 0.023f, Metal, id);
            d.Polygon(inner, ProjectorWindowZ, Window, id, true);

            // Battery tube on the left: a bridge from the base, the tube and a knurled end cap at the back.
            d.Box(Frame.At(new Vector3(-0.0185f, 0.0075f, -0.004f)), new Vector3(0.009f, 0.006f, 0.030f), Metal);
            var tube = Frame.Along(new Vector3(-0.0245f, 0.0105f, -0.022f), Vector3.forward);
            d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0058f, 0f), new Vector2(0.0062f, 0.001f), new Vector2(0.0062f, 0.034f), new Vector2(0.0055f, 0.036f), new Vector2(0f, 0.036f) }, 20, Metal, tube);
            var cap = Frame.Along(new Vector3(-0.0245f, 0.0105f, -0.027f), Vector3.forward);
            d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0060f, 0f), new Vector2(0.0064f, 0.0008f), new Vector2(0.0064f, 0.005f), new Vector2(0f, 0.005f) }, 20, Metal, cap);
            d.KnurlBand(0.0064f, 0.0005f, 0.0008f, 0.0045f, 14, Metal, cap);

            // Two buttons on the back face of the base.
            d.Box(Frame.At(new Vector3(-0.006f, 0.0055f, -0.0315f)), new Vector3(0.006f, 0.003f, 0.002f), Metal);
            d.Box(Frame.At(new Vector3(0.006f, 0.0055f, -0.0315f)), new Vector3(0.006f, 0.003f, 0.002f), Metal);
        }

        // A wide hexagon: pointed left and right, flat top and bottom.
        static Vector2[] Hexagon(float width, float height, float chamfer, float cy)
        {
            float w = width * 0.5f, h = height * 0.5f;
            return new[]
            {
                new Vector2(-w, cy), new Vector2(-w + chamfer, cy - h), new Vector2(w - chamfer, cy - h),
                new Vector2(w, cy), new Vector2(w - chamfer, cy + h), new Vector2(-w + chamfer, cy + h),
            };
        }

        static void HexBolt(Draft d, Vector3 at, Vector3 axis, float radius, float length)
        {
            var f = Frame.Along(at, axis);
            d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius, length), new Vector2(radius * 0.8f, length + radius * 0.15f), new Vector2(0f, length + radius * 0.15f) }, 6, Metal, f, false);
        }

        // ---------------------------------------------------------------- magnified scopes

        static void Scope(Draft d, float zoom)
        {
            var b = OpticLook.BodyFor(zoom);
            float h = b.AxisHeight, e = b.EyeRadius, t = b.TubeRadius, o = b.ObjectiveRadius;
            float zE = -b.Length * 0.5f, zO = b.Length * 0.5f;
            var axis = Frame.Along(new Vector3(0f, h, 0f), Vector3.forward);

            // The body: walked from inside the eye bore, round the outside, into the objective bore (solid on the right).
            var profile = new List<Vector2>
            {
                new Vector2(e * 0.72f, zE + 0.005f), new Vector2(e * 0.80f, zE), new Vector2(e * 0.94f, zE),
                new Vector2(e, zE + 0.0015f), new Vector2(e, zE + 0.019f), new Vector2(e * 0.96f, zE + 0.021f),
                new Vector2(t, zE + 0.036f),
            };
            if (b.ZoomRing)
            {
                // A raised zoom ring just in front of the eye bell.
                float zr = zE + 0.038f;
                profile.Add(new Vector2(t, zr));
                profile.Add(new Vector2(t * 1.32f, zr + 0.0015f));
                profile.Add(new Vector2(t * 1.32f, zr + 0.016f));
                profile.Add(new Vector2(t, zr + 0.0175f));
            }
            profile.Add(new Vector2(t, zO - 0.040f));
            profile.Add(new Vector2(o * 0.97f, zO - 0.013f));
            profile.Add(new Vector2(o, zO - 0.011f));
            profile.Add(new Vector2(o, zO - 0.0015f));
            profile.Add(new Vector2(o * 0.93f, zO));
            profile.Add(new Vector2(o * 0.80f, zO));
            profile.Add(new Vector2(o * 0.74f, zO - 0.006f));
            d.Lathe(profile.ToArray(), 32, Metal, axis);

            // Lenses: the eye lens faces back, the objective faces forward.
            d.Lathe(new[] { new Vector2(0f, zE + 0.005f), new Vector2(e * 0.72f, zE + 0.005f) }, 32, Glass, axis);
            d.Lathe(new[] { new Vector2(o * 0.74f, zO - 0.006f), new Vector2(0f, zO - 0.006f) }, 32, Glass, axis);

            if (b.Turrets) d.KnurlBand(e, 0.0008f, zE + 0.004f, zE + 0.016f, 30, Metal, axis);
            else d.KnurlBand(e, 0.0006f, zE + 0.006f, zE + 0.013f, 24, Metal, axis);
            if (b.ZoomRing)
            {
                d.KnurlBand(t * 1.32f, 0.0007f, zE + 0.042f, zE + 0.051f, 26, Metal, axis);
                d.Box(Frame.At(new Vector3(t * 1.32f + 0.003f, h, zE + 0.046f)), new Vector3(0.006f, 0.003f, 0.004f), Metal);   // throw lever
            }

            if (b.TopDial)
            {
                // 4x: a block on top of the tube with a knurled dial, and a small battery cap on the left.
                float zd = -b.Length * 0.06f;
                d.Box(Frame.At(new Vector3(0f, h + t * 0.6f, zd)), new Vector3(0.016f, t * 1.4f + 0.004f, 0.020f), Metal);
                var dial = Frame.Along(new Vector3(0f, h + t * 1.3f + 0.002f, zd), Vector3.up);
                d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0078f, 0f), new Vector2(0.0078f, 0.0085f), new Vector2(0.0068f, 0.0095f), new Vector2(0f, 0.0095f) }, 24, Metal, dial);
                d.KnurlBand(0.0078f, 0.0006f, 0.001f, 0.0075f, 18, Metal, dial);
                var side = Frame.Along(new Vector3(-(t * 0.6f + 0.008f), h + t * 0.4f, zd), Vector3.left);
                d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0050f, 0f), new Vector2(0.0050f, 0.004f), new Vector2(0f, 0.004f) }, 16, Metal, side);
            }

            if (b.Turrets)
            {
                // Saddle with turrets on top and on the right; the long scopes get a small knob on the left too.
                float zs = b.Length * 0.02f;
                float s = t + 0.0035f;
                d.Box(Frame.At(new Vector3(0f, h, zs)), new Vector3(s * 2f, s * 2f, 0.024f), Metal);
                Turret(d, new Vector3(0f, h + s, zs), Vector3.up, 0.0085f);
                Turret(d, new Vector3(s, h, zs), Vector3.right, 0.0085f);
                if (zoom >= 7f)
                {
                    var knob = Frame.Along(new Vector3(-s, h, zs), Vector3.left);
                    d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0065f, 0f), new Vector2(0.0065f, 0.007f), new Vector2(0.0055f, 0.008f), new Vector2(0f, 0.008f) }, 20, Metal, knob);
                }
            }

            if (b.FlipCaps)
            {
                // 2x, 3x: open flip caps. The eye cap swings out to the right on a vertical hinge, the front cap stands up.
                FlipCap(d, new Vector3(e, h, zE), Vector3.up, e * 1.03f, new Vector3(-e, 0f, -0.0015f), Quaternion.AngleAxis(120f, Vector3.up));
                FlipCap(d, new Vector3(0f, h + o, zO), Vector3.right, o * 1.03f, new Vector3(0f, -o, 0.0015f), Quaternion.AngleAxis(-115f, Vector3.right));
            }

            if (b.RingMounts) RingMounts(d, b, h, t);
            else BlockMount(d, b, h, t);
        }

        static void Turret(Draft d, Vector3 at, Vector3 axis, float radius)
        {
            var f = Frame.Along(at, axis);
            d.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(radius * 1.05f, 0f), new Vector2(radius * 1.05f, 0.004f), new Vector2(radius, 0.0045f),
                new Vector2(radius, 0.0125f), new Vector2(radius * 0.85f, 0.0135f), new Vector2(0f, 0.0135f),
            }, 24, Metal, f);
            d.KnurlBand(radius, 0.0006f, 0.005f, 0.012f, 20, Metal, f);
        }

        // A thin disc with a hinge block. `closedCentre` is the cap's centre relative to the hinge when shut;
        // `open` swings it about the hinge.
        static void FlipCap(Draft d, Vector3 hinge, Vector3 hingeAxis, float radius, Vector3 closedCentre, Quaternion open)
        {
            var centre = hinge + open * closedCentre;
            var f = new Frame(centre, open);
            d.Lathe(new[] { new Vector2(0f, -0.0015f), new Vector2(radius, -0.0015f), new Vector2(radius, 0.0015f), new Vector2(radius * 0.86f, 0.0015f), new Vector2(radius * 0.86f, 0.0008f), new Vector2(0f, 0.0008f) }, 24, Metal, f);
            d.Box(new Frame(hinge, Quaternion.FromToRotation(Vector3.up, hingeAxis)), new Vector3(0.004f, 0.007f, 0.004f), Metal);
        }

        // 6x, 8x, 16x: two rings round the tube, each on a leg with a clamp base; two bolts on every ring cap and a
        // clamp bolt on the right of every base.
        static void RingMounts(Draft d, ScopeBody b, float h, float t)
        {
            for (int i = -1; i <= 1; i += 2)
            {
                float z = i * b.Length * 0.24f;
                var ring = Frame.Along(new Vector3(0f, h, z - 0.005f), Vector3.forward);
                float r = t + 0.003f;
                d.Lathe(new[] { new Vector2(t, 0f), new Vector2(r, 0f), new Vector2(r, 0.010f), new Vector2(t, 0.010f) }, 28, Metal, ring);
                float legHeight = Mathf.Max(0.002f, h - t);
                d.Box(Frame.At(new Vector3(0f, legHeight * 0.5f, z)), new Vector3(0.012f, legHeight, 0.010f), Metal);
                d.Box(Frame.At(new Vector3(0f, 0.002f, z)), new Vector3(0.020f, 0.004f, 0.013f), Metal);
                HexBolt(d, new Vector3(r - 0.0005f, h + 0.0025f, z), Vector3.right, 0.0018f, 0.0018f);
                HexBolt(d, new Vector3(-(r - 0.0005f), h + 0.0025f, z), Vector3.left, 0.0018f, 0.0018f);
                HexBolt(d, new Vector3(0.010f, 0.002f, z), Vector3.right, 0.0025f, 0.0025f);
            }
        }

        // 2x, 3x, 4x: one block under the tube with a clamp jaw and two bolt heads on the right.
        static void BlockMount(Draft d, ScopeBody b, float h, float t)
        {
            float height = Mathf.Max(0.003f, h - t * 0.6f);
            float length = b.Length * 0.42f;
            d.Box(Frame.At(new Vector3(0f, height * 0.5f, 0f)), new Vector3(0.016f, height, length), Metal);
            d.Box(Frame.At(new Vector3(0.0100f, 0.002f, 0f)), new Vector3(0.004f, 0.008f, length * 0.9f), Metal);
            HexBolt(d, new Vector3(0.012f, 0.002f, length * 0.28f), Vector3.right, 0.0028f, 0.0025f);
            HexBolt(d, new Vector3(0.012f, 0.002f, -length * 0.28f), Vector3.right, 0.0028f, 0.0025f);
        }

        // ---------------------------------------------------------------- the eyepiece seen from the eye

        // Housing space: the housing camera at the origin looking along +z. Every radius is chosen as a share of the
        // screen height at its depth (OpticLook), so the lens, the funnel and the eye ring land exactly where the
        // screen overlay expects them. The mesh pivot is moved to the eye ring face (z = RimDistance).
        static void BuildEyepiece(Draft d, float zoom)
        {
            var look = OpticLook.For(zoom);
            var axis = Frame.Identity;
            float zR = OpticLook.RimDistance, zL = zR + look.Depth;
            float lens = look.LensRadius, rim = look.RimRadius, outer = look.OuterRadius;

            // The funnel from the lens out to the eye ring, with two rear-facing lens-cell steps.
            var bore = new List<Vector2>();
            void At(float g, float z) => bore.Add(new Vector2(OpticLook.HousingRadius(lens + (rim - lens) * g, z), z));
            float z1 = Mathf.Lerp(zL, zR, 0.40f), z2 = Mathf.Lerp(zL, zR, 0.78f);
            At(0f, zL);
            At(0.06f, zL - 0.002f);
            At(0.36f, z1);
            At(0.46f, z1);
            At(0.74f, z2);
            At(0.84f, z2);
            At(1f, zR + 0.004f);

            float rimFace = OpticLook.HousingRadius(rim * 1.035f, zR);
            bore.Add(new Vector2(rimFace, zR));
            float faceZ = zR;
            if (look.Knurled)
            {
                // Plain lip, then the knurled face as its own piece, then the outer chamfer.
                bore.Add(new Vector2(OpticLook.HousingRadius(rim * 1.10f, zR), zR));
                d.Lathe(bore.ToArray(), 64, Metal, axis);
                d.KnurlFace(OpticLook.HousingRadius(rim * 1.10f, zR), OpticLook.HousingRadius(outer * 0.95f, zR), zR, 0.0022f, 44, Metal, axis);
                bore.Clear();
                bore.Add(new Vector2(OpticLook.HousingRadius(outer * 0.95f, zR), zR));
            }
            else if (look.DoubleBand)
            {
                // Two bands: the inner one nearer the eye, the outer one stepped back behind a dark groove.
                float split = rim + (outer - rim) * 0.45f;
                bore.Add(new Vector2(OpticLook.HousingRadius(split, zR), zR));
                faceZ = zR + 0.006f;
                bore.Add(new Vector2(OpticLook.HousingRadius(split, zR), faceZ));
                bore.Add(new Vector2(OpticLook.HousingRadius(split + 0.025f, faceZ), faceZ));
                bore.Add(new Vector2(OpticLook.HousingRadius(split + 0.025f, faceZ), faceZ - 0.0015f));
                bore.Add(new Vector2(OpticLook.HousingRadius(outer * 0.96f, faceZ), faceZ - 0.0015f));
                faceZ -= 0.0015f;
            }
            else
            {
                // One face with a shallow groove ring.
                float groove = rim + (outer - rim) * 0.55f;
                bore.Add(new Vector2(OpticLook.HousingRadius(groove, zR), zR));
                bore.Add(new Vector2(OpticLook.HousingRadius(groove, zR), zR + 0.0015f));
                bore.Add(new Vector2(OpticLook.HousingRadius(groove + 0.02f, zR), zR + 0.0015f));
                bore.Add(new Vector2(OpticLook.HousingRadius(groove + 0.02f, zR), zR));
                bore.Add(new Vector2(OpticLook.HousingRadius(outer * 0.965f, zR), zR));
            }
            float rOuter = OpticLook.HousingRadius(outer, zR);
            bore.Add(new Vector2(rOuter, faceZ + 0.004f));
            bore.Add(new Vector2(rOuter, zR + 0.05f));
            d.Lathe(bore.ToArray(), 64, Metal, axis);

            if (look.BoxBody)
            {
                // 4x: a wide rounded-square body face behind the eye ring that covers much of the screen, with four bolts.
                float zb = zR + 0.012f;
                float hb = OpticLook.ViewHeightAt(zb, OpticLook.HousingFovDeg);
                float hw = 0.86f * hb, hh = 0.62f * hb, c = 0.16f * hb;
                var outerBox = new[]
                {
                    new Vector2(-hw + c, -hh), new Vector2(hw - c, -hh), new Vector2(hw, -hh + c), new Vector2(hw, hh - c),
                    new Vector2(hw - c, hh), new Vector2(-hw + c, hh), new Vector2(-hw, hh - c), new Vector2(-hw, -hh + c),
                };
                // The hole: an octagon round the eye ring, rotated to line up with the corners of the box.
                float hole = rOuter * 0.995f / Mathf.Cos(Mathf.PI / 8f);
                var holeBox = new Vector2[8];
                for (int i = 0; i < 8; i++)
                {
                    float a = -Mathf.PI * 0.5f - Mathf.PI / 8f + i * Mathf.PI / 4f;
                    holeBox[i] = new Vector2(Mathf.Cos(a) * hole, Mathf.Sin(a) * hole);
                }
                d.PolyRing(outerBox, holeBox, zb, zb + 0.05f, Metal, axis);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        HexBolt(d, new Vector3(sx * hw * 0.72f, sy * hh * 0.55f, zb), Vector3.back, 0.03f * hb, 0.012f * hb);
            }

            if (look.FlipCap)
            {
                // 2x, 3x: an open flip cap hanging at the left of the eye ring, seen at an angle, on a hinge arm.
                float zc = zR + 0.012f;
                float hc = OpticLook.ViewHeightAt(zc, OpticLook.HousingFovDeg);
                float capRadius = 0.19f * hc;
                var centre = new Vector3(-(outer + 0.21f) * hc, -0.02f * hc, zc);
                var capFrame = new Frame(centre, Quaternion.AngleAxis(25f, Vector3.up));
                d.Lathe(new[] { new Vector2(0f, -0.002f), new Vector2(capRadius, -0.002f), new Vector2(capRadius, 0.002f), new Vector2(capRadius * 0.85f, 0.002f), new Vector2(capRadius * 0.85f, 0.0005f), new Vector2(0f, 0.0005f) }, 40, Metal, capFrame);
                float armFrom = OpticLook.HousingRadius(outer * 0.97f, zR);
                float armTo = (outer + 0.05f) * hc;
                d.Box(Frame.At(new Vector3(-(armFrom + armTo) * 0.5f, -0.02f * hc, zR + 0.006f)), new Vector3(armTo - armFrom, 0.05f * hc, 0.006f), Metal);
            }

            if (look.SideTurret)
            {
                // 8x, 16x: the side turret shows at the right edge; a smaller knob at the left.
                float zt = zR + 0.035f;
                float ht = OpticLook.ViewHeightAt(zt, OpticLook.HousingFovDeg);
                float start = OpticLook.HousingRadius(outer * 0.9f, zR);
                float r = 0.11f * ht;
                var right = Frame.Along(new Vector3(start, 0f, zt), Vector3.right);
                float length = (outer + 0.17f) * ht - start;
                d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(r * 1.1f, 0f), new Vector2(r * 1.1f, length * 0.35f), new Vector2(r, length * 0.38f), new Vector2(r, length * 0.96f), new Vector2(r * 0.86f, length), new Vector2(0f, length) }, 32, Metal, right);
                d.KnurlBand(r, r * 0.06f, length * 0.42f, length * 0.92f, 22, Metal, right);
                var left = Frame.Along(new Vector3(-start, 0f, zt), Vector3.left);
                float lengthLeft = (outer + 0.08f) * ht - start;
                d.Lathe(new[] { new Vector2(0f, 0f), new Vector2(r * 0.8f, 0f), new Vector2(r * 0.8f, lengthLeft * 0.9f), new Vector2(r * 0.68f, lengthLeft), new Vector2(0f, lengthLeft) }, 28, Metal, left);
            }

            d.Offset(new Vector3(0f, 0f, -zR));
        }

        // ---------------------------------------------------------------- mesh drafting

        // A position and orientation for a part: local z is the part's axis.
        public readonly struct Frame
        {
            public readonly Vector3 Origin;
            public readonly Quaternion Rotation;

            public Frame(Vector3 origin, Quaternion rotation)
            {
                Origin = origin;
                Rotation = rotation;
            }

            public static Frame Identity => new Frame(Vector3.zero, Quaternion.identity);
            public static Frame At(Vector3 origin) => new Frame(origin, Quaternion.identity);
            // Local z along `axis`.
            public static Frame Along(Vector3 origin, Vector3 axis) => new Frame(origin, Quaternion.FromToRotation(Vector3.forward, axis));

            public Vector3 Point(Vector3 local) => Origin + Rotation * local;
            public Vector3 Dir(Vector3 local) => Rotation * local;
        }

        sealed class Draft
        {
            const float UvPerMetre = 20f;
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int>[] _t = { new List<int>(), new List<int>(), new List<int>() };

            // A flat quad a-b-c-d (in order round its edge) facing the side `facing` points to.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, int sub)
            {
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-16f) n = Vector3.Cross(c - a, d - a);
                if (n.sqrMagnitude < 1e-16f) return;
                if (Vector3.Dot(n, facing) < 0f)
                {
                    (b, d) = (d, b);
                    n = -n;
                }
                n.Normalize();
                Emit(a, b, c, d, n, n, n, n, sub);
            }

            // A quad with a normal per corner (smooth shading round a lathe); faces the way the normals do.
            void SmoothQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, int sub)
            {
                var flat = Vector3.Cross(b - a, c - a);
                if (flat.sqrMagnitude < 1e-16f) flat = Vector3.Cross(c - a, d - a);
                if (flat.sqrMagnitude < 1e-16f) return;
                if (Vector3.Dot(flat, na + nb + nc + nd) < 0f)
                {
                    (b, d) = (d, b);
                    (nb, nd) = (nd, nb);
                }
                Emit(a, b, c, d, na, nb, nc, nd, sub);
            }

            void Emit(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, int sub)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                _n.Add(na); _n.Add(nb); _n.Add(nc); _n.Add(nd);
                // Planar texture coordinates on the plane the quad faces most.
                var n = na + nb + nc + nd;
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                _uv.Add(Uv(a, ax, ay, az)); _uv.Add(Uv(b, ax, ay, az)); _uv.Add(Uv(c, ax, ay, az)); _uv.Add(Uv(d, ax, ay, az));
                var t = _t[sub];
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            static Vector2 Uv(Vector3 p, float ax, float ay, float az)
            {
                if (ax >= ay && ax >= az) return new Vector2(p.z, p.y) * UvPerMetre;
                if (ay >= az) return new Vector2(p.x, p.z) * UvPerMetre;
                return new Vector2(p.x, p.y) * UvPerMetre;
            }

            // Revolves `profile` (x = radius, y = position along the axis) round the frame's z axis. Walk the
            // profile with the solid on the right (axis position to the right, radius up): every face then looks out
            // of the solid. `smooth` shades round the axis without facets.
            public void Lathe(Vector2[] profile, int segments, int sub, in Frame f, bool smooth = true)
            {
                float step = Mathf.PI * 2f / segments;
                for (int p = 0; p + 1 < profile.Length; p++)
                {
                    Vector2 p0 = profile[p], p1 = profile[p + 1];
                    var n2 = new Vector2(p1.y - p0.y, -(p1.x - p0.x));
                    if (n2.sqrMagnitude < 1e-16f) continue;
                    n2.Normalize();
                    for (int s = 0; s < segments; s++)
                    {
                        float a0 = s * step, a1 = (s + 1) * step;
                        float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                        var v00 = f.Point(new Vector3(c0 * p0.x, s0 * p0.x, p0.y));
                        var v01 = f.Point(new Vector3(c1 * p0.x, s1 * p0.x, p0.y));
                        var v11 = f.Point(new Vector3(c1 * p1.x, s1 * p1.x, p1.y));
                        var v10 = f.Point(new Vector3(c0 * p1.x, s0 * p1.x, p1.y));
                        if (smooth)
                        {
                            var n0 = f.Dir(new Vector3(c0 * n2.x, s0 * n2.x, n2.y));
                            var n1 = f.Dir(new Vector3(c1 * n2.x, s1 * n2.x, n2.y));
                            SmoothQuad(v00, v01, v11, v10, n0, n1, n1, n0, sub);
                        }
                        else
                        {
                            float am = (a0 + a1) * 0.5f;
                            Quad(v00, v01, v11, v10, f.Dir(new Vector3(Mathf.Cos(am) * n2.x, Mathf.Sin(am) * n2.x, n2.y)), sub);
                        }
                    }
                }
            }

            static Vector3 Ring(in Frame f, float angle, float radius, float z) =>
                f.Point(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z));

            // Knurling as bevel strips: `ridges` raised flat strips (radius r + depth) between sunken ones (radius r)
            // round the frame's z axis from z0 to z1.
            public void KnurlBand(float r, float depth, float z0, float z1, int ridges, int sub, in Frame f)
            {
                int sectors = ridges * 2;
                float step = Mathf.PI * 2f / sectors;
                float top = r + depth;
                for (int s = 0; s < sectors; s++)
                {
                    bool raised = (s & 1) == 0;
                    float rr = raised ? top : r;
                    float a0 = s * step, a1 = (s + 1) * step, am = (a0 + a1) * 0.5f;
                    var radial = f.Dir(new Vector3(Mathf.Cos(am), Mathf.Sin(am), 0f));
                    Quad(Ring(f, a0, rr, z0), Ring(f, a1, rr, z0), Ring(f, a1, rr, z1), Ring(f, a0, rr, z1), radial, sub);
                    if (raised)
                    {
                        Quad(Ring(f, a0, r, z0), Ring(f, a1, r, z0), Ring(f, a1, top, z0), Ring(f, a0, top, z0), f.Dir(Vector3.back), sub);
                        Quad(Ring(f, a0, r, z1), Ring(f, a1, r, z1), Ring(f, a1, top, z1), Ring(f, a0, top, z1), f.Dir(Vector3.forward), sub);
                    }
                    // The wall at a1 between this strip and the next.
                    var tangent = f.Dir(new Vector3(-Mathf.Sin(a1), Mathf.Cos(a1), 0f)) * (raised ? 1f : -1f);
                    Quad(Ring(f, a1, r, z0), Ring(f, a1, top, z0), Ring(f, a1, top, z1), Ring(f, a1, r, z1), tangent, sub);
                }
            }

            // Knurling on a face looking back along the axis (toward -z): raised sectors stand `depth` nearer the eye.
            public void KnurlFace(float r0, float r1, float z, float depth, int ridges, int sub, in Frame f)
            {
                int sectors = ridges * 2;
                float step = Mathf.PI * 2f / sectors;
                float near = z - depth;
                for (int s = 0; s < sectors; s++)
                {
                    bool raised = (s & 1) == 0;
                    float zz = raised ? near : z;
                    float a0 = s * step, a1 = (s + 1) * step, am = (a0 + a1) * 0.5f;
                    Quad(Ring(f, a0, r0, zz), Ring(f, a1, r0, zz), Ring(f, a1, r1, zz), Ring(f, a0, r1, zz), f.Dir(Vector3.back), sub);
                    if (raised)
                    {
                        var radial = f.Dir(new Vector3(Mathf.Cos(am), Mathf.Sin(am), 0f));
                        Quad(Ring(f, a0, r0, z), Ring(f, a1, r0, z), Ring(f, a1, r0, near), Ring(f, a0, r0, near), -radial, sub);
                        Quad(Ring(f, a0, r1, z), Ring(f, a1, r1, z), Ring(f, a1, r1, near), Ring(f, a0, r1, near), radial, sub);
                    }
                    var tangent = f.Dir(new Vector3(-Mathf.Sin(a1), Mathf.Cos(a1), 0f)) * (raised ? 1f : -1f);
                    Quad(Ring(f, a1, r0, z), Ring(f, a1, r1, z), Ring(f, a1, r1, near), Ring(f, a1, r0, near), tangent, sub);
                }
            }

            // A box centred on the frame origin, edges along the frame's axes.
            public void Box(in Frame f, Vector3 size, int sub)
            {
                var h = size * 0.5f;
                var fr = f;
                Vector3 P(float x, float y, float z) => fr.Point(new Vector3(x * h.x, y * h.y, z * h.z));
                Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), fr.Dir(Vector3.right), sub);
                Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), fr.Dir(Vector3.left), sub);
                Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), fr.Dir(Vector3.up), sub);
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), fr.Dir(Vector3.down), sub);
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), fr.Dir(Vector3.forward), sub);
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), fr.Dir(Vector3.back), sub);
            }

            // A frame between two convex polygons (same vertex count, same winding) in the frame's xy plane,
            // extruded along z from z0 to z1: outer walls, inner walls and both end faces.
            public void PolyRing(Vector2[] outer, Vector2[] inner, float z0, float z1, int sub, in Frame f)
            {
                var centre = Vector2.zero;
                foreach (var p in inner) centre += p;
                centre /= inner.Length;
                int n = outer.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector2 o0 = outer[i], o1 = outer[j], i0 = inner[i], i1 = inner[j];
                    var outward = (o0 + o1) * 0.5f - centre;
                    var inward = centre - (i0 + i1) * 0.5f;
                    Quad(f.Point(new Vector3(o0.x, o0.y, z0)), f.Point(new Vector3(o1.x, o1.y, z0)), f.Point(new Vector3(o1.x, o1.y, z1)), f.Point(new Vector3(o0.x, o0.y, z1)), f.Dir(outward), sub);
                    Quad(f.Point(new Vector3(i0.x, i0.y, z0)), f.Point(new Vector3(i1.x, i1.y, z0)), f.Point(new Vector3(i1.x, i1.y, z1)), f.Point(new Vector3(i0.x, i0.y, z1)), f.Dir(inward), sub);
                    Quad(f.Point(new Vector3(o0.x, o0.y, z0)), f.Point(new Vector3(o1.x, o1.y, z0)), f.Point(new Vector3(i1.x, i1.y, z0)), f.Point(new Vector3(i0.x, i0.y, z0)), f.Dir(Vector3.back), sub);
                    Quad(f.Point(new Vector3(o0.x, o0.y, z1)), f.Point(new Vector3(o1.x, o1.y, z1)), f.Point(new Vector3(i1.x, i1.y, z1)), f.Point(new Vector3(i0.x, i0.y, z1)), f.Dir(Vector3.forward), sub);
                }
            }

            // A flat convex polygon at depth z; both sides when `twoSided` (sight glass seen from front and back).
            public void Polygon(Vector2[] points, float z, int sub, in Frame f, bool twoSided)
            {
                var centre = Vector2.zero;
                foreach (var p in points) centre += p;
                centre /= points.Length;
                var c = f.Point(new Vector3(centre.x, centre.y, z));
                for (int i = 0; i < points.Length; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Length];
                    var pa = f.Point(new Vector3(a.x, a.y, z));
                    var pb = f.Point(new Vector3(b.x, b.y, z));
                    Quad(c, pa, pb, c, f.Dir(Vector3.back), sub);
                    if (twoSided) Quad(c, pa, pb, c, f.Dir(Vector3.forward), sub);
                }
            }

            public void Offset(Vector3 by)
            {
                for (int i = 0; i < _v.Count; i++) _v[i] += by;
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
                if (_v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.subMeshCount = SubMeshCount;
                for (int i = 0; i < SubMeshCount; i++) mesh.SetTriangles(_t[i], i, false);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}

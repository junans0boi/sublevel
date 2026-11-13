using System;

namespace Game.Core
{
    // The reticle drawn in a magnified lens (ADR-060 (2)(3)): lit parts amber, lines black.
    //  Arc      = lit arc + dot + black ranging ticks (3x; 2x uses it too)
    //  Post     = black crosshair + thick lower post + lit dot (4x)
    //  Fine     = thin black crosshair (6x)
    //  Holdover = thin crosshair + short horizontal holdover lines below the centre (8x; 15x uses it too)
    public enum ScopeReticle { Arc = 0, Post = 1, Fine = 2, Holdover = 3 }

    // What the eyepiece around the lens looks like from the eye, per magnification (ADR-060 (2)(7)).
    // Radii are fractions of the SCREEN HEIGHT, so the picture is the same at 1280x720 and 2560x1440.
    public struct ScopeLook
    {
        public float LensRadius;   // the clear lens window
        public float RimRadius;    // where the funnel of the eyepiece meets the eye ring
        public float OuterRadius;  // outer edge of the eye ring; the zoomed scene shows beyond it (darkened)
        public float Depth;        // metres from the eye ring to the lens in housing space: short (3x) .. long (4x, 8x)
        public ScopeReticle Reticle;
        public bool FlipCap;       // 2x, 3x: an open flip cap at the side of the eye ring
        public bool BoxBody;       // 4x: a wide rounded-square body face around the eye ring
        public bool DoubleBand;    // 6x: a thick eye ring in two bands (the zoom ring look)
        public bool Knurled;       // 8x, 15x: thick knurled eye ring
        public bool SideTurret;    // 8x, 15x: a turret showing at the right
    }

    // The shape of a magnified scope as a 3D model on the weapon / on the ground / in an icon (metres).
    public struct ScopeBody
    {
        public float Length;          // eye ring to objective
        public float TubeRadius;
        public float ObjectiveRadius; // front bell
        public float EyeRadius;       // rear bell
        public float AxisHeight;      // tube axis above the rail
        public bool FlipCaps, TopDial, ZoomRing, RingMounts, Turrets;
    }

    // Pure numbers for the optic visuals, shared by the screen overlay, the eyepiece housing, the models and tests.
    public static class OpticLook
    {
        // The eyepiece housing is drawn by its own camera with this fixed vertical field of view (ScopeView), so its
        // size on screen does not change with the zoom of the main camera.
        public const float HousingFovDeg = 40f;
        // Distance from that camera to the eye ring face.
        public const float RimDistance = 0.07f;

        static readonly float[] Zooms = { 2f, 3f, 4f, 6f, 8f, 15f };
        // Lens radius falls from 3x to 4x (small window, body covers much) and grows again for 6x and 8x (large lens).
        static readonly float[] Lens = { 0.30f, 0.28f, 0.21f, 0.34f, 0.38f, 0.40f };
        static readonly float[] Rim = { 0.38f, 0.37f, 0.30f, 0.43f, 0.47f, 0.49f };
        static readonly float[] Outer = { 0.50f, 0.57f, 0.44f, 0.66f, 0.80f, 0.84f };
        static readonly float[] Depth = { 0.05f, 0.04f, 0.11f, 0.08f, 0.10f, 0.12f };

        // Scope bodies on the weapon: 3x short and chunky, 4x long and slim, 6x thick, 8x long sniper scope.
        static readonly float[] BodyLength = { 0.105f, 0.115f, 0.175f, 0.195f, 0.270f, 0.330f };
        static readonly float[] BodyTube = { 0.0125f, 0.0150f, 0.0105f, 0.0160f, 0.0135f, 0.0145f };
        static readonly float[] BodyObjective = { 0.017f, 0.021f, 0.015f, 0.024f, 0.025f, 0.029f };
        static readonly float[] BodyEye = { 0.017f, 0.020f, 0.016f, 0.021f, 0.020f, 0.021f };

        // Piecewise-linear in zoom between the keys, clamped at both ends.
        static float Sample(float[] values, float zoom)
        {
            if (!(zoom > Zooms[0])) return values[0];
            for (int i = 1; i < Zooms.Length; i++)
            {
                if (zoom <= Zooms[i])
                {
                    float t = (zoom - Zooms[i - 1]) / (Zooms[i] - Zooms[i - 1]);
                    return values[i - 1] + (values[i] - values[i - 1]) * t;
                }
            }
            return values[values.Length - 1];
        }

        public static ScopeReticle ReticleFor(float zoom) =>
            zoom < 3.5f ? ScopeReticle.Arc : zoom < 5f ? ScopeReticle.Post : zoom < 7f ? ScopeReticle.Fine : ScopeReticle.Holdover;

        public static ScopeLook For(float zoom)
        {
            var reticle = ReticleFor(zoom);
            return new ScopeLook
            {
                LensRadius = Sample(Lens, zoom),
                RimRadius = Sample(Rim, zoom),
                OuterRadius = Sample(Outer, zoom),
                Depth = Sample(Depth, zoom),
                Reticle = reticle,
                FlipCap = reticle == ScopeReticle.Arc,
                BoxBody = reticle == ScopeReticle.Post,
                DoubleBand = reticle == ScopeReticle.Fine,
                Knurled = reticle == ScopeReticle.Holdover,
                SideTurret = reticle == ScopeReticle.Holdover,
            };
        }

        public static ScopeBody BodyFor(float zoom)
        {
            var reticle = ReticleFor(zoom);
            float tube = Sample(BodyTube, zoom);
            float eye = Sample(BodyEye, zoom);
            return new ScopeBody
            {
                Length = Sample(BodyLength, zoom),
                TubeRadius = tube,
                ObjectiveRadius = Sample(BodyObjective, zoom),
                EyeRadius = eye,
                AxisHeight = Math.Max(eye, Sample(BodyObjective, zoom)) + 0.008f,
                FlipCaps = reticle == ScopeReticle.Arc,
                TopDial = reticle == ScopeReticle.Post,
                ZoomRing = reticle == ScopeReticle.Fine,
                RingMounts = reticle == ScopeReticle.Fine || reticle == ScopeReticle.Holdover,
                Turrets = reticle == ScopeReticle.Holdover || reticle == ScopeReticle.Fine,
            };
        }

        // Lens window radius in pixels (or canvas units) for a screen `screenHeight` tall.
        public static float LensRadiusPixels(float zoom, float screenHeight) => For(zoom).LensRadius * screenHeight;

        // Height of the view at `distance` in front of a camera with vertical field of view `fovDeg`.
        public static float ViewHeightAt(float distance, float fovDeg) =>
            (float)(2.0 * distance * Math.Tan(fovDeg * 0.5 * Math.PI / 180.0));

        // The radius in metres that covers `fraction` of the screen height at `distance` in front of the housing camera.
        public static float HousingRadius(float fraction, float distance) => fraction * ViewHeightAt(distance, HousingFovDeg);
    }
}

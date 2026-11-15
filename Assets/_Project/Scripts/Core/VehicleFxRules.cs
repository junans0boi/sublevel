using System;

namespace Game.Core
{
    // Pure rules behind the vehicle lights and the tyre / engine effects (#202, #203, ADR-155); the views
    // (VehicleLights, VehicleTyreFx, VehicleAudio) only draw and play what these say.
    public static class VehicleFxRules
    {
        // ---- Lights ----

        public const byte LightsAuto = 0, LightsOn = 1, LightsOff = 2;

        // The headlamps come on by themselves when it is dusk or night, or when rain / fog dims the day.
        // `ambient` is SkyLook.Ambient (1 = full day); `weatherDimmed` = rain or fog.
        public static bool AutoLights(float ambient, bool weatherDimmed) => ambient < 0.62f || (weatherDimmed && ambient < 0.95f);

        // The lamps are lit only with the engine running; the driver's L key can force them on or off.
        public static bool LightsLit(byte mode, bool auto, bool engineOn) =>
            engineOn && (mode == LightsOn || (mode == LightsAuto && auto));

        // What a press of L asks for: the opposite of what shows now (back to Auto when that already gives it).
        public static byte ToggleMode(byte mode, bool auto, bool engineOn)
        {
            bool want = !LightsLit(mode, auto, engineOn);
            if (want == auto) return LightsAuto;
            return want ? LightsOn : LightsOff;
        }

        // The light budget: which of the lit vehicles (by squared distance to the camera) get real lights, the nearest `max`.
        public static bool[] NearestAllowed(float[] sqrDistances, int max)
        {
            var allowed = new bool[sqrDistances.Length];
            var order = new int[sqrDistances.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => sqrDistances[a].CompareTo(sqrDistances[b]));
            for (int i = 0; i < order.Length && i < max; i++) allowed[order[i]] = true;
            return allowed;
        }

        // Brake lights: slowing down harder than the car coasts, or the handbrake. `speed` is signed along the car's
        // forward axis, `accel` its change per second (also signed along forward).
        public const float BrakeAccel = 2.2f;
        public static bool Braking(float speed, float accel, bool handbrake) =>
            Math.Abs(speed) > 0.8f && (handbrake || (speed > 0f ? accel < -BrakeAccel : accel > BrakeAccel));

        public static bool Reversing(float speed) => speed < -0.4f;

        // Indicators blink while the wheel is held well over at walking-to-town speed (a junction, a parking turn).
        // -1 left, +1 right, 0 none. `steerDeg` is the front wheels' angle, right positive.
        public static int Indicator(float steerDeg, float speed) =>
            Math.Abs(speed) > 0.5f && Math.Abs(speed) < 14f && Math.Abs(steerDeg) > 9f ? (steerDeg > 0f ? 1 : -1) : 0;

        // 1.5 Hz on/off.
        public static bool BlinkOn(float time) => (int)Math.Floor(time * 3f) % 2 == 0;

        // ---- Tyres ----

        // How hard the tyres slide, 0..1: sideways speed (drift), braking beyond what grip holds (`decel` = slowing
        // down, m/s2, positive), or the handbrake at speed.
        public static float Slip(float lateralSpeed, float decel, float speed, bool handbrake)
        {
            speed = Math.Abs(speed);
            float side = Math.Max(0f, (Math.Abs(lateralSpeed) - 1.2f) / 3.5f);
            float brake = speed > 6f ? Math.Max(0f, decel - 4.5f) / 3.5f : 0f;
            float hand = handbrake && speed > 5f ? 0.6f : 0f;
            return Math.Min(1f, Math.Max(side, Math.Max(brake, hand)));
        }

        // Dust / spray kinds per ground. Paved ground gives none unless the tyres slide (then smoke).
        public enum DustKind { None, Dust, Grass, Spray, Smoke }

        public static DustKind DustFor(FootSurface surface, float slip)
        {
            switch (surface)
            {
                case FootSurface.Dirt: return DustKind.Dust;
                case FootSurface.Grass: return DustKind.Grass;
                case FootSurface.Water: return DustKind.Spray;
                default: return slip > 0.15f ? DustKind.Smoke : DustKind.None;
            }
        }

        // Particles per second from one wheel.
        public static float DustRate(DustKind kind, float speed, float slip)
        {
            speed = Math.Abs(speed);
            if (kind == DustKind.None || speed < 2f) return 0f;
            float s = Math.Min(1f, (speed - 2f) / 20f);
            switch (kind)
            {
                case DustKind.Smoke: return 40f * slip;
                case DustKind.Spray: return 90f * s;
                case DustKind.Grass: return 26f * s + 20f * slip;
                default: return 46f * s + 40f * slip;
            }
        }

        // Tyre screech loudness: only on ground that squeals (paved), from sliding.
        public static float ScreechVolume(FootSurface surface, float slip) =>
            surface == FootSurface.Concrete || surface == FootSurface.Metal ? Math.Min(1f, slip * 1.4f) : 0f;

        // Wind noise at speed: from ~36 km/h, full at ~150.
        public static float WindVolume(float speedMps)
        {
            float t = (Math.Abs(speedMps) - 10f) / 30f;
            return Math.Max(0f, Math.Min(1f, t)) * 0.8f;
        }

        // ---- Collisions ----

        // The impact sound's loudness for a dent: bullets (shallow) make none.
        public static float ImpactVolume(float dentDepth) => dentDepth < 0.04f ? 0f : Math.Min(1f, 0.35f + dentDepth * 4f);
    }
}

using System;

namespace Game.Core
{
    // Hands holding and using things (CHARACTER_AUDIT #220 #221 #212): a throwable in hand with its pin and the
    // overhand throw, the bandage, kit, drink, pill and syringe, and the reach for loot or a door. Pure functions so
    // third and first person (and the tests) share them. Hand spots are in the character's frame (x right, y up,
    // z forward, meters) measured from that hand's own shoulder.
    public enum HandProp { None = 0, Grenade, Smoke, Flash, Molotov, Bandage, Kit, Bottle, Pill, Syringe }
    public enum HandGesture { None = 0, Pickup, Door }

    public struct HandsPose
    {
        public float RX, RY, RZ, LX, LY, LZ;   // right and left hand spots from their shoulders
        public float RightW, LeftW;            // how much each hand leaves the clip/hold (0..1)
        public float TorsoPitchDeg, TorsoTwistDeg; // forward bend, turn to the right
        public HandProp Prop;                  // what the right hand holds
        public float Curl;                     // right hand fingers closed (0..1)
    }

    public static class ItemMotion
    {
        public const float ThrowSeconds = 0.5f, PickupSeconds = 0.55f, DoorSeconds = 0.45f;

        static float Ease(float x) { x = Math.Max(0f, Math.Min(1f, x)); return x * x * (3f - 2f * x); }
        static float L(float a, float b, float k) => a + (b - a) * k;

        public static HandProp PropOf(ThrowableKind kind) =>
            kind == ThrowableKind.Smoke ? HandProp.Smoke : kind == ThrowableKind.Flash ? HandProp.Flash : kind == ThrowableKind.Molotov ? HandProp.Molotov : HandProp.Grenade;

        // A throwable in the right hand at the chest. `windup` 0..1 cocks the arm back behind the head (the throw key
        // held); `release` < 0 = not throwing, else 0..1 through the throw (arm over the shoulder, snap forward, back to the chest);
        // `pin` = the left hand pulls the pin (cooking).
        public static HandsPose Throw(HandProp prop, float windup, float release, bool pin)
        {
            var p = new HandsPose { Prop = prop, RightW = 1f, Curl = 0.8f };
            float hx = -0.06f, hy = -0.1f, hz = 0.3f;     // at the chest
            float wx = 0.1f, wy = 0.2f, wz = -0.2f;       // cocked back over the shoulder
            float fx = -0.03f, fy = 0.1f, fz = 0.52f;     // fully forward at the release
            float k = Ease(windup);
            float x = L(hx, wx, k), y = L(hy, wy, k), z = L(hz, wz, k);
            float pitch = L(0f, -8f, k), twist = L(0f, 22f, k);
            if (release >= 0f)
            {
                float r = Math.Min(1f, release);
                if (r < 0.45f)
                {
                    // Over the top: from the cocked arm to the forward reach, rising on the way.
                    float s = Ease(r / 0.45f);
                    x = L(wx, fx, s); y = L(wy, fy, s) + 0.14f * (float)Math.Sin(Math.PI * s); z = L(wz, fz, s);
                    pitch = L(-8f, 16f, s); twist = L(22f, -18f, s);
                }
                else
                {
                    float s = Ease((r - 0.45f) / 0.55f);
                    x = L(fx, hx, s); y = L(fy, hy, s); z = L(fz, hz, s);
                    pitch = L(16f, 0f, s); twist = L(-18f, 0f, s);
                }
                p.Prop = r < 0.2f ? prop : HandProp.None; // the grenade has left the hand
            }
            p.RX = x; p.RY = y; p.RZ = z; p.TorsoPitchDeg = pitch; p.TorsoTwistDeg = twist;
            if (pin && release < 0f && windup < 0.5f)
            {
                // The left hand comes across to the right hand and pinches the pin ring.
                p.LeftW = 1f;
                p.LX = 0.35f + x; // the left shoulder is 0.4 m left of the right one p.LY = y + 0.04f; p.LZ = z + 0.02f;
            }
            return p;
        }

        // p = progress 0..1 of the use.
        public static HandsPose Heal(HealKind kind, float p)
        {
            p = Math.Max(0f, Math.Min(1f, p));
            float rise = Ease(p / 0.15f) * (1f - Ease((p - 0.9f) / 0.1f)); // hands come up, work, go down
            float wave = (float)Math.Sin(p * Math.PI * 2f * 3f);
            var h = new HandsPose { RightW = rise, Curl = 0.7f };
            switch (kind)
            {
                case HealKind.Bandage:
                    // The left forearm held out, the right hand winding the roll round it.
                    h.Prop = HandProp.Bandage; h.LeftW = rise;
                    h.LX = 0.16f; h.LY = -0.08f; h.LZ = 0.3f;
                    h.RX = -0.14f + 0.04f * wave; h.RY = -0.08f + 0.03f * (float)Math.Cos(p * Math.PI * 2f * 3f); h.RZ = 0.3f;
                    h.TorsoPitchDeg = 6f * rise;
                    break;
                case HealKind.FirstAid:
                case HealKind.Medkit:
                    // A kit held low in the left hand, the right hand working in it.
                    h.Prop = HandProp.Kit; h.LeftW = rise;
                    h.LX = 0.16f; h.LY = -0.18f; h.LZ = 0.28f;
                    h.RX = -0.14f; h.RY = -0.12f + 0.025f * wave; h.RZ = 0.3f + 0.03f * (float)Math.Cos(p * Math.PI * 2f * 3f);
                    h.TorsoPitchDeg = 10f * rise;
                    break;
                case HealKind.Syringe:
                    // The right hand jabs the left thigh.
                    h.Prop = HandProp.Syringe;
                    h.RX = -0.3f; h.RY = -0.45f + 0.03f * Ease((p - 0.35f) / 0.15f) * (1f - Ease((p - 0.65f) / 0.15f)); h.RZ = 0.18f;
                    h.TorsoPitchDeg = 14f * rise;
                    break;
                default:
                {
                    // Drink and pills: to the mouth, head back, down again.
                    bool pill = kind == HealKind.Pill;
                    h.Prop = pill ? HandProp.Pill : HandProp.Bottle;
                    float up = Ease(p / 0.22f) * (1f - Ease((p - 0.78f) / 0.2f));
                    h.RX = L(-0.06f, -0.17f, up); h.RY = L(-0.1f, 0.17f, up); h.RZ = L(0.3f, 0.14f, up);
                    h.RightW = Math.Max(rise, up);
                    h.TorsoPitchDeg = -(pill ? 3f : 7f) * up;
                    break;
                }
            }
            return h;
        }

        // t = 0..1 through the gesture: the right hand reaches and comes back.
        public static HandsPose Gesture(HandGesture g, float t)
        {
            float s = (float)Math.Sin(Math.PI * Math.Max(0f, Math.Min(1f, t)));
            s = Ease(s);
            if (g == HandGesture.Pickup)
                return new HandsPose { RX = L(0f, 0.04f, s), RY = L(-0.2f, -0.56f, s), RZ = L(0.1f, 0.3f, s), RightW = s, TorsoPitchDeg = 32f * s, Curl = 0.3f + 0.5f * s };
            if (g == HandGesture.Door)
                return new HandsPose { RX = L(0f, -0.02f, s), RY = L(-0.2f, -0.08f, s), RZ = L(0.1f, 0.56f, s), RightW = s, TorsoPitchDeg = 5f * s, Curl = 0.2f };
            return default;
        }
    }
}

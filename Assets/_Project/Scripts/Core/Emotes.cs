using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The emotes of the emote wheel (ADR-131): six short poses of the whole model, made in code from our own numbers
    // (no animation files). 0 = none.
    public enum Emote : byte { None = 0, Wave = 1, Cheer = 2, Point = 3, Bow = 4, Dance = 5, Clap = 6 }

    // Bone turns of an emote at one moment, in degrees. Arms: Out is the raise to the side (a right arm out is positive,
    // overhead about 170), Fwd the raise to the front. Torso: pitch forward is positive, roll right is positive, yaw to
    // the right is positive. Bend: the knees' bend.
    public struct EmotePose
    {
        public float RightOut, LeftOut, RightFwd, LeftFwd, TorsoPitch, TorsoRoll, TorsoYaw, Bend;
    }

    public static class Emotes
    {
        public const int Count = 6;
        const float Fade = 0.3f; // into and out of the pose

        public static bool IsValid(int emote) => emote >= 1 && emote <= Count;

        public static string Name(Emote e)
        {
            switch (e)
            {
                case Emote.Wave: return "손 흔들기";
                case Emote.Cheer: return "만세";
                case Emote.Point: return "가리키기";
                case Emote.Bow: return "인사";
                case Emote.Dance: return "춤";
                case Emote.Clap: return "박수";
                default: return "";
            }
        }

        // How long the emote plays before the server ends it (seconds).
        public static float Duration(Emote e)
        {
            switch (e)
            {
                case Emote.Wave: return 2.6f;
                case Emote.Cheer: return 2.2f;
                case Emote.Point: return 2.0f;
                case Emote.Bow: return 2.0f;
                case Emote.Dance: return 4.0f;
                case Emote.Clap: return 2.4f;
                default: return 0f;
            }
        }

        // 0 -> 1 over the first Fade seconds, 1 until the last Fade seconds, then back to 0.
        public static float Envelope(float t, float duration)
        {
            if (t <= 0f || t >= duration) return 0f;
            float inn = Math.Min(1f, t / Fade), outt = Math.Min(1f, (duration - t) / Fade);
            float k = Math.Min(inn, outt);
            return k * k * (3f - 2f * k);
        }

        // The pose `t` seconds into the emote.
        public static EmotePose PoseAt(Emote e, float t)
        {
            float d = Duration(e);
            float env = Envelope(t, d);
            var p = new EmotePose();
            if (env <= 0f) return p;
            float w = (float)Math.Sin(t * 2.0 * Math.PI);
            switch (e)
            {
                case Emote.Wave:
                    // The right arm up beside the head, swinging at the elbow's height: here a swing of the whole arm.
                    p.RightOut = (150f + 22f * (float)Math.Sin(t * 2.0 * Math.PI * 1.6)) * env;
                    p.TorsoRoll = 3f * env;
                    break;
                case Emote.Cheer:
                    // Both arms overhead, the body bouncing with the knees.
                    p.RightOut = (165f + 8f * w) * env;
                    p.LeftOut = (165f - 8f * w) * env;
                    p.Bend = (6f + 6f * (float)Math.Sin(t * 2.0 * Math.PI * 2.0)) * env;
                    p.TorsoPitch = -4f * env;
                    break;
                case Emote.Point:
                    // The right arm straight ahead and a little up, the torso turned into it.
                    p.RightFwd = 85f * env;
                    p.RightOut = 8f * env;
                    p.TorsoYaw = 6f * env;
                    break;
                case Emote.Bow:
                    // Down from the waist and up again, hands at the sides.
                    float bow = (float)Math.Sin(Math.PI * Math.Min(1f, t / d));
                    p.TorsoPitch = 38f * bow;
                    p.Bend = 4f * bow;
                    break;
                case Emote.Dance:
                    float beat = t * 2.0f * (float)Math.PI * 1.25f;
                    p.RightOut = (95f + 55f * (float)Math.Sin(beat)) * env;
                    p.LeftOut = (95f - 55f * (float)Math.Sin(beat)) * env;
                    p.TorsoRoll = 9f * (float)Math.Sin(beat * 0.5f) * env;
                    p.TorsoYaw = 12f * (float)Math.Sin(beat * 0.5f + 1.2f) * env;
                    p.Bend = (7f + 7f * (float)Math.Sin(beat * 2f)) * env;
                    break;
                case Emote.Clap:
                    // Both arms forward, meeting and parting.
                    float clap = 0.5f + 0.5f * (float)Math.Sin(t * 2.0 * Math.PI * 2.2);
                    p.RightFwd = 70f * env;
                    p.LeftFwd = 70f * env;
                    p.RightOut = (4f + 18f * (1f - clap)) * env;
                    p.LeftOut = (4f + 18f * (1f - clap)) * env;
                    break;
            }
            return p;
        }
    }

    // Server: at most one emote request per MinInterval from each owner.
    public class EmoteGate
    {
        readonly Dictionary<int, float> _last = new Dictionary<int, float>();

        public bool TryAccept(int owner, float now, float minInterval)
        {
            if (_last.TryGetValue(owner, out var last) && now - last < minInterval) return false;
            _last[owner] = now;
            return true;
        }
    }
}

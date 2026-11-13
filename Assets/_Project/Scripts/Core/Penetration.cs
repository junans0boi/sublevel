using System;

namespace Game.Core
{
    // What a bullet hit (ADR-114): decides whether it goes through and which impact effect shows.
    public enum BulletSurface : byte { Ground = 0, Concrete = 1, Wood = 2, Metal = 3, Glass = 4, Vehicle = 5, Body = 6 }

    // Bullet penetration (ADR-114, #151). Per surface, indexed by BulletSurface: the thickest piece (m, along the shot)
    // a bullet still goes through (0 = never), and the share of damage and speed it keeps after one piece.
    [Serializable]
    public class PenetrationRules
    {
        //                                    Ground Concrete Wood   Metal  Glass  Vehicle Body
        public float[] MaxThickness = { 0f, 0f, 0.45f, 0.30f, 0.10f, 3f, 0f };
        public float[] DamageKeep = { 0f, 0f, 0.70f, 0.55f, 1f, 0.50f, 0f };
        public float[] SpeedKeep = { 0f, 0f, 0.80f, 0.70f, 0.95f, 0.65f, 0f };
        public float MinSpeed = 120f;      // a bullet slower than this after a piece stops in it
        public int MaxPieces = 3;          // pieces one bullet can go through
    }

    public static class Penetration
    {
        // True when a bullet at `speed` goes through `thickness` metres of `surface`; then what it keeps.
        public static bool TryPass(BulletSurface surface, float thickness, float speed, PenetrationRules r, out float damageKeep, out float speedKeep)
        {
            int i = (int)surface;
            damageKeep = speedKeep = 0f;
            if (i < 0 || i >= r.MaxThickness.Length || !(thickness <= r.MaxThickness[i])) return false;
            if (speed * r.SpeedKeep[i] < r.MinSpeed) return false;
            damageKeep = r.DamageKeep[i];
            speedKeep = r.SpeedKeep[i];
            return true;
        }
    }
}

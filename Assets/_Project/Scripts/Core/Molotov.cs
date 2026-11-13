using System;

namespace Game.Core
{
    // The fire bottle (ADR-110, PUBG_GAP_V2 G9): it breaks on the first thing it hits and leaves a fire on the ground.
    // Standing in the fire hurts per second; anyone it touched keeps burning for a few seconds after leaving it, and a
    // burning body cannot hold the sights steady. Fire and burn do not stack: the larger of the two counts.
    [Serializable]
    public class MolotovRules
    {
        public static MolotovRules Current = new MolotovRules();

        public int MaxCarried = 3;
        public float FuseSeconds = 4f;       // flight limit: it breaks in the air when nothing was hit by then
        public float Radius = 3f;            // metres, of the fire on the ground
        public float HeightMeters = 1.6f;    // the fire reaches this high above the ground
        public float FireSeconds = 10f;      // how long the fire lasts
        public float FireDps = 12.5f;        // health per second standing in it
        public float BurnDps = 10f;          // health per second while burning outside it
        public float BurnSeconds = 4f;       // burning lasts this long after the last contact with the fire
    }

    public static class Molotov
    {
        // Is a body whose feet are at (px,py,pz) in the fire centred at (cx,cy,cz)?
        public static bool InFire(float px, float py, float pz, float cx, float cy, float cz, MolotovRules r)
        {
            float dx = px - cx, dz = pz - cz;
            return dx * dx + dz * dz <= r.Radius * r.Radius && py >= cy - 0.5f && py <= cy + r.HeightMeters;
        }

        // One tick of dt seconds: standing in the fire (re)starts the burn; returns the health lost in the tick and
        // runs `burnLeft` down when out of the fire. Burning is `burnLeft > 0`.
        public static float Step(ref float burnLeft, bool inFire, float dt, MolotovRules r)
        {
            if (dt <= 0f) return 0f;
            if (inFire) burnLeft = r.BurnSeconds;
            float dps = inFire ? Math.Max(r.FireDps, r.BurnDps) : burnLeft > 0f ? r.BurnDps : 0f;
            if (!inFire && burnLeft > 0f) burnLeft = Math.Max(0f, burnLeft - dt);
            return dps * dt;
        }

        // The sights cannot be held steady on fire: the aim drops to the shoulder.
        public static AimMode LimitAim(AimMode aim, bool burning) => burning && aim == AimMode.Sights ? AimMode.Shoulder : aim;
    }
}

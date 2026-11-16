namespace Game.Core
{
    // The derived numbers the roster is balanced by (ADR-172, ADR-182). Data stores FireInterval; players see RPM and DPS.
    public static class WeaponStats
    {
        public static float Rpm(WeaponDef w) => w.FireInterval > 0f ? 60f / w.FireInterval : 0f;

        // Damage per second with every pellet landing, ignoring reloads and falloff.
        public static float Dps(WeaponDef w) => w.Damage * (w.Pellets < 1 ? 1 : w.Pellets) * Rpm(w) / 60f;

        // A gun in the table (not a fist, not a melee weapon).
        public static bool IsFirearm(WeaponDef w) => w != null && !w.IsMelee && w.Id > 0;
    }
}

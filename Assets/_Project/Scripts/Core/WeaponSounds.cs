using System;

namespace Game.Core
{
    // Which recorded gun a weapon sounds like (ADR-250). Many weapons share one recording each class; the per-weapon
    // pitch and loudness tell them apart. Pure data: CombatantAudio loads the clips by name prefix.
    public enum GunClass { Pistol, Smg, Rifle, Lmg, Dmr, Sniper, Shotgun }

    public struct WeaponSound
    {
        public int WeaponId;
        public GunClass Class;
        // Playback pitch of the near layer (below 1 = heavier). Far layer and tails follow it.
        public float Pitch;
        // Multiplies the shot volume (a sniper cracks louder than a sidearm).
        public float Loudness;
    }

    public static class WeaponSounds
    {
        // Resources/Shots clip name prefixes (Audio/Weapons/Freesound, tools/sound_shots.json).
        public const string SuppressedNear = "gun_suppressed_near_", IndoorTail = "gun_tail_indoor_", DryFire = "gun_dryfire_", Casing = "gun_casing_";

        // Weapon ids are the rows of weapons.json (1~5, 6~16 new, 31~35 supply). Melee (21~24) has no gunshot.
        public static readonly WeaponSound[] Table =
        {
            S(1, GunClass.Pistol, 1.00f, 0.90f), S(6, GunClass.Pistol, 0.84f, 1.00f),
            S(2, GunClass.Smg, 1.00f, 0.90f), S(7, GunClass.Smg, 1.06f, 0.85f), S(8, GunClass.Smg, 0.95f, 0.90f), S(35, GunClass.Smg, 1.10f, 0.85f),
            S(3, GunClass.Rifle, 1.00f, 1.00f), S(9, GunClass.Rifle, 1.04f, 1.00f), S(34, GunClass.Rifle, 0.96f, 1.00f),
            S(10, GunClass.Rifle, 0.90f, 1.05f), S(11, GunClass.Rifle, 0.86f, 1.10f),
            S(31, GunClass.Lmg, 0.95f, 1.05f), S(32, GunClass.Lmg, 0.84f, 1.10f),
            S(12, GunClass.Dmr, 1.00f, 1.00f), S(13, GunClass.Dmr, 1.04f, 1.00f), S(14, GunClass.Dmr, 0.90f, 1.10f), S(33, GunClass.Dmr, 0.94f, 1.05f),
            S(5, GunClass.Sniper, 1.00f, 1.20f), S(15, GunClass.Sniper, 0.86f, 1.25f),
            S(4, GunClass.Shotgun, 1.00f, 1.10f), S(16, GunClass.Shotgun, 0.88f, 1.15f),
        };

        static WeaponSound S(int id, GunClass c, float pitch, float loud) => new WeaponSound { WeaponId = id, Class = c, Pitch = pitch, Loudness = loud };

        public static bool TryGet(int weaponId, out WeaponSound sound)
        {
            foreach (var e in Table)
                if (e.WeaponId == weaponId) { sound = e; return true; }
            sound = default;
            return false;
        }

        // The recording a class plays. The light machine guns reuse the rifle recording (pitched lower per weapon).
        public static string Family(GunClass c) => c == GunClass.Lmg ? "rifle" : c.ToString().ToLowerInvariant();

        public static string NearPrefix(GunClass c) => "gun_" + Family(c) + "_near_";
        public static string FarPrefix(GunClass c) => "gun_" + Family(c) + "_far_";

        // Reload sound by class: pistols have their own, shotguns pump, everything else is the rifle's.
        public static string ReloadName(GunClass c) => c == GunClass.Pistol ? "gun_reload_pistol_01" : c == GunClass.Shotgun ? "gun_shotgun_pump_01" : "gun_reload_rifle_01";
    }
}

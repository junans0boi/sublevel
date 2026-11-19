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
        public const string Ricochet = "impact_ricochet_";
        // Resources/Sfx clip name prefixes of the body sounds (#288, tools/sound_combat.json).
        public const string MeleeSwing = "melee_swing_", PainGrunt = "pain_grunt_", HealStart = "heal_bandage_", Pickup = "loot_rummage_", SwimStroke = "swim_stroke_";

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

        // ---- Variants (#287) ----
        // Recorded clips are few (one to three per layer), so each clip also plays through these pitch and low-pass
        // treatments of the same source: every layer has at least MinVariants distinct takes. The first is the clip as cut.
        public const int MinVariants = 3;
        static readonly float[] VariantPitch = { 1f, 0.96f, 1.04f };
        static readonly float[] VariantCutoff = { 22000f, 9000f, 14000f };

        public struct Variant
        {
            public int Clip;       // index into the layer's clips
            public float Pitch;    // multiplies the weapon's own pitch
            public float CutoffHz; // low-pass on top of distance and occlusion
        }

        // How many distinct takes a layer with `clips` recordings has (0 when it has none).
        public static int VariantCount(int clips) => clips <= 0 ? 0 : clips * VariantPitch.Length;

        // Take `index` (any int; wraps) of a layer with `clips` recordings: every clip as cut first, then each treated.
        public static Variant VariantOf(int clips, int index)
        {
            if (clips <= 0) return new Variant { Clip = -1, Pitch = 1f, CutoffHz = VariantCutoff[0] };
            int n = VariantCount(clips);
            int i = ((index % n) + n) % n;
            int t = i / clips;
            return new Variant { Clip = i % clips, Pitch = VariantPitch[t], CutoffHz = VariantCutoff[t] };
        }

        // The next take after `last`, never the same one twice in a row (random step, so a burst does not cycle audibly).
        public static int NextVariant(int clips, int last, double random01)
        {
            int n = VariantCount(clips);
            if (n <= 1) return 0;
            int step = 1 + (int)(Math.Min(0.999999, Math.Max(0.0, random01)) * (n - 1));
            return (((last + step) % n) + n) % n;
        }

        // The indoor tail (third layer, AUDIO.md 4): only when the shooter stands under a roof. As loud as the louder of
        // the near layer and a share of the far one, so a shot down the corridor still rings and a distant indoor shot booms.
        public const float IndoorTailGain = 0.55f, IndoorTailFarShare = 0.6f;
        public static float TailLevel(bool shooterIndoors, float near, float far) =>
            shooterIndoors ? IndoorTailGain * Math.Max(near, IndoorTailFarShare * far) : 0f;
    }
}

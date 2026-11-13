using System;

namespace Game.Core
{
    // How an optic looks through (ADR-056 / ADR-058 / ADR-060). Dot and Holo are the 1x sights — our own designs,
    // "반사 조준 사이트" (reflex) and "투영 조준 사이트" (projector) — with a reticle over the weapon;
    // Scope narrows the field of view. The numbers are stored in data: never reorder.
    public enum OpticKind { Dot = 0, Holo = 1, Scope = 2 }

    public enum ReticleStyle { Dot = 0, Ring = 1, Crosshair = 2 }

    // One optic. Loaded from data (optics.json); Id 0 means "no optic".
    [Serializable]
    public class OpticDef
    {
        public int Id;
        public string Name;               // Korean, generic (no product names)
        public OpticKind Kind;
        public float Zoom = 1f;           // 1 for the dot and holographic sights
        public float SensitivityFactor = 1f; // mouse sensitivity while looking through it (about 1 / zoom^0.8)
        public ReticleStyle Reticle;
    }

    // Which optics one weapon takes.
    [Serializable]
    public class WeaponOptics
    {
        public int WeaponId;
        public int[] Optics = new int[0];
    }

    // The optic table and the per-weapon lists. Defaults until the data loader replaces `Current`, so everything
    // runs without a data file. 8x and 16x are taken only by the sniper rifle (Sentinel, id 5).
    [Serializable]
    public class OpticRules
    {
        public static OpticRules Current = new OpticRules();

        public OpticDef[] Optics =
        {
            new OpticDef { Id = 1, Name = "반사 조준 사이트", Kind = OpticKind.Dot, Zoom = 1f, SensitivityFactor = 1f, Reticle = ReticleStyle.Dot },
            new OpticDef { Id = 2, Name = "투영 조준 사이트", Kind = OpticKind.Holo, Zoom = 1f, SensitivityFactor = 1f, Reticle = ReticleStyle.Ring },
            new OpticDef { Id = 3, Name = "2배율", Kind = OpticKind.Scope, Zoom = 2f, SensitivityFactor = 0.574f, Reticle = ReticleStyle.Crosshair },
            new OpticDef { Id = 4, Name = "3배율", Kind = OpticKind.Scope, Zoom = 3f, SensitivityFactor = 0.415f, Reticle = ReticleStyle.Crosshair },
            new OpticDef { Id = 5, Name = "4배율", Kind = OpticKind.Scope, Zoom = 4f, SensitivityFactor = 0.330f, Reticle = ReticleStyle.Crosshair },
            new OpticDef { Id = 6, Name = "6배율", Kind = OpticKind.Scope, Zoom = 6f, SensitivityFactor = 0.238f, Reticle = ReticleStyle.Crosshair },
            new OpticDef { Id = 7, Name = "8배율", Kind = OpticKind.Scope, Zoom = 8f, SensitivityFactor = 0.189f, Reticle = ReticleStyle.Crosshair },
            new OpticDef { Id = 8, Name = "16배율", Kind = OpticKind.Scope, Zoom = 16f, SensitivityFactor = 0.109f, Reticle = ReticleStyle.Crosshair },
        };

        // Weapon ids as in weapons.json: 1 VK-9, 2 Ordo, 3 Warden, 4 Breacher, 5 Sentinel.
        public WeaponOptics[] Weapons =
        {
            new WeaponOptics { WeaponId = 1, Optics = new[] { 1, 2 } },
            new WeaponOptics { WeaponId = 2, Optics = new[] { 1, 2, 3, 4, 5 } },
            new WeaponOptics { WeaponId = 3, Optics = new[] { 1, 2, 3, 4, 5, 6 } },
            new WeaponOptics { WeaponId = 4, Optics = new[] { 1, 2, 3 } },
            new WeaponOptics { WeaponId = 5, Optics = new[] { 1, 2, 3, 4, 5, 6, 7, 8 } },
        };

        static readonly int[] Empty = new int[0];

        // Null when there is no such optic (0 = none).
        public OpticDef Find(int id)
        {
            if (Optics == null) return null;
            foreach (var o in Optics)
                if (o != null && o.Id == id) return o;
            return null;
        }

        // The ids this weapon takes; empty for an unknown weapon.
        public int[] AllowedFor(int weaponId)
        {
            if (Weapons != null)
                foreach (var w in Weapons)
                    if (w != null && w.WeaponId == weaponId && w.Optics != null) return w.Optics;
            return Empty;
        }
    }

    public static class Optics
    {
        public const int None = 0;
        // Sensitivity falls as zoom^-Exponent, never below MinSensitivity.
        public const float SensitivityExponent = 0.8f;
        public const float MinSensitivity = 0.1f;

        // Optic and weapon must both exist, and the weapon's list must name the optic.
        public static bool CanAttach(int weaponId, int opticId) => CanAttach(weaponId, opticId, OpticRules.Current);

        public static bool CanAttach(int weaponId, int opticId, OpticRules rules)
        {
            if (weaponId == 0 || opticId == None || rules.Find(opticId) == null) return false;
            foreach (int allowed in rules.AllowedFor(weaponId))
                if (allowed == opticId) return true;
            return false;
        }

        // Magnification of the optic (1 when none or unknown).
        public static float ZoomOf(int opticId)
        {
            var def = OpticRules.Current.Find(opticId);
            return def != null && def.Zoom > 1f ? def.Zoom : 1f;
        }

        // Vertical field of view in degrees at `zoom`: the image is `zoom` times larger, so tan(fov / 2) is divided
        // by it. Zoom of 1 or less leaves the view alone.
        public static float FovFor(float baseFovDeg, float zoom)
        {
            if (!(zoom > 1f)) return baseFovDeg;
            const double rad = Math.PI / 180.0;
            return (float)(2.0 * Math.Atan(Math.Tan(baseFovDeg * 0.5 * rad) / zoom) / rad);
        }

        // Mouse sensitivity multiplier at `zoom`: 1 / zoom^0.8, not below MinSensitivity.
        public static float SensitivityFor(float zoom)
        {
            if (!(zoom > 1f)) return 1f;
            float factor = (float)Math.Pow(zoom, -SensitivityExponent);
            return factor < MinSensitivity ? MinSensitivity : factor;
        }

        // "×4"; the dot and holographic sights show "×1".
        public static string ZoomText(float zoom) => "×" + (int)Math.Round(zoom);

        public static LootEntry ToLoot(int opticId) => new LootEntry { Kind = LootKind.Optic, Optic = opticId, Amount = 1 };
    }
}

using System;

namespace Game.Core
{
    // Where on a gun an attachment sits (ADR-100). Optic is the sights (OpticDef); the rest are AttachmentDef.
    // Stored in the packed gear value: never reorder.
    public enum AttachSlot { Optic = 0, Muzzle = 1, Grip = 2, Stock = 3, Mag = 4 }

    // One attachment that is not an optic. Loaded from data (optics.json); ids 9-16 share the optic id space, so a
    // pickup, the bag and the inventory handle them like an optic (ADR-082).
    [Serializable]
    public class AttachmentDef
    {
        public int Id;
        public string Name;               // Korean, original (no product names)
        public AttachSlot Slot;
        // Recoil (ADR-100): factors on the weapon's kick (pitch = up, yaw = sideways) and on how fast the view settles.
        public float RecoilPitch = 1f;
        public float RecoilYaw = 1f;
        public float RecoverFactor = 1f;
        // ADR-102: a suppressor shrinks how far the shot is heard (SoundRules.SuppressedDistanceFactor); a magazine
        // multiplies the rounds it holds and the reload time.
        public bool Suppressed;
        public float MagFactor = 1f;
        public float ReloadFactor = 1f;
    }

    // Which attachments (not optics) one weapon takes: its slot table.
    [Serializable]
    public class WeaponAttachments
    {
        public int WeaponId;
        public int[] Ids = new int[0];
    }

    // What the attachments fitted to one weapon do together; factors multiply.
    public class AttachMods
    {
        public float Pitch = 1f, Yaw = 1f, Recover = 1f;
        public bool Suppressed;
        public float MagFactor = 1f, ReloadFactor = 1f;
        public static readonly AttachMods None = new AttachMods();
    }

    public static class Attachments
    {
        public const int Slots = 5;                  // AttachSlot values
        public const int BitsPerSlot = 6;            // ids are below 64
        // A gear index names one fitted thing: weapon slot + 16 x AttachSlot (an optic is just the weapon slot).
        public const int IndexStride = 16;

        public static int GearIndex(int weaponSlot, AttachSlot slot) => weaponSlot + IndexStride * (int)slot;
        public static int WeaponSlotOf(int gearIndex) => gearIndex & (IndexStride - 1);
        public static AttachSlot SlotOfIndex(int gearIndex) => (AttachSlot)Math.Min(Slots - 1, gearIndex / IndexStride);

        // The slot an id (optic or attachment) goes in; null for an unknown id.
        public static AttachSlot? SlotOf(int id, OpticRules rules)
        {
            if (rules.Find(id) != null) return AttachSlot.Optic;
            var a = rules.FindAttachment(id);
            return a != null ? a.Slot : (AttachSlot?)null;
        }

        public static AttachSlot? SlotOf(int id) => SlotOf(id, OpticRules.Current);

        static readonly string[] PlaceNames = { "조준경", "총구", "손잡이", "개머리판", "탄창" };
        public static string PlaceName(AttachSlot slot) => PlaceNames[(int)slot];

        // The Korean name of an optic or attachment; empty for none or unknown.
        public static string NameOf(int id)
        {
            var rules = OpticRules.Current;
            return rules.Find(id)?.Name ?? rules.FindAttachment(id)?.Name ?? "";
        }

        // Second line of a tooltip: "손잡이 · 위 반동 -15%"; "조준경 · 4배율" for an optic; empty for unknown.
        public static string Describe(int id)
        {
            var rules = OpticRules.Current;
            var optic = rules.Find(id);
            if (optic != null) return "조준경 · " + (optic.Zoom > 1f ? optic.Zoom.ToString("0.#") + "배율" : "1배율");
            var a = rules.FindAttachment(id);
            if (a == null) return "";
            string text = PlaceName(a.Slot);
            text += Change(" · 위 반동", a.RecoilPitch) + Change(" · 좌우 반동", a.RecoilYaw);
            if (a.RecoverFactor > 1.001f) text += " · 반동 회복 +" + Math.Round((a.RecoverFactor - 1f) * 100f) + "%";
            if (a.Suppressed) text += " · 총성 들리는 거리 1/" + SoundRules.SuppressedDistanceFactor.ToString("0.#");
            if (a.MagFactor > 1.001f) text += " · 장전 수 +" + Math.Round((a.MagFactor - 1f) * 100f) + "% · 재장전 +" + Math.Round((a.ReloadFactor - 1f) * 100f) + "%";
            return text;
        }

        static string Change(string label, float factor) =>
            factor < 0.999f ? label + " -" + Math.Round((1f - factor) * 100f) + "%" : "";

        // Several slots' ids in one int, 6 bits each (the weapon slot's whole fit in one SyncVar).
        public static int Get(int packed, AttachSlot slot) => (packed >> ((int)slot * BitsPerSlot)) & 63;
        public static int With(int packed, AttachSlot slot, int id)
        {
            int shift = (int)slot * BitsPerSlot;
            return (packed & ~(63 << shift)) | ((id & 63) << shift);
        }

        // The combined effect of everything in `packed` (the optic slot has none).
        public static AttachMods ModsOf(int packed, OpticRules rules)
        {
            AttachMods mods = null;
            for (int s = 1; s < Slots; s++)
            {
                var def = rules.FindAttachment(Get(packed, (AttachSlot)s));
                if (def == null) continue;
                mods = mods ?? new AttachMods();
                mods.Pitch *= def.RecoilPitch;
                mods.Yaw *= def.RecoilYaw;
                mods.Recover *= def.RecoverFactor;
                mods.Suppressed |= def.Suppressed;
                mods.MagFactor *= def.MagFactor;
                mods.ReloadFactor *= def.ReloadFactor;
            }
            return mods ?? AttachMods.None;
        }

        // ---- default tables (data in optics.json replaces them) ----
        // Ids: 9 compensator, 10 flash hider, 11 suppressor, 12 vertical grip, 13 angled grip, 14 curved grip,
        // 15 reinforced stock, 16 extended magazine.

        public static AttachmentDef[] DefaultDefs() => new[]
        {
            new AttachmentDef { Id = 9, Name = "반동 보정기", Slot = AttachSlot.Muzzle, RecoilPitch = 0.88f, RecoilYaw = 0.9f },
            new AttachmentDef { Id = 10, Name = "화염 분산기", Slot = AttachSlot.Muzzle, RecoilPitch = 0.95f, RecoilYaw = 0.95f },
            new AttachmentDef { Id = 11, Name = "소음기", Slot = AttachSlot.Muzzle, RecoilPitch = 0.97f, Suppressed = true },
            new AttachmentDef { Id = 12, Name = "수직 손잡이", Slot = AttachSlot.Grip, RecoilPitch = 0.85f },
            new AttachmentDef { Id = 13, Name = "각진 손잡이", Slot = AttachSlot.Grip, RecoilPitch = 0.95f, RecoilYaw = 0.8f },
            new AttachmentDef { Id = 14, Name = "곡선 손잡이", Slot = AttachSlot.Grip, RecoverFactor = 1.35f },
            new AttachmentDef { Id = 15, Name = "보강 개머리판", Slot = AttachSlot.Stock, RecoilPitch = 0.92f, RecoilYaw = 0.92f, RecoverFactor = 1.1f },
            new AttachmentDef { Id = 16, Name = "확장 탄창", Slot = AttachSlot.Mag, MagFactor = 1.33f, ReloadFactor = 1.1f },
        };

        // Weapon ids as in weapons.json: 1 VK-9, 2 Ordo, 3 Warden, 4 Breacher, 5 Sentinel.
        public static WeaponAttachments[] DefaultSlots() => new[]
        {
            new WeaponAttachments { WeaponId = 1, Ids = new[] { 9, 10, 11, 16 } },
            new WeaponAttachments { WeaponId = 2, Ids = new[] { 9, 10, 11, 12, 13, 14, 15, 16 } },
            new WeaponAttachments { WeaponId = 3, Ids = new[] { 9, 10, 11, 12, 13, 14, 15, 16 } },
            new WeaponAttachments { WeaponId = 4, Ids = new[] { 9, 11, 15 } },
            new WeaponAttachments { WeaponId = 5, Ids = new[] { 9, 10, 11, 15, 16 } },
        };
    }
}

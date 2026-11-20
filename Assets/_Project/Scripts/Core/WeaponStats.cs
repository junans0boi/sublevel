using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One bar of a gun's tooltip (ADR-493): what it measures, how full (0-1) and the number beside it.
    public struct StatBar
    {
        public string Label;
        public float Fill;
        public string Value;
    }

    // The derived numbers the roster is balanced by (ADR-172, ADR-182). Data stores FireInterval; players see RPM and DPS.
    public static class WeaponStats
    {
        public static float Rpm(WeaponDef w) => w.FireInterval > 0f ? 60f / w.FireInterval : 0f;

        // Damage per second with every pellet landing, ignoring reloads and falloff.
        public static float Dps(WeaponDef w) => w.Damage * (w.Pellets < 1 ? 1 : w.Pellets) * Rpm(w) / 60f;

        // A gun in the table (not a fist, not a melee weapon).
        public static bool IsFirearm(WeaponDef w) => w != null && !w.IsMelee && w.Id > 0;

        // ---- Tooltip (ADR-493) ----

        // Full bars at: one trigger pull's damage, effective range (m), RPM, muzzle speed (m/s).
        public const float FullDamage = 120f, FullRange = 340f, FullRpm = 1200f, FullSpeed = 1000f;
        public static readonly string[] BarLabels = { "피해", "사거리", "안정성", "연사력", "탄속" };

        static readonly string[] ClassLabels = { "총기", "HG 권총", "SMG 기관단총", "AR 돌격소총", "DMR 지정사수소총", "SR 저격소총", "SG 산탄총", "LMG 경기관총" };

        public static string ClassLabel(WeaponDef w)
        {
            if (w == null) return "";
            if (w.IsMelee) return "근접 무기";
            int c = (int)w.Class;
            return c >= 0 && c < ClassLabels.Length ? ClassLabels[c] : ClassLabels[0];
        }

        // "단발/점사 3발/연사"; a bolt rifle says so. Single is always there (Loadout.ModeAt).
        public static string FireModes(WeaponDef w)
        {
            if (w == null || w.IsMelee) return "";
            if (w.Class == WeaponClass.Sr) return "볼트액션";
            var modes = new List<string> { "단발" };
            if (w.Burst > 1) modes.Add("점사 " + w.Burst + "발");
            if (w.Auto) modes.Add("연사");
            return string.Join("/", modes);
        }

        // Kick per shot after the fitted grips, muzzle and stock; 1 = no kick at all, falls towards 0.
        public static float Stability(WeaponDef w, AttachMods mods)
        {
            mods = mods ?? AttachMods.None;
            float kick = w.RecoilPitchDeg * mods.Pitch + 0.5f * w.RecoilYawDeg * mods.Yaw;
            return 1f / (1f + 0.6f * Math.Max(0f, kick));
        }

        public static int MagazineOf(WeaponDef w, AttachMods mods) =>
            (int)Math.Round(w.MagSize * (mods ?? AttachMods.None).MagFactor);

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        public static StatBar[] Bars(WeaponDef w, AttachMods mods)
        {
            int pellets = w.Pellets < 1 ? 1 : w.Pellets;
            float stability = Stability(w, mods);
            return new[]
            {
                new StatBar { Label = BarLabels[0], Fill = Clamp01(w.Damage * pellets / FullDamage), Value = pellets > 1 ? w.Damage.ToString("0") + "×" + pellets : w.Damage.ToString("0") },
                new StatBar { Label = BarLabels[1], Fill = Clamp01(w.FalloffEnd / FullRange), Value = w.FalloffEnd.ToString("0") + "m" },
                new StatBar { Label = BarLabels[2], Fill = Clamp01(stability), Value = Math.Round(stability * 100f).ToString("0") },
                new StatBar { Label = BarLabels[3], Fill = Clamp01(Rpm(w) / FullRpm), Value = Rpm(w).ToString("0") + "rpm" },
                new StatBar { Label = BarLabels[4], Fill = Clamp01(w.MuzzleSpeed / FullSpeed), Value = w.MuzzleSpeed.ToString("0") + "m/s" },
            };
        }

        // One fitted part as a tooltip line: "총구 반동 보정기: 위 반동 -12%, 좌우 반동 -10%".
        public static string PartLine(int id)
        {
            string name = Attachments.NameOf(id);
            if (name.Length == 0) return "";
            var parts = Attachments.Describe(id).Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries);
            string line = (parts.Length > 0 ? parts[0] + " " : "") + name;
            if (parts.Length > 1 && parts[1] != name) line += ": " + string.Join(", ", parts, 1, parts.Length - 1); // an optic's name is its zoom
            return line;
        }

        // The places of this gun that take something ("조준경/총구/손잡이"), from the optic and attachment tables.
        public static string SlotNames(int weaponId, OpticRules rules)
        {
            var places = new List<string>();
            if (rules.AllowedFor(weaponId).Length > 0) places.Add(Attachments.PlaceName(AttachSlot.Optic));
            var ids = rules.AttachmentsFor(weaponId);
            for (int s = 1; s < Attachments.Slots; s++)
                foreach (int id in ids)
                    if (rules.FindAttachment(id)?.Slot == (AttachSlot)s) { places.Add(Attachments.PlaceName((AttachSlot)s)); break; }
            return string.Join("/", places);
        }

        // Everything after the name, " · "-separated (the inventory splits it: first part = type line, rest = body lines).
        // `packed` = the fitted parts (Attachments.With), 0 for a gun on the ground.
        public static string TooltipText(WeaponDef w, int packed, OpticRules rules)
        {
            if (w == null) return "";
            if (w.IsMelee) return "근접 무기 · 피해 " + w.Damage.ToString("0");
            var mods = Attachments.ModsOf(packed, rules);
            var lines = new List<string>
            {
                ClassLabel(w),
                "사격 " + FireModes(w),
                "탄약 " + AmmoNames.Of(w.Ammo),
                "탄창 " + MagazineOf(w, mods) + "발",
            };
            var fitted = new List<string>();
            for (int s = 0; s < Attachments.Slots; s++)
            {
                string part = PartLine(Attachments.Get(packed, (AttachSlot)s));
                if (part.Length > 0) fitted.Add(part);
            }
            if (fitted.Count > 0) lines.AddRange(fitted);
            else
            {
                string slots = SlotNames(w.Id, rules);
                lines.Add(slots.Length > 0 ? "부착 칸 " + slots : "부착 칸 없음");
            }
            return string.Join(" · ", lines);
        }
    }

    // The short stat lines of armor and healing tooltips (ADR-493).
    public static class GearTips
    {
        // "피해 감소 40% · 내구도 150"; empty for no such tier.
        public static string Armor(ArmorSlot slot, int tier, ArmorRules rules)
        {
            var def = rules.Find(slot, tier);
            return def == null ? "" : "피해 감소 " + Math.Round(def.Reduction * 100f) + "% · 내구도 " + def.Durability.ToString("0");
        }

        // "체력 +10 (75까지) · 4초", "체력 75까지 · 6초", "부스트 +40 · 4초"; empty for none.
        public static string Heal(HealKind kind, HealRules rules)
        {
            var def = Healing.For(kind, rules);
            if (def == null) return "";
            string seconds = def.Seconds.ToString("0.#") + "초";
            if (def.Boost > 0f) return "부스트 +" + def.Boost.ToString("0") + " · " + seconds;
            string hp = def.Amount >= def.MaxHp ? "체력 " + def.MaxHp.ToString("0") + "까지" : "체력 +" + def.Amount.ToString("0") + " (" + def.MaxHp.ToString("0") + "까지)";
            return hp + " · " + seconds;
        }
    }
}

using FishNet.Object;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    public enum InventorySourceKind
    {
        None,
        Vicinity,
        Ammo,
        Medkit,
        Grenade,
        Weapon,
        Helmet,
        Vest,
        Optic, // the optic on the weapon card of slot Index
    }

    public readonly struct InventoryDragSource
    {
        public InventorySourceKind Kind { get; }
        public LootItem Loot { get; }
        public int Index { get; }

        internal InventoryDragSource(InventorySourceKind kind, LootItem loot, int index)
        {
            Kind = kind;
            Loot = loot;
            Index = index;
        }

        public bool IsValid => Kind != InventorySourceKind.None;
    }

    public readonly struct InventoryGearState
    {
        public bool Has { get; }
        public int Tier { get; }
        public int Durability { get; }
        public int MaxDurability { get; }

        public InventoryGearState(bool has, int tier, int durability, int maxDurability)
        {
            Has = has;
            Tier = tier;
            Durability = durability;
            MaxDurability = maxDurability;
        }

        public float Fraction => MaxDurability > 0 ? (float)Durability / MaxDurability : 0f;
    }

    // The only UI-side adapter for replicated inventory data and owner requests. Master-owned server contracts
    // stay isolated here while the screen remains a presentation component.
    public static class InventoryBackend
    {
        public const int AmmoTypeCount = 4;
        public const int MedkitRow = 4;
        public const int GrenadeRow = 5;
        public const int BackpackRows = 6;
        public const int HelmetSlot = 0;
        public const int VestSlot = 1;

        static readonly string[] AmmoKeys = { "ammo_9mm", "ammo_556", "ammo_12ga", "ammo_762" };

        public static string AmmoName(int index) => AmmoNames.Of(index);
        public static string AmmoKey(int index) => index >= 0 && index < AmmoKeys.Length ? AmmoKeys[index] : "";
        public static string WeaponKey(int weaponId) => weaponId > 0 ? "weapon_" + weaponId : "";
        public static int AmmoIndex(WeaponDef weapon) => weapon != null ? (int)weapon.Ammo : -1;
        public static AmmoType AmmoValue(int index) => (AmmoType)index;

        public static int Reserve(Combatant combatant, int index) =>
            combatant == null || !AmmoNames.IsValid((AmmoType)index) ? 0 : combatant.ReserveFor((AmmoType)index);

        public static int Grenades(Combatant combatant)
        {
            var user = combatant != null ? combatant.GetComponent<ThrowableUser>() : null;
            return user != null ? user.Grenades.Value : 0;
        }

        public static InventoryGearState Gear(Combatant combatant, int slot)
        {
            var gear = combatant != null && (slot == HelmetSlot || slot == VestSlot) ? combatant.GetComponent<CombatantGear>() : null;
            if (gear == null) return default;
            var armorSlot = (ArmorSlot)slot;
            return new InventoryGearState(gear.Has(armorSlot), gear.TierOf(armorSlot),
                Mathf.RoundToInt(gear.DurabilityOf(armorSlot)), Mathf.RoundToInt(gear.MaxDurabilityOf(armorSlot)));
        }

        public static bool TryArmorSlot(LootItem loot, out int slot)
        {
            slot = -1;
            if (loot == null) return false;
            var kind = (LootKind)loot.Kind.Value;
            if (!Armor.IsArmor(kind)) return false;
            slot = (int)Armor.SlotOf(kind);
            return true;
        }

        public static bool IsGrenade(LootItem loot) => loot != null && (LootKind)loot.Kind.Value == LootKind.Grenade;

        // Mirrors the server's rule (CombatantGear.ServerTryTake): only a better piece is taken.
        public static bool CanEquipArmor(Combatant combatant, LootItem loot, int slot)
        {
            if (!TryArmorSlot(loot, out int lootSlot) || lootSlot != slot) return false;
            var gear = combatant != null ? combatant.GetComponent<CombatantGear>() : null;
            if (gear == null) return false;
            var armorSlot = (ArmorSlot)slot;
            var candidate = new ArmorPiece { Tier = loot.Tier.Value, Durability = loot.Durability.Value };
            var current = new ArmorPiece { Tier = gear.TierOf(armorSlot), Durability = gear.DurabilityOf(armorSlot) };
            return Armor.IsBetter(candidate, current);
        }

        public static string LootName(LootItem item)
        {
            if (item == null) return "아이템";
            switch ((LootKind)item.Kind.Value)
            {
                case LootKind.Weapon:
                    var weapon = GameData.Weapons.Find(item.WeaponId.Value);
                    return weapon != null ? weapon.Name : "무기";
                case LootKind.Ammo: return AmmoName(item.Ammo.Value);
                case LootKind.Medkit: return "구급상자";
                case LootKind.Optic: return LootNames.OfOptic(item.Optic.Value);
                default:
                    if (TryArmorSlot(item, out int slot)) return slot == HelmetSlot ? "헬멧" : "조끼";
                    return IsGrenade(item) ? "수류탄" : "아이템";
            }
        }

        public static string LootAmount(LootItem item)
        {
            if (item == null) return "";
            return (LootKind)item.Kind.Value == LootKind.Weapon ? "탄창 " + item.Amount.Value + "발" : "x" + item.Amount.Value;
        }

        public static string LootIconKey(LootItem item)
        {
            if (item == null) return "";
            switch ((LootKind)item.Kind.Value)
            {
                case LootKind.Weapon: return WeaponKey(item.WeaponId.Value);
                case LootKind.Ammo: return AmmoKey(item.Ammo.Value);
                case LootKind.Medkit: return "medkit";
                case LootKind.Optic: return ItemIcons.OpticKey(item.Optic.Value);
                default:
                    if (TryArmorSlot(item, out int slot)) return (slot == HelmetSlot ? "helmet_" : "vest_") + item.Tier.Value;
                    return IsGrenade(item) ? "grenade" : "";
            }
        }

        public static string Describe(InventoryDragSource source, Combatant combatant)
        {
            switch (source.Kind)
            {
                case InventorySourceKind.Vicinity:
                    return DescribeLoot(source.Loot);
                case InventorySourceKind.Ammo:
                    return AmmoName(source.Index) + " 탄약\n보유 " + Reserve(combatant, source.Index) + "발";
                case InventorySourceKind.Medkit:
                    return "구급상자\n보유 " + (combatant != null ? combatant.Medkits.Value : 0) + "개";
                case InventorySourceKind.Grenade:
                    return "수류탄\n보유 " + Grenades(combatant) + "개";
                case InventorySourceKind.Weapon:
                    int weaponId = WeaponId(combatant, source.Index);
                    var weapon = GameData.Weapons.Find(weaponId);
                    return weapon == null ? "빈 총기 슬롯" : weapon.Name + "\n" + AmmoName(AmmoIndex(weapon));
                case InventorySourceKind.Optic:
                    int opticId = OpticOf(combatant, source.Index);
                    var def = OpticRules.Current.Find(opticId);
                    return def == null ? "" : def.Name + "\n조준경 · " + (def.Zoom > 1f ? def.Zoom.ToString("0.#") + "배율" : "1배율");
                case InventorySourceKind.Helmet: return GearDescription(combatant, HelmetSlot, "헬멧");
                case InventorySourceKind.Vest: return GearDescription(combatant, VestSlot, "조끼");
                default: return "";
            }
        }

        static string GearDescription(Combatant combatant, int slot, string name)
        {
            var gear = Gear(combatant, slot);
            return gear.Has ? name + " " + gear.Tier + "단계\n내구도 " + gear.Durability + " / " + gear.MaxDurability : name + "\n비어 있음";
        }

        static string DescribeLoot(LootItem loot)
        {
            if (loot == null) return "";
            switch ((LootKind)loot.Kind.Value)
            {
                case LootKind.Weapon:
                    var weapon = GameData.Weapons.Find(loot.WeaponId.Value);
                    return (weapon != null ? weapon.Name : "무기") + "\n무기 · " +
                           (weapon != null ? AmmoName(AmmoIndex(weapon)) : "구경 미상") + " · 탄창 " + loot.Amount.Value + "발";
                case LootKind.Ammo: return AmmoName(loot.Ammo.Value) + " 탄약\n탄약 · 수량 " + loot.Amount.Value + "발";
                case LootKind.Medkit: return "구급상자\n소모품 · 수량 " + loot.Amount.Value + "개";
                case LootKind.Optic:
                    var optic = OpticRules.Current.Find(loot.Optic.Value);
                    return LootNames.OfOptic(loot.Optic.Value) + "\n조준경 · " + (optic != null && optic.Zoom > 1f ? optic.Zoom.ToString("0.#") + "배율" : "1배율");
                default:
                    if (TryArmorSlot(loot, out int slot)) return (slot == HelmetSlot ? "헬멧" : "조끼") + "\n장구류";
                    return IsGrenade(loot) ? "수류탄\n투척물 · 수량 " + loot.Amount.Value + "개" : "아이템";
            }
        }

        // Five weapon slots (ADR-058): 0-1 primary, 2 sidearm, 3 melee (always empty for now), 4 throwable.
        public const int WeaponSlotCount = Loadout.SlotCount;
        public const int SidearmSlot = Loadout.SidearmSlot;
        public const int MeleeSlot = Loadout.MeleeSlot;
        public const int ThrowableSlot = Loadout.ThrowableSlot;

        public static int WeaponId(Combatant combatant, int slot) => combatant == null ? 0 : combatant.WeaponInSlot(slot);
        public static int WeaponMag(Combatant combatant, int slot) => combatant == null ? 0 : combatant.MagInSlot(slot);

        // Korean label of a weapon slot for the cards: 주무기 1, 주무기 2, 보조무기, 근접, 투척.
        public static string SlotLabel(int slot)
        {
            switch (slot)
            {
                case 0: return "주무기 1";
                case 1: return "주무기 2";
                case Loadout.SidearmSlot: return "보조무기";
                case Loadout.MeleeSlot: return "근접";
                case Loadout.ThrowableSlot: return "투척";
                default: return "";
            }
        }

        // Does the slot take this weapon (mirrors the server's rule: a pistol only goes in the sidearm slot)?
        public static bool SlotAccepts(int slot, int weaponId)
        {
            var weapon = GameData.Weapons.Find(weaponId);
            return weapon != null && Loadout.SlotTakes(slot, weapon.Category);
        }

        // The optic id on the weapon in the slot (0 = none).
        public static int OpticOf(Combatant combatant, int slot)
        {
            var optics = combatant != null ? combatant.GetComponent<CombatantOptics>() : null;
            return optics != null ? optics.OpticInSlot(slot) : 0;
        }

        public static string OpticName(int opticId) => opticId > 0 ? LootNames.OfOptic(opticId) : "";

        public static bool IsOptic(LootItem loot) => loot != null && (LootKind)loot.Kind.Value == LootKind.Optic;

        // Mirrors the server: the slot holds a weapon that takes this optic.
        public static bool CanAttachOptic(Combatant combatant, int slot, int opticId) =>
            combatant != null && Optics.CanAttach(combatant.WeaponInSlot(slot), opticId);

        public static void AttachOpticFromGround(Combatant combatant, LootItem loot, int slot)
        {
            var optics = combatant != null ? combatant.GetComponent<CombatantOptics>() : null;
            if (optics != null && loot != null && loot.IsSpawned) optics.CmdAttachFromGround(loot.NetworkObject, slot);
        }

        public static void DropOptic(Combatant combatant, int slot)
        {
            var optics = combatant != null ? combatant.GetComponent<CombatantOptics>() : null;
            if (optics != null) optics.CmdDropOptic(slot);
        }

        // "단발" / "연사" for the weapon in the slot; empty for an empty slot.
        public static string FireMode(Combatant combatant, int slot) => combatant == null ? "" : combatant.FireModeOf(slot);

        // The weapon in hand is put away (X).
        public static bool IsHolstered(Combatant combatant) => combatant != null && combatant.Holstered.Value;

        public static void Pickup(Combatant combatant, LootItem loot)
        {
            if (combatant != null && loot != null && loot.IsSpawned) combatant.CmdPickup(loot.NetworkObject);
        }

        public static void PickupToSlot(Combatant combatant, LootItem loot, int slot)
        {
            if (combatant != null && loot != null && loot.IsSpawned) combatant.CmdPickupToSlot(loot.NetworkObject, slot);
        }

        public static void SelectSlot(Combatant combatant, int slot)
        {
            if (combatant != null) combatant.CmdSelectSlot(slot);
        }

        // The two primary weapons trade places.
        public static void SwapWeaponSlots(Combatant combatant)
        {
            if (combatant != null) combatant.CmdSwapWeaponSlots();
        }

        // Any two slots; the server refuses a swap that puts a weapon in a slot that does not take it.
        public static void SwapWeaponSlots(Combatant combatant, int a, int b)
        {
            if (combatant != null) combatant.CmdSwapSlots(a, b);
        }

        public static void UseMedkit(Combatant combatant)
        {
            if (combatant != null) combatant.CmdUseMedkit();
        }

        public static void DropWeapon(Combatant combatant, int slot)
        {
            if (combatant != null) combatant.CmdDropWeapon(slot);
        }

        public static void DropAmmo(Combatant combatant, int ammoIndex)
        {
            if (combatant != null) combatant.CmdDropAmmo(ammoIndex);
        }

        public static void DropMedkit(Combatant combatant)
        {
            if (combatant != null) combatant.CmdDropMedkit();
        }

        public static void DropArmor(Combatant combatant, int slot)
        {
            var gear = combatant != null ? combatant.GetComponent<CombatantGear>() : null;
            if (gear != null) gear.CmdDropArmor(slot);
        }

        public static void DropGrenade(Combatant combatant)
        {
            var user = combatant != null ? combatant.GetComponent<ThrowableUser>() : null;
            if (user != null) user.CmdDropGrenade();
        }

        // The server takes the piece only when it is better than the worn one (see CanEquipArmor).
        public static void EquipArmor(Combatant combatant, LootItem loot, int slot) => Pickup(combatant, loot);
    }
}

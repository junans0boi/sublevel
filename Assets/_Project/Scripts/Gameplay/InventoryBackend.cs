using FishNet.Object;
using Game.Core;
using Game.Net;

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

        static readonly string[] AmmoNames = { "9mm", "5.56mm", "12게이지", "7.62mm" };
        static readonly string[] AmmoKeys = { "ammo_9mm", "ammo_556", "ammo_12ga", "ammo_762" };

        public static string AmmoName(int index) => index >= 0 && index < AmmoNames.Length ? AmmoNames[index] : "탄약";
        public static string AmmoKey(int index) => index >= 0 && index < AmmoKeys.Length ? AmmoKeys[index] : "";
        public static string WeaponKey(int weaponId) => weaponId > 0 ? "weapon_" + weaponId : "";
        public static int AmmoIndex(WeaponDef weapon) => weapon != null ? (int)weapon.Ammo : -1;
        // [MASTER] Keep the pending four-calibre enum conversion in this adapter until its names are final.
        public static AmmoType AmmoValue(int index) => (AmmoType)index;

        public static int Reserve(Combatant combatant, int index)
        {
            if (combatant == null) return 0;
            // [MASTER] Replace these legacy fields with the four-calibre replicated contract.
            switch (index)
            {
                case 0: return combatant.ReserveLight.Value;
                case 1: return combatant.ReserveMedium.Value;
                case 2: return combatant.ReserveShell.Value;
                default: return 0;
            }
        }

        public static int Grenades(Combatant combatant)
        {
            // [MASTER] Read the replicated grenade count once Combatant exposes it.
            return 0;
        }

        public static InventoryGearState Gear(Combatant combatant, int slot)
        {
            // [MASTER] Bridge CombatantGear.TierOf/DurabilityOf/MaxDurabilityOf/Has here.
            return default;
        }

        public static bool TryArmorSlot(LootItem loot, out int slot)
        {
            // [MASTER] Map the armour LootKind values after the gear branch lands.
            slot = -1;
            return false;
        }

        public static bool IsGrenade(LootItem loot)
        {
            // [MASTER] Map the throwable LootKind after the grenade branch lands.
            return false;
        }

        public static bool CanEquipArmor(Combatant combatant, LootItem loot, int slot)
        {
            // [MASTER] Reject a lower-tier replacement using CombatantGear and the armour loot payload.
            return TryArmorSlot(loot, out int lootSlot) && lootSlot == slot;
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
                default:
                    if (TryArmorSlot(item, out int slot)) return (slot == HelmetSlot ? "helmet_" : "vest_") + item.Amount.Value;
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
                default:
                    if (TryArmorSlot(loot, out int slot)) return (slot == HelmetSlot ? "헬멧" : "조끼") + "\n장구류";
                    return IsGrenade(loot) ? "수류탄\n투척물 · 수량 " + loot.Amount.Value + "개" : "아이템";
            }
        }

        public static int WeaponId(Combatant combatant, int slot) => combatant == null ? 0 : slot == 0 ? combatant.Slot0.Value : combatant.Slot1.Value;
        public static int WeaponMag(Combatant combatant, int slot) => combatant == null ? 0 : slot == 0 ? combatant.Mag0.Value : combatant.Mag1.Value;

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

        public static void SwapWeaponSlots(Combatant combatant)
        {
            if (combatant != null) combatant.CmdSwapWeaponSlots();
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
            if (combatant != null) combatant.CmdDropArmor(slot);
        }

        public static void DropGrenade(Combatant combatant)
        {
            // [MASTER] Call the authoritative grenade drop request once its name is finalized.
        }

        public static void EquipArmor(Combatant combatant, LootItem loot, int slot)
        {
            // [MASTER] Replace with the authoritative armour-to-slot request if it differs from CmdPickup.
            Pickup(combatant, loot);
        }

        // [MASTER] Temporary extension: an instance ServerRpc with this exact name takes precedence after merge.
        public static void CmdSwapWeaponSlots(this Combatant combatant)
        {
        }

        // [MASTER] Temporary extension: current pickup remains authoritative but cannot honor the requested slot.
        public static void CmdPickupToSlot(this Combatant combatant, NetworkObject loot, int slot) => combatant.CmdPickup(loot);

        // [MASTER] Temporary extension until Combatant.CmdDropArmor(int) arrives with the gear branch.
        public static void CmdDropArmor(this Combatant combatant, int slot)
        {
        }
    }
}

using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;

namespace Game.Net
{
    // A pickup lying in the world. Spawned and removed by the server only.
    public class LootItem : NetworkBehaviour
    {
        public readonly SyncVar<int> Kind = new SyncVar<int>();
        public readonly SyncVar<int> WeaponId = new SyncVar<int>();
        public readonly SyncVar<int> Ammo = new SyncVar<int>();
        public readonly SyncVar<int> Amount = new SyncVar<int>();

        public static readonly List<LootItem> All = new List<LootItem>();

        public override void OnStartNetwork() => All.Add(this);
        public override void OnStopNetwork() => All.Remove(this);

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy() => All.Remove(this);

        // Call before spawning.
        public void Init(LootEntry entry)
        {
            Kind.Value = (int)entry.Kind;
            WeaponId.Value = entry.WeaponId;
            Ammo.Value = (int)entry.Ammo;
            Amount.Value = entry.Amount;
        }

        public LootEntry ToEntry() => new LootEntry
        {
            Kind = (LootKind)Kind.Value,
            WeaponId = WeaponId.Value,
            Ammo = (AmmoType)Ammo.Value,
            Amount = Amount.Value,
        };

        public string Label()
        {
            switch ((LootKind)Kind.Value)
            {
                case LootKind.Weapon: return GameData.Weapons.Find(WeaponId.Value)?.Name ?? "Weapon";
                case LootKind.Ammo: return AmmoName((AmmoType)Ammo.Value) + " x" + Amount.Value;
                default: return "Medkit";
            }
        }

        public static string AmmoName(AmmoType type) =>
            type == AmmoType.Light ? "Light ammo" : type == AmmoType.Medium ? "Medium ammo" : "Shells";
    }
}

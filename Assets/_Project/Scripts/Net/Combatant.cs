using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Anything that can shoot and be shot: players, the training dummy, later bots.
    // The server owns health and loadout; clients read the SyncVars and send requests (ADR-029).
    public class Combatant : NetworkBehaviour
    {
        public const float PickupRange = 3f;

        [SerializeField] bool _isPlayer;
        [SerializeField] float _maxHealth = 100f;
        [SerializeField] float _eyeHeight = 1.6f;

        public readonly SyncVar<float> Hp = new SyncVar<float>();
        public readonly SyncVar<bool> Dead = new SyncVar<bool>();
        public readonly SyncVar<bool> Reloading = new SyncVar<bool>();
        public readonly SyncVar<int> ActiveSlot = new SyncVar<int>();
        public readonly SyncVar<int> Slot0 = new SyncVar<int>();
        public readonly SyncVar<int> Slot1 = new SyncVar<int>();
        public readonly SyncVar<int> Mag0 = new SyncVar<int>();
        public readonly SyncVar<int> Mag1 = new SyncVar<int>();
        public readonly SyncVar<int> ReserveLight = new SyncVar<int>();
        public readonly SyncVar<int> ReserveMedium = new SyncVar<int>();
        public readonly SyncVar<int> ReserveShell = new SyncVar<int>();
        public readonly SyncVar<int> Medkits = new SyncVar<int>();

        public static readonly List<Combatant> All = new List<Combatant>();
        public static Combatant LocalPlayer { get; private set; }

        // Raised on every client for each projectile: (origin, end point).
        public event Action<Vector3, Vector3> ShotFired;

        static readonly float[] YawBuf = new float[32];
        static readonly float[] PitchBuf = new float[32];

        Health _health;
        Loadout _loadout;
        System.Random _rng;

        public bool IsPlayer => _isPlayer;
        public Vector3 EyePosition => transform.position + Vector3.up * _eyeHeight;
        public int ActiveWeaponId => ActiveSlot.Value == 0 ? Slot0.Value : Slot1.Value;
        public int ActiveMag => ActiveSlot.Value == 0 ? Mag0.Value : Mag1.Value;
        public WeaponDef ActiveWeapon => GameData.Weapons.Find(ActiveWeaponId);

        public int ReserveFor(AmmoType type) =>
            type == AmmoType.Light ? ReserveLight.Value : type == AmmoType.Medium ? ReserveMedium.Value : ReserveShell.Value;

        public override void OnStartNetwork() => All.Add(this);

        public override void OnStopNetwork()
        {
            All.Remove(this);
            if (LocalPlayer == this) LocalPlayer = null;
        }

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy() => OnStopNetwork();

        public override void OnStartServer()
        {
            _health = new Health(_maxHealth);
            _loadout = new Loadout(GameData.Weapons, GameData.Rules.MedkitHeal);
            // Derived from the match seed so a match can be replayed.
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.Seed : 0;
            _rng = new System.Random(unchecked(matchSeed * 31 + ObjectId * 7919 + 17));
            Push();
        }

        public override void OnStartClient()
        {
            if (_isPlayer && IsOwner) LocalPlayer = this;
        }

        void Update()
        {
            if (IsServerInitialized && _loadout != null && _loadout.Tick(Time.timeAsDouble)) Push();
        }

        void Push()
        {
            Hp.Value = _health.Current;
            Dead.Value = _health.IsDead;
            Reloading.Value = _loadout.IsReloading;
            ActiveSlot.Value = _loadout.ActiveSlot;
            Slot0.Value = _loadout.WeaponInSlot(0);
            Slot1.Value = _loadout.WeaponInSlot(1);
            Mag0.Value = _loadout.MagInSlot(0);
            Mag1.Value = _loadout.MagInSlot(1);
            ReserveLight.Value = _loadout.Reserve(AmmoType.Light);
            ReserveMedium.Value = _loadout.Reserve(AmmoType.Medium);
            ReserveShell.Value = _loadout.Reserve(AmmoType.Shell);
            Medkits.Value = _loadout.Medkits;
        }

        // ---- Server API (players reach it through the Cmd* requests below; AI calls it directly) ----

        [Server]
        public bool ServerFire(Vector3 direction)
        {
            float sqr = direction.sqrMagnitude;
            if (_health.IsDead || float.IsNaN(sqr) || float.IsInfinity(sqr) || sqr < 1e-6f) return false;
            // Shots always start at the server's idea of this combatant's eyes, never at a client-supplied point.
            var origin = EyePosition;
            var weapon = _loadout.ActiveWeapon;
            if (!_loadout.TryFire(Time.timeAsDouble)) return false;

            var aim = Quaternion.LookRotation(direction.normalized);
            int pellets = DamageRules.ShotOffsets(weapon, _rng, YawBuf, PitchBuf);
            for (int i = 0; i < pellets; i++)
            {
                var dir = aim * Quaternion.Euler(PitchBuf[i], YawBuf[i], 0f) * Vector3.forward;
                var end = origin + dir * weapon.Range;
                if (Physics.Raycast(origin, dir, out var hit, weapon.Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    var target = hit.collider.GetComponentInParent<Combatant>();
                    if (target != null && target != this)
                        target.ServerTakeDamage(DamageRules.AtDistance(weapon, hit.distance));
                }
                RpcShotFx(origin, end);
            }
            Push();
            return true;
        }

        [Server]
        public void ServerTakeDamage(float amount)
        {
            if (_health.ApplyDamage(amount) > 0f) Push();
        }

        [Server]
        public void ServerGive(LootEntry loot)
        {
            switch (loot.Kind)
            {
                case LootKind.Weapon:
                    int replaced = _loadout.AddWeapon(loot.WeaponId, loot.Amount, out int replacedMag);
                    // A replaced weapon goes back on the ground instead of vanishing.
                    if (replaced != 0 && MatchDirector.Instance != null)
                        MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = replaced, Amount = replacedMag }, transform.position + Vector3.up * 0.3f);
                    break;
                case LootKind.Ammo: _loadout.AddAmmo(loot.Ammo, loot.Amount); break;
                case LootKind.Medkit: _loadout.AddMedkits(loot.Amount); break;
            }
            Push();
        }

        [Server]
        public void ServerReload()
        {
            if (!_health.IsDead && _loadout.TryStartReload(Time.timeAsDouble)) Push();
        }

        // ---- Owner requests ----

        [ServerRpc]
        public void CmdFire(Vector3 direction) => ServerFire(direction);

        [ServerRpc]
        public void CmdReload() => ServerReload();

        [ServerRpc]
        public void CmdSelectSlot(int slot)
        {
            if (!_health.IsDead && _loadout.SelectSlot(slot)) Push();
        }

        [ServerRpc]
        public void CmdUseMedkit()
        {
            if (_loadout.TryUseMedkit(_health)) Push();
        }

        [ServerRpc]
        public void CmdPickup(NetworkObject lootObject)
        {
            if (_health.IsDead || lootObject == null) return;
            var item = lootObject.GetComponent<LootItem>();
            if (item == null || !item.IsSpawned) return;
            if ((item.transform.position - transform.position).sqrMagnitude > PickupRange * PickupRange) return;
            ServerGive(item.ToEntry());
            item.Despawn();
        }

        // Solo has no revival: a dead player may ask the server for a fresh match.
        [ServerRpc]
        public void CmdRequestRestart()
        {
            if (_health.IsDead && MatchDirector.Instance != null) MatchDirector.Instance.Restart();
        }

        [ObserversRpc]
        void RpcShotFx(Vector3 origin, Vector3 end) => ShotFired?.Invoke(origin, end);
    }
}

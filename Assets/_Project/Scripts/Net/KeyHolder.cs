using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The keys a combatant carries (ADR-058 (7)): one key starts a key elevator (ADR-170).
    // The server owns the count; the owner only asks to use one, naming the pass.
    [RequireComponent(typeof(Combatant))]
    public class KeyHolder : NetworkBehaviour, ILootReceiver
    {
        // The loot kind of a key pickup.
        public const LootKind KeyLootKind = LootKind.Key;
        // Shown on pickups and in the inventory.
        public const string KeyName = "봉쇄 열쇠";
        public const int MaxCarried = 4;

        public readonly SyncVar<int> Keys = new SyncVar<int>();
        // Gas protection carried (pollution, ADR-237): filters and antidotes live here beside the keys.
        public readonly SyncVar<int> Filters = new SyncVar<int>();
        public readonly SyncVar<int> Antidotes = new SyncVar<int>();
        // The cover in force, on the match clock, so the owner's HUD can show what is left (0 = none).
        public readonly SyncVar<float> CoverUntil = new SyncVar<float>();
        public readonly SyncVar<float> CoverFactor = new SyncVar<float>();

        // The local player's own, for the HUD. Null when there is none.
        public static KeyHolder Local { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Local = null;

        Combatant _combatant;

        void Awake() => _combatant = GetComponent<Combatant>();

        public override void OnStartClient()
        {
            if (IsOwner && _combatant != null && _combatant.IsPlayer) Local = this;
        }

        // An owner that returns after a reconnect (#166) gets the object after it already started as an observer.
        public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prev) => OnStartClient();

        public override void OnStopClient()
        {
            if (Local == this) Local = null;
        }

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        // ---- Server API ----

        // Returns how many were taken (the carry limit may refuse some or all).
        [Server]
        public int ServerAdd(int amount)
        {
            int taken = Mathf.Clamp(amount, 0, MaxCarried - Keys.Value);
            if (taken > 0) Keys.Value += taken;
            return taken;
        }

        // Key pickups, handed over by Combatant.ServerGive. A pickup stays on the ground when no key fits.
        public bool ServerTryTake(LootEntry loot)
        {
            if (!IsServerInitialized || loot == null) return false;
            if (loot.Kind == LootKind.GasFilter) return TakeGas(Filters, loot.Amount);
            if (loot.Kind == LootKind.Antidote) return TakeGas(Antidotes, loot.Amount);
            if (loot.Kind != KeyLootKind) return false;
            return ServerAdd(Mathf.Max(1, loot.Amount)) > 0;
        }

        static bool TakeGas(SyncVar<int> count, int amount)
        {
            int taken = Mathf.Clamp(Mathf.Max(1, amount), 0, MaxCarried - count.Value);
            if (taken <= 0) return false;
            count.Value += taken;
            return true;
        }

        // Uses one filter (kind 0) or antidote (1): the gas cover starts on the server clock.
        [Server]
        public bool ServerUseGas(int kind)
        {
            var pollution = PollutionDirector.Instance;
            if (_combatant == null || _combatant.Dead.Value || pollution == null || !pollution.Ready) return false;
            var count = kind == 0 ? Filters : Antidotes;
            if (count.Value <= 0) return false;
            count.Value--;
            if (kind == 0) pollution.ServerUseFilter(_combatant.ObjectId); else pollution.ServerUseAntidote(_combatant.ObjectId);
            var cover = pollution.ExposureOf(_combatant.ObjectId)?.Cover ?? default;
            CoverUntil.Value = (float)cover.Until;
            CoverFactor.Value = cover.Factor;
            return true;
        }

        // The use key: the antidote when carried (the stronger), else the filter.
        public void RequestGasCover()
        {
            if (Antidotes.Value > 0) CmdUseGas(1);
            else if (Filters.Value > 0) CmdUseGas(0);
        }

        [ServerRpc] public void CmdUseGas(int kind) => ServerUseGas(kind == 0 ? 0 : 1);

        // WP 7: the key elevator takes one key when it is inserted and gives it back when the use is void.
        [Server]
        public bool ServerConsumeKey()
        {
            if (Keys.Value <= 0) return false;
            Keys.Value--;
            return true;
        }

        // ---- Owner request ----

        [ServerRpc] public void CmdKeyInsert(int station) => KeyElevatorNet.Instance?.ServerInsert(_combatant, station);
        [ServerRpc] public void CmdKeyClose() => KeyElevatorNet.Instance?.ServerClose(_combatant);
        [ServerRpc] public void CmdKeySelect(int region) => KeyElevatorNet.Instance?.ServerSelect(_combatant, region);

    }
}

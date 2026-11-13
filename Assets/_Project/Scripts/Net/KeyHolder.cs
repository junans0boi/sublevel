using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The keys a combatant carries (ADR-058 (7)): one key opens one pass barrier for LockdownRules.KeyOpenSeconds.
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
            if (!IsServerInitialized || loot == null || loot.Kind != KeyLootKind) return false;
            return ServerAdd(Mathf.Max(1, loot.Amount)) > 0;
        }

        // Uses one key on a pass barrier: refused when dead, without a key, when the pass is not locked or already
        // open, or when this combatant is not next to it. AI and tests call this directly.
        [Server]
        public bool ServerUseKey(int pass)
        {
            var director = ZoneDirector.Instance;
            if (director == null || _combatant == null || _combatant.Dead.Value || Keys.Value <= 0) return false;
            if (!director.IsPassLocked(pass) || director.IsPassOpen(pass)) return false;
            if (director.PassDistance(pass, transform.position) > director.Rules.PassInteractRange) return false;
            Keys.Value--;
            director.ServerOpenPass(pass, director.Rules.KeyOpenSeconds);
            return true;
        }

        // ---- Owner request ----

        [ServerRpc]
        public void CmdUseKey(int pass) => ServerUseKey(pass);
    }
}

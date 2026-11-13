using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Fire (ADR-110): a fire on the ground lights whoever stands in it (ServerIgnite, called by the fire every frame),
    // and a lit body burns for a few seconds more after it leaves. Armour does not stop it. The server runs it;
    // clients read Burning (HUD, no steady aim).
    public partial class Combatant
    {
        public readonly SyncVar<bool> Burning = new SyncVar<bool>();

        float _burnLeft, _fireOwed;
        int _inFireFrame = -10;
        Combatant _fireStarter;

        // Server: this body is standing in a fire this frame; `starter` gets the credit.
        public void ServerIgnite(Combatant starter)
        {
            if (!IsServerInitialized) return;
            _inFireFrame = Time.frameCount;
            _fireStarter = starter;
        }

        // Server, for tests: the body burns for the full burn time as if it had just left a fire.
        public void ServerSetBurning(float seconds)
        {
            if (IsServerInitialized) _burnLeft = Mathf.Max(_burnLeft, seconds);
        }

        void LateUpdate()
        {
            if (!IsServerInitialized || _health == null) return;
            if (_health.IsDead)
            {
                _burnLeft = _fireOwed = 0f;
                if (Burning.Value) Burning.Value = false;
                return;
            }
            bool inFire = _inFireFrame >= Time.frameCount - 1;
            float damage = Molotov.Step(ref _burnLeft, inFire, Time.deltaTime, MolotovRules.Current);
            bool burning = _burnLeft > 0f;
            if (burning != Burning.Value) Burning.Value = burning;
            if (damage <= 0f) { if (!burning) _fireOwed = 0f; return; }
            // Whole points at a time: one health change a frame would flood the network.
            _fireOwed += damage;
            if (_fireOwed < 1f) return;
            float dealt = Mathf.Floor(_fireOwed);
            _fireOwed -= dealt;
            ServerTakeDamage(new DamageInfo { Amount = dealt, Attacker = _fireStarter, Zone = HitZone.Body, WeaponId = 0, Cause = DamageCause.Fire });
        }
    }
}

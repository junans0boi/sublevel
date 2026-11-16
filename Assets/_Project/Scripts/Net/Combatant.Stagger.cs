using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Stopping power on the server (ADR-182, ADR-240). The server owns the stagger state (hits never add up); it sends
    // (shakeDeg, slow, seconds left) plus a counter that moves on every accepted hit, so an equal hit renews the clock on
    // the victim's side too. Movement is computed by the owner (player) or the server (bot): both multiply their speed
    // by StaggerMoveFactorOf. The aim shake is read by the owner's camera through StaggerShakeDegOf.
    public partial class Combatant
    {
        readonly StaggerState _stagger = new StaggerState();
        StaggerReplica _staggerView; // what MoveFactorOf/ShakeDegOf read, on the server and on every client

        // x = shake degrees, y = share of speed lost, z = seconds left when sent.
        public readonly SyncVar<Vector3> StaggerWire = new SyncVar<Vector3>();
        public readonly SyncVar<byte> StaggerSeq = new SyncVar<byte>();

        public static float StaggerMoveFactorOf(Combatant c) => c != null ? c._staggerView.SpeedFactor(Time.timeAsDouble) : 1f;
        public static float StaggerShakeDegOf(Combatant c) => c != null ? c._staggerView.ShakeDeg(Time.timeAsDouble) : 0f;

        void HookStagger()
        {
            StaggerWire.OnChange += (_, __, asServer) => { if (!asServer) StaggerReceive(); };
            StaggerSeq.OnChange += (_, __, asServer) => { if (!asServer) StaggerReceive(); };
        }

        void StaggerReceive()
        {
            var w = StaggerWire.Value;
            if (w.z <= 0f) _staggerView.Clear();
            else _staggerView.Set(w.x, w.y, w.z, Time.timeAsDouble);
        }

        // Called from ServerTakeDamage after health went down. Only bullets stagger (not fire, drowning, melee).
        void ServerStaggerOnDamage(in DamageInfo hit)
        {
            if (_health.IsDead) { ServerStaggerClear(); return; }
            if (hit.Cause != DamageCause.Shot || hit.WeaponId <= 0) return;
            double now = Time.timeAsDouble;
            var weapon = GameData.Weapons.Find(hit.WeaponId);
            if (!StaggerHits.Register(_stagger, weapon, hit.Zone, now)) return;
            var rules = StoppingRules.Current;
            float shake = _stagger.ShakeDeg(now), slow = 1f - _stagger.SpeedFactor(now);
            _staggerView.Set(shake, slow, rules.Seconds, now);
            StaggerWire.Value = new Vector3(shake, slow, rules.Seconds);
            StaggerSeq.Value = (byte)(StaggerSeq.Value + 1);
        }

        void ServerStaggerClear()
        {
            _stagger.Clear();
            _staggerView.Clear();
            if (StaggerWire.Value != Vector3.zero) StaggerWire.Value = Vector3.zero;
        }
    }
}

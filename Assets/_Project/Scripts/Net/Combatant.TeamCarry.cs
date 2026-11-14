using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Carrying a knocked teammate on the back (ADR-129): the carrier walks slowly, the carried one bleeds on but
    // cannot be revived meanwhile. The server decides who carries whom; the carried body follows the carrier on the
    // side that moves it (its owner's client, or the server for a bot) through TryCarryPose.
    public partial class Combatant
    {
        // On the carrier: who it carries (-1 none). On the carried: who carries it (-1 none).
        public readonly SyncVar<int> CarryTargetId = new SyncVar<int>(-1);
        public readonly SyncVar<int> CarriedById = new SyncVar<int>(-1);

        Combatant _carryTarget;

        public bool IsCarrying => CarryTargetId.Value >= 0;
        public bool BeingCarried => CarriedById.Value >= 0;

        // ---- Server ----

        [Server]
        public bool ServerBeginCarry(Combatant target)
        {
            if (_health == null || target == null || target == this || !Standing || IsCarrying || Reviving || VehicleSeat.IsAttached(this)) return false;
            if (!target.IsServerInitialized || target.Dead.Value) return false;
            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (!KnockCarry.CanStart(distance, Standing, false, target.Knocked.Value, !target.BeingRevived && !target.BeingCarried && !target.IsCarrying, IsTeammateOf(target), TeamRules.Current)) return false;
            _carryTarget = target;
            CarryTargetId.Value = target.ObjectId;
            target.CarriedById.Value = ObjectId;
            _loadout.CancelReload();
            Aim.Value = 0;
            Lean.Value = 0;
            Push();
            return true;
        }

        [Server]
        public void ServerEndCarry()
        {
            var target = _carryTarget;
            _carryTarget = null;
            if (CarryTargetId.Value != -1) CarryTargetId.Value = -1;
            if (target != null && target.CarriedById.Value == ObjectId) target.CarriedById.Value = -1;
        }

        // The carried one's own side of an ended carry (it died, was knocked again): its carrier lets go.
        void ServerReleaseCarried()
        {
            if (CarriedById.Value < 0) return;
            var carrier = FindById(CarriedById.Value);
            if (carrier != null && carrier.IsServerInitialized) carrier.ServerEndCarry();
            else CarriedById.Value = -1;
        }

        void TickCarry()
        {
            if (!IsCarrying) return;
            var target = _carryTarget;
            if (target == null || !target.IsSpawned || target.CarriedById.Value != ObjectId)
            {
                ServerEndCarry();
                return;
            }
            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (!KnockCarry.KeepGoing(distance, Standing && !VehicleSeat.IsAttached(this), target.Knocked.Value && !target.Dead.Value, TeamRules.Current)) ServerEndCarry();
        }

        static Combatant FindById(int id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].ObjectId == id) return All[i];
            return null;
        }

        // ---- Every side ----

        void HookCarry() => CarriedById.OnChange += OnCarriedByChanged;

        // The two bodies must not push each other while one rides the other.
        void OnCarriedByChanged(int prev, int next, bool asServer)
        {
            Toggle(prev, false);
            Toggle(next, true);
            void Toggle(int id, bool ignore)
            {
                var carrier = id >= 0 ? FindById(id) : null;
                if (carrier == null) return;
                foreach (var a in GetComponentsInChildren<Collider>(true))
                    foreach (var b in carrier.GetComponentsInChildren<Collider>(true))
                        if (a != b) Physics.IgnoreCollision(a, b, ignore);
            }
        }

        // Where this body rides while carried (and the carrier's heading); false when nobody carries it.
        public bool TryCarryPose(out Vector3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            if (!BeingCarried) return false;
            var carrier = FindById(CarriedById.Value);
            if (carrier == null) return false;
            yaw = carrier.transform.eulerAngles.y;
            var o = KnockCarry.Offset(yaw);
            position = carrier.transform.position + new Vector3(o.x, o.y, o.z);
            return true;
        }

        // ---- Owner requests ----

        [ServerRpc]
        public void CmdBeginCarry(NetworkObject target)
        {
            if (target != null) ServerBeginCarry(target.GetComponent<Combatant>());
        }

        [ServerRpc]
        public void CmdEndCarry() => ServerEndCarry();
    }
}

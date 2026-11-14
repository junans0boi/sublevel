using System.Reflection;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Observing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Observer condition (#167, ADR-135): combatants, loot, vehicles and thrown items reach a client only within the
    // radii of InterestRules (the rule itself is pure, see Interest). Everything else on the network stays global.
    // Fish-Net re-checks timed conditions about every 0.5 s and removes an object from a client by despawning it there.
    public sealed class InterestCondition : ObserverCondition
    {
        InterestKind _kind;
        Combatant _combatant;
        Vehicle _vehicle;

        // Installs the condition as the manager's default for every NetworkObject spawned from now on (call before spawning).
        public static void Install(NetworkManager manager)
        {
            var field = typeof(FishNet.Managing.Observing.ObserverManager).GetField("_defaultConditions", BindingFlags.Instance | BindingFlags.NonPublic);
            var list = field?.GetValue(manager.ObserverManager) as System.Collections.Generic.List<ObserverCondition>;
            if (list == null) { Debug.LogError("InterestCondition.Install: ObserverManager._defaultConditions not found"); return; }
            if (list.Exists(c => c is InterestCondition)) return;
            list.Add(CreateInstance<InterestCondition>());
        }

        public override void Initialize(NetworkObject networkObject)
        {
            base.Initialize(networkObject);
            _combatant = networkObject.GetComponent<Combatant>();
            _vehicle = networkObject.GetComponent<Vehicle>();
            _kind = _combatant != null ? InterestKind.Combatant
                : _vehicle != null ? InterestKind.Vehicle
                : networkObject.GetComponent<LootItem>() != null ? InterestKind.Loot
                : networkObject.GetComponent<ThrownItem>() != null ? InterestKind.Thrown
                : InterestKind.Always;
        }

        public override ObserverConditionType GetConditionType() => ObserverConditionType.Timed;

        public override bool ConditionMet(NetworkConnection connection, bool currentlyAdded, out bool notProcessed)
        {
            notProcessed = false;
            var rules = InterestRules.Current;
            if (_kind == InterestKind.Always || !rules.Enabled || NetworkObject.Owner == connection) return true;

            var me = connection.FirstObject != null ? connection.FirstObject.GetComponent<Combatant>() : null;
            var observer = new InterestObserver { HasBody = false, NearestMeters = float.MaxValue, Dead = me != null && me.Dead.Value };
            Vector3 here = NetworkObject.transform.position;
            foreach (var owned in connection.Objects)
            {
                if (owned == null) continue;
                observer.HasBody = true;
                float d = Vector3.Distance(owned.transform.position, here);
                if (d < observer.NearestMeters) observer.NearestMeters = d;
            }
            return Interest.Observes(rules, _kind, currentlyAdded, in observer, TeamMember(me));
        }

        // The object is a member of the client's team, or a vehicle carrying one (a teammate far away stays in the
        // vehicle it rides, or its seat points at nothing).
        bool TeamMember(Combatant me)
        {
            if (me == null || !me.HasTeam) return false;
            if (_combatant != null) return _combatant.HasTeam && _combatant.TeamId.Value == me.TeamId.Value;
            if (_vehicle == null) return false;
            var spawned = NetworkObject.ServerManager.Objects.Spawned;
            for (int seat = 0; seat < _vehicle.SeatCount; seat++)
            {
                int id = _vehicle.OccupantId(seat);
                if (id >= 0 && spawned.TryGetValue(id, out var nob))
                {
                    var c = nob.GetComponent<Combatant>();
                    if (c != null && c.HasTeam && c.TeamId.Value == me.TeamId.Value) return true;
                }
            }
            return false;
        }
    }
}

using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Running people over (#112, ADR-084). Every physics step, a vehicle moving at RoadkillMinKmh or more looks for
    // people on foot touching its body box (plus a small reach):
    // - the server hurts them by the closing speed and the vehicle's mass (Vehicles.RoadkillDamage; the driver gets
    //   the credit, nothing to the driver's own team) and pushes them away (CombatantShove, on whoever moves them);
    // - the simulating side (the driver's client) lets the vehicle pass through them for a moment instead of stopping
    //   dead against their capsule.
    // The closing speed is the vehicle's velocity along the outward face of its box nearest the person: the bumper
    // takes the forward speed, a side takes only a slide sideways, so passing close by does nothing.
    public partial class Vehicle
    {
        const float RoadkillReach = 0.3f;          // metres beyond the body box
        const float RoadkillCooldownSeconds = 0.8f; // one hit per person per pass
        const float PassThroughSeconds = 1f;

        static readonly Collider[] RoadkillHits = new Collider[32];
        readonly Dictionary<Combatant, float> _roadkillUntil = new Dictionary<Combatant, float>();
        readonly List<Combatant> _expired = new List<Combatant>();
        Bounds _bodyBox;
        bool _bodyBoxReady;

        // The vehicle's world velocity as this side sees it.
        Vector3 SeenVelocity => _simulating ? _body.linearVelocity : _estimatedVelocity;

        void TickRoadkill()
        {
            bool server = IsServerInitialized;
            if (!server && !_simulating) return;
            ExpirePassThrough();
            var r = Rules;
            var velocity = SeenVelocity;
            // The race hall (ADR-508): nobody is run over, shoved or carried off; a moving race car drives through people on foot.
            bool race = IsRaceCar;
            if (race ? velocity.sqrMagnitude < 1f : Vehicles.ToKmh(velocity.magnitude) < r.RoadkillMinKmh) return;
            if (!_bodyBoxReady) MeasureBodyBox();
            var half = _bodyBox.extents + Vector3.one * RoadkillReach;
            int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(_bodyBox.center), half, RoadkillHits, transform.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = RoadkillHits[i].GetComponentInParent<Combatant>();
                if (c == null || c.Dead.Value || VehicleSeat.IsAttached(c)) continue;
                if (race)
                {
                    if (_simulating && !_roadkillUntil.ContainsKey(c)) SetPassThrough(c, true);
                    _roadkillUntil[c] = Time.time + PassThroughSeconds;
                    continue;
                }
                if (_roadkillUntil.ContainsKey(c)) continue;
                var normal = FaceNormal(c.transform.position + Vector3.up * 0.9f);
                float closing = Vector3.Dot(velocity, normal);
                if (Vehicles.ToKmh(closing) < r.RoadkillMinKmh) continue;
                _roadkillUntil[c] = Time.time + Mathf.Max(RoadkillCooldownSeconds, PassThroughSeconds);
                if (_simulating) SetPassThrough(c, true);
                if (server) ServerRunOver(c, closing, velocity, normal);
            }
        }

        // The body box in the vehicle's own space, from its box colliders.
        void MeasureBodyBox()
        {
            _bodyBoxReady = true;
            bool any = false;
            foreach (var col in _colliders)
            {
                if (!(col is BoxCollider box) || box == null) continue;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? -0.5f : 0.5f, (k & 4) == 0 ? -0.5f : 0.5f);
                    var local = transform.InverseTransformPoint(box.transform.TransformPoint(box.center + Vector3.Scale(box.size, corner)));
                    if (!any) { _bodyBox = new Bounds(local, Vector3.zero); any = true; }
                    else _bodyBox.Encapsulate(local);
                }
            }
            if (!any) _bodyBox = new Bounds(Vector3.up * 0.8f, new Vector3(1.8f, 1.4f, _halfLength * 2f));
        }

        // The outward normal (world) of the body box's side face (front, back, left or right) nearest `point`.
        Vector3 FaceNormal(Vector3 point)
        {
            var local = transform.InverseTransformPoint(point) - _bodyBox.center;
            var ext = _bodyBox.extents;
            bool side = Mathf.Abs(local.x) / Mathf.Max(0.1f, ext.x) > Mathf.Abs(local.z) / Mathf.Max(0.1f, ext.z);
            var face = side ? new Vector3(Mathf.Sign(local.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(local.z));
            return transform.TransformDirection(face);
        }

        void SetPassThrough(Combatant c, bool ignore)
        {
            var theirs = c.GetComponentsInChildren<Collider>();
            foreach (var mine in _colliders)
            {
                if (mine == null || !mine.enabled || !mine.gameObject.activeInHierarchy) continue;
                foreach (var col in theirs)
                    if (col != null && col.enabled && col.gameObject.activeInHierarchy) Physics.IgnoreCollision(mine, col, ignore);
            }
        }

        void ExpirePassThrough()
        {
            if (_roadkillUntil.Count == 0) return;
            _expired.Clear();
            foreach (var pair in _roadkillUntil) if (pair.Key == null || Time.time >= pair.Value) _expired.Add(pair.Key);
            foreach (var c in _expired)
            {
                _roadkillUntil.Remove(c);
                // Someone who got in meanwhile keeps the seat's own ignore.
                if (_simulating && c != null && VehicleSeat.Find(c)?.Vehicle != this) SetPassThrough(c, false);
            }
        }

        [Server]
        void ServerRunOver(Combatant c, float closing, Vector3 velocity, Vector3 normal)
        {
            var r = Rules;
            var driver = FindCombatant(Seat0.Value);
            int driverTeam = driver != null ? driver.TeamId.Value : Teams.None;
            float damage = Vehicles.RoadkillDamage(closing, r.MassKg, driverTeam, c.TeamId.Value, r);
            if (damage > 0f)
                c.ServerTakeDamage(new DamageInfo { Amount = damage, Attacker = driver, Zone = HitZone.Body, Cause = DamageCause.Vehicle });
            if (c.Dead.Value) return;
            float shove = Vehicles.RoadkillShove(closing, r.MassKg, r);
            // Away along the vehicle's way and off the face it hit, a little up.
            var away = Vector3.ProjectOnPlane(velocity.normalized + normal * 0.5f, Vector3.up).normalized;
            var push = away * shove + Vector3.up * shove * r.RoadkillLiftShare;
            // Movement is the owner's (ADR-029): a player's client moves its body; a bot is moved here.
            if (c.Owner.IsValid) TargetShove(c.Owner, c.ObjectId, push);
            else CombatantShove.Apply(c, push);
        }

        [TargetRpc]
        void TargetShove(NetworkConnection conn, int objectId, Vector3 push)
        {
            var c = FindCombatant(objectId);
            if (c != null) CombatantShove.Apply(c, push);
        }
    }
}

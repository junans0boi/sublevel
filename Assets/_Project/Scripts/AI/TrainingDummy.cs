using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.AI
{
    // M1 stand-in opponent (ADR-030): stands still and shoots any living player it can see. Server-side only.
    [RequireComponent(typeof(Combatant))]
    public class TrainingDummy : MonoBehaviour
    {
        [SerializeField] int _weaponId = 1;
        [SerializeField] float _sightRange = 25f;
        [SerializeField] float _shotInterval = 0.6f;
        [SerializeField] float _aimErrorDeg = 2f;

        Combatant _self;
        float _nextShot;
        bool _equipped;

        void Awake() => _self = GetComponent<Combatant>();

        void Update()
        {
            if (!_self.IsServerInitialized || _self.Dead.Value) return;
            if (!_equipped)
            {
                var weapon = GameData.Weapons.Find(_weaponId);
                _self.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = _weaponId, Amount = weapon.MagSize });
                _self.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = weapon.Ammo, Amount = 9999 });
                _equipped = true;
            }
            if (_self.ActiveMag == 0) { _self.ServerReload(); return; }
            if (Time.time < _nextShot) return;

            var target = FindVisiblePlayer();
            if (target == null) return;
            _nextShot = Time.time + _shotInterval;
            var eye = _self.EyePosition;
            var toChest = target.transform.position + Vector3.up * 1.1f - eye;
            var error = Quaternion.Euler(Random.Range(-_aimErrorDeg, _aimErrorDeg), Random.Range(-_aimErrorDeg, _aimErrorDeg), 0f);
            _self.ServerFire(eye, Quaternion.LookRotation(toChest) * error * Vector3.forward);
        }

        Combatant FindVisiblePlayer()
        {
            Combatant best = null;
            float bestSqr = _sightRange * _sightRange;
            var eye = _self.EyePosition;
            foreach (var c in Combatant.All)
            {
                if (!c.IsPlayer || c.Dead.Value) continue;
                var to = c.transform.position + Vector3.up * 1.1f - eye;
                if (to.sqrMagnitude > bestSqr) continue;
                if (!Physics.Raycast(eye, to.normalized, out var hit, _sightRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.GetComponentInParent<Combatant>() != c) continue;
                best = c;
                bestSqr = to.sqrMagnitude;
            }
            return best;
        }
    }
}

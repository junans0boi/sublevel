using System;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Spawn protection after the elevator start (ADR-055 (2)(3)). MatchDirector adds it on the server to every
    // combatant it puts in an elevator. Until the doors open and for ProtectionSeconds after, damage is ignored;
    // the combatant's first shot or throw ends it at once. While the doors are closed a bot's brain is paused and
    // the bot rides its elevator car up (a player is held by its own controller through InputBlocked, which also
    // moves it with the car, and by the door itself).
    // What clients know (HUD) comes from ElevatorDoors' broadcasts: use the static helpers, which work on every side.
    [RequireComponent(typeof(Combatant))]
    public class SpawnProtection : MonoBehaviour
    {
        // The AI's per-frame driver lives in Game.AI, above this assembly: paused by name while the doors are shut.
        const string BotBrainComponent = "BotController";

        Combatant _combatant;
        ElevatorDoors _doors;
        ThrowableUser _throws;
        Behaviour _brain;
        Func<DamageInfo, float> _previous;
        Func<DamageInfo, float> _filter;
        bool _begun, _ended, _brainPaused;
        int _weapon0, _weapon1, _mag0, _mag1, _thrown;

        // ---- Every side (HUD) ----

        // The match started its players in elevators (the doors exist on this peer).
        public static bool Deployed => ElevatorDoors.Instance != null;
        // The local player must not move or fire yet: the doors are still closed. PlayerController reads this.
        public static bool InputBlocked => ElevatorDoors.Instance != null && ElevatorDoors.Instance.DoorsClosed;
        // Seconds until the doors open (0 once open or without elevators). HUD: "문 열림 N".
        public static float DoorSecondsLeft => ElevatorDoors.Instance != null && ElevatorDoors.Instance.DoorsClosed ? ElevatorDoors.Instance.DoorSecondsLeft : 0f;
        // Protection left for a combatant as this peer knows it (0 = none). HUD: "보호 N초".
        public static float SecondsLeftOf(Combatant combatant)
        {
            if (combatant == null || ElevatorDoors.Instance == null || combatant.Dead.Value) return 0f;
            return ElevatorDoors.Instance.ProtectionSecondsLeftOf(combatant.ObjectId);
        }
        public static float LocalSecondsLeft => SecondsLeftOf(Combatant.LocalPlayer);

        // ---- Server ----

        // Server clock (Time.timeAsDouble): when protection ends. PositiveInfinity while the doors are not yet
        // scheduled; 0 once ended early. Clients see the same moment on their own clock through SecondsLeftOf.
        public double ProtectedUntil => _ended || !_begun ? 0d : _doors != null ? _doors.ServerProtectedUntil : 0d;
        public bool IsProtected => !_ended && _begun && Time.timeAsDouble < ProtectedUntil;
        public float SecondsLeft
        {
            get
            {
                if (!IsProtected) return 0f;
                // Not scheduled yet: the whole wait is still ahead.
                if (!_doors.ServerScheduled) return DeploymentRules.Current.DoorDelaySeconds + DeploymentRules.Current.ProtectionSeconds;
                return (float)(ProtectedUntil - Time.timeAsDouble);
            }
        }
        // Doors still shut for this combatant.
        public bool IsHeld => _begun && _doors != null && _doors.ServerDoorsClosed;
        public bool EndedEarly => _ended;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _throws = GetComponent<ThrowableUser>();
            _filter = FilterDamage;
        }

        // Called by MatchDirector right after the combatant spawned (its own components have set their damage
        // filters by then; this one goes in front of them and hands everything through once protection is over).
        public void ServerBegin(ElevatorDoors doors)
        {
            _doors = doors;
            _begun = true;
            _brain = GetComponent(BotBrainComponent) as Behaviour;
            Chain();
            Snapshot();
            Hold(IsHeld);
        }

        // Fired or threw: protection is over for good.
        public void ServerEnd()
        {
            if (_ended || !_begun) return;
            _ended = true;
            if (_doors != null) _doors.ServerProtectionEnded(_combatant);
        }

        void Update()
        {
            if (!_begun || _combatant == null || !_combatant.IsServerInitialized) return;
            // Someone set a filter after us (equipment added later): keep it, in front of it.
            if (_combatant.ServerDamageFilter != _filter) Chain();
            Hold(IsHeld && !_combatant.Dead.Value);
            // A held bot rides its elevator car up (ADR-065 (1)); the server moves it and NetworkTransform shows it.
            // Players are moved by their owner (PlayerController), so the server leaves them alone.
            if (IsHeld && !_combatant.IsPlayer && ElevatorCar.TryRideFloor(transform.position, out float feet))
            {
                var p = transform.position;
                transform.position = new Vector3(p.x, feet, p.z);
            }
            // Only once the doors are open: nothing fired from inside a shut station (it only hits the door) counts.
            if (!_ended && !IsHeld && Attacked()) ServerEnd();
            Snapshot();
        }

        void OnDestroy()
        {
            if (_combatant != null && _combatant.ServerDamageFilter == _filter) _combatant.ServerDamageFilter = _previous;
            Hold(false);
        }

        void Chain()
        {
            var current = _combatant.ServerDamageFilter;
            if (current == _filter) return;
            _previous = current;
            _combatant.ServerDamageFilter = _filter;
        }

        // Combatant.ServerDamageFilter: nothing gets through while protected; afterwards the previous filter (armour)
        // decides as before.
        // The zone (radiation) is not held back: protection is against other combatants, and radiation is never
        // instant (ADR-061).
        float FilterDamage(DamageInfo info)
        {
            if (IsProtected && info.Cause != DamageCause.Zone) return 0f;
            return _previous != null ? _previous(info) : info.Amount;
        }

        void Hold(bool held)
        {
            if (_brain == null || held == _brainPaused) return;
            _brainPaused = held;
            _brain.enabled = !held;
        }

        // A round left a magazine (same weapon in the slot, fewer rounds) or a grenade was thrown since last frame.
        // Watched from the replicated state so no fire path needs a hook; reloads and pickups only add rounds,
        // and a weapon that changed slot resets the comparison.
        bool Attacked()
        {
            bool shot0 = _combatant.Slot0.Value == _weapon0 && _weapon0 != 0 && _combatant.Mag0.Value < _mag0;
            bool shot1 = _combatant.Slot1.Value == _weapon1 && _weapon1 != 0 && _combatant.Mag1.Value < _mag1;
            bool threw = _throws != null && _throws.ServerThrows > _thrown;
            return shot0 || shot1 || threw;
        }

        void Snapshot()
        {
            _weapon0 = _combatant.Slot0.Value;
            _weapon1 = _combatant.Slot1.Value;
            _mag0 = _combatant.Mag0.Value;
            _mag1 = _combatant.Mag1.Value;
            _thrown = _throws != null ? _throws.ServerThrows : 0;
        }
    }
}

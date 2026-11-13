using System.Collections.Generic;
using FishNet;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The training range scene (#153): reads what the local player does, ticks the checklist, puts the table's items
    // out, builds the runtime-only parts (doors, the pool's water) and keeps the player alive. The scene itself is built
    // by TrainingRangeBuilder; the match around it is the ordinary host-run one without bots or region lockdown.
    public class TrainingRange : MonoBehaviour
    {
        public const string SceneName = "TrainingRange";
        // The player starts hurt so the healing step has something to do (bandages heal up to 75).
        public const float StartHp = 50f;
        const float PickupWatchMeters = 4f;
        const float RespawnAwayMeters = 5f;
        const float RespawnSeconds = 2f;

        public static TrainingRange Instance { get; private set; }

        public TrainingLoot[] Loot = new TrainingLoot[0];
        public Transform[] DoorSpots = new Transform[0];
        public float DoorWidth = 1.3f, DoorHeight = 2.3f;
        public bool HasPool;
        public Vector2 PoolCentre, PoolSize;
        public float PoolSurfaceY;

        public TrainingChecklist Checklist { get; } = new TrainingChecklist();
        public int TargetHits { get; private set; }

        readonly List<LootItem> _watched = new List<LootItem>();
        Combatant _hooked;
        int _shots, _pickups, _doorOpens, _zeroChanges;
        float _lastZero = float.NaN, _zeroKeyAt = -10f;
        bool _doorsBuilt, _lootPut, _injured;
        float _nextRespawn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            Instance = this;
            Door.Moved += OnDoorMoved;
            if (HasPool) WaterVolume.Create(transform, PoolCentre, PoolSize, PoolSurfaceY, true);
            new GameObject("TrainingHud").AddComponent<TrainingHud>().transform.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Door.Moved -= OnDoorMoved;
            Unhook();
        }

        void OnDoorMoved(Door door, bool opening)
        {
            if (opening) _doorOpens++;
        }

        void Update()
        {
            BuildDoors();
            var me = Combatant.LocalPlayer;
            if (me == null || PlayerController.Local == null) return;
            if (_hooked != me) Hook(me);
            if (me.IsServerInitialized) ServerTick(me);
            var o = Observe(me, PlayerController.Local);
            Checklist.Observe(o);
        }

        // Doors are runtime objects of the match's DoorSystem, which the match director adds a little after the scene loads.
        void BuildDoors()
        {
            if (_doorsBuilt || DoorSystem.Instance == null) return;
            _doorsBuilt = true;
            var system = DoorSystem.Instance;
            foreach (var spot in DoorSpots)
                if (spot != null) system.Add(Door.Create(system.transform, system.Count, spot.position, spot.rotation, DoorWidth, DoorHeight));
        }

        void Hook(Combatant me)
        {
            Unhook();
            _hooked = me;
            me.ShotResolved += OnShot;
        }

        void Unhook()
        {
            if (_hooked != null) _hooked.ShotResolved -= OnShot;
            _hooked = null;
        }

        static readonly Collider[] Overlap = new Collider[16];

        void OnShot(in Combatant.ShotResult shot)
        {
            if (shot.Phase != Combatant.ShotPhase.Impact) _shots++;
            if (shot.Phase == Combatant.ShotPhase.Launch) return;
            int n = Physics.OverlapSphereNonAlloc(shot.End, 0.25f, Overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            TrainingTarget hit = null;
            for (int i = 0; i < n && hit == null; i++) hit = Overlap[i].GetComponentInParent<TrainingTarget>();
            if (hit == null) return;
            hit.Hit();
            TargetHits++;
        }

        TrainingObservation Observe(Combatant me, PlayerController pc)
        {
            CountPickups(pc.transform.position);
            // The zero counts when PageUp / PageDown was pressed just before it changed (it also moves by itself, e.g. when a scope is swapped).
            if (pc.LastInput.ZeroSteps != 0) _zeroKeyAt = Time.time;
            if (me.ZeroMeters != _lastZero && !float.IsNaN(_lastZero) && Time.time - _zeroKeyAt < 1f) _zeroChanges++;
            _lastZero = me.ZeroMeters;
            var optics = me.GetComponent<CombatantOptics>();
            int attached = 0;
            if (optics != null)
                for (int slot = 0; slot < CombatantOptics.MaxSlots; slot++)
                    for (int place = 0; place < Attachments.Slots; place++)
                        if (optics.AttachmentInSlot(slot, (AttachSlot)place) != 0) attached++;
            var healing = me.GetComponent<CombatantHealing>();
            var throws = me.GetComponent<ThrowableUser>();
            return new TrainingObservation
            {
                PlanarSpeed = me.PlanarSpeed,
                Sprinting = pc.Sprinting,
                Stance = pc.BodyStance,
                Parkour = pc.ParkourUnderWay,
                Swimming = pc.Swimming,
                Seated = VehicleSeat.IsSeated(me),
                Reloading = me.Reloading.Value,
                Aim = pc.Aim,
                Shots = _shots,
                Pickups = _pickups,
                Attachments = attached,
                Heals = healing != null ? healing.ServerCompleted : 0,
                Throws = throws != null ? throws.ServerThrows : 0,
                DoorOpens = _doorOpens,
                ZeroChanges = _zeroChanges,
            };
        }

        // A pickup is loot that was within reach and is gone from the world (taken, or fitted straight onto the gun).
        void CountPickups(Vector3 at)
        {
            for (int i = _watched.Count - 1; i >= 0; i--)
            {
                var item = _watched[i];
                if (item != null && LootItem.All.Contains(item)) continue;
                _pickups++;
                _watched.RemoveAt(i);
            }
            float sqr = PickupWatchMeters * PickupWatchMeters;
            foreach (var item in LootItem.All)
                if (item != null && (item.transform.position - at).sqrMagnitude < sqr && !_watched.Contains(item)) _watched.Add(item);
            // Something walked out of reach is not a pickup.
            _watched.RemoveAll(item => item != null && (item.transform.position - at).sqrMagnitude > sqr * 1.5f);
        }

        // Host only: the table's items, the starting injury, and no way to die here.
        void ServerTick(Combatant me)
        {
            var director = MatchDirector.Instance;
            if (director == null || !InstanceFinder.IsServerStarted) return;
            if (!_lootPut)
            {
                _lootPut = true;
                foreach (var l in Loot) if (l != null) director.ServerSpawnLoot(l.Entry, l.transform.position);
            }
            else if (Time.time >= _nextRespawn) { _nextRespawn = Time.time + RespawnSeconds; RespawnTaken(director, me.transform.position); }
            if (_injured) return;
            _injured = true;
            me.ServerTakeDamage(me.Hp.Value - StartHp);
            var before = me.ServerDamageFilter;
            me.ServerDamageFilter = d => Mathf.Min(before != null ? before(d) : d.Amount, Mathf.Max(0f, me.Hp.Value - 1f));
        }

        // A table spot whose item is gone gets a new one once the player is away from it.
        void RespawnTaken(MatchDirector director, Vector3 player)
        {
            foreach (var l in Loot)
            {
                if (l == null) continue;
                var at = l.transform.position;
                if ((new Vector3(at.x, player.y, at.z) - player).sqrMagnitude < RespawnAwayMeters * RespawnAwayMeters) continue;
                bool there = false;
                foreach (var item in LootItem.All)
                    if (item != null && (item.transform.position - at).sqrMagnitude < 0.36f) { there = true; break; }
                if (!there) director.ServerSpawnLoot(l.Entry, at);
            }
        }
    }
}

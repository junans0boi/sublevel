using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server -> the driver of an island race car, twice a second (ADR-509): the lap in progress (-1 = not on a clean lap), the last and the best.
    public struct IslandLapBroadcast : IBroadcast { public float Running, Last, Best; }

    // The race of the waiting island (ADR-505..511, #438), server only: the race cars stand on their grid spots in the pit, anyone waiting
    // takes one (the usual boarding) and drives the circuit; a car left empty (or upside down) goes back to its spot, a car that somehow left the hall is put
    // back with its people out. At the departure everyone gets out and the cars are despawned. Damage, fuel and roadkill are off for any
    // vehicle in the hall (Vehicle.IsRaceCar). Owned by the WaitingRoom.
    public sealed class RaceRoom : MonoBehaviour
    {
        public const float ResetSeconds = 4f;   // an empty car waits this long before it is put back on its spot

        sealed class Car
        {
            public Vehicle Vehicle; public Vector3 Home; public Quaternion HomeRotation; public float EmptySince = -1f;
            public Combatant Driver; public RaceLap Lap; public float Last, Best;   // the lap of whoever drives it now (ADR-507)
        }
        readonly RaceRecords _records = new RaceRecords();
        public RaceRecords Records => _records;   // the room's best laps (ADR-509): the board's LAP TOP3
        float _nextLapSend;

        NetworkManager _nm;
        readonly List<Car> _cars = new List<Car>();

        public static RaceRoom Current { get; private set; }
        public int CarCount => _cars.Count;
        public Vehicle CarAt(int i) => _cars[i].Vehicle;

        public static RaceRoom TryCreate(NetworkManager nm, Transform island)
        {
            if (island.Find("RaceTrack") == null) { Debug.LogWarning("[RaceRoom] the island has no race hall (rebuild World): no race"); return null; }
            var prefabs = VehicleSpawner.Instance != null ? VehicleSpawner.Instance.CarPrefabs() : null;
            if (prefabs == null || prefabs.Count == 0) { Debug.LogWarning("[RaceRoom] no car models in the scene: no race"); return null; }
            var room = island.gameObject.AddComponent<RaceRoom>();
            room._nm = nm;
            var spots = RaceTrack.GridSpots();
            for (int i = 0; i < spots.Count; i++)
            {
                var (lx, lz, yaw) = spots[i];
                var at = island.TransformPoint(new Vector3(lx, 0.05f, lz));
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var nob = Instantiate(prefabs[i % prefabs.Count], at, rot);
                var vehicle = nob.GetComponent<Vehicle>();
                vehicle.ServerSetPaint(VehicleColors.Pick(i, i % prefabs.Count));
                nm.ServerManager.Spawn(nob);
                room._cars.Add(new Car { Vehicle = vehicle, Home = at, HomeRotation = rot });
            }
            Current = room;
            Debug.Log($"[RaceRoom] open: {room._cars.Count} race cars on the grid, lap {RaceTrack.LapLength:F0} m (circuit)");
            return room;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            bool send = Time.unscaledTime >= _nextLapSend;
            if (send) _nextLapSend = Time.unscaledTime + 0.5f;
            foreach (var car in _cars)
            {
                var v = car.Vehicle;
                if (v == null || !v.IsServerInitialized) continue;
                var p = v.transform.position;
                JudgeLap(car, p, now, send);
                if (v.AnyOccupant)
                {
                    car.EmptySince = -1f;
                    // The walls keep a car in; one that is out anyway (a client gone wrong) comes back with its people out.
                    if (!RaceTrack.InArea(p.x, p.z)) { Debug.LogWarning($"[RaceRoom] {v.name} left the hall at {p}: back to the grid"); v.ServerEjectAll(); v.ServerPlace(car.Home, car.HomeRotation); }
                    continue;
                }
                if (car.EmptySince < 0f) car.EmptySince = Time.time;
                bool away = (p - car.Home).sqrMagnitude > 0.25f, flipped = v.transform.up.y < 0.5f;
                if (Time.time - car.EmptySince >= ResetSeconds && (away || flipped)) v.ServerPlace(car.Home, car.HomeRotation);
            }
        }

        // The server's lap judge (ADR-507/509): its own copy of the car's place, for whoever is at the wheel now (a new driver starts afresh).
        // A clean lap goes into the room's records under the driver's name; the driver hears the running, last and best lap twice a second.
        void JudgeLap(Car car, Vector3 p, double now, bool send)
        {
            var driver = car.Vehicle.FindCombatant(car.Vehicle.OccupantId(Vehicles.DriverSeat));
            if (driver != car.Driver) { car.Driver = driver; car.Lap = driver != null ? new RaceLap(RaceTrack.Course) : null; car.Last = car.Best = 0f; }
            if (driver == null || car.Lap == null) return;
            float lap = car.Lap.Step(RaceTrack.Project(p.x - WaitingIsland.CentreX, p.z - WaitingIsland.CentreZ).s, now);
            if (lap > 0f)
            {
                car.Last = lap;
                if (car.Best <= 0f || lap < car.Best) car.Best = lap;
                var room = WaitingRoom.Current;
                if (room != null) _records.Submit(room.KeyOf(driver), WaitingRoom.NameOf(driver), lap);
                Debug.Log($"[RaceRoom] {driver.name}: lap {lap:0.00} s (best {car.Best:0.00})");
            }
            if (send && driver.Owner != null && driver.Owner.IsValid)
                _nm.ServerManager.Broadcast(driver.Owner, new IslandLapBroadcast { Running = car.Lap.Timing ? (float)(now - car.Lap.Start) : -1f, Last = car.Last, Best = car.Best }, true, Channel.Unreliable);
        }

        // The departure (ADR-511): everyone out, the cars gone.
        public void ServerClose()
        {
            foreach (var car in _cars)
            {
                if (car.Vehicle == null) continue;
                car.Vehicle.ServerEjectAll();
                if (car.Vehicle.IsSpawned) _nm.ServerManager.Despawn(car.Vehicle.NetworkObject);
            }
            _cars.Clear();
            _records.Clear();
            Debug.Log("[RaceRoom] closed: race cars out and gone");
            if (Current == this) Current = null;
            Destroy(this);
        }
    }
}

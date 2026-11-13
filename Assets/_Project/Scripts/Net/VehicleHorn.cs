using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The horn (ADR-067, ADR-075): the driver's left mouse button while no pistol is out. Held = sounding, replicated
    // so every side plays it (VehicleAudio). The server lets bots hear it as a noise (BotNoise "Other") on the press
    // and every HornNoiseInterval while held. It stops when the driver leaves the seat or draws the pistol.
    //
    // Sits on the vehicle prefab next to Vehicle (VehicleBuilder). Without it there is no horn.
    [RequireComponent(typeof(Vehicle))]
    public class VehicleHorn : NetworkBehaviour
    {
        public readonly SyncVar<bool> Held = new SyncVar<bool>();

        Vehicle _vehicle;
        float _nextNoise;
        bool _sent;

        // Server: presses heard so far, for tests.
        public int ServerPresses { get; private set; }

        public bool Sounding => Held.Value;

        void Awake() => _vehicle = GetComponent<Vehicle>();

        void Update()
        {
            if (!IsServerInitialized || !Held.Value) return;
            var driver = Driver();
            if (driver == null || !MayHonk(driver))
            {
                Held.Value = false;
                return;
            }
            if (Time.time >= _nextNoise) EmitNoise(driver);
        }

        Combatant Driver()
        {
            int id = _vehicle.OccupantId(Vehicles.DriverSeat);
            if (id < 0) return null;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ObjectId == id) return all[i];
            return null;
        }

        // The driver, alive, no pistol out (with a pistol out the button fires it).
        static bool MayHonk(Combatant c) => c != null && !c.Dead.Value && !c.TeamBlocksAction && c.ActiveWeaponId == 0;

        void EmitNoise(Combatant driver)
        {
            var rules = FuelRules.Current;
            _nextNoise = Time.time + rules.HornNoiseInterval;
            var p = transform.position;
            BotNoise.Emit(p.x, p.y + 1f, p.z, rules.HornHearingRadius, driver.ObjectId, NoiseKind.Other);
        }

        // ---- Server ----

        // `c` must be the driver; false when refused.
        public bool ServerSet(Combatant c, bool on)
        {
            if (!IsServerInitialized) return false;
            if (on && (c == null || Driver() != c || !MayHonk(c))) return false;
            if (!on && c != null && Driver() != c) return false;
            if (on && !Held.Value)
            {
                ServerPresses++;
                EmitNoise(c);
            }
            Held.Value = on;
            return true;
        }

        // ---- Owner requests ----

        // The local driver's button state, sent only when it changes. The host is the server: no round trip.
        public void Request(Combatant c, bool on)
        {
            if (on == _sent) return;
            _sent = on;
            if (IsServerInitialized) ServerSet(c, on);
            else CmdSet(on);
        }

        // The driver's client does not always own the vehicle yet (ownership follows the driver's seat, ADR-069):
        // ownership is not required, the sender must be the driver.
        [ServerRpc(RequireOwnership = false)]
        void CmdSet(bool on, NetworkConnection sender = null)
        {
            var driver = Driver();
            if (driver == null || driver.Owner != sender) return;
            ServerSet(driver, on);
        }
    }
}

using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The ONLY place the in-game HUD reads server-owned data from (ADR-057). Everything is a static method that
    // returns a value. Where the server contract does not exist yet the method returns a marked fallback;
    // lines tagged [MASTER] are switched to the new API here and nowhere else.
    public static class HudBackend
    {
        // Weapon strip order (ADR-058): (1)(2) primary, (3) sidearm = pistol only, (4) melee = empty for now, (5) throwable.
        public const int SlotCount = Combatant.SlotCount;
        public const int Sidearm = 2, Melee = 3, Throwable = 4;

        // ---- Weapons ----

        // Weapon id in strip slot 0..4 (0 = empty). The melee slot is always empty and the throwable slot holds no weapon id.
        public static int WeaponInSlot(Combatant me, int slot) => me != null ? me.WeaponInSlot(slot) : 0;

        public static int MagInSlot(Combatant me, int slot) => me != null ? me.MagInSlot(slot) : 0;

        // The slot in hand; -1 while the weapon is put away (X): empty hands.
        public static int ActiveSlot(Combatant me) => me == null || me.Holstered.Value ? -1 : me.ActiveSlot.Value;

        public static int ActiveWeaponId(Combatant me) => WeaponInSlot(me, ActiveSlot(me));
        public static int ActiveMag(Combatant me) => MagInSlot(me, ActiveSlot(me));

        // "단발" / "연사" for the weapon in `slot`.
        public static string FireModeOf(Combatant me, int slot) => me != null && slot >= 0 ? me.FireModeOf(slot) : "";

        // Short name of the optic fitted to the weapon in `slot` ("" = none), e.g. "2x", "반사" / "투영" for the 1x sights.
        public static string OpticOf(Combatant me, int slot)
        {
            if (me == null || slot < 0) return "";
            var optics = me.GetComponent<CombatantOptics>();
            var def = optics != null ? OpticRules.Current.Find(optics.OpticInSlot(slot)) : null;
            if (def == null) return "";
            // Short enough for the corner of a weapon-strip cell; the full names are in the optic table.
            if (def.Zoom > 1f) return def.Zoom.ToString("0.#") + "x";
            return def.Kind == OpticKind.Holo ? "투영" : "반사";
        }

        // True while a magnified scope overlay is up: the HUD then hides its crosshair.
        public static bool ScopeActive => ScopeView.Active;

        public static int Reserve(Combatant me, WeaponDef weapon) =>
            weapon == null ? 0 : InventoryBackend.Reserve(me, InventoryBackend.AmmoIndex(weapon));

        public static int Grenades(Combatant me) => InventoryBackend.Grenades(me);

        // ---- Body ----

        public static Stance StanceOf(Combatant me)
        {
            var stance = me != null ? me.GetComponent<CombatantStance>() : null;
            return stance != null ? stance.Current : Stance.Stand;
        }

        // Seconds of damage immunity left after deployment; 0 when none.
        public static float SpawnProtectionLeft(Combatant me)
        {
            return SpawnProtection.SecondsLeftOf(me);
        }

        // The elevator doors are still shut: no moving or firing; the HUD counts down to "문 열림".
        public static bool DoorsClosed => SpawnProtection.InputBlocked && !Vehicle.EventRace;   // an event race locks input before GO, no elevator
        public static float DoorSecondsLeft => SpawnProtection.DoorSecondsLeft;

        // ---- Vehicle (ADR-065 (5)) ----

        // The seat the local player sits in, 1..4 (1 = driver); 0 when not in a car.
        public static int VehicleSeatNumber(Combatant me) => VehicleSeat.TryGet(me, out _, out int seat) ? seat + 1 : 0;

        // The speed of the car the player sits in (km/h, never negative); 0 when not in a car.
        public static float VehicleSpeedKmh(Combatant me) => VehicleSeat.TryGet(me, out var vehicle, out _) ? vehicle.SpeedKmh : 0f;

        // "운전석", "조수석", "뒷좌석" in a car, "운전", "동승" on a motorcycle; "" when not in a vehicle.
        public static string VehicleSeatName(Combatant me) =>
            VehicleSeat.TryGet(me, out var vehicle, out int seat) ? SeatName(seat, vehicle.IsMotorcycle) : "";

        public static string SeatName(int seat, bool motorcycle) =>
            seat < 0 ? "" : motorcycle ? (seat == 0 ? "운전" : "동승") : seat == 0 ? "운전석" : seat == 1 ? "조수석" : "뒷좌석";

        // On foot next to a car with a free seat: F gets in.
        public static bool VehicleEnterable(Combatant me) =>
            me != null && !me.Dead.Value && !VehicleSeat.IsAttached(me)
            && Vehicle.NearestEnterable(me.transform.position, VehicleRules.Current.EnterRange, out _) != null;

        // ---- Match ----

        public static int Alive()
        {
            var match = MatchState.Instance;
            if (match != null && match.TotalCount > 0) return match.AliveCount;
            int alive = 0;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && !all[i].Dead.Value) alive++;
            return alive;
        }

        public static int Kills()
        {
            var match = MatchState.Instance;
            return match != null ? match.LocalKills : 0;
        }

        // ---- Map ----

        // The baked overview picture of the whole island (square, covering 0..WorldInfo.Size on X and Z); null = draw a grid.
        public static Texture2D MapImage()
        {
            return Game.Gameplay.MapImage.Get();
        }
    }
}

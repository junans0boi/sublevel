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

        // Short name of the optic fitted to the weapon in `slot` ("" = none), e.g. "2x", "레드도트".
        public static string OpticOf(Combatant me, int slot)
        {
            if (me == null || slot < 0) return "";
            var optics = me.GetComponent<CombatantOptics>();
            var def = optics != null ? OpticRules.Current.Find(optics.OpticInSlot(slot)) : null;
            if (def == null) return "";
            return def.Zoom > 1f ? def.Zoom.ToString("0.#") + "x" : def.Name;
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
        public static bool DoorsClosed => SpawnProtection.InputBlocked;
        public static float DoorSecondsLeft => SpawnProtection.DoorSecondsLeft;

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

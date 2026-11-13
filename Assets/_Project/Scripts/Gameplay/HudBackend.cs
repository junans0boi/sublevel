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
        public const int SlotCount = 5;            // [MASTER] = Combatant.SlotCount
        public const int Sidearm = 2, Melee = 3, Throwable = 4;

        // ---- Weapons ----

        // Weapon id in strip slot 0..2 (0 = empty). The melee slot is always empty and the throwable slot holds no weapon id.
        public static int WeaponInSlot(Combatant me, int slot)
        {
            if (me == null) return 0;
            switch (slot)
            {
                case 0: return me.Slot0.Value;   // [MASTER] me.WeaponInSlot(slot) for every slot
                case 1: return me.Slot1.Value;
                default: return 0;               // [MASTER] sidearm: me.WeaponInSlot(2)
            }
        }

        public static int MagInSlot(Combatant me, int slot)
        {
            if (me == null) return 0;
            switch (slot)
            {
                case 0: return me.Mag0.Value;    // [MASTER] me.MagInSlot(slot)
                case 1: return me.Mag1.Value;
                default: return 0;               // [MASTER] sidearm: me.MagInSlot(2)
            }
        }

        // The slot in hand; anything outside 0..4 (for example -1 once unarmed exists) means empty hands.
        public static int ActiveSlot(Combatant me) => me != null ? me.ActiveSlot.Value : -1;   // [MASTER] same call, may be -1/unarmed

        public static int ActiveWeaponId(Combatant me) => WeaponInSlot(me, ActiveSlot(me));
        public static int ActiveMag(Combatant me) => MagInSlot(me, ActiveSlot(me));

        // "단발" / "연사" for the weapon in `slot`.
        public static string FireModeOf(Combatant me, int slot)
        {
            // [MASTER] return me.FireModeOf(slot) (the player's selected mode) once it exists.
            var weapon = GameData.Weapons.Find(WeaponInSlot(me, slot));
            if (weapon == null) return "";
            return weapon.Auto ? "연사" : "단발";
        }

        // Short name of the optic fitted to the weapon in `slot` ("" = none), e.g. "2x", "레드도트".
        public static string OpticOf(Combatant me, int slot)
        {
            return "";   // [MASTER] CombatantOptics.OpticInSlot(slot) -> AttachmentNames.Of(id) (short form)
        }

        // True while a magnified scope overlay is up: the HUD then hides its crosshair.
        public static bool ScopeActive => false;   // [MASTER] ScopeView.Active

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
            return 0f;   // [MASTER] SpawnProtection.LocalSecondsLeft (elevator-deployment branch)
        }

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
            return null;   // [MASTER] MapImage.Get()
        }
    }
}

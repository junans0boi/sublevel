using System;

namespace Game.Core
{
    // Tuning for the weapon slots and fire modes. Loaded from data.
    [Serializable]
    public class SlotRules
    {
        // Single-shot mode on an automatic weapon fires no faster than this, whatever the weapon's own interval:
        // the server cannot tell clicks from a held button, so it holds the rate to what a finger can do.
        public float SingleModeMinInterval = 0.12f;
    }

    // B cycles a weapon's modes in the order Auto -> Single -> Burst (those it has), starting from the first (ADR-089).
    public enum FireMode { Auto = 0, Single = 1, Burst = 2 }

    // What one combatant carries: five weapon slots (ADR-058), reserve ammo per type, medkits. Owns fire-rate and
    // reload timing. Slots 0 and 1 take primary weapons, slot 2 a sidearm (pistols), slot 3 a melee weapon (no
    // magazine: it never fires or reloads, ADR-073) and slot 4 is the throwable slot (grenades are
    // counted by the throwing component, never held here).
    public class Loadout
    {
        public const int SlotCount = 5;
        public const int PrimarySlotA = 0, PrimarySlotB = 1, SidearmSlot = 2, MeleeSlot = 3, ThrowableSlot = 4;
        // Weapons that may be carried: every slot that takes a weapon.
        public const int WeaponSlotCount = 3;

        readonly WeaponTable _table;
        readonly float _medkitHeal;
        readonly int[] _slotWeapon = new int[SlotCount];
        readonly int[] _slotMag = new int[SlotCount];
        // How many times B was pressed for the weapon in each slot (an index into its modes). Cleared whenever
        // the slot gets another weapon.
        readonly int[] _slotModeStep = new int[SlotCount];
        readonly int[] _reserve = new int[AmmoNames.Count];
        readonly FireCadence _cadence = new FireCadence();
        double _reloadEndTime = -1;
        // ADR-089: the weapon in hand fires from this time on; NaN = it was just taken in hand and the draw starts
        // at the next Tick or fire attempt (callers that change the slot do not pass the time).
        double _readyAt = double.NegativeInfinity;
        int _burstShot;            // shots fired of the current burst
        double _lastShotAt = double.NegativeInfinity;

        public int ActiveSlot { get; private set; }
        public int Medkits { get; private set; }
        public bool IsReloading => _reloadEndTime >= 0;

        public Loadout(WeaponTable table, float medkitHeal = 50f)
        {
            _table = table;
            _medkitHeal = medkitHeal;
        }

        // Does this slot take weapons of this category?
        public static bool SlotTakes(int slot, WeaponCategory category)
        {
            switch (slot)
            {
                case PrimarySlotA:
                case PrimarySlotB: return category == WeaponCategory.Primary;
                case SidearmSlot: return category == WeaponCategory.Sidearm;
                case MeleeSlot: return category == WeaponCategory.Melee; // ADR-073
                default: return false; // throwable: grenades are not weapons
            }
        }

        public static string FireModeName(bool auto) => auto ? "연사" : "단발";
        public static string FireModeName(FireMode mode) => mode == FireMode.Auto ? "연사" : mode == FireMode.Burst ? "점사" : "단발";

        // The modes a weapon has, in B order. Never empty for a weapon (single shots at least).
        static int ModeCount(WeaponDef w) => w == null ? 1 : 1 + (w.Auto ? 1 : 0) + (w.Burst > 1 ? 1 : 0);
        static FireMode ModeAt(WeaponDef w, int step)
        {
            if (w == null) return FireMode.Single;
            int i = step % ModeCount(w);
            if (w.Auto) { if (i == 0) return FireMode.Auto; i--; }
            return i == 0 ? FireMode.Single : FireMode.Burst;
        }

        public FireMode ModeOf(int slot) => slot >= 0 && slot < SlotCount ? ModeAt(_table.Find(_slotWeapon[slot]), _slotModeStep[slot]) : FireMode.Single;

        // The chosen fire modes for the network: bit i = single shots in slot i, bit 8 + i = burst.
        public int ModeBits()
        {
            int bits = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!CanSwitchFireMode(i)) continue; // a weapon with one mode records nothing
                var mode = ModeOf(i);
                if (mode == FireMode.Single) bits |= 1 << i;
                else if (mode == FireMode.Burst) bits |= 1 << (8 + i);
            }
            return bits;
        }

        public static FireMode ModeFromBits(WeaponDef weapon, int bits, int slot)
        {
            if (weapon == null || slot < 0 || slot >= SlotCount) return FireMode.Single;
            if ((bits & (1 << (8 + slot))) != 0 && weapon.Burst > 1) return FireMode.Burst;
            return weapon.Auto && (bits & (1 << slot)) == 0 ? FireMode.Auto : FireMode.Single;
        }

        // Seconds from a shot to the next one the weapon may fire. `burstShot`: the shot's place in its burst
        // (1 = first). Single shots on a weapon that can fire faster wait at least `singleModeInterval`; the last
        // shot of a burst waits the weapon's burst gap.
        public static double IntervalAfter(WeaponDef weapon, FireMode mode, int burstShot, double singleModeInterval)
        {
            switch (mode)
            {
                case FireMode.Auto: return weapon.FireInterval;
                case FireMode.Burst: return burstShot < weapon.Burst ? weapon.FireInterval : Math.Max(weapon.FireInterval, Math.Max(weapon.BurstGapSeconds, singleModeInterval));
                default: return ModeCount(weapon) > 1 ? Math.Max(weapon.FireInterval, singleModeInterval) : weapon.FireInterval;
            }
        }

        public int WeaponInSlot(int slot) => slot >= 0 && slot < SlotCount ? _slotWeapon[slot] : 0;
        public int MagInSlot(int slot) => slot >= 0 && slot < SlotCount ? _slotMag[slot] : 0;
        public int Reserve(AmmoType type) => (int)type >= 0 && (int)type < _reserve.Length ? _reserve[(int)type] : 0;
        public WeaponDef ActiveWeapon => _table.Find(_slotWeapon[ActiveSlot]);
        public bool HasAnyWeapon
        {
            get
            {
                for (int i = 0; i < SlotCount; i++) if (_slotWeapon[i] != 0) return true;
                return false;
            }
        }

        // Some weapon slot (primary or sidearm) is empty.
        public bool HasFreeWeaponSlot
        {
            get
            {
                for (int i = 0; i < WeaponSlotCount; i++) if (_slotWeapon[i] == 0) return true;
                return false;
            }
        }

        // ---- Which slot a weapon may go in ----

        // Is this weapon known and allowed in this slot?
        public bool CanHold(int slot, int weaponId)
        {
            if (slot < 0 || slot >= SlotCount || weaponId == 0) return false;
            var def = _table.Find(weaponId);
            return def != null && SlotTakes(slot, def.Category);
        }

        // Where a picked-up weapon goes: the first free slot that takes it; else the slot in hand when that takes
        // it; else the first slot that takes it. -1 when no slot does.
        public int PickupSlotFor(int weaponId)
        {
            for (int i = 0; i < SlotCount; i++)
                if (_slotWeapon[i] == 0 && CanHold(i, weaponId)) return i;
            if (CanHold(ActiveSlot, weaponId)) return ActiveSlot;
            for (int i = 0; i < SlotCount; i++)
                if (CanHold(i, weaponId)) return i;
            return -1;
        }

        public bool CanAddWeapon(int weaponId) => PickupSlotFor(weaponId) >= 0;

        // Puts the weapon where PickupSlotFor says. Returns the replaced weapon id (0 if none) and what was left in
        // its magazine. Changes nothing when no slot takes the weapon (check CanAddWeapon first to tell that apart
        // from "nothing was replaced").
        public int AddWeapon(int weaponId, int ammoInMag, out int replacedMag)
        {
            replacedMag = 0;
            int slot = PickupSlotFor(weaponId);
            if (slot < 0) return 0;
            return Place(slot, weaponId, ammoInMag, out replacedMag);
        }

        public int AddWeapon(int weaponId, int ammoInMag) => AddWeapon(weaponId, ammoInMag, out _);

        // Puts the weapon in a chosen slot. Returns the weapon that was there (0 if none) and its magazine.
        // Changes nothing (and returns 0) when the slot does not take this weapon: CanHold says so beforehand.
        // The new weapon is taken in hand when the slot was the active one or nothing was held.
        public int AddWeaponToSlot(int slot, int weaponId, int ammoInMag, out int replacedMag)
        {
            replacedMag = 0;
            if (!CanHold(slot, weaponId)) return 0;
            return Place(slot, weaponId, ammoInMag, out replacedMag);
        }

        int Place(int slot, int weaponId, int ammoInMag, out int replacedMag)
        {
            int replaced = _slotWeapon[slot];
            replacedMag = _slotMag[slot];
            _slotWeapon[slot] = weaponId;
            _slotMag[slot] = ammoInMag;
            _slotModeStep[slot] = 0;
            if (slot == ActiveSlot) _reloadEndTime = -1;
            if (ActiveWeapon == null) ActiveSlot = slot;
            return replaced;
        }

        // The two primary weapons trade places; the weapon in hand stays in hand.
        public bool SwapSlots() => SwapSlots(PrimarySlotA, PrimarySlotB);

        // Two slots trade contents when each can take what the other holds (an empty slot takes anything it could
        // hold anyway); the weapon in hand stays in hand (the active slot follows it).
        public bool SwapSlots(int a, int b)
        {
            if (a < 0 || b < 0 || a >= SlotCount || b >= SlotCount || a == b) return false;
            if (_slotWeapon[a] == 0 && _slotWeapon[b] == 0) return false;
            if (_slotWeapon[b] != 0 && !CanHold(a, _slotWeapon[b])) return false;
            if (_slotWeapon[a] != 0 && !CanHold(b, _slotWeapon[a])) return false;
            int weapon = _slotWeapon[a]; _slotWeapon[a] = _slotWeapon[b]; _slotWeapon[b] = weapon;
            int mag = _slotMag[a]; _slotMag[a] = _slotMag[b]; _slotMag[b] = mag;
            int step = _slotModeStep[a]; _slotModeStep[a] = _slotModeStep[b]; _slotModeStep[b] = step;
            if (ActiveSlot == a) ActiveSlot = b;
            else if (ActiveSlot == b) ActiveSlot = a;
            return true;
        }

        public void AddAmmo(AmmoType type, int amount)
        {
            if ((int)type >= 0 && (int)type < _reserve.Length) _reserve[(int)type] += amount;
        }
        public void AddMedkits(int amount) => Medkits += amount;

        // Switching slots cancels a reload in progress. Any of the five slots can be taken in hand; the melee and
        // throwable slots hold no weapon, so selecting one leaves the hands empty.
        public bool SelectSlot(int slot)
        {
            if (slot < 0 || slot >= SlotCount || slot == ActiveSlot) return false;
            ActiveSlot = slot;
            _reloadEndTime = -1;
            StartDraw();
            return true;
        }

        public void CancelReload() => _reloadEndTime = -1;

        // ADR-089: the weapon in hand was just taken out (slot change, put-away weapon drawn): it may fire once
        // its DrawSeconds have passed, counted from the next Tick or fire attempt.
        public void StartDraw() => _readyAt = double.NaN;

        // Still taking the weapon in hand at `now`.
        public bool IsDrawing(double now) => double.IsNaN(_readyAt) || now < _readyAt;

        void ResolveDraw(double now)
        {
            if (!double.IsNaN(_readyAt)) return;
            var w = ActiveWeapon;
            _readyAt = now + (w != null ? w.DrawSeconds : 0f);
        }

        // ---- Fire mode ----

        // Only weapons with more than one mode (automatic and/or burst) have a choice.
        public bool CanSwitchFireMode(int slot) => ModeCount(_table.Find(WeaponInSlot(slot))) > 1;

        // Does the weapon in this slot fire while the button is held?
        public bool IsAutoFire(int slot) => ModeOf(slot) == FireMode.Auto;

        // B: the next of the weapon's modes (Auto -> Single -> Burst). False for a weapon with one mode.
        public bool ToggleFireMode(int slot)
        {
            if (!CanSwitchFireMode(slot)) return false;
            _slotModeStep[slot] = (_slotModeStep[slot] + 1) % ModeCount(_table.Find(WeaponInSlot(slot)));
            return true;
        }

        // `earlySeconds`: how far ahead of the weapon's schedule a shot is still accepted. The schedule itself does
        // not move for an early shot, so the average rate never exceeds the weapon's. `singleModeInterval`: an
        // automatic weapon set to single shots waits at least this long between shots.
        public bool TryFire(double now, double earlySeconds = 0, double singleModeInterval = 0)
        {
            var w = ActiveWeapon;
            if (w == null || _slotMag[ActiveSlot] <= 0) return false;
            ResolveDraw(now);
            if (IsDrawing(now + earlySeconds)) return false;
            // A shell-by-shell reload stops for a shot when there is a round to fire (ADR-089).
            if (IsReloading && !(w.ReloadsByShell && _cadence.Ready(now, earlySeconds))) return false;
            var mode = ModeOf(ActiveSlot);
            // A burst the shooter stopped (or one that ended) starts over after a pause longer than the rate allows.
            int burstShot = mode == FireMode.Burst && _burstShot < w.Burst && now - _lastShotAt <= w.FireInterval * 2.0 + earlySeconds ? _burstShot + 1 : 1;
            if (!_cadence.TryFire(now, IntervalAfter(w, mode, burstShot, singleModeInterval), earlySeconds)) return false;
            _burstShot = burstShot;
            _lastShotAt = now;
            _reloadEndTime = -1;
            _slotMag[ActiveSlot]--;
            return true;
        }

        // The time between shots in this slot's current mode (the first shot of a burst).
        public double EffectiveInterval(WeaponDef weapon, int slot, double singleModeInterval) =>
            IntervalAfter(weapon, ModeOf(slot), 1, singleModeInterval);

        public bool TryStartReload(double now)
        {
            var w = ActiveWeapon;
            if (w == null || IsReloading || _slotMag[ActiveSlot] >= w.MagSize || _reserve[(int)w.Ammo] <= 0) return false;
            // Empty takes longer than a tactical reload; a shell-by-shell weapon loads its first round after this.
            _reloadEndTime = now + w.ReloadSeconds(_slotMag[ActiveSlot]);
            return true;
        }

        // Completes a reload whose time has come. Returns true when state changed.
        public bool Tick(double now)
        {
            ResolveDraw(now);
            if (!IsReloading || now < _reloadEndTime) return false;
            var w = ActiveWeapon;
            if (w != null && w.ReloadsByShell)
            {
                // One round per ShellReloadSeconds until full or out of rounds (several when a tick came late).
                while (IsReloading && now >= _reloadEndTime)
                {
                    int ammo = (int)w.Ammo;
                    if (_slotMag[ActiveSlot] < w.MagSize && _reserve[ammo] > 0) { _slotMag[ActiveSlot]++; _reserve[ammo]--; }
                    bool more = _slotMag[ActiveSlot] < w.MagSize && _reserve[ammo] > 0;
                    _reloadEndTime = more ? _reloadEndTime + w.ShellReloadSeconds : -1;
                }
                return true;
            }
            _reloadEndTime = -1;
            if (w == null) return true;
            int need = w.MagSize - _slotMag[ActiveSlot];
            int take = need < _reserve[(int)w.Ammo] ? need : _reserve[(int)w.Ammo];
            _slotMag[ActiveSlot] += take;
            _reserve[(int)w.Ammo] -= take;
            return true;
        }

        public bool TryUseMedkit(Health health)
        {
            if (Medkits <= 0 || health.IsDead || health.Current >= health.Max) return false;
            Medkits--;
            health.Heal(_medkitHeal);
            return true;
        }

        // ---- Dropping (inventory screen) ----

        // Empties the slot and returns the weapon that was in it (0 if none or no such slot) with its magazine.
        // Dropping the weapon in hand cancels its reload and switches to another slot that holds a weapon.
        public int RemoveWeapon(int slot, out int mag)
        {
            mag = 0;
            if (slot < 0 || slot >= SlotCount || _slotWeapon[slot] == 0) return 0;
            int removed = _slotWeapon[slot];
            mag = _slotMag[slot];
            _slotWeapon[slot] = 0;
            _slotMag[slot] = 0;
            _slotModeStep[slot] = 0;
            if (slot == ActiveSlot)
            {
                _reloadEndTime = -1;
                for (int i = 0; i < SlotCount; i++)
                    if (_slotWeapon[i] != 0) { ActiveSlot = i; StartDraw(); break; }
            }
            return removed;
        }

        // ADR-081: a weapon goes on the ground unloaded: the weapon with an empty magazine, then (when it held any)
        // its rounds as a separate ammo item of its calibre. Melee weapons and unknown ids carry no rounds.
        public static LootEntry[] DropEntries(WeaponTable table, int weaponId, int mag)
        {
            if (weaponId == 0) return new LootEntry[0];
            var weapon = new LootEntry { Kind = LootKind.Weapon, WeaponId = weaponId, Amount = 0 };
            var def = table?.Find(weaponId);
            if (def == null || def.IsMelee || mag <= 0) return new[] { weapon };
            return new[] { weapon, new LootEntry { Kind = LootKind.Ammo, Ammo = def.Ammo, Amount = mag } };
        }

        // Takes the whole reserve of one ammo type. Returns how many rounds that was.
        public int RemoveAmmo(AmmoType type)
        {
            int index = (int)type;
            if (index < 0 || index >= _reserve.Length) return 0;
            int amount = _reserve[index];
            _reserve[index] = 0;
            return amount;
        }

        public bool RemoveMedkit()
        {
            if (Medkits <= 0) return false;
            Medkits--;
            return true;
        }
    }
}

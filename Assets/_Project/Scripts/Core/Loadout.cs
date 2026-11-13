namespace Game.Core
{
    // What one combatant carries: two weapon slots, reserve ammo per type, medkits. Owns fire-rate and reload timing.
    public class Loadout
    {
        public const int SlotCount = 2;

        readonly WeaponTable _table;
        readonly float _medkitHeal;
        readonly int[] _slotWeapon = new int[SlotCount];
        readonly int[] _slotMag = new int[SlotCount];
        readonly int[] _reserve = new int[AmmoNames.Count];
        readonly FireCadence _cadence = new FireCadence();
        double _reloadEndTime = -1;

        public int ActiveSlot { get; private set; }
        public int Medkits { get; private set; }
        public bool IsReloading => _reloadEndTime >= 0;

        public Loadout(WeaponTable table, float medkitHeal = 50f)
        {
            _table = table;
            _medkitHeal = medkitHeal;
        }

        public int WeaponInSlot(int slot) => _slotWeapon[slot];
        public int MagInSlot(int slot) => _slotMag[slot];
        public int Reserve(AmmoType type) => (int)type >= 0 && (int)type < _reserve.Length ? _reserve[(int)type] : 0;
        public WeaponDef ActiveWeapon => _table.Find(_slotWeapon[ActiveSlot]);

        // Puts the weapon in the first empty slot, or replaces the active one.
        // Returns the replaced weapon id (0 if none) and what was left in its magazine.
        public int AddWeapon(int weaponId, int ammoInMag, out int replacedMag)
        {
            replacedMag = 0;
            int slot = -1;
            for (int i = 0; i < SlotCount; i++)
                if (_slotWeapon[i] == 0) { slot = i; break; }
            int replaced = 0;
            if (slot < 0)
            {
                slot = ActiveSlot;
                replaced = _slotWeapon[slot];
                replacedMag = _slotMag[slot];
                _reloadEndTime = -1;
            }
            _slotWeapon[slot] = weaponId;
            _slotMag[slot] = ammoInMag;
            if (ActiveWeapon == null) ActiveSlot = slot;
            return replaced;
        }

        public int AddWeapon(int weaponId, int ammoInMag) => AddWeapon(weaponId, ammoInMag, out _);

        // Puts the weapon in a chosen slot. Returns the weapon that was there (0 if none) and its magazine.
        // The new weapon is taken in hand when the slot was the active one or nothing was held.
        public int AddWeaponToSlot(int slot, int weaponId, int ammoInMag, out int replacedMag)
        {
            replacedMag = 0;
            if (slot < 0 || slot >= SlotCount || weaponId == 0) return 0;
            int replaced = _slotWeapon[slot];
            replacedMag = _slotMag[slot];
            _slotWeapon[slot] = weaponId;
            _slotMag[slot] = ammoInMag;
            if (slot == ActiveSlot) _reloadEndTime = -1;
            if (ActiveWeapon == null) ActiveSlot = slot;
            return replaced;
        }

        // Trades the two slots; the weapon in hand stays in hand (the active slot follows it).
        public bool SwapSlots()
        {
            if (_slotWeapon[0] == 0 && _slotWeapon[1] == 0) return false;
            int weapon = _slotWeapon[0]; _slotWeapon[0] = _slotWeapon[1]; _slotWeapon[1] = weapon;
            int mag = _slotMag[0]; _slotMag[0] = _slotMag[1]; _slotMag[1] = mag;
            ActiveSlot = 1 - ActiveSlot;
            return true;
        }

        public void AddAmmo(AmmoType type, int amount)
        {
            if ((int)type >= 0 && (int)type < _reserve.Length) _reserve[(int)type] += amount;
        }
        public void AddMedkits(int amount) => Medkits += amount;

        // Switching slots cancels a reload in progress.
        public bool SelectSlot(int slot)
        {
            if (slot < 0 || slot >= SlotCount || slot == ActiveSlot) return false;
            ActiveSlot = slot;
            _reloadEndTime = -1;
            return true;
        }

        // `earlySeconds`: how far ahead of the weapon's schedule a shot is still accepted. The schedule itself does
        // not move for an early shot, so the average rate never exceeds the weapon's.
        public bool TryFire(double now, double earlySeconds = 0)
        {
            var w = ActiveWeapon;
            if (w == null || IsReloading || _slotMag[ActiveSlot] <= 0) return false;
            if (!_cadence.TryFire(now, w.FireInterval, earlySeconds)) return false;
            _slotMag[ActiveSlot]--;
            return true;
        }

        public bool TryStartReload(double now)
        {
            var w = ActiveWeapon;
            if (w == null || IsReloading || _slotMag[ActiveSlot] >= w.MagSize || _reserve[(int)w.Ammo] <= 0) return false;
            _reloadEndTime = now + w.ReloadTime;
            return true;
        }

        // Completes a reload whose time has come. Returns true when state changed.
        public bool Tick(double now)
        {
            if (!IsReloading || now < _reloadEndTime) return false;
            var w = ActiveWeapon;
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
        // Dropping the weapon in hand cancels its reload and switches to the other slot when that holds a weapon.
        public int RemoveWeapon(int slot, out int mag)
        {
            mag = 0;
            if (slot < 0 || slot >= SlotCount || _slotWeapon[slot] == 0) return 0;
            int removed = _slotWeapon[slot];
            mag = _slotMag[slot];
            _slotWeapon[slot] = 0;
            _slotMag[slot] = 0;
            if (slot == ActiveSlot)
            {
                _reloadEndTime = -1;
                for (int i = 0; i < SlotCount; i++)
                    if (_slotWeapon[i] != 0) { ActiveSlot = i; break; }
            }
            return removed;
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

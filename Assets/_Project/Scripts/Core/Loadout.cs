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
        readonly int[] _reserve = new int[3];
        double _nextFireTime;
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
        public int Reserve(AmmoType type) => _reserve[(int)type];
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

        public void AddAmmo(AmmoType type, int amount) => _reserve[(int)type] += amount;
        public void AddMedkits(int amount) => Medkits += amount;

        // Switching slots cancels a reload in progress.
        public bool SelectSlot(int slot)
        {
            if (slot < 0 || slot >= SlotCount || slot == ActiveSlot) return false;
            ActiveSlot = slot;
            _reloadEndTime = -1;
            return true;
        }

        public bool TryFire(double now)
        {
            var w = ActiveWeapon;
            if (w == null || IsReloading || now < _nextFireTime || _slotMag[ActiveSlot] <= 0) return false;
            _slotMag[ActiveSlot]--;
            _nextFireTime = now + w.FireInterval;
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
    }
}

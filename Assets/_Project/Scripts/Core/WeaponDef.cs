using System;

namespace Game.Core
{
    public enum AmmoType { Light = 0, Medium = 1, Shell = 2 }

    // One weapon's tuning values. Loaded from data (ADR-032); Id 0 means "no weapon".
    [Serializable]
    public class WeaponDef
    {
        public int Id;
        public string Name;
        public AmmoType Ammo;
        public bool Auto;
        public float Damage;
        public float FireInterval;
        public int MagSize;
        public float ReloadTime;
        public float SpreadDeg;
        public int Pellets = 1;
        public float Range = 100f;
        public float FalloffStart;
        public float FalloffEnd;
        public float MinDamageFactor = 1f;
        public float RecoilPitchDeg;  // view kick per shot, before the aim stance's factor
        public float RecoilYawDeg;    // random left/right kick, up to this much
    }

    [Serializable]
    public class WeaponTable
    {
        public WeaponDef[] Weapons = new WeaponDef[0];

        public WeaponDef Find(int id)
        {
            foreach (var w in Weapons)
                if (w.Id == id) return w;
            return null;
        }
    }
}

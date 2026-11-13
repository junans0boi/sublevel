using System;

namespace Game.Core
{
    // Four calibres (ADR-054). The numbers are stored in data and sent over the network: never reorder.
    public enum AmmoType { Pistol9 = 0, Rifle556 = 1, Shotgun12 = 2, Rifle762 = 3 }

    public static class AmmoNames
    {
        public const int Count = 4;
        static readonly string[] Names = { "9mm", "5.56mm", "12게이지", "7.62mm" };

        // The one place that names a calibre for the player.
        public static string Of(AmmoType type) => Of((int)type);
        public static string Of(int type) => type >= 0 && type < Names.Length ? Names[type] : "탄약";
        public static bool IsValid(AmmoType type) => (int)type >= 0 && (int)type < Count;
    }

    // Which slots a weapon may sit in (ADR-058). Never reorder: stored in data. Melee weapons go in slot 4 (ADR-073).
    public enum WeaponCategory { Primary = 0, Sidearm = 1, Melee = 2 }

    // How a melee weapon is swung (ADR-073): a sideways slash or an overhead chop. Picks the swing pose only.
    public enum MeleeSwingStyle { Slash = 0, Chop = 1 }

    // One weapon's tuning values. Loaded from data (ADR-032); Id 0 means "no weapon".
    [Serializable]
    public class WeaponDef
    {
        public int Id;
        public string Name;
        public AmmoType Ammo;
        public WeaponCategory Category;   // Primary (slots 1-2) or Sidearm (slot 3, pistols)
        public bool Auto;                 // can fire in bursts when the shooter picks automatic (semi-auto weapons never)
        public float Damage;
        public float FireInterval;
        public int MagSize;
        public float ReloadTime;
        public float SpreadDeg;
        public int Pellets = 1;
        public float Range = 100f;
        public float MuzzleSpeed;         // m/s (ADR-098); 0 = BallisticsRules.DefaultMuzzleSpeed
        public float FalloffStart;
        public float FalloffEnd;
        public float MinDamageFactor = 1f;
        public float RecoilPitchDeg;  // view kick per shot, before the aim stance's factor
        public float RecoilYawDeg;    // random left/right kick, up to this much
        // Recoil pattern (ADR-089): the side kick leans this way (-1 left .. 1 right, share of RecoilYawDeg), the
        // side drift of one spray stops at RecoilYawMaxDeg (0 = no limit), and the view settles back at
        // RecoilRecoverDegPerSecond (0 = RecoilRules.RecoverDegPerSecond).
        public float RecoilYawBias;
        public float RecoilYawMaxDeg;
        public float RecoilRecoverDegPerSecond;
        // Burst mode (ADR-089): rounds per burst (0 or 1 = no burst mode) and the pause after a burst.
        public int Burst;
        public float BurstGapSeconds;
        // Seconds from taking the weapon in hand to the first shot it may fire (ADR-089). 0 = at once.
        public float DrawSeconds;
        // Reload with rounds still in the magazine is ReloadTime; from empty it is EmptyReloadTime (0 = ReloadTime).
        // ShellReloadSeconds > 0: loads one round per that many seconds instead, and a shot stops it (ADR-089).
        public float EmptyReloadTime;
        public float ShellReloadSeconds;
        // The aim and stance factors never narrow the cone below this share of SpreadDeg (shotgun pellets).
        public float MinSpreadFactor;
        // Damage multipliers by where the hit lands (ADR-053).
        public float HeadFactor = 2f;
        public float BodyFactor = 1f;
        public float LimbFactor = 0.75f;
        // Spread multipliers for a shooter moving at full walking speed or more, and for one off the ground.
        public float MoveSpreadFactor = 1.5f;
        public float AirSpreadFactor = 2.5f;
        // Melee weapons (ADR-073) use Damage, Range (reach from the eyes), FireInterval (seconds between swings) and
        // HeadFactor from above, plus these two. They have no magazine, calibre or spread.
        public float ConeDeg = 60f;       // full opening angle of the swing's cone
        public MeleeSwingStyle Swing;

        public bool IsMelee => Category == WeaponCategory.Melee;
        public bool ReloadsByShell => ShellReloadSeconds > 0f;
        // Reload seconds from a magazine holding `mag` rounds (the whole reload, or one round for shell loading).
        public float ReloadSeconds(int mag) => ReloadsByShell ? ShellReloadSeconds : mag <= 0 && EmptyReloadTime > 0f ? EmptyReloadTime : ReloadTime;
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

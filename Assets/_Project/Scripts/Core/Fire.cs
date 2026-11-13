using System;

namespace Game.Core
{
    // What one projectile ended on. Sent with every shot so clients can show impacts and hit confirmation.
    // Where on a combatant it landed travels next to it as a HitZone (Combatant.ShotResult.Zone), so a headshot
    // is Combatant or Kill with Zone == Head. Values stay below 16: the zone shares the byte on the wire.
    public enum ShotHit : byte { None = 0, World = 1, Combatant = 2, Kill = 3 }

    // How strictly fire timing is kept on each side.
    [Serializable]
    public class FireRules
    {
        // The server accepts a shot that arrives this much before the weapon is ready (frame and network jitter)...
        public float ServerEarlySeconds = 0.05f;
        // ...but never more than this share of the weapon's fire interval.
        public float MaxEarlyFraction = 0.4f;
        // A frame that lands this late still counts as fired on time, so the rhythm does not follow frame times.
        public float ClientCatchUpSeconds = 0.035f;
        // A click on a semi-automatic weapon that comes this much too soon fires as soon as the weapon is ready.
        public float ClickBufferSeconds = 0.12f;
        // Movement spread (DamageRules.MovementSpread): none up to the first planar speed (m/s), full from the second.
        public float MoveSpreadMinSpeed = 0.5f;
        public float MoveSpreadFullSpeed = 4.5f;
        // Head / body / limb height bands (ADR-053).
        public HitZoneRules Zones = new HitZoneRules();

        public double ServerEarly(double interval) => Math.Min(ServerEarlySeconds, interval * MaxEarlyFraction);

        // Never more than the server tolerates, or the server would refuse the caught-up shot.
        public double ClientCatchUp(double interval, double frameSeconds) =>
            Math.Min(frameSeconds, Math.Min(ClientCatchUpSeconds, ServerEarly(interval)));
    }

    // Fire-rate timing against an ideal schedule instead of "interval after whenever the last shot happened".
    // Shots are a fixed interval apart on the schedule; a shot may happen a little early (`early`, used by the
    // server for requests that arrive ahead of time) or late (`late`, used by the client whose frames never land
    // exactly on time) without moving it. Any n shots still take at least (n - 1) * interval - early - late.
    public class FireCadence
    {
        double _next = double.NegativeInfinity;

        public bool Ready(double now, double early = 0) => now >= _next - early;

        public void Fired(double now, double interval, double late = 0) => _next = Math.Max(_next, now - late) + interval;

        public bool TryFire(double now, double interval, double early = 0, double late = 0)
        {
            if (!Ready(now, early)) return false;
            Fired(now, interval, late);
            return true;
        }
    }

    // How shots and hits look and sound. Sizes in metres, times in seconds.
    [Serializable]
    public class CombatFxRules
    {
        // Muzzle: in front of and below the eyes, where the weapon is held.
        public float MuzzleForward = 0.6f;
        public float MuzzleDrop = 0.22f;
        // Indexed by weapon id (0 = none).
        public float[] MuzzleFlashSize = { 0f, 0.28f, 0.32f, 0.4f, 0.6f };
        public float MuzzleFlashSeconds = 0.05f;
        public float EyeViewFlashFactor = 0.45f; // the flash is right in front of the camera there
        public float MuzzleLightIntensity = 6f;  // at flash size 1
        public float MuzzleLightRange = 7f;

        public float TracerSpeed = 250f;
        public float TracerLength = 4f;
        public float TracerMinSeconds = 0.06f;    // short shots slow the streak down so it is on screen at least this long
        public float TracerWidth = 0.035f;
        public float TracerWidthPerMeter = 0.0015f; // grows with distance from the camera so it never gets thinner than a pixel or two

        public float ImpactFlashSize = 0.3f;
        public float ImpactFlashSeconds = 0.07f;
        public float DustSize = 0.35f;
        public float DustGrowth = 2.2f;           // size multiplier reached at the end of its life
        public float DustSeconds = 0.45f;
        public float DustDriftSpeed = 0.5f;       // away from the surface

        public float HitConfirmVolume = 0.5f;
        public float HitConfirmPitch = 1.7f;
        public float KillConfirmVolume = 0.8f;
        public float KillConfirmPitch = 1.1f;
        public float HeadConfirmPitch = 2.2f;     // a hit on the head that does not kill sounds sharper
    }
}

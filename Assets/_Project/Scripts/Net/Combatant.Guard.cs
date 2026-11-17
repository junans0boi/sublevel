using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server-side limits on what this combatant's owner asks for (#170, ADR-142). Refusals are counted in ServerGuard.
    public partial class Combatant
    {
        RequestBucket _fireRequests, _pingRequests, _emoteRequests;

        // A rate-limited request kind (ping, emote): more than the limit's interval allows, with a margin, is a flood.
        bool NotFlooding(ref RequestBucket bucket, float minInterval, string what)
        {
            var g = GameData.Rules.Guard;
            return bucket.TryTake(Time.timeAsDouble, g.FireRateMargin / minInterval, g.FireBurst) || ServerGuard.Refuse(GuardKind.Rate, this, what + " requests too often");
        }

        // How far a pickup may be: the reach plus what the server's late copy of the body can be off by.
        public static float ServerPickupRange => InteractRules.Current.ReachMeters + GameData.Rules.Guard.LagSlackMeters;

        // A fire or punch request: a finite direction, and no faster than the weapon (plus a margin) can shoot.
        bool FireRequestOk(Vector3 direction)
        {
            if (!InputGuard.Finite(direction.x, direction.y, direction.z)) return ServerGuard.Refuse(GuardKind.Arg, this, "fire direction is not a number");
            var g = GameData.Rules.Guard;
            var w = _loadout != null ? _loadout.ActiveWeapon : null;
            double interval = w == null || w.IsMelee ? 0.0 : w.FireInterval;
            if (_fireRequests.TryTake(Time.timeAsDouble, InputGuard.FireRequestsPerSecond(interval, g), g.FireBurst)) return true;
            return ServerGuard.Refuse(GuardKind.Fire, this, "fire requests faster than the weapon can shoot");
        }

        bool SlotOk(int slot, string what) =>
            InputGuard.ValidSlot(slot, Loadout.SlotCount) || ServerGuard.Refuse(GuardKind.Slot, this, $"{what}: slot {slot}");

        // The pickup's distance, storey and line of sight from this body's eyes (ADR-276; the late copy of the body gets the lag slack).
        bool PickupInReach(LootItem item, string what)
        {
            var g = GameData.Rules.Guard;
            if (PickupSight.Reachable(transform.position, EyePosition, item.transform, transform, g.LagSlackMeters)) return true;
            return ServerGuard.Refuse(GuardKind.Distance, this, $"{what}: item {(item.transform.position - transform.position).magnitude:F1} m away, out of reach or sight");
        }
    }
}

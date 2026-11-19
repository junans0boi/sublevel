using System;

namespace Game.Core
{
    // What kind of networked thing a distance rule is deciding about (#167, ADR-135).
    public enum InterestKind { Always, Combatant, Loot, Vehicle, Thrown }

    // Interest management: a client is sent a combatant, loot item, vehicle or thrown item (and so its sound and effect
    // events, which ride on the object) only while it is within a radius of the client's own body. Data: rules.json "Interest".
    [Serializable]
    public class InterestRules
    {
        // Set from data when there is any; tests may replace it.
        public static InterestRules Current = new InterestRules();

        public bool Enabled = true;
        // Gunshots are heard to 200 m (SoundRules.FarLayerFull) and scoped targets are seen further: combatants carry both.
        public float CombatantMeters = 300f;
        public float VehicleMeters = 700f; // ADR-480
        public float LootMeters = 100f;
        // Thrown items carry the smoke cloud, the fire and the blast (heard to about 200 m).
        public float ThrownMeters = 200f;
        // Hysteresis: an object already seen stays until the distance passes radius * (1 + this).
        public float HideExtraFraction = 0.2f;

        public float Radius(InterestKind kind)
        {
            switch (kind)
            {
                case InterestKind.Combatant: return CombatantMeters;
                case InterestKind.Vehicle: return VehicleMeters;
                case InterestKind.Loot: return LootMeters;
                case InterestKind.Thrown: return ThrownMeters;
                default: return float.PositiveInfinity;
            }
        }

        public float HideRadius(InterestKind kind) => Radius(kind) * (1f + Math.Max(0f, HideExtraFraction));
    }

    // One client as the server sees it: its own body and what that body is doing.
    public struct InterestObserver
    {
        // False until the client's player exists: it sees nothing then (the next timed check catches up within ~0.5 s).
        public bool HasBody;
        // Distance to the nearest object the client owns (its body, a vehicle it drives).
        public float NearestMeters;
        // Dead: the client spectates, and the server does not know whom, so it keeps every combatant, vehicle and thrown item.
        public bool Dead;
    }

    public static class Interest
    {
        // The distance rule alone, with hysteresis: `seen` = the client has the object now.
        public static bool Within(InterestRules rules, InterestKind kind, bool seen, float meters) =>
            meters <= (seen ? rules.HideRadius(kind) : rules.Radius(kind));

        // Does the client get the object? `teamMember`: the object is (or carries) a member of the client's team, which
        // is always seen.
        public static bool Observes(InterestRules rules, InterestKind kind, bool seen, in InterestObserver observer, bool teamMember)
        {
            if (!rules.Enabled || kind == InterestKind.Always) return true;
            if (!observer.HasBody) return false;
            if (teamMember) return true;
            if (observer.Dead && kind != InterestKind.Loot) return true;
            return Within(rules, kind, seen, observer.NearestMeters);
        }
    }
}

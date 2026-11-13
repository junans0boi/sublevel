using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Swimming and the breath (ADR-112). Every peer reads the body's position against the water (positions are
    // replicated), so each knows who swims without a message; the server also keeps the breath of a swimmer whose head
    // is under and hurts one who has none left. A plain component that Combatant adds to itself: no prefab changes.
    public class CombatantSwim : MonoBehaviour
    {
        readonly SwimBreath _breath = new SwimBreath();
        Combatant _combatant;
        bool _swimming;

        // In water deep enough to swim (shooting and throwing refuse).
        public bool InWater => _swimming;
        // 1 = full breath; this peer's own reckoning (the owner's HUD, the server's damage).
        public float Breath => _breath.Fraction;
        // The head is under water right now.
        public bool HeadUnder { get; private set; }
        // Server: the total drowning damage dealt (tests).
        public float DrownDamage { get; private set; }

        public static bool In(Combatant c)
        {
            if (c == null) return false;
            var swim = c.GetComponent<CombatantSwim>();
            return swim != null && swim.InWater;
        }

        void Awake() => _combatant = GetComponent<Combatant>();

        void Update()
        {
            if (_combatant == null) return;
            var rules = SwimRules.Current;
            if (_combatant.Dead.Value)
            {
                _swimming = HeadUnder = false;
                return;
            }
            float depth = WaterVolume.DepthAt(transform.position, out float surface, out _);
            _swimming = Swim.Swimming(_swimming, depth, rules);
            HeadUnder = _swimming && Swim.HeadUnder(_combatant.EyePosition.y, surface, rules);
            float damage = _breath.Tick(Time.deltaTime, HeadUnder, rules);
            if (damage > 0f && _combatant.IsServerInitialized)
            {
                DrownDamage += damage;
                _combatant.ServerTakeDamage(new DamageInfo { Amount = damage, Cause = DamageCause.Drown, Zone = HitZone.Body });
            }
        }
    }
}

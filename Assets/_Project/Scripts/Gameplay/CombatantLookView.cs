using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Applies the replicated look (CombatantLook) to the character model: the colour variant, on every peer.
    // The model itself is the prefab the server spawned (MatchDirector picks Player_<ModelId>).
    public class CombatantLookView : MonoBehaviour
    {
        CombatantLook _look;
        CharacterDef _def;

        // What was applied last (tests): the model id of the prefab and the variant index used.
        public string ModelId => _def != null ? _def.Id : "";
        public int VariantApplied { get; private set; } = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => MatchDirector.LocalLookSource = () => PlayerAppearanceStore.Load().Serialize();

        void Awake()
        {
            _look = GetComponent<CombatantLook>();
            _def = GetComponentInChildren<CharacterDef>(true);
            if (_look != null) _look.Look.OnChange += OnChanged;
        }

        void OnDestroy() { if (_look != null) _look.Look.OnChange -= OnChanged; }

        void Start() => Apply(_look != null ? _look.Look.Value : "");

        void OnChanged(string previous, string next, bool asServer) => Apply(next);

        void Apply(string text)
        {
            if (_def == null || _def.Variants == null || _def.Variants.Length == 0 || string.IsNullOrEmpty(text)) return;
            var look = PlayerAppearance.Parse(text);
            _def.ApplyVariant(look.Variant);
            VariantApplied = look.Variant % _def.Variants.Length;
        }
    }
}

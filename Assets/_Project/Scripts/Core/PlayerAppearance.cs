using System;

namespace Game.Core
{
    // What the player chose to look like (ADR-247): a roster model, one of its colour variants and the gear shown on it.
    // Pure data with a text form for storage and for the party sync. The lobby stage and the match spawn both read the
    // same value, so the character in the lobby is the character in the game.
    public struct PlayerAppearance : IEquatable<PlayerAppearance>
    {
        // The 15 roster models (Roster_<Id>.prefab), in screen order. Ids are the prefab names without "Roster_".
        public static readonly string[] ModelIds =
        {
            "MixamoSwat", "MixamoSteve", "MixamoSwatGuy", "MixamoGasMask", "MixamoBrian", "MixamoLouise", "MixamoJames", "MixamoMegan",
            "MixamoKate", "MixamoJosh", "MixamoAdam", "MixamoMartha", "MixamoAlex", "UbcMale", "UbcFemale",
        };

        // Korean names for the 외형 screen (same order as ModelIds).
        public static readonly string[] ModelNames =
        {
            "특수부대원", "철모 군복", "위장 전술복", "방독면", "브라이언", "루이즈", "제임스", "메건",
            "케이트", "조쉬", "아담", "마사", "알렉스", "작업복 남성", "작업복 여성",
        };

        public const string DefaultModelId = "MixamoSwat";

        public string ModelId;     // one of ModelIds; anything else reads as the default model
        public int Variant;        // colour variant of the model (CharacterDef.Variants index; clamped by the user of it)
        public int Helmet, Vest, Pack; // worn gear tier shown on the character (0 = none)

        public static PlayerAppearance Default => new PlayerAppearance { ModelId = DefaultModelId, Variant = 0 };

        public int ModelIndex
        {
            get
            {
                int i = Array.IndexOf(ModelIds, ModelId);
                return i < 0 ? 0 : i;
            }
        }

        // Always valid: a known model, a non-negative variant, gear tiers 0..3.
        public PlayerAppearance Normalized() => new PlayerAppearance
        {
            ModelId = ModelIds[ModelIndex],
            Variant = Math.Max(0, Variant),
            Helmet = Math.Min(3, Math.Max(0, Helmet)),
            Vest = Math.Min(3, Math.Max(0, Vest)),
            Pack = Math.Min(3, Math.Max(0, Pack)),
        };

        public static PlayerAppearance FromIndex(int modelIndex, int variant = 0) =>
            new PlayerAppearance { ModelId = ModelIds[Math.Min(ModelIds.Length - 1, Math.Max(0, modelIndex))], Variant = variant }.Normalized();

        // "MixamoSwat:3:0:0:0" — model:variant:helmet:vest:pack.
        public string Serialize()
        {
            var n = Normalized();
            return n.ModelId + ":" + n.Variant + ":" + n.Helmet + ":" + n.Vest + ":" + n.Pack;
        }

        // Anything unreadable gives the default; parts that are missing or wrong fall back to 0 / the default model.
        public static PlayerAppearance Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Default;
            var p = text.Trim().Split(':');
            var a = new PlayerAppearance { ModelId = p[0] };
            int Get(int i) => i < p.Length && int.TryParse(p[i], out var v) ? v : 0;
            a.Variant = Get(1); a.Helmet = Get(2); a.Vest = Get(3); a.Pack = Get(4);
            return a.Normalized();
        }

        public bool Equals(PlayerAppearance o)
        {
            var a = Normalized(); var b = o.Normalized();
            return a.ModelId == b.ModelId && a.Variant == b.Variant && a.Helmet == b.Helmet && a.Vest == b.Vest && a.Pack == b.Pack;
        }

        public override bool Equals(object obj) => obj is PlayerAppearance o && Equals(o);
        public override int GetHashCode() => Serialize().GetHashCode();
        public override string ToString() => Serialize();
    }
}

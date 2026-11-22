namespace Game.Core
{
    public enum HazardWarn : byte { None = 0, Warning = 1, Imminent = 2, Sealed = 3 }
    public enum HudChip : byte { None = 0, Held = 1, Used = 2 }

    // What the HUD's hazard slot shows (ADR-246): filled by the pollution code, read by the UI. Plain data, no core dependency.
    public struct HudHazardView : System.IEquatable<HudHazardView>
    {
        public HazardWarn Warn;
        public int SecondsToClose; // for Warning/Imminent; <0 = unknown
        public string Region;      // "" = none
        public int Keys;
        public HudChip Chip;

        // One line, never colour only: "[경고] 해안 구역 · 봉쇄까지 01:05".
        public string Line()
        {
            string tag = Warn == HazardWarn.Warning ? "경고" : Warn == HazardWarn.Imminent ? "봉쇄 임박" : Warn == HazardWarn.Sealed ? "봉쇄됨" : "";
            if (tag.Length == 0) return "";
            string where = string.IsNullOrEmpty(Region) ? "" : " " + Region;
            string time = Warn != HazardWarn.Sealed && SecondsToClose >= 0 ? " · 봉쇄까지 " + (SecondsToClose / 60).ToString("00") + ":" + (SecondsToClose % 60).ToString("00") : "";
            return "[" + tag + "]" + where + time;
        }

        // Typed: the HUD compares every frame, and ValueType.Equals boxes and reflects (7 allocations a call).
        public bool Equals(HudHazardView o) => Warn == o.Warn && SecondsToClose == o.SecondsToClose && Region == o.Region && Keys == o.Keys && Chip == o.Chip;
        public override bool Equals(object obj) => obj is HudHazardView o && Equals(o);
        public override int GetHashCode() => System.HashCode.Combine(Warn, SecondsToClose, Region, Keys, Chip);

        public string Items()
        {
            string k = Keys > 0 ? "열쇠 ×" + Keys : "";
            string c = Chip == HudChip.Held ? "칩 보유" : Chip == HudChip.Used ? "칩 사용됨" : "";
            return k.Length > 0 && c.Length > 0 ? k + "  ·  " + c : k + c;
        }
    }
}

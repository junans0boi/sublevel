using System;

namespace Game.Core
{
    // What the minimal pollution HUD shows (grilling Q4), as plain numbers the UI layer draws. No Unity types.
    public struct PollutionHudModel
    {
        public bool BannerVisible;       // a region is announced and not yet confirmed
        public int BannerRegion;         // the soonest announced one
        public float BannerSeconds;      // until its confirmation
        public int Confirmed;            // regions confirmed so far
        public bool InGas;               // the player's cell is reached by the front
        public float Dps;                // health per second the player takes now (after cover)
        public float Vignette01;         // red screen edge strength, 0 outside the gas
        public float CoverSeconds;       // gas cover left, 0 = none
        public bool CoverIsAntidote;
        public bool CoverUseless;        // in the final region the cover does nothing
    }

    // Same ordering as the UI track's HazardWarn (ui_HUD_SLOT.md): None / Warning / Imminent / Sealed.
    public enum PollutionHazardLevel : byte { None = 0, Warning = 1, Imminent = 2, Sealed = 3 }
    // Same as HudChip: None / Held / Used.
    public enum PollutionChipFlag : byte { None = 0, Held = 1, Used = 2 }

    public static class PollutionHud
    {
        public const float ImminentSeconds = 30f;

        // The HUD slot's warning level: in the gas = Sealed, a confirmation within 30 s = Imminent, announced = Warning.
        public static PollutionHazardLevel Level(PollutionHudModel h) =>
            h.InGas ? PollutionHazardLevel.Sealed
            : !h.BannerVisible ? PollutionHazardLevel.None
            : h.BannerSeconds <= ImminentSeconds ? PollutionHazardLevel.Imminent : PollutionHazardLevel.Warning;

        // Held while the team has a chip on a body-bearer or in a box; Used when only revived/spent ones remain is
        // decided by the caller (it knows the history), here None when the team has no live chip.
        public static PollutionChipFlag ChipFlag(ChipTeamStatus s) =>
            s.Carried + s.Stored > 0 ? PollutionChipFlag.Held : PollutionChipFlag.None;

        public static PollutionHudModel Build(PollutionMatch m, double now, float x, float z, PollutionExposure exposure)
        {
            var tl = m.Timeline;
            var hud = new PollutionHudModel { Confirmed = tl.Confirmed(now), BannerRegion = -1 };
            double best = double.MaxValue;
            for (int r = 0; r < m.RegionCount; r++)
            {
                if (tl.StateOf(r, now) != PollutionState.Warning) continue;
                double left = tl.ConfirmAt(r) - now;
                if (left < best) { best = left; hud.BannerRegion = r; }
            }
            if (hud.BannerRegion >= 0) { hud.BannerVisible = true; hud.BannerSeconds = (float)best; }
            int cell = m.CellAt(x, z);
            if (!tl.Grid.IsSea(cell) && tl.Contaminated(cell, now))
            {
                hud.InGas = true;
                float raw = tl.DamagePerSecond(cell, now);
                hud.Dps = exposure == null ? raw : tl.DamagePerSecond(cell, now, exposure.Cover);
                float top = m.Rules.Dps[m.Rules.Dps.Length - 1];
                hud.Vignette01 = Math.Max(0f, Math.Min(1f, raw / Math.Max(1f, top)));
                hud.CoverUseless = tl.Grid.Region[cell] == tl.FinalRegion && exposure != null && exposure.CoverLeft(now) > 0f;
            }
            if (exposure != null)
            {
                hud.CoverSeconds = exposure.CoverLeft(now);
                hud.CoverIsAntidote = hud.CoverSeconds > 0f && exposure.Cover.Factor <= m.Rules.AntidoteFactor;
            }
            return hud;
        }

        // The HUD slot's content (ui_HUD_SLOT.md): level, seconds and region of the soonest confirmation, keys, chip.
        public static HudHazardView ToHazard(PollutionHudModel h, string regionName, int keys, PollutionChipFlag chip) => new HudHazardView
        {
            Warn = (HazardWarn)(byte)Level(h),
            SecondsToClose = h.BannerVisible ? (int)Math.Ceiling(h.BannerSeconds) : -1,
            Region = h.InGas || h.BannerVisible ? regionName : "", // in the gas: the region the player is in (the caller names it)
            Keys = keys,
            Chip = (HudChip)(byte)chip,
        };

        // The cover badge (#270): which item icon shows while a cover runs ("" = none), and its clock.
        public static string CoverIconKey(PollutionHudModel h) => h.CoverSeconds <= 0f ? "" : h.CoverIsAntidote ? "antidote" : "gas_filter";

        // Geiger hook (#270, sound in S5): clicks per second the counter would make here, 0 outside the gas.
        // Faster with the damage the player really takes: 2/s at the lightest gas, about 20/s at 20 hp/s.
        public static float GeigerClicksPerSecond(PollutionHudModel h) => h.InGas ? 2f + 0.9f * Math.Max(0f, h.Dps) : 0f;

        // Seconds to the next click for a uniform draw u in [0,1): random (Poisson) gaps like a real counter, mean 1/rate.
        public static float NextGeigerGap(float clicksPerSecond, float u) => clicksPerSecond <= 0f ? float.PositiveInfinity : -(float)Math.Log(1.0 - Math.Min(u, 0.9999f)) / clicksPerSecond;

        // "오염 중 · 초당 4.5 · 방독 0:42" (cover useless in the final region), "" outside the gas.
        public static string GasLine(PollutionHudModel h)
        {
            if (!h.InGas) return string.Empty;
            string line = "오염 중 · 초당 " + h.Dps.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            if (h.CoverSeconds > 0f) line += h.CoverUseless ? " · 방독 효과 없음" : " · " + (h.CoverIsAntidote ? "해독" : "방독") + " " + Clock(h.CoverSeconds);
            return line;
        }

        // What the key prompt says at a station ("" = nothing): insert, close the doors, pick the destination.
        // `ridesLeft`: key rides the team still has (ADR-360); 0 = used up, the key cannot start a ride.
        public static string KeyPrompt(KeyPhase phase, bool isHolder, int keys, float secondsLeft, int ridesLeft = int.MaxValue)
        {
            switch (phase)
            {
                case KeyPhase.Idle: return keys <= 0 ? "" : ridesLeft <= 0 ? "팀 승강 횟수를 모두 사용함" : "F — 열쇠 삽입 (열쇠 " + keys + "개)";
                case KeyPhase.DoorOpen: return isHolder ? "F — 문 닫기 · 탑승 " + Clock(secondsLeft) : "승강기 대기 중 " + Clock(secondsLeft);
                case KeyPhase.Selecting: return isHolder ? "F — 행선지 선택, 길게 눌러 출발 " + Clock(secondsLeft) : "행선지 선택 중";
                case KeyPhase.Recharging: return "전력 충전 중 " + Clock(secondsLeft);
                default: return "";
            }
        }

        public static string Clock(float seconds)
        {
            int s = Math.Max(0, (int)Math.Ceiling(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // "오염 경보 · 청람읍권 1:45 후 확정" (the name comes from the map's region list).
        public static string BannerText(PollutionHudModel h, string regionName) =>
            h.BannerVisible ? "오염 경보 · " + regionName + " " + Clock(h.BannerSeconds) + " 후 확정" : string.Empty;
    }
}

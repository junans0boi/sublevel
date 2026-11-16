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

    public static class PollutionHud
    {
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

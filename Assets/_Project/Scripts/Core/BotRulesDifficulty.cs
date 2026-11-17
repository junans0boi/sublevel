using System;

namespace Game.Core
{
    // The three bot difficulties (#149, ADR-117): one factor set each on reaction time, aim error and what the bot
    // senses. Normal is the data file's own values, untouched.
    public enum BotDifficulty { Easy = 0, Normal = 1, Hard = 2 }

    public static class BotLevels
    {
        // The level the match runs at (the settings screen sets it; GameSettings.ApplyLive). Normal until told.
        public static BotDifficulty Level = BotDifficulty.Normal;

        public static readonly string[] Labels = { "쉬움", "보통", "어려움" };

        // `baseRules` for Normal itself (same object), a scaled copy otherwise.
        public static BotRules For(BotRules baseRules, BotDifficulty level)
        {
            if (baseRules == null) baseRules = new BotRules();
            if (level == BotDifficulty.Normal) return baseRules;
            bool hard = level == BotDifficulty.Hard;
            float reaction = hard ? 0.55f : 1.8f;   // time from seeing to the first shot
            float aim = hard ? 0.55f : 1.8f;        // aim error cone
            float turn = hard ? 1.5f : 0.6f;        // how fast it turns to a target
            float sight = hard ? 1.15f : 0.7f;      // view distance and close-sense radius
            float cone = hard ? 1.05f : 0.85f;      // field of view
            float ears = hard ? 1.2f : 0.7f;        // hearing radii
            float sense = hard ? 0.7f : 1.5f;       // seconds between two looks
            float pause = hard ? 0.7f : 1.3f;       // pause between bursts
            var r = baseRules.Clone();
            r.ReactionSeconds *= reaction;
            r.ReacquireSeconds *= hard ? 0.7f : 1.3f;
            r.AimErrorNearDeg *= aim;
            r.AimErrorFarDeg *= aim;
            r.TurnDegPerSecond *= turn;
            r.ViewDistance *= sight;
            r.CloseSenseRadius *= sight;
            r.FieldOfViewDeg = Math.Min(170f, r.FieldOfViewDeg * cone);
            r.FootstepHearingRadius *= ears;
            r.GunshotHearingRadius *= ears;
            r.SenseInterval *= sense;
            r.BurstPauseMin *= pause;
            r.BurstPauseMax *= pause;
            r.ThrowIntervalSeconds *= hard ? 0.7f : 1.5f;
            // Farming (ADR-294): the easy bot farms longest and is told where a weapon is; the hard one kits itself out with armour.
            r.FarmSeconds *= hard ? 180f / 210f : 240f / 210f;
            r.FarmHint = !hard;
            if (hard) { r.FarmArmorPieces = Math.Max(r.FarmArmorPieces, 2); r.FarmHealItems = Math.Max(r.FarmHealItems, 2); }
            else r.FarmHealItems = 0;
            return r;
        }
    }
}

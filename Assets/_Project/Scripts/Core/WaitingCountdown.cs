using System;

namespace Game.Core
{
    public enum StartReason { None, Full, TimeUp }

    // What the waiting room shows and what the server decides this tick (#413).
    public struct CountdownState
    {
        public bool Running;        // somebody is there and the clock runs
        public bool Done;           // start the match now
        public float SecondsLeft;
        public StartReason Reason;  // Full: the short deadline of a full room ended it; TimeUp: the max time (or the end of the clock in general)
        public float Elapsed;       // seconds since the first person came (0 while nobody is there)
        public int BotsTotal;       // bots the match gets in all (BotFill.Plan); the waiting bots already there count towards it
    }

    // The countdown before a match starts (ADR-380). Max WaitMaxSeconds after the first person came; when the room is full
    // (people + bots at Participants, or people at MaxHumans) the deadline moves up to WaitFullSeconds after it filled, if
    // that is sooner. Pure: the caller passes the clock. Nobody there: no clock. O(1) per tick.
    public sealed class WaitingCountdown
    {
        readonly MatchRules _rules;
        readonly float _max;     // 0 = never start by time
        double _openedAt = double.NaN, _fullAt = double.NaN;

        // overrideMaxSeconds: --start-seconds when given (>= 0; 0 = no time limit), negative = the data value.
        public WaitingCountdown(MatchRules rules, float overrideMaxSeconds = -1f)
        {
            _rules = rules;
            _max = overrideMaxSeconds >= 0f ? overrideMaxSeconds : rules.WaitMaxSeconds;
        }

        public float MaxSeconds => _max;

        public void Reset() { _openedAt = double.NaN; _fullAt = double.NaN; }

        public bool IsFull(int humans, int bots)
            => humans >= _rules.MaxHumans || humans + bots >= Math.Min(_rules.Participants, MatchRules.MaxParticipantsLimit);

        public CountdownState Update(double now, int humans, int bots)
        {
            if (humans <= 0)
            {
                Reset();
                return new CountdownState { SecondsLeft = _max > 0f ? _max : 0f, BotsTotal = BotFill.Plan(0, _rules.Participants, _rules.MinBots) };
            }
            if (double.IsNaN(_openedAt)) _openedAt = now;
            if (double.IsNaN(_fullAt) && IsFull(humans, bots)) _fullAt = now; // once full the short deadline stays, even if people leave
            double maxEnd = _max > 0f ? _openedAt + _max : double.PositiveInfinity;
            double fullEnd = double.IsNaN(_fullAt) ? double.PositiveInfinity : _fullAt + _rules.WaitFullSeconds;
            bool fullWins = fullEnd < maxEnd;
            double end = Math.Min(maxEnd, fullEnd);
            bool never = double.IsPositiveInfinity(end);
            float left = never ? 0f : (float)Math.Max(0.0, end - now);
            bool done = !never && left <= 0f;
            return new CountdownState
            {
                Running = true,
                Elapsed = (float)Math.Max(0.0, now - _openedAt),
                Done = done,
                SecondsLeft = left,
                Reason = done ? (fullWins ? StartReason.Full : StartReason.TimeUp) : StartReason.None,
                BotsTotal = BotFill.Plan(humans, _rules.Participants, _rules.MinBots),
            };
        }
    }
}

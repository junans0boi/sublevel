using System.Collections.Generic;

namespace Game.Core
{
    // Who may come back to which combatant after a dropped connection (#166, ADR-125). Pure: the server keeps one of
    // these, tells it the clock, and acts on the answers. A hold belongs to a session token the client chose.
    public sealed class SessionHolds
    {
        struct Hold { public int Combatant; public double Until; }

        readonly Dictionary<string, Hold> _holds = new Dictionary<string, Hold>();

        public int Count => _holds.Count;

        // The combatant of `token` stays for `seconds` from `now` (a second hold of the same token replaces the first).
        public void Begin(string token, int combatant, double now, double seconds)
        {
            if (string.IsNullOrEmpty(token)) return;
            _holds[token] = new Hold { Combatant = combatant, Until = now + seconds };
        }

        public void Clear() => _holds.Clear();

        public bool Has(string token) => !string.IsNullOrEmpty(token) && _holds.ContainsKey(token);

        // The combatant of `token` if it is still held at `now` (strictly before the deadline); the hold ends.
        public bool TryResume(string token, double now, out int combatant)
        {
            combatant = -1;
            if (string.IsNullOrEmpty(token) || !_holds.TryGetValue(token, out var hold) || now >= hold.Until) return false;
            _holds.Remove(token);
            combatant = hold.Combatant;
            return true;
        }

        // Holds whose time is over at `now`: removed, their combatants returned (a bot takes them over).
        public List<int> TakeExpired(double now)
        {
            var result = new List<int>();
            List<string> gone = null;
            foreach (var pair in _holds)
            {
                if (now < pair.Value.Until) continue;
                result.Add(pair.Value.Combatant);
                (gone ?? (gone = new List<string>())).Add(pair.Key);
            }
            if (gone != null) foreach (var key in gone) _holds.Remove(key);
            return result;
        }

        // Drops a hold without a result (its combatant is gone for another reason).
        public void Cancel(string token) { if (token != null) _holds.Remove(token); }
    }
}

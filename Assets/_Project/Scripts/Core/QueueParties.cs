using System.Collections.Generic;

namespace Game.Core
{
    // Who stands behind a queue ticket (#392). A backend party is several connections but ONE ticket: the first member's connection id
    // is the ticket's id, the others join it. Without a party id (0) every connection is its own ticket. Pure.
    public sealed class QueueParties
    {
        public enum AddResult { NewTicket, JoinedTicket, Duplicate, Refused }

        readonly Dictionary<long, int> _ticketOfParty = new Dictionary<long, int>();
        readonly Dictionary<int, List<int>> _members = new Dictionary<int, List<int>>();
        readonly Dictionary<int, int> _ticketOfConn = new Dictionary<int, int>();
        readonly Dictionary<int, int> _sizeOfTicket = new Dictionary<int, int>();

        public bool Has(int conn) => _ticketOfConn.ContainsKey(conn);

        // `size`: the places a new ticket holds (the first member's party size; later members' word does not change it). Refused (#499): a member beyond that, who would take the
        // next team's place, and a new connection whose reused id still keys the ticket of a party its first member left.
        public AddResult Add(int conn, long partyId, int size)
        {
            if (_ticketOfConn.ContainsKey(conn)) return AddResult.Duplicate;
            if (partyId != 0 && _ticketOfParty.TryGetValue(partyId, out int key))
            {
                if (_members[key].Count >= _sizeOfTicket[key]) return AddResult.Refused;
                _members[key].Add(conn); _ticketOfConn[conn] = key;
                return AddResult.JoinedTicket;
            }
            if (_members.ContainsKey(conn)) return AddResult.Refused;
            _members[conn] = new List<int> { conn }; _ticketOfConn[conn] = conn; _sizeOfTicket[conn] = size;
            if (partyId != 0) _ticketOfParty[partyId] = conn;
            return AddResult.NewTicket;
        }

        // A connection left (cancelled or dropped). True when its ticket has nobody left: `ticket` must leave the queue.
        public bool Remove(int conn, out int ticket)
        {
            ticket = -1;
            if (!_ticketOfConn.TryGetValue(conn, out ticket)) return false;
            _ticketOfConn.Remove(conn);
            var list = _members[ticket];
            list.Remove(conn);
            if (list.Count > 0) return false;
            _members.Remove(ticket); _sizeOfTicket.Remove(ticket);
            long party = 0; bool found = false;
            foreach (var p in _ticketOfParty) if (p.Value == ticket) { party = p.Key; found = true; break; }
            if (found) _ticketOfParty.Remove(party);
            return true;
        }

        // Everyone behind a ticket (just the ticket's own connection when it had no party).
        public IReadOnlyList<int> Members(int ticket) => _members.TryGetValue(ticket, out var l) ? l : (IReadOnlyList<int>)new int[0];
    }
}

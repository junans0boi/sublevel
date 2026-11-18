using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Game.Core;

namespace Game.Net
{
    // The lobby's "join" through the backend (#392): create a ticket, poll it every 2s, and when it is matched hand out the server to join.
    // Call Tick from the main thread; backend answers are applied there, so the fields below are only touched on that thread.
    public sealed class MatchTicketClient
    {
        public enum StateKind { Idle, Queued, Matched, Failed }

        readonly IBackend _backend;
        readonly ConcurrentQueue<Action> _done = new ConcurrentQueue<Action>();
        bool _busy;
        double _nextPoll;
        string _ticketId;

        public StateKind State { get; private set; }
        public string Text { get; private set; } = "";
        public string Host { get; private set; }
        public int Port { get; private set; }
        public int Bots { get; private set; }
        public event Action Changed;

        public MatchTicketClient(IBackend backend) { _backend = backend; }

        public void Start(string mode, double now)
        {
            if (State == StateKind.Queued || _busy) return;
            State = StateKind.Queued; Text = "매칭 요청 중…"; Host = null; _ticketId = null; Changed?.Invoke();
            Run(_backend.CreateTicket(mode), r => Apply(r), now);
        }

        public void Cancel(double now)
        {
            var id = _ticketId;
            State = StateKind.Idle; Text = ""; _ticketId = null; Changed?.Invoke();
            if (id != null) _backend.CancelTicket(id); // best effort: the backend also expires it after 45s without polling
        }

        public void Tick(double now)
        {
            while (_done.TryDequeue(out var a)) a();
            if (State != StateKind.Queued || _busy || _ticketId == null || now < _nextPoll) return;
            Run(_backend.CurrentTicket(), r => Apply(r), now);
        }

        void Run(Task<ApiResult<TicketDto>> call, Action<ApiResult<TicketDto>> apply, double now)
        {
            _busy = true;
            _nextPoll = now + TicketRules.PollSeconds;
            call.ContinueWith(t => _done.Enqueue(() => { _busy = false; apply(t.IsFaulted ? ApiResult<TicketDto>.Fail(0, "") : t.Result); }));
        }

        void Apply(ApiResult<TicketDto> r)
        {
            if (State != StateKind.Queued) return; // cancelled while the answer was on its way
            if (!r.Ok || r.Value == null) { Fail(TicketRules.ErrorText(r.Status, r.Error)); return; }
            var t = r.Value;
            _ticketId = t.id;
            if (t.status == "matched" && t.match != null && t.match.server != null)
            {
                State = StateKind.Matched; Host = t.match.server.host; Port = t.match.server.port; Bots = t.match.bots; Text = "판이 잡혔다. 접속 중…";
            }
            else if (t.status == "queued")
                Text = t.queue != null ? TicketRules.QueueText(t.queue.reason, t.queue.position, t.queue.fill_in_s, t.queue.players) : "대기 중…";
            else { Fail("매칭이 취소되었거나 만료되었다."); return; }
            Changed?.Invoke();
        }

        void Fail(string text) { State = StateKind.Failed; Text = text; Changed?.Invoke(); }
    }
}

# 매칭 대기열 필드 계약 (UI 트랙용, 1장) — 2026-11-17, ADR-247~249

서버 → 모든 로비 클라이언트. 읽기만 한다(UI는 서버 상태를 바꾸지 않는다). 계정이 없어 사람 식별은 접속 클라이언트 id.

```csharp
// Game.Core (Core/MatchQueue.cs)
public struct MatchQueueState {
    public bool  Open;         // 누군가 기다리고 창이 돈다
    public int   Waiting;      // 대기 중 사람 수(파티 크기 합)
    public int   Parties;      // 대기 중 파티(티켓) 수
    public int   Target;       // 판 참가자 수(사람+봇), 기본 50
    public float SecondsLeft;  // 대기 창 남은 초(0 = 안 돌거나 끝남)
    public bool  Started;      // 판이 만들어지는 중(클라는 이어지는 포트로 이동)
}
// Game.Net: LobbyNet.Queue (클라 읽기), LobbyNet.ServerSetQueue(state) (서버 쓰기), LobbyState.Queue (방송)
```

표시 문구 제안: `Open`이면 "매칭 중 {Waiting}/{Target} · 남은 {SecondsLeft:0}초", 아니면 숨김. `Waiting`은 사람만이며 나머지(`Target − Waiting`)는 봇으로 채워진다는 뜻이다(문구에 "나머지는 봇" 가능). 창이 끝나면 `Started=true` 후 기존 `LobbyStart`(매치 포트)가 온다.

솔로/스쿼드·파티 크기는 클라이언트가 시작을 누를 때 서버로 보내는 값(`QueueParty.Size` 1~4, 모드 솔로/듀오/스쿼드)이다. **그 전송(RPC/방송)은 아직 없다** — 대기열 흐름 연결(DedicatedServer)과 함께 후속. UI는 지금 `LobbyNet.Queue`만 읽도록 만들어 두면 된다.

## UI가 부르는 함수·이벤트 (확정, ADR-258)
```csharp
// Game.Net.MatchmakingClient — 서버(전용 서버)에 접속한 LobbyNet(Join) 위에 만든다
var mm = new MatchmakingClient(lobby);
bool ok = mm.RequestStart(TeamMode.Solo /*Solo|Duo|Squad*/, partySize: 1);   // 시작(매칭 요청). 1~4, 솔로는 무시. 로비에 연결 전이면 false
mm.Cancel();                                                                // 대기 취소
MatchQueueState s = mm.State;                                               // Open/Waiting/Parties/Target/SecondsLeft
mm.StateChanged += state => { /* "매칭 중 {Waiting}/{Target} · 남은 {SecondsLeft:0}초" */ };
// 판이 만들어지면 기존 이벤트: lobby.MatchStarting (→ MainMenu.StartLobbyMatch, LobbyStart.Mode로 모드 전달)
```
- 서버는 첫 요청의 모드로 대기 창(기본 30 s)을 열고, 다른 모드 요청은 그 판에 안 넣는다. 창 만료·방 가득(사람 40)이면 구성기 결과(사람 N + 봇 M)로 시작하고 요청한 사람들에게만 매치 포트가 간다(요청 안 한 접속자는 방에 남는다).
- **솔로 "시작" 버튼(싱글)은 대기열을 쓰지 않는다**: `MatchRules.FillToParticipants`(rules.json, 켜짐)이면 사람 1 + 봇 49로 바로 시작한다(`MatchRules.BotsFor`). 호스트 방도 `TargetParticipants`(50)로 봇을 채운다.
- 파티원 식별: 아직 없다 — 파티장 한 명이 `partySize`만 보내고 파티원은 따로 대기열에 들어가지 않는다(팀 편성은 기존 `Teams.Deploy`가 사람부터 채움; 파티원을 같은 팀으로 묶는 것은 후속).

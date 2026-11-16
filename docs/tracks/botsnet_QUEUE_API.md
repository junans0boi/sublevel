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

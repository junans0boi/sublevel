# 멀티 종단 시험 체크리스트 (#389~#392)

준비물: 구글 계정 2개(A, B, 둘 다 닉네임 설정), Mac 2대 또는 Mac 1대 + 두 번째 런처 계정이 가능한 환경, 런처 0.1.5 이상, 서버(오라클)에 r3 게임 서버와 백엔드 배포 완료.
토큰·키는 화면이나 채팅에 붙여넣지 않는다.

## 0. 서버 상태(에이전트/준환)
- [ ] **[준환]** 백엔드 헬스: `https://sublevel.steady2vivid.kro.kr/api/` 응답, `MAX_CONCURRENT_MATCHES` 확인(기본 1).
- [ ] **[준환]** 게임 서버 환경에 `SUBLEVEL_TOKEN_SECRET`(= 백엔드 `TOKEN_SECRET`), `BACKEND_BASE_URL`, `INTERNAL_API_KEY`가 있다(값은 서버 env 파일에만).
- [ ] **[준환]** 서버 로그에 `[Server] backend matches: only assigned accounts may join` 와 `results: sending to the backend`. — 같은 두 줄이 로컬 E2E 로그에 나옴 (로컬 E2E ✓ L)

## 1. 로그인 (#389)
1. [ ] **[준환]** 런처 실행 → 구글 로그인 → 게임 실행. (#402 세션이 Mac에서 런처 0.1.7 → v0.2.0 실행 확인. 토큰 env 전달·argv 없음: 런처 `cargo test e2e_install_run_token_hygiene` 통과)
2. [x] (로컬 E2E ✓ L) 서랍 열림 / **[준환]** 런처 실토큰 — 게임 메인 메뉴 알림 벨을 누르면 친구 서랍이 열린다(열리지 않고 "로그인하면…" 토스트면 토큰 전달 실패 → 런처 버전/`SUBLEVEL_GAME_TOKEN` 확인).
3. [x] (로컬 E2E ✓ L) 로그인 없이 게임 exe를 직접 실행한 경우: 서버에 접속하면 "로그인이 필요하다"로 거절된다.

## 2. 친구·파티 (#391) — A와 B 두 클라이언트
1. [x] (로컬 E2E ✓ L) 로직·서랍 화면 — A: 서랍의 입력칸에 B의 닉네임 → "친구 추가". B 서랍(최대 5초 안에)에 "친구 요청"이 뜬다.
2. [x] (로컬 E2E ✓ L) 로직·화면 — B: "수락". 양쪽 친구 목록에 서로 "로비"로 보인다(접속 상태 20초 하트비트, 반영에 최대 20초).
3. [x] (로컬 E2E ✓ L) 로직·화면·★ — A: B 옆 "초대". B 서랍(2.5초 안에)에 "파티 초대" → "수락". 양쪽에 "파티 2/4", A에 ★(파티장).
4. [x] (로컬 E2E ✓ L) 로직 / 화면은 나가기 후 솔로 복귀만 — B가 파티를 "파티 나가기" → 양쪽에서 반영. 다시 초대·수락으로 복구.
5. [x] (로컬 E2E ✓ L) 오류 문구: 없는 닉네임 추가 → "그 닉네임의 플레이어가 없다."

## 3. 매칭 → 배정 → 접속 (#392)
1. [x] (로컬 E2E ✓ L) 문구·로비 버튼 — 파티(A 파티장, B 멤버) 상태에서 A가 로비의 시작/매칭 버튼을 누른다. 문구가 "인원을 모으는 중 · n초 뒤 봇으로 채워 시작"(또는 서버가 가득 찼을 때 "n번째").
2. [x] (로컬 E2E ✓ L) B 화면에도 같은 대기 문구가 나온다(파티원이 티켓을 이어받음).
3. [x] (로컬 E2E ✓ L) 로비 방 접속까지 / **[준환]** 실서버·`backend match m-…` 판 시작 로그 — 배정되면(기본 15초) 두 클라이언트가 자동으로 서버 방에 접속하고 서버 대기열 문구로 바뀐다. 서버 로그: `[Server] backend match m-…`가 판 시작 때, 방 접속 로그에 두 사람.
4. [x] (로컬 E2E ✓ L) 닉네임이 방 목록에 토큰의 닉네임으로 보인다.
5. [ ] **[준환]** (팀 매핑은 #424 범위, 로컬 미검증) 판 시작 후 A와 B가 **같은 팀**이다(HUD 팀 표시/엘리베이터 같은 칸). 서버 로그: `queue closed: … teams`.
6. [x] (로컬 E2E ✓ L) 취소: 다른 파티로 매칭 후 "취소" → 서버 대기열·백엔드 티켓이 사라진다(`GET /api/match/tickets/current` 404).
7. [x] (로컬 E2E ✓ L) 배정되지 않은 계정 C가 서버 주소로 직접 접속 시도 → "이 서버에 배정된 판이 없다".

## 4. 판 종료 → 결과 (#390)
1. [ ] **[준환]** (로컬은 판 결정 이벤트를 직접 발생시킴, 실제 한 판은 미진행) 한 판을 끝까지 하거나 서버 로그 `match decided`가 나올 때까지 진행.
2. [x] (로컬 E2E ✓ L) 서버 로그: `results: 2 people queued for the backend (m-…)` → `backend answered 200`.
3. [x] (로컬 E2E ✓ L) API / **[준환]** 사이트 화면 — 사이트/API: `GET /api/me/results`(또는 내 정보 페이지)에 순위·킬·생존시간이 보인다. A, B 모두.
4. [x] (로컬 E2E ✓ L) 백엔드의 해당 판이 finished이고 다음 티켓이 배정 가능하다(`GET /api/match/matches?status=active`에서 사라짐).
5. [ ] **[준환]** (EditMode `ResultsOutboxTests.BackendDown_KeepsFile_ThenSendsAfterBackoff_AndDeletes`·`SurvivesRestart_…`는 통과 이력, 실백엔드 중단은 미시험) 백엔드를 잠시 내렸다 올린 뒤에도(결과 전송 중 장애) 결과가 들어온다: 서버 `results-outbox` 폴더에 파일이 남았다가 지워진다.
6. [ ] **[준환]** (로컬은 사람 2행만 넣음 — 봇 제외는 `ResultsRules`/`MatchState` 단위 범위) 봇은 기록되지 않는다(결과 행 수 = 사람 수).

## 알려진 한계(실패해도 버그가 아님)
- 12시간 넘게 켜 둔 게임은 토큰 만료(401) → 런처에서 다시 실행.
- 판이 결정되기 전에 연결이 끊긴 사람은 결과에 빠질 수 있다.
- 서버는 한 대·동시 판 1개. 판 중에는 다음 파티가 "서버가 가득 참"으로 대기.
- 친구 패널 화면 배치는 실제 화면으로 처음 확인하는 것이다. 어긋나면 스크린샷을 남겨 달라.

## 에이전트 로컬 종단 결과 (2026-11-20, #389)
`L` = 이 Mac에서 로컬 백엔드(`npm`/`node src/main.ts`, 임시 DB, 테스트 계정 3개 E2eAlpha/E2eBravo/E2eCharlie)와 게임 코드로 확인. `[준환]` = 실서버·구글 로그인·실제 화면이 필요해 사람이 확인.

- 백엔드 HTTP: `BASE=http://127.0.0.1:3199 scripts/e2e_social.sh` → 23항목 **ALL PASS** (게임 토큰 발급·범위 제한, 친구 요청/수락/접속 상태, 파티 초대/가입, 비파티장 매칭 거절, 티켓 생성/공유/중복 거절/취소).
- 게임 PlayMode `Game.Tests.BackendE2ETests` (게임의 `HttpBackend`·`MatchTicketClient`·`LobbyNet` 전용 서버+클라 3개·`BackendMatchPoller`·`ResultsReporter`를 실제 백엔드에 연결, 한 프로세스) → **Passed**. 로그 `Logs/net-e2e/`:
  - 없는 닉네임 → "그 닉네임의 플레이어가 없다." / 친구 수락 후 B "lobby" / 파티 2명·A 파티장, B 나가기→A 반영→재초대
  - C 티켓 취소 → `GET /api/match/tickets/current` 404
  - A·B 대기 문구 "인원을 모으는 중 (2명) · 2초 뒤 봇으로 채워 시작"(B는 파티 티켓을 이어받음) → 배정 `127.0.0.1:<port>`, 봇 6
  - 로그인 없는 접속 → "로그인이 필요하다…", 배정 안 된 C → "이 서버에 배정된 판이 없다…" (서버 로그 `[Lobby] refused client …: LoginRequired/NotAssigned`)
  - 방 목록 = 토큰 닉네임 `E2eAlpha, E2eBravo`
  - 결과: `[Server] results: 2 people queued for the backend (m-…)` → `backend answered 200`, A·B `/api/me/results`에 순위·킬·생존시간, `matches?status=active` 비어 있음
  - Unity 로그에 `sg_` 토큰 0회.
- 수정한 버그: 서버가 거절 직후 연결을 끊어 거절 사유가 경쟁으로 사라지고 클라이언트에 "응답이 없다"가 뜸 → 거절 후 클라이언트가 스스로 끊고, 서버는 2초 뒤 정리(`LobbyNet.Refuse`).
- 다시 돌리기: 백엔드를 `TOKEN_SECRET`·`INTERNAL_API_KEY`·`GAME_SERVER_ADDR=127.0.0.1:17989`·`MATCH_FILL_AFTER_S=5`로 띄우고, `scripts/e2e-accounts.ts create`(+계정 C)로 세션을 만든 뒤 `POST /api/auth/game-token`으로 sg_ 토큰 3개를 받아 `SUBLEVEL_E2E_API/GAME_A/B/C`, `SUBLEVEL_TOKEN_SECRET`, `BACKEND_BASE_URL`, `INTERNAL_API_KEY`, `SUBLEVEL_E2E_PORT` env로 `tools/unity.sh test playmode Game.Tests.BackendE2ETests`. env가 없으면 테스트는 Ignore.
- 실제 메인 메뉴 화면(`Game.Tests.FriendsCaptureTests`, Capture/Explicit, A는 런처와 같은 `SUBLEVEL_GAME_TOKEN`+`SUBLEVEL_API_BASE`로 로그인): 빈 서랍, 친구 요청, 친구 로비, 파티 초대, 파티원(★ 파티장), 없는 닉네임 오류, 파티원 대기 문구, 솔로 매칭 대기. 캡처 9장은 세션 노트 `i389_shots/`.
- 화면에서 찾아 고친 것: 로비 이름판이 로그인 닉네임 대신 "플레이어"였음, 백엔드 파티에 들어가도 로비가 "솔로 · 1인"/"혼자 시작"이고 파티원이 무대에 없었음(→ 파티 구성·파티장·스쿼드 반영, `MainMenu.SyncBackendParty`), 파티원 화면에 파티장이 시작한 매칭 대기 문구가 안 나옴(`LobbyPanel` search), 서랍이 반투명하고 하단 시작 버튼을 덮었음(불투명, 상단바~하단 콘솔 사이).
- 남은 작은 흠: 벨에 새 요청/초대 배지 없음(서랍을 열어야 보임), 파티원 버튼이 매칭 중에도 "준비 완료 · 취소".


# CODE_STRUCTURE_AUDIT — 인게임 코드 구조 점검 (2026-11-23, main 1cedd4f 기준)

범위: `Assets/_Project/Scripts/{Core,Net,Gameplay,AI}`(약 80k줄) + Editor 빌더 일부. 읽기만 했고 Unity는 돌리지 않았다.
가정한 부하: 데디 서버 1프로세스가 **여러 판을 연속으로** 돈다(`DedicatedServer.EndMatch` → Bootstrap → 로비 재개), 80인, 변조 클라 가능성 있음.
중복 제외: 성능·GC·중복 구현·죽은 코드는 [INEFFICIENCY_AUDIT](INEFFICIENCY_AUDIT.md)(#469~#476), 연습↔멀티 차이는 [MODE_PARITY_AUDIT](MODE_PARITY_AUDIT.md)(#459~#467), 측정은 [OPTIMIZATION_ANALYSIS](OPTIMIZATION_ANALYSIS.md). 여기는 그 밖의 것만.

크기 S(반나절 이하)/M(1~2일)/L. 우선순위 P1(판·데이터가 실제로 깨짐) / P2(변조 클라 악용·드문 고장) / P3(구조 개선).
"확인"= 이 세션에서 코드로 직접 확인, "추정"= 일부 경로만 확인.

## 순위표

| # | 우선 | 크기 | 항목 | 위치 | 이슈 |
|---|---|---|---|---|---|
| 1 | P1 | S | 스쿼드 판 뒤 솔로 판도 스쿼드 규칙 | `Net/DedicatedServer.cs:183` | #494 |
| 2 | P1 | S | 판 결과 영구 삭제(400/401) + 디스크 예외가 판 종료 방송을 막음 | `Core/ResultsOutbox.cs:43,86`, `Net/ResultsReporter.cs:43-44` | #495 |
| 3 | P1 | S | 매치 서버가 못 뜨면 데디 서버가 영구 정지 | `Net/DedicatedServer.cs:205`, `Net/MatchDirector.cs:157` | #496 |
| 4 | P1 | S~M | 매칭된 플레이어가 `NotAssigned`로 거절되는 경쟁(추정) | `Net/BackendMatchPoller.cs:19,41`, `Gameplay/IMatchQueue.cs:68-72` | #497 |
| 5 | P1 | S | 문 소리가 초기화 순서에 따라 통째로 꺼질 수 있음 | `Gameplay/DoorAudio.cs:13-17` ↔ `Net/Door.cs:74-82` | #498 |
| 6 | P1 | S | 큐 파티 인원이 팀 크기를 넘어도 받아 팀 자리가 밀림 | `Net/LobbyNet.cs:409-411`, `Core/QueueParties.cs:17-28` | #499 |
| 7 | P2 | M | `CmdTeleported`로 0.5초마다 25 m 벽 통과 | `Net/MoveGuard.cs:49-61`, `Core/InputGuard.cs:53` | #500 |
| 8 | P2 | S | 낙하 보고를 안 보내면 낙하 피해 0 | `Net/CombatantStance.cs:203-230`, `Core/Locomotion.cs:292` | #501 |
| 9 | P2 | S | 차량 충돌 보고가 서버 속도와 비교 안 됨·빈도 제한 없음 | `Net/Vehicle.cs:1290`(CmdCrash), CmdImpact, `Net/VehicleDamage.cs:351-371` | #502 |
| 10 | P2 | S | 인벤토리 RPC 전체에 빈도 제한 없음 | `Net/Combatant.cs:835-912` 외 | #503 |
| 11 | P2 | S | 토큰 HTTP(8초)가 접속 제한 6초 안에 들어 있음 | `Net/LobbyNet.cs:64,179,301-320`, `Net/HttpBackend.cs:13` | 문서만 |
| 12 | P2 | S | 루팅·봇·연료 시드가 매 판 12345 고정 | `Scenes/World.unity`(`_seed: 12345`), `Net/MatchDirector.cs:230-233` | 문서만(ADR 필요) |
| 13 | P2 | S | `KeyElevatorNet.LastStatus`가 판 사이에 안 비워짐 | `Net/KeyElevatorNet.cs:28-31` | 문서만 |
| 14 | P2 | S | 기절 상태 검사 누락: 파쿠르·열쇠 승강기 | `Net/CombatantStance.cs:248-252`, `Net/ElevatorRides.cs:338`, `Net/KeyElevatorNet.cs:114-133` | 문서만 |
| 15 | P2 | S | `LookBroadcast` 길이 무제한 + `_looks` 미정리 | `Net/MatchDirector.cs:137,434` | 문서만 |
| 16 | P2 | S | 부팅 때 `/api/me` 1회 실패면 세션 내내 파티 UI 죽음 | `Net/LoginEnv.cs:21`, `Gameplay/MainMenu.cs:135` | 문서만 |
| 17 | P2 | S | 200 + 비JSON 응답이면 `DedicatedServer.Update` 프레임 통째로 건너뜀 | `Net/BackendMatchPoller.cs:55` | 문서만 |
| 18 | P2 | S | LAN 호스트 판: 먼저 죽은 게스트가 전원 판 재시작 | `Net/MatchState.cs:405-416` | #462에 포함 |
| 19 | P3 | M | `PlayerController.Update` 580줄·조기 return 5곳·입력 차단 8단 체인 | `Gameplay/PlayerController.cs:203-780` | 문서만 |
| 20 | P3 | S~M | `"WaitingIsland"` 이름 문자열 계약 6곳 + Net 어셈블리 안의 화면 코드 | `Net/WaitingRoom.cs:128`, `Net/WaitingHud.cs:271,282,313` 외 | 문서만(#464 관련) |
| 21 | P3 | S | UI 빌드 도우미 두 벌(UiKit/UiPaint) + 화면별 복사본 | `Gameplay/UiKit.cs`, `UiPaint.cs`, `Hud.cs:564-572`, `PauseMenu.cs:125`, `VehicleHud.cs:144` | 문서만 |

이미 다른 이슈에 있는 구조 항목(링크만): 중복 구현 → #475, 캡처·디버그 도구 ~3,700줄이 런타임 어셈블리에 있음 → #476, 호스트 단축 경로 검증 누락 → #460, 모드 룰을 토폴로지에서 분리 → #462.

## 상세

### 1. 스쿼드 판 뒤 솔로 판도 스쿼드 규칙 (확인)
- 문제: `OnMatchStarting`이 `StartMode != Solo || QueueMode != Solo`일 때만 `TeamMatch.Mode`를 쓴다. 솔로 판이면 이 줄을 건너뛰고, 프로세스 안에서 Mode를 되돌리는 곳이 없다(`TeamMatch.ResetStatics`는 앱 시작 때 한 번).
- 시나리오: 1판 스쿼드 큐 → Mode=Squad. 2판 솔로 큐 → 기절·부활·팀 승리 판정이 그대로 적용된다.
- 제안: `TeamMatch.Mode = _options.PerfSquad ? TeamMode.Squad : _lobby.StartMode;` 한 줄 + 두 판 연속 EditMode/PlayMode 테스트.

### 2. 판 결과 데이터 손실 (확인)
- 문제 A: `ResultsRules.Outcome`이 400/401을 Drop으로 보고 `Flush`가 파일을 지운다. 백엔드는 계정 하나만 FK 위반이어도 판 전체를 400으로 거절한다(서브 점검이 `sublevel-backend/src/modules/results/routes.ts:48`에서 확인).
- 문제 B: `OnResults`가 `Reported = true`를 먼저 세우고 `Enqueue`(파일 쓰기)를 try 없이 부른다. 예외는 `MatchState.ServerPostResults`를 지나 `ServerSendStatus(null)`(판 종료 방송)을 건너뛰게 한다.
- 시나리오: 판 도중 계정 하나 삭제 → 나머지 79명 전적 소멸. `INTERNAL_API_KEY` 교체를 백엔드 먼저 배포 → 그동안 모든 판 결과 401로 삭제. VM 디스크 가득 → 클라가 판 종료를 못 받고, `Reported`가 true라 `BackendMatchPoller.Finish`도 안 불림.
- 제안: Drop이면 `results-outbox/rejected/`로 이동, 401은 Retry. `OnResults` 본문 try/catch, `Reported`는 Enqueue 성공 뒤.

### 3. 매치 서버가 못 뜨면 데디 서버 영구 정지 (확인)
- 문제: `TickMatch`가 `director == null || !network.IsServerStarted`이면 `ShouldEndMatch` 전에 return한다. `MatchDirector`는 `ServerManager.StartConnection()` 반환값을 무시한다.
- 시나리오: `FreePort` 검사와 실제 바인드 사이에 포트를 뺏기거나 트랜스포트가 판 도중 멈춤 → Match 상태에서 못 빠져나와 로비가 다시 안 열린다. systemd 재시작 전까지 서버 1대가 죽은 상태.
- 제안: early-return 앞에 "시작 후 `NobodyCameSeconds`가 지났는데 서버가 안 떠 있으면 `EndMatch()`" 한 블록.

### 4. 매칭된 플레이어가 NotAssigned로 거절 (추정)
- 문제: 클라는 2초마다 티켓을 폴링해 `matched`면 바로 접속, 서버의 배정 목록(`_active`)은 3초 폴링 뒤에야 새 판을 안다. 티켓은 이미 Consume.
- 시나리오: 판이 잡힐 때마다 일정 확률로 "이 서버에 배정된 판이 없다"로 튕긴다.
- 제안: 서버가 모르는 계정이 오면 `_next = 0`으로 즉시 다시 폴링하고 hello를 3~4초 보류. 백엔드가 서버 확인 전에 `matched`를 주는지 먼저 확인할 것(백엔드 코드는 안 봄).

### 5. 문 소리가 초기화 순서에 따라 꺼질 수 있음 (확인)
- 문제: `DoorAudio.Hook`(SubsystemRegistration)이 `Door.Moved +=`를 하고, `Door.ResetStatics`(같은 SubsystemRegistration)가 `Moved = null`을 한다. Unity는 같은 단계 `[RuntimeInitializeOnLoadMethod]` 사이 순서를 보장하지 않는다. DoorAudio 테스트는 없다.
- 시나리오: 리셋이 나중에 돌면 그 실행 내내 문 여닫는 소리가 안 난다. Unity 업그레이드·어셈블리 변경으로 순서가 바뀌면 조용히 터진다.
- 제안: `DoorAudio.Hook`을 `BeforeSceneLoad`로(다른 훅들 `ResultScreen.Hook`, `PollutionWorld.Hook`과 같은 방식). 규칙 한 줄: "다른 클래스 정적 이벤트 구독은 BeforeSceneLoad 이후". 같은 패턴은 이것 하나만 찾음(GlassFx는 GlassPane이 이벤트를 안 지워서 안전).

### 6. 큐 파티 인원 미검사 (확인, UI가 막는지는 미확인)
- 문제: 첫 멤버만 크기(Duo면 2)로 티켓을 만들고, 같은 `PartyId`로 오는 연결은 인원 제한 없이 `JoinedTicket`. 레이아웃은 `PartySizes`(티켓 크기)로 짜고 토큰은 전원 것을 넣는다. `PartyId`는 클라가 보낸 값 그대로.
- 시나리오: 4인 파티가 Duo 큐 → 3·4번째가 다음 팀 자리로 들어가고 뒤 팀이 한 칸씩 밀림. id를 알면 남의 파티에 끼어듦.
- 제안: JoinedTicket이면 `Members(ticket).Count >= size`일 때 거절. 백엔드 토큰이 있으면 파티 소속을 claims로 확인.

### 7. 순간이동 보고로 벽 통과 (확인)
- 문제: `TryTeleport`는 거리 ≤ `TeleportMaxMeters`(25 m)·간격 ≥ 0.5초만 본다. 서버가 아는 사유(승강기 도착, 하차)나 벽·지오메트리 검사가 없고, 받으면 `_good`이 갱신돼 MoveGuard 교정도 피한다.
- 시나리오: 변조 클라가 0.5초마다 `CmdTeleported(pos + forward*24)` → 초속 ~50 m로 닫힌 문·벽 통과.
- 제안: 서버가 사유를 미리 걸어 둔 창(하차 후 N초, 승강기 도착, ServerCorrect 응답) 안에서만 수락 + 출발점→도착점 Linecast.

### 8. 낙하 피해 회피 (확인)
- 문제: `ServerTickFall`은 `_reported <= 0`이면 return, `AcceptedFall = Min(보고값, 관측값+허용치)`. 서버가 관측한 낙하(`_watchedDrop`)는 보고가 없으면 버려진다.
- 시나리오: `CmdReportFall`을 안 보내면 30 m 낙하도 무피해.
- 제안: 관측 착지 후 창 안에 보고가 없으면 `observed - 허용치`로 직접 피해, 하한도 `Max(보고값, 관측값 - 허용치)`.

### 9. 차량 충돌 보고 검증 (추정, Vehicle.cs는 마스터 편집 중이라 읽기만)
- 문제: `CmdCrash(speed)`는 60 m/s로만 자르고 탑승자 전원을 내리며 피해. 서버가 아는 `ServerSpeed`와 비교 안 함. `CmdImpact`도 빈도 제한 없음.
- 시나리오: 정지한 오토바이에서 운전자 클라가 `CmdCrash(999)` → 동승자 사망/기절. `CmdImpact(40)` 연타 → 정지 상태에서 화재·폭발.
- 제안: `speed = Min(speed, ServerSpeed + slack)`, `CmdImpact`에 기존 `RequestBucket`. 차량 머지 끝난 뒤 진행.

### 10. 인벤토리 RPC 빈도 제한 없음 (확인)
- 문제: 사격·투척·핑·이모트만 버킷이 있고, `CmdPickup`/`CmdDrop*`·투척물 선택·치료·조준경·장비·연료통 Cmd는 없다. 버릴 때마다 Instantiate+Spawn이 관심 범위 전원에게 복제.
- 시나리오: 변조 클라가 `CmdDropAmmoHalf`↔`CmdPickup`을 초당 수천 번 → 서버 CPU·주변 클라 대역폭 소진.
- 제안: 이미 있는 `NotFlooding(ref ...)` 버킷 하나를 인벤토리 Cmd 전부가 공유.

### 11~18 (짧게)
- **11 토큰 대기 vs 접속 제한**: `/api/auth/token`(타임아웃 8초)을 기다리는 동안에도 6초 접속 제한이 흐른다 → 백엔드 콜드 스타트 때 정상 서버를 "연결 불가"로 표시. 제안: 토큰 대기 중엔 제한 연장. S.
- **12 시드 고정**: World 씬 `_seed: 12345`, `SeedOverride`는 `--perf-seed` 때만. ADR-066은 봉쇄 시드만 판마다 바꿨다 → 모든 판 루팅 배치·조준경 몫·봇 이름·연료가 같다(외울 수 있음). 의도인지 ADR로 정한 뒤 `OnMatchStarting`에서 무작위 시드 + 로그. S.
- **13 KeyElevatorNet.LastStatus**: 앱 시작 때만 비움. 클라 프로세스에서 2판째에 이전 판 역 상태(Selecting, Holder=57)가 남아, 내 ObjectId가 57이면 목적지 UI가 뜬다. 제안: Awake/OnDestroy에서 Clear. S.
- **14 기절 검사**: `ServerStartParkour`는 Dead만, `ServerKeyRide`·`ServerSelect`는 Knocked 미확인 → 기절한 원격 플레이어가 담을 넘거나 열쇠 승강기로 이송. 일반 탑승(`ElevatorRides.cs:314`)은 막고 있어 서로 안 맞음. S.
- **15 LookBroadcast**: 인증 없이 받은 문자열을 길이 검사 없이 SyncVar<string>로 전원에게 복제, `_looks[ClientId]`는 연결 해제 때 안 지움 → 재사용된 ClientId가 이전 사람 외형을 물려받음. 제안: `PlayerAppearance.Parse(look).Serialize()`로 정규화 + 해제 시 Remove. S.
- **16 /api/me 재시도 없음**: 런처 시작 순간 백엔드 재배포 중이면 `AccountId=0`으로 세션 내내 파티 리더 UI 숨김. 제안: 0이면 10초마다 재시도. S.
- **17 BackendMatchPoller 파싱 예외**: 프록시 점검 페이지가 200으로 오면 `JsonUtility.FromJson` 예외가 `DedicatedServer.Update` 맨 앞에서 나서 그 프레임 TickLobby/TickMatch가 건너뛰어짐(3초마다). 제안: Apply를 try/catch + 경고. S.
- **18 LAN 호스트 재시작**: 판 결정 전에도 죽은 게스트의 재시작 요청을 받아 호스트가 씬을 다시 로드 → 살아 있는 사람 판이 날아감. 데디는 막혀 있음. #462(MatchConfig)에서 같이 처리.

### 19. PlayerController.Update 한 함수가 너무 많은 일 (구조)
- 문제: `Update` 하나(203~780줄)가 입력 읽기 → 화면/결과창/승강기/휠/투척/기절/업힘 입력 차단 8단 → 죽음 → 파쿠르 → 스탠스 → 수영 → 이동 → 줍기를 한다. 조기 return 5곳이 각자 `SendStance/SendBodyStance/SendSlowWalk/PlaceCamera`를 손으로 반복한다.
- 시나리오: 새 상태(예: 사다리)를 추가할 때 return 하나에서 `Send*` 하나를 빠뜨리면 원격에서 자세가 안 바뀐다. 실제로 턱 잡기(`:462`)·수영 탈출 return은 `ToggleView`·`NearbyLoot` 갱신을 건너뛴다(지금은 다음 프레임이 메워서 무해).
- 제안: 입력 차단 체인을 Core 순수 함수 `InputGate.Filter(frame, state)`로 빼고 EditMode 테스트(어떤 상태에서 어떤 키가 죽는지 표로). 상태 송신·카메라는 한 출구(`EndFrame()`)로. 동작 변경 없이 M.

### 20. "WaitingIsland" 문자열 계약 + Net 안의 화면 코드 (구조)
- 문제: 서버의 기능 판정(`WaitingRoom.TryCreate`)이 `GameObject.Find("WaitingIsland")`에 달려 있고, 같은 이름 찾기가 런타임 6곳(WaitingHud 3, WaitingRoom, BungeeRide, CombatantAnimation.Bungee)에 흩어져 있다. 또 `WaitingHud`(화면·케이지 생성)가 Net 어셈블리에 있어, 클라가 만든 충돌체를 서버 판정이 쓰는 #464류 버그가 생기기 쉽다.
- 시나리오: 빌더에서 루트 이름을 바꾸면 데디 서버가 경고 한 줄만 남기고 대기섬 없이 판을 시작한다.
- 제안: `WorldBuilder.Waiting`이 루트에 마커 컴포넌트(`WaitingIslandRoot`, 정적 `Current`)를 붙이고 6곳이 그걸 읽게. 화면 부분은 Gameplay로 옮김(#464 수정 때 같이). S~M.

### 21. UI 빌드 도우미 두 벌 (구조, 낮음)
- `UiKit`(Rect/Box/Label)과 `UiPaint`(Box/Label)가 같은 일을 하고, `Hud`·`PauseMenu`·`VehicleHud`가 `Panel/Label`을 또 따로 가진다. 글꼴·외곽선 바꿀 때 4곳을 고쳐야 한다. 하나로 합치기(S), 급하지 않음.

## 확인했고 문제없던 것
- 정적 상태: `[RuntimeInitializeOnLoadMethod]` 리셋 82곳, 정적 이벤트 구독/해제 수 일치, 싱글턴 OnDestroy는 대부분 `Instance == this` 확인. 1·5·13번 말고는 판 사이 누수 없음(시드 오버라이드·`RulesOverride`·`EventRace`·`LootGrid`·`BotController._points`·`CurrentMatchId` 리셋됨).
- 네트워크: RPC 인자 검증(슬롯·종류·NaN·거리·IsSpawned) 대부분 있음, 서버 예외를 낼 핸들러 없음, 클라가 SyncVar를 쓰는 곳 없음. 파쿠르는 Cmd→Server 같은 함수(좋은 패턴). 차량 `RequireOwnership=false` RPC 6개는 좌석·소유 확인.
- 전역 난수: Core에서 `UnityEngine.Random`·시드 없는 `System.Random` 없음. 타임스탬프 SyncVar는 서버 시계 기준.
- rules.json: 아무 코드도 안 읽는 키 없음. JSON에 없는 블록(Bot.* ~30개, Ballistics/Penetration/VehicleDamage 전체 등)은 코드 기본값으로 돈다(튜닝 대상에서 빠져 있다는 점만 기록).
- 비밀키: 런타임 코드·Resources에 하드코딩 없음. `Tests/EditMode/GameTokenTests.cs`의 테스트 시크릿이 운영값과 다른지만 사람이 확인.

## 안 본 것
AI 8개 파일 상세, Gameplay UI 대부분(InventoryScreen 1,675줄 포함), DoorSystem·PollutionDirector·Ballistics·LagCompensation·ThrownItem 내부, Vehicle.cs 1~1050줄(마스터 편집 중), Editor 빌더 대부분, 백엔드 저장소. Unity·테스트 실행 안 함.

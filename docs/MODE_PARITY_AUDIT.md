# 모드 동등성 감사 (봇 연습 ↔ 솔로/스쿼드 멀티)

작성 2026-11-22, 기준 커밋 97ff434 (v0.3.0 / protocol 8). 준환 요구: **"게임 운영 방식만 다를 뿐 맵과 캐릭터의 상호작용은 동일해야 한다."**

## 0. 요약

- 봇 연습과 멀티는 **같은 씬(World)·같은 MatchDirector·같은 네트워크 코드**를 쓴다. 다른 것은 **토폴로지**다: 연습은 Fish-Net **호스트**(서버+내 클라가 한 프로세스, 내 몸 `IsServerInitialized == true`), 멀티는 **전용 서버 + 원격 클라**.
- 그런데 입력 코드 곳곳이 `if (IsServerInitialized) ServerX(); else CmdX();` 형태라, **연습에서는 원격 경로(요청 → 서버 검증 → 복제 → 클라 적용)가 한 번도 실행되지 않는다.** 연습에서 잘 되는 것은 멀티 경로가 맞다는 증거가 아니다. 이것이 "모드마다 조금씩 다르다"의 구조적 원인이다.
- 테스트도 같은 맹점: 기존 멀티 테스트(`MultiHarness`)는 전부 **호스트 + 원격 클라**이고, 한 프로세스라 문·유리 같은 씬 객체를 **양쪽이 공유**한다. 원격 클라가 받은 메시지를 자기 객체에 적용하는 코드는 실행되지 않는다.
- 문: 현재 HEAD를 **실제 서버 빌드(별도 프로세스, `--dedicated -batchmode -nographics`) + 원격 클라**로 돌린 `DedicatedServerDoorPlayTests`는 **통과**한다(서버·클라 문 1834개, F로 열림, 100 ms/2 % 손실). 즉 **깨끗한 1인 경로는 동작**한다. 실서버에서 안 되는 원인은 **이 테스트가 덮지 않는 조건**에 있다(§2.4). 서버가 F를 거절할 때 아무 로그도 남기지 않아 실서버 저널로 원인을 가릴 수 없었다 → 이번에 **거절 사유 로그**를 넣고, 호스트도 같은 검증을 거치게 했다.

- 같은 구조에서 나온 **확실한 멀티 전용 버그 2건을 고쳤다**: (1) 전용 서버에서 **수영·익사가 아예 꺼져 있었다**(서버가 클라 전용 "엘리베이터 문 닫힘"을 읽어 매치 내내 "갇힘"으로 봄), (2) **죽은 원격 플레이어 누구나 서버 매치 전체를 리로드**할 수 있었다(재시작 요청). 나머지는 §4 순위표와 이슈.

## 1. 진입 경로별 배선

| | 봇 연습 (`MainMenu.StartGame`) | 훈련장 (`StartTraining`) | LAN 방 호스트 (`LobbyNet.StartHost`) | 전용 서버 (`DedicatedServer`) | 원격 클라 (LAN 참가 / 큐 매치) |
|---|---|---|---|---|---|
| 씬 | `World` | `TrainingRange` | 로비(Bootstrap 내 LobbyNet) → `World` | `Bootstrap`(로비) → `World`, 끝나면 `Bootstrap` | 로비 → `World` (이벤트면 코스 씬) |
| 프로세스 역할 | 호스트(서버+클라) | 호스트 | 호스트 | 서버만 (`NetSession.Dedicated = true`, `DedicatedServer.cs:189`) | 클라만 (`NetSession.Role = Client`) |
| NetSession | Role=Single | Single | Role=Host, Players/Bots/PartySizes = 로비 값 | Role=Host + Dedicated | Role=Client, Address/Port = 매치 포트 |
| 연결 시작 | `ServerManager.StartConnection` + `ClientManager.StartConnection` (`MatchDirector.cs:156-157`) | 같음 | 같음(포트=로비가 알린 포트) | 서버만 (`!NetSession.Dedicated`일 때만 클라 시작, `MatchDirector.cs:157`) | `ClientManager.StartConnection(Address, Port)` + 60초 재시도 (`MatchDirector.cs:138-146`) |
| MatchDirector.Start에서 붙는 것 | MatchState, ElevatorDoors, GlassPanes, DoorSystem, SkySync, WaitingHud, InterestCondition | 같음 | 같음 | **WaitingHud 없음**(`:134`) | MatchState·Elevator·Glass·Door·Sky(클라 쪽 등록), WaitingHud, SessionHelloSender, PingSimulation |
| 서버 시작 시(`OnServerState`) | SessionKeeper, NetPulse, 배치, **WaitingRoom = null**, 루트·봇 스폰 | 같음(봇·봉쇄 없음) | 같음, WaitingRoom = null | **WaitingRoom 생성**(`WaitingRoom.cs:125`: `NetSession.Dedicated || ForceEnabled`) → 대기섬·번지·사격장·레이스 | (서버 없음) |
| 플레이어 첫 위치 | 엘리베이터 | 스폰 포인트 | 엘리베이터 | **대기섬** → 카운트다운 후 `ServerDeployFromIsland`로 엘리베이터 | 서버가 정함 |
| 팀 모드 | 메뉴 선택 | Solo | 로비 모드 | `_lobby.StartMode` (`DedicatedServer.cs:183`) | `StartLobbyMatch`가 로비 모드로 설정 (`MainMenu.cs:228`) |
| 봇 수 | `MatchRules.BotsFor(1)` | 0 | `LobbyRules.ClampBots(NetSession.Bots)` | 같음(로비/`--bots`) | — |
| 문 대기 | 첫 플레이어 들어오면 열림 예약 | — | 모두 들어오면(60초 상한) | 대기섬 카운트다운 후 (`ServerOpenDoorsAfterWaiting`) | — |

토폴로지에 묶인 분기(전수, `grep`):

| 위치 | 분기 | 영향 |
|---|---|---|
| `MatchDirector.cs:134` | `!NetSession.Dedicated` → WaitingHud | 서버엔 화면이 없으니 정상 |
| `MatchDirector.cs:157` | `!NetSession.Dedicated` → 로컬 클라 | 토폴로지 그 자체 |
| `WaitingRoom.cs:125` | `NetSession.Dedicated || ForceEnabled` → 대기섬 | **룰이 토폴로지에 묶임.** 대기섬은 "운영 방식"(룰)인데 "서버가 전용인가"로 켜진다. LAN 호스트 멀티에는 대기섬이 없고, 연습에서 대기섬 관련 상호작용(리프트, 번지, 사격장)을 검증할 수 없다 |
| `Door.cs:86` | 그래픽 Null → 콜라이더만 | 시각만 생략, 상태·콜라이더 동일 (ADR-481/482) |
| `CombatantAnimation.cs:492`, `InteriorLights.cs:39` | 그래픽 Null → 시각 생략 | 시각만 |
| `PlayerController.cs:173` | batchmode → 실제 입력 안 읽음 | 서버엔 입력 없음, 정상 |

## 2. 문, 끝에서 끝까지

### 2.1 경로

| 단계 | 봇 연습 (호스트) | 멀티 (전용 서버 + 원격 클라) |
|---|---|---|
| 입력 | `PlayerController.cs:483-488` → `DoorSystem.TryInteract` | 같음 |
| 대상 고르기 | 클라 자기 문 목록 `Pick(eye, look)` (`DoorSystem.cs:173`) | 같음 (클라가 시드로 만든 자기 목록) |
| 서버로 | **없음**: `me.IsServerInitialized` → 바로 `ServerToggle` (수정 전 `DoorSystem.cs:190`) | `DoorToggleRequest{Index, At}` 브로드캐스트, 자기 몸의 NetworkManager로 |
| 서버 검증 | **없음** (거리 검사 없음, 번호=같은 목록) | `OnRequest`: `connection.FirstObject`의 Combatant, `Resolve(Index, At)`(번호·위치), 죽음, **서버가 보는 몸 위치**에서 평면 3.5 m 이내 |
| 상태 | `door.Set` | 같음 |
| 복제 | `DoorChanged` 브로드캐스트 (호스트 자기 클라는 상태가 같아 무시) | `DoorChanged{Index, At, State}` 모든 인증 클라 |
| 클라 적용 | (공유 객체) | `OnChanged` → `Resolve` → `Set` → `Update`가 회전, `Door.Moved` → DoorAudio |
| 실패 시 | — | **조용히 버림, 로그 없음** (수정 전) |

### 2.2 증거

- `DedicatedServerDoorPlayTests.RemoteClient_OpensAMapDoor_OnTheRealDedicatedServer`: 이 워크트리에서 Mac 서버 빌드(`tools/unity.sh build server`, 2026-11-22)를 별도 프로세스로 띄워 **통과**. 서버 로그 `[Doors] 1834 doors built`, 클라 `doors: server 1834, client 1834`, 문 838을 F로 열었다.
- 즉 프로토콜·`Resolve`·클라 적용은 1인·텔레포트 접근 조건에서 맞다. ADR-480(번호 불일치)·ADR-482(헤드리스 문) 수정은 유효하다.

### 2.3 근본 원인 (구조)

1. **연습은 멀티 경로를 실행하지 않는다**: 호스트 단축 경로는 검증도 복제 적용도 건너뛴다. 연습에서 "된다"는 것은 원격 경로에 대해 아무것도 증명하지 않는다. 문 외 같은 패턴 20여 곳(§3).
2. **서버 거절이 보이지 않는다**: `OnRequest`의 네 가지 거절(몸 없음, 죽음, 위치에 문 없음, 거리 초과)이 로그 없이 `return`. r5부터 세 번 "문이 안 열린다"가 왔지만 실서버 저널로 어느 단계인지 가릴 수 없었다.
3. **테스트가 같은 맹점을 갖는다**: 문 테스트(`DoorPlayTests`, `DoorBreakPlayTests`, `DoorCaptureTests`)는 호스트 전용, `ReplicationWorldTests`의 원격 문 테스트는 호스트+원격이고 공유 객체를 본다. 전용 서버 토폴로지는 Docker/빌드가 필요한 무거운 테스트 하나뿐.

### 2.4 실서버에서 남은 가설 (테스트가 덮지 않는 조건, 확신 순)

| # | 가설 | 근거 | 확신 |
|---|---|---|---|
| A | **서버가 보는 몸 위치가 클라와 다르다** (엘리베이터 상승·번지·대기섬→엘리베이터 이동 직후, MoveGuard 보정 대기 중) → 거리 검사 실패 | ADR-482가 같은 경합(섬 위치로 판정)을 지적, 로컬 100 ms에서 재현 안 됨. 테스트는 걷지 않고 텔레포트로 접근 | 중 |
| B | 릴리스 클라(IL2CPP, Win/Mac)와 Linux ARM64 서버의 문 목록이 1 m 넘게 어긋난 건물 → `Resolve` -1 | ADR-480. 테스트는 에디터(Mono) 클라 + Mac 서버. Linux Docker 통과 기록은 8abaa4e 시점 | 낮~중 |
| C | 80인 매치·관심 관리·봇 문 열기와의 상호작용(동시에 봇이 토글) | 테스트는 봇 2, 최대 4인 | 낮 |
| D | 클라가 v0.3.0 이전 빌드 | ADR-482 사례 | 낮(준환 확인 필요) |

→ 이번 수정의 거절 로그 `[Doors] refused F from <이름>: <사유>`가 다음 실서버 플레이에서 A/B/C를 바로 가린다(서버 몸 위치와 거리, 해석 실패 위치를 찍는다).

## 3. 호스트 전용 가정 — 패턴

| 패턴 | 왜 연습에선 안 보이나 | 대표 위치 |
|---|---|---|
| ① 입력 단축 `if (IsServerInitialized) ServerX() else CmdX()` 중 **Cmd 쪽에만 검증** | 호스트는 검증을 건너뜀 → 원격만 거절당함 | 문 `DoorSystem.cs:190`(수정), 사격 `PlayerController.cs:840`↔`FireRequestOk`, 펀치 `:864`, 투척 `ThrowableView.cs:338`↔`ThrowRequestOk`, 차 문 들이받기 `Vehicle.cs:645`↔`OnRam`(40 m/s 상한·8 m) |
| ② **서버가 클라 전용 상태를 읽음** | 호스트 서버 = 내 클라라 값이 채워져 있음. 전용 서버엔 클라가 없어 기본값 | 수영 `CombatantSwim.cs:57` → `SpawnProtection.InputBlocked` → `ElevatorDoors.DoorsClosed`(클라 스케줄, `ElevatorDoor.cs:162`)(수정) |
| ③ **클라 전용 객체에만 있는 충돌체** | 호스트 서버는 클라가 만든 충돌체도 물리에 가짐 | 번지 리프트 케이지 `LiftHoist.cs:35-42`(WaitingHud가 만듦, 전용 서버엔 없음 `MatchDirector.cs:134`) |
| ④ **모드 룰이 토폴로지에 묶임** | 연습엔 그 룰이 아예 없음 | 대기섬 `WaitingRoom.cs:125`, 재시작 `MatchDirector.Restart`(수정) |
| ⑤ **원격만 받는 제한**(관심 관리) | 호스트 프로세스엔 모든 객체가 있음 | 원거리 총성(750 m) vs 전투원 관심 300 m `Core/Interest.cs:18-22`, `SpatialAudio.cs:26-27` |
| ⑥ **피어마다 따로 계산하는 목록·그리드** | 호스트는 한 프로세스라 어긋날 수 없음 | 문 번호(ADR-480에서 위치로 해결), 오염 그리드 `PollutionWorld.cs:23-35`(해시 불일치는 로그만 `PollutionDirector.cs:256`), 문 파손 무늬 시드 `Door.Index` |
| ⑦ **원격 몸만 감시** | 호스트 자기 몸엔 MoveGuard 없음 `Combatant.cs:343` | 위치 교정·섬 경계는 원격만 겪음 |

## 4. 순위표

| # | 기능 | 연습(호스트) | 멀티(전용+원격) | 원인 | 근거 | 확신 | 상태 |
|---|---|---|---|---|---|---|---|
| 1 | **수영·익사** (봇 포함) | 정상 | 서버가 수영으로 안 봄: 익사 피해 없음, 물속 무기 안 넣음, 물속 사격·투척 허용, 봇 수영 안 함. 원격 화면엔 숨 게이지만 줄어듦 | ② 서버가 클라 전용 `DoorsClosed`를 읽음(전용 서버에선 매치 내내 true) | `CombatantSwim.cs:57`, `SpawnProtection.cs:35`, `ElevatorDoor.cs:162` | 높음 | **수정** `SpawnProtection.HeldFor` |
| 2 | **재시작** | 의도대로 새 매치 | 죽은 원격 플레이어 아무나 Enter/결과창 "다시 시작"으로 **서버 매치 전체 리로드**(전원 끊김) | ④ `Restart()`가 모드를 안 봄 | `Combatant.cs:912-915`, `MatchState.cs:405-416`, `MatchDirector.cs:514` | 높음 | **수정**(전용 서버는 거절. 결과창 버튼은 남음 → 이슈) |
| 3 | **문 열기/닫기** | 됨(검증 없음) | 실서버 보고: 안 됨. 깨끗한 1인 E2E는 통과 | ① 검증 경로가 원격에만 있고, 거절이 무로그 | `DoorSystem.cs:190, 296-304` | 구조: 높음 / 실서버 원인: 미확정(§2.4) | **수정**(단일 검증 `ServerRequest` + 거절 로그 + 이중 토폴로지 테스트) |
| 4 | 사격·펀치·투척 검증 | 검증 없이 즉시 | `FireRequestOk`/`ThrowRequestOk` 검증 | ① | `PlayerController.cs:840, 864`, `ThrowableView.cs:338` | 높음 | 이슈 |
| 5 | 내 사격 피드백 | 즉시 반동·총구화염·소리 | RTT 뒤, 서버 거절 시 무반응. 원점은 서버의 지연된 `EyePosition` | 피드백이 `RpcShotFx`(RunLocally)에만 | `Combatant.cs:919-960, 437` | 지연: 높음 / 원점: 중 | 이슈 |
| 6 | 차로 문 들이받기 | 상한·거리 검사 없음 | 40 m/s 상한, 8 m | ① | `Vehicle.cs:645` ↔ `DoorSystem.OnRam` | 높음 | 이슈(#4와 함께) |
| 7 | 원거리 총성·적 핑·"전체 N팀" | 615~800 m까지 들림/판정 | 관심 300 m(투척 200 m) 밖은 없음 | ⑤ | `Core/Interest.cs:18-22`, `SpatialAudio.cs:26-27`, `TeamHud.cs:340`, `MatchState.Team.cs:52` | 중(호스트에서 Fish-Net이 숨긴 객체를 어떻게 다루는지 미확인) | 이슈 |
| 8 | 번지 리프트 케이지 충돌 | 서버 탄도·수류탄·파쿠르가 케이지를 봄 | 서버엔 케이지 없음(관통·끼임) | ③ | `LiftHoist.cs:6, 35-42`, `WaitingHud.cs:146-162` | 중 | 이슈 |
| 9 | 오염 안개 vs 피해 | 일치 | ARM64 서버·클라 float 차이로 경계 어긋날 수 있음(문 번호와 같은 원인) | ⑥ | `PollutionWorld.cs:23-35`, `PollutionDirector.cs:256` | 중 | 이슈 |
| 10 | 위치 교정·섬 경계 | 내 몸 감시 없음 | 원격만 교정 | ⑦ | `Combatant.cs:343` | 중 | 이슈(#4와 함께) |
| 11 | 차량 파손 파편 | — | 전용 서버도 충돌체 파편 생성, 피어마다 다른 위치 → 서버 레이가 안 보이는 파편에 맞음 | RunLocally RPC에 `IsClientStarted` 가드 없음 | `VehicleDamage.cs:401-411`, `VehicleDamageVisual.cs:651-655` | 낮~중 | 이슈 |
| 12 | 대기섬 아이템 잠금 | (대기섬 없음) | 치료제·연료통 버리기, 장비 탈착은 잠금 안 걸림 | 형제 시스템 누락 | `CombatantHealing.cs:336-338`, `CombatantJerrycan.cs:227-229`, `CombatantGear.cs:125-132` | 낮 | 이슈 |
| 13 | 로드킬 판정 | 운전자=서버 | 서버는 보간 사본으로 판정, 통과 처리는 운전자 클라 | 클라 권한 차량 | `Vehicle.Roadkill.cs:33-62` | 낮 | 이슈 |
| 14 | 문 파손 무늬 | 같음 | 피어마다 다를 수 있음(외관만) | ⑥ 시드가 `Door.Index` | `Door.cs` ShowDamage | 낮 | 메모 |

문제 없음 확인: 유리(씬 직렬화 id), 엘리베이터·`ElevatorRides`(서버/클라 시계 분리), 보급 상자(서버 id, 같은 `ServerTryOpen`), 사격장 표적, 루트 줍기(호스트도 `CmdPickup`), 장비 외형(SyncVar), 핑·감정표현·업기·부활, 치료·연료통·가스 커버, 투척 이펙트(`IsClientStarted` 가드), 결과(MatchState 브로드캐스트), DoorAudio, 봇(AI가 클라 상태를 안 읽음), SkySync. 깊이 안 본 것: AmbientBeds, CombatantAudio 세부, 레이스 이벤트, BungeeRide 내부.

## 5. 구조적 해결: 한 게임플레이 층, 모드는 설정

원칙: **모든 상호작용은 "요청 → 서버 검증 함수 하나 → 상태 → 복제 → 모든 클라가 같은 적용 함수"로만 흐른다. 호스트의 내 몸도 예외 없이 같은 서버 검증 함수를 부른다.** 모드는 그 위의 룰(봇 수, 대기섬, 봉쇄, 결과 보고)만 바꾼다.

1. **입력의 단일 입구** — `if (IsServerInitialized) ServerX(); else CmdX();`를 금지하고, 서버 진입점 하나(`ServerRequestX(Combatant who, ...)`)를 두어 호스트는 직접, 원격은 RPC/브로드캐스트 핸들러가 **같은 함수**를 부른다. 검증(거리·생존·쿨다운)은 그 함수 안에만. 문은 이번에 이렇게 바꿨다(`DoorSystem.ServerRequest`). 리프트 호출(`WaitingRoom.ServerCall`)은 이미 이 모양이다.
2. **적용의 단일 출구** — 서버가 상태를 바꾸면 그 변경은 SyncVar/브로드캐스트로만 시각에 반영하고, 호스트도 자기 브로드캐스트 핸들러(또는 SyncVar 콜백)로 시각을 갱신한다. "서버 쪽에서 직접 시각 변경 + 원격용 메시지 따로"를 없앤다.
3. **모드 = 데이터** — `MatchConfig`(가칭) 하나: `UseWaitingIsland`, `BotCount`, `Lockdown`, `ReportResults`, `TeamMode`, `ExpectedPlayers`. 연습·LAN·전용이 이 값만 다르게 채운다. `WaitingRoom.cs:125`의 `NetSession.Dedicated` 조건을 `MatchConfig.UseWaitingIsland`로 바꾸는 것이 첫 대상. 토폴로지(`Dedicated`)는 "로컬 클라를 띄우나"에만 쓴다.
4. **거절은 보인다** — 모든 서버 검증 실패는 이유와 함께 한 줄 로그(`ServerGuard.Refuse` 패턴). 실서버 저널이 곧 진단 도구가 된다.

## 6. 회귀 방지: 이중 토폴로지 하네스

- `MultiHarness.Topology { Host, Dedicated }`와 `MultiHarness.Start(topology, scene)` 추가. `StartDedicated`는 같은 프로세스에서 `NetSession.Dedicated = true`로 씬을 띄워 **로컬 클라 없는 서버**를 만들고, 플레이어는 전부 `AddClient`(원격 NetworkManager)로 붙는다.
- 상호작용 테스트는 `[Values] MultiHarness.Topology topology`로 **두 토폴로지 모두** 돈다. 입력은 원격 클라의 `PlayerController`에 넣고(사람이 누르는 경로), 결과는 (a) 서버 상태, (b) **원격 클라 NetworkManager에 붙인 리스너가 받은 메시지**(공유 객체 맹점 회피)로 본다.
- 첫 적용: `ModeParityPlayTests` — 문 열기/닫기(두 토폴로지), 거리 밖 요청 거절(두 토폴로지), 전용 서버에서 죽은 플레이어의 재시작이 매치를 리로드하지 않음.
- 별도 프로세스 검증은 `DedicatedServerDoorPlayTests`(실서버 빌드)로 유지. 다음 대상(이슈로 등록): 차량 탑승/하차, 루트 줍기, 치료, 투척, 핑/감정표현/업기, 유리 파괴, 보급 상자.
- 규칙 제안(WORKFLOW에 추가할 것): **새 상호작용은 `ModeParityPlayTests` 스타일의 두 토폴로지 테스트 없이는 머지 금지.**

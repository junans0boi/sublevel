# 네트워크

> 상태: 확정 · 요약: Fish-Net 4.7.3/Tugboat 30 Hz, 소유자 권위 이동+서버 권위 전투, 관심 영역, 자체 LagComp, 전용 서버, 대역폭 실측(16인)과 80명 요구·위험.

## Fish-Net 구성

| 상태 | 항목 | 값 | 설명 | 근거 |
|---|---|---|---|---|
| 확정 | 버전 | Fish-Net 4.7.3 (무료판, git 패키지) | Pro 기능(ColliderRollback 등)은 본문이 빈 스텁이라 쓰지 않는다. Pro 구매는 규칙 3상 하지 않는다(ADR-121). | Packages/manifest.json:3 |
| 확정 | 전송 | Tugboat(UDP) | 로비 포트 N(기본 7770), 매치는 N+1(쓰는 중이면 빈 포트). 방화벽 포트 2개. | Assets/_Project/Scripts/Net/LobbyNet.cs:8,195; Assets/_Project/Scripts/Net/MatchDirector.cs:132 |
| 확정 | 틱레이트 | 30 Hz(보간 2틱) | Fish-Net 기본값을 쓴다. 설정 파일에서 바꾼 흔적은 찾지 못함(코드 검색 결과 없음). NetPulse가 서버 틱을 클라이언트에 알려 F3 줄에 표시한다. | docs/DECISIONS.md:136 (ADR-126) |
| 확정 | 방 구조 | 한 프로세스 = 방 1개 = 매치 1개 | 로비(NetworkManager 하나, Broadcast만, 스폰 없음)와 매치(World 씬, 별도 NetworkManager)가 분리돼 있다(ADR-124). | docs/DECISIONS.md:137 (ADR-127); docs/DEDICATED_SERVER.md |
| 확정 | 호스트 모드 | 서버+클라이언트 한 프로세스 | 싱글도 같은 구조(서버 권위 구조, SPEC §2). 전용 서버는 로컬 플레이어 없이 `--dedicated`. | Assets/_Project/Scripts/Net/MatchDirector.cs:121-145 |

## 권위 모델

| 상태 | 영역 | 누가 결정 | 설명 | 근거 |
|---|---|---|---|---|
| 확정 | 플레이어 이동 | 소유자 클라이언트(소유자 권위 NetworkTransform) | 소유자 몸이 곧 예측. Fish-Net 예측/서버 재생은 쓰지 않음(이동 코드가 점프·수영·파쿠르·차량 좌석 등과 얽혀 있어서). 서버 MoveGuard가 0.25초마다 위치를 검사해 의심스러우면 되돌린다(수평 (6.3+12)x1.2 m/s, 수직 60 m/s, 여유 2.5 m). | docs/DECISIONS.md:136 (ADR-126) |
| 확정 | 사격·피해 | 서버 | 클라이언트는 방향+viewTick만 보낸다. 탄도·관통·피해는 서버 계산. 연사 속도 상한 무기 x1.5+버스트 4. | docs/DECISIONS.md:151-152 (ADR-141,142) |
| 확정 | 봇 | 서버만 | 봇은 서버에서만 시뮬한다. 감지 레이캐스트 0.15초 주기, NavMesh 서버. | Assets/_Project/Scripts/AI/BotController.cs:19; docs/research/MASTER_PLAN.md:3 |
| 확정 | 루팅·인벤토리 | 서버 | LootItem NetworkObject(SyncVar), 줍기 거리 3 m + 지연 여유 1.5 m 서버 검사. | docs/MP_AUDIT_WORLD.md:18 |
| 확정 | 구역·날씨 | 서버가 시드·시각을 정함 | ZoneUpdate, SkyState 방송. 클라이언트는 받은 시드로 같은 하늘·안개를 계산(ADR-131). | docs/MP_AUDIT_WORLD.md:15-16 |
| 확정 | 차량 | 서버(상태 복제) | 차량은 되감기 제외(ADR-141). 상세는 MP_AUDIT_VEHICLES.md 참고. | docs/MP_AUDIT_VEHICLES.md |

## 복제되는 것 (수단별)

| 상태 | 수단 | 내용 | 특징 | 근거 |
|---|---|---|---|---|
| 확정 | NetworkObject + SyncVar | 전투원(플레이어·봇), 아이템, 차량, 투척물(연막·불), 보급 상자 등 | 관심 영역 필터를 받는다. 늦게 들어온 클라이언트도 현재 상태를 받는다(투척물은 Effect/EffectLeft SyncVar). | Assets/_Project/Scripts/Net/Combatant.cs; Assets/_Project/Scripts/Net/LootItem.cs; Assets/_Project/Scripts/Net/Vehicle.cs; Assets/_Project/Scripts/Net/ThrownItem.cs |
| 확정 | ObserversRpc/ServerRpc/TargetRpc | 사격 효과(RpcShotFx), 근접, 차량 피해, 폭발, 줍기·사용 요청 | 객체에 실려 있어 관찰자만 받는다. 소리·효과 이벤트를 따로 거르지 않는다. | docs/DECISIONS.md:145 (ADR-135 (2)) |
| 확정 | Broadcast(전 클라이언트) | 문(DoorChanged), 유리(GlassShattered), 매치 상태, 구역(ZoneUpdate), 승강기 문·사이클, 보급(SupplyDropped), 하늘, 핑, 스폰 보호 | NetworkObject 없음, 관심 영역을 타지 않는다. 접속 시 현재 상태(열린 문·깨진 유리 포함)를 한꺼번에 보낸다. 총 약 54개 Broadcast 호출·29개 IBroadcast 구조체(코드 검색 계수). | docs/MP_AUDIT_WORLD.md:7-22 |
| 확정 | 신뢰성 없는 펄스 | NetPulse(번호, 서버 틱) 0.1초마다 | 클라이언트가 번호 빈 곳으로 패킷 손실을 계산, 핑은 Fish-Net RTT. | docs/DECISIONS.md:138 (ADR-128); Assets/_Project/Scripts/Core/NetStats.cs:48 |
| 확정 | 복제 안 함(로컬) | 총알 자국, 사망 표시, 발소리 간격, 반동·카메라, 소리 재생 | 영구 상태가 아니라 각 클라이언트가 만든다. 소리는 이벤트로 동기화하고 걸음 간격은 클라이언트가 계산(MASTER_PLAN §2-9, 제안). | docs/MP_AUDIT_WORLD.md:22 |

## 관심 영역(Interest Management) — 구현됨

| 상태 | 대상 | 반경(m) | 설명 | 근거 |
|---|---|---|---|---|
| 확정 | 전투원 | 300 | 총소리 200 m 까지 들리므로(FarLayerFull) 그보다 크게. | Assets/_Project/Data/Resources/GameData/rules.json:105 |
| 확정 | 차량 | 350 |  | Assets/_Project/Data/Resources/GameData/rules.json:105 |
| 확정 | 아이템 | 100 | 가장 큰 절감 요인(보이는 아이템 253 → 30). | Assets/_Project/Data/Resources/GameData/rules.json:105 |
| 확정 | 투척물(연막·불·폭발) | 200 |  | Assets/_Project/Data/Resources/GameData/rules.json:105 |
| 확정 | 깜빡임 방지 | 0 | 이미 보이는 객체는 반경 x1.2까지 유지(HideExtraFraction 0.2). | Assets/_Project/Data/Resources/GameData/rules.json:105 |
| 확정 | 항상 보냄 | 0 | 자기 소유, 같은 팀, 팀원이 탄 차량, NetworkObject 아닌 전역 상태. 죽은 클라이언트는 전투원·차량·투척물 전부 받고 아이템만 거리로 거른다. 조건은 시간형이라 Fish-Net이 약 0.5초 주기로 재검사(들어올 때 최대 0.5초+ 지연). | docs/DECISIONS.md:145 (ADR-135) |

## 지연 보상 (LagComp)

서버가 모든 전투원 위치를 링버퍼(240개, 약 1초)에 기록하고, 클라이언트가 보내는 viewTick(마지막 받은 서버 틱 - 보간 2틱)만큼 되감아 사격자 밖의 전투원을 그 시각 위치로 옮긴 뒤 레이캐스트하고 되돌린다. 되감기 상한 0.25초(왕복 약 180 ms까지). 호스트와 봇의 사격은 되감기 없음. 문·유리·차량은 되감지 않는다. 사격마다 Physics.SyncTransforms 비용이 든다(건물 콜라이더가 수만 개로 늘면 증가, 06 문서 I10 — 영향 낮음, 관찰).


## 전용 서버 문서 요약

빌드: `tools/unity.sh build server`(macOS Mono) / `server-linux-arm64`(오라클 ARM) / server-win은 미검증. 실행: `--dedicated --port 7770 --region C --start-seconds 60` + `-batchmode -nographics`(`--bots` 0..79·`--max-players` 1..64·`--participants` 2..80, 생략하면 rules.json Match). 끝: 결정 20초 뒤 / 모두 떠난 지 30초 뒤 / 150초간 아무도 안 옴 → 로비로 복귀. SIGTERM에 정상 종료. 잘못된 인자는 코드 2로 종료.

- 아직 확인하지 않은 것: Windows 서버 빌드·실행, 다른 기기 접속(방화벽·NAT), 두 사람 이상, 서버 CPU·메모리 예산(PERF_BUDGET에 서버 항목 없음), 장시간 운영.
- 한 프로세스에 방 하나라 80명 방을 여러 개 운영하려면 프로세스(포트 2개씩)를 여러 개 띄운다. 동시 방 수 계산법은 docs/DEDICATED_SERVER.md '동시 방 수 계산법'.

## 대역폭 실측 (16명 + 봇 20)

| 상태 | 조건 | 클라이언트당 B/s | 보이는 전투원/아이템/차량 | 근거 |
|---|---|---|---|---|
| 확정 | 관심 영역 끔 | 14719 | 36.0 / 253 / 10.0 | docs/PERF_BUDGET.md:145-157 |
| 확정 | 관심 영역 켬 | 8497 | 20.7 / 30 / 5.0 | docs/PERF_BUDGET.md:145-157 |
| 확정 | 7명 + 봇 20, 켬 | 6249 | 10.2 / 12 / 4.5 | docs/PERF_BUDGET.md:145-157 |
| 확정 | 봇 12 + 사람 4 한 매치 79초 평균 | 8300 | 클라이언트 4명 모두 약 8.1~8.2 KiB/s | docs/MP_AUDIT_MATCH.md:21-23 |
| 제안 | 80명(가정) | 미측정 | 80인 실측 없음. 옛 50명 외삽(12,000 B/s·서버 약 5 Mbit/s)은 폐기. 제안 한도: 클라당 ≤15 KB/s, 서버 송신 ≤6 Mbit/s(docs/research/server_perf_budget_proposal.md). | docs/research/server_perf_budget_proposal.md |

## 80명 요구와 위험 (계획)

| 상태 | 영역 | 위험/요구 | 제안 | 근거 |
|---|---|---|---|---|
| 제안 | 문·유리 방송 | 문·유리가 전 클라이언트 Broadcast이고 서버 문 리스트는 선형 탐색(ClosedBetween 등). 문 수천 개 x 봇 30마리가 0.25초마다 스캔. | DoorSystem 공간 해시 + 문·유리 방송을 건물 소속+관심 영역 필터로. | docs/research/06_missed_items.md:I9; Assets/_Project/Scripts/Net/DoorSystem.cs:170-223 |
| 제안 | 루팅 스폰 폭주 | 루팅 약 1,500개를 서버 시작에 전부 NetworkObject로 스폰하면 접속 직후 폭주. | 플레이어 접근 시 지연 스폰 또는 지역 단위. | docs/research/06_missed_items.md:I9 |
| 제안 | 봇 서버 CPU | 봇은 서버 전용 시뮬. 많을수록 CPU 증가(감지 레이캐스트 0.15초, NavMesh 경로). | 봇 AI LOD(먼 봇은 낮은 틱, 사람 없는 지역은 거의 정지). | docs/research/MASTER_PLAN.md:62-63 |
| 제안 | 늦은 접속 | 접속 시 열린 문·깨진 유리 상태를 한꺼번에 보내므로 건물 수에 선형. | 건물 단위 지연 전송. 전송량 측정. | docs/MP_AUDIT_WORLD.md:7-8 |
| 제안 | 엘리베이터 슬롯 | 승강기는 팀당 1기라 판의 봇 수가 승강기 수×팀 크기−사람으로 잘린다(ADR-260, MatchDirector 경고). 솔로 80명이면 80칸이 필요. | 현재 맵 승강기 수와 대조(서버 로그 '[MatchDirector] ... capped' 확인). | docs/DECISIONS.md ADR-260; Assets/_Project/Scripts/Net/MatchDirector.cs:307-309 |
| 제안 | 치트·정보 은닉 | 서버 판정·이동 속도 검증은 있음(ADR-142). 시야 밖 적 정보는 관심 영역 300 m 안이면 전송됨. | 시야 기반 은닉은 비용이 커서 우선순위 낮음(제안). | docs/research/MASTER_PLAN.md:67-68 |
| 제안 | 틱·대역폭 예산 | 목표값이 없다(MASTER_PLAN '근거 없음'). | 제안 한도 클라당 ≤15 KB/s·서버 송신 ≤6 Mbit/s(`tools/perf/perf_verdict.py`로 판정). 80인 실측 뒤 확정. | docs/research/server_perf_budget_proposal.md |
| 제안 | 물리 비용 | 건물 콜라이더가 많아지면 SyncTransforms·Raycast 비용 증가. | 건물당 콜라이더 수 상한 테스트. | docs/research/06_missed_items.md:I10 |

## 테스트 계획

| 상태 | 테스트 | 내용 | 근거 |
|---|---|---|---|
| 확정 | MultiHarness | 같은 프로세스에 원격 클라이언트 N개(localhost), 지연 100 ms RTT·손실 2% 주입. MultiClientSmokeTests 4개. | docs/DECISIONS.md:133 (ADR-123); Assets/_Project/Tests/PlayMode/MultiHarness.cs |
| 확정 | InterestBandwidthTests | 16+봇 20 대역폭 실측(PlayMode, 에디터 batchmode 한 프로세스). | docs/PERF_BUDGET.md:147 |
| 확정 | MatchDedicatedEndToEndTests | 서버(--bots 8 --max-players 4)와 클라이언트 1 별도 프로세스로 로비~World~복귀 250초 통과. | docs/MP_AUDIT_MATCH.md:38-45 |
| 확정 | ReplicationCombatTests 12개 | 원격 시점 사격·탄도·관통·투척·늦은 접속. | docs/DECISIONS.md:140 (ADR-130) |
| 제안 | 헤드리스 부하 시험(제안) | 헤드리스 클라이언트 N개(예 63+1)로 서버 CPU·대역폭·틱 지연 측정. 봇만 80인 측정은 `--perf p0\|p3 --participants 80 --bots 79`(docs/research/bot_perf_b1_2026-11-17.md). CI에는 최소 버전(예 8+봇)만. | docs/research/MASTER_PLAN.md:68 |
| 제안 | 도시 규모 재측정(제안) | InterestBandwidthTests를 건물 200동·루팅 1,500·봇 30 시나리오로 확장, 합격선은 ADR로. | docs/research/06_missed_items.md:I9,N6 |

## 정해야 할 것

- [x] **80명 서버 구조** (확정) → 프로세스 방당 1개 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 프로세스 1개에 80명 방 / 프로세스 여러 개(방당 1개)로 확장  · 추천: 프로세스 방당 1개
  - 근거: 현재 구조가 이미 한 프로세스=한 방. 서버 CPU 측정 뒤 동시 방 수 결정.
- [x] **틱레이트** (확정) → 30 Hz 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 30 Hz 유지 / 60 Hz / 20 Hz  · 추천: 30 Hz 유지
  - 근거: 80명에서 서버 비용을 줄이려면 올리지 않는다. 사격 판정은 LagComp 보정이 있으므로 30 Hz로 충분하다고 본다(추정, 측정 필요).
- [ ] **대역폭 목표** (미정) 
  - 선택지: 다운 15 KB/s 이하 / 측정 뒤 결정  · 추천: 측정 뒤 결정
  - 근거: 근거 없는 수치를 고정하지 않는다(MASTER_PLAN §3-5). 제안: 클라당 ≤15 KB/s(server_perf_budget_proposal.md). 실측: 16인+봇 20에서 8.5 KB/s. 80인 실측 뒤 사람이 확정(ADR-176).
- [x] **문·유리 방송 방식** (확정) → 건물 소속+관심 영역 필터 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 건물 소속+관심 영역 필터 / 전역 유지  · 추천: 건물 소속+관심 영역 필터
  - 근거: 06 문서 D10 권장.


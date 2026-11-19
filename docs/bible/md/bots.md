# 봇

> 상태: 확정 · 요약: 봇은 순수 규칙 두뇌(BotBrain) + 서버에서 NavMeshAgent로 걷는 컨트롤러다. 한 판은 사람+봇 80명(MinBots 16, 사람 최대 64), 이름은 가상 닉네임 풀, 난이도는 봇별 혼합. 차량 운전·창문/옥상 NavMeshLink·시야 기반 은닉은 없다(BACKLOG).

## 상태 (BotBrain)

| 상태 | 상태 | 하는 일 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | Roam | 관심 지점 사이를 돌아다님(반경 35m) | 지점 목록이 없으면 무작위. 45초 안에 못 가면 포기 | Assets/_Project/Scripts/Core/BotBrain.cs:31 |
| 확정 | Loot | 쓸모 있는 아이템으로 이동·줍기 | 무기가 없으면 최우선 | BotBrain.cs:31, 334-352 |
| 확정 | Investigate | 소리가 난 곳으로 가서 본다 | 12초 | BotBrain.cs:31, 372 |
| 확정 | Engage | 적을 보면 교전: 선호 거리 25m, 조준 안정 후 버스트 사격, 사격 중 좌우 이동 | 반응 0.45초 지연, 재장전은 시야 밖에서 | BotBrain.cs:31, 240-296 |
| 확정 | Retreat | 체력 35% 이하면 엄폐로 후퇴(20m, 최대 6초) | 후퇴해도 시선이 안 끊기면 반격 | BotBrain.cs:31, 220-231 |
| 확정 | Heal | 체력 60% 미만이고 안전하면 치료(1.5초) |  | BotBrain.cs:31, 314 |

## 우선순위

생존(후퇴·치료) > 보이는 적과 교전 > 무장 > 들은 곳 조사 > 쓸모 있는 것 줍기 > 배회 (BotBrain.cs:101-103). 같은 시드와 같은 입력이면 같은 결정을 하는 순수·결정적 코드라 EditMode 테스트로 숫자를 고정한다.

구역 봉쇄용 목표는 별도: None, Escape, SafeSubZone, ToFinalRegion, Hunt, Elevator (BotController.cs:52). 봉쇄 직전 구역은 배회 후보에서 뺀다(#77).


## 감각 (Normal 기준, rules.json Bot)

| 상태 | 항목 | 값 | 단위 | 메모 | 근거 |
|---|---|---|---|---|---|
| 확정 | 시야 거리 | 70 | m |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 시야각 | 110 | 도 |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 근접 감지(벽 뒤도) | 3 | m |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 감지 주기 | 0.15 | 초 |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 총소리 청취 | 80 | m |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 발소리 청취 | 14 | m |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 유리 깨지는 소리 | 40 | m |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 차량 경적 | 60 | m |  | Assets/_Project/Data/Resources/GameData/rules.json Fuel |
| 확정 | 적 기억 | 8 | 초 |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 반응 시간 | 0.45 | 초 |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 조준 오차(가까이 10m / 멀리 60m) | 1.2 / 2.6 | 도 |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |
| 확정 | 버스트 | 2~5발, 휴식 0.3~0.8초 |  |  | Assets/_Project/Data/Resources/GameData/rules.json Bot |

## 난이도 (BotDifficulty)

| 상태 | 단계 | 반응 시간 x | 조준 오차 x | 회전 x | 시야 x | 청각 x | 감지 주기 x | 근거 |
|---|---|---|---|---|---|---|---|---|
| 확정 | 쉬움 | 1.8 | 1.8 | 0.6 | 0.7 | 0.7 | 1.5 | Assets/_Project/Scripts/Core/BotRulesDifficulty.cs:7-27 |
| 확정 | 보통(기본) | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 | BotRulesDifficulty.cs:7,18 |
| 확정 | 어려움 | 0.55 | 0.55 | 1.5 | 1.15 | 1.2 | 0.7 | BotRulesDifficulty.cs:7-27 |

## 난이도 보충

시야각은 어려움 x1.05, 쉬움 x0.85(최대 170도). 버스트 휴식은 어려움 x0.7, 쉬움 x1.3. 투척 간격은 어려움 x0.7, 쉬움 x1.5. 설정 화면 값이 혼합의 중심이고, 봇마다 매치 시드+번호로 단계를 하나 뽑아 판 내내 고정한다(BotMix, ADR-245): 쉬움 설정 50/40/10%, 보통 20/60/20%, 어려움 10/40/50%(쉬움/보통/어려움). `--perf-level`이면 혼합 없이 모두 같은 단계(BotMix.Pure).


## 장비·아이템 사용 규칙

| 상태 | 항목 | 값 | 근거 | 근거 |
|---|---|---|---|---|
| 확정 | 줍기 반경 / 줍는 거리 | 25m / 2.2m |  | Assets/_Project/Scripts/Core/BotRules.cs:71-77 |
| 확정 | 원하는 양 | 탄약 120, 의료키트 3, 붕대+구급 4, 부스트 아이템 종류별 2, 투척 종류별 2 |  | Assets/_Project/Scripts/Core/BotRules.cs:71-77 |
| 확정 | 투척 선택 | 화염병은 정지한 적만; 섬광과 수류탄은 각자 거리 구간 안일 때, 둘 다 가능하면 무작위 50% | ADR-116 | Assets/_Project/Scripts/Core/BotRulesItems.cs:25-33 |
| 확정 | 투척 간격 | 6초 |  | Assets/_Project/Scripts/Core/BotRules.cs:90 |
| 확정 | 부스트 사용 | 시야 밖이고 체력이 기준 미만이거나 최근 위협이 있을 때 |  | BotRulesItems.cs:56 |
| 확정 | 재장전 | 탄 50% 미만, 적이 충분히 멀 때 시야 밖에서 | ADR-117 | BotBrain.cs:253 |
| 확정 | 팀 행동 | 아군 14m 안 따라감, 35m 넘으면 합류, 45m 안에서 소생 대상 탐색 | BotController.Team.cs | Assets/_Project/Data/Resources/GameData/rules.json Team |

## 이동·경로 (NavMesh 사실)

| 상태 | 사실 | 내용 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 서버에서만 NavMeshAgent를 붙여 걷는다 | 먼 목적지는 NavMesh.CalculatePath 코너를 한 점씩 따라감. 프리팹에는 에이전트 없음 |  | Assets/_Project/Scripts/AI/BotController.cs:16,34,868 |
| 확정 | NavMeshLink/OffMeshLink 0건 | 창문 넘기, 사다리, 옥상 진입 경로를 봇은 모른다. 계단은 걷는다. 경사 40도까지, 계단 높이 0.3m |  | docs/research/06_missed_items.md:59 |
| 확정 | 닫힌 문은 0.25초마다 열기 시도 | 막히면 포기하고 새 목표(#77) |  | BotController.cs:264 |
| 확정 | NavMesh는 지형+건물+바위+나무를 한 덩어리로 한 번에 구움 | 복셀 0.117m, 타일 256, 1km²에서 5.2MB | 도시·산으로 커지면 구움 시간과 파일 크기가 비선형으로 증가 | docs/research/06_missed_items.md:65 |
| 확정 | 엄폐 탐색이 단순함 | 위협이 못 보는 곳을 찾을 뿐, 바위·능선·건물 활용 로직 없음 |  | BotController.cs:984 |
| 확정 | 관심 지점이 도시가 커지면 한곳으로 수렴할 수 있음 | 권장: 건물 입구 + 지붕 + 골목 교차점을 생성기가 자동 출력 | 제안 상태 | docs/research/06_missed_items.md:59-62 |

## 인원 수

| 상태 | 항목 | 지금 | 계획 | 메모 | 근거 |
|---|---|---|---|---|---|
| 확정 | 한 판 최대 인원(사람+봇) | 80 | 80 | Participants 80 = MaxParticipantsLimit(ADR-168·241·259). 사람은 최대 64(Participants−MinBots) | Assets/_Project/Data/Resources/GameData/rules.json Match |
| 확정 | 봇 수 | max(16, 80 − 사람 수) | 사람 수에 맞춰 채움 | BotFill.Plan(Deployment.cs), MinBots 16·MaxBots 79. 엘리베이터 슬롯을 넘으면 잘림(MatchDirector). BotCount 15는 FillToParticipants false일 때(테스트)만 | Assets/_Project/Data/Resources/GameData/rules.json Match; Assets/_Project/Scripts/Core/Deployment.cs BotFill |
| 확정 | 인구 밀도 | 약 48명/km²(31명/0.65km²) | 80명 기준 미계산 | 조사 06은 31명 기준으로 계산함(출처 그대로). 현재 맵 면적으로 다시 계산 필요. | docs/research/06_missed_items.md:16 |
| 확정 | 봇 투입 | 엘리베이터별 1대씩 배정 | 승강장 20곳, 슬롯 60 (MASTER_PLAN.md:140) | Teams.Deploy가 사람+봇을 팀 크기로 배치 | Assets/_Project/Scripts/Net/MatchDirector.cs:227-289 |

## 이름

| 상태 | 항목 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 봇 이름 | 가상 닉네임 풀 64개(한국어 16·영어 48)에서 매치 시드로 섞어 번호 순으로 배정 | 한 판 안에서 중복 없음. 풀을 넘는 번호는 이름 뒤에 숫자(예: 하늘2). ADR-245 | Assets/_Project/Scripts/Core/BotNames.cs; AI/BotController.cs:343; Net/MatchDirector.cs:290 |
| 확정 | 이름 규칙 | 실존 인물·브랜드·스트리머 이름 금지 | 구현됨(BotNames.Pool) | Assets/_Project/Scripts/Core/BotNames.cs:5 |

## 오염·열쇠 행동 (BotPollutionPlan, ADR-245)

| 상태 | 항목 | 쉬움 | 보통 | 어려움 | 근거 |
|---|---|---|---|---|---|
| 확정 | 경고 뒤 반응 지연(봇별 0~최대, 시드 고정) | 최대 45초 | 최대 25초 | 최대 10초 | Assets/_Project/Scripts/Core/BotPollutionPlan.cs:11 |
| 확정 | 구역을 떠나는 조건 | 오염 확정·전선 접근이면 즉시, 확정 40초 전이면 지연과 무관하게 출발 | 같음 | 같음 | BotPollutionPlan.cs:18-25 |
| 확정 | 열쇠를 가진 봇이 경고 때 열쇠를 쓸 확률(봇별 고정) | 50% | 80% | 100% | BotPollutionPlan.cs:29; AI/BotController.cs:928 |
| 확정 | 최종 구역을 아는 시점 | 경고·오염 구역 수 ≥ ceil(구역 수×2/3) | 같음 | 같음 | BotPollutionPlan.cs:34-37 |
| 확정 | 소리 난 곳으로 가는 봇 비율 | 40% | 70% | 100% | Assets/_Project/Scripts/Core/BotPriority.cs:31-32 |

## 시가전·산악 행동 (제안)

| 상태 | 행동 | 필요한 이유 | 선행 조건 | 근거 |
|---|---|---|---|---|
| 제안 | 건물 진입·방 정리: 입구에서 멈춰 소리·시야 확인, 방 단위 이동 | 실내 교전이 핵심. 지금은 문만 열고 지나감 | 건물 입구 관심 지점 자동 생성 |  |
| 제안 | 창문·옥상 활용: 창문 사격, 옥상 점령 | 도시 전투의 수직 차원 | NavMeshLink(창문·옥상 진입), 파쿠르 연결 |  |
| 제안 | 엄폐 사용: 모서리 피킹, 차량·바위 뒤 숨기 | 지금 엄폐는 '안 보이는 곳 이동'뿐 | 엄폐 후보 지점 스캔 |  |
| 제안 | 능선·사면 이동과 고지 선점 | 산 맵에서 거리 교전이 늘어남 | 고도 가중 경로, 산 NavMesh 연결성 테스트 |  |
| 제안 | 차량 이용·회피: 도로 이동 때 탑승, 차량 소리에 반응 | 맵이 커지면 이동 수단 필요. 경적은 이미 청취 | 차량 운전 봇 로직(지금 없음) |  |
| 제안 | 소리 구분: 총소리 방향, 실내/실외 가림 | 80명이 동시에 쏘면 청취가 폭주 | 서버 소리 이벤트 관심 영역 제한(MASTER_PLAN.md:43) |  |
| 제안 | 난이도 혼합: 같은 판에 쉬움·보통·어려움 비율 배치 | 지금은 판 전체가 한 단계 | BotRules 봇별 인스턴스 |  |
| 제안 | 서버 비용 관리: 멀리 있는 봇은 감지 주기 늘리거나 단순 이동 | 봇 수 증가 시 감지 레이캐스트와 NavMesh 비용 증가 | 관심 영역 기반 LOD |  |

## 정해야 할 것

- [x] **80명 중 봇 비율은?** (확정) → 사람이 적으면 봇으로 채우고 많으면 줄임(최소 봇 수 보장) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능] → 구현: BotFill(MinBots 16)
  - 선택지: 사람 수 무관하게 항상 80명으로 채움 / 사람이 적으면 봇으로 채우고 많으면 줄임(최소 봇 수 보장) / 고정 비율  · 추천: 사람이 적으면 봇으로 채우고 많으면 줄임(최소 봇 수 보장)
  - 근거: MASTER_PLAN.md:9 '사람 수가 달라도 같은 규칙'과 맞고, 서버 CPU 상한을 지키기 쉽다.
- [x] **난이도는 판 전체 하나인가, 봇별로 섞는가?** (확정) → 봇별 혼합(보통 중심, 쉬움·어려움 소수) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능] → 구현: BotMix(ADR-245)
  - 선택지: 판 전체 하나(현재) / 봇별 혼합  · 추천: 봇별 혼합(보통 중심, 쉬움·어려움 소수)
  - 근거: 한 판 안에서 실력 차이가 있어야 사람 같고, 초보도 이길 수 있다. 구현은 BotLevels.For를 봇별로 부르는 정도.
- [x] **봇이 차량을 운전하는가?** (확정) → 봇은 차량을 운전하지 않는다(준환 확정). 봇의 차량 탑승도 이번 범위 밖
  - 선택지: 아니오(회피·청취만) / 예  · 추천: 아니오(이번 범위 밖, BACKLOG)
  - 근거: NavMesh 기반 보행과 물리 차량 주행은 다른 문제. 06_missed_items.md I3 권장도 창문·차량 제외.
- [x] **봇이 창문·옥상·사다리를 쓰는가?** (확정) → 이번 범위에서 제외(준환: 상관없음, 제 판단으로 유지). 도시 지도 확정 후 NavMeshLink 후보만 표시
  - 선택지: 이번엔 제외 / NavMeshLink로 지원  · 추천: 이번엔 제외, 도시 지도 확정 후 NavMeshLink 후보만 표시
  - 근거: NavMeshLink 0건이 현재 상태. 도시 설계가 정해진 뒤 입구·옥상 지점을 생성기가 낼 때 같이 한다.
- [x] **봇 이름은 어떻게?** (확정) → 가상 닉네임 풀 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능] → 구현: BotNames(ADR-245)
  - 선택지: 봇 N 유지 / 가상 닉네임 풀  · 추천: 가상 닉네임 풀
  - 근거: 80명 판에서 봇이 한눈에 티 난다. 실존 이름 금지.


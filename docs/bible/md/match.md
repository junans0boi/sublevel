# 매치·투입·점수 규칙

> 상태: 확정 · 요약: 엘리베이터 투입, 지역 순차 봉쇄, 인원(지금 16명 vs 목표 50명), 시간표, 루팅 한도(MaxLootSpawns 250), 점수 없음.

## 한눈에 보기

| 상태 | 항목 | 값 | 근거 |
|---|---|---|---|
| 확정 | 참가자 | 16명 (사람+봇), 상한 24 | Assets/_Project/Data/Resources/GameData/rules.json:104 |
| 확정 | 지역 | 4개, 1024 m 섬 | Assets/_Project/Data/Resources/GameData/world.json |
| 확정 | 첫 경고 | 5분, 지역당 약 3분 30초 | Assets/_Project/Scripts/Core/Lockdown.cs:17-21 |
| 확정 | 승리 | 마지막 생존자(팀) | docs/SPEC.md:11 |

## SPEC 규칙 (기획서 원문 요약)

| 상태 | 규칙 | SPEC 내용 | 현재 구현과 차이 | 근거 |
|---|---|---|---|---|
| 확정 | 엘리베이터 투입 | 비행기 낙하 대신 무작위 지역의 엘리베이터에서 시작. 구역당 3~4대. 스폰 보호 5~10초. | 구현됨: 시드로 참가자를 엘리베이터에 배정. 문은 시작 3초 뒤 열림, 열린 뒤 6초 보호(첫 사격·투척에서 해제). | docs/SPEC.md:50-51 (SPEC §5); Assets/_Project/Scripts/Core/Deployment.cs:51-100; docs/DECISIONS.md ADR-055 |
| 확정 | 엘리베이터 1명 | 엘리베이터 1대에 1명(팀 모드는 팀 단위). | 빈 엘리베이터가 있는 동안 1명씩. 모자라면 가장 덜 찬 곳에 같이 탐(승강기 안 3x3 격자 0.8 m 간격). 지금 승강장은 합계 13대, 참가자 16명 이상이라 항상 같이 타는 경우가 생긴다. | Assets/_Project/Scripts/Core/Deployment.cs:62-66,151-153 |
| 확정 | 지역 상한 | 지역별 시작 인원 상한 = ceil(전체/지역 수)+1. 지역 순서는 시드로 섞는다. | 그대로 구현(RegionCap). | Assets/_Project/Scripts/Core/Deployment.cs:68; docs/DECISIONS.md ADR-055 |
| 확정 | 지역 순차 봉쇄 | safe → warning → lockdown(방사능 지속 데미지, 즉사 아님) → red-zone 폭격 → closed. 폐쇄 순서와 최종 지역은 매 판 랜덤. | 구현됨(아래 시간표). 규칙 이름은 '봉쇄'지만 방사능 데미지는 이미 있다. | docs/SPEC.md:11-21 (SPEC §3); Assets/_Project/Scripts/Core/Lockdown.cs:17-53 |
| 확정 | 최종 지역 공개 | 서버가 시작 때 최종 지역을 정하고 숨김. 마지막 2~3개가 남으면 후보가 좁혀짐. 최종 지역은 소구역 3~4개를 순차 폭격. | 구현됨: 남은 폐쇄가 1개일 때 최종 지역 공개(FinalRevealLastClosings=1), 소구역 3~4개. | docs/SPEC.md:23-27 (SPEC §3-1); Assets/_Project/Scripts/Core/Lockdown.cs:47-53 |
| 확정 | 연결 보장 | 폐쇄 순서가 랜덤이어도 남은 구역은 항상 엘리베이터로 연결(허브형). | 지역 간 엘리베이터 이동 구현(ADR-076): 안전·경고 지역만 운행, 목적지는 안전 지역만, 8초·쿨 60초. | docs/SPEC.md:34-35 (SPEC §3-3) |
| 확정 | 승리 조건 | 최후의 개인/팀. | 마지막 생존자(팀)가 '승리', 탈락은 순위 표시. | Assets/_Project/Scripts/Gameplay/ResultScreen.cs:8-14 |
| 미정 | 칩 부활 | 팀 모드에서만: 사망 시 칩 드롭, 팀원이 수거해 보관함에서 부활, 90초 제한. | 코드에서 칩 부활을 찾지 못했다(Scripts 검색에 '칩'은 인벤토리·휠 UI뿐). 대신 기절(출혈 시간표)·팀원 부활 F 10초가 구현됨(ADR-072·129). SPEC 갱신 필요. | docs/SPEC.md:42-45 (SPEC §6) |
| 확정 | 구역 목록 | 7개(산악·습지·도시·공장·지하·온실·코어), MVP는 산악·공장. | 현재 월드는 4개 지역 A 산악·B 습지·C 마을·D 공장, 지역마다 엘리베이터 3·3·4·3 = 13대(world.json). SPEC과 다르다. | docs/SPEC.md:36-45 (SPEC §4) |

## 참가 인원·모드 (지금 vs 목표)

| 상태 | 항목 | 지금 | 목표/제안 | 근거 |
|---|---|---|---|---|
| 확정 | 한 판 참가자 | 사람+봇 = 기본 16명(BotCount 15 + 1). 상한 24(MaxBotsLimit 23 + 1). | 50명(사람+봇). 06 문서 권장은 24~32부터지만 준환 초안은 50. | Assets/_Project/Data/Resources/GameData/rules.json:104; Assets/_Project/Scripts/Core/Deployment.cs:114-127 |
| 확정 | 한 방의 사람 수 | MaxPlayers 16, 전용 서버 인자 --max-players 1~24. | 50이면 상한을 올리고 서버 비용을 재측정(network 섹션 참조). | Assets/_Project/Data/Resources/GameData/rules.json:104; Assets/_Project/Scripts/Core/Lobby.cs:11 |
| 확정 | 팀 모드 | 솔로/듀오(2)/스쿼드(4), 아군 피해 없음(FriendlyFire false). | 유지. | docs/DECISIONS.md ADR-072; Assets/_Project/Scripts/Core/Teams.cs:71-72 |
| 확정 | 맵 | 1024 m 정사각 섬(육지 약 0.6~0.7 km²), 지역 4개. 밀도 약 48명/km²(31명 기준). | C2안: 2.4x2.0 km, 육지 약 3.2 km², 지역 6, 승강장 20곳(슬롯 60), 15.5명/km². | Assets/_Project/Data/Resources/GameData/world.json:2-9 |
| 확정 | 재접속 | 끊기면 몸을 60초 보관, 같은 세션이 돌아오면 복귀, 아니면 봇이 인계(ADR-125). | 유지. | Assets/_Project/Scripts/Core/Deployment.cs:123-127; Assets/_Project/Data/Resources/GameData/rules.json:104 |

## 봉쇄 시간표 (Lockdown, 초)

| 상태 | 항목 | 값 | 설명 | 근거 |
|---|---|---|---|---|
| 확정 | 첫 지역 경고 | 300 | 매치 시작 후 이 시간에 첫 지역이 발표된다. | Assets/_Project/Scripts/Core/Lockdown.cs:17 |
| 확정 | 경고(발표~봉쇄) | 45 | 아직 열려 있음, 탈출 시간. | Assets/_Project/Scripts/Core/Lockdown.cs:18 |
| 확정 | 봉쇄(폭격 전) | 120 | 출입문 봉쇄 + 방사능 지속 데미지. | Assets/_Project/Scripts/Core/Lockdown.cs:19 |
| 확정 | 폭격 | 45 | 미사일. 끝나면 폐쇄. | Assets/_Project/Scripts/Core/Lockdown.cs:20 |
| 확정 | 두 지역 발표 사이 최소 간격 | 150 | 지역 폐쇄 주기. | Assets/_Project/Scripts/Core/Lockdown.cs:21 |
| 확정 | 방사능 DPS / 유예 / 폐쇄 지역 DPS | 2 | 초당 2, 4초 유예 후 시작, 폐쇄 지역은 초당 20. 단계가 오를수록 배율 1→5.5(RadiationStageScale 7칸). | Assets/_Project/Scripts/Core/Lockdown.cs:24-27 |
| 확정 | 폭발 | 70 | 간격 1.6초, 3초 전 예고, 안쪽 3 m 전체 피해 70, 10 m 밖 0, 절반은 전투원 근처를 노림. | Assets/_Project/Scripts/Core/Lockdown.cs:33-39 |
| 확정 | 열쇠 | 2 | 지역당 열쇠 2개, 통로는 사용 후 10초 열림, 거리 4 m. | Assets/_Project/Scripts/Core/Lockdown.cs:42-44 |
| 확정 | 최종 지역 소구역 | 3 | 최종 지역 폐쇄 20초 뒤 시작, 경고 30·폭격 30초, 90초 간격, 소구역 3~4개. | Assets/_Project/Scripts/Core/Lockdown.cs:48-53 |
| 제안 | 추정: 현재 4지역 총 길이 | 1100 | 경고 300/450/600초, 마지막 폐쇄 810초, 소구역 단계 포함 약 18분(계산 추정, 실측 아님). | 계산: 위 값 + world.json 지역 4개 |
| 제안 | 추정: 6지역 총 길이 | 1460 | 경고 300~900초, 마지막 폐쇄 1110초, 소구역 포함 약 24분. MASTER_PLAN 목표 20~25분과 맞는다(추정). 지역 가로지르기 시간이 경고 45초보다 길어지는 문제는 별도(06 B2). | 계산: 위 값 + C2안 지역 6개 |

## 루팅 규칙과 한도

| 상태 | 항목 | 값 | 설명 | 근거 |
|---|---|---|---|---|
| 확정 | MaxLootSpawns | 250 | 월드 빌더가 건물마다 한 점씩 순환하며 250개에서 자른다. 건물이 늘면 건물당 1개 미만이 되고 마지막 건물은 0개(06 문서 I1). | Assets/_Project/Editor/WorldBuilder.cs:60,497-508 |
| 확정 | MaxRocks | 900 | 바위 상한도 1 km² 기준 상수(WorldBuilder.Structures.cs:30). | docs/research/06_missed_items.md:I1 |
| 확정 | 바닥 루팅 테이블 | 전역 1개, 항목 50개, 가중치 합 242 | 건물 종류·지역 구분 없음. LootTable.Plan(점 수, 시드)이 결정적으로 뽑는다(Core/Loot.cs:26-45). | Assets/_Project/Data/Resources/GameData/loot.json |
| 확정 | 아이템 종류 | 무기·탄약·의료·방어구·조준경·투척물·소모품 등 18종 | LootKind 열거. 열쇠(12)는 지역당 2개를 따로 배치. | Assets/_Project/Scripts/Core/Loot.cs:5 |
| 확정 | 보급 상자 | 2단계부터 2단계마다, 6개 포함 | 앞 4개는 보장 항목. 하강 6초, 열기 3초(F 길게), 연막 150초, 높이 45 m. 테이블 29항목, 가중치 합 138. 방어구 3단·조준경이 몰려 있다. | Assets/_Project/Data/Resources/GameData/rules.json:84; Assets/_Project/Data/Resources/GameData/supply.json |
| 제안 | 50명 기준 총량 | 미정 | 건물 411채(진입 305)에서 건물당 점 수 x 종류 가중으로 계산하고 상한을 데이터(world.json)로 옮긴다. 루팅 NetworkObject 수가 커지면 접속 직후 스폰 폭주가 날 수 있다(network 섹션). | docs/research/06_missed_items.md:I1,I2 |

## 현재 루팅 테이블의 가중치 비율 (계산)

| 상태 | 분류 | 가중치 | 비율(%) | 근거 |
|---|---|---|---|---|
| 확정 | 조준경 | 55 | 22.7 | Assets/_Project/Data/Resources/GameData/loot.json (계산) |
| 확정 | 탄약 | 52 | 21.5 | Assets/_Project/Data/Resources/GameData/loot.json (계산) |
| 확정 | 총(VK-9, Ordo, Warden, Breacher, Sentinel) | 42 | 17.4 | Assets/_Project/Data/Resources/GameData/loot.json (계산) |
| 확정 | 근접(마체테 등 4종) | 14 | 5.8 | Assets/_Project/Data/Resources/GameData/loot.json (계산) |
| 확정 | 헬멧·조끼·가방(각 10) | 30 | 12.4 | Assets/_Project/Data/Resources/GameData/loot.json (계산) |
| 확정 | 붕대·구급상자·음료·알약·주사기·의료키트 | 37 | 15.3 | Assets/_Project/Data/Resources/GameData/loot.json (계산) |
| 확정 | 섬광탄·화염병·연막·제리캔 | 12 | 5.0 | Assets/_Project/Data/Resources/GameData/loot.json (계산) |

## 슬롯

| 상태 | 슬롯 | 값 | 설명 | 근거 |
|---|---|---|---|---|
| 확정 | 무기 칸 5개 | (1)(2) 주무기 (3) 보조(권총 전용) (4) 근접 (5) 투척 | HUD 무기 띠와 같다. | docs/DECISIONS.md ADR-058 |
| 확정 | 엘리베이터 슬롯(현재) | 승강장 13대 | A 3 + B 3 + C 4 + D 3. 참가자 16명이라 같이 타는 경우가 생김. | Assets/_Project/Data/Resources/GameData/world.json:20-48 |
| 제안 | 엘리베이터 슬롯(C2안) | 승강장 20곳 x 승강기 3기 = 60 | 50명 중 점유 83%, 승강장 사이 최소 215 m, 지역 상한 ceil(50/6)+1 = 10. | docs/research/map_options/option_C2_notes.md:11 |

## 정해야 할 것

- [x] **한 판 인원** (확정) → 50 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 50 (사람+봇) / 24~32 (06 문서 권장)  · 추천: 50
  - 근거: 준환 초안은 50. 서버 CPU·대역폭은 측정 전이라 24~32로 먼저 돌려 보고 올리는 단계 접근도 가능.
- [x] **봉쇄 단위** (확정) → 하이브리드(C) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 지역 6개 순차(SPEC 유지) / 오염 전선(B) / 하이브리드(C, 제안)  · 추천: 하이브리드(C)
  - 근거: MASTER_PLAN §6. 규칙 변경이므로 ADR 필수(CLAUDE.md 규칙 5).
- [x] **SPEC §6 칩 부활 처리** (확정) → 칩 부활 유지(SPEC §6 유지, 팀 모드 한정). 현재 코드는 기절·부활만 구현돼 있어 칩 부활 구현이 필요함 [준환 결정 2026-11-16, 일괄 확정 번복]
  - 선택지: SPEC에서 삭제하고 기절·부활로 확정 / 칩 부활 구현  · 추천: SPEC에서 삭제하고 기절·부활로 확정
  - 근거: 코드는 이미 기절·부활이다. SPEC과 구현이 어긋나 있다.
- [x] **판 길이 목표** (확정) → 20~25분 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 20~25분 / 15분  · 추천: 20~25분
  - 근거: 위 계산상 6지역 시간표가 약 24분이라 맞는다(추정).
- [x] **루팅 구조** (확정) → 지역·건물 종류별 가중 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 지역·건물 종류별 가중 테이블 + 3단 장비는 일부 종류만 / 전역 1개 유지  · 추천: 지역·건물 종류별 가중
  - 근거: 06 문서 I2. 도시가 커질수록 거점 가치 차이가 이동 동기가 된다. 룰 변경이라 ADR.
- [x] **점수·크레딧(재화) 도입** (확정) → 도입 안 함 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 도입 안 함(순위+킬·피해·생존 시간만) / 경기 후 보상 점수  · 추천: 도입 안 함
  - 근거: 코드에 점수제는 없다. Credits.cs는 에셋 저작자 표기용이다.
- [x] **칩 부활을 오염 시스템·열쇠 엘리베이터와 어떻게 맞추나? (SPEC §6: 칩 드롭, 팀원이 수거, 구역 입구 칩 보관함, 90초, 구역 폐쇄 시 칩 파괴)** (확정) → 각 지역 승강장에 칩 보관함, 사망 후 90초 수거, 그 지역이 오염 확정되면 칩 파괴, 부활은 비오염 지역 승강장에서(승강장 선택) [준환 위임 2026-11-16: 추천안 적용]
  - 선택지: 각 지역 승강장에 칩 보관함, 사망 후 90초 수거, 그 지역이 오염 확정되면 칩 파괴, 부활은 비오염 지역 승강장에서(승강장 선택) / 칩 보관함은 지역 입구 고정 유지, 부활은 투입처럼 무작위 승강장 / 칩 부활은 오염 1~3단계에서만 허용  · 추천: 각 지역 승강장에 칩 보관함, 사망 후 90초 수거, 그 지역이 오염 확정되면 칩 파괴, 부활은 비오염 지역 승강장에서(승강장 선택)
  - 근거: SPEC의 '구역 입구 칩 보관함'과 '구역 폐쇄 시 칩 파괴'를 새 구조(6지역, 승강장 20곳, 오염 확정)에 대응시켜야 한다. 팀 모드(스쿼드 4)와 기절·부활과의 관계(기절 중 수혈 vs 사망 후 칩)도 정리 필요.


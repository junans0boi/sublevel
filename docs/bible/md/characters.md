# 캐릭터

> 상태: 확정 · 요약: 지금은 Quaternius CC0 'Swat' 한 모델(Generic 리그)이 플레이어·봇·더미를 모두 맡는다. 키 1.8m 캡슐, 눈 1.6m, 높이 기반 피격 구역. 실사 비율 Humanoid 교체와 20종 이상 로스터는 제안 단계.

## 현재 모델

| 상태 | 항목 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 캐릭터 모델 | Quaternius Ultimate Modular Men - Swat.fbx (CC0, Generic 리그, 손 뼈 Wrist.R) | 플레이어·봇·훈련 더미가 같은 모델. 스타일화 비율(큰 머리, 5.5등신). | docs/ASSET_LEDGER.md:38 (C-0001); Assets/_Project/Editor/CharacterKit.cs:15 |
| 제안 | 스킨·복장 변형 | Mixamo: 의상 고정 + 약한 머티리얼 틴트 + 팀 색 완장·머리띠 소품. UBC: 피부 2 x 머리 2 x CC0 소품. 봇은 시드 배정, 인접 중복 금지 [S3 가정 2026-11-16, 준환 위임] | 팀 구분 색이 어떻게 입혀지는지는 이 조사에서 확인 못 함. 제안은 아래 로스터 참고. |  |
| 확정 | 캡슐 | 높이 1.8m, 반지름 0.35m, 중심 0.9m |  | Assets/_Project/Editor/TestBoxBuilder.cs:331 |
| 확정 | 눈 높이(서기) | 1.6m | 앉기 -0.65, 엎드리기 -1.2 (Stance 규칙) | Assets/_Project/Scripts/Net/Combatant.cs:28 |
| 확정 | 무기 소켓 | Wrist.R 이름 문자열 한 곳 | 교체 비용 작음 | docs/research/01_character.md:5-6 |

## 자세별 수치

| 상태 | 자세 | 캡슐 높이 m | 눈 낮춤 m | 이동 배율 | 탄퍼짐 배율 | 발소리 배율 | 근거 |
|---|---|---|---|---|---|---|---|
| 확정 | 서기 | 1.8 | 0 | 1.0 | 1.0 | 1.0 | Assets/_Project/Data/Resources/GameData/rules.json Stance |
| 확정 | 웅크리기 | 1.0 | 0.65 | 0.745 | 0.5 | 0.4 | Assets/_Project/Data/Resources/GameData/rules.json Stance |
| 확정 | 엎드리기 | 0.7 | 1.2 | 0.255 | 0.2 | 0.1 | Assets/_Project/Data/Resources/GameData/rules.json Stance |

## 이동 수치

| 상태 | 동작 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 걷기 | 4.7 m/s | 무기별 배율: SMG 0.977, 소총 0.954, 마크스맨 0.952, 샷건 0.939 | Assets/_Project/Data/Resources/GameData/rules.json Locomotion |
| 확정 | 질주 | 6.3 m/s |  | Assets/_Project/Data/Resources/GameData/rules.json Locomotion |
| 확정 | 느린 걷기(발소리 작음) | 1.7 m/s, 발소리 0.25 |  | Assets/_Project/Data/Resources/GameData/rules.json Locomotion |
| 확정 | 웅크려 걷기 | 약 3.5 m/s | 배율을 곱한 값(코드에서 직접 읽은 수치 아님) | 계산: 4.7 x 0.745 |
| 확정 | 엎드려 기기 | 약 1.2 m/s | 같음 | 계산: 4.7 x 0.255 |
| 확정 | 점프 | 초속 4.8, 중력 -14 | 낙하: 3m까지 안전, 12m 이상 즉사(피해 100) | Assets/_Project/Data/Resources/GameData/rules.json Locomotion |
| 확정 | 스태미나 | 없음 | 질주에 제한이 없다. 의도된 설계인지는 open | Assets/_Project/Scripts 전체 grep 결과 0건 |
| 확정 | 수영 | 질주 속도의 46%(약 2.9 m/s), 잠수 20초, 회복 9초, 익사 피해 초당 5 | 수심 1.2m 이상에서 수영, 1.0m 이하에서 해제 | Assets/_Project/Scripts/Core/Swim.cs:10-30 |
| 확정 | 파쿠르 | 넘기 0.5~1.0m, 오르기 최대 2.0m, 창문 턱 0.9~1.25m, 난간 매달리기 2.0~2.5m | 넘기 0.55초, 창문 0.65초, 낮은 오르기 0.6초, 높은 0.9초 | Assets/_Project/Data/Resources/GameData/rules.json Parkour |
| 확정 | 기울이기 | 옆으로 0.25m, 눈 0.04m 낮춤 |  | Assets/_Project/Scripts/Core/Aiming.cs:35-36 |

## 피격 구역 (높이 기반)

| 상태 | 구역 | 판정 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 머리 | 발 위 높이 >= 키 - 0.3m (서기: 1.5m 이상) | 자세가 낮아져도 머리는 위 0.3m 고정 | Assets/_Project/Scripts/Core/HitZones.cs:14-18 |
| 확정 | 다리 | 키의 45% 미만 (서기: 0.81m 미만) | 팔은 몸통으로 친다(뼈 단위 판정 아님) | HitZones.cs:16 |
| 확정 | 몸 | 그 사이 |  | HitZones.cs:31-35 |
| 확정 | 최소 키 | 0.6m | 어떤 자세도 이보다 작지 않다 | HitZones.cs:18 |

## 피격 배율 (weapons.json)

| 상태 | 무기 | 머리 | 몸 | 다리 | 근거 |
|---|---|---|---|---|---|
| 확정 | VK-9 권총 | 2.1 | 1.0 | 0.9 | Assets/_Project/Data/Resources/GameData/weapons.json |
| 확정 | Ordo SMG | 2.1 | 1.05 | 1.3 | weapons.json |
| 확정 | Warden 불펍 | 2.35 | 1.0 | 0.9 | weapons.json |
| 확정 | Breacher 샷건 | 1.5 | 1.0 | 0.8 | weapons.json |
| 확정 | Sentinel 저격 | 2.5 | 1.0 | 0.7 | weapons.json |
| 확정 | 근접 4종(마체테, 쇠지렛대, 낫, 프라이팬) | 1.5 | 1.0 | 1.0 | weapons.json |

## 체력·방어

| 상태 | 항목 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 최대 체력 | 100 |  | Assets/_Project/Scripts/Net/Combatant.cs:27 |
| 확정 | 헬멧 3등급 | 피해 감소 30/40/55%, 내구 80/150/230 |  | Assets/_Project/Data/Resources/GameData/rules.json Armor |
| 확정 | 조끼 3등급 | 감소 30/40/55%, 내구 200/220/250 | 부서지면 감소 20% | Assets/_Project/Data/Resources/GameData/rules.json Armor |
| 확정 | 회복 아이템 | 붕대 4초 +10(75까지), 구급 6초 +75(75까지), 의료키트 8초 +100 | 치료 중 이동 배율 0.6 | Assets/_Project/Data/Resources/GameData/rules.json Heal |
| 확정 | 부스트 게이지 | 최대 100, 초당 0.8 소모. 음료 +40, 알약 +60, 주사 +100 | 단계별 이속 보너스 0/0/2.5/5% | Assets/_Project/Data/Resources/GameData/rules.json Boost |
| 확정 | 쓰러짐(팀전) | 쓰러짐 체력 100, 출혈 초당 2.5, 소생 10초 후 체력 10 | 듀오 2 / 스쿼드 4, 아군 사격 없음 | Assets/_Project/Data/Resources/GameData/rules.json Team |

## 감정 표현

| 상태 | 코드 | 표시 이름 | 근거 |
|---|---|---|---|
| 확정 | e1 | 손 흔들기 (Wave) | Assets/_Project/Scripts/Core/Emotes.cs:8 |
| 확정 | e2 | 만세 (Cheer) | Emotes.cs:8 |
| 확정 | e3 | 가리키기 (Point) | Emotes.cs:8 |
| 확정 | e4 | 인사 (Bow) | Emotes.cs:8 |
| 확정 | e5 | 춤 (Dance) | Emotes.cs:8 |
| 확정 | e6 | 박수 (Clap) | Emotes.cs:8 |

## 애니메이션 클립 목록

| 상태 | 묶음 | 클립 | 쓰임 | 메모 | 근거 |
|---|---|---|---|---|---|
| 확정 | Swat 내장(24개, Quaternius CC0) | Idle, Walk, Run, Run_Back, HitRecieve, Punch_Left, Punch_Right, Idle_Gun_Pointing 등 | 이동 기본층, 피격, 펀치 | 이름은 코드에 쓰인 것만 적음 | docs/ASSET_LEDGER.md:38; Assets/_Project/Editor/CharacterKit.cs:45-50 |
| 확정 | Mixamo 소총 17개를 Swat 뼈대에 구운 Mx_* (ADR-166) | RifleIdle, RifleAimingIdle, FiringRifleStand, ReloadingRifleStand, ReloadRifleCrouch, RifleWalk, RifleWalkBack, RifleRun, RifleSideStep, RifleCrouchWalk, RifleCrouchWalkBack, RifleKneelIdle, RiflePullOut, RiflePutAway, RifleDeath, ProneFiringRifle, ProneReloadingRifle | 조준/낮춤/발사/사망에 연결. ProneFiringRifle, ProneReloadingRifle은 구웠지만 어느 상태에도 안 이어짐 | 비 CC0, 저장소 비공개 유지(Mixamo/LICENSE.txt). Adobe 추가 약관 미확인. | Assets/_Project/Prefabs/M1_5/Baked/*.anim; docs/ASSET_LEDGER.md:77 (C-0005) |
| 확정 | Quaternius UAL1/UAL2 Standard (CC0) | UAL1: 권총 조준·대기·장전·발사, 웅크리기, 수영, 달리기, 구르기, 사망, 피격. UAL2: 근접·슬라이드·점프·피격 | 임포트만, 현 컨트롤러에는 연결 안 됨(확인 필요) | Humanoid 리그용이라 현 Swat(Generic)에는 못 붙는다 | docs/ASSET_LEDGER.md:72-76 (C-0002, C-0004) |
| 확정 | 코드 구동 포즈 | 엎드리기(모델 85도 눕힘), 웅크리기 무릎, 수영 팔·다리, 총 두 손 IK, 차량 착석, 난간, 파쿠르 포즈 | 클립 없이 코드가 뼈를 직접 돌림 | 엎드림 시 몸이 약 35도 뜨고 소총 달릴 때 한 팔이 늘어지는 결함이 감사에서 확인됨 | docs/research/04_animation_audit.md |

## 애니메이터 구조

| 상태 | 이름 | 종류 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | Base | 레이어 | 이동 블렌드 (Locomotion) | Assets/_Project/Prefabs/M1_5/Character.controller |
| 확정 | Upper | 레이어 | 상체 자세: Lowered, Aim, Death 상태 | Character.controller |
| 확정 | PunchRight / PunchLeft | 레이어 |  | Character.controller |
| 확정 | Air, Shoot, Hit | 레이어 |  | Character.controller |

## 교체 계획 (제안)

Swat은 Humanoid 리타겟이 안 되는 뼈 구조(다리가 Hips 아래가 아님)라 Mixamo 클립을 오프라인으로 구워 써야 했고, 엎드리기·소총 팔에서 결함이 남았다. 실사 비율 Humanoid 캐릭터로 교체하면 Mixamo/Quaternius 클립을 베이크 없이 직접 쓸 수 있다.

교체 때 바꿔야 하는 곳: CharacterKit(전면 재작성), MixamoBake(폐기), CombatantAnimation 본 슬롯, 소총·차량 IK, MeleeKit 소켓, 좌석 위치 재측정. 피격 구역은 높이 기반이라 키·눈 높이·캡슐만 새 비율(약 1.75m)에 맞추면 된다(조사 01 §0, §4).

폴리 예산: 화면 내 삼각형 1.5M 이하. LOD 3단계 필수(LOD0 15k 이하 8명, LOD1 약 5k, LOD2 약 1.5k, 100m 밖 컬).

- 목표 로스터: 서로 구분되는 외형 20종 이상(남녀·체형 혼합), 같은 모델은 인접하지 않게 배치 + 머티리얼 틴트 + 팀 색 소품.
- AI 생성 모델 금지(규칙 2). Ready Player Me는 2026-01-31 서비스 종료라 제외.

## 교체 후보 (모두 제안)

| 상태 | 후보 | 출처 | 라이선스 | 수량 | 장단점 | 근거 |
|---|---|---|---|---|---|---|
| 제안 | Mixamo 사실풍 캐릭터 풀 | docs/research/01_character.md §1a | Adobe Mixamo FAQ: 상업 무료. 비 CC0, 원본 단독 재배포 금지 | 8~12종 (남녀·체형 혼합) | 표준 Mixamo 뼈대로 Humanoid 자동 구성, 클립 직결. 단점: 폴리·텍스처 미확인, 약관 원문 미확인(사람 검토 R2), 모델마다 의상 고정. | docs/research/01_character.md §1a |
| 제안 | Quaternius Universal Base Characters Standard | 저장소 Art/Characters/QuaterniusUniversal/BaseCharacters; docs/ASSET_LEDGER.md:73 (C-0003) | CC0 1.0 | 보유 2종(Superhero 남/녀) x 피부 2 x 머리 2 = 8 조합 | 약 13k 폴리, Humanoid 설정됨(Avatar 유효성은 실측 필요). 이상화 체형, 군복 없음. Regular/Teen 체형은 유료라 규칙 3상 blocked. | 저장소 Art/Characters/QuaterniusUniversal/BaseCharacters; docs/ASSET_LEDGER.md:73 (C-0003) |
| 제안 | Sketchfab CC0/CC-BY 개별 캐릭터 | 조사 01 §1c | 건별 확인 | 보조 | 리깅 품질 편차 큼, 건별 심사 비용. | 조사 01 §1c |
| 제안 | MakeHuman 출력물 | 조사 01 §1c | CC0 표기(원문 확인 필요) | 대량 변형 가능 | 리깅에 Blender 작업 필요. 2차 수단. | 조사 01 §1c |
| 제안 | Kenney, Quaternius Ultimate Modular 계열 | 조사 01 §1c | CC0 | - | 스타일화라 실사 방향에 안 맞음. 비추천. | 조사 01 §1c |
| 제안 | Asset Store 무료 캐릭터 | 조사 01 §1c | Asset Store EULA | - | 외형 부적합·상표 위험. 비추천. | 조사 01 §1c |

## 로스터 규모 (제안)

| 상태 | 항목 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 제안 | 서로 다른 외형 수 | 20종 이상 | Mixamo 8~12 + UBC 조합 8 이상 |  |
| 제안 | 플레이어 전용 | UBC 조합(피부·머리 선택) | CC0라 크레딧 부담 없음 |  |
| 제안 | 봇 전용 | Mixamo 풀 + 시드 기반 배정 | 팀 색은 완장·머리띠 소품 + 약한 틴트 |  |
| 제안 | 진행 순서 | 1종 시험 임포트로 Avatar·폴리·텍스처 검증 -> 소켓 일반화 -> 클립 직결 -> LOD -> 변형 시스템 -> ledger 갱신 -> Swat 제거 | 조사 01 체크리스트 I1~I9 |  |

## 정해야 할 것

- [x] **Mixamo 약관 원문 검토 전에 Mixamo 캐릭터를 출시 빌드에 넣어도 되는가?** (확정) → 검토 전까지 보류 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 검토 전까지 보류 / 그냥 진행  · 추천: 검토 전까지 보류
  - 근거: 비 CC0, 약관 원문 미확인(법적 판단은 사람의 일). 개발용은 가능하되 출시 전 체크리스트 항목으로.
- [x] **플레이어 기본 아바타는 CC0(UBC)로 가고 Mixamo는 봇만 쓰는가?** (확정) → 예 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 예 / Mixamo도 플레이어에게 개방  · 추천: 예
  - 근거: 약관 위험을 봇 한쪽으로 가두고, 폴백(UBC 단독)이 항상 있다.
- [x] **스태미나를 둘 것인가?** (확정) → 없음 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 없음 유지 / 질주 스태미나 추가  · 추천: 없음 유지
  - 근거: 현재 질주 무제한이 전투 흐름에 맞는지 플레이 피드백 필요. 도시·산 맵 크기 결정 후 재검토.
- [x] **새 캐릭터의 서기 키와 눈 높이 기준은?** (확정) → 키 1.8 / 눈 1.6 유지(캡슐 기준) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 키 1.8 / 눈 1.6 유지 / 키 1.75 / 눈 1.65 등 새 비율에 맞춰 조정  · 추천: 키 1.8 / 눈 1.6 유지(캡슐 기준)
  - 근거: 캡슐·피격 구역·좌석·문 높이가 모두 이 값에 묶여 있다. 모델을 캡슐에 맞추는 편이 변경 범위가 작다.


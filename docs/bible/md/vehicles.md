# 차량

> 상태: 확정 · 요약: 운전 가능 차량 7종(승용 5 + 오토바이 2)과 정적 소품 차량 1팩. 모델은 전부 Sketchfab CC BY 4.0이라 크레딧 화면이 필수. 엔진·경적·타이어·충돌음은 파일 없이 코드로 합성한 임시 소리다.

## 현재 구성

차량은 한 프레임워크(Vehicle)에 Car와 Motorcycle 두 종류가 올라가 있다. 모델별 차이는 질량·좌석 배치·무게중심 정도이고, 최고속도·가속·서스펜션 등은 종류별(차/오토바이) 한 벌의 rules.json 값을 공유한다.

월드에는 주차 지점마다 1대가 놓인다: 차 6대 + 오토바이 4대(총 10대), 거기에 정적 주차·폐차 소품(물리 없음) 최대 28대.

- 좌석 번호는 네트워크로 전송되므로 순서를 바꾸지 않는다(0 운전석, 1 조수석, 2·3 뒷좌석).
- 차량 사운드는 모두 코드 합성(VehicleAudio)이며 소리 파일이 없다 → 정식 소리 후보 조사 필요.
- 인게임 표시 이름은 코드에 없다(프리팹 이름과 크레딧 화면 제목만 있음).

## 운전 가능 차량

| 상태 | 모델(프리팹 이름) | 종류 | 차체 | 좌석 | 페인트 틴트 | 질량 kg | 무게중심 높이 m | 라이선스 | 메모 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|
| 확정 | FairheavenLT80 | 차 | 세단 | 4 (운전+조수+뒷 2) | #751a14 | 1250 | 기본 0.38 | CC BY 4.0 (V-0001, DanielZhabotinsky) | 틴트는 코드의 Color 값(0.46,0.10,0.08). 텍스처와 곱해지므로 실제 화면 색은 다를 수 있다. | Assets/_Project/Editor/VehicleModels.cs:220-224 |
| 확정 | FairheavenSW84 | 차 | 왜건 | 4 | #a89978 | 1350 | 기본 0.38 | CC BY 4.0 (V-0002) | 트렁크 덮개는 분리 부품에서 제외(해치가 뒷유리 포함). | Assets/_Project/Editor/VehicleModels.cs:225-229 |
| 확정 | LightbodyLifted85 | 차 | 리프트업 픽업(일반 캡) | 2 (적재함 탑승 불가, ADR-077) | #334730 | 1900 | 0.62 | CC BY 4.0 (V-0003) | 좌석 2개만 측정됨. 적재함에는 못 탄다. | Assets/_Project/Editor/VehicleModels.cs:230-236 |
| 확정 | Shvan92 | 차 | 패널 밴 | 2 (뒤는 맨 화물 바닥) | #c7c7bf | 1750 | 0.6 | CC BY 4.0 (V-0004) | 화물칸은 좌석이 아니다. | Assets/_Project/Editor/VehicleModels.cs:237-242 |
| 확정 | GoblinV12 | 차 | 스포츠카 | 2 |  | 1450 | 0.32 | CC BY 4.0 (V-0005, ollitei, 수정함) | 알베도 텍스처를 코드(VehicleArt)로 수정(번호판·웹주소 덧칠). 색은 텍스처 그대로. 핸들이 모델에 없어 코드로 만든 핸들을 쓴다. | Assets/_Project/Editor/VehicleModels.cs:244-262 |
| 확정 | DirtBike | 오토바이 | 오프로드 | 2 (운전+뒤) |  | 190 | 0.55 | CC BY 4.0 (V-0006, danilloribeiro) | 7색 텍스처 중 빨강만 사용. 모델 질량 190인데 rules의 Motorcycles.MassKg는 230: 어느 값이 실제 물리에 쓰이는지는 확인 필요(open). | Assets/_Project/Editor/VehicleModels.cs:740-746 |
| 확정 | RoadMotorcycle | 오토바이 | 로드 | 2 |  | 230 | 0.55 | CC BY 4.0 (V-0007, animanyarty) | 부품 이름이 없어 경계 상자로 분류해 병합. | Assets/_Project/Editor/VehicleModels.cs:799-800 |

## 정적 소품 차량

| 상태 | 이름 | 용도 | 라이선스 | 메모 | 근거 |
|---|---|---|---|---|---|
| 확정 | PassengerCarPack | 마을 주차·폐차 소품(물리 없음, 충돌체만). 폐차 변형은 녹색 갈색 톤, 바퀴 없음, 기울임 | CC BY 4.0 (V-0008, comrade1280) | 월드에 최대 28대(MaxProps), 주차 후보지의 55%(PropShare). | docs/ASSET_LEDGER.md:54 |

## 주행 수치 (rules.json, 모델 공통)

| 상태 | 항목 | 차 | 오토바이 | 단위 | 근거 |
|---|---|---|---|---|---|
| 확정 | 최고속도(도로) | 90 | 105 | km/h | Assets/_Project/Data/Resources/GameData/rules.json Vehicles/Motorcycles |
| 확정 | 부스트 최고속도(Shift) | 105 | 120 | km/h | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 후진 최고속도 | 25 | 8 | km/h | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 출발 가속(최고속도에서 0으로 감소) | 5.5 | 7.5 | m/s² | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 제동 | 9 | 8 | m/s² | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 조향 최대각(정지/최고속) | 32 / 6 | 30 / 4 | 도 | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 바퀴 반지름(기본값, 모델은 실측으로 덮어씀) | 0.34 | 0.31 | m | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 타이어 접지 계수 | 1.05 | 1.1 |  | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 탑승 거리 / 탑승 시간 / 자리 바꾸기 | 3m / 0.6s / 0.5s | 같음 |  | Assets/_Project/Scripts/Core/VehicleRules.cs:40-42 |
| 확정 | 하차 피해(이 속도 이하 안전 / 이상 즉사) | 30 / 65 | 30 / 65 | km/h | Assets/_Project/Data/Resources/GameData/rules.json ExitSafeKmh/ExitLethalKmh |
| 확정 | 전복 시 자동 복구 | 5초 뒤 2.5초 | 5초 뒤 2초 |  | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 침수 정지 수심 | 0.8 | 0.6 | m | Assets/_Project/Data/Resources/GameData/rules.json |
| 확정 | 오토바이 기울기/충돌 |  | 기울기 35도, 충돌 속도 9m/s 이상이면 탑승자 이탈 |  | Assets/_Project/Data/Resources/GameData/rules.json Motorcycles |

## 연료

| 상태 | 항목 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 탱크 | 0~1 비율(리터 단위 없음) | 엔진 켜진 채 공회전 25분, 풀스로틀 가산 약 5분. 부스트 시 가산분 x2. 오토바이 x0.7 | Assets/_Project/Scripts/Core/Fuel.cs:20-31 |
| 확정 | 시작 연료 | 35%~90% (차량별 시드) |  | Fuel.cs:24-25 |
| 확정 | 경고(HUD 빨강) | 15% 이하 |  | Fuel.cs:27 |
| 확정 | 제리캔 | 5초 주입, 1통 = 탱크 60%, 사거리 1.6m, 10km/h 이하 | 피해·사격·무기 교체·이동하면 중단 | Fuel.cs:30-37 |
| 확정 | 경적 소음 | 봇이 60m까지 듣는다, 1초마다 갱신 |  | Fuel.cs:40-41 |

## 내구도·폭발·피해

| 상태 | 항목 | 차 | 오토바이 | 메모 | 근거 |
|---|---|---|---|---|---|
| 확정 | 체력 | 1000 | 450 | 부위 배율: 엔진 100%, 차체 75%, 지붕 50% (ADR-067) | Assets/_Project/Scripts/Core/VehicleDamageRules.cs:24-40 |
| 확정 | 연기 단계 | 50% 이하 흰 연기, 25% 이하 검은 연기(최고속 x0.85) | 같음 | 0이 되면 5초 불탄 뒤 폭발 | VehicleDamageRules.cs:42-47 |
| 확정 | 폭발 | 피해 100, 안쪽 2.5m, 바깥 8m. 탑승 중이면 250 | 같음 | 다른 차량에 x4 | VehicleDamageRules.cs:50-57 |
| 확정 | 수류탄 | 폭발 피해 x6, 1.2m 이내면 즉시 파괴 | 같음 |  | VehicleDamageRules.cs:60-62 |
| 확정 | 충돌 피해 | 6m/s 이상, m/s당 22 | m/s당 14 | 유리: 1발 금, 2발 파손(6장) | VehicleDamageRules.cs:66-69 |
| 확정 | 타이어 | 체력 30, 펑크 시 접지 x0.55·차체 9cm 하강·최고속 x0.9 | 접지 x0.45 |  | VehicleDamageRules.cs:72-78 |
| 확정 | 분리 부품(후드·트렁크·앞뒤 범퍼) | 체력 120, 절반이면 덜렁, 0이면 탈락(20초 뒤 사라짐) | 없음 |  | VehicleDamageRules.cs:80-85 |
| 확정 | 치고 지나가기(로드킬) | 10km/h 초과분 1km/h당 2.5, 질량 제곱근 비례(기준 1350kg) | 같음 | 밀침 최대 12m/s | Assets/_Project/Scripts/Core/VehicleRules.cs:94-99 |

## 스폰 규칙

| 상태 | 항목 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 월드 배치 | 차 6 + 오토바이 4 | 주차 지점당 1대, 간격 40m. 차 모델은 5종을 순환(6번째는 세단 반복), 오토바이는 2종 교대. | Assets/_Project/Editor/VehicleBuilder.cs:382-407 |
| 확정 | 도로 판정 | 4m 셀 도로 마스크; 도로 밖 x0.85 |  | Assets/_Project/Scripts/Net/VehicleSpawner.cs:12-33 |
| 확정 | 테스트 박스 | 차 5 + 오토바이 2 고정 배치 |  | VehicleBuilder.cs:338-355 |
| 제안 | 50명 판 목표 대수 | 산·도시 확장에 비례해 증가(제안: 차 12~16 + 오토바이 6~8) | 맵 면적 결정 후 확정. |  |

## 사운드 현황

| 상태 | 소리 | 클립 | 방식 | 파일 | 메모 | 근거 |
|---|---|---|---|---|---|---|
| 확정 | 엔진 루프 | VehicleEngine | 코드 합성(5단 가상 기어, 피치/음량 EngineNote) |  | 임시. 후보 조사 필요(차/오토바이 별도). | Assets/_Project/Scripts/Gameplay/VehicleAudio.cs:153 |
| 확정 | 휘파람(변속기/터빈) | VehicleWhine | 코드 합성 |  | 임시 | VehicleAudio.cs:154 |
| 확정 | 경적 | VehicleHorn | 코드 합성 두 음 루프, 오토바이 피치 1.35 |  | 임시 | VehicleAudio.cs:159 |
| 확정 | 타이어 끌림 | TyreScreech | 코드 합성, 노면·미끄러짐에 따라 음량 |  | 임시 | VehicleAudio.cs:155 |
| 확정 | 바람 | VehicleWind | 코드 합성 |  | 임시 | VehicleAudio.cs:156 |
| 확정 | 충돌 | VehicleCrunch | 코드 합성, 충돌 깊이로 음량 |  | 임시 | VehicleAudio.cs:157 |
| 확정 | 기어 변속 | GearClunk | 코드 합성 |  | 임시 | VehicleAudio.cs:158 |
| 미정 | 문 여닫기·시동 키 |  | 없음 |  | 후보 조사 필요 |  |
| 미정 | 차량 폭발 |  | 확인 필요(전역 폭발음 재사용 여부) |  | 후보 조사 필요 |  |

## 라이선스·크레딧

| 상태 | ledger | 작품 | 작가 | 라이선스 | 크레딧 필수 | 메모 | 근거 |
|---|---|---|---|---|---|---|---|
| 확정 | V-0001..8 | 차량 8건(세단·왜건·픽업·밴·스포츠카·오토바이 2·소품팩) | DanielZhabotinsky(4), ollitei, danilloribeiro, animanyarty, comrade1280 | CC BY 4.0 | 예 - 메인 메뉴 크레딧 화면 + 폴더 LICENSE.txt | 수정함: Goblin(텍스처)·병합·머티리얼 교체. 배지 메시와 미국 번호판 시트는 쓰지 않고 오리지널 한국식 번호판으로 대체. | docs/ASSET_LEDGER.md:47-54; Assets/_Project/Scripts/Core/Credits.cs:16-24 |

## 실존 이름 주의

작품 제목(Fairheaven, Lightbody, Shvan 등)은 실존 제조사·차종을 패러디한 이름처럼 읽힐 수 있다. 크레딧 화면에는 작가 표기를 위해 제목을 써야 하지만, 게임 안(HUD·상점·UI) 표시 이름은 우리가 지은 가상 이름을 쓰는 것이 안전하다. 법적 판단은 사람의 일(CLAUDE.md 규칙 8)이므로 RISKS_OPEN_QUESTIONS에 올려둔다.

- Dirt bike 모델 파일 내부 머티리얼 이름에 실존 타이어 브랜드(Mishelin) 철자가 있다. 화면에는 안 나옴(ledger V-0007 기록).
- 'V12 Goblin'은 가상 이름이라 문제 없어 보이나 외형이 실존 슈퍼카와 닮았는지는 사람이 한 번 본다.
- 인게임 이름 후보(제안): 세단 '해안 세단', 왜건 '여행 왜건', 픽업 '농장 픽업', 밴 '배달 밴', 스포츠 '질주', 오토바이 '산길 바이크'/'도로 바이크'.

## 정해야 할 것

- [x] **차량 인게임 표시 이름은 어떻게?** (확정) → 종류만 표시(세단/밴 등) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 가상 한국어 이름을 새로 짓는다 / 작품 제목을 그대로 쓴다 / 종류만 표시(세단/밴 등)  · 추천: 종류만 표시(세단/밴 등)
  - 근거: 실존 차종 패러디 이름 위험을 피하고 구현이 가장 작다. 필요해지면 가상 이름을 추가.
- [x] **차량 색 변형을 둘 것인가?** (확정) → 머티리얼 틴트로 3~5색 변형 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 모델당 1색 고정(현재) / 머티리얼 틴트로 3~5색 변형 / 없음  · 추천: 머티리얼 틴트로 3~5색 변형
  - 근거: 차량 수가 적어 같은 차가 반복된다. 틴트 한 값이면 모델 추가 없이 변화가 생긴다. 법적 위험 없음.
- [x] **50명 판에서 차량 대수는?** (확정) → 면적 비례 증가(차 12~16, 바이크 6~8) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 10대 유지 / 면적 비례 증가(차 12~16, 바이크 6~8) / 없음  · 추천: 면적 비례 증가(차 12~16, 바이크 6~8)
  - 근거: 맵이 커지면 이동 수단이 없을 때 템포가 느려진다. 맵 크기 결정 뒤 확정.
- [x] **차량 소리를 파일로 바꿀 것인가?** (확정) → CC0 소리 파일 후보 조사 후 교체 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 합성 유지 / CC0 소리 파일 후보 조사 후 교체  · 추천: CC0 소리 파일 후보 조사 후 교체
  - 근거: 합성 소리는 임시이고 체감 품질 약점. 오디오 트랙에서 후보를 조사한다. 사운드가 구매 필요하면 blocked.
- [x] **오토바이 질량 190 vs 230 중 무엇이 물리에 쓰이는가?** (확정) → rules 값을 데이터 한 곳으로 통일 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 모델 값 / rules 값  · 추천: rules 값을 데이터 한 곳으로 통일
  - 근거: VehicleModels와 rules.json에 값이 둘이라 헷갈린다. 코드에서 어느 쪽이 최종인지 확인 필요.


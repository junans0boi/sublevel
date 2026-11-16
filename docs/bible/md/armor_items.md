# 방어구·장비·소모품·투척·근접

> 상태: 확정 · 요약: 헬멧·조끼 3티어, 배낭 3종, 회복·부스트 6종, 투척 4종, 열쇠·연료통의 수치와 루팅 비중. 근접 무기는 [총기] 참고.

## 정의

방어구는 헬멧(머리)·조끼(몸통) 두 칸, 각 3티어. 팔다리는 보호받지 않는다. 총·폭발만 방어구가 막고 낙하·자기장 피해는 그대로 받는다(Armor.cs).

- 루팅 비중은 loot.json 가중치(상대값).
- 전 아이템 모델은 코드 생성(LootMeshes 등) — 외부 에셋 없음. 아이콘은 ItemIconBuilder가 만든 PNG.
- 무게 표는 rules.json Carry 값(기본 한도 60, 조끼 +25, 배낭 +75/+100/+125).

## 방어구·장비·소모품·투척

| 상태 | 게임 내 이름 | 종류 | 등급/id | 수치 | 무게 | 루팅 비중 | 아이콘 | 모델 | 소리 (현재) | 설계 역할 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 확정 | 헬멧 1 | 방어구(머리) | 1 | 머리 피해 30% 흡수, 내구 80 | 0 | 6 | ../../Assets/_Project/Resources/Icons/helmet_1.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 입문 방어구. | Assets/_Project/Data/Resources/GameData/rules.json:47-52; Assets/_Project/Data/Resources/GameData/loot.json:25 |
| 확정 | 헬멧 2 | 방어구(머리) | 2 | 40% 흡수, 내구 150 | 0 | 3 | ../../Assets/_Project/Resources/Icons/helmet_2.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 중급. | Assets/_Project/Data/Resources/GameData/rules.json:47-52; Assets/_Project/Data/Resources/GameData/loot.json:26 |
| 확정 | 헬멧 3 | 방어구(머리) | 3 | 55% 흡수, 내구 230 | 0 | 1 | ../../Assets/_Project/Resources/Icons/helmet_3.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 최상급, 희귀. | Assets/_Project/Data/Resources/GameData/rules.json:47-52; Assets/_Project/Data/Resources/GameData/loot.json:27 |
| 확정 | 조끼 1 | 방어구(몸) | 1 | 몸통 피해 30% 흡수, 내구 200. 소지 무게 +25 | 0 | 6 | ../../Assets/_Project/Resources/Icons/vest_1.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 입문 방어구. | Assets/_Project/Data/Resources/GameData/rules.json:53-57; Assets/_Project/Data/Resources/GameData/loot.json:28; Carry VestBonus1 |
| 확정 | 조끼 2 | 방어구(몸) | 2 | 40%, 내구 220. 소지 +25 | 0 | 3 | ../../Assets/_Project/Resources/Icons/vest_2.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 중급. | Assets/_Project/Data/Resources/GameData/rules.json:53-57; Assets/_Project/Data/Resources/GameData/loot.json:29 |
| 확정 | 조끼 3 | 방어구(몸) | 3 | 55%, 내구 250. 소지 +25 | 0 | 1 | ../../Assets/_Project/Resources/Icons/vest_3.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 최상급, 희귀. | Assets/_Project/Data/Resources/GameData/rules.json:53-57; Assets/_Project/Data/Resources/GameData/loot.json:30 |
| 확정 | 배낭 1 | 장비 | Lv1 | 소지 한도 +75 (기본 60) | 0 | 6 | ../../Assets/_Project/Resources/Icons/backpack_1.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 소지량 확대. | Assets/_Project/Data/Resources/GameData/rules.json:76-79; Assets/_Project/Data/Resources/GameData/loot.json:31-33 |
| 확정 | 배낭 2 | 장비 | Lv2 | 소지 한도 +100 (기본 60) | 0 | 3 | ../../Assets/_Project/Resources/Icons/backpack_2.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 소지량 확대. | Assets/_Project/Data/Resources/GameData/rules.json:76-79; Assets/_Project/Data/Resources/GameData/loot.json:31-33 |
| 확정 | 배낭 3 | 장비 | Lv3 | 소지 한도 +125 (기본 60) | 0 | 1 | ../../Assets/_Project/Resources/Icons/backpack_3.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 소지량 확대. | Assets/_Project/Data/Resources/GameData/rules.json:76-79; Assets/_Project/Data/Resources/GameData/loot.json:31-33 |
| 제안 | 열쇠 | 장비 | - | 문/상자용 키(LootKind.Key) | 0 | 0 | ../../Assets/_Project/Resources/Icons/key.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 열쇠 아이템. 루팅 표에는 없음. | Loot.cs:5; ItemIcons.cs:19 |
| 확정 | 연료통 | 장비 | - | 차량 연료 +60%(JerrycanAmount 0.6), 사용 5초, 무게 20 | 20 | 3 | ../../Assets/_Project/Resources/Icons/jerrycan.png | LootMeshes 코드 생성 | (고유 소리 없음 — proposed: 후보 조사 필요) | 차량 연료 보충. | Assets/_Project/Data/Resources/GameData/rules.json:Fuel; Assets/_Project/Data/Resources/GameData/loot.json:22+ |
| 확정 | 붕대 | 회복 | - | 4초 사용, +10 (75까지) 반복. 이동 x0.6 | 1.0 | 14 | ../../Assets/_Project/Resources/Icons/bandage.png | LootMeshes 코드 생성 + 손에 드는 HandPropView(코드 생성) | (고유 소리 없음 — proposed: 후보 조사 필요) | 저비용 소량 회복. | Assets/_Project/Data/Resources/GameData/rules.json:67; Heal.cs:30-40; Assets/_Project/Data/Resources/GameData/loot.json:15-21 |
| 확정 | 응급키트 | 회복 | - | 6초, +75 (75까지) | 3.0 | 5 | ../../Assets/_Project/Resources/Icons/firstaid.png | LootMeshes 코드 생성 + 손에 드는 HandPropView(코드 생성) | (고유 소리 없음 — proposed: 후보 조사 필요) | 중간 회복. | Assets/_Project/Data/Resources/GameData/rules.json:68; Heal.cs:30-40; Assets/_Project/Data/Resources/GameData/loot.json:15-21 |
| 확정 | 구급상자 | 회복 | - | 8초, 체력 100까지 전부 | 6.0 | 3 | ../../Assets/_Project/Resources/Icons/medkit.png | LootMeshes 코드 생성 + 손에 드는 HandPropView(코드 생성) | (고유 소리 없음 — proposed: 후보 조사 필요) | 완전 회복. | Assets/_Project/Data/Resources/GameData/rules.json:69; Heal.cs:30-40; Assets/_Project/Data/Resources/GameData/loot.json:15-21 |
| 확정 | 활력 음료 | 부스트 | - | 4초, 부스트 게이지 +40 | 1.0 | 8 | ../../Assets/_Project/Resources/Icons/drink.png | LootMeshes 코드 생성 + 손에 드는 HandPropView(코드 생성) | (고유 소리 없음 — proposed: 후보 조사 필요) | 부스트 소량. | Assets/_Project/Data/Resources/GameData/rules.json:70; Heal.cs:30-40; Assets/_Project/Data/Resources/GameData/loot.json:15-21 |
| 확정 | 집중 정제 | 부스트 | - | 6초, 게이지 +60 | 2.0 | 5 | ../../Assets/_Project/Resources/Icons/pill.png | LootMeshes 코드 생성 + 손에 드는 HandPropView(코드 생성) | (고유 소리 없음 — proposed: 후보 조사 필요) | 부스트 중간. | Assets/_Project/Data/Resources/GameData/rules.json:71; Heal.cs:30-40; Assets/_Project/Data/Resources/GameData/loot.json:15-21 |
| 확정 | 각성 주사 | 부스트 | - | 6초, 게이지 +100 | 2.0 | 2 | ../../Assets/_Project/Resources/Icons/syringe.png | LootMeshes 코드 생성 + 손에 드는 HandPropView(코드 생성) | (고유 소리 없음 — proposed: 후보 조사 필요) | 부스트 최대. | Assets/_Project/Data/Resources/GameData/rules.json:72; Heal.cs:30-40; Assets/_Project/Data/Resources/GameData/loot.json:15-21 |
| 제안 | 파편 수류탄 | 투척 | - | 신관 5초. 폭발: 3.5m 안 100, 8.5m 40, 10m 0 (엎드리면 -20%, 조끼 흡수율만큼 감쇠) | 3.0 | (루팅 표에 항목 없음 — 확인 필요) | ../../Assets/_Project/Resources/Icons/grenade.png | 투척체 코드 생성(ThrowableView/HandPropView), 에셋 아님 | 폭발/연막 효과음: shot_shotgun.wav를 피치 0.55로 낮춰 재사용(ThrowableView _blastClip, TestBoxBuilder.cs:143-144). 섬광은 피치 x1.8·볼륨 0.6. 화염은 FireCrackle 코드 합성. — proposed: replace(전용 폭발음 후보 조사) | 범위 피해, 건물 청소. | Assets/_Project/Data/Resources/GameData/rules.json:61; Throwables.cs |
| 확정 | 연막탄 | 투척 | - | 신관 1초. 반경 5m 연막 25초(마지막 5초 옅어짐) | 3.0 | 3 | ../../Assets/_Project/Resources/Icons/smoke.png | 투척체 코드 생성(ThrowableView/HandPropView), 에셋 아님 | 폭발/연막 효과음: shot_shotgun.wav를 피치 0.55로 낮춰 재사용(ThrowableView _blastClip, TestBoxBuilder.cs:143-144). 섬광은 피치 x1.8·볼륨 0.6. 화염은 FireCrackle 코드 합성. — proposed: replace(전용 폭발음 후보 조사) | 시야 차단·이동 엄호. | Assets/_Project/Data/Resources/GameData/rules.json:83; Throwables.cs |
| 확정 | 섬광탄 | 투척 | - | 신관 2초. 실명: 20m까지, 정면 5.5초~거리 따라 1초(뒤 x0.3). 6.5m 안 벽 너머 이명. 소리도 감쇠 | 2.0 | 3 | ../../Assets/_Project/Resources/Icons/flash.png | 투척체 코드 생성(ThrowableView/HandPropView), 에셋 아님 | 폭발/연막 효과음: shot_shotgun.wav를 피치 0.55로 낮춰 재사용(ThrowableView _blastClip, TestBoxBuilder.cs:143-144). 섬광은 피치 x1.8·볼륨 0.6. 화염은 FireCrackle 코드 합성. — proposed: replace(전용 폭발음 후보 조사) | 실내 돌입 보조. | Assets/_Project/Data/Resources/GameData/rules.json:85; Throwables.cs |
| 확정 | 화염병 | 투척 | - | 신관 4초(공중 비행 한도). 반경 3m 화염 10초, 초당 12.5, 불붙으면 4초 초당 10 | 3.0 | 3 | ../../Assets/_Project/Resources/Icons/molotov.png | 투척체 코드 생성(ThrowableView/HandPropView), 에셋 아님 | 폭발/연막 효과음: shot_shotgun.wav를 피치 0.55로 낮춰 재사용(ThrowableView _blastClip, TestBoxBuilder.cs:143-144). 섬광은 피치 x1.8·볼륨 0.6. 화염은 FireCrackle 코드 합성. — proposed: replace(전용 폭발음 후보 조사) | 지역 거부. | Assets/_Project/Data/Resources/GameData/rules.json:89; Throwables.cs |
| 확정 | 근접 무기 4종 + 주먹 | 근접 | - | [총기] 섹션 참고 | 0 |  |  | MeleeMeshes 코드 생성 | [총기] 섹션 참고 | - | docs/bible/data/weapons.json |

## 소리 방향 (소모품·투척)

회복·방어구는 전용 소리가 없다(후보 조사 필요). 투척 폭발음은 산탄총 발사음을 피치 0.55로 낮춘 임시 재사용이라 어색할 수 있다 — proposed: replace.


## 방어구·소모품 — 정해야 할 것

- [x] **방어구 티어 수와 수치** (확정) → 현재 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 현재(헬멧/조끼 3티어, 흡수 30/40/55%) / 티어 4 추가(탈것 대응) / 수치 하향(TTK 늘리기)  · 추천: 현재 유지
  - 근거: 방어구 수치는 PUBG 비율 인용(ADR-091). 조끼 3티어(55%)면 Warden 몸통 TTK 약 2배 → 총기 TTK 목표와 함께 조정.
- [x] **파편 수류탄이 루팅 표에 없는 문제** (확정) → 루팅 표에 추가 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 루팅 표에 추가(비중 5 제안) / 현재처럼 스폰/보급상자로만  · 추천: 루팅 표에 추가
  - 근거: loot.json에 Kind 10 항목이 없음. 보급상자(supply.json) 쪽은 확인 못 함.
- [x] **회복류 종류 정리** (확정) → 현재 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 현재 6종(붕대/응급키트/구급상자 + 부스트 3) / 부스트를 1~2종으로 줄임  · 추천: 현재 유지
  - 근거: 부스트 3종이 겹침(+40/+60/+100). 디자인상 구분이 약하면 1종 통합 가능.
- [x] **무게 시스템(Carry) 균형** (확정) → 현재 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 현재 유지 / 조정  · 추천: 현재 유지
  - 근거: 기본 60, 조끼 +25(티어 무관), 배낭 +75/100/125. 구급상자 6, 수류탄 3 — 한도 대비 플레이 테스트로 확인.
- [x] **헬멧·조끼 티어별 이름(현재 '헬멧 1/2/3')** (확정) → 번호 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 번호 유지 / 가상 이름 부여  · 추천: 번호 유지
  - 근거: 실존 제품명 금지 규칙 때문에 번호가 안전.


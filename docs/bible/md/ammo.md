# 탄

> 상태: 확정 · 요약: 4개 구경(9mm·5.56mm·12게이지·7.62mm)과 그 사용 총·루팅 수량·무게.

## 정의

탄은 구경별로 나뉘어 인벤토리에 쌓인다(ADR-054). enum 순서는 데이터·네트워크에 저장되므로 바꾸지 않는다. 총 한 자루는 한 구경만 쓴다.

- 루팅 비중은 loot.json 가중치(탄 항목 합계 52, 전체 대비 아님).
- 소지 무게는 Carry 규칙의 개당 값(탄 1발 기준).

## 탄 목록

| 상태 | 게임 내 이름 | 코드 enum | 사용 총 (id) | 루팅 기본 수량 | 루팅 비중 | 무게(개당) | 아이콘 | 실제 대응(참고) | 설계 역할 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|
| 확정 | 9mm | Pistol9 = 0 | VK-9(1), Ordo(2) | 30 | 18 | 0.2 | ../../Assets/_Project/Resources/Icons/ammo_9mm.png | 권총탄 구경 일반 명칭 | 권총·SMG 공용. 가장 흔함. | WeaponDef.cs:6-14; Assets/_Project/Data/Resources/GameData/loot.json:17-20; Assets/_Project/Data/Resources/GameData/rules.json:Carry(줄 ~80) |
| 확정 | 5.56mm | Rifle556 = 1 | Warden(3) | 30 | 14 | 0.3 | ../../Assets/_Project/Resources/Icons/ammo_556.png | 소총탄 구경 일반 명칭 | 소총 전용. | WeaponDef.cs:6-14; Assets/_Project/Data/Resources/GameData/loot.json:17-20; Assets/_Project/Data/Resources/GameData/rules.json:Carry(줄 ~80) |
| 확정 | 12게이지 | Shotgun12 = 2 | Breacher(4) | 8 | 12 | 0.8 | ../../Assets/_Project/Resources/Icons/ammo_12ga.png | 산탄 구경 일반 명칭 | 산탄 전용, 무게 가장 큼. | WeaponDef.cs:6-14; Assets/_Project/Data/Resources/GameData/loot.json:17-20; Assets/_Project/Data/Resources/GameData/rules.json:Carry(줄 ~80) |
| 확정 | 7.62mm | Rifle762 = 3 | Sentinel(5) | 15 | 8 | 0.5 | ../../Assets/_Project/Resources/Icons/ammo_762.png | 대구경 소총탄 일반 명칭 | 저격 전용. 가장 희귀. | WeaponDef.cs:6-14; Assets/_Project/Data/Resources/GameData/loot.json:17-20; Assets/_Project/Data/Resources/GameData/rules.json:Carry(줄 ~80) |

## 탄 — 정해야 할 것

- [x] **탄 종류를 늘릴 것인가(현재 4종: 9/5.56/12게이지/7.62)** (확정) → 4종 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 4종 유지 / 특수탄(관통·소이 등) 추가 / 대구경 .50 추가  · 추천: 4종 유지
  - 근거: 범위 최소화. 7.62는 DMR 추가 시 사용처가 늘어남. (2026-11-16 총기 구성 확정에 따라 신규 탄 종류 없이 9mm / 5.56 / 7.62 / 12게이지 4종으로 충분하다고 본다 — 확인 필요.)
- [x] **탄 무게/소지량 균형** (확정) → 현재 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 현재 유지(9mm 0.2, 5.56 0.3, 12ga 0.8, 7.62 0.5) / 재조정  · 추천: 현재 유지
  - 근거: 배낭 기본 60 + 조끼/배낭 보너스(25/75~125)와 맞물림 — 방어구 섹션 참고.
- [x] **탄 종류 표기(숫자 구경)와 실존 총기 연상** (확정) → 일반 구경 표기 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 일반 구경 표기 유지 / 가상 표기(예: A형/B형)  · 추천: 일반 구경 표기 유지
  - 근거: 구경 수치는 상표가 아니고 현실감 전달에 유리.


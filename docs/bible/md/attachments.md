# 부착물

> 상태: 확정 · 요약: 총구·손잡이·개머리판·탄창 8종과 조준경 8종의 효과·장착 가능 총·루팅 비중.

## 정의

부착물은 5칸(조준경·총구·손잡이·개머리판·탄창)이고, 효과는 곱셈으로 쌓인다(ADR-100). 총마다 받는 칸이 optics.json 칸표로 정해진다. 조준경·부착물은 id 1~16이 같은 번호 공간을 쓴다.

- 표시는 인벤토리 툴팁 Attachments.Describe 형식(위 반동 -15% 등)과 같다.
- 모델은 전부 코드에서 만든다. 라이선스 문제 없음(AI 생성 에셋 아님).

## 부착물 (조준경 제외)

| 상태 | 게임 내 이름 | id | 칸 | 효과 | 장착 가능 총 | 루팅 비중 | 아이콘 | 모델 | 소리 | 설계 역할 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 확정 | 반동 보정기 | 9 | 총구 | 위 반동 -12%, 좌우 -10% | VK-9, Ordo, Warden, Breacher, Sentinel (전부) | 3 | ../../Assets/_Project/Resources/Icons/att_comp.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 반동 억제 기본형. | Assets/_Project/Data/Resources/GameData/optics.json:20; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |
| 제안 | 화염 분산기 | 10 | 총구 | 위 반동 -5%, 좌우 -5% | VK-9, Ordo, Warden, Sentinel | 3 | ../../Assets/_Project/Resources/Icons/att_flash.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 약한 반동 보정. 현재 섬광 시각효과는 없음(확인 필요). | Assets/_Project/Data/Resources/GameData/optics.json:21; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |
| 확정 | 소음기 | 11 | 총구 | 위 반동 -3%, 총성 들리는 거리 1/4 | 전부 | 2 | ../../Assets/_Project/Resources/Icons/att_supp.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 은신 플레이. | Assets/_Project/Data/Resources/GameData/optics.json:22; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |
| 확정 | 수직 손잡이 | 12 | 손잡이 | 위 반동 -15% | Ordo, Warden | 4 | ../../Assets/_Project/Resources/Icons/att_grip_v.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 상하 반동 전문. | Assets/_Project/Data/Resources/GameData/optics.json:23; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |
| 확정 | 각진 손잡이 | 13 | 손잡이 | 위 반동 -5%, 좌우 -20% | Ordo, Warden | 3 | ../../Assets/_Project/Resources/Icons/att_grip_a.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 좌우 반동 전문. | Assets/_Project/Data/Resources/GameData/optics.json:24; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |
| 확정 | 곡선 손잡이 | 14 | 손잡이 | 반동 회복 속도 +35% | Ordo, Warden | 3 | ../../Assets/_Project/Resources/Icons/att_grip_c.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 연사 간 회복. | Assets/_Project/Data/Resources/GameData/optics.json:25; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |
| 확정 | 보강 개머리판 | 15 | 개머리판 | 반동 위 -8%, 좌우 -8%, 회복 +10% | Ordo, Warden, Breacher, Sentinel | 3 | ../../Assets/_Project/Resources/Icons/att_stock.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 전반 안정. | Assets/_Project/Data/Resources/GameData/optics.json:26; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |
| 확정 | 확장 탄창 | 16 | 탄창 | 탄창 x1.33, 재장전 시간 x1.1 | VK-9, Ordo, Warden, Sentinel | 3 | ../../Assets/_Project/Resources/Icons/att_mag.png | 코드 생성(LootMeshes/OpticMeshes, 에셋 아님) | (고유 소리 없음). 소음기는 총성이 들리는 거리를 1/4로 줄임(SoundRules.cs:80) — 소리 파일 자체는 안 바뀜. | 탄창 크기. | Assets/_Project/Data/Resources/GameData/optics.json:27; 칸표 Assets/_Project/Data/Resources/GameData/optics.json:30-34 |

## 조준경

| 상태 | 게임 내 이름 | id | 종류 | 배율 | 감도 계수 | 장착 가능 총 | 루팅 비중 | 아이콘 | 모델 | 설계 역할 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 확정 | 반사 조준 사이트 | 1 | 반사식 | 1x | 1.0 | 전부(1~5) | 8 | ../../Assets/_Project/Resources/Icons/optic_reddot.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 기본 1배 | Assets/_Project/Data/Resources/GameData/optics.json:3 |
| 확정 | 투영 조준 사이트 | 2 | 투영식 | 1x | 1.0 | 전부(1~5) | 6 | ../../Assets/_Project/Resources/Icons/optic_holo.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 기본 1배(다른 레티클) | Assets/_Project/Data/Resources/GameData/optics.json:4 |
| 확정 | 2배율 | 3 | 배율 | 2x | 0.574 | Ordo, Warden, Breacher, Sentinel | 6 | ../../Assets/_Project/Resources/Icons/optic_2x.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 근중거리 | Assets/_Project/Data/Resources/GameData/optics.json:5 |
| 확정 | 3배율 | 4 | 배율 | 3x | 0.415 | Ordo, Warden, Sentinel | 4 | ../../Assets/_Project/Resources/Icons/optic_3x.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 중거리 | Assets/_Project/Data/Resources/GameData/optics.json:6 |
| 확정 | 4배율 | 5 | 배율 | 4x | 0.33 | Ordo, Warden, Sentinel | 3 | ../../Assets/_Project/Resources/Icons/optic_4x.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 중거리 | Assets/_Project/Data/Resources/GameData/optics.json:7 |
| 확정 | 6배율 | 6 | 배율(가변 3~6x) | 6x | 0.238 | Warden, Sentinel | 1 | ../../Assets/_Project/Resources/Icons/optic_6x.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 장거리 | Assets/_Project/Data/Resources/GameData/optics.json:8 |
| 확정 | 8배율 | 7 | 배율(가변 4~8x) | 8x | 0.189 | Sentinel | 2 | ../../Assets/_Project/Resources/Icons/optic_8x.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 저격 | Assets/_Project/Data/Resources/GameData/optics.json:9 |
| 확정 | 15배율 | 8 | 배율(가변 8~15x) | 15x | 0.115 | Sentinel | 1 | ../../Assets/_Project/Resources/Icons/optic_15x.png | 코드 생성(OpticMeshes, ADR-060), 에셋 아님 | 초장거리(저격 전용) | Assets/_Project/Data/Resources/GameData/optics.json:10 |

## 소리 방향 (부착물)

부착물은 전용 소리가 없다. 소음기만 '들리는 거리 1/4'로 반영되고 발사음 파일은 그대로다(proposed: 소음기 전용 클립 후보 조사).


## 부착물·조준경 — 정해야 할 것

- [x] **조준경 종류가 8종, 다 필요한가(1x 2종 + 2/3/4/6/8/15배)** (확정) → 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 유지 / 3/6배 제거해 5종으로 압축  · 추천: 유지
  - 근거: 15배는 저격 전용이라 의미 있음. 3·4배 차이가 작아 정리 후보.
- [x] **부착물 등급(1~3티어) 도입** (확정) → 단일 등급 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 단일 등급 유지 / 보강 개머리판/확장 탄창에 티어 추가  · 추천: 단일 등급 유지
  - 근거: 방어구는 티어 3단계인데 부착물은 1단계 — 배틀로얄 표준과 맞추려면 확장 탄창 티어가 가장 유용.
- [x] **산탄총에 맞는 초크·탄창 부착물** (확정) → 추가 안 함 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 추가 안 함 / 초크 추가  · 추천: 추가 안 함
  - 근거: Breacher는 칸이 3개뿐이지만 M1 범위 유지.
- [x] **소음기 소리를 실제로 다르게(총성 파일 교체/감쇄)** (확정) → 소음기용 별도 발사음 클립 추가 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 소음기용 별도 발사음 클립 추가 / 현재처럼 거리만 줄임  · 추천: 소음기용 별도 발사음 클립 추가
  - 근거: 현재 파일은 그대로고 거리만 1/4 — 사운드 교체 라운드에서 같이 후보 조사.


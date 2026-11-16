# 총기

> 상태: 확정 · 요약: 게임에 실제 존재하는 총 5종과 근접 무기 4종(+주먹)의 수치, 모델 출처, 아이콘, 현재 소리.

## 정의

지금 게임에 있는 모든 총기와 근접무기의 수치·모델·소리. 값은 weapons.json과 코드에서 읽은 사실이며, '추정'과 '설계 역할'은 제안이다. 데미지는 체력 100 기준이고, 방어구 감쇠 전 값이다.

- 총 5종(권총 1·SMG 1·소총 1·산탄 1·저격 1) + 근접 4종 + 맨손.
- 발사 모드: B로 연사→단발→점사 순환(무기가 가진 것만, ADR-089).
- 탄속 있는 탄도(ADR-098): 25m 이내는 즉시 판정, 그 이상은 탄속+중력 낙하.
- 관통(Penetration.cs:14-19): 나무 0.45m / 금속 0.30m / 유리 0.10m / 차량 3m까지 통과 가능, 최대 3겹, 느려진 탄 120m/s 미만이면 멈춤. 방어구는 [방어구·장비…] 섹션.

## 총기 목록 (원거리)

| 상태 | 게임 내 이름 | id | 분류 | 구경 | 피해(몸) | 머리/팔다리 배율 | 연사속도(RPM) | 발사 간격(초) | 탄창 | 재장전(남음/빈탄창, 초) | 사거리·감쇠 | 탄속(m/s) | 탄퍼짐(°)·펠릿 | 반동(상승/좌우/한계/회복) | 발사 모드 | 꺼내기(초) | 부착물 칸(id) | 추정 TTK(체력100, 몸통) | 모델 출처·길이 | 아이콘 | 손에 든 모습 | 발사음 (현재) | 재장전음 (현재) | 빈 총 소리 | 설계 역할 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 확정 | VK-9 | 1 | 권총(사이드암) | ammo:9mm | 34 | 머리 x2.1 / 몸 x1.0 / 팔다리 x0.9 | 333 | 0.18 | 12 | 1.4 / 1.8 | 80m, 감쇠 15→40m 구간에서 최대 x0.6 | 360 | 1.5° · 1발 | 위 1.4 · 좌우 0.5(우 0.1) · 한계 1.0 · 회복 16°/s | 단발 | 0.3 | 반동보정기·화염분산기·소음기·확장탄창 (9,10,11,16) | 3발 ≈ 0.36초(근거리) | Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/Pistol_1.fbx (W-0001, CC0), 길이 0.24m — 실제 권총 약 0.20m: 큰 편 아님 | ../../Assets/_Project/Resources/Icons/weapon_1.png | ../handoff/shots_2026-11-16/m15_weapons.png | shot_pistol.wav (자유 총기 라이브러리 녹음, S-0001) + 먼 층 shot_pistol_far.wav — proposed: replace | reload_pistol.wav (OGA gunreload1, S-0002) — proposed: replace | 없음(코드에 빈 총 클릭 소리 없음) — proposed: 추가 필요 | 슬롯3 사이드암. 시작 보조·저비용 근거리 폴백. | Assets/_Project/Data/Resources/GameData/weapons.json:3; 모델 CharacterKit.cs:29; 소리 TestBoxBuilder.cs:291-292; 탄속 ADR-098 |
| 확정 | Ordo | 2 | 기관단총 | ammo:9mm | 26 | 머리 x2.1 / 몸 x1.05 / 팔다리 x1.3 | 800 | 0.075 | 30 | 2.0 / 2.6 | 90m, 감쇠 12→45m 구간에서 최대 x0.5 | 400 | 3.0° · 1발 | 위 0.7 · 좌우 0.6(좌 0.2) · 한계 1.5 · 회복 14°/s | 연사 / 단발 / 2점사(간격 0.25초) | 0.45 | 전부 8종 (9~16) | 4발 ≈ 0.23초(근거리, 몸 26x1.05) | Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/SubmachineGun_1.fbx (W-0001, CC0), 길이 0.50m — 실제 SMG 0.45~0.6m: 적정 | ../../Assets/_Project/Resources/Icons/weapon_2.png | ../handoff/shots_2026-11-16/m15_weapons.png | shot_smg.wav + 먼 층 shot_smg_far.wav — proposed: replace | reload_rifle.wav (소총 재장전음을 공유 — 권총탄 SMG에 어울리는 별도 소리 없음) — proposed: replace | 없음(코드에 빈 총 클릭 소리 없음) — proposed: 추가 필요 | 근거리 고연사. 9mm 공유로 VK-9와 탄이 겹침. | Assets/_Project/Data/Resources/GameData/weapons.json:7; 모델 CharacterKit.cs:30; 소리 TestBoxBuilder.cs:291-292 |
| 확정 | Warden | 3 | 소총(불펍형) | ammo:5.56mm | 40 | 머리 x2.35 / 몸 x1.0 / 팔다리 x0.9 | 545 | 0.11 | 30 | 2.4 / 3.1 | 200m, 감쇠 40→120m 구간에서 최대 x0.7 | 880 | 1.8° · 1발 | 위 0.9 · 좌우 0.5(우 0.3) · 한계 2.0 · 회복 10°/s | 연사 / 단발 / 3점사(간격 0.3초) | 0.5 | 전부 8종 (9~16) | 3발 ≈ 0.22초(근거리), 몸 40x3=120 | Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/Bullpup_1.fbx (W-0001, CC0), 길이 0.75m — 실제 불펍 소총 0.65~0.75m: 적정 | ../../Assets/_Project/Resources/Icons/weapon_3.png | ../handoff/shots_2026-11-16/m15_weapons.png | shot_rifle.wav + 먼 층 shot_rifle_far.wav — proposed: replace | reload_rifle.wav (OGA assaultriflereload1_0) — proposed: replace | 없음(코드에 빈 총 클릭 소리 없음) — proposed: 추가 필요 | 범용 주력 소총. 유일한 5.56mm 총. | Assets/_Project/Data/Resources/GameData/weapons.json:11; 모델 CharacterKit.cs:31; 소리 TestBoxBuilder.cs:291-292 |
| 확정 | Breacher | 4 | 산탄총 | ammo:12게이지 | 24 | 머리 x1.5 / 몸 x1.0 / 팔다리 x0.8 (펠릿 9발 x24) | 70 | 0.85 | 6 | 2.8(전체) / 발당 0.5초 장전 | 45m, 감쇠 8→30m 구간에서 최대 x0.3 | 380 | 7.0°(정조준에서도 x0.6 이하로 안 좁아짐) · 9펠릿 | 위 4.5 · 좌우 1.2 · 한계 2.5 · 회복 12°/s | 단발 | 0.6 | 반동보정기·소음기·보강개머리판 (9,11,15) · 조준경 1x~2배율 | 근접 전탄 216 → 1발 치명 가능성(근거리 펠릿 5발 이상 명중 시 100+) | Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/Shotgun_1.fbx (W-0001, CC0), 길이 0.95m — 실제 산탄총 0.9~1.1m: 적정 | ../../Assets/_Project/Resources/Icons/weapon_4.png | ../handoff/shots_2026-11-16/m15_weapons.png | shot_shotgun.wav + 먼 층 shot_shotgun_far.wav — proposed: replace | reload_shotgun.wav (OGA shotguncock_0) — proposed: replace | 없음(코드에 빈 총 클릭 소리 없음) — proposed: 추가 필요 | 근거리 폭딜·실내전. 한 발씩 장전 중 사격으로 끊기. | Assets/_Project/Data/Resources/GameData/weapons.json:15; 모델 CharacterKit.cs:32; 소리 TestBoxBuilder.cs:291-292 |
| 확정 | Sentinel | 5 | 저격소총 | ammo:7.62mm | 82 | 머리 x2.5 / 몸 x1.0 / 팔다리 x0.7 | 46 | 1.3 | 5 | 3.4 / 4.2 | 400m, 감쇠 100→300m 구간에서 최대 x0.8 | 800 | 0.6° · 1발 | 위 6.5 · 좌우 0.8 · 한계 1.5 · 회복 10°/s | 단발(볼트 동작 연출 없음) | 0.7 | 반동보정기·화염분산기·소음기·보강개머리판·확장탄창 (9,10,11,15,16) · 조준경 1x~15배율 전부 | 몸통 2발(82x2), 머리 1발(82x2.5=205) | Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/AssaultRifle_1.fbx를 1.05m로 늘린 임시 모양(W-0001, CC0) — 저격 전용 모델 없음. 실제 저격소총 1.1~1.3m | ../../Assets/_Project/Resources/Icons/weapon_5.png | ../handoff/shots_2026-11-16/m15_weapons.png | shot_rifle.wav를 피치 0.72로 낮춰 재사용 + 먼 층 shot_sniper_far.wav (임시 재사용 — proposed: replace) | reload_shotgun.wav 재사용(저격용 별도 없음) — proposed: replace | 없음(코드에 빈 총 클릭 소리 없음) — proposed: 추가 필요 | 장거리 저격. 머리 한 발 치명 가능(헬멧 보정 전). | Assets/_Project/Data/Resources/GameData/weapons.json:19; 모델 CharacterKit.cs:33-34 (임시 형태 주석); 소리 CombatantAudio.cs:27, TestBoxBuilder.cs:291-292 |

## 총기 구성 확정 (2026-11-16)

일반 파밍 총기: SMG 3 / AR 5탄(5.56) 2 + 7탄(7.62) 2 / DMR 5탄 2 + 7탄 1 / SR 7탄 2 / 샷건 2(2탄·5탄짜리). LMG는 지금 필요 없음. 보급 총기는 따로(위 구성은 일반 파밍 가능 총기). 총기마다 스탯이 달라야 하며 항목은 이름·대미지·탄약(mm)·탄속(m/s)·저지력·RPM·DPS·사격 모드. 총기 이름은 저작권 문제 없는 선에서.

해석: '5탄/7탄'은 5.56mm/7.62mm. 샷건 '2탄·5탄짜리'는 더블 배럴(장탄 2발)과 5발 탄창식 반자동(Saiga-12 계열 예시) — 준환 답변으로 확정. 실존 총기명·외형은 쓰지 않는다.

- 일반 파밍 14종 = SMG 3 + AR 4 + DMR 3 + SR 2 + 샷건 2 (권총은 별도 결정).
- DPS는 대미지×RPM÷60(펠릿 샷건은 전 펠릿 명중 기준). 저지력은 새 스탯으로 정의가 필요하다(아래 결정).
- 이름은 전부 가상 이름(제안). 저작권·상표 검토는 사람의 일(CLAUDE.md 규칙 8).

## 일반 파밍 총기 로스터 (구성 확정 / 수치 제안, 권총 2 포함 16종)

| 상태 | 이름 | 분류 | 탄약(mm) | 대미지 | 탄속(m/s) | 저지력(1~10) | RPM | DPS | 사격 모드 | 구분 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 제안 | VK-9 | 권총 | 9 | 34 | 360 | 2 | 333 | 189 | 단발 | 기존 |  |
| 제안 | Vesper | 권총(대구경) | 9 | 52 | 380 | 4 | 180 | 156 | 단발 | 신규 |  |
| 제안 | Ordo | SMG | 9 | 26 | 400 | 2 | 800 | 347 | 연사/단발/2점사 | 기존 |  |
| 제안 | Brisk | SMG | 9 | 22 | 380 | 1 | 950 | 348 | 연사/단발 | 신규 |  |
| 제안 | Sable | SMG | 9 | 30 | 420 | 3 | 650 | 325 | 연사/단발 | 신규 |  |
| 제안 | Warden | AR(5탄) | 5.56 | 40 | 880 | 4 | 545 | 363 | 연사/단발/3점사 | 기존 |  |
| 제안 | Vanguard | AR(5탄) | 5.56 | 36 | 900 | 3 | 700 | 420 | 연사/단발 | 신규 |  |
| 제안 | Bulwark | AR(7탄) | 7.62 | 49 | 840 | 6 | 500 | 408 | 연사/단발 | 신규 |  |
| 제안 | Hauler | AR(7탄) | 7.62 | 54 | 820 | 7 | 430 | 387 | 연사 | 신규 |  |
| 제안 | Lancer | DMR(5탄) | 5.56 | 52 | 920 | 4 | 300 | 260 | 반자동 | 신규 |  |
| 제안 | Tracer | DMR(5탄) | 5.56 | 46 | 940 | 3 | 360 | 276 | 반자동/2점사 | 신규 |  |
| 제안 | Arbiter | DMR(7탄) | 7.62 | 66 | 860 | 7 | 240 | 264 | 반자동 | 신규 |  |
| 제안 | Sentinel | SR(7탄) | 7.62 | 82 | 800 | 9 | 46 | 63 | 단발(볼트 연출 없음) | 기존 |  |
| 제안 | Obelisk | SR(7탄) | 7.62 | 95 | 840 | 10 | 40 | 63 | 단발 | 신규 |  |
| 제안 | Breacher | 샷건(5탄 탄창식 반자동) | 12게이지 | 180 | 380 | 8 | 110 | 330 | 반자동(펠릿 9×20) | 기존 |  |
| 제안 | Duo | 샷건(2탄 더블 배럴) | 12게이지 | 234 | 360 | 9 | 120 | 468 | 2연발 즉시(펠릿 9×26) | 신규 |  |

## 근접 무기

| 상태 | 게임 내 이름 | id | 피해 | 머리 배율 | 스윙 간격(초) | 초당 피해(추정) | 사거리(m) | 휘두르는 각(°) | 스윙 방식 | 모델 출처·길이 | 아이콘 | 소리 (현재) | 설계 역할 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 확정 | 주먹 | 0 | 12 | 1.5 | 0.55 | 21.8 | 1.3 | 60 | - | (없음, 캐릭터 애니메이션) |  | 피격 Kenney impactPunch_medium_000~002.ogg, 헛스윙은 같은 클립 고피치 — proposed: replace | 무기 없을 때 폴백. | Assets/_Project/Data/Resources/GameData/rules.json:101 |
| 확정 | 마체테 | 21 | 50 | 1.5 | 0.65 | 76.9 | 1.6 | 70 | 가로 베기 | MeleeMeshes.BuildMachete 코드 생성, 약 0.6m | ../../Assets/_Project/Resources/Icons/weapon_21.png | 휘두름: MeleeAudio 코드 합성 필터잡음 whoosh(합성). 적중: Kenney impactPunch_heavy_000~002.ogg — proposed: replace(합성 whoosh는 별도 후보 조사) | 균형형 근접. 가장 흔한 편(루팅 비중 4). | Assets/_Project/Data/Resources/GameData/weapons.json:23; 모델 MeleeMeshes.cs:41-100 |
| 확정 | 쇠지렛대 | 22 | 55 | 1.5 | 0.8 | 68.8 | 1.6 | 55 | 내려치기 | MeleeMeshes.BuildCrowbar 코드 생성, 약 0.65m | ../../Assets/_Project/Resources/Icons/weapon_22.png | 휘두름: MeleeAudio 코드 합성 필터잡음 whoosh(합성). 적중: Kenney impactPunch_heavy_000~002.ogg — proposed: replace(합성 whoosh는 별도 후보 조사) | 느리지만 센 한 방. 비중 3. | Assets/_Project/Data/Resources/GameData/weapons.json:25; 모델 MeleeMeshes.cs:41-100 |
| 확정 | 낫 | 23 | 40 | 1.5 | 0.5 | 80.0 | 1.4 | 80 | 가로 베기 | MeleeMeshes.BuildSickle 코드 생성 | ../../Assets/_Project/Resources/Icons/weapon_23.png | 휘두름: MeleeAudio 코드 합성 필터잡음 whoosh(합성). 적중: Kenney impactPunch_heavy_000~002.ogg — proposed: replace(합성 whoosh는 별도 후보 조사) | 빠르고 넓음, 피해 낮음. 비중 4. | Assets/_Project/Data/Resources/GameData/weapons.json:27; 모델 MeleeMeshes.cs:41-100 |
| 확정 | 프라이팬 | 24 | 60 | 1.5 | 0.9 | 66.7 | 1.4 | 60 | 내려치기 | MeleeMeshes.BuildPan 코드 생성 | ../../Assets/_Project/Resources/Icons/weapon_24.png | 휘두름: MeleeAudio 코드 합성 필터잡음 whoosh(합성). 적중: Kenney impactPunch_heavy_000~002.ogg; 프라이팬은 추가로 impactBell_heavy_000.ogg 울림 — proposed: replace(합성 whoosh는 별도 후보 조사) | 최대 피해·최저속, 장난스러운 보너스. 비중 3. | Assets/_Project/Data/Resources/GameData/weapons.json:29; 모델 MeleeMeshes.cs:41-100 |

## 소리 방향 (총기)

현재 발사음은 'The Free Firearm Sound Library'(CC0) 근거리 녹음 4개 + 먼 층 5개, 재장전음은 OGA gun-reload 3개로 구성되어 있다. 파일 소리이며 합성이 아니다(합성은 탄 스침 whiz와 근접 whoosh만). 준환은 이 소리를 '스타워즈 같다'고 느낀다.

- 현재 소리 전부 proposed: replace. 실사 후보를 선별해 준환이 고르는 라운드에서 교체(후보 조사 필요).
- 저격은 소총 발사음을 피치 0.72로 낮춘 임시 재사용, SMG 재장전은 소총 재장전음을 공유 — 총마다 별도 소리가 없음.
- 빈 총 클릭 소리, 일부 총(저격) 재장전 별도음이 없음.
- 나중에 교체할 때는 CombatantAudio의 _shotByWeapon/_reloadByWeapon 배열(TestBoxBuilder.cs:291-292)과 Resources/Shots/shot_*_far.wav 이름만 맞추면 됨.

## 무기 — 정해야 할 것

- [x] **50인 배틀로얄에 빠진 총 종류를 채울 것인가? 현재 5종: 권총·SMG·소총·산탄·저격(볼트 없음). DMR(반자동 7.62)과 LMG가 없음.** (확정) → 일반 파밍 총기: SMG 3 / AR 5탄(5.56) 2 + 7탄(7.62) 2 / DMR 5탄 2 + 7탄 1 / SR 7탄 2 / 샷건 2(2탄·5탄짜리). LMG는 지금 필요 없음. 보급 총기는 따로(위 구성은 일반 파밍 가능 총기). 총기마다 스탯이 달라야 하며 항목은 이름·대미지·탄약(mm)·탄속(m/s)·저지력·RPM·DPS·사격 모드. 총기 이름은 저작권 문제 없는 선에서.
  - 선택지: 지금 5종 유지 + 이후 DMR만 추가 / DMR+LMG 둘 다 추가 / 추가 안 함(M1 범위 유지)  · 추천: 지금 5종 유지 + 이후 DMR만 추가
  - 근거: 7.62mm가 Sentinel 한 자루에만 쓰여 탄 종류 대비 총이 적음. DMR이 7.62를 공유하면 탄 낭비 없음. 모델은 CC0 총기 팩 추가 조사 후.
- [x] **저격소총 Sentinel 전용 모델 확보** (확정) → CC0 저격 모델 조사(자산 라운드) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: CC0 저격 모델 조사(자산 라운드) / 돌격소총 늘린 임시 모양 유지  · 추천: CC0 저격 모델 조사(자산 라운드)
  - 근거: CharacterKit.cs:33에 임시 형태라고 명시됨. 볼트 장전 애니메이션도 없음.
- [x] **TTK(몸통 기준) 목표** (확정) → 현재 수치 유지 후 플레이테스트로 조정 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 소총 0.25~0.35초, SMG 0.25~0.4초, 저격 2발 / 현재 수치 유지  · 추천: 현재 수치 유지 후 플레이테스트로 조정
  - 근거: 현재 Warden 3발 ≈ 0.22초는 헬멧·조끼 감쇠 전 값. 조끼 3티어(55%)면 약 2배 늘어남 — 방어구와 같이 봐야 함.
- [ ] **총 이름 실존 유사 여부 검토** (제안) 
  - 선택지: 현재 이름 유지(VK-9/Ordo/Warden/Breacher/Sentinel) / 전면 개명  · 추천: 현재 이름 유지
  - 근거: VK-9는 실존 모델명과 형태가 비슷할 수 있어 출시 전 사람이 상표 검토(RISKS 체크리스트). 신규 제안 이름은 실존 총기명 금지.
- [x] **산탄 근접 전탄 216 피해(펠릿 9x24)와 헬멧 보정** (확정) → 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 유지(거리 감쇠 8→30m로 제어) / 펠릿 피해 하향  · 추천: 유지
  - 근거: 실제 근접 전탄 맞히는 경우가 드물지만 한 발 치명 가능 → 밸런스 검증 필요.
- [x] **빈 총(탄 없음) 클릭 소리 추가** (확정) → 추가 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 추가 / 불필요  · 추천: 추가
  - 근거: 현재 코드에 빈 총 소리 없음.
- [x] **'저지력'은 무엇을 뜻하나?** (확정) → 맞은 상대의 조준 흔들림·이동 감속(경직 강도), 1~10
  - 선택지: 맞은 상대의 조준 흔들림·이동 감속(경직 강도) — 1~10 눈금 / 방어구 관통력 / 두 가지 모두(관통은 별도 스탯)  · 추천: 맞은 상대의 조준 흔들림·이동 감속(경직 강도) — 1~10 눈금
  - 근거: 현재 코드에는 저지력 스탯이 없다(피격 반응은 별도). 새 스탯이므로 정의·구현 범위를 정해야 한다.
- [x] **샷건 '2탄·5탄짜리'는 탄창 용량(2발 더블 / 5발 펌프)이 맞나?** (확정) → 샷건 2종 = 더블 배럴(장탄 2발)과 5발 탄창식 반자동 샷건(준환이 Saiga-12 계열을 예로 듦). 펌프식이 아님. 실존 총기명·고유 외형 복제 금지(오리지널 이름·외형 사용)
  - 선택지: 맞다(용량) / 아니다(다른 뜻)  · 추천: 맞다(용량)
  - 근거: 5.56/7.62탄과 구분되는 표기라 용량으로 해석했다.
- [x] **권총(사이드암)은 일반 파밍 목록에 포함되나? (현재 VK-9 1종, 목록에는 언급 없음)** (확정) → 포함 — 1~2종 유지
  - 선택지: 포함 — 1~2종 유지 / 제외 — 시작 장비 또는 보급 전용 / 권총 없이 근접 무기로 대체  · 추천: 포함 — 1~2종 유지
  - 근거: 전투 체감과 소음기 용도가 크다. 기존 VK-9가 있다.
- [x] **보급 총기(보급상자 전용)는 어떤 종류로 하나?** (확정) → 일반 파밍에 없는 총 4~6종(LMG·고급 DMR·특수 AR 등)
  - 선택지: LMG·고급 DMR·특수 AR 등 일반 파밍에 없는 총 4~6종 / 일반 파밍 총기의 강화판(부착물 장착형)  · 추천: LMG·고급 DMR·특수 AR 등 일반 파밍에 없는 총 4~6종
  - 근거: 준환: LMG는 지금 필요 없음(일반 파밍). 보급 전용으로 뺄지 아예 제외할지 결정.
- [x] **신규 총기 이름 후보 승인(Brisk, Sable, Vanguard, Bulwark, Hauler, Lancer, Tracer, Arbiter, Obelisk, Duo)** (확정) → 신규 이름은 가칭으로 유지(Brisk, Sable, Vanguard, Bulwark, Hauler, Lancer, Tracer, Arbiter, Obelisk, Duo). 상표·실존 상품명 충돌 검토는 사람이 릴리스 전 확인(RISKS 체크리스트) [준환 위임 '알아서' 2026-11-16: 추천안 적용]
  - 선택지: 이대로 승인 / 이름 후보를 더 조사해서 선택  · 추천: 이름 후보를 더 조사해서 선택
  - 근거: 실존 상품명·상표와 겹치지 않는지 확인이 필요하다. 한글 표기 이름도 정할지.
- [x] **권총 2번째 종류를 추가할까? (1~2종 유지로 확정, 현재 VK-9 1종)** (확정) → 강한 한 발 권총(대구경, 낮은 RPM) 1종 추가 — 새 탄 종류 없이 9mm 사용 [준환 위임 '알아서' 2026-11-16: 추천안 적용]
  - 선택지: 강한 한 발 권총(대구경, 낮은 RPM) 1종 추가 / 현재 VK-9 1종만 유지 / 연사 가능한 기관권총 1종 추가  · 추천: 강한 한 발 권총(대구경, 낮은 RPM) 1종 추가
  - 근거: 새 탄 종류 없이 9mm를 쓸지, 새 구경이 필요한지도 같이 정해야 한다.
- [x] **5발 탄창식 반자동 샷건(기존 Breacher 대체) 모델은 어떻게 구하나?** (확정) → CC0 모델 조사 후 교체(더블 배럴 모델과 함께 조사) [준환 위임 '알아서' 2026-11-16: 추천안 적용]
  - 선택지: 현재 펌프 샷건 모델을 개조해 탄창식으로 표현 / CC0 모델 조사 후 교체 / 더블 배럴 모델과 함께 조사  · 추천: CC0 모델 조사 후 교체
  - 근거: 현재 Breacher는 6발 펌프식이다. 펌프 → 탄창식 반자동으로 모양·장전 동작·사운드가 달라진다.


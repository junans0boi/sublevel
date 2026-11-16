# 총기 트랙 DONE (2026-11-16, 모델 반입·Unity EditMode 확인 완료)

## 한 것 (커밋 가능 상태, Unity 미확인)
- **루팅 연결**(ADR-215): `loot.json` 일반 16종 가중(합 53)·근접 4 유지, `supply.json`에 보급 5종 + Arbiter·Obelisk 추가. 총기 Amount=탄창 용량. 신규 `WeaponLootTests`(5건: 일반 loot에 SupplyOnly 없음·전 일반총 1행, 보급 5종 포함, 풀 탄창·양의 가중, 클래스 비율, 40000회 Roll 분포 ±2%p).
- **저지력 서버 판정**(ADR-216): `StaggerState.Hit(weapon, zone, now)` 서버 진입점 추가 + 샷건 9펠릿 혼합 부위 테스트. 코어 규칙·테스트는 이전 세션(7건) 그대로.
- **상표 검토 목록**: 이미 `RISKS_OPEN_QUESTIONS.md` S7 절에 21종 전부 올라 있음(추가 변경 없음, 판단은 사람).
- 확인한 것: JSON 파싱, 전 총기 Amount==MagSize, 일반 loot 권총 비율 24.5%(파이썬 계산). **추정**: C# 테스트는 컴파일·실행 안 해 봄(Unity·dotnet 하네스 미실행).

## 못 한 것 / 막힌 것
1. **모델 반입 — 다운로드 완료(master, 2026-11-16), ledger W-0002~W-0005 기록 완료, Unity 반입만 남음**. (이전 서술: `AssetStaging/guns`에는 Quaternius 5종뿐(이미 W-0001). 임시 확정안(s7_gun_spec §6)의 나머지 모델(Revolver·SMG 2번째·AR 변형·Sniper A~D·Scope/Bipod, Poly Pizza Smg Compact West·Shotgun Auto West, CreativeTrio 더블배럴)은 **Quaternius Ultimate Guns Pack 전체 zip(CC0, 로그인 불필요)과 Poly Pizza 모델 2~3개 다운로드**가 선행. 그 뒤 Unity 순번에서 반입·개명·ledger 행·`placeholder/` 5종 키트배시·`CharacterKit`/`Fire.MuzzleFlashSize`/아이콘 등록(#279~#281, 소유 밖 파일 포함)·PERF.
2. **서버 배선**: `Combatant`에 `StaggerState` 보유·`SpeedFactor`·`ShakeDeg` 적용은 Net/Gameplay(소유 밖). master가 S8/애니와 조율.
3. **Unity EditMode 확인**(공통 정의 1항): 미실행. 신규·수정 테스트 + `JsonUtility`가 `Stopping`/`SupplyOnly`를 읽는지, `optics.json` 병합 확인 필요.
4. 신규 총은 모델 등록(#280) 전까지 스폰 켜면 모델·소리 배열 범위 밖 → **머지 전 #280 선행 또는 loot 신규 행 비활성** 필요.

## 모델 반입 계획 (Unity 순번 필요, 다운로드는 완료)
파일은 `AssetStaging/guns/`(git 무시): `quaternius_ultimate_guns_fbx/`(55파일, CC0) + `polypizza/`(3개 CC0, 마스터가 페이지 표기 확인). 원본 파일명엔 실총 이름이 없음(Quaternius는 범용 이름, Poly Pizza는 West/Auto/Compact 수준). 반입 시 `Assets/_Project/Art/Weapons/Wpn<id>_<이름>.fbx`로 개명.
| id | 총 | 1차 선택(Unity에서 눈으로 확인해 확정) |
|---|---|---|
| 6 | Vesper | Revolver_1 |
| 7 | Brisk | SubmachineGun_2 |
| 8 | Sable | SMG_Compact_West(W-0003, 윤곽 조정) |
| 9 / 10 | Vanguard / Bulwark | AssaultRifle_2 / AssaultRifle2_1 (길이·색으로 구분) |
| 11 | Hauler | placeholder/ AssaultRifle_3 확대 |
| 12 / 13 / 14 | Lancer / Tracer / Arbiter | AssaultRifle2_2+Scope_1 / Bullpup_2+Scope_2 / SniperRifle_5(짧은 것)+Scope |
| 5 / 15 | Sentinel / Obelisk | SniperRifle_1 / SniperRifle_2 |
| 4 / 16 | Breacher / Duo | Shotgun_Auto_West(W-0004) / Shotgun_doubleBarrel(W-0005) |
| 31 / 32 | Bastion / Anvil | placeholder/ AssaultRifle2_3·4 + Bipod |
| 33 / 34 / 35 | Marshal / Fang / Hornet | placeholder/ SniperRifle_3 / AssaultRifle_5 / SubmachineGun_3 |
Unity 순번에서 할 일(순차 1회): ① FBX 복사·임포트(삼각형 수 측정) ② 후보 형상 캡처 확인·교체 ③ placeholder 5종 키트배시 ④ `CharacterKit`·`Fire.MuzzleFlashSize`·아이콘 등록(**소유 밖**: `Editor/CharacterKit`, `Scripts/.../Fire.cs`, ItemIcons) ⑤ EditMode 전체 + PERF. 소유 밖 변경 목록은 위 ④로 master가 허가해야 함.

## Unity 반입 결과 (ADR-217)
- 첫 임포트·EditMode 전체: **1227건 중 1226 통과, 1 실패(아이콘 미생성)** → `ItemIconBuilder.Build` 후 재실행 **1227/1227 통과**(`Logs/test-editmode.xml`·`.log`, 약 2분). Unity 프로세스 남은 것 없음(확인).
- 반입: `Art/Weapons/Roster/` FBX 18 + 부착물 3(Scope 2·Bipod, 미장착). 삼각형 646~2528/총(최대 Tracer 2528), 서브메시(머티리얼) 3~6개, 용량 합 약 1MB.
- 등록: `CharacterKit.Weapons`(+아이콘 자동), `rules.json` MuzzleFlashSize 36칸, `ItemIcons.KnownKeys`·테스트 기대값. 아이콘 PNG 16개 생성(Resources/Icons).
- 변경 사항: Poly Pizza 3종은 시험 결과 쓸 수 없어(skinned 0 tris·축 불일치, Duo는 총열만) Quaternius 변형으로 대체 → ledger에 "미사용" 기록.
- 캡처: `WeaponProbe.RenderHold`로 캐릭터가 21종을 조준 자세로 든 모습 3장 `docs/tracks/guns_shots/guns_hold_0~2.png`(전 총 손에 맞게 들림, Anvil 길이 1.15→0.9로 조정). PlayMode 실제 게임 장면은 아님.
- **안 한 것**: 키트배시(스코프·바이포드 장착), PERF 정량 판정(드로우콜 측정 안 함; 삼각형·머티리얼 수만 기록), Breacher는 펌프형 그대로(박스 탄창 윤곽 변경 안 함), 손 위치 `Forward/Up`·길이는 눈대중이라 게임 안에서 조정 필요, 신규 총 소리 없음(`CombatantAudio` 배열, 사운드 트랙).
- 위험: Vanguard·Hauler가 AK형으로 보임(실총 연상) → 상표/디자인 검토 목록에 추가 필요.

## 2차 작업 (master 지시 1~7, 2026-11-16)
1. **키트배시**(ADR-217): `CharacterKit.Part` 추가 — Lancer·Tracer 스코프, Hauler 포어그립, Fang 소음기, Bastion·Anvil 양각대+상자 탄창, Breacher 상자 탄창. 눈으로 `WeaponProbe.RenderRoster`·`RenderHold`로 맞춤. placeholder 목록은 `placeholder/guns_PLACEHOLDERS.md`(Unity 참조가 끊어져 파일은 안 옮김).
2. **Breacher** 박스 탄창형 윤곽: 펌프 샷건에 상자 탄창 추가(임시, Blender 정리는 아님).
3. **AK형 제거**: AssaultRifle_1~5(전부 AK형) 미사용, AR2·Bullpup 계열로 교체(Vanguard·Bulwark·Hauler·Lancer·Fang). 남은 윤곽 위험(AR-15형·불펍형)은 RISKS에 기록.
4. **PERF**: `docs/PERF_BUDGET.md` 맨 아래 표. 총 몫 +98 드로우(서브메시)·+36k 삼각형·+0.8ms, 판정 통과(잠정, 빌드 SetPass 미측정).
5. **손 위치**: `Forward = Grip(길이, 그립비율)`로 수식화, `WeaponProbe.Fit` 결과 `docs/tracks/guns_shots/fit_table.txt` — 장총 개머리판이 어깨까지 1.0~1.15배(1.3 이하 기준), 권총 0. Anvil 길이 0.9 확정(LMG 중 Bastion 1.05보다 짧게).
6. 저지력 배선 요구서 `docs/tracks/guns_STAGGER_WIRING.md`.
7. 신규 총 소리 요청서 `docs/tracks/guns_SOUND_REQUEST.md`.
- 확인: Unity EditMode 1227/1227 통과(키트배시 후 재실행), Unity 프로세스 없음. 캡처 `docs/tracks/guns_shots/guns_hold_0~2.png`.
- 못 한 것: 플레이어 빌드 드로우콜/SetPass 정량, Breacher 실제 박스 탄창 모델링, 모델 고유 구분(일부 같은 모델 재사용), 소유 밖 배선 2건(요구서로 인계).

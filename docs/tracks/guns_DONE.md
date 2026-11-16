# 총기 트랙 DONE (부분 완료 — 2026-11-16, Unity 미실행 상태)

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

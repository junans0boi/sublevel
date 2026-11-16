# 총기 트랙 DONE (부분 완료 — 2026-11-16, Unity 미실행 상태)

## 한 것 (커밋 가능 상태, Unity 미확인)
- **루팅 연결**(ADR-215): `loot.json` 일반 16종 가중(합 53)·근접 4 유지, `supply.json`에 보급 5종 + Arbiter·Obelisk 추가. 총기 Amount=탄창 용량. 신규 `WeaponLootTests`(5건: 일반 loot에 SupplyOnly 없음·전 일반총 1행, 보급 5종 포함, 풀 탄창·양의 가중, 클래스 비율, 40000회 Roll 분포 ±2%p).
- **저지력 서버 판정**(ADR-216): `StaggerState.Hit(weapon, zone, now)` 서버 진입점 추가 + 샷건 9펠릿 혼합 부위 테스트. 코어 규칙·테스트는 이전 세션(7건) 그대로.
- **상표 검토 목록**: 이미 `RISKS_OPEN_QUESTIONS.md` S7 절에 21종 전부 올라 있음(추가 변경 없음, 판단은 사람).
- 확인한 것: JSON 파싱, 전 총기 Amount==MagSize, 일반 loot 권총 비율 24.5%(파이썬 계산). **추정**: C# 테스트는 컴파일·실행 안 해 봄(Unity·dotnet 하네스 미실행).

## 못 한 것 / 막힌 것
1. **모델 반입+ledger — 다운로드 필요(규칙: 다운로드는 master 보고)**. `AssetStaging/guns`에는 Quaternius 5종뿐(이미 W-0001). 임시 확정안(s7_gun_spec §6)의 나머지 모델(Revolver·SMG 2번째·AR 변형·Sniper A~D·Scope/Bipod, Poly Pizza Smg Compact West·Shotgun Auto West, CreativeTrio 더블배럴)은 **Quaternius Ultimate Guns Pack 전체 zip(CC0, 로그인 불필요)과 Poly Pizza 모델 2~3개 다운로드**가 선행. 그 뒤 Unity 순번에서 반입·개명·ledger 행·`placeholder/` 5종 키트배시·`CharacterKit`/`Fire.MuzzleFlashSize`/아이콘 등록(#279~#281, 소유 밖 파일 포함)·PERF.
2. **서버 배선**: `Combatant`에 `StaggerState` 보유·`SpeedFactor`·`ShakeDeg` 적용은 Net/Gameplay(소유 밖). master가 S8/애니와 조율.
3. **Unity EditMode 확인**(공통 정의 1항): 미실행. 신규·수정 테스트 + `JsonUtility`가 `Stopping`/`SupplyOnly`를 읽는지, `optics.json` 병합 확인 필요.
4. 신규 총은 모델 등록(#280) 전까지 스폰 켜면 모델·소리 배열 범위 밖 → **머지 전 #280 선행 또는 loot 신규 행 비활성** 필요.

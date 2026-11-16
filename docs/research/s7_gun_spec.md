# S7 총기 스펙 · wayfinder 지도 · 티켓 초안 (2026-11-16)

근거: ADR-172, `docs/bible/data/weapons.json` 로스터, SESSION_PLAN S7. grilling Q1~Q4는 준환이 "네 생각대로"로 위임 → 아래 **가정**은 ADR-182 초안으로 올린다(번호는 master 배정).

## 1. 가정 (ADR-182 초안, 준환 위임 2026-11-16)
1. **일반 파밍 16종**: 권총 VK-9·Vesper / SMG Ordo·Brisk·Sable / AR Warden·Vanguard(5.56), Bulwark·Hauler(7.62) / DMR Lancer·Tracer(5.56), Arbiter(7.62) / SR Sentinel·Obelisk / 샷건 Breacher·Duo. 무기 id: 기존 1~5·21~24 유지, 신규 6~16, **보급 31~35**.
2. **Breacher**: 6발 펌프 → **5발 탄창식 반자동**(탄창 교체형 재장전 2.6/3.2초, 펠릿 9×20, 110 RPM). 발당 장전(`ShellReloadSeconds`) 규칙은 Loadout에 남기되 현재 데이터에서는 쓰는 총 없음.
3. **Duo(더블 배럴)**: 한 번 누를 때 **한 발씩**(발 간격 0.12초, 총 2발), 재장전은 2발 한꺼번에. 펠릿 9×26을 **발당** 값으로 본다(전탄 명중 시 한 방 234, 두 방 468 → 사거리 40m·퍼짐 8°로 제어, 수치는 플레이테스트 조정).
4. **Vesper**: 9mm 6발 탄창 반자동, 52 대미지, 180 RPM, 저지력 4(새 탄 없음).
5. **보급 총기 5종(SupplyOnly)**: Bastion(LMG 5.56, 탄창 75) · Anvil(LMG 7.62, 60) · Marshal(엘리트 DMR 7.62, 15) · Fang(특수 AR 5.56, 3점사, 저지력 5) · Hornet(9mm 기관권총, 33). 일반 파밍 가중치는 0, 보급상자에서만(가중치는 일반 총의 약 1/3 제안 → S1 루팅 가중과 합칠 것).
6. **저지력(1~10)** — 맞은 상대의 조준 흔들림 + 이동 감속. 강도 "중간": 저지력 10 몸통 1발 = 흔들림 최대 4°, 이동속도 −35%, 0.5초. 저지력 1 = 효과 없음, 선형. 머리=몸통 동일, 팔다리 ×0.5. 연속 피격은 합산하지 않고 **더 센 쪽으로 갱신**(약한 피격은 무시, 같으면 시간 갱신). 이동 정지·시야 튕김 없음. 수치는 `StoppingRules`에 두고 이후 `rules.json`으로 이동.

## 2. 구현 상태 (이번 세션, Unity 미실행)
| 항목 | 파일 | 상태 |
|---|---|---|
| `Stopping`·`SupplyOnly` 필드 | `Core/WeaponDef.cs` | 완료 |
| 저지력 규칙·`StaggerState` | `Core/WeaponStopping.cs` | 완료, dotnet 통과 |
| RPM·DPS 파생값 | `Core/WeaponStats.cs` | 완료 |
| 무기 21종 데이터 | `Data/Resources/GameData/weapons.json` | 완료(수치는 제안) |
| 조준경·부착물 칸(신규 총 전부) | `Data/Resources/GameData/optics.json` | 완료 |
| 테스트 | `WeaponRosterTests`(8), `WeaponStoppingTests`(7) | dotnet 통과 |
| 기존 테스트 3건 수정 | `AmmoCaliberTests`, `SniperRifleTests`(총 수·id 목록), `WeaponTimingTests`·`AttachmentTests`(펌프 샷건 → 가상 튜브 샷건) | 수정함 |

## 3. wayfinder 지도 (남은 길)
```
[데이터·규칙(완료)] ─┬─> T1 서버 저지력 적용(S8 네트워크) ─> T2 흔들림 클라 표시
                    ├─> T3 루팅·보급 가중(S1) ─> T4 분포 테스트
                    ├─> T5 모델 선택(준환, 블로커) ─> T6 프리팹·CharacterKit 등록·아이콘·총구섬광 크기 ─> T7 LOD·드로우 PERF
                    └─> T8 Unity EditMode 전체 실행(master 순번 9)
```

## 4. 티켓 초안 (GitHub 이슈는 준환 승인 후 생성)
| # | 제목 | 선행 | 완료 기준 |
|---|---|---|---|
| T1 | Server: apply stopping power on hit (aim shake + move slow) | S8 네트워크 구조 | `Combatant`가 `StaggerState`를 갖고 서버에서 `Apply`, 이동 속도에 `SpeedFactor` 곱, 서버 판정 EditMode/PlayMode 테스트(펠릿 중복 비합산·팔다리 ½) |
| T2 | Client: show aim shake while staggered | T1 | `ShakeDeg`를 조준 오프셋에 반영, 캡처 + 수치 |
| T3 | Loot: weights for 16 ordinary guns, supply table for 5 supply guns | S1 루팅 가중 | `loot.json`·`supply.json` 갱신, 보급상자에 SupplyOnly만 나오고 일반 파밍엔 안 나옴 |
| T4 | Test: loot distribution by gun class | T3 | 분포 EditMode 테스트 |
| T5 | Models: pick CC0 models for 21 guns (08 시트) | **준환 선택** | ASSET_LEDGER, `placeholder/` |
| T6 | Register models/icons/muzzle flash for ids 6~16,31~35 | T5 | `CharacterKit`, `Fire.cs MuzzleFlashSize`(id 인덱스 배열 확장), 아이콘 |
| T7 | Perf: gun model LOD and draw calls | T6 | PERF 판정 |
| T8 | Run Unity EditMode with new roster | master 순번 | 971 + 신규 통과 (아래 §5 위험 확인) |

## 5. 위험·메모 (master 확인)
- **Unity 미실행**: 순수 C# 하네스(dotnet, NUnit shim, `JsonUtility`→System.Text.Json)로만 확인. Unity에서는 `JsonUtility`가 `Stopping`/`SupplyOnly`를 읽는지, `OpticRules` JSON 병합이 새 `Weapons` 배열로 정상 대체되는지 EditMode에서 확인 필요(특히 `optics.json`을 코드 기본값과 대조하는 테스트가 있으면 실패 가능).
- **기존 코드의 id 하드코딩**: `Fire.cs`의 `MuzzleFlashSize` 배열(id 인덱스, 현재 0~5)과 `CharacterKit`/`TestBoxBuilder`의 모델·소리 배열은 id 6~16·31~35 엔트리가 없다 → 신규 총이 실제로 스폰되기 전에 T6 필요(지금은 루팅에 안 나옴). 이 파일들은 S7 소유 밖이라 건드리지 않았다.
- **발사 모드 표현**: `WeaponDef.Auto`는 "연사 가능"이라 Hauler(연사 전용)·Tracer(반자동/2점사)·Marshal(2점사/단발)의 모드 제한은 표현 못 함. Hauler는 연사+단발, Tracer·Marshal은 단발만. 문서 모드와 다름 → 필요하면 `FireModes` 비트 필드 신설(ADR).
- **Breacher 확장탄창**: 이제 탄창식이라 확장 탄창 부착을 허용할지 미정(현재 칸 9·11·15 유지, `AttachmentTests`의 "탄창 불가" 단언 유지).
- **상표**: 가칭 전부 08 시트 §5 목록 그대로, Fang·Hornet·Bastion·Anvil·Marshal은 추가분 → 상표 검토 목록 추가 필요. VK-9 기존 플래그 유지(규칙 8).

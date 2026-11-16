# 오염·열쇠·칩 트랙 DONE (2026-11-16, 중간 보고 — 완성 정의 미충족)

브랜치 `track/pollution`. Unity 미실행 구간(순수 C#·문서·dotnet 하네스)까지.

## 한 것 (커밋 1개 + 미커밋 1개)
| 항목 | 파일 | 증거 |
|---|---|---|
| 맵→오염 변환 `IPollutionTerrain`·`PollutionGridBuilder`(그리드·인접·면적 가중·해시), 그리드 원점 | `Core/PollutionGridBuilder.cs`, `PollutionFront.cs` | `PollutionWiringTests` |
| 서버 집계 `PollutionMatch`(스냅샷·클라 재계산·해시 비교)·`PollutionExposure`(0.5초 단계·방독) | `Core/PollutionMatch.cs` | 테스트 6건 |
| HUD 모델 `PollutionHud`(배너 카운트다운·비네팅·방독 시간) | `Core/PollutionHud.cs` | 테스트 1건 |
| WP8 `ChipBoard`(드롭·운반자 사망·입금·만료·보관함 파괴·부활·팀 상태) | `Core/ChipBoard.cs` | 테스트 2건 |
| WP7 `KeyElevatorSystem`(승강장 N개·팀 쿨다운·도착 재배정·무효 반환·충전) | `Core/KeyElevatorSystem.cs` | 테스트 2건 |
| ADR-235~239 | `docs/DECISIONS.md` | |
| 서버 `PollutionDirector`(방송·늦은 접속·피해 적용·방독 API) | `Net/PollutionDirector.cs` | Unity 컴파일 확인·커밋(2026-11-16) |

수치: **Unity EditMode 전체 1233/1233 통과**. dotnet 하네스 17/17 통과(새 테스트 포함 전체가 아니라 `PollutionWiringTests` 단독; 기존 41건은 S6에서 Unity 통과).

## 서버 연결 (WP7/8 일부, Unity 컴파일·EditMode 확인)
- `Net/PollutionDirector.cs`: 칩 이벤트(`Combatant.ServerDied/ServerKnocked` 구독이라 **Combatant 수정 0줄**), 열쇠 6~8개 배치(`MatchDirector.ServerSpawnLoot`), 칩 줍기·입금 서버 API
- `Net/KeyElevatorNet.cs`(신규): `KeyElevatorSystem` 서버 조정(삽입·탑승 자동·문 닫기·지역 선택·무효 시 열쇠 반환), `KeyStationStatus` 방송(패널용)
- **소유 밖 변경 줄 수**: `ElevatorRides.cs` +31/-6(`ServerKeyRide`, `StationCount`, `Trip.Group`, `ServerSwitch`의 열쇠 차량 분기), `KeyHolder.cs` +13/0(`ServerConsumeKey`, Cmd 3개). `Combatant*.cs`·`ZoneDirector`·`loot.json`·`supply.json`·`Hud*.cs` 변경 없음.
- HUD: `PollutionHud.Level`/`ChipFlag`가 UI 슬롯(`HudHazardView`: Warn/SecondsToClose/Region/Keys/Chip)에 맞는 값을 낸다. `Hud.Instance.Hazard.Set(...)` 호출 어댑터는 `track/ui` 병합 뒤(그 타입이 이 브랜치에 없음).

## 못 한 것 (Unity 순번 필요)
- 연출(붉은 안개·가이거 훅·지도 점멸·HUD 그리기)과 게임 안 캡처, PlayMode 멀티 복제 테스트
- 방독 아이템: 새 `LootKind`(필터·해독제)는 `LootItem`·`InventoryBackend`·`LootMeshes`·아이콘 switch를 건드려 총기·UI 트랙과 충돌 → 병합 뒤. 서버 API(`ServerUseFilter/Antidote`)만 있음. 데이터 행은 새 파일로 넣을 예정(loot.json 끝에 끼우지 않음)
- 칩 **부활 실행**(죽은 몸 되살리기: Combatant 상태 복구)·보관함 오브젝트·줍기/입금 입력(상호작용 UI), 열쇠 `ElevatorPanel` 확장·삽입 입력 UI, 승강장 문 열림 연출, 보급상자 열쇠 배치(지금은 전부 일반 루팅)
- 연출 전부, 멀티 PlayMode 복제·승강기 시나리오 테스트(`ElevatorTravelPlayTests` 등 기존 PlayMode도 이번엔 안 돌림)
- T10 기존 Lockdown/통로 제거(맵 병합 뒤)
- 맵 어댑터(`IPollutionTerrain` 구현)는 `track/map` 병합 뒤 맵 데이터로 작성
- 열쇠 이탈률 25/28% 시뮬 대조(JS 몬테카를로, PlayMode 통계 필요)

## 확인 / 추정
- 확인: 순수 로직(위 테스트). 판 길이·전선 시뮬 대조는 S6 결과(평균 22.1분 대조) 그대로.
- 추정: 클라이언트 해시 일치(플랫폼 부동소수), 그리드 계산 시간(수십 ms), `IBroadcast`의 `int[]` 직렬화.

## 시뮬 대조 차이 (확인 + 추정)
- **확인**: 실제 섬(`WorldPlanTerrain`, 12 m 격자) 200시드 평균 **21.65분**(최소 19.84, 최대 23.39), 시뮬 평균 22.13분과 차이 0.48분. 앞의 21.0분은 6시드 표본이라 표본 편차였다.
- **추정**: 남은 0.5분은 지형 차이 — 시뮬은 C2 시안 지형(경사·도로 래스터), 게임은 생성된 섬(경사 분포와 도로 연결이 다름)이라 평균 전선 속도가 약간 빠르다. 한 시드의 최소 19.84분은 20분 바로 아래이므로 병합 전 시드 1000개로 분포를 다시 보고, 20분 미만이 나오면 속도 조정(룰 변경이라 ADR 필요).

## T10 진행 (ZoneDirector 제거) — 1~2a 단계 완료, 삭제는 botsnet 병합 뒤
- 완료(Unity 컴파일 확인, 759b959): `PollutionDirector` 호환 읽기 API(`PollutionDirector.Compat.cs`: StateOf·SecondsLeft·지역 이름/ID/중심·FinalRegion·이벤트·`FinalRegionOverride`), `MatchDirector`가 `PollutionDirector`를 붙임(1줄), `AutoStart` 기본 켜짐. 소비자 이동: `SupplyDirector`(보급은 경보마다 1회), `ElevatorRides`, `MinimapView`, `ElevatorPanel`, `ZoneBanner`(오염 경보 문구, 폭격·통로 분기 삭제), `MatchRecorder`, `MatchAutoplay`, `FeelCapture`(w4 오염 캡처로 교체), `DedicatedServer`(--region), `ZoneFogView/RegionCard/ZoneStatus/Minimap/EnvironmentView`(이름 교체). `PollutionOrder.Plan`에 `forcedFinal` 인자(기본 -1, 다른 시드 결과 불변).
- 현재 상태: 오염이 판을 돌린다. `ZoneDirector`는 지오메트리 소스가 없어 **작동하지 않음**(컴파일만 유지, `BotController` 5곳 때문). **봇은 아직 오염을 피하지 않는다**(botsnet 병합 뒤 `IsDoomed/TryEscapeTarget`을 전선 도달 시간 기반으로 이전).
- 남음(삭제 대상): `ZoneDirector.cs`, `Lockdown.cs`(+`ZoneGeometry`는 별도 파일로 보존), `PassBarrier.cs`, `KeyHolder.ServerUseKey/CmdUseKey`, `Editor/MatchKit.cs`·`TestBoxBuilder.cs` 참조, `ZoneState.Lockdown/Bombardment`. 테스트: EditMode `LockdownTests`(Lockdown 삭제와 함께, `PollutionTests`가 순서·연결·전선·피해·경고를 이미 덮음 — 겹치지 않는 항목만 이전), PlayMode `ZoneDirectorTests`·`MinimapZoneTests`·`SupplyPlayTests`·`ElevatorTravelPlayTests`·`MatchSimulationTests`·`ReplicationWorldTests`·`MatchEndToEndTests`·`SupplyCaptureTests`는 `PollutionDirector.ServerStart` 기준으로 재작성(PlayMode는 소리 여부 확인 뒤 실행 — 이번엔 실행하지 않음).
- 환경 이슈: 이 워크트리에는 Mixamo FBX가 없어 `CharacterRosterTests` 7건이 실패한다(오염과 무관, main 체크아웃에서는 통과해야 함).

## T10 완료 (코드, 06c1156) — 2026-11-16
- 삭제: `ZoneDirector.cs`, `Lockdown.cs`(→ `ZoneState`(Safe/Warning/Closed)·`ZoneGeometry`는 `Core/ZoneGeometry.cs`로 이전), `PassBarrier.cs`, `LockdownTests.cs`, `ZoneDirectorTests.cs`, `KeyHolder.ServerUseKey/CmdUseKey`.
- 봇: `BotController` ZoneDirector 5곳 → `PollutionDirector`(+7/-12). 통로 열쇠·소구역 분기 삭제. `IsDoomed/TryEscapeTarget`은 전선 도달 시간 기반(`PollutionDirector.Compat.cs`).
- 테스트 재작성 목록: `FinalRegionRandomnessTests`(섬 6지역·`PollutionOrder`, `forcedFinal` 테스트 추가), `ElevatorTravelTests`·`SupplyTests`(Lockdown/Bombardment→Closed), PlayMode: 신규 `PollutionDirectorTests`(일정·피해·방독·스냅샷), `SupplyPlayTests`·`MinimapZoneTests`·`ElevatorTravelPlayTests`·`MatchSimulationTests`·`ReplicationWorldTests`(`PollutionSync` 기준)·`MatchEndToEndTests`(6단계)·`SupplyCaptureTests`. `LockdownTests`가 덮던 순서·연결·시간표·피해는 `PollutionTests`·`PollutionMapTests`가 덮는다.
- 의미 변경(기록): 최종 지역도 마지막에 확정(전엔 닫히지 않음). `MatchSimulationTests`의 "최종 지역이 닫히지 않음"·"종료 시 폐쇄 지역에 생존자 없음" 단언 삭제(전선은 지역 확정 뒤 셀 단위로 번지므로 성립하지 않음). `MatchRecorder.finalInClosingOrder`는 "마지막이 아닌 자리에 최종 지역"일 때만 참.
- 확인: Unity EditMode 컴파일(양 어셈블리) 통과, 1321/1335 — 실패 14건은 모두 `CharacterRosterTests`·`CharacterAnimatorTests`(이 워크트리에 Mixamo FBX 없음). **PlayMode 테스트는 실행하지 않았다**(소리 가능성) → 순번과 음소거 확인 뒤 실행 필요(`PollutionDirectorTests`, `ElevatorTravelPlayTests`, `MatchSimulationTests`, `ReplicationWorldTests`, `SupplyPlayTests`).
- 남은 구 시스템: 자유 승강(`ElevatorRides.RequestRide/ServerTryStart`, `ElevatorPanel` hold-F)은 열쇠 승강기 UI로 교체될 때까지 유지(ADR-170) — 봇 인터페이스는 `pollution_BOT_API.md`.
- `Editor/TestBoxBuilder.cs`의 통로 장벽 이펙트 에셋 생성 코드는 남김(미사용, 정리 대기).

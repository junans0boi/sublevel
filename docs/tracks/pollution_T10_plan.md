# T10 계획 — ZoneDirector·Lockdown·구 통로 제거 (2026-11-16, 오염 트랙)

원칙: 맹목 삭제 금지. 소비자를 `PollutionDirector` 기준으로 하나씩 옮기고, 매 단계 Unity 컴파일·EditMode로 확인한 뒤 옛 코드를 지운다.

## 준비 완료(이미 코드에 있음)
- 맵 입력: `PollutionWorld`(Gameplay, world.json → WorldPlan → `PollutionGrid`), `WorldPlanTerrain`. 섬 실측(dotnet): 생성 ~1 s, 그리드 ~0.7 s, 전선 14 ms(Mono는 더 느림 → 매치 시작 시 한 번), 판 길이 평균 21.0 min.
- `PollutionDirector.AutoStart`(기본 false): true면 `MatchDirector.LockdownSeed`로 서버가 자동 시작. 뒤집는 순간이 T10의 전환점.

## 소비자 목록과 옮길 방향 (변경 수 = ZoneDirector 계열 언급 수)
| 파일 | 수 | 방향 |
|---|---|---|
| `Net/SupplyDirector.cs` | 13 | `ServerRegionStateChanged` → `PollutionDirector` 지역 단계 이벤트(신규 추가) + 보급상자 열쇠 배치. `ZoneState` 목록 → `PollutionState` |
| `Gameplay/Minimap*.cs` (14+11) | 25 | `ZoneColour/ZoneTexel`을 Safe/Warning/Contaminated 3단계로. Lockdown/Bombardment/Closed 제거 |
| `Gameplay/ElevatorPanel.cs` (7) | 7 | 지역 상태 표 → `PollutionDirector.States`; 열쇠 삽입·문 닫기·지역 선택 UI 확장(`KeyStationStatus`) |
| `Gameplay/RegionCard.cs`, `ZoneStatus.cs`, `ZoneBanner.cs`, `ZoneFogView.cs`, `PassBarrier.cs` | 6+26+36+9+14 | `ZoneBanner`→오염 경보 배너(HUD 슬롯), `ZoneFogView`→붉은 안개, `ZoneStatus`→`PollutionStatus`, `PassBarrier` 삭제 |
| `AI/BotController.cs` (5) | 5 | 봇 트랙 소유 — **botsnet 병합 뒤** 변경 줄 최소로(`TryEscapeTarget/IsDoomed` → 오염 전선 도달 시간 기반) |
| `Gameplay/MatchAutoplay.cs`, `MatchRecorder.cs`, `FeelCapture.cs`, `SpectateCamera.cs`(ZoneBanner.SpectatorFeet), `Editor/TestBoxBuilder.cs`, `MatchKit.cs` | 13+11+6+4+4+1 | 이벤트·상태 이름만 교체 |
| `Core/ElevatorTravel.cs`, `Core/Supply.cs` | 8+3 | `ZoneState` 기반 함수는 `PollutionState` 기반(KeyElevatorTravel)로 대체, 호출부 정리 |
| `Net/ElevatorRides.cs` (7), `Net/KeyHolder.cs` | | `ServerUseKey(pass)`와 통로 경로 삭제, 일반 승강 `States` → 오염 상태 |
| `Core/Lockdown.cs`(558), `Net/ZoneDirector.cs`(865) | | 마지막에 삭제. `ZoneGeometry`가 쓰이면 필요한 부분만 남겨 이름 변경 |

## 테스트 (재작성, 삭제 아님)
- EditMode `LockdownTests`(364줄) → 이미 있는 `PollutionTests`가 대응하는 항목(순서·연결성·전선·피해·경고 시각)을 확인한 뒤, 대응이 없는 항목만 새로 쓰고 나머지는 근거를 DONE에 적어 삭제.
- PlayMode `ZoneDirectorTests`(425), `MinimapZoneTests`, `ElevatorTravelPlayTests`, `MatchSimulationTests`, `SupplyPlayTests`, `ReplicationWorldTests`, `MatchEndToEndTests`: `PollutionDirector` + `ServerStart(seed, grid, regions)` 기준으로 재작성. 소리 없는 것만 실행.

## 순서
1. `PollutionDirector`에 호환 읽기 API(StateOf·RegionCount·FinalRegion·SecondsLeft·지역 이름/중심·이벤트) 추가 → 컴파일.
2. 소비자 옮기기(위 표 위→아래, 파일당 컴파일). 봇은 마지막.
3. `AutoStart=true`, ZoneDirector 삭제, 테스트 재작성, EditMode+PlayMode(무음) 확인.

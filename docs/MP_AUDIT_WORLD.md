# 멀티 복제 감사 B: 월드 (#164)

월드 상태(문, 유리, 보급, 날씨·시간대, 승강기, 봉쇄, 연기·불, 아이템)가 모든 클라이언트에서 같게 보이는지, **늦게 접속한 클라이언트가 현재 상태를 받는지** 점검한 표.
월드 쪽은 NetworkObject 없이 `NetworkManager`의 Broadcast로 전달하는 구조(DoorSystem 방식)가 대부분이다. 서버는 `OnClientLoadedStartScenes`에서 새 클라이언트에게 현재 상태를 보낸다.

| 시스템 | 전달 수단 | 늦은 접속자 | 갭 | 조치 | 테스트 |
|---|---|---|---|---|---|
| 문(ADR-104) | `DoorChanged` Broadcast, 요청 `DoorToggleRequest`(서버가 거리 검사) | 열려 있는 문을 `Quiet`로 전송 | 없음 | - | LateJoiner_Receives…(문) |
| 유리 | `GlassShattered` Broadcast | 깨진 창을 `Quiet`로 전송 | 없음 | - | LateJoiner_Receives…(유리) |
| 승강기 문·출발 시계 | `ElevatorDoorStatus`(남은 시간), 클라이언트가 자기 시계로 환산 | 예약 상태와 남은 시간을 전송 | 없음 | - | LateJoiner_Receives…(승강기) |
| 승강기 이동 사이클(지역 간 탑승) | `ElevatorCycleStatus`(종류, 경과 시간) | 진행 중인 사이클은 전송되지 않았음 | **있었음** | `ElevatorRides.ServerSendCycles`, 접속 시 진행 중인 사이클 전송 | (코드 경로, 아래 미검증 참고) |
| 승강기 탑승 요청·결과 | `ElevatorRideRequest`(C→S), `ElevatorRideStatus`(소유자에게만) | 해당 없음(본인 것) | 없음 | - | - |
| 스폰 보호 | `SpawnProtectionEnded` | 이미 끝난 목록 전송 | 없음 | - | - |
| 봉쇄 단계·구역 안개 | `ZoneUpdate`(지역, 하위 구역, 최종 지역, 통로 개방) | 상태와 남은 시간 전송. 안개(`ZoneFogView`)는 받은 상태에서 각 쪽이 계산 | 없음 | - | LateJoiner_Receives…(구역) |
| 폭격 | `ZoneBlast`(지연, 반경) | 이미 날아오는 중인 폭격 표시는 전송 안 됨(수 초짜리) | 짧은 표시 누락 | 허용 | - |
| 보급 상자·신호 연막·지도 표시 | 호스트만(정적 이벤트, 원격 없음) | 없음 | **있었음** | `SupplyDropped`/`SupplyOpened`/`SupplyOpenRequest` Broadcast, 접속 시 남은 상자 전송(경과 시간 포함), 원격이 F 길게로 열면 서버에 요청 | SupplyCrate_ReachesTheRemote…, LateJoiner_Receives…(보급) |
| 날씨·시간대 | 각 클라이언트가 자기 설정·난수로 정함 | 서로 다른 하늘 | **있었음** | `SkySync`: 서버가 시드·선택을 정해 `SkyState`로 전송, 클라이언트는 `WorldSky`로 시간·날씨 계산, 늦은 접속자는 지난 시간 포함해 받음. 서버(호스트)의 설정이 우선 | Sky_IsPickedByTheServer…, FixedChoice_OnTheServer…, EditMode HourAt |
| 연막·불 구역 | `ThrownItem` 효과 RPC + 늦은 접속용 SyncVar | 남은 시간으로 표시 | **있었음**(#163에서 수정) | `ThrownItem.Effect/EffectLeft` | ReplicationCombatTests.LateJoiner… |
| 아이템 스폰 상태 | `LootItem` NetworkObject(SyncVar) | 현재 땅 위의 모든 아이템 | 없음 | - | Loot_AMatchStartedWithTheSameLoot… |
| 매치 상태(생존 수 등) | `MatchStatus` Broadcast | 접속 시 현재 상태 전송 | 없음 | - | LateJoiner_Receives…(매치 상태) |
| 통로·열쇠 | `ZoneUpdate.KindPassOpen` + `KeyHolder.Keys`(SyncVar) | 열린 통로 전송 | 없음 | - | - |
| 물(수영) | 장면 고정 볼륨, 각 쪽이 몸과 비교 | 해당 없음 | 없음 | - | - |
| 총알 자국·사망 표시 | 각 클라이언트 로컬 | 이전 것은 없음 | 영구 상태 아님 | 허용 | - |

## 서버 권위 날씨·시간대 (ADR-131)
- 서버가 `SkySync.ServerPick`으로 시드와 시간대·날씨 선택을 정한다(호스트의 설정, 없으면 매치마다 무작위, 시드는 난수).
- 클라이언트는 `SkyState{시드, 시간대 선택, 날씨 선택, 경과 시간}`을 받아 `WorldSky.StartHour/PickWeather/HourAt`로 같은 시간·날씨를 계산한다. 시간 흐름은 각 쪽 시계 + 받은 경과 시간이므로 서로 몇 프레임 안에서 일치한다.
- 클라이언트의 설정 화면 값은 멀티에서 무시된다(싱글/호스트에서는 호스트 설정이 곧 서버 선택).

## 하네스 한계
호스트와 원격이 한 프로세스라 씬의 `DoorSystem`/`SkySync` 등은 호스트 매니저에만 묶여 있다. "원격이 받았다"는 것은 (1) 원격 매니저가 접속 전에 등록한 Broadcast 수신기와 (2) 원격 매니저에 묶은 두 번째 `SkySync`/`SupplyDirector`(클라이언트 쪽 논리 그대로)로 확인한다.

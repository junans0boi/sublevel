# 차량 코어 API (ADR-065 (5), #86)

차량 코어(자동차 + 오토바이 공용 프레임워크)와, 뒤이어 붙일 시스템(연료·차량 체력/피격 부위·타이어·화재/폭발·충돌 피해·경적·HUD·정식 모델)이 어디에 끼우면 되는지 정리한 문서.

## 클래스

| 파일 | 클래스 | 역할 |
|---|---|---|
| `Scripts/Core/VehicleRules.cs` | `VehicleRules`, `Vehicles`, `RoadMask`, `VehicleParking`, `VehicleKind`, `SeatRole`, `HandUse` | 데이터(rules.json `"Vehicles"` = 자동차, `"Motorcycles"` = 오토바이)와 순수 규칙(좌석 선택·이동, 무기 허용, 가속/제동, 하차 피해, 공중 제어, 크래시 판정, 기울기) |
| `Scripts/Net/Vehicle.cs` (+ `Vehicle.TwoWheels.cs`) | `Vehicle : NetworkBehaviour`, `DriverInput` | 물리(레이캐스트 바퀴), 권한(운전자 클라가 소유·시뮬레이션), 좌석 SyncVar, 탑승/하차/좌석 이동, 엔진, 부스트, 런치, 크래시 |
| `Scripts/Net/VehicleSeat.cs` | `VehicleSeat` | 좌석 표식(눈 위치 + `Door` 하차점) + 정적 훅(`CanUseWeapon/CanThrow/CanPunch`, `Raycast`, `SeatedAmount`, `Astride`) |
| `Scripts/Net/VehicleState.cs` | `VehicleState` | **확장 지점**(아래) |
| `Scripts/Net/VehicleSpawner.cs` | `VehicleSpawner` | 서버 시작 시 스폰(지점별 종류), 해수면, 도로 지도 |
| `Scripts/Gameplay/VehicleInput.cs` | `VehicleInput`, `IVehicleInput`, `VehicleInputFrame` | 로컬 키 입력(F, Ctrl+1~4, W/S/A/D, Space, Shift, Z, V, 공중 Space/Ctrl/Q/E) |
| `Scripts/Gameplay/VehicleCamera.cs` | `VehicleCamera` | 3인칭 궤도 카메라 / 좌석 1인칭(V) |
| `Editor/VehicleBuilder.cs` | `VehicleBuilder` | 프리팹(`Prefabs/M1/Car.prefab`, `Motorcycle.prefab`), 씬 배치 |

## Vehicle 공개 멤버

- 상태: `Kind`, `IsMotorcycle`, `Rules`, `State`(VehicleState), `Model`(시각 루트), `Body`(Rigidbody), `Colliders`, `WheelCount`, `WheelPivot(i)`, `IsFrontWheel(i)`, `Speed`(m/s, 부호 있음), `SpeedKmh`, `OffRoad`, `Drowned`, `Airborne`, `Crashed`, `LeanShown`, `Righting`, `Simulating`
- 좌석: `SeatCount`, `Seat(i)`, `OccupantId(i)`(-1 = 빈 좌석), `IsFree(i)`, `HasFreeSeat`, `AnyOccupant`, `DoorPosition(i)`, `ExitPoint(seat, combatant)`, `static NearestEnterable(point, range, out dist)`, `static All`
- SyncVar: `Seat0..Seat3`, `EngineOn`
- 서버: `ServerEnter(c)`, `ServerExit(c)`, `ServerSwitch(c, seat)`, `ServerSetEngine(on)`, `ServerCrash(speed)`, `ServerThrottle`/`ServerBoost`(운전자가 보낸 값, 연료 소비용)
- 소유자 요청: `RequestEnter/Exit/Switch/Engine`, `SetDriverInput(in DriverInput)`

## VehicleState (확장 지점)

같은 오브젝트에 붙어 있다(없으면 Vehicle이 Awake에서 추가). 코어는 아래 값을 **읽기만** 한다. 값의 복제는 각 시스템이 자기 NetworkBehaviour의 SyncVar로 하고 모든 쪽에서 여기에 써 넣는다.

| 멤버 | 코어의 동작 | 붙일 시스템 |
|---|---|---|
| `Fuel01` | 0이면 시동 불가, 켜진 엔진은 서버가 끔 | 연료(소비 = `Vehicle.ServerThrottle/ServerBoost` + `EngineOn`), 연료통 |
| `Health01` | 읽기만(표시용) | 차량 체력 |
| `Disabled` | 운전 불가(제동만), 탑승 불가, 엔진 불가 | 화재·폭발(잔해) |
| `WheelGrip[i]`, `WheelDrop[i]`, `SteerPullDeg` | 바퀴별 접지력 배율, 모서리 내려앉음, 조향 쏠림 | 타이어 사격(펑크) |
| `TopSpeedFactor` | 최고 속도 배율 | 엔진 손상, 노면 접지(오프로드 그립) |
| `event Impact(speed, collider)` | 시뮬레이션 쪽에서 충돌(법선 방향 속도 ≥ `CollisionMinSpeed`) 때 | 충돌 피해: 서버로 보고(운전자 클라 → ServerRpc, 상한 둘 것) |
| `event Entered/Left(combatant, seat)` | 모든 쪽 | 효과음, HUD |
| `event Crashed(speed)` | 서버, 오토바이 크래시 | 효과 |

## 끼울 곳

- **피격 부위/차량 피해**: `Combatant.ServerFire`의 레이캐스트(`VehicleSeat.Raycast` 줄)에서 `target == null`이고 `hit.collider.attachedRigidbody`에 `Vehicle`이 있으면 차량 피해 호출 한 줄. 부위 콜라이더(엔진·지붕·타이어 히트박스)는 프리팹의 `Colliders` 아래 자식 BoxCollider로 추가하면 `Vehicle.Colliders`에 자동 포함(트리거가 아닌 것만). 현재 콜라이더: 자동차 `Chassis`(벨트라인까지)·`Roof`, 오토바이 `Frame`. 실내는 비어 있어 창문으로 탑승자가 맞는다.
- **수류탄 폭발 → 차량**: `ThrownItem.Explode()` 끝에 `Vehicle.All`을 돌며 피해 한 줄.
- **HUD**: `HudBackend.VehicleSeatNumber/VehicleSpeedKmh/VehicleSeatName/VehicleEnterable`(이미 있음). 연료·체력·엔진 표시는 `VehicleState`와 `EngineOn`을 읽는다. Hud.cs에는 아직 아무것도 그리지 않는다.
- **정식 모델**: 프리팹의 `Model` 자식 아래 내용을 바꾼다. 좌석(`Seat0..`, 눈 위치), 바퀴 피벗(`Wheel_*`, 정지 상태 바퀴 중심, 회전부 `Spin` 자식)은 `Vehicle.Configure`로 연결되므로 위치만 맞추면 된다. 오토바이는 바퀴·좌석이 `Model` 아래(기울기·윌리를 같이 받음), 자동차는 루트 아래.
- **엔진음/경적**: `EngineOn`, `Speed`, 운전자 입력은 서버에 `ServerThrottle`. 경적(운전자 무기 없음 + 좌클릭)은 새 SyncVar로.
- **연료통 루팅**: `ILootReceiver`를 구현하는 플레이어 컴포넌트로(투척물과 같은 방식). `LootKind`에 16번 추가.

## 규칙 요약(수치는 rules.json)

- F: 가장 가까운 빈 좌석(운전석이 비면 운전석), 3 m. 하차는 그 좌석 쪽 문 옆(막히면 반대쪽 → 뒤 → 앞 → 위). 30 km/h 초과 하차는 낙하 피해, 65 km/h 이상 치명.
- Ctrl+1~4 좌석 이동 0.5 s(이동 중 사격 불가). 운전자가 자리를 옮기면 차는 관성으로 굴러감(약한 저항), 아무도 없으면 제동.
- 운전석은 권총(보조무기)만, 다른 좌석은 전부(주먹 제외). 서버 검사: `VehicleSeat.CanUseWeapon/CanThrow/CanPunch`.
- Z 엔진 끄기/켜기(W로도 재시동), Shift 부스트, Space 핸드브레이크, W+Shift+Space 유지 후 Space 떼면 런치.
- 오토바이: 회전 시 모델 기울기, 강한 가속 시 윌리(표시), 무인 정지 시 받침대 기울기, 공중에서 Space/Ctrl 피치·Q/E 롤, 충돌 9 m/s 이상 또는 45° 넘게 기운 착지 시 탑승자 튕겨나감(속도 비례 낙하 피해).

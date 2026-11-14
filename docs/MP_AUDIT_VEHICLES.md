# 멀티 복제 감사 C: 차량·수영·모서리·사망·관전 (#165)

차량(탑승·운전·피해·불·폭발·로드킬·연료·경적), 수영, 모서리 매달리기, 사망 뒤 데스캠·결과·관전이 **원격 클라이언트**에서 동작하는지, 그리고 **늦게 접속한 클라이언트**가 현재 상태를 받는지 점검한 표.
방법: 코드의 SyncVar / ServerRpc / ObserversRpc / 서버 전용 상태를 읽어 표를 만들고, 호스트 + 원격 클라이언트 PlayMode(`ReplicationVehicleTests`, `ReplicationVehicleDamageTests`, `ReplicationBodyTests`, `MultiHarness`)로 원격 쪽 결과만 확인한다.
규칙: 판정은 서버, 클라이언트는 입력·표시만(ADR-029). 예외 하나가 차량 움직임이다: 운전석 클라이언트가 소유권을 받아 직접 시뮬레이션하고 `NetworkTransform`(클라이언트 권위)이 나머지에게 전한다(ADR-069).

범례: SV = SyncVar(서버 쓰기, 모든 관찰자 읽기), OR = ObserversRpc, SR = ServerRpc(소유자 요청), TR = TargetRpc, 서버 = 서버 객체에만 있음.

| 시스템 | 상태 / 이벤트 (수단) | 원격 클라이언트가 보는 것 | 갭 | 조치 | 테스트 |
|---|---|---|---|---|---|
| 차량 움직임(운전자 → 다른 화면) | 운전석 클라이언트가 소유권(`GiveOwnership`)을 받아 시뮬레이션, 서버·다른 쪽은 키네마틱 + `NetworkTransform`. 운전자 입력 중 스로틀·부스트만 `CmdDriverState`(SR, 초당 4번)로 서버에 감(연료·엔진음용) | 서버와 제3 클라이언트가 지연 100 ms / 손실 2 %에서 운전자 속도의 평균 1.0배, 0.1 s 창마다 정지 없음, 급가속 점프 없음(수치는 아래) | 없음 | - | RemoteDriver_IsSeenSmoothly… |
| 소유권 따라가기 | 서버 `Vehicle.ServerFollowDriver`: 운전석 사람의 연결이 차의 소유자. 운전석이 비거나 운전자가 주인 없음이면 서버가 시뮬레이션 | 운전자가 죽거나 나가면 서버가 이어받고 차는 굴러가다 선다. 옛 운전자 쪽 시뮬레이션은 멈춤 | 재접속(#166)으로 운전자가 돌아와도 차의 소유권이 돌아오지 않았음(좌석 바꿀 때만 갱신) | 매 프레임 `ServerCheckSeats`에서 `ServerFollowDriver`로 확인 | DriverLeavingOrDying… |
| **운전자 접속 끊김** | Fish-Net이 접속 끊김 때 그 연결이 소유한 객체를 모두 Despawn | 운전자 연결이 소유한 차가 **동승자와 함께 사라졌음** | **있었음** | `Vehicle.OnOwnerDisconnecting`(`ServerManager.Objects.OnPreDestroyClientObjects`): 끊기는 연결이 차의 주인이면 소유권을 먼저 회수. 서버가 이어서 시뮬레이션 | DriverDisconnecting… |
| 좌석 | `Seat0..3`(SV, 점유자 ObjectId, -1 = 빔). 각 쪽이 자기 `VehicleSeat` 표를 만들고 몸을 좌석에 놓음. 요청 `CmdEnter/CmdExit/CmdSwitch`(SR, 보낸 연결이 그 전투원의 주인이어야 함), 나가는 위치 `RpcLeft`(OR) | 4석 차: 한 사람이 1→2→3→4→1석으로 옮기고, 세 사람이 동시에 앉아 있고, 한 명이 내리는 모습이 호스트·원격 두 쪽에서 같음. 2석 차·오토바이는 둘만 태우고 셋째는 거절 | 점유자를 `Combatant.All`의 첫 일치로 찾았다(같은 프로세스에 매니저가 여럿이면 다른 매니저의 사본을 잡음). 실제 게임(프로세스당 매니저 하나)에서는 없음 | `Vehicle.FindCombatant`가 **이 차가 속한 매니저**의 객체를 먼저 찾는다(사전 조회라 더 빠르기도 함). `VehicleHorn`도 같은 조회 | Boarding_… (4석, 2석+오토바이) |
| 엔진 | `EngineOn`(SV), 켜고 끔 `CmdEngine`(SR, 운전자만). 운전석에 앉으면 켜짐, 내리면 꺼짐, 연료 없음·불타면 서버가 끔 | 모두 같은 엔진 상태, 엔진 소리(`VehicleAudio`)가 상태와 각 쪽이 본 속도를 따라감 | 없음 | - | FuelHornAndEngine… |
| 연료·제리캔 | `VehicleFuel.Fuel`(SV, 0.1 % 단위), 제리캔 `Count/TargetId/Duration`(SV), 시작 `CmdToggle`(SR), 서버가 5초 뒤 채움 | 다른 클라이언트가 탱크 눈금 변화와 "누가 어느 차에 붓는지"를 봄. 60 % 채워짐 | 없음 | - | FuelHornAndEngine… |
| 경적 | `VehicleHorn.Held`(SV), 누름 `CmdSet`(SR, 운전자만), 봇 소음은 서버가 누를 때와 누르는 동안 주기로 | 다른 클라이언트가 경적 소리를 듣고, 놓으면 멈춤. 늦은 접속자도 울리는 중이면 들음 | 없음(운전자 조회는 위 `FindCombatant`) | - | FuelHornAndEngine…, LateJoiner_SeesTheDriver… |
| 차량 피해(타이어·유리·구멍·부위·체력) | `VehicleDamage.Health/Stage/FlatTyres/Panes/Parts`(SV), 구멍·유리 금·찌그러짐·폭발 `RpcHole/RpcPane/RpcDent/RpcExploded`(OR), 충돌 보고 `CmdImpact`(SR, 서버가 상한·위치 검사) | 원격이 쏜 총알이 타이어를 터뜨리고 유리를 깨고 구멍을 냄. 서버, 쏜 사람, 다른 클라이언트 모두 같은 체력·타이어(그립·내려앉은 모서리·찌그러진 바퀴)·유리 | 없음 | - | RemoteShots_FlatTyre_Glass_Holes… |
| 구멍·찌그러짐 기록(늦은 접속) | 서버가 최근 구멍·찌그러짐을 `TargetHistory`(TR)로 새 클라이언트에 | 늦게 들어온 클라이언트가 이미 난 구멍·터진 타이어·깨진 유리를 봄 | 없음 | - | RemoteShots_… (late) |
| 불·폭발 | 서버 단계 `Stage`(SV): 검은 연기 → 불 → 잔해. 불에 타면 운전 불가·탑승 불가(`VehicleState.Disabled`), 폭발 `ServerExplode`가 근처 전투원과 차 안 사람에게 피해 | 모든 클라이언트가 연기·불·폭발 이벤트(1번)·폭발음·잔해. 옆에 서 있던 원격 플레이어 체력 감소, 차 안 사람은 죽고 자리가 모든 쪽에서 빔 | 없음 | - | Fire_SmokesBurns… |
| 로드킬 | 서버 `TickRoadkill`이 몸 상자에 닿은 전투원을 속도로 때림(운전자에게 공이 감, 같은 팀은 0). 밀침은 그 사람의 주인 클라이언트에게 `TargetShove`(TR) | 원격 운전자가 원격 보행자를 치면 보행자 쪽에서 체력이 100 → 38로 떨어지고(한 번에 62), 보행자의 몸이 4.4 m 밀림. 공은 운전자 | 없음 | - | Roadkill_… |
| 수영 | 물 볼륨은 장면 고정, 각 쪽이 몸 위치와 비교(`CombatantSwim`), 엎드린 자세는 복제된 몸 위치·`CombatantStance.State`(SV, 엎드림)로, 숨·익사 피해는 서버가 자기 계산으로 | 모든 클라이언트가 수면에 엎드려 떠 있는 자세, 다이빙 때 머리가 물속에 들어감. 서버 숨이 다 되면 익사 피해가 서버에서 나가고 둘 다 체력 감소를 봄. 수중 사격 거절, 숨 회복 | 없음 | - | Swimming_… |
| 모서리 매달리기·오르기 | `CombatantStance.Parkour`(SV, 움직임 종류), 시작·끝은 `CmdStartParkour`/`CmdEndParkour`(SR, 서버가 시작점·거리 검사), 자세는 복제된 상태와 몸 위치로(`CombatantAnimation.Ledge`) | 매달린 자세(`LedgePose` ~1)와 오르기 동작(Hang → LedgeClimb → 없음)을 서버와 다른 클라이언트가 봄, 꼭대기에서 같은 위치. 서버 보정(MoveGuard) 0회 | 없음 | - | Ledge_… |
| 사망 → 데스캠 | `Dead`(SV), 킬 기록 `KillEntry`(Broadcast: 킬러·무기·헤드샷·원인), 숫자 `PlayerStats`(Broadcast: 준 피해, 버틴 시간) 먼저 | 죽은 원격 플레이어의 화면: 데스캠이 킬러 이름·무기 표시·거리를 띄움, 4초 뒤 결과 패널 | 없음 | - | Death_… |
| 결과 화면 | 같은 `KillEntry`/`PlayerStats`/`MatchStatus`(Broadcast) | "탈락", 순위 #6/전체 6, 킬 0, 준 피해(원격이 호스트에게 쏜 만큼), 버틴 시간, "누가 어떻게" | 없음 | - | Death_… |
| 관전 | 후보는 복제된 `Combatant` 목록(`Dead`, `TeamId`), 킬러는 `MatchState.KillerOf`(KillEntry에서) | 킬러부터 보고, 다음 사람으로 넘기면 다른 원격 플레이어도 따라감. 보던 사람이 죽으면 그 킬러로 | 보던 사람이 죽은 프레임에 `Dead`(SV)는 왔는데 킬 기록(Broadcast)이 아직 안 와서 **킬러를 모르고 아무나 보게 됐음** | `SpectateCamera`: 킬 기록이 오거나 1초가 지날 때까지 시체를 보고 있다가 고름 | Death_… |
| 늦은 접속자: 차 | 좌석·엔진·연료·경적·피해 단계는 SV, 구멍·찌그러짐은 `TargetHistory` | 앉아 있는 운전자·동승자(같은 좌석 위치), 켜진 엔진·남은 연료·울리는 경적과 엔진 소리, 잔해 상태 | 없음 | - | LateJoiner_SeesTheDriver…, Fire_… (late), RemoteShots_… (late) |
| 늦은 접속자: 수영·매달림·죽은 사람 | 위치와 `Stance.State`/`Parkour`/`Dead`(SV) | 물에 엎드린 사람, 매달린 사람(자세 포함), 죽은 사람이 죽은 채로, 남은 생존 수가 맞음 | 이미 일어난 킬 기록은 늦은 접속자에게 가지 않음(순위·이름은 이후 사망부터) | 허용(관전 후보는 복제된 `Combatant`에서 오고 킬러 조회는 이후 사망부터 동작) | LateJoiner_SeesASwimmer…, Death_… (late) |

## 실제 지연에서의 부드러움 (RemoteDriver_IsSeenSmoothly…)
호스트·운전자 A·구경꾼 B, 지연 왕복 100 ms·손실 2 %. A가 4석 차를 3.2초 몰아 약 27 m 가고(동승자 한 명), 가속 중인 1.2~3.2 s를 0.1 s 창(0.05 s 간격)으로 재서 운전자 화면의 같은 창과 견줌. 기준은 달리는 플레이어와 같다(#161: 정지 0.2 s 이하, 55 % 미만 창 3 % 이하, 최고 1.6배 미만).

| 보는 쪽 | 창 수 | 평균 속도 / 운전자 | 가장 빠른 창 / 운전자 | 55 % 미만 창 | 가장 긴 정지 |
|---|---|---|---|---|---|
| 서버 | 38 | 0.92 | 0.95 | 0 | 0 s |
| 제3 클라이언트 B | 38 | 0.88 | 1.22 | 1 | 0 s |

(평균이 1보다 낮은 것은 가속 중인 차를 약 0.15 s 늦게 보기 때문이다. `Logs/net-replication/vehicles/RemoteDriver_…log`)
`NetworkTransform`은 기본 설정(보간 2틱) 그대로다. 이 수치에서는 따로 조정할 이유가 없었다.

## 하네스 한계 (테스트 해석)
한 프로세스에 서버와 클라이언트 매니저가 모두 있어, 같은 차·같은 사람의 사본이 **같은 물리 세계 같은 자리에** 겹쳐 있다. 실제 게임(프로세스당 하나)에는 없는 현상이라 테스트 쪽에서만 풀었다.
- `ReplicaLayers`: 같은 차의 사본끼리, 그리고 차의 사본과 다른 매니저의 사람 사본 사이의 충돌을 끈다(안 끄면 시뮬레이션하는 차가 겹친 사본에 밀려 공중으로 튄다).
- `SeparateTwins`: 운전하는 사본의 바퀴 레이캐스트가 서버 사본을 밟지 않도록 서버 사본을 `Ignore Raycast` 층에 둔다(운전 테스트만).
- `LocalSwitch`: 프로세스에는 "내 플레이어"가 하나뿐이라(`PlayerController.Local`, `Combatant.LocalPlayer`) 차 키·결과 화면·관전 카메라가 누구를 위해 동작할지 테스트가 고른다.
- 모서리 테스트는 서버 사본의 충돌체를 `Ignore Raycast` 층에 둔다(소유자의 모서리 탐색이 자기 서버 사본을 맞히지 않도록).
- 테스트 상자의 봇은 플레이어를 쏜다: 탑승 테스트는 `BotRules.TargetsPlayers = false`.
- F 키로 타기: 한 프로세스에 차 사본이 여럿이라 `NearestEnterable`이 아무 매니저의 사본을 고를 수 있다. 키가 아무것도 못 했으면 테스트가 같은 요청(`RequestEnter`)을 그 클라이언트의 차 사본에 직접 한다(로그에 남음).

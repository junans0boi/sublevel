# 오염·열쇠·칩 → 봇 트랙 인터페이스 요청 (2026-11-16)

`AI/*`는 봇·네트워크 트랙 소유라 아래는 **요청**이다. 서버 쪽 API는 `track/pollution`에 이미 있다(코드 위치 병기). 모두 서버에서 호출, 봇은 `Combatant`를 넘긴다.

## 지금 봇이 쓰는 것 (T10에서 옮김, 변경 줄 7+/12-)
`BotController`는 `PollutionDirector.Instance`로 `OnlyFinalRegionLeft`·`FinalRegion`·`RegionIndexAt`·`RegionCentre`·`IsDoomed(p)`·`TryEscapeTarget`을 읽는다. 구 통로·보급 소구역 분기는 삭제. 자유 승강(`ServerBotRide`)은 열쇠 승강기로 대체될 때까지 남겨 둠(ADR-170).

## 1. 오염 예고 인지·이동 (요청)
| 필요 | 이미 있는 것 | 봇 쪽 요청 |
|---|---|---|
| 경고 중인 지역 | `StateOf(region)==Warning`, `SecondsLeft(region)`(확정까지 초) | 경고 지역에 있으면 `ZoneGoal.Escape`로 미리 이동(지금은 전선 45초 전부터만) |
| 전선 임박 | `IsDoomed(p)`(전선 도달 ≤45초), `PollutionDirector.DoomedLeadSeconds` | 필요하면 값 조정 요청 |
| 이탈 목적지 | `TryEscapeTarget(from, skip, out target, out pass, out toRegion)`(가장 가까운 비오염·비임박 지역 중심) | 목적지 도달 후 최종 지역 쪽으로 이동 |
| 방독 사용 | `KeyHolder.Filters/Antidotes`(SyncVar), `KeyHolder.ServerUseGas(0=필터,1=해독제)` | 가스 안·임박 시 해독제 > 필터 순으로 사용, 최종 지역은 무효(사용 금지), 줍기 우선순위에 `LootKind.GasFilter(23)/Antidote(24)` 추가(`BotRulesItems`) |

## 2. 열쇠 줍기·사용 (요청)
- 줍기: `LootKind.Key(12)`(기존). 승강기 열쇠 외 용도 없음. 보유 수 `KeyHolder.Keys.Value`.
- 사용 흐름(서버): 봇이 승강장 안(`ElevatorRides.StationAt(pos)`)에서
  1. `KeyElevatorNet.Instance.ServerInsert(combatant, station)` → true면 문 열림 30초(승강장 안의 누구나 자동 탑승, 최대 4)
  2. 동승 대기 후 `ServerClose(combatant)`(선택, 안 하면 30초 뒤 자동)
  3. 선택 단계(15초)에 `ServerSelect(combatant, region)` — 목적지는 `KeyElevatorTravel.BotDestination(from, states, finalRegion)`(최종 지역 우선, 아니면 첫 비오염 지역; `PollutionDirector.Instance.States`로 상태 배열)
- 상태 읽기: `KeyElevatorNet.Instance.System.Station(i)`(`Phase/PhaseLeft/HolderId/Riders`), 팀 쿨다운 `System.Cooldowns.Left(teamKey, now)`. `KeyElevatorNet.TeamKey`는 private → 필요하면 public으로 열겠다고 알려 달라.
- 사용 판단(요청): 자신의 지역이 경고·임박이고 열쇠가 있고 팀 쿨다운이 없을 때. 승강장이 오염 확정 지역이면 사용 뒤 90초 충전.

## 3. 칩 (스쿼드 봇)
- 칩 장부 `PollutionDirector.Instance.Chips`(`ChipBoard`), 위치 `ChipPosition(owner, out pos)`, 줍기 `ServerPickUpChip(carrier, owner)`, 보관함 입금 `ServerDepositChips(carrier, boxRegion)`(승강장의 지역 번호). 90초 안에만 가능.
- 요청: 팀원 봇이 죽으면 가까운 동료 봇이 칩으로 이동·줍기·가까운 비오염 승강장 보관함 입금. **부활 실행(몸 되살리기)과 보관함 오브젝트는 오염 트랙이 만든다**(아직 없음) — 봇은 줍기·입금까지만.

## 4. 봇 팀 구성
- `Combatant.TeamId`/`TeamMatch.Mode`(기존). 요청 없음(오염은 팀 id만 사용, 솔로는 개인 id).

## 5. 오염이 봇에 요구하는 필드·이벤트 (봇 쪽 추가 요청)
- `BotStats`에 `escapeStalls`(오염 이탈 중 정지) 항목, 판 로그에 "오염 사망 수"(`DamageCause.Zone`) — 판 길이·이탈률 통계(시뮬 대조 25/28%)에 필요.
- 확인 요청: 봇이 승강장에서 `ServerInsert`할 때 `Combatant.Standing`이어야 하고 열쇠는 소모(무효 시 반환)된다.

# 멀티 복제 감사 A: 전투 (#163)

원격 클라이언트가 호스트와 같은 것을 보고 듣는지, 싱글 티켓(G1~G10 등)에서 만든 시스템별로 점검한 표.
방법: 코드의 SyncVar / ServerRpc / ObserversRpc / 서버 전용 상태를 읽어 표를 만들고, 호스트 + 원격 클라이언트 PlayMode(`ReplicationCombatTests`, `MultiHarness`)로 원격 쪽 결과만 확인한다.
규칙: 판정은 서버, 클라이언트는 입력·표시만(ADR-029). RPC는 `ObserversRpc` 관찰자에게만(관찰자는 거리로 정해짐: 관심 영역 #167, ADR-135).

범례: SV = SyncVar(서버 쓰기, 모든 관찰자 읽기), OR = ObserversRpc, SR = ServerRpc(소유자 요청), 서버 = 서버 객체에만 있음.

| 시스템 | 상태 / 이벤트 (수단) | 원격 클라이언트가 보는 것 | 갭 | 조치 | 테스트 |
|---|---|---|---|---|---|
| 몸의 충돌체(피격 판정) | 플레이어 프리팹의 충돌체는 `CharacterController` 하나뿐인데 `PlayerController`가 소유자만 켰음 | 서버(호스트·전용 서버)의 원격 플레이어 복사본에는 충돌체가 없어 사격·수류탄·근접이 통과했다. 호스트가 원격 플레이어를 쏠 수 없는 상태 | **있었음(가장 큰 갭)** | `PlayerController.OnStartNetwork`: 소유자가 아닌 쪽(서버의 복사본, 다른 클라이언트의 복제본)도 캡슐을 켠다. 움직이는 것은 소유자뿐 | HostShot_ReachesTheRemote… (머리·다리 판정과 피해자 체력) |
| 사격 | `RpcShotFx`(OR): 시작점·끝점·법선·명중 종류·부위·피해자·표면 | 총구 화염·소리·예광탄·충돌 효과·피격 표시 데이터 | 없음 | - | HostShot_ReachesTheRemote…, RemoteShot_ReachesTheHost… |
| 피격 부위 표시 | 같은 메시지에 부위(머리/몸/다리) 포함, 피해자는 NetworkObject | 머리 = 머리 표시, 다리 = 연한 파랑 | 없음 | - | HostShot_… (머리·다리) |
| 체력 / 사망 | `Hp`, `Dead`(SV) | 피해자 본인과 관찰자가 같은 값 | 없음 | - | HostShot_… |
| 탄도 투사체 | 서버가 `BulletWorld`에서 비행. 발사 순간 `Launch`, 착탄 시 `Impact` 메시지(OR) | 총구 효과 먼저, 도착 때 충돌 효과 | 없음 | - | BallisticsProjectile_ShowsMuzzleThenImpact… |
| 관통 | 서버 판정, 입구·출구 표시를 각각 `Impact` 메시지(OR)로 | 얇은 벽의 두 면에 표시 | 없음 | - | Penetration_BothMarksOfAThinWall… |
| 근접 | `RpcPunch`(OR) | 휘두름 소리·동작·피격 표시 | 없음 | - | Punch_ReachesTheRemote |
| 부착물 | `CombatantOptics.Gear0..4`, `Bag`(SV, 슬롯별 묶음 정수) | 총에 붙은 모델(`AttachmentVisual`), 수정치(`ActiveMods`), 소음기 표시 | 없음 | - | Attachments_FittedOnTheServer… |
| 반동 | 입력 쪽(소유자 화면)에서만 계산. 서버는 반동을 쓰지 않음, 수정치는 위와 같은 SV | 관찰자는 반동을 보지 않고 총구 화염만 봄(PUBG도 같음) | 없음 | - | (수정치 일치는 위 테스트) |
| 소음기 가청 거리 | 서버 `ShotHearingRadius`(봇 귀) = 소음기 1/4. 원격 소리는 `CombatantAudio.Suppressed`가 복제된 `ActiveMods`를 따라감 | 소음기 쏘면 멀리서 작게 들림, 떼면 원래대로 | 없음 | - | Attachments_… (오디오 훅, 봇 반경, 해제) |
| 영점 조절 | `CmdSetZero`(SR) → 서버 `ZeroMeters` | 탄이 소유자 영점에서 교차 | 없음 | - | Zeroing_TheRemoteOwnerSetsItsOwnDistance… |
| 투척물 생성 | 서버가 `ThrownItem`(NetworkObject + NetworkTransform) 생성, 던지기 `CmdThrow`(SR) | 날아가는 수류탄 | 없음 | - | Throwables_AllKinds… |
| 파편 폭발 | `RpcExploded`(OR), 피해는 서버 | 폭발 효과·소리, 피해자 체력 | 없음 | - | Throwables_AllKinds… |
| 조리(핀 뽑음) | `Cooking`(SV) + 각 쪽 시계 | 남은 퓨즈 | 없음 | - | Throwables_AllKinds… |
| 섬광 | `RpcFlashed`(OR), 눈부심은 각 클라이언트가 자기 플레이어 기준으로 계산 | 터짐 + 본인 시야 | 없음 | - | Throwables_AllKinds… |
| 화염병 | `RpcFired`(OR) + 불에 닿은 몸의 `Burning`(SV) | 불꽃·소리, 타는 몸 | 없음 | - | Throwables_AllKinds… |
| 연막 / 화염병(늦은 접속) | 구름·불은 객체가 남아 있는 동안만 존재하지만 효과는 RPC로만 전달됨 | 늦게 들어온 클라이언트는 구름·불이 안 보임 | **있었음** | `ThrownItem.Effect`/`EffectLeft`/`ThrowerObject`(SV) 추가, 늦게 생성된 클라이언트가 남은 시간으로 효과를 띄움 | LateJoiner_GetsTheSmokeCloudAndTheFire… |
| 회복·부스트 | `UsingKind`/`StartTime`/`Duration`/`Gauge`/개수(SV) | 사용 중 표시, 진행률(각 쪽 시계), 게이지, 개수 | 없음 | - | HealAndBoost_… |
| 방어구·가방 | `HelmetTier/Durability`, `VestTier/Durability`, `BackpackTier`(SV) | 같은 등급·내구도·용량, 가방 모델 | 없음 | - | ArmourAndBackpack_… |
| 집기 목록 | 소유자 화면(`PickupList`)이 복제된 `LootItem`을 거리로 걸러 표시, F는 `CmdPickup`(SR) | 원격 소유자가 줍고 장비에 들어옴 | 없음 | - | Loot_PickedUpByTheRemote… |
| 사망 상자 | 서버가 소지품을 `LootItem`(NetworkObject)으로 뿌림, 표시 상자는 `Dead`(SV)를 보고 각 쪽에서 | 떨어진 물건, 시체 위 표시 | 늦은 접속자는 표시 상자를 못 봄(이미 사망한 쪽엔 만들지 않는 규칙, 물건은 보임) | 규칙 유지 | Loot_PickedUpByTheRemote… |
| 총알 자국 / 충돌 표면 효과 | 각 클라이언트가 받은 `Impact`로 로컬 생성 | 같은 표면별 효과 | 늦은 접속자는 이전 자국 없음(영구 상태 아님) | 규칙 유지 | (효과 수신은 위 테스트) |

원격 소유자의 사격·영점·줍기는 서버가 서버의 눈 위치로 다시 판정한다(클라이언트가 보낸 점은 쓰지 않음).

## 같은 프로세스 하네스의 한계 (테스트 해석)
호스트와 원격 클라이언트가 한 프로세스라 `static` 이벤트(`ThrownItem.Client*`)와 씬 싱글턴은 둘이 공유한다. 그래서 "원격에도 도착했다"는 이벤트 수가 2(호스트 클라이언트 + 원격)로 늘어나는지로 확인하고, 상태는 원격 매니저가 만든 복제본(`MultiHarness.ReplicaOf`)에서만 읽는다.

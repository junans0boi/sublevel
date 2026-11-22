# INEFFICIENCY_AUDIT — 비효율 감사 (2026-11-22, main 97ff434 기준)

준환 요청: "프로젝트 내부에 비효율적인 방법을 사용 중인 것들이 있다면 그것도 해결해줘".
범위: 런타임 핫패스(클라·서버·봇), 네트워크, 에디터·파이프라인, 중복 구현, 죽은 코드·에셋. 문·유리 복제(mode-parity 세션 담당)는 제외.

## 측정 방법
- `MatchGcAllocTests`(PlayMode, Explicit, 새로 추가): World 호스트 매치, 봇 40 + 플레이어(HUD 켜짐), 15초 워밍업 뒤 600프레임.
  "GC Allocated In Frame"·"GC Allocation In Frame Count"·"Main Thread" ProfilerRecorder. `Logs/perf/match_gc.json`.
- `MATCH_GC_BREAKDOWN=1`: 같은 장면을 프로파일러로 300프레임 기록해 GC.Alloc 바이트를 호출 마커별로 합산(`Logs/perf/match_gc_breakdown.txt`).
  원인을 좁힐 때는 임시 `Profiler.BeginSample`을 하위 호출에 넣어 다시 쟀다(측정 뒤 제거).
- 에디터 batchmode, 비독점(다른 Unity와 겹칠 수 있음). GC 바이트는 부하와 무관, 메인 스레드 ms는 참고값.
- 측정하지 못한 항목은 "추정"으로 표시했다(코드 읽기 + 산수).

## 기준선 (수정 전)
| 지표 | 값 | 예산(PERF_BUDGET) |
|---|---|---|
| GC 할당/프레임 평균 | **5,056 B** (p50 3,314 B, 최대 260,924 B) | 목표 0, 상한 1 KB → **초과** |
| GC 할당 횟수/프레임 | 105 | — |
| 메인 스레드 평균 / p99 | 6.39 ms / 8.62 ms | — |
| World 로드 시 경고 | 3,275줄(스택 트레이스 포함) | — |

분해(수정 전, B/프레임): BotController.Update 1,805 · FishNet NetworkTransform.SendToClients 1,443 · CombatantAnimation.LateUpdate 435 · WheelHud 288 · PollutionHudView 212 · PlayerController 152 · WaitingHud 72.

## 순위표
효과/노력 순. 상태: ✅ 이번에 고침 · 🎫 이슈 · 📝 기록만.

| # | 항목 | 위치 | 왜 낭비인가 | 비용/영향 | 해결 | 노력 | 상태 |
|---|---|---|---|---|---|---|---|
| 1 | 봇 클로저가 매 프레임 할당 | `AI/BotController.cs` TryWarnedLeaveTarget(람다 `(x,z) => zones.RegionIndexAt(..)==own`) | 람다가 매개변수를 잡아 C#이 **메서드 진입마다** 클로저 객체를 만듦(람다는 2초에 한 번만 실행) | **실측** 봇당 1회/프레임, 40봇 1,280 B/f(80봇 ≈2.6 KB/f) | 람다를 별도 정적 메서드로 이동 | S | ✅ |
| 2 | 발 붙이기(PlantFeet)가 배열 4개+디버그 문자열 2개 생성 | `Gameplay/CombatantAnimation.Gait.cs` PlantFeet | LOD0 전투원마다 매 프레임 `new float[2]`×2·`float?[2]`·`Vector3[2]`, `PlantDebug` 보간 문자열(float 박싱) | **실측** 25회·1,083 B/f(근처 전투원 수에 비례) | 배열을 필드로 재사용, PlantDebug는 읽을 때 포맷(`PlantDebug(side)`) | S | ✅ |
| 3 | 발소리마다 `Resources.LoadAll` | `Gameplay/CombatantAudio.cs` StepClips | 캐시 확인 전에 폴더 결정용 `LoadAll`+`FindAll`+`Sort`+문자열 연결+클로저 2개 | 걸음당 0.3~0.6 KB + 네이티브 조회, 걷는 전투원당 초당 ~2회(추정) | 캐시 먼저(클립+게인을 SurfaceId별 보관) | S | ✅ |
| 4 | 대기섬 없는 씬에서 대기 HUD가 매 프레임 `GameObject.Find` 2회 | `Net/WaitingHud.cs` MoveLiftCar·TryBuildBoard | 섬을 못 찾으면 **씬 전체 계층을 프레임당 2번 순회**(NetworkManager 상주라 Race_*·Training 씬 등). World에는 섬이 있어 해당 없음(실측 변화 없음) | 씬 객체 수 비례 CPU(미실측) | 못 찾으면 1초에 한 번만 다시 찾기 | S | ✅ |
| 5 | HUD 위험 슬롯 비교가 리플렉션+박싱 | `Core/HudHazardView.cs`(struct, Equals 없음) ← `HudHazardSlot.Set` | `ValueType.Equals`가 string 필드 때문에 리플렉션으로 필드마다 박싱 | **실측** 7회·212 B/f | `IEquatable<HudHazardView>` 구현 | S | ✅ |
| 6 | 휠 HUD 람다 2개 매 프레임 | `Gameplay/WheelHud.cs` LateUpdate | `me`/`player`를 잡는 람다 → 클로저+델리게이트 3개 | **실측** 3회·288 B/f | 콜백 대신 선택 값을 반환 | S | ✅ |
| 7 | World 로드 때 경고 3,270줄(스택 트레이스) | `Gameplay/WorldMeshBank.cs` `mesh.subMeshCount = subs` | Float16 위치 메시에서 bounds 재계산 시도 → 메시마다 경고 + 스택 | 로드마다 3,275줄, 테스트 로그 122 MB 중 대부분 | `SetSubMeshes(descs, DontRecalculateBounds)` 한 번 | S | ✅ |
| 8 | 지연 보상 이력이 오래된 쪽부터 선형 탐색 | `Core/LagComp.cs` PositionHistory.TryAt ← `Net/LagCompensation.cs` | 240칸 링(60fps에서 4초)을 0.1~0.3 s 되감기에 앞에서부터 ~222~234칸 훑음, 전투원 80명 × 탄 구간마다 | 사람이 쏠 때 Rewind당 ~19k 반복(추정), 산탄 1발 ~170k | 최신 쪽부터 역탐색(6~18칸), 결과 동일 | S | ✅ |
| 9 | 탄약 자동 줍기가 같은 프레임에 몰림 | `Net/CombatantCarry.cs` Update | 같이 스폰된 전투원이 0.25초마다 **같은 프레임에** 전 루팅(~2k) 스캔 | 80×2k 검사가 15프레임마다 한 프레임에(스파이크, 추정 2~5 ms) | 첫 스캔 위상 무작위(분산). 공간 격자는 🎫 | S | ✅(분산), 격자 🎫 #473 |
| 10 | 열쇠 승강기 탑승자 `ToArray` 매 프레임 | `Core/KeyElevatorSystem.cs` Tick | 탑승 중 역마다 매 프레임 배열 복사(도착 때만 필요) | 탑승 중 역당 1회/f | 재사용 버퍼, 도착 때만 배열 | S | ✅ |
| 11 | 사망캠·탑승 카메라 `SphereCastAll` | `Gameplay/SpectateCamera.cs` Clear, `Gameplay/PlayerController.cs` RideCameraSpot | 매 프레임 `RaycastHit[]` 할당 | 해당 화면에서 1~2회/f | `SphereCastNonAlloc` + 정적 버퍼 | S | ✅ |
| 12 | FishNet NetworkTransform 송신 할당 | 패키지 `NetworkTransform.SendToClients`(FishNet 4.7.3) | 호스트 모드에서 틱마다 할당(패키지 내부) | **실측** 29회·1,443~1,520 B/f — 남은 GC의 최대 몫 | 전용 서버에서 재측정 → 패키지 설정/업그레이드/포크 판단 | M | 🎫 #469 |
| 13 | 루팅 1,300개가 매 프레임 콜백 2개 | `Gameplay/LootMarker.cs` LateUpdate, `Gameplay/LootView.cs` Update | ~2,600 MonoBehaviour 호출/프레임, 대부분 숨김 상태 | 추정 0.5~1.5 ms/f(가장 큰 순수 CPU 후보) | LootView는 SyncVar OnChange로 끄기, LootMarker는 정적 관리자+격자 | M | 🎫 #470 |
| 14 | 원경 전투원도 포즈 파이프라인 전부 | `Gameplay/CombatantAnimation.cs` LateUpdateBody | LOD2·화면 밖 60~70명도 Turn ~20회+Pose* 12개 | 추정 ~1 ms/f | `_lod>=2 && !isVisible`이면 발 접지 뒤 생략 | S~M | 🎫 #471 |
| 15 | 관심 영역 검사가 객체마다 관찰자 재계산 | `Net/InterestCondition.cs` | 연결마다 `GetComponent<Combatant>()`+연결 객체 순회를 객체 수만큼 | 80인에서 추정 0.3~0.7 ms/f | 틱당 연결별 관찰자 캐시, 루팅은 격자 | S~M | 🎫 #472 |
| 16 | 산탄·탄 구간마다 큰 RPC | `Net/Combatant.cs` RpcShotFx | 펠릿·구간마다 Vector3 3개 RPC, 총구 위치 반복 | 산탄 1발 ~400 B×관찰자, 결승 구역 15 KB/s 초과 가능(추정) | 한 발의 펠릿을 한 RPC로 묶고 압축 | M | 🎫 #472 |
| 17 | 매칭 중 로비 명단 0.5초마다 전체 재방송 | `Net/LobbyNet.cs` PushQueueState→BroadcastState | 카운트다운만 바뀌는데 명단 전체를 전원에게 | 80인 ~320 KB/s 송신(추정, 인원² 증가) | 명단은 변경 시만, 대기 상태는 작은 메시지 | S | 🎫 #472 |
| 18 | 봇 팀 로직이 매 프레임 전원 2회 순회 | `AI/BotController.Team.cs` NearestTeammate·TeamCentre | Sense처럼 간격 분할 안 됨 | 스쿼드 모드 ~12.8k 반복/f(추정) | 프레임당 팀 명단 1회 구성 또는 감지 간격 | S | 🎫 #473 |
| 19 | 미니맵 깜빡임마다 텍스처 전체 재도색 | `Gameplay/MinimapView.cs` RefreshZones | 경고 중 초당 2회 192² 텍셀×8 이웃 + Apply(147 KB) + 라벨 문자열 | 0.5~2 ms 스파이크 초당 2~3회(추정) | 깜빡임은 머티리얼 알파, 재도색은 상태 변경 시만 | S | 🎫 #473 |
| 20 | PlayMode 테스트가 테스트마다 World 재로드 | 46개 픽스처(TeamPlayTests 15회, WaitingIslandPlayTests 13회 …) | 116 MB 씬을 테스트마다 파싱 | 전체 실행당 ~125회 로드, 추정 20~40분 | 읽기 전용 픽스처는 1회 로드 공유 | M | 🎫 #474 |
| 21 | 오클루전 베이크가 매 빌드 + 결정성 확인용 2회차에도 | `Editor/WorldBuilder.cs` BakeOcclusion | 해시 비교용 두 번째 Build도 Umbra 베이크·116 MB 저장 반복 | 머지당 수 분(추정) | `SKIP_OCCLUSION=1` 환경 변수 | S | 🎫 #474 |
| 22 | 충돌 메시 902개가 씬에 인라인 + blob 중복 | `Editor/WorldMeshes.cs` Store.Finish(MeshCollider 메시 미해제) | 런타임에 WorldMeshBank가 어차피 다시 넣음 | World.unity 116 MB 중 19 MB | Finish에서 collider 메시도 비움 | S | 🎫 #474 |
| 23 | 무기 FBX 강제 재임포트 42회 | `Editor/MagazineKit.cs` Prepare(+GearKit 같은 패턴) | 읽기 가능 토글 위해 ForceUpdate 2회×21정, LOD 재생성 | TestBoxBuilder/CharacterKit 실행마다 | 탄창 메시를 해시 키 `.asset`로 캐시, 토글 헬퍼 하나로 | M | 🎫 #474 |
| 24 | 런타임에 월드 계획을 두 번 재생성 | `Net/DoorSystem.cs` BuildFromMap, `Gameplay/PollutionWorld.cs` Build | 같은 `WorldGenerator.Generate`(+건물 계획)를 클라·서버 모두 2회 | World 로드당 추정 1~6 s | 최소: 정적 캐시 공유. 정석: 빌드 때 베이크 | S~M | 🎫 #474 |
| 25 | EditMode 테스트 핫스팟 | `ForestTests`(84 s, Plan 8회 재계산), 섬 WorldPlan을 5개 픽스처가 따로 생성 | 같은 계산 반복 | 추정 30~40 s 절약 | 공용 `TestWorlds.Island` + 캐시 | S | 🎫 #474 |

## 중복 구현 (단일 진실 공급원) — 🎫 #475
| 묶음 | 위치 | 해결 |
|---|---|---|
| mm:ss 시계 8곳, 출력도 다름(`0:32`/`00:32`) | Core/PartyLobby:143, HudHazardView:21, QueueText:17, PollutionHud:113, WaitingBoard:80, Gameplay/ResultScreen:280, ElevatorPanel:272, Net/WaitingHud:355 | Core `TimeText.Clock(seconds, padMinutes)` 하나 |
| "ObjectId로 Combatant 찾기" 9곳(선형 탐색) | Net/Vehicle:705, Combatant.TeamCarry:72, KeyElevatorNet:83, CombatantJerrycan:136, MatchState.Team:~103, AI/BotController.Team:205, Gameplay/SpectateCamera:181, MatchAutoplay:200, KillFeed:141 | `Combatant.Find(id)`(사전 기반) |
| world.json 로드·폴백 5곳 | Net/DoorSystem:108, Gameplay/PollutionWorld:27, Editor/WorldBuilder:209, MapBaker:71, VehicleBuilder:630 | 런타임 `WorldSpec.Load()` 하나 |
| 로트 시드 해시 3곳(문·루팅이 일치해야 맞음) | Editor/WorldBuilder.Structures:55, Net/DoorSystem:68, Tests/LootRoleTests:64 | `Buildings.LotSeed(seed, index)` |
| URP Lit 단색 머티리얼 6곳 | Net/ElevatorDoor:283, DeathCrate:159, Door:247, Gameplay/SupplyView:130, BackpackVisual:117, WornGearMeshes:89 | `FxKit.Lit(name, color)` 캐시 |
| rules.json ↔ C# 기본값 369개 중복, 일부 불일치 | Core/Deployment:139 `MaxPlayers=16`(json 64), Core/VehicleRules:59-61(json과 3값 다름) | 기본값=JSON 확인 테스트(의도적 예외는 화이트리스트) |
| `RoadProfile` 73줄은 테스트만 사용 | Core/World/RoadProfile.cs vs WorldGenerator:491 SolveRoadProfile | 하나로 합치기 |
| FishNet 프리팹 재구성 리플렉션 복붙 | Editor/TestBoxBuilder:150, VehicleBuilder:638 | 한쪽 호출 |
| FBX 읽기 가능 토글 2곳 | Editor/MagazineKit:113, GearKit:136 | 공용 헬퍼 |

## 죽은 코드·쓸모없는 에셋 — 🎫 #476 (ServerPerfProbe는 #473)
| 항목 | 위치 | 해결 |
|---|---|---|
| 소비자 없는 머티리얼을 빌드마다 재생성 | Resources/Lockdown/BarrierMaterial.mat, Editor/TestBoxBuilder:197 | 메서드·머티리얼 삭제 |
| 바이트 동일 1 MB 배경 6장(각자 로드) | Resources/Ui/Screens/{char,credits,custom,result,set_gfx,tutorial}_bg.png | 1장으로, JSON src 6곳 변경 |
| `LoadAll` 후 버려지는 옛 클립 | Kenney footstep_* 24개, SynthDoors door_open/close 6개 | 삭제(원장 표시) |
| 녹음본 뒤 도달 불가 합성 폴백 ~150줄 | AmbientBeds:190, ElevatorAudio:94, VehicleAudio:153-159, GlassFx:118 | 녹음 확정 시 삭제(선택) |
| 캡처·디버그 도구 ~3,700줄이 런타임 어셈블리 | FeelCapture*, PerfCapture, MatchAutoplay, ServerPerfProbe 등 | 개발용 asmdef(우선순위 낮음) |
| 측정 도구 자체가 스파이크 | Net/ServerPerfProbe:187(전체 프레임 매 창마다 정렬) | 고정 버킷 히스토그램 |

## 결과 (같은 테스트, 수정 후)
| 지표 | 전 | 후 | 변화 |
|---|---|---|---|
| GC 할당/프레임 평균 | 5,056 B | **2,790 B** | −45% |
| GC 할당/프레임 p50 | 3,314 B | **1,082 B** | −67% |
| GC 할당 횟수/프레임 | 105 | **47** | −55% |
| 게임 코드 몫(분해, FishNet 제외) | ~3,200 B/f | **~740 B/f** | −77% |
| 메인 스레드 평균 / p99 | 6.39 / 8.62 ms | 6.37 / 8.32 ms | 오차 범위(비독점) |
| World 로드(LoadScene → 다음 프레임) | 10.82 s | 10.45 s | −0.37 s |
| World 로드 경고 | 3,275줄 | **0** | — |

항목별(분해, B/프레임): 봇 클로저 1,280→0 · PlantFeet 1,083→0 · WheelHud 288→0 · PollutionHudView 212→0 · CombatantAnimation 합계 435→22.
지연 보상 TryAt(#8): 240칸 링·0.1~0.3 s 되감기에서 반복 ~222~234 → 6~18(같은 표본 쌍을 고르므로 결과 동일, `LagCompTests` 통과). 미실측 항목(#3·#4·#9·#10·#11)은 코드상 할당/호출 제거만 확인.

남은 것: FishNet NetworkTransform 1,462 B/f(🎫 #469), BotController.Update 435 B/f(감지 주기마다 2.4회, 원인 미확인), PlayerController.Update 152 B/f(PickupList.Refresh 람다 등), WaitingHud.Update 72 B/f(원인 미확인). 남은 게임 코드 할당 ~660 B/f는 상한 1 KB 안이지만 목표(0)에는 못 미친다.

재현: `tools/unity.sh test playmode Game.Tests.MatchGcAllocTests` (분해는 `MATCH_GC_BREAKDOWN=1`, 봇 수는 `MATCH_GC_BOTS`).

## 테스트
- EditMode 전체 **1,763/1,763 통과**(LagCompTests·PollutionHud·KeyElevator 포함).
- 관련 PlayMode 15개 픽스처(71개): 67 통과, 4 실패 — **HEAD(수정 전)에서도 같은 메시지·값으로 실패**(기존 문제):
  AnimationJointTests.FlatGround_SolesOnTheGround…(발목 0.080 < 0.094), TeamPingPlayTests.Ping_Reaches…(듀오 팀 5≠1),
  WorldSceneTests.NeighbouringRegions…(R0 hub→서령 북안부 경로 끊김), WorldSceneTests.World_RendersWithinBudget(p99 11.9/13.0 ms, 비독점).

## 바뀐 파일
`AI/BotController.cs`, `Core/HudHazardView.cs`, `Core/KeyElevatorSystem.cs`, `Core/LagComp.cs`, `Gameplay/CombatantAnimation.Gait.cs`, `Gameplay/CombatantAudio.cs`, `Gameplay/FeelCapture.Character.cs`, `Gameplay/PlayerController.cs`, `Gameplay/SpectateCamera.cs`, `Gameplay/WheelHud.cs`, `Gameplay/WorldMeshBank.cs`, `Net/CombatantCarry.cs`, `Net/WaitingHud.cs`(모두 `Assets/_Project/Scripts/`), `Tests/PlayMode/AnimationJointTests.cs`, 새 `Tests/PlayMode/MatchGcAllocTests.cs`. 문·유리 파일은 건드리지 않음.

# 최적화 분석 (2026-11-22, main bfc870f)

준환: "최적화 위해서 분석합시다." 추측 대신 측정. 전부 **독점 창**(슬롯 3개 보유, 다른 Unity 0)에서 M4 Pro로 쟀다.
원자료: 워크트리 `perf-analysis-64c5c6`의 `Logs/opt/`, `Logs/perf/`, `Logs/net-interest/`, `Logs/net-race/`.

## 0. 한 줄 요약
- **클라이언트 렌더가 예산을 넘는 유일한 큰 항목**이다. 원인은 GPU 채움이 아니라 **원거리 드로우 제출**(해상도 0.75배 −0.2 ms, far clip 절반 −1.4~2.6 ms).
- 서버(M4 Pro)는 80인 평균 6.2 ms·p99 ≤10 ms·RSS 1.06 GB로 통과. 그런데 **오라클 ARM 환산은 7~17 ms로 폭이 넓어** 실측이 필요하다. mac 서버 측정 조건부터 오라클과 다르다(Fish-Net이 500 fps로 덮어씀).
- 네트워크는 클라당 9.8 KB/s(예산 15)로 통과. **95.7%가 NetworkTransform**이고, 80명으로 환산한 서버 송신은 6.25 Mbit/s로 예산 6을 약간 넘는다.
- 메인 스레드는 호스트 80인 기준 8.0 ms(에디터). 스크립트가 75%를 쓰고 물리·내비·애니메이션은 작다.

## 1. 기준선

### 1-1. 클라이언트 렌더 (방법 A: 에디터 오프스크린 1080p, 포스트 켬, `RenderProbe`)
| 장면 | avg ms | p99 ms | 드로우 | SetPass | 삼각형 | 판정(p99≤10·드로우≤2,500·SetPass≤60·tri≤1.5M) |
|---|---|---|---|---|---|---|
| 섬 look_01 R1 도로(해 등짐) | 7.35 | 9.26 | 930 | 61 | 420k | SetPass ✗ |
| 섬 look_02 R1 도로(역광) | 7.88 | **11.77** | 1,365 | 57 | 705k | p99 ✗ |
| 섬 look_03 상점 벽 10 m | 8.65 | **11.46** | 1,468 | **67** | 695k | ✗ |
| 섬 look_04 R0 들판 원경 | 5.91 | 7.44 | 509 | 35 | 259k | ✓ |
| 섬 look_05 학교 실내 | 8.21 | 9.67 | 1,941 | 59 | 785k | ✓(빠듯) |
| 섬 look_06 주택 문간→밖 | 9.25 | **14.31** | 2,361 | **74** | 839k | ✗(p99가 상한 14 초과) |
| 섬 look_07 바다 수평선 | 2.76 | 2.92 | 8 | 9 | 5k | ✓ |
| World R0 / R1 / R2 | 8.36 / 8.92 / 6.51 | **11.89 / 12.15** / 8.34 | 1,428 / 1,955 / 975 | 54 / **62** / 37 | 556k / 699k / 261k | R0·R1 ✗ |
| World R3 / R4 / R5 | 5.92 / 7.84 / 5.84 | 7.85 / **10.18 / 10.72** | 587 / 1,669 / 744 | 31 / 40 / 33 | 248k / 478k / 279k | R4·R5 ✗ |
| 80인 군중 6~90 m +장비+총 | 7.54 | 8.67 | 1,417 | 19 | 1.01M | ✓ |
| 80인 군중 8~25 m +장비+총(최악) | 7.95 | **10.09** | 1,853 | 20 | **2.06M** | ✗(tri·p99) |
| 대기 섬 게이트(레이스) | 4.54 | 6.27 | 279 | 30 | 431k | ✓ |
| 레이스 피트·그리드 / 직선 / 헤어핀 / 시케인 | 4.85 / 4.06 / 3.83 / 4.42 | 6.14 / 4.72 / 4.27 / 5.71 | ≤386 | ≤36 | ≤445k | ✓ |

**어디에 시간이 드는가** (섬 6구도마다 하나씩 끄고 다시 잼, `IslandLookCaptureTests` `LOOK_COST=1`, 40프레임):
| 끈 것 | 렌더 avg 변화 | 해석 |
|---|---|---|
| 렌더 스케일 1.0→0.75 | −0.1~−0.3 ms | GPU 채움 병목 **아님**(M4 Pro) |
| 포스트 끔 | −0.15~−0.3 ms | 작다 |
| 태양 그림자 끔(이미 1캐스케이드·50 m) | −0.3~−0.7 ms, 드로우 −10% | 중간 |
| 그림자 거리 50→25 m | ±0.2 ms | 효과 없음 |
| LOD bias 2.0→1.0 | −0.1~−1.1 ms(look_06 −1.1) | 문간 구도만 |
| **far clip 700→350 m** | **−1.4~−2.6 ms, 드로우 −40~50%** | **최대 레버** |

결론: 비용은 드로우 수에 비례한다(약 1.7 ms/1,000 드로우 + 고정 약 2.5 ms; 바다 구도 8 드로우가 2.8 ms). 기준선(PERF_BUDGET 1.8 ms/1,000)과 일치한다. 해상도·포스트를 깎아도 소용없고, **멀리 있는 작은 것의 드로우를 줄여야** 한다.

### 1-2. 클라이언트 메인 스레드 (호스트 80인 = 사람 1 + 봇 79, World, 에디터, `MatchGcAllocTests`)
- 프로파일러 끔: **평균 7.97 ms / p99 10.09 ms**, GC **4,734 B/프레임**(p50 1,602, 최대 222 KB, 76회/프레임). 감사 때 40봇 2,790 B였으니 봇 수에 비례해 는다.
- 프로파일러 켬(300프레임, 8.90 ms/프레임, 렌더 제외) 분해:

| 몫 | ms | % | 내역 |
|---|---|---|---|
| 스크립트 Update | 4.25 | 48 | PlayerController 1.07(로컬 1개!), BotController 0.48, LootView 0.37, CombatantAnimation.Update 0.35, CombatantMotion 0.33, CombatantSwim 0.10, FishNet 수신 0.10 |
| 스크립트 LateUpdate | 2.45 | 27 | CombatantAnimation 0.69, **LootMarker 0.58**, Vehicle 0.28, CombatantView 0.08, Hud 0.08 |
| 오디오(AudioManager) | 0.75 | 8 | 80명 × AudioSource 5 |
| 내비메시(AIUpdate) | 0.29 | 3 | |
| 애니메이션(Director) | 0.33 | 4 | |
| 물리 | 0.23 | 3 | Simulate 0.13 |
| UI 캔버스 | 0.12 | 1 | |
| 차량 FixedUpdate | 0.16 | 2 | |

프로파일러는 호출 수만큼 부풀린다. 호출이 많은 LootMarker·LootView(루팅 ~1,600개)는 실제보다 크게 나온다.

### 1-3. 플레이어 빌드 (mac IL2CPP, r5.2 빌드 f6002f2, World, 80명, 2048×1280 창, vsync 끔 = 방법 C)
- 프레임 **평균 7.63 ms / p99 9.20 / 최대 16.1 ms**(131 fps). C 기준(평균 8.3 ms 초과면 티켓) 아래지만 여유는 0.7 ms다. 캡처 구도는 엘리베이터 문이 열린 직후(마을 앞)다.
- **RSS 최고 1.24 GB**(예산 3 GB ✓). VRAM은 잴 수단이 없다. 추정은 빌드 텍스처 626 MB + World 메시 99 MB + 렌더 타깃 약 100 MB = 약 0.8~0.9 GB.
- World 로드 10.55 s(에디터, `[MatchGc]`). 구역 전환 예산(5 s)은 단일 World라 해당 없음. 첫 진입 로딩으로 참고만 한다.

### 1-4. 서버 (mac 서버 빌드 Mono, `--participants 80 --bots 79 --region C`, 사람 0, 독점)
| 시나리오 | 평균 ms(창 중앙) | p99(창 중앙/최대) | 최대 프레임 | 봇 합(bot_ai) | sense / loot / path / doors | RSS 최고 | CPU% |
|---|---|---|---|---|---|---|---|
| P0 파밍 430 s (normal s1) | **6.15** | 8.32 / 10.02 | 20.3 | 0.34 ms | 0.10 / 0.06 / 0.08 / 0.001 | **1.06 GB** | 128 |
| P3 최종 원 군중 240 s | **5.48** | 8.04 / 8.18 | 47.9(워프 순간 1회) | 0.30 ms | 0.05 / 0.02 / 0.11 / 0.001 | 1.05 GB | 128 |

- 봇 AI는 프레임의 **6%**뿐이다. 나머지 약 5.8 ms는 호스트 분해(1-2)로 보면 이동·애니메이션·루팅·내비 엔진·FishNet 몫이다(서버에도 CombatantAnimation·LootView 등이 돈다. 서버 전용 분해는 아직 없음 → #479).
- **측정 조건 함정**: mac 서버 빌드는 Player 서브타깃이라 `UNITY_SERVER`가 없다. 그래서 Fish-Net `ServerManager`가 서버 시작 때 `targetFrameRate=500`으로 덮어써 `DedicatedServer`의 60이 무시되고, 163 fps로 돌며 CPU 1.3코어를 쓴다. 오라클 빌드(Server 서브타깃)는 Fish-Net이 TickRate+15 = **45 fps**로 묶는다(유휴 CPU 2.3% 실측과 일치).
- **오라클 2코어 ARM 환산(추정, 실측 못 함)**: Tailscale SSH가 추가 인증을 요구해 실서버 읽기 전용 샘플을 못 했다(준환 브라우저 승인 필요). Ampere A1 단일 스레드는 M4 Pro P코어의 약 1/3. IL2CPP가 Mono보다 1.3~2배 빠르다고 보면 프레임당 6.15 ms → **약 7~17 ms**(예산 평균 10 ms 경계). 물리·내비 잡이 2코어를 나눠 써서 p99는 더 나쁠 수 있다. → #479.

### 1-5. 네트워크 (호스트 + 원격 클라 6 + 봇 73 = 80인, 관심 영역 켬, 클라들을 봇 곁에 흩뿌림, `InterestBandwidthTests` `INTEREST_BW_BOTS=73 INTEREST_BW_BREAKDOWN=1`)
- 클라당 수신 **9,773 B/s**(관심 영역 끔 14,764). 평균으로 전투원 6.7명, 루팅 48개, 차량 12.8대를 본다. 예산 15 KB/s ✓.
- 서버 송신 종류별(Fish-Net `NetworkTrafficStatistics`를 리플렉션으로 켬, 36 s):

| 종류 | B/s(7연결 합) | % |
|---|---|---|
| ObserversRpc `ObserversUpdateClientAuthoritativeTransform`(NetworkTransform) | 66,071 | **95.7** |
| BulkSpawnOrDespawn(관심 영역 진입·이탈) | 1,610 | 2.3 |
| Broadcast DoorChanged | 639 | 0.9 |
| Broadcast NetPulse | 537 | 0.8 |
| SyncType(Combatant·Gear·Healing 등 합) | ~100 | 0.1 |
| RPC Dent·Timing·PingPong | ~100 | 0.1 |

- 보이는 전투원 1명당 약 **1.4 KB/s**(30 Hz × 약 47 B). 80명으로 환산한 서버 송신은 80 × 9.8 KB/s = **6.25 Mbit/s**(예산 6 Mbit/s, 약간 초과, 추정).
- 문·유리 방송(서버 실측 `[PerfNet]`): P0 1.71회/s, P3 0.34회/s → 클라당 **약 51 B/s / 10 B/s**(#331 게이트 1 KB/s의 5%). 유리 0.
- 경주(#449, `RaceSyncCostTests`, 원격 3): 0대 4,285 → 6대 6,894 → **12대 10,311 B/s/클라**(차 1대 +500 B/s). 호스트 프레임 +2.9 ms(한 프로세스에 클라 3개라 부풀려짐).

### 1-6. 빌드·에셋 (build-mac r5.2 빌드 리포트)
- mac 앱 1.1 GB, win 966 MB, 서버 tar.zst 112 MB(linux-arm64 풀면 약 400 MB). 예산 4 GB ✓.
- 사용자 에셋 874 MB 중 **텍스처 626 MB(72%)**, 기타 183 MB, 메시 36 MB.
- 큰 것: World_Meshes.bytes 99 MB, World_NavMesh 57 MB(빌드 시점. #426 뒤 현재 21 MiB), World_Terrain 34 MB, world_map.png 21 MB, **캐릭터 텍스처 Mixamo 212 MB(2K 5.3 MB × 40장)**, 차량 텍스처 약 110 MB, HDRI 2장 16 MB.

## 2. 병목 순위
| # | 어디 | 예산 대비 | 근거 | 고칠 방법(기대 효과 / 노력) |
|---|---|---|---|---|
| 1 | 클라 렌더: 원거리 드로우(마을·World R0/R1/R4/R5) | p99 최대 14.3 ms = **143%**, SetPass 74 = 123% | §1-1 표·토글. far clip 절반이면 −1.4~2.6 ms | 소품·실내·식생 레이어에 `layerCullDistances`, 지평선은 유지(−1.5~2 ms / S~M) → **#477**. 셀 병합·SetPass는 #452 |
| 2 | 오라클 ARM 서버 불확실 | 추정 70~170% | §1-4. mac 조건이 오라클과 다름 | mac 서버 프레임 상한 45로 맞추기(1줄), 실서버 읽기 전용 샘플(준환 SSH 승인) / S → **#479** |
| 3 | 80인 근거리 군중 삼각형 | tri 137%, p99 101% | roster80 2.06M, 장비 없으면 1.11M | 장비·총 LOD1 거리 앞당김(−0.6M tri / S) → **#478** |
| 4 | 메인 스레드 스크립트(호스트 80인 8.0 ms) | 프레임 예산 16.7의 48%(렌더와 합치면 빠듯) | §1-2 | 아래 5~8로 쪼갬 |
| 5 | 로컬 PlayerController.Update 1.07 ms | 메인의 12% | 인스턴스 1개인데 큼 | 하위 샘플로 원인 확인(S) → **#480** |
| 6 | 루팅 콜백 LootMarker 0.58 + LootView 0.37 ms | 11%(프로파일러 부풀림 포함) | 감사 추정 0.5~1.5 ms를 실측으로 확인 | 숨김 상태 비활성·정적 관리자(−0.5~0.9 ms / M) → **#470** |
| 7 | 원경 포즈 CombatantAnimation 0.69+0.35 ms | 12% | 80명 전원 포즈 | LOD2·화면 밖 생략(−0.5 ms / S~M) → **#471** |
| 8 | 오디오 0.75 ms | 8% | AudioManager.Update | 원거리 소스 정지·가상화(−0.4 ms / S) → **#480** |
| 9 | GC 4.7 KB/프레임(80인) | 상한 1 KB의 **470%** | §1-2 | NetworkTransform 송신 할당 → **#469**, 나머지 #473 |
| 10 | NetworkTransform 95.7% / 서버 송신 6.25 Mbit/s | 클라 65%, 서버 104%(추정) | §1-5 | 전투원 회전을 yaw만(X/Z 스냅), 위치 패킹, 원거리 관찰자 간격 2(−20~40% / S~M) → **#472** |
| 11 | 캐릭터 텍스처 212 MB | 빌드·VRAM | §1-6 | 2K→1K(−160 MB / S, 캡처 확인) → **#481** |

## 3. 로드맵 (순서대로)
**빠른 것(각 ≤1시간)**
1. mac 서버 측정 조건 맞추기: 서버 시작 뒤 `ServerManager.SetFrameRate(45)`. 이후 모든 서버 수치가 오라클과 같은 조건이 된다 — #479
2. 레이어별 컬링 거리 시험(소품 150 m·실내 80 m) + World/섬 재측정 — #477
3. 장비·총 LOD1 거리 앞당기고 roster80 재측정 — #478
4. PlayerController.Update와 AudioManager 원인 샘플링 — #480
5. 전투원 NetworkTransform 회전 X/Z 스냅(yaw만) + 대역폭 재측정 — #472
6. 캐릭터 텍스처 1K 캡처 비교 — #481

**큰 것**
7. 루팅 콜백 정리(LootView 비활성·LootMarker 관리자) — #470
8. 원경·화면 밖 포즈 생략 — #471
9. 실내 셀 병합·SetPass 정리 — #452
10. NetworkTransform GC(패키지 판단) — #469, 작은 CPU 묶음 — #473
11. 오라클 실측(사람 있는 판 읽기 전용 → 필요하면 `--participants` 조정) — #479(준환)
12. 서버 전용 분해: 서버 빌드에도 Profiler 마커 집계(`ServerPerfProbe`에 ProfilerRecorder 몇 개) — #329 후속
13. 파이프라인·중복·죽은 코드 — #474 #475 #476
14. E5 남은 항목: 경주 서버 시간(사람 1 + 봇 7) — #449

이번 세션에 고친 것: 없다. 빠른 수정도 전후 비교 측정이 같은 창에 들어가지 않아 넣지 않았다(측정 도구만 추가).

## 4. 대기 중이던 독점 수치와 이슈 처리
| 항목 | 결과 | 이슈 |
|---|---|---|
| #329 서버 RSS·문/유리 대역폭 | RSS 1.06 GB(≤1.5 ✓), 문 51 B/s·유리 0(≤1 KB/s ✓), 평균 6.15·p99 최대 10.0 ms(✓, 오염 없음 `other_unity=0`), 클라당 9.8 KB/s(✓), 서버 송신 6.25 Mbit/s(추정, 경계) | **#329 닫을 수 있음**(ARM은 #479로 넘김) |
| #331 문·유리 셀 필터 게이트 | 문·유리 51 B/s, 루팅 진입·이탈 230 B/s/클라 → 게이트 미발동 | **#331 닫을 수 있음**(불필요) |
| #377 봇 성능 `bot_stats.sh 430 normal 1` | frame 5.80 / p99 8.31 ms(r5.1 5.9 / 9.9보다 좋음), armed4min 0.975, kills 34, stalled 0.405 | 성능 재판정 통과. 튜닝 AC가 끝났으면 **닫을 수 있음** |
| roster80 | p99 10.09 ms, tri 2.06M → **불합격(근소)** | #478 새로 |
| World_RendersWithinBudget | 독점에서도 R0 11.89·R1 12.15·R4 10.18·R5 10.72 → **실제 초과**(오염 탓 아님) | #477 새로, #452 유지 |
| E5 #449 | 대역폭 12대 10.3 KB/s/클라 ✓, 렌더 A 레이스홀 p99 ≤6.3 ✓ | 경주 서버 시간 남음 → **열어 둠** |

## 5. 재현
측정 도구는 기존 테스트에 env 스위치로만 붙였다(기본 동작은 그대로).
- 클라 렌더: `tools/unity.sh test playmode "Game.Tests.IslandLookCaptureTests.CaptureIslandLook;Game.Tests.WorldSceneTests.World_RendersWithinBudget;Game.Tests.RosterRenderPerfTests.Eighty_Characters_RenderWithinBudget;Game.Tests.RaceTrackPlayTests.RaceHall_RendersWithinBudget"`. 비용 분해는 `LOOK_COST=1`.
- 메인 스레드: `MATCH_GC_BOTS=79 tools/unity.sh test playmode Game.Tests.MatchGcAllocTests`. 시스템별 분해는 `MATCH_GC_BREAKDOWN=time` → `Logs/perf/match_time_breakdown.txt`.
- 대역폭: `INTEREST_BW_BOTS=73 INTEREST_BW_BREAKDOWN=1 tools/unity.sh test playmode Game.Tests.InterestBandwidthTests` → `Logs/net-interest/traffic.txt`.
- 서버: `tools/bot_stats.sh 430 normal 1`, P3는 `--perf p3_x --perf-quick 2`. RSS는 `ps -o rss=` 10초 샘플.
- 플레이어 빌드: `Sublevel -perfcapture out.json -scene World` + `ps` RSS.

**한계**: 렌더 A는 에디터 오프스크린이다(플레이어 빌드는 C 한 번, 다른 해상도 2048×1280). 서버는 mac Mono, 프레임 상한이 오라클과 다르다. 대역폭은 클라 6개를 봇 곁에 둔 한 배치다(실제 판의 밀집 정도에 따라 다름). 플레이어 빌드는 11-20 빌드(r5.2)다. VRAM은 추정이다.

# 봇·네트워크 트랙 DONE (중간 보고, 2026-11-16) — 완성 정의 **미달**

## 한 것 (commit 3개, 컴파일 미확인: Unity 미실행)
- **저지력 서버 배선** `35b91e9`, ADR-240: `Net/Combatant.Stagger.cs`(서버 `StaggerState`, 복제 `SyncVar<Vector3>`+카운터, `StaggerMoveFactorOf/ShakeDegOf`), `Core/StaggerNet.cs`(`StaggerHits.Register`, `StaggerReplica`), `ServerTakeDamage` 훅(Shot만, 사망·넉다운 Clear), 봇 `_agent.speed` 반영. 플레이어 1줄은 소유 밖 → `botsnet_STAGGER_PLAYER_PATCH.md`.
- **50인 파라미터화(T1)** `d9f757f`, ADR-241: `Participants 50·MinBots 10·MaxHumans 40`, 한도 23/24→49/50, `BotFill.Plan`, `--participants`, rules.json·DEDICATED_SERVER 갱신.
- **LagComp 결정화** `8fd0283`, ADR-244: S8 패치 A·B 적용(고정 프레임·손실 0·샷별 모델 로그).

## 확인한 것
- dotnet 하네스(Core만 컴파일·단언): Stagger 등록/무시/복제 미러, BotFill 8케이스, ServerArgs 범위, MatchRules 기본값 — 전부 OK.
- EditMode 테스트 파일 작성: `StaggerNetTests`, `BotFillTests`, `ServerArgsTests` 갱신(24→50, 25→41). **Unity EditMode로는 아직 안 돌림.**

## 못 한 것 / 추정
- Unity 필요: EditMode 전체, `Combatant.Stagger.cs`·`DedicatedServer` 컴파일(구조체 대신 Vector3 SyncVar로 위험 줄임), LagComp 3회 통과, 저지력 PlayMode(서버·클라 복제 오차 0.01).
- 50인 엘리베이터 배정(`Teams.Deploy`) 수용은 미확인.
- 미착수: T2 이름·난이도 혼합, T3~T4 측정 하네스·측정, T5/T6(측정 게이트), T7~T10 봇 오염·열쇠·팀·관심점(S6·S1 API 선행), T11 NavMesh 연결성, T12 통계, T13 PERF 자동화, LagComp 손실 2% 통계 테스트(HIT_FLOOR는 측정 후).
- ADR 242·243 미사용.

## 갱신 (Unity 순번 후)
- **EditMode 전체 1242/1242 통과**(기존 1221 + 신규 21), 컴파일 오류 0. PlayerController 저지력 1줄 적용(master 승인, 1줄).
- **LagCompPlayTests는 아직 안정 아님.** 손실 0 + 고정 프레임(captureDeltaTime) 적용: 2건 모두 3회 실패, 사본 지연 거리 0.16~1.98 m로 오히려 흔들림 → 고정 프레임 제거. 손실 0만: 100ms 테스트 6/6 통과 1회, 이후 속도 게이트를 넣은 3회는 4/6·6/6·5/6(통과 1/3). 35m 테스트는 3/5·1/5·3/5로 실패(임계 shots-1). 샷 로그상 되감기 모델 200 ms vs 필요 ≈ 0.8 m/3.2 m/s ≈ 250 ms(상행 약 50 ms 포함하면 모델 범위 안) → **시간 모델 결함의 증거는 없고 무기 확산(Rifle)·표적 가속 변동이 유력(추정)**. 다음 후보: 확산 없는 무기로 정확도 테스트를 바꾸거나 표본을 키워 통계 임계로 전환(별도 결정). 속도 게이트는 효과 없어 뺐다.
- 미착수 항목은 위와 동일.

## LagComp 원인 분리 (master 지시 (1)~(3), 확산 0 + 실전 무기 통계 테스트 추가 후 3회씩)
| 조건 | 100 ms 정확도(shots 6, 확산 0) 보정/무보정 | 35 m(5발, 확산 0) | 실전 무기 20발 보정/무보정 |
|---|---|---|---|
| 실행 1 | 5/6, 1/6 | 3/5 | 20/20, 3/20 |
| 실행 2 | 6/6, 0/6 | 3/5 | 3/20, 0/20 |
| 실행 3 | 6/6, 0/6 | 3/5 | 13/20, 0/20 |
- **확산은 원인이 아니다(확인):** 확산 콘 반경 0.9° = 9 m에서 0.14 m < 몸 반폭 0.3 m라 이론상 확산 단독으로는 안 빗나가고, 실전 무기가 20/20도 나왔다.
- **실행마다 크게 갈린다(20/20 ↔ 3/20, 같은 코드·같은 무기):** 샷 단위 잡음이 아니라 실행 단위 지연 오프셋(사본 지연 0.8~1.0 m, 프레임 고정 시 0.16~1.98 m)이 원인. 다른 Unity가 동시에 돌던 때라 **프레임 요동·CPU 경합이 유력(추정, 단독 실행으로 분리 필요)**. 되감기 모델 167~200 ms vs 필요 ≈250 ms로 여유(표적 반폭 0.3 m = 약 90 ms) 대부분이 소진돼 있어 작은 요동에도 빗나감.
- **35 m 3/5 고정(3회 모두):** 확산 0·실행 무관하게 일정 → **비행 시간(약 0.04 s×3.2 m/s≈0.13 m) 보정 누락 등 제품 쪽 체계 오차 의심(추정)**. 제품 코드(`LagCompensation` 비행 탄 판정) 조사 티켓 필요(소유: Net, 이 트랙).
- 통계 테스트는 임계 숫자 대신 "보정이 무보정보다 5발 초과"로만 단언(실행 2·3에서 보정 3/20·13/20이라 이 단언도 실행 2에서 실패 가능). 임계(최저값−1)는 **단독 실행 5회 측정 뒤** 기입. 완료(3회 연속 통과) **미달**.

## 35 m 3/5 — 코드 읽기 결과(순수 C# 재현은 못 함)
- `ServerLaunch`는 `RewindAt = 발사 시각 − 되감기`로 **고정**하고, 날아가는 탄은 이후 구간도 같은 `RewindAt`로 판정한다(표적을 발사 순간 시청한 위치에 고정). 조준한 사본 위치로 탄이 가면 맞는 설계라 **비행 시간 보정 누락은 아니다(코드로 확인)**.
- 35 m 미스 로그: 탄이 표적 라인에서 옆으로 약 0.8 m 벗어나 끝남. 100 ms 테스트의 사본 지연(0.8~1.0 m ≈ 250~310 ms)과 같은 크기이고, 모델 되감기는 167~233 ms에 **캡 0.25 s(`MaxRewindSeconds`)**가 걸린다 → 필요(≥250 ms)가 모델·캡보다 커서 오차 여유(반폭 0.3 m ≈ 90 ms)를 소진. 즉 **사본이 `InterpolationTicks=2`(66 ms)보다 더 늦게 보이는 것(NetworkTransform 평활화 추정)과 캡 0.25 s**가 후보. 수정(보간 틱 2→3 또는 캡 상향)은 프리팹 설정·공정성 한계를 바꾸므로 **Unity 독점 창에서 사본 지연을 재측정한 뒤** 결정(ADR-244 갱신 예정). 미확인.
- T3 `ServerPerfProbe`(ADR-242) 코드 작성 완료·미실행. 서버 빌드·P0 실행은 순번 필요.

## 독점 창 결과 (2026-11-16)
- EditMode **1245/1245**(컴파일 오류 0). 내가 기존 `Core/FrameStats.cs`·테스트를 덮어쓴 실수를 발견·복구하고 내 클래스를 `ServerFrameStats`로 개명(커밋 포함). 그 사이 PlayMode 1차 시도는 컴파일 오류 상태였음.
- **LagCompPlayTests 5회(단독, 다른 Unity 0개): 3회 연속 통과 못 함.** 정확도 100 ms(확산 0): 6/6·6/6·6/6·6/6·5/6 → 4/5회 통과. 35 m: 3/5·1/5·1/5·3/5·1/5 → 매번 실패. 실전 무기 통계: 20/20·19/20·0/20·13/20·0/20.
- **원인 분리:** 동시 Unity 경합이 아니다(독점에서도 같은 분산). 0/20 실행의 샷 로그는 탄이 표적(z≈−1)이 아닌 z=−14.5(뒷벽)에 끝남 = **조준 방향이 표적과 무관하게 틀어짐**(사본 지연은 0.75~1.03 m로 정상) → 되감기와 무관한 **테스트 하네스/사격자 위치·방향 레이스(사격자 서버 위치 동기 전 발사 추정)**. 35 m 1/5·3/5도 같은 부류일 수 있음(미확인). 보간 vs 캡 분리 수치: 사본 지연 0.75~1.03 m(≈235~320 ms) vs 모델 167~233 ms·캡 250 ms — 보간 2틱(66 ms)으로는 설명 안 되는 +70~90 ms 잔차(평활화 추정), 캡은 235 ms 이상일 때만 관여.
- **서버 빌드 성공**(`Build/server/SublevelServer.app`), 그러나 **P0 측정 실패:** 사람 0명이면 `LobbyNet.StartMatch`가 시작하지 않아 봇만의 매치를 띄울 수 없다(`--perf` 자동 시작 시도 → 시작 안 됨, 변경 되돌림). 다음 단계: `LobbyNet`에 사람 0명 시작 허용(측정 전용) 또는 헤드리스 클라 1명(P1).
- 정리: 서버 프로세스·Unity 프로세스 없음, git restore·메타 복원 완료.

- 측정 중 다른 부하: master 통보로 main의 `Build/mac/Sublevel.app`(CPU 약 92%, 22분+)이 돌고 있었을 수 있음(PID·CPU를 측정 전후로 기록하지 못함). 위 LagComp 분산의 원인 후보에서 **배제하지 못한다**(독점 창이 실제로는 독점이 아니었음). 재측정 때 `ps -axo pid,pcpu,command`를 전후 기록.

## 후속 (코드만, Unity 미실행)
- `--perf`에서만 사람 0명 강제 시작 허용(`LobbyRules.MayForceStart`, `LobbyNet.AllowEmptyStart`, DedicatedServer가 `--perf`일 때 5초 뒤 시작). 프로덕션 경로 불변은 `ForcedStart_NeedsSomeone_UnlessMeasuring` 테스트로 보장. 시작 뒤 `Players=0` 매치가 정상 진행하는지는 Unity 확인 전.
- LagComp 하네스: 사격자가 서버에 반영될 때까지 대기 + 사격 전 "서버가 본 사격자 눈 위치 == 클라 눈 위치(<0.3 m)" 어서션 추가(통과 기준은 안 내림).
- **제안만(ADR 필요):** 사본 지연 +70~90 ms 잔차는 보간 2틱(66 ms) 위에 NetworkTransform 평활화가 얹힌 것으로 추정되고, 필요 지연이 235~320 ms로 캡 0.25 s를 넘는 샷이 있어 `Ballistics.MaxRewindSeconds` 0.25→0.30 s를 제안한다. 근거: 측정 사본 지연 최대 ≈320 ms(1.03 m / 3.2 m/s), 상행 50 ms 제외 시 ≈270 ms. 공정성 비용: 피격자가 최대 0.30 s 전 위치에서 맞는다. 룰 변경이라 승인 전까지 적용 안 함.

## 독점 창 2차 (UI 코드 작업 중 일부 겹침) — 2026-11-16
- EditMode **1246/1246**, 컴파일 오류 0.
- **LagComp 5회** (로그 `Logs/lagcomp-run{1..5}.log`, 저장소 밖). 다른 Unity 관측: run1 시작 때 다른 Unity PID 72781(CPU 79%), run3~5는 PID 73617(CPU 99~192%)가 겹침 → **run1·3·4·5 오염, run2만 깨끗**.
| 회 | 100 ms 정확도(확산 0) | 35 m | 실전 무기 20발(보정/무보정) |
|---|---|---|---|
| 1(오염) | 6/6 통과 | 3/5 실패 | 14/20, 0/20 |
| 2(깨끗) | 6/6 통과 | 3/5 실패 | 7/20, 3/20 (통계 단언 실패) |
| 3(오염) | 6/6 통과 | 1/5 실패 | 20/20, 2/20 |
| 4(오염) | 6/6 통과 | 1/5 실패 | 20/20, 1/20 |
| 5(오염) | 5/6 통과 | 1/5 실패 | 20/20, 0/20 |
- 사격자 동기 대기·어서션 추가 후 **0/20(뒷벽) 실행은 사라짐 → 하네스 레이스 가설 확인.** 100 ms 정확도 테스트는 5/5 통과(3회 연속 통과 달성). **35 m와 통계 테스트는 여전히 불통과**: 35 m는 깨끗한 회(run2)에서도 3/5 → 부하가 아니라 제품 쪽(사본 지연 > 모델/캡 0.25 s 후보, 위 제안). 통계 테스트 run2의 7/20은 깨끗한 회에서도 낮아 임계 설계 전 재측정 필요. 완료(전부 3회 연속 통과)는 **미달**.
- **P0 서버 측정 성공**(`--perf p0 --participants 50 --bots 49`, 사람 0, 현재 World, 서버 빌드 `Build/server/SublevelServer.app`, 로그 `Logs/server-p0.log`·`Logs/perf/s8_server_p0.csv`). 10초 창 15개: 프레임 평균 2.05~3.97 ms, p99 3.9~8.0 ms, 최대 6~20 ms(첫 창 358 ms = 로딩), 힙 27~30 MB, 전투원 49, 서버 CPU 약 106%. 예산(평균 ≤10 ms·p99 ≤25 ms) 안 → **봇 LOD 게이트(봇 >4 ms) 발동 안 함(이 조건에서)**. 단서: 배치 모드는 프레임 상한이 없어 프레임 간격=작업 시간으로 읽음(추정); 사람 0·승강기 대기 위주라 교전·결승 밀집(P3)은 미측정; 프로세스 총 메모리·대역폭·LagComp 되감기(0회)는 못 봄; match는 150 s에 "아무도 안 옴"으로 종료(`NobodyCameSeconds`).
- 정리: 서버·Unity 프로세스 남지 않음.

## 통계 테스트 임계 설계 (미적용, `[Category("Stat"), Explicit]`로 기본 실행에서 제외)
- 표본 N=20발(한 번의 듀얼). 맞을 확률 p는 (a) 확산(콘 반경 θ=SpreadDeg×0.5=0.9°, 9 m → r=9·tan θ=0.14 m)이 몸 반폭 w=0.3 m 안에 드는 비율 = 1(r<w), (b) 시간 모델 오차 e(= 필요 지연−모델, 실측 +70~90 ms)가 표적 이동 v=3.2 m/s에서 만드는 횡오차 d=e·v=0.22~0.29 m가 w 안에 드는 비율. 즉 이론 p는 e의 분포에 달려 있고 실측 e 분산이 크다(7/20~20/20) → **먼저 e의 분포(샷별 로그 `copy behind real`)를 깨끗한 환경 10회에서 모은 뒤** p̂를 정한다.
- 임계: p̂는 깨끗한 회의 명중률 평균, 단언은 Wilson 하한 L = (p̂ + z²/2N − z√(p̂(1−p̂)/N + z²/4N²))/(1+z²/N), z=2.33(99%)로 **hits ≥ floor(N·L)**. 값은 측정 뒤 기입(지금 숫자를 정하지 않음). 무보정 대조군은 같은 방식의 상한으로 "보정 하한 > 무보정 상한" 비교.

## P3 결승 밀집 시나리오 (코드 작성·미실행)
`--perf p3 --perf-gather X,Z --participants 50 --bots 49`: 매치 시작 20초 뒤 모든 봇을 NavMesh 위 (X,Z) 반경 60 m 안 점으로 Warp(시드 고정) → 봇끼리 교전·`ClosedBetween`·LagComp·관찰자 조건이 한곳에 몰리는 최악 조건. 같은 `[Perf]` CSV(`s8_server_p3.csv`)에 프레임·힙·되감기 수가 남는다. 대역폭은 아직 못 잼(Fish-Net 송신 바이트 훅은 후속). X,Z는 최종 지역 중심으로 맵 데이터에서 골라야 함(현재 World의 지역 C 마을 좌표 확인 필요, 미정). Unity 순번 필요.

## 캡 0.30 s 적용 후 (ADR-243), 35 m 5회 + P3 — 2026-11-16
- EditMode **1247/1247**. 35 m 테스트 5회: **3/5 ×5회(전부 실패)**. 깨끗한 회는 run1뿐(run2~5는 다른 Unity PID 77518·77643·78195·78538 겹침 = 오염, 로그 `Logs/lagcomp30-run{1..5}.log`). 100 ms 정확도는 6/6 ×5.
- **캡은 35 m 실패의 원인이 아니다(깨끗한 run1도 3/5).** 결정적 단서: 35 m 미스 2건이 매번 **같은 지점에서 끝남 — `World at (4.00, 1.07, 23.7x)`**(표적은 x=8, 사격자는 x=−27): 표적 4 m 앞 x=4.00, 높이 1.07 m의 **고정 지형/장애물에 탄이 막힘** → 표적 z가 장애물 틈 밖(≈25.0~25.2)일 때 막힘. 되감기·지연이 아니라 **테스트 지오메트리(TestBox 사선 중간 장애물)** 문제로 보인다(추정; Unity 씬 확인 전). 다음: 사선 위 x=4 근처 오브젝트를 씬에서 찾아 표적 레인을 옮기거나 사격 위치를 바꿔 장애물을 피한다. ADR-243의 캡 변경은 100 ms 정확도에 해롭지 않았으나 35 m 근거는 약해졌으므로 **되돌릴지 master 판단**(근거가 '35 m 개선'이었음).
- **P3 실패(코드 문제 2건):** 서버는 정상(프레임 평균 2.0~3.5 ms, p99 ≤8 ms, 힙 ≈25 MB, 로그 `Logs/server-p3.log`·`Logs/perf/s8_server_p3.csv`)이지만 `ZoneDirector.FinalRegion = −1`(최종 지역이 정해지지 않음 → 허브 (0,0)로 대체)이고 **Warp된 봇 0명**(허브 (0,0) 반경 60 m에 NavMesh 점이 없거나 에이전트 조건 불일치 추정). 즉 P3 밀집은 **미측정**. 다음: 지역 허브 좌표를 월드 데이터에서 직접 읽거나 `--perf-gather`로 NavMesh 위 좌표를 지정, `--region` 인자로 최종 지역 지정.
- 정리: 서버 프로세스 없음, git restore 완료.

## 후속 코드 (master 결정 반영, Unity 미실행)
- **캡 0.25 s 복구**(`Ballistics.cs`·`LagComp.cs`), ADR-243 상태 "보류"로 수정(삭제 안 함). EditMode 기대값은 상수 참조라 변경 없음.
- **35 m 장애물 확인:** `TestBoxBuilder`의 `covers[10]=(-5,0,24)`, `covers[11]=(5,0,24)`(2×1.2×1 m 판)이 사선과 만남(Cover11은 x 4~6, z 23.5~24.5, 높이 1.2 → 탄이 (4.00, 1.07)에서 막힘). 35 m 테스트가 시작할 때 `Cover10`·`Cover11`을 끈다(기준 수치 불변). 5회 재측정은 독점 창 필요.
- **P3 수정:** 최종 지역 미정(−1)이면 지역 0 허브(또는 `--region N`으로 강제한 지역), NavMesh 샘플 반경 200 m, Warp 수/에이전트 없음/NavMesh 없음을 로그하고 **90% 미만이면 `LogError`로 시나리오 무효 표시**.

## 독점 창 3차 (다른 Unity 0개 확인 후 시작) — 2026-11-16
- EditMode **1247/1247**.
- **35 m(캡 0.25, Cover10·11 비활성) 5회**: 1/5(실패)·5/5·5/5·5/5·5/5 → **run2~5 4회 연속 통과**(3회 연속 달성). run1은 첫 실행이 탄 4발이 표적 옆(0.4 m)으로 빗나감(실행 직후 첫 PlayMode, 원인 미확인). 로그 `Logs/lagcomp25b-run{1..5}.log`(저장소 밖). 시작 전 `ps` Unity 0개, 실행 중 다른 Unity 관측 없음(깨끗). → 장애물이 원인이라는 판단 **확인**, 캡 상향 불필요(ADR-243 보류 유지).
- **P3 서버 측정 성공**(`--region C --perf p3 --participants 50 --bots 49`, 로그 `Logs/server-p3c.log`, `Logs/perf/s8_server_p3.csv`): 봇이 에이전트를 달 때까지(≈60 s) 기다린 뒤 **49/49 봇을 허브 (520, 800) 반경 60 m로 Warp**(오류 0). 밀집 후 10초 창 프레임 평균 2.1~2.8 ms, p99 4.1~6.0 ms, 최대 38 ms, 힙 ≈28 MB → 예산(10/25 ms) 안. **단서:** 이전 P0·P3 1차 값(약 20~60 s 구간)은 봇이 승강기 안(에이전트 없음)이라 **봇 AI를 실제로 돌린 측정이 아니었다**; LagComp 되감기 수 0(봇 사격은 되감기 없음, 사람이 없어서), 대역폭·교전량(총성 수)은 미측정 → 봇 몫 ≤4 ms 게이트 판정은 **밀집 후 구간만 유효**하고 교전 지표(만남/분·총성 수) 추가 전에는 "예산 안(교전 미확인)"으로만 기록.
- 정리: 서버·Unity 프로세스 없음.

## T2·T7 (2026-11-17): 이름·난이도 혼합·오염 경고 반응 — `git merge track/pollution`(fast-forward) 후
- 구현: `Core/BotNames`·`BotMix`·`BotPollutionPlan`(순수) + `BotController`(봇별 난이도, 이름, `TryWarnedLeaveTarget`) + `MatchDirector` 이름 한 곳. ADR-245.
- 확인: EditMode 1342건 중 **내 `BotBehaviourTests` 전부 통과**(이름 49개 유일, 혼합 비율 ±2%p, 경고 반응 단조: 어려움 30 s 100%, 쉬움 30 s 50~80%(이론 67%), 확정 40 s 전 100%). **실패 14건은 전부 `CharacterAnimatorTests`·`CharacterRosterTests`**: 이 워크트리에 로스터 FBX가 없어서(캐릭터 트랙 자산, `Roster_*.fbx.meta`가 삭제 상태로 나옴) — 내 변경과 무관(미확인: main에서도 같은 결과인지).
- 수치(판정): 경고 105 s 중 **확정 45 s 전(경고 60 s 시점)에 이동을 시작한 비율** = 어려움 100%, 보통 100%(0~25 s 지연), 쉬움 100%(0~45 s 지연, 45<60) — 시뮬이 아니라 지연 분포 계산값. 실제 PlayMode 시나리오에서의 비율은 **미측정**.
- **못 한 것:** T8 열쇠(줍기·삽입·탑승·목적지: `KeyElevatorNet.ServerInsert/Select`, `ElevatorRides.StationAt`, `TeamKey` public 필요), T9 팀 구성·칩, T10 도시 관심 지점, 최종 지역 인지(`KnowsFinal`)를 `ToFinalRegion`에 연결, 방독 사용, PlayMode 검증, `BotStats.escapeStalls`. 오염 쪽 요청: `KeyElevatorNet.TeamKey` public.

## T8·최종 지역 인지 (2026-11-17, `git merge main` 후) — ADR-246
- 구현(commit): 봇 열쇠 흐름 `TryKeyElevator`(경고·전선 임박 + 열쇠 보유 + 난이도별 사용 확률 0.5/0.8/1.0 → 자기 지역 승강장으로 이동 → `ServerInsert` → 6초 동승 대기 → `ServerClose` → 선택 단계에 `BotDestination(from, states, 알려진 최종 지역)` → `ServerSelect`), 최종 지역은 지도로 알 수 있을 때만(경고·오염 지역 ≥ ceil(2/3×지역 수)) `ToFinalRegion`에 사용, `KeyElevatorNet.TeamKey` public(오염 트랙 파일 1단어, master 승인), `ElevatorRides.StationSpawn` 접근자 추가. 열쇠 줍기는 기존 `KeysWanted=1` 유지. `[BotStat] warn-leave|key-insert|key-ride` 로그와 정적 카운터(`KeyInsertOk/Fail/KeyRides/WarnedLeaveStarts`), 서버 인자 `--perf-quick N`(오염 표 가속)·`--perf`는 판정 전까지 매치 유지.
- 확인: EditMode 1345건 중 내 테스트(열쇠 사용 확률 3건 포함) 통과, 실패 14건은 전과 같은 Character 테스트(FBX 없음).
- **막힘: 서버 빌드 실패** — `AssetValidator`: `LICENSE.txt missing: Assets/_Project/Art/Characters/Ubc; …/Art/Weapons/Roster` (main 병합분의 에셋 폴더에 LICENSE.txt가 없음, 내 변경과 무관). 라이선스 가드라 우회·더미 파일 생성은 안 함. → 실측(경고 이동 시작 비율, 열쇠 성공률, 수렴 시간)은 **미측정**.

## 실측 결과와 마무리 (2026-11-17) — 있는 그대로
서버 빌드(`Build/server`), `--participants 50 --bots 49 --region C --perf keys --perf-quick 2`(오염 표 2배속: 경고 52.5 s, 확정 2·3.75·5.25·6.5·7.5·8.25분). 이 구간 내내 다른 Unity 2개가 동시에 돌고 있었음(CPU 100%×2, 오염). 로그 `Logs/server-keys{,2,3}.log`(저장소 밖).
- **경고 이동 시작(`[BotStat] warn-leave`)**: 3회 실행에서 22·6·19건. 확정까지 남은 시간 최소 40.0 s(1·2회)·34.0 s(3회) → **기록된 모든 봇이 확정 ≥34 s 전에 이동 시작(100%)**, 난이도별 건수 쉬움 9/1/6·보통 10/4/10·어려움 3/1/3. 쉬움·보통이 대부분 `since=12.5 s`(= 경고 52.5 − 최소 이동 40 s 강제 시점)에 몰림 = 지연 분포 계산값(쉬움 0~45 s는 강제 시점에 잘림)과 방향 일치. **한계:** 경고 지역에 있던 봇 전체 대비 비율(분모)은 못 셈 — 이동 시작 로그만 있어 경고 지역 봇 수를 같이 기록하지 못했다(그리고 매치가 빨리 결정되어 건수가 적음).
- **열쇠 사용 성공률: 못 잼.** 모든 봇에 열쇠 1개를 준 실행(`--perf-keys`, "gave a key to 49 bots")에서도 `key-insert`·`key-ride` 0건, 자격 로그(`key-eligible`)도 0건 → 흐름이 그 이전 관문에서 막힘. 원인(`KeyElevatorNet`/`ElevatorRides.Running`/지역 상태 중 무엇)은 **미확인**: 진단 로그(`key-gate`)를 넣었으나 그 빌드·실행은 중단되어 결과 없음. 흐름 자체의 EditMode 확률 테스트만 통과(사용 확률 0.5/0.8/1.0).
- **최종 지역 수렴 시간: 못 잼**(위와 같은 이유로 판 끝까지의 이동 지표를 남기지 않음).
- **미구현(BACKLOG):** T9 팀 구성·칩 임무, 방독 사용, T10 도시 관심 지점, T4 이후 측정 자동화(T13), T5/T6(측정 게이트 미발동), T11 NavMesh 연결성, T12 통계 시나리오, LagComp 통계 테스트 임계. 열쇠 흐름의 막힘 원인 조사가 다음 첫 일.

## 매칭 흐름 1차 (2026-11-17) — ADR-247~249
- 구현: `Core/MatchQueue.cs`(`MatchQueue`·`QueueParty`·`MatchComposer`·`MatchPlan`·`MatchQueueState`), `MatchRules.QueueWindowSeconds`(rules.json), `LobbyState.Queue`·`LobbyNet.Queue/ServerSetQueue`(복제 필드), 계약 `docs/tracks/botsnet_QUEUE_API.md`, EditMode `MatchQueueTests`(사람 1/11/40/60 × 솔로/스쿼드 표, 파티 1~4, 창 만료·조기 시작, 퇴장, 상태).
- **미구현:** DedicatedServer가 대기열로 판을 시작하는 연결과 시작 요청(모드·파티 크기) 전송 — 현재 로비 준비/타이머 흐름이 그대로다. PlayMode 없음.

## 매칭 연결 (2026-11-17) — ADR-258
- 연결: 클라 `MatchmakingClient.RequestStart/Cancel/State/StateChanged` → `LobbyQueueRequest/Cancel` → 전용 서버 `LobbyNet`(대기열 등록, 0.5 s마다 `LobbyState.Queue`, 창 만료·가득 시 `MatchComposer`로 요청자에게만 `LobbyStart{Players, Bots, Mode}`), `DedicatedServer`는 대기열이 있으면 준비/타이머 시작 보류. 싱글·호스트 메뉴 시작: `MatchRules.FillToParticipants`(rules.json 켜짐)로 사람+봇 50. `MaxPlayers` 16→40.
- **확인(수치):** EditMode 1365건 중 1351 통과(실패 14 = Character FBX 부재); **PlayMode `MenuMatchFillPlayTests`(오디오 끔) 통과: 솔로 시작 → 전투원 50 = 사람 1 + 봇 49**. 대기열↔전용 서버 왕복(요청→창→시작)은 **PlayMode로 못 확인**(서버 빌드+클라 시나리오 필요) — 구성 규칙·창 로직만 EditMode로 확인.
- 한계: 파티원 묶기(파티장만 대기열), 모드 혼합 동시 대기, 50인 엘리베이터 수용(PlayMode에서 50명이 엘리베이터에 배치돼 완료는 확인, 시작 후 진행은 미확인).

## 80인 전환 (2026-11-17) — ADR-259
- **규칙:** `MaxParticipantsLimit 80`, `Participants 80`, 최소 봇 16, 사람 최대 64, rules.json `MaxBots 79·MaxPlayers 64`, SPEC §2 갱신. 테스트(사람 1/11/64/90 × 솔로/스쿼드 구성 표, BotFill, ServerArgs, 대기열) EditMode **1378건 중 1364 통과, 컴파일 0**(실패 14 = Character FBX 부재). 스쿼드 팀 수: 사람 1명 21팀(1+봇 20), 11명 29팀, 64명 68팀(봇 4인 균등).
- **투입 슬롯(60대 < 80명):** `Deployment.Assign`가 자유 승강기가 없으면 가장 덜 찬 승강기를 공유하므로 60대 80명 → 20대가 2명씩. EditMode로 확인(최대 2명/대, 베이크 베이 9칸·팀 4 이하) → **승강기·맵 변경 불필요**. 실제 World에서 80명 배치는 서버 실측에서 79봇이 모두 시작·Warp까지 진행(오류 없음)으로만 확인, 승강기 위치 겹침 캡처는 안 함. 맵 트랙 요구사항 없음.
- **서버 실측 80인**(`--participants 80 --bots 79 --perf p3 --perf-quick 2 --perf-keys --region C`, 사람 0, `Logs/server-p80.log`): 봇 79/79 Warp 성공, 79 전투원 창 51개 중 밀집 후 44개: **프레임 평균 2.1~6.0 ms(중앙 ≈2.7), p99 4.0~10.4 ms(한 창만 67.5 ms), 최대 1.78 s(한 번, 로딩 직후 구간)**, 힙 34~40 MB. 예산(평균 ≤10 ms·p99 ≤25 ms) **평균은 통과, p99는 한 창 초과**. **측정 오염 큼:** 실행 내내 다른 Unity 3~6개(CPU 80~300%)가 겹쳤고 소켓 `Address already in use`(다른 서버와 포트 충돌) 로그가 있어 p99 초과 창은 부하 때문일 가능성이 높지만(미확인) 판정은 "평균 통과, p99 재측정 필요". 이전 50인 P3(평균 2.1~2.8 ms, p99 4.1~6.0 ms, 힙 ≈28 MB) 대비 평균은 비슷, 힙 +6~12 MB.
- **봇 AI LOD(S8-A6) 게이트:** 서버 프레임 전체 평균이 ≈2.7 ms라 봇 몫 ≤4 ms 게이트는 **발동 안 함**(80인, 교전 없음 조건). **문·유리 관심영역(S8-A7) 게이트:** 클라이언트가 없어 대역폭(문·유리 방송 KB/s)은 **못 잼** → 구현 불필요 판정은 보류. 교전량(총성)·열쇠 사용(여전히 0건)은 못 잼.

## 한 승강기 한 팀 + 열쇠 삽입 수정 (2026-11-17) — ADR-260
- **슬롯:** `Deployment.Assign(…, share=false)`/`Teams.Deploy`는 낯선 사람끼리 승강기를 공유하지 않는다(솔로 1인 1기, 스쿼드는 팀 단위로 한 기). 승강기보다 많으면 남는 쪽은 `-1`이고, `MatchDirector.BotCount`는 `승강기 수×팀 크기 − 사람`으로 봇을 제한하며 서버 로그에 경고(`[MatchDirector] N bots wanted but the M elevators hold K … capped`). EditMode: 솔로 80명/80기 → 전원 1인 1기, 80명/60기 → 20명 무배정·1기 1명 이하; 스쿼드 80명/60기 → 20팀이 20기, 한 기에 한 팀. 기존 `Deploy_Solo_IsThePlainAssignment`는 공유 없는 배정과 비교하도록 갱신(옛 배정은 16명 솔로도 지역 상한 때문에 일부 공유했음).
- **현재 World의 승강기는 13기뿐**: PlayMode `MenuMatchFillPlayTests` 결과 **솔로 시작 = 사람 1 + 봇 12 = 13명**(통과; 기대값을 승강기 수로 바꿈). 즉 **맵이 승강장당 4기(20×4=80)를 내놓기 전에는 판이 80명이 아니라 승강기 수로 줄어든다**(옛 방식은 공유로 50명을 채웠음). 맵 병합 전까지 이 상태를 감수해야 함.
- **열쇠 삽입 수정 확인:** 원인 = `ElevatorRides.Running`(클라 스케줄)이 전용 서버에서 항상 false. 봇은 `ServerRunning`(`ServerScheduled && !ServerDoorsClosed`)을 쓴다. 서버 실측(`--participants 40 --perf keys --perf-quick 2 --perf-keys`, 로그 `Logs/server-keys6.log`): `key-eligible` 3, **`key-insert` 2, `key-ride` 2(삽입 성공 2/2 → 탑승·목적지 선택 2/2), 삽입 실패 0**, 경고 이동 1건(이 판은 승강기 13기·봇 12로 줄어 표본이 작음). 열쇠 사용 성공률은 시도(자격 있음 3 중 삽입 2 = 67%)로만 말할 수 있고 표본이 작아 통계는 아님(1건은 시간 부족 추정, 미확인).

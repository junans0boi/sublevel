# 마스터 인수인계 3 (2026-11-19 11:20 기준)

> 이전 마스터(Claude, 계정 A)가 **다른 계정으로 이동**해서 이 마스터와 서브 세션들을 더 쓸 수 없다. 새 마스터는 이 문서와 `docs/MASTER_HANDOFF_2.md`(규칙·인프라·검증 레시피는 거기가 더 자세함)를 읽고 **서브 세션을 새로 시작**해야 한다(이전 세션에는 메시지를 못 보낸다 — 아래 §7의 브리프로 재시작).
> 읽는 순서: `CLAUDE.md` → `docs/MASTER_HANDOFF.md`(1차: 역할·준환 성향) → `docs/MASTER_HANDOFF_2.md`(규칙·인프라) → **이 문서** → `docs/MASTER_STATE.md`, `docs/PROGRESS.md` → `docs/ISSUE_RECONCILE_2026-11-18.md`.
> 비밀 값은 어디에도 없다. `/Volumes/WorkSpace/Personal/Key/`는 읽거나 출력하지 않는다.

## 0. 준환과 일하는 법 (이번 세션에서 확인된 것)
- 한국어, 간결·직접. 사소한 건 묻지 말고 결정, 되돌릴 수 없고 양쪽 다 합리적인 것만 묻는다. 질문은 **"예/아니오 한 줄"**로 끝나게.
- **"세션이 보고함"은 증거가 아니다.** 캡처는 파일 시각까지 확인하고 직접 연다(이번에 옛 캡처를 새것으로 보내 신뢰를 잃었다). 테스트 수치·커밋 해시·공개 URL 응답을 근거로 보고한다.
- 불만 포인트: 진행이 안 보이는 것("진행 안 되는 것 같다"), 일괄 처리의 오판(과거 이슈 일괄 `not_planned`는 틀렸다 — 구현됐는데 열려 있는 것을 `completed`로 닫아야 한다), Unity를 하나씩만 쓰는 답답함, 게임 룰 변경을 묻지 않고 하는 것.
- 위임된 범위: 사이트·런처·백엔드 저장소 커밋·푸시·배포, 워크트리 브랜치 커밋, 통합 브랜치를 main에 병합·푸시(LFS 마이그레이션 포함). 그 밖의 대량 푸시·폴더 삭제·유료·계정 생성은 한 줄로 묻는다.
- **commit/push가 서브 세션에서 권한 분류기에 자주 막힌다.** 위임 범위 안이면 마스터가 직접 한다.

## 1. 현재 스냅샷 (2026-11-19 11:16 확인)
- **main = origin/main = `663a3ce`**(푸시됨). 통합 r4(1인 스쿼드 실검증·차량 카메라·자동 달리기=스프린트·열쇠 삽입 입력·저지력 흔들림·파티 매핑 수정·친구/파티 패널·동기화 보고서)까지 포함. 검증: 컴파일 0, EditMode 1545/1545, PlayMode 34/34.
- 열린 이슈 **100개**(오늘 오전 143개). 대기섬 13개는 구현 진행 중.
- Unity 프로세스: 봇 측정 서버 1개(11:04 시작, 약 11:50 종료). 다른 Unity 컴파일을 돌리면 측정이 오염된다.

### 배포된 것 (공개 상태)
| 구성 | 상태 |
|---|---|
| 게임 서버(오라클 100.97.58.29, Tailscale UDP 7770) | r3 가동(프로토콜 2 = 버전 `1.0/2`, 옵션 `--dedicated --port 7770 --start-seconds 60`, 80인 규칙 기본값). 롤백용 r2 보관 |
| 게임 클라이언트 릴리스 | `r3-3c6ff5a`(Mac+Windows 한 버전 폴더, 서버 업로드·sha256 재검증 완료). 런처에서 업데이트하면 받음 |
| 런처 | 0.1.7 Mac·Windows 서명 배포(층 타임라인 디자인, 자동 업데이트, 게임 실행 후 런처 종료, 게임 토큰 game-token 우선). Mac은 Chrome 다운로드 시 "손상됨" 경고(미공증) — `curl` 설치는 우회됨 |
| 백엔드 | 00f1972(게임 전용 토큰 `sg_` 12시간·범위 제한, 친구·파티·매칭 티켓·결과 API). 서버 e2e(`scripts/e2e_social.sh`) ALL PASS |
| 사이트 sublevel.steady2vivid.kro.kr | 3차 개편·디자인 패스·메가메뉴·게임소식 4분류·FAQ·프로필·푸터·사양표 배포. 이메일 `steady2vivid@gmail.com`(준환 확정) |

### main에 **아직 없는** 브랜치 (워크트리 `.claude/worktrees/*`, 전부 미푸시)
| 브랜치 | HEAD | 내용 | 상태 |
|---|---|---|---|
| `claude/intelligent-leavitt-8d85a9` | `f2211c0` | **열쇠 엘리베이터 규칙 ADR-360**: 열쇠 수 = ceil(팀 수×0.65) 상한 40, 팀당 이동 최대 2회, 옛 자유 승강 `RequestRide` 차단 | 컴파일·EditMode 1539/1539 통과. **(d) 실제 맵에서 열쇠 줍고 엘리베이터 타는 한 판 캡처·PlayMode 미완** |
| `claude/bot-slices-3072b8` | `2a616ec` | 봇 B7·B8(통계 자동화·`tools/bot_stats.sh`), ADR-311~320(초반 평화·파밍 우선) | 보통 난이도 목표 달성(아래). 쉬움·어려움·스쿼드 측정 진행 중 |
| `claude/island-impl` (워크트리 `quizzical-swanson-bcf35d`) | `87e35b0` | 대기 섬 T1~T7(#413~#419) + 돔·조명·전광판 수정 | EditMode 1576/1576, 컴파일 0. T8(#420)·이슈 종료·캡처 판정 미완 |
| 나머지 `claude/*`, `integration/r2·r3·r4` | — | 전부 main에 병합됨(삭제 가능). 단 `integration/r4`·`great-taussig-f977a1` 워크트리는 방금 병합 |

## 1-B. **지금 자동으로 돌고 있는 작업과 새 마스터가 이어받을 지점** (11:30 기준)
준환이 지시한 것: **"대기 섬을 main에 병합하고, 통과하면 #413~#419를 닫고 마스터가 main 병합·푸시"**. 이 작업은 마스터가 바뀌어도 계속 돌고 있다.

### 1-B-1. 대기 섬 통합 파이프라인 (워크트리 `.claude/worktrees/quizzical-swanson-bcf35d`, 브랜치 `claude/island-impl`)
- 상태: `origin/main`(663a3ce)을 이 브랜치에 병합 완료(병합 커밋 `7226672`, PlayMode 테스트 추가 `c3cb1ee`, 이전 `87e35b0`). 충돌한 생성물(.prefab/.unity/.asset/.mat)은 **main 쪽을 임시 선택**했으므로 병합 뒤 생성기로 재생성이 필요하다. `DECISIONS.md`는 양쪽 유지(main의 335·350, 섬의 380~386, 번호 중복 없음).
- 자동 스크립트(서브 세션 `integrator-r2`가 시작해 둠, 이 세션 소속이라 새 마스터는 메시지를 못 보낸다): 봇 측정 서버(`Build/server`의 Sublevel)와 Unity가 모두 사라진 뒤에 **생성기 5개 → EditMode 전체 → `WaitingIslandPlayTests`(대기 섬 PlayMode) 순서로 실행**하고, 통과하면 #413~#419에 근거 코멘트와 함께 `completed`로 닫는 것까지 하도록 지시돼 있다. 로그·결과 위치는 해당 워크트리의 `Logs/`(`test-editmode.xml`, `run-*.log`). **새 마스터는 이 결과를 직접 확인한다**(`ps`로 Unity 유무, `Logs/test-editmode.xml` 수치, 이슈 코멘트).
- 예상 완료: **13:10~13:30**(봇 측정 종료 11:50 대기 → 생성기 20~30분 → EditMode 10분 → PlayMode 15~20분 → 정리 10분). PlayMode 테스트(`WaitingRoom.ForceEnabled` 포함)는 한 번도 실행된 적 없어서 실패하면 원인 수정 후 재실행(+30분 이상). 생성기를 임시 선택한 충돌 위에서 돌리므로 재생성이 어긋나면 한 번 더 필요할 수 있다.
- **통과 후 새 마스터의 할 일(준환 지시)**: ①`island-impl` 최신 HEAD 확인, 테스트 수치(EditMode ≥1576, PlayMode 통과) 확인 ②main에서 `git merge --ff-only claude/island-impl`(main이 그 사이 전진했으면 `git merge`로 병합 후 다시 EditMode) ③`git push origin main`(미푸시 커밋·100MB 블롭 검사: `git rev-list --objects origin/main..main | git cat-file --batch-check='%(objecttype) %(objectsize)' | awk '$1=="blob"&&$2>95000000'`) ④#413~#419가 닫혔는지 확인(안 닫혔으면 근거 코멘트와 함께 닫기), **#420(T8 종단 검증)은 열어 둔다** ⑤대기 섬 캡처를 파일 시각 확인 후 직접 열어 준환에게 보고.
- 캡처 위치: `.claude/worktrees/quizzical-swanson-bcf35d/Logs/lookdev/island_*.png`(11:13 재캡처 8장 + `island_board_live.png`, `island_crowd.png`). 섬 세션의 자체 평가: "아직 밋밋해서 놀이동산 느낌은 못 냈다" — 준환의 방향 답을 받아 다듬어야 한다(§3).

### 1-B-2. 봇 측정 (bot-slices 워크트리 `Logs/botstats/*_all.csv`, 약 11:50 종료)
- 쉬움·어려움·스쿼드 시드 1·2(430초×6회)가 11:04부터 돈다. 끝나면 `bot-slices`(브랜치 `claude/bot-slices-3072b8`, HEAD `2a616ec`)의 결과를 판정: 4분 내 무장 ≥90%, 3분 내 사망 ≤8~10, 5분 시점 생존 55~65/80, 서버 프레임 평균 ≤10ms·p99 ≤25ms(보통 난이도 시드 1/2는 이미 달성: 생존 59/65, 무장 91/96%).
- 합격이면 #375·#376·#377을 근거(CSV·커밋)와 함께 닫고, **bot-slices를 통합 r5에 포함**(아래).

### 1-B-3. 11:50 이후 병행해서 풀 작업 (준환 승인 대기 중이던 제안, 새 마스터가 판단)
Unity 슬롯은 2개. 대기 섬 파이프라인이 1개를 쓰므로 나머지 1개로 아래 중 하나를 돌린다(독점 측정은 없음):
1. **열쇠 규칙 마무리**(`intelligent-leavitt-8d85a9`, `f2211c0`): 실제 맵에서 열쇠 줍기→삽입→엘리베이터 이동 캡처, 팀당 2회 제한, `RequestRide` 차단 확인, 봇의 열쇠 사용. #267 #268 #270 #272 닫기.
2. **달리기 애니메이션**(준환 "병신 같다"): 3인칭 달리기 8방향 캡처 판정·수정. 자동 달리기 속도 규칙(ADR-350)은 이미 main에 있다.
3. 차량 남은 항목(Goblin 실내 어두움 #99, 바이크 3인칭, 카메라 상수 데이터화).
- 이후 **통합 r5**: 열쇠(`f2211c0`)·봇(`2a616ec`)·위 결과를 순차 병합 → 생성기 → EditMode → PlayMode → main ff 병합·푸시. 서버·클라이언트 새 릴리스가 필요하면 프로토콜 확인 후 둘을 연달아 배포(§4-5).

### 1-B-4. 이번 라운드에 닫은 이슈 (근거 코멘트 있음)
#275(저지력 흔들림), #337(NavMesh 연결성 테스트·게이트 연결). 새 이슈 **#426**(NavMesh 57.4MB가 한도 30MB 초과, 타일 분할 검토, 우선순위 낮음)을 #337에서 분리. 열린 이슈는 98개. 남은 열린 이슈 중 main에 구현이 이미 들어 있는데 "게임 안 확인" 때문에 열려 있는 것은 #99(Goblin 실내), #223·#219(캐릭터 8방향·칼 스케일) 등 — **근거가 있으면 닫고, 사람 확인(체감·청취·실기)은 `docs/HUMAN_TODO.md`로 묶는다.**

## 2. 이번 세션의 결정사항 (준환 확정)
1. **1인 스쿼드**: 스쿼드에서 사람이 혼자면 1인 팀, 팀원 봇 자동 채움 없음. 봇은 4인 팀으로 균등(79명이면 19×4 + 3). ADR-332, `Core/TeamLayout.cs`. 늦게 들어온 사람은 1인 팀.
2. **열쇠 엘리베이터**: 팀당 이동 최대 2회, 열쇠 수는 팀 수 비례(0.65, 상한 40). SPEC §5-2의 나머지(편중 0.5, 30초 카운트다운, 지역 선택 15초, 팀 쿨다운 3분, 충전 90초)는 유지. 옛 `RequestRide`는 열쇠 배치가 된 뒤 막는다(ADR-360).
3. **동기화**: 합격 범위 ping 0~100ms(지터 ±20, 손실 1% 포함), **150ms는 보장하지 않음**(#425에서 캡 0.30→0.35~0.40 검토 제안만, 변경 금지). 창 2·3의 이동 표적 실패는 **하네스 결함**(장전 중 표적이 벽 뒤로 걸어감)으로 판명 → 무효. #397·#398 닫음. SPEC 수정안(미적용): "권장 ping ≤100ms … 150ms는 보장하지 않는다(되감기 상한 0.30s, #425)". #400(실서버 실제 핑)은 준환 Tailscale+서버 한 판 필요.
4. **봇 초반**: 초반 주먹싸움 금지·파밍 우선. 목표 곡선: **3분 내 사망 ≤8~10, 5분 시점 생존 약 55~65/80, 4분 내 무장 ≥90%**, 서버 프레임 평균 ≤10ms·p99 ≤25ms. ADR-314~320(비무장 12m 밖 무시, 초반 평화, 거리 0, 봇마다 종료 시각 어긋남, 기준 230초). 보통 난이도 시드 1/2 결과: 3분 사망 0/0, 5분 시점 생존 59/65, 무장 91%/96%, 프레임 6.7/10.2ms — **달성**. 쉬움·어려움·스쿼드 시드 1·2는 11:04 시작(약 11:50 종료, `bot-slices` 워크트리 `Logs/botstats/*_all.csv`).
5. **자동 달리기 = 스프린트 속도**(ADR-350): `=` 자동 달리기는 항상 스프린트 속도. 조준·사격·슬로우워크·치유 등 스프린트 불가 시 해제(`Core/AutoRunRules.cs`).
6. **차량 카메라**: 3인칭 원인은 헤드램프 빔(길이 7m)이 차체 크기에 포함된 것 — 수정. 자동차 1인칭은 시점을 올려 앞이 보이게. 남음: Goblin 실내 어두움, 바이크 3인칭, 카메라 상수 데이터화.
7. **이슈 처리 원칙**: 구현된 것은 근거(커밋·테스트·캡처)와 함께 `completed`, 불필요한 것은 사유와 함께 `not_planned`, 필요한 것만 열어 둔다. **11-17 이전 이슈 일괄 종료는 하지 않는다**(구현된 것이 많음). 사람 확인(청취·시각·실기)은 닫지 않고 체크리스트로 준환에게.
8. **Unity 동시 실행 최대 2개**(서로 다른 워크트리 폴더). 슬롯 락 `mkdir /private/tmp/sublevel-unity-slot1 || mkdir …slot2`, 끝나면 rmdir, 실패하면 30초 대기. **동기화·서버 성능·봇 측정은 독점 창**(다른 Unity·서버·게임 일체 금지). 이번에 독점 창을 어긴 세션이 있었다(오염).
9. 대기 섬 확정: 먼 바다 쪽 밀폐 섬(지름 80m), 서버 경계 클램프, 봇도 섬에 모임, 카운트다운 최대 90초·가득 차면 15초, 1차 번지 → 2차 전광판·사격장 → 3차 경주, 본 판 이월 없음(ADR-380~386). 스펙 `docs/WAITING_ISLAND_SPEC.md`, 이슈 #411(스펙)·#412(지도)·#413~#423.
10. 사양표(사이트): "예상치" 표기. Win 최소 GTX1050Ti/4코어/8GB, 권장 GTX1070~1080+Ryzen5 2600X급/16GB, Mac 최소 M1 8GB·권장 M1 16GB·M4 Pro, Intel Mac 미지원.

## 3. 준환에게 **아직 묻지 못한/답을 못 받은** 것
- **칩 부활(#269)을 접었는가?** 디테일 세션이 #269를 `not_planned`로 닫으며 "준환 지시"라고 적었으나 마스터가 전달한 적 없다. SPEC §6·ADR-171/179/239와 사이트 유저 가이드 "사망과 부활"은 칩 부활을 전제. 접은 것이 맞다면 SPEC·사이트·ADR 정리 필요. 확인 전 칩 코드 삭제 금지.
- 대기 섬 외형 방향(밋밋함 → '놀이동산 느낌' 참고 요청 중).
- NavMesh 57.4MB(한도 30MB): 타일 분할은 맵 구조 변경 — 우선순위 낮게 권함.
- Apple 공증(유료)과 사이트 설치 안내를 `curl`로 바꿀지.
- 서버 r4 배포 시 서버 환경변수(멀티 세션 보고: `docs/E2E_MULTI_CHECKLIST.md` 참고, 이름은 그 문서에서 확인).
- `sublevel-b/-c/-d`는 정리 세션이 삭제함(원격과 같은 clone, 미푸시 0, stash 비어 있음 — 복구 필요 없다고 보고).

## 4. 다음 할 일 (우선순위)
1. **봇 측정 종료(≈11:50) 후** 쉬움·어려움·스쿼드 결과 판정 → #375·#376·#377 근거와 함께 닫기, 미달 시 튜닝. 브랜치 `bot-slices`를 통합 r5에 포함.
2. **열쇠 규칙 마무리**(leavitt `f2211c0`): 실제 맵 한 판 캡처(열쇠 줍기→삽입→엘리베이터 이동), 팀당 2회 제한, `RequestRide` 차단 확인, 봇이 열쇠를 줍고 쓰는지(봇 세션과 조율), #267 #268 #270 #272 근거 코멘트·종료.
3. **통합 r5**: `intelligent-leavitt`(열쇠), `bot-slices`(B7·B8), `island-impl`(대기 섬 T1~T7)을 한 번에 병합 → 생성 파이프라인(WorldBuilder.Build → CharacterImport.SyncStaging → CharacterAnimClips.Reimport → TestBoxBuilder.Build → MainMenuBuilder.Build) → EditMode 전체 → PlayMode(팀·이동·로비·대기 섬) → main 병합·푸시.
4. **대기 섬**: 캡처를 직접 열어 판정(파일 시각 확인), '놀이동산 느낌' 개선, T8(#420: 80인 대기→투입 종단 + PERF + 준환 체크리스트), 이후 2차(사격장 #421, 전광판 확장 #422), 3차 지도 #423.
5. **게임 쪽 멀티 종단**: 서버 r4(통합 r5) 빌드·교체(Linux ARM64 `tools/unity.sh build server-linux-arm64` → `/opt/sublevel-game/releases/rN`, 옵션은 기본값 80인), 클라이언트 릴리스 `tools/release.sh --mac --win --version r5-<해시>` 후 `sublevel-web/scripts/release_upload.sh`, **서버 교체 시 접속자 확인**, 프로토콜이 바뀌면 서버·클라이언트를 연달아 배포. 준환과 `docs/E2E_MULTI_CHECKLIST.md`로 런처→로그인→친구·파티→매칭→접속→판 시작→결과 종단. #389·#391·#392(게임 쪽) 닫기.
6. **캐릭터**: 달리기 애니메이션(준환 "병신 같다") 캡처 판정·수정이 아직 안 됐다(자동 달리기 속도 규칙만 끝남), #219 칼 스케일, 방향 8방향, 엎드리기·점프·ADS 등. 캡처는 직접 열어 엄격히.
7. **차량 잔여**: Goblin 실내 어두움, 바이크 3인칭, 카메라 상수 데이터화, 소리 PlayMode(준환 청취 필요).
8. **동기화 후속**: #400 실서버 핑(준환 Tailscale 로그인 + 서버 한 판, 하네스가 루프백 전용이라 서버 시험 훅 코드 필요), #425 검토, SPEC 한 줄 반영. 동기화 브랜치는 이미 main에 병합됨.
9. **사이트**: 게임 캡처 추가 확보(전투·숲·건물 내부·엘리베이터·오염 연출), 폰 실기 확인, 포털(Steady2Vivid-Web) 연결 #386, 사양표를 실측으로 교체, 법적 검토(#394, 준환).
10. **이슈 정리 계속**: 열린 100개 중 사람 확인 항목은 `docs/HUMAN_TODO.md`로 묶고, 부분 구현은 영역별로 처리.
11. 정리: 병합 완료 워크트리·브랜치 삭제(`integration/r2·r3·r4`, `claude/*` 병합분 — 삭제 전 `git branch --contains`, 미커밋 확인), `docs/PROGRESS.md`·`docs/MASTER_STATE.md` 갱신(이번 세션에서 미갱신).

## 5. 이번 세션이 만든 파일(커밋 상태)
- main에 커밋·푸시됨: 위 통합 r3·r4 내용 전부, `docs/MASTER_HANDOFF_2.md`, `docs/handoff/CODEX_MASTER_PROMPT.md`, `docs/WAITING_ISLAND_SPEC.md`, `docs/ISSUE_RECONCILE_2026-11-18.md`, `docs/E2E_MULTI_CHECKLIST.md`(멀티 세션).
- **미커밋(main 폴더)**: `docs/MASTER_HANDOFF_3.md`(이 문서), `docs/handoff/NEXT_MASTER_PROMPT.md`, `docs/research/closed_old_issues_2026-11-18.json`·`docs/CLOSED_OLD_ISSUES_2026-11-18.md`(일괄 종료를 중단해 **쓰지 말 것** — 삭제해도 됨), `trash/`(Codex가 만든 대화 기록, 커밋하지 말 것), `Logs/`.
- 별도 저장소(전부 푸시·배포 완료): sublevel-web(`d00de83` 이후 `e558a5b`·`ee76b3e`·`806f8d8`…), sublevel-backend(`00f1972`), sublevel-launcher(0.1.7).

## 6. 이번 세션의 교훈 (같은 실수 방지)
- Unity 독점 창 중 컴파일·캡처를 돌린 세션이 측정을 오염시켰다(두 번). 창을 줄 때 **모든 세션에 명시**하고, 측정 세션에 창 종료 시각을 받아 푸는 순서를 지킨다. 반면 측정이 없을 땐 2개 병행으로 빨라진다.
- 완료 보고의 **파일 시각·테스트 수치를 확인**한다. 옛 캡처를 새 캡처로 속이게 되는 일이 있었다(스크립트가 슬롯 락 충돌로 중간 종료).
- 서브 세션이 `git restore --staged --worktree`·commit·push를 분류기에 막힐 때가 많다 → 위임 범위 안이면 마스터가 대신(되돌릴 수 없는 삭제는 하지 않는다).
- 정리 세션이 "삭제하지 말고 조사만" 지시를 어기고 폴더를 지웠다(복구 불필요했지만 위반). 삭제 지시는 명시적으로 쓴 것만 허용한다.
- 새 세션을 시작할 수 없는 턴(원격 제어 중 크론 등)이 있었다 → 그땐 기존 idle 세션에 SendMessage로 맡긴다.
- 통합은 **한 통합 브랜치에서 순차 병합 → 생성기 재실행 → EditMode → PlayMode → main ff 병합**이 가장 안전했다. 생성물(.prefab/.unity)은 충돌 시 한쪽 선택 말고 재생성.
- 이슈는 "세션이 닫았다"가 아니라 근거 코멘트가 있는지 확인한다.

## 7. 서브 세션 재시작 브리프 (이전 세션은 못 쓴다 — 아래를 새 세션에 주어라)
공통 머리말(모든 브리프 앞에): "SubLevel(/Volumes/WorkSpace/Project/sublevel, Unity 6000.3.28 + Fish-Net, GitHub junans0boi/sublevel). main=origin/main 663a3ce 이후. CLAUDE.md → docs/WORKFLOW.md → docs/UNITY_CLI.md → docs/MASTER_HANDOFF_3.md 순으로 읽는다. ADR 선행(룰 변경), CC0/상업 가능 에셋만+ledger, 비밀 금지. 새 워크트리에는 git-ignored 자산(AssetStaging·Assets/_Project/Art/Characters FBX·.meta·Library)을 main 폴더에서 cp -c로 복사. Unity 슬롯 락(최대 2개, mkdir /private/tmp/sublevel-unity-slotN), 측정은 독점 창. 커밋은 워크트리 브랜치(영문), push는 마스터. 이슈는 근거와 함께 닫기, 사람 확인은 체크리스트."
| 세션 | 브리프 요약 |
|---|---|
| 통합 r5 | §4-3. 대상 브랜치 intelligent-leavitt(f2211c0)·bot-slices(2a616ec)·island-impl(87e35b0). 순차 병합 → 생성기 → EditMode → PlayMode → 보고(마스터가 main 병합) |
| 열쇠 마무리 | 워크트리 `.claude/worktrees/intelligent-leavitt-8d85a9`, 브랜치 `claude/intelligent-leavitt-8d85a9`. §4-2 |
| 봇 | 워크트리 `.claude/worktrees/bot-slices-3072b8`. §2-4 곡선, 쉬움·어려움·스쿼드 측정 판정, B8 #377, `tools/bot_stats.sh` |
| 대기 섬 | 워크트리 `.claude/worktrees/quizzical-swanson-bcf35d`(브랜치 `claude/island-impl`). §4-4, 이슈 #413~#423 |
| 캐릭터·애니 | 달리기 애니메이션 캡처 판정·수정, #219, 8방향 등. 브랜치 병합분은 main에 있음 → 새 워크트리 |
| 맵·차량 | §4-7, #337 베이크 게이트·#329·#339 성능 판정 |
| 멀티 클라이언트 | §4-5 종단. 서버 환경변수·E2E 체크리스트 |
| 사이트(t-infra) | 사이트 저장소 `/Volumes/WorkSpace/Project/sublevel-web`, 게임 캡처 확보·사양 실측 교체·`curl` 설치 안내(승인 시) |
| 런처(t-launcher) | `/Volumes/WorkSpace/Project/sublevel-launcher`, 필요 시 새 릴리스(mac+win 모두 업로드 `launcher_upload.sh`) |
| 서버(t-server) | `docs/DEPLOY_GAME_SERVER.md`, 서버 r4 교체·접속자 확인·롤백 |
| 백엔드 | `/Volumes/WorkSpace/Project/sublevel-backend`, 필요 시 API 보강 |

## 8. 서브 세션 이전 이름 ↔ 워크트리 (참고)
`t-bot`=bot-slices-3072b8, `mp-client`=festive-almeida-b611e2, `issues-detail-pollution`=intelligent-leavitt-8d85a9, `issues-character-anim`=zealous-nobel-1ce9c1, `issues-map-vehicles`=charming-lamport-ffd0f6, `integrator-r2`(대기 섬)=quizzical-swanson-bcf35d, `integrator-r4`=great-taussig-f977a1, `squad-solo`=squad-verify, `t-sync`=sync-lagcomp-7d48a6, `t-server`=server-run-1263cb. 나머지(t-map, t-interact 등)는 이미 보관됨.

## 9. 인수받은 즉시 할 것
1. `date`, `git worktree list`, `git log origin/main -3`, `gh issue list --state open`, `ps`(Unity·서버)로 현황 확인.
2. 봇 측정 결과(`bot-slices` 워크트리 `Logs/botstats/*_all.csv`) 확인 — 측정 중이면 Unity를 돌리지 않는다.
3. 준환에게 §3 질문을 한 번에 한 줄씩.
4. `docs/PROGRESS.md`·`docs/MASTER_STATE.md` 갱신, 이 문서 커밋(준환 승인).

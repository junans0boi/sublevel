# 마스터 인수인계 2 (2026-11-18 14:00 기준)

> 이 문서는 마스터 세션(Claude)의 토큰이 소진될 때를 대비해 **다음 마스터(Codex 등)가 바로 이어받도록** 쓴 것이다.
> 먼저 읽을 것: `CLAUDE.md` → `docs/MASTER_HANDOFF.md`(1차, 규칙·성향·역사) → **이 문서** → `docs/MASTER_STATE.md` → `docs/PROGRESS.md`.
> 비밀 값(키·토큰·시리얼)은 이 문서에도 없다. 위치만 적는다. 읽거나 출력하거나 저장소에 넣지 말 것.

## 0. 사람과 역할
- 준환(1인 개발자, STEADY TO VIVID STUDIO). 한국어, 간결·직접. 사소한 건 묻지 말고 결정해서 진행하고 DECISIONS.md에 가정을 남긴다. 되돌릴 수 없고 양쪽 다 합리적인 결정만 묻는다.
- 마스터의 일: 작업을 쪼개 서브 세션(또는 직접 구현)에 시키고, 결과를 **직접 검증**하고(스크린샷·테스트·공개 URL), 이슈를 근거와 함께 닫고, 준환에게 짧게 보고한다. 서브 세션 보고는 증거가 아니다.
- 준환이 직접 해야 하는 일(대신하지 말 것): Windows PC 실기 확인, 서버에 사람이 접속해 한 판, 폰 확인, 법적 검토(RISKS), 코드 서명 등 유료 결제, 계정 생성/약관 동의.

## 1. 큰 그림 (현재 상태)
게임: SubLevel — Unity 6000.3.28 + URP + Fish-Net, 서버 권위 배틀로얄, Mac(Apple Silicon만) + Windows. 판당 80명(사람+봇), 6개 지역 순차 오염, 열쇠 엘리베이터 탈출.

| 영역 | 상태 |
|---|---|
| 싱글플레이 핵심(전투·파밍·오염·차량 일부·맵 분위기·새 설정창) | 완료, main 병합·푸시 |
| 서버(오라클 A1, Ubuntu aarch64) | 통합 빌드 r2 가동 중(`--bots 5 --max-players 16` 시험 설정 — 80명 규칙으로 바꿀지 #383에서 결정 필요) |
| 백엔드(Fastify+SQLite) | 로그인·닉네임·런처 manifest·**게임 토큰**·친구·파티·매칭 티켓·결과 API 배포(커밋 00f1972), e2e 통과 |
| 사이트 sublevel.steady2vivid.kro.kr | 3차 개편·디자인 패스 배포(홈·게임소식 4분류·메가메뉴·FAQ·프로필·푸터 등) |
| 런처(Tauri) | 0.1.5 Mac/Windows 서명 배포, 자동 업데이트 동작, 새 디자인(층 타임라인+분절 진행 바) |
| 게임 클라이언트 릴리스 | `r1-cf90ce8-all`(Mac+Windows) 서버 업로드됨. Windows에서 설치·접속·플레이 확인(준환) |
| 동기화(lagcomp) | 창2·3 측정 완료. after 모델이 낫고, 이동 표적은 ping≤100 안정 합격 아님. #397/#398 열림 |
| 봇 고도화 | B5(#374) 닫음, B6(#375) 커밋(3a9f77f) 캡처·재측정 남음, B7·B8 미착수 |
| 멀티 클라이언트 | #389/#390 코드 커밋(f1d0e8c), #391 코드 작성, #392 코드 일부, **컴파일·테스트 전** |
| 1인 스쿼드(#424) | ADR-330·SPEC 갱신·코드 커밋(86fa83c), 컴파일 전 |
| 차량 잔여 | 코드·조사 시트 커밋(392b7d8), 컴파일 전 |
| 대기 섬 | 계획·티켓만(#411 스펙, #412 지도, 하위 11개). **구현은 위 작업 끝난 뒤** |

### main / 푸시 상태
- main = `0cd8d16`(푸시됨, LFS 재작성 후 해시). origin/main과 동기. 이후 병합 대기 중: `integration/r2`.
- 미푸시 워크트리 브랜치 다수(아래 §4). 푸시는 **준환이 승인**한다(앞서 main 병합·푸시는 승인받음. 새 브랜치 푸시는 필요 시 한 줄로 묻기).
- `backup/pre-lfs-migrate` 로컬 브랜치(LFS migrate 이전 상태) 있음, 지워도 됨.
- LFS 용량: 0.83GB+(GitHub 무료 1GB). 초과하면 push 실패 — 유료 결제는 대신 못 함, 준환에게 알릴 것. World.unity(현재 130MB)는 `.gitattributes`에서 LFS 추적으로 마이그레이션됨.

## 2. 지금 돌고 있는 일 (가장 중요)
**통합 세션 `integrator-r2`**(워크트리 `.claude/worktrees/quizzical-swanson-bcf35d`, 브랜치 `integration/r2`, HEAD 21afdf5 이후)가 4개 브랜치를 한 번에 컴파일·EditMode 중이다:
- `claude/festive-almeida-b611e2`(멀티 클라이언트), `claude/festive-clarke-d96a35`(1인 스쿼드), `claude/eager-moore-7bf6f8`(차량), `claude/bot-slices-3072b8`(봇 B5+B6, 3a9f77f는 파이프라인 후 추가 병합 예정).
- 끝나면 마스터에게 보고 → 마스터가 **main에 병합(ff-only 가능하면)·푸시·이슈 닫기**.
- 병합 후 절차(반드시): `WorldBuilder.Build`(해시 비교) → `CharacterImport.SyncStaging` → `CharacterAnimClips.Reimport` → `TestBoxBuilder.Build` → `MainMenuBuilder.Build` → EditMode 전체 → 빌드. 지난 통합(r1)은 EditMode 1455/1455였고, 이 통합은 1480 전후가 정상.
- **main이 dirty일 때**(생성물 변경) ff 병합이 막히면: 변경을 tar로 백업 후 `git checkout -- .` → merge. 지난번에 이렇게 했다.

## 3. 반드시 지킬 규칙 (사고 방지)
1. **Unity는 한 번에 하나.** 실행 전 `ps -axo command | grep 'Unity.app/Contents/MacOS/Unity '`. 동기화 측정 같은 **독점 창**에는 다른 어떤 Unity·서버·게임 프로세스도 금지(측정 무효). 컴파일·EditMode는 부하 가드(load<~5) 하에 최대 2개까지 병행 가능하다고 제안만 했고, 실제로는 순차로 썼다.
2. **git-ignored 자산 복사**: `AssetStaging/`(1.8GB), `Assets/_Project/Art/Characters`의 Mixamo FBX, `.meta`, `Library`는 워크트리에 `cp -c`로 복사. 없으면 Unity가 orphan `.meta`를 지워서 **FBX .meta 120개 삭제 커밋** 사고가 난다(r1에서 발생·복구, EditMode `Fbx_AreNotTracked_ButMetaAre`가 잡음). 원본은 main 폴더 또는 `.claude/worktrees/release-r1`.
3. **게임 룰 변경은 ADR 먼저**(`docs/DECISIONS.md`). SPEC과 충돌하면 SPEC이 이긴다. ADR 번호 대역: 동기화 300–304, 봇 290–294·311, 추가 310–319, 런처/서버 320–324, Linux 295–296, 스쿼드 330–339(ADR-330 사용), 차량 340–349.
4. **라이선스**: 에셋은 CC0/상업 가능만, 상표·실존 브랜드 금지, `docs/ASSET_LEDGER.md` 기록. AI 생성 에셋은 최종 빌드에 포함 금지. 폰트는 OFL 확인 후 ledger.
5. 비밀 금지: `/Volumes/WorkSpace/Personal/Key/`(구글 OAuth JSON, ssh 접속 스크립트, 서명 키 등)는 **읽거나 출력하지 않는다**. 서버 접속은 `ssh-ubuntu.command`를 읽지 말고 **실행만**, 명령은 표준입력으로 준다(예: `printf 'cmd; exit\n' | /Volumes/WorkSpace/Personal/Key/ssh-ubuntu.command`). 환경 변수 파일은 서버 `/etc/sublevel-backend/backend.env`(640)에만 있다.
6. 서브 세션의 **commit/push가 권한 분류기에 자주 막힌다**(마스터 허가 메시지로 못 풀림). 준환이 위임한 범위(사이트·런처·백엔드·워크트리 브랜치 커밋, 통합 병합·푸시)는 마스터가 직접 수행했다. 위임 범위 밖은 하지 말 것.
7. 한 번에 서브 세션 5개 정도까지(사용량 한도 소진 이력). 완료된 세션은 보관(archive)한다.
8. `docs/PROGRESS.md`, `docs/MASTER_STATE.md`를 작업 후 갱신한다(이번 세션에선 미갱신 — §9 TODO).

## 4. 저장소·워크트리·브랜치
| 저장소 | 경로 | 비고 |
|---|---|---|
| 게임 | `/Volumes/WorkSpace/Project/sublevel` (GitHub junans0boi/sublevel) | main |
| 사이트 | `/Volumes/WorkSpace/Project/sublevel-web` (junans0boi/sublevel-web, private) | main f6e23e9 이후 deploy |
| 백엔드 | `/Volumes/WorkSpace/Project/sublevel-backend` (junans0boi/sublevel-backend, private) | main 00f1972 배포 |
| 런처 | `/Volumes/WorkSpace/Project/sublevel-launcher` (junans0boi/sublevel-launcher, private) | main 32b798c |
| 포털 | `/Volumes/WorkSpace/Project/Steady2Vivid-Web` | 게임 사이트 연결 #386 미착수 |

게임 저장소 워크트리(미푸시): `bot-slices-3072b8`(3a9f77f), `eager-moore-7bf6f8`(392b7d8), `festive-almeida-b611e2`(f1d0e8c), `festive-clarke-d96a35`(86fa83c), `sync-lagcomp-7d48a6`(9964db4, **docs/research/lagcomp_matrix.md + CSV 미커밋**, 인덱스에 남이 스테이징한 씬 8개 — 건드리지 말 것), `server-run-1263cb`(df9b0db, #383 배포 문서·스크립트), `quizzical-swanson-bcf35d`(integration/r2), `release-r1`(integration/r1, 이미 main에 병합됨 — 삭제 가능), `linux-server-build-fce850`/`map-forest-doors-acb9ba`/`ui-settings-7e0605`(병합 완료 — 삭제 가능). 별도: `sublevel-wt/animation`(커밋 안 한 변경 42개, 지우기 전 확인), `sublevel-b/-c/-d`(각 5GB, 용도 불명 — **준환에게 먼저 물을 것**).
삭제는 되돌릴 수 없으니 병합 여부(`git branch --contains`)와 미커밋 변경을 먼저 확인한다.

## 5. 인프라 (오라클 서버)
- Oracle Always Free A1, Ubuntu 24.04 aarch64, 2코어, 11GiB. Tailscale tailnet(서버 100.97.58.29). **공개 포트는 80/443만**(Caddy 자동 HTTPS). 게임 UDP 7770은 tailscale0로만 → 플레이하려면 접속자도 Tailscale 필요(준환 Windows/Mac은 설정됨).
- systemd: `sublevel-game`(게임 서버, `/opt/sublevel-game/current`→`releases/r*`, 롤백용 r1 보관), `sublevel-backend`(Node, 포트 3100), Caddy, 포털(3000).
- 게임 서버 교체: Linux ARM64 빌드(`tools/unity.sh build server-linux-arm64`) → 서버 `/opt/sublevel-game/releases/rN` → `current` 링크 교체 → 재시작·`/api/health`. 절차·체크리스트는 `claude/server-run-1263cb`의 `docs/deploy/*`(main 미병합 — **병합 필요**, #383).
- 백엔드 배포: 푸시 후 서버에서 `cd /opt/sublevel-backend && scripts/deploy.sh deploy`(health 확인, 롤백 `rollback`). 사이트 배포: `sublevel-web`에서 푸시 후 `scripts/deploy.sh`(서버에서 빌드·원자적 current 교체).
- 클라이언트 릴리스 업로드: `tools/release.sh [--mac] [--win] [--upload] --version <이름>` → `sublevel-web/scripts/release_upload.sh`(서버가 sha256 재검증, 기존 버전 이름 거부). **한 버전 폴더에 mac+win이 함께 있어야** 백엔드가 최신 하나를 고른다(현재 `r1-cf90ce8-all`). 런처는 `sublevel-web/scripts/launcher_upload.sh`로 올린다(update.json 병합 포함).
- Windows 빌드는 게임: `tools/unity.sh build win`(로컬 Unity), 런처: GitHub Actions(windows-latest).

## 6. 세션/에이전트 구성 (Claude 기준) — 이어받는 법
Claude Code의 서브 세션들(`mcp__ccd_session__start_session`, `SendMessage`로 지시). 이어받는 마스터가 Claude가 아니면 **같은 일을 CLI/직접 구현으로** 한다.
- 작업 중/대기: `t-sync`(동기화), `t-bot`(봇 B6~B8), `t-backend`, `t-launcher`, `t-server`, `t-infra`(사이트), `mp-client`(멀티 클라이언트), `squad-solo`, `vehicles-rest`, `integrator-r2`, `issue-reconcile-2`.
- 보관됨(완료): t-map, t-interact, t-animation, t-ui-settings, t-linux, t-bot-spike, 사이드 채팅, 지형 분위기, issue-reconcile, waiting-island-plan.
- 서브 세션은 통상 `commit`이 막히면 보고만 하고 멈춘다 → 마스터가 커밋·푸시·배포.

## 7. 남은 일 (우선순위)
1. **integrator-r2 결과 확인 → main 병합·푸시 → 이슈 닫기**: #389, #390(게임 쪽), #391, #392, #424, #345·#347·#348·#349·#350, #375(캡처 3장·재측정은 별도, 닫지 말 것). 근거(커밋·테스트명) 코멘트 후 닫기.
2. **멀티 종단**: 병합 후 `mp-client`가 #391/#392 컴파일·EditMode·실연동(런처 → 게임 토큰 → 친구·파티 → 매칭 티켓 → 서버 접속). 서버 한 판은 준환과 함께.
3. **동기화(#397/#398/#400)**: 다음 실험 (1) 이동 구간 단위 큐 깊이·사본 지연 매 프레임 기록(코드 ~30분, 창 ~10분) → (2) 지연 변동 확인 시 ViewTick 대신 사본이 실제 보인 틱으로 샷 주장(코드 ~1시간, 창 ~20분). **독점 창 필요**, 캡 0.35/0.40·ADR 변경 금지(제안만). 합격 기준은 하네스의 Q6: ping≤80은 정지 100%+이동 90%, ping≥100은 정지 95%+이동 80%(35 m는 이동 기준). 판정: after가 before보다 낫고(ping100 이동 58→90%), ping150 불합격, 보간1은 채택 근거 없음. 결과 문서 `docs/research/lagcomp_matrix.md`는 sync 브랜치에 **미커밋**.
4. **봇 B6 마무리**(#375 캡처 3장·재측정) → B7 #376(이동 우선순위·총성 조사) → B8 #377(난이도 튜닝+80인 통계 자동화, PERF 재판정). 기준선: `docs/research/bot_spike_2026-11-17.md`(main 미추적 파일, 커밋 필요), 서버 프레임 p99 ≤25ms.
5. **서버 운영**: 서버 옵션을 판당 80명 규칙에 맞추기(현재 시험값 5봇/16명), #383 배포 문서 main 병합·이슈 정리, 사람 접속 한 판(준환).
6. **런처/사이트 이슈 닫기**: #403·#404·#410(Windows는 준환 확인됨, #405는 닫음), #409 딥링크 e2e(설치된 런처 + 사이트 "게임 시작"), #393 우산, #402 잔여(RELEASE.md, 로그인 세션 manifest 200 e2e), #406 자동 업데이트(동작 확인됨 → 근거 코멘트 후 닫기 가능). 새 런처 릴리스 시 mac+win 모두 올릴 것.
7. **사이트**: 게임 캡처 추가 확보(전투·숲·건물 내부·엘리베이터·오염 연출 장면 목록을 t-infra가 보고하기로 함, 미수령), #386 포털 연결(Steady2Vivid-Web 폴더 신뢰 필요), 폰 실기 확인, 개인정보처리방침 법적 검토는 준환(#394).
8. **차량 잔여**: 통합에 포함됨. 컴파일·테스트 후 이슈 닫기.
9. **이슈 정리**: 열린 143개(2차 정리에서 추가로 닫을 게 없었음). 부분 구현 66건은 영역별로 묶어 세션에 맡기거나 출시 전 점검 목록으로 묶는 안을 준환에게 제안한 상태(응답 대기: "분류표 만들까요?"). 사람 확인 24건은 준환용 해야 할 일 목록으로 정리할 것(`docs/HUMAN_TODO.md` 갱신).
10. **대기 섬**(#411~#423): 위가 끝난 뒤 구현. 스펙 `docs/WAITING_ISLAND_SPEC.md`(main 폴더에 **미커밋**), 지도 #412. 결정: 먼 바다 쪽 밀폐 섬, 사람은 서버가 섬 경계 클램프, 봇도 섬에 모아 인원처럼 보이게(RISKS 올림), 카운트다운 최대 90초·가득 차면 15초, 1차 번지점프→2차 전광판·사격장→3차 경주, 본 판 이월 없음.
11. 정리: 병합된 워크트리/브랜치 삭제(§4), main 폴더의 미커밋 파일(WAITING_ISLAND_SPEC.md, RISKS 수정, ISSUE_RECONCILE 문서, bot_spike 문서, 미커밋 단풍 재질 *_Broadleaf_Gold/Rust.mat) 커밋 여부 결정 — 재질은 생성물일 수 있으니 WorldBuilder 해시와 함께 확인.

## 8. 준환 성향·최근 피드백 (자세한 건 docs/handoff/README.md)
- 증거 없는 "완료" 보고를 싫어한다: 스크린샷을 **직접 열어 엄격히** 판단하고, 안 되면 다시 시킨다.
- 디자인: 사이트는 서든어택식 레이아웃 *참고*, 런처는 배틀그라운드식이 *아닌* 자기만의 디자인(승인된 안: 층 타임라인 B1~B8 + 분절 진행 바 + 구역 문구). "촌스러움", "섹션 구분 안 됨", "노골적 문구(스크린샷·시스템 요구사양 섹션)" 피드백 반영 완료.
- 사이트 이메일은 `steady2vivid@gmail.com`(준환이 .com 확정).
- Intel Mac은 미지원(런처·게임 모두 Apple Silicon만). 최소/권장 사양은 "예상치"로 표기(실측 전): Win 최소 GTX1050Ti/4코어/8GB, 권장 GTX1070~1080+Ryzen5 2600X급/16GB, Mac 최소 M1 8GB, 권장 M1 16GB·M4 Pro.
- 장비: 개발 MacBook Pro M1 13", M4 Pro 맥북, Windows(2600X+GTX1080). Unity 병렬성이 약한 이유. M4 Pro/Windows를 빌드·측정 노드로 쓰자는 제안은 준환 설정이 필요해 보류.
- 혼자 스쿼드는 **1인 스쿼드**(ADR-330) — 봇 팀원 자동 채움 없음.
- 사용량: 한도 소진 이력 있음. 병렬 세션 수를 줄여 운영.

## 9. 아직 갱신 안 된 것 (인수받은 즉시 할 것)
- `docs/PROGRESS.md`, `docs/MASTER_STATE.md`에 이 문서의 §1·§7 반영.
- `docs/HUMAN_TODO.md`에 준환 해야 할 일(§0) 갱신.
- 이슈 정리 결과 문서 `docs/ISSUE_RECONCILE_2026-11-18.md`는 미커밋.
- 이 문서와 `docs/handoff/CODEX_MASTER_PROMPT.md` 커밋(준환 승인 후, 영문 커밋 메시지).

## 10. 검증 레시피
- 공개 URL: 사이트 라우트 200 루프(`curl -s -o /dev/null -w '%{http_code}'`), `/api/health`의 commit이 배포 커밋과 같은지.
- 백엔드 e2e: 서버에서 `scripts/e2e-accounts.ts create` → 로컬에서 환경변수로 토큰 주입 → `scripts/e2e_social.sh` → `cleanup`(테스트 계정 삭제). 토큰은 출력·저장 금지.
- 런처 자동 업데이트: 서버의 이전 버전 아카이브를 풀어 실행 → 2초 내 새 버전.
- EditMode: `tools/unity.sh test editmode`(UNITY_CLI.md 참조), 결과 `Logs/test-editmode.xml`.
- 사이트 캡처: `sublevel-web/docs/captures/`, 런처 캡처: `sublevel-launcher/docs/captures/`.

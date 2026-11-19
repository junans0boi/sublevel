# 마스터 인수인계 4 (2026-11-19 15:40 기준)

> 이전 마스터(이 세션)의 토큰이 떨어져 넘긴다. 이 세션의 서브 에이전트에는 메시지를 보낼 수 없다. 워크트리와 브랜치 상태는 남아 있으니 거기서 이어받는다.
> 읽는 순서: `CLAUDE.md` → `docs/MASTER_HANDOFF_3.md`(규칙·인프라) → **이 문서**. 비밀 값은 읽지 않는다(`/Volumes/WorkSpace/Personal/Key/`, 서버 접속 스크립트는 실행만 한다).

## 0. 준환과 일하는 법 (오늘 확인한 것)
- 한국어로 간결하게. **"너가 담당자·관리자, 엄청 중요한 것만 묻고 나머지는 알아서"**. 서브 세션을 최대한 병렬로 돌린다.
- **장비는 M4 Pro(12코어, 24GB)다. M1 아님**(옛 문서는 틀렸다. 준환이 여러 번 정정함). Unity는 서로 다른 워크트리에서 **동시 3개**까지 돌린다.
- 느린 진행을 매우 싫어한다("너무 오래걸린다"). 서브 세션이 캡처를 반복해 다시 찍으면 시간 상자(15~20분)를 걸고 마무리시킨다.
- 캡처는 파일 시각을 확인하고 직접 열어 판정한 뒤 `SendUserFile`로 보낸다(준환은 원격으로 볼 때가 많다).
- Unity 슬롯 락 `/private/tmp/sublevel-unity-slot{1,2,3}`은 **Unity가 실제로 도는 동안만** 잡는다. 주인 없는 락은 확인 후 rmdir. 1분 간격 감시(held > running이 3분 넘게 이어지면 경고)를 Monitor로 돌렸다.

## 1. 지금 진행 중: 통합 r5 → 배포 (준환 지시: "통합하고 배포해줘")
- 워크트리 `.claude/worktrees/integration-r5`(브랜치 `integration/r5`, main 663a3ce 기준). 병렬용으로 `integration-r5-pm`, `integration-r5-cap`도 있다.
- 병합 대상(HEAD):
  | 브랜치 | HEAD | 내용 |
  |---|---|---|
  | claude/docs-r5 | be07620 | #340 #293 #394 문서, #402 RELEASE.md |
  | claude/bot-slices-3072b8 | 2165252 | 봇 B7·B8, ADR-321(무기 안내 150초) |
  | claude/intelligent-leavitt-8d85a9 | 8b3afc2 | 열쇠 ADR-360, 보급 열쇠, 오염 HUD·지도, 해안 승강장 수영 판정 버그 수정 |
  | claude/fx-impact | f9b36ac | 피·탄흔·파편·카메라 흔들림 ADR-470·471 |
  | claude/fx-weapon | 4c28d05 | 탄피·전투 소리·실내 꼬리·탄창 분리(MagazineKit) ADR-396 |
  | claude/fx-ragdoll | 5d8f533 | 래그돌(RagdollRigBuilder) ADR-395 |
  | claude/char-anim-r5 | 7724e31 | 보폭 수정, Mixamo 8방향 옆걸음 ADR-390·391, 1인칭 칼 |
  | claude/island-impl | 46658d4 | 대기 섬 T1~T7, 번지 60m·고속 승강기 ADR-387 |
- 통합 세션에 지시한 것:
  - 병합 → `git lfs checkout`(씬 4개가 LFS 포인터였음) → 생성기 5개 → 컴파일·EditMode 전체 → 영향 묶음 PlayMode 먼저 → 보고.
  - main과 비교해 네트워크 RPC·SyncVar가 바뀌었으면 `LobbyNet.Protocol` 2→3과 ADR 한 줄.
  - 기존 PlayMode 실패 약 21개(맵 지역 수·NavMesh·렌더 p99·팀/핑·보급·문·차량 소리 등)는 main 대비 회귀인지 분류한다.
- **15:52 진행 상황**: 8개 브랜치 병합 완료(`9204939`). 재생성 커밋(`6821c0b`), 프로토콜 2→3(`bf342e5`, ADR-322), 캡처 테스트 수정(`99ff288`)까지 끝났다. 이전 세션의 통합 에이전트가 아직 돌고 있을 수 있다: 영향 묶음 PlayMode(`integration-r5-pm`), 실제 맵 캡처(`integration-r5-cap`), main 기준 비교(`great-taussig-f977a1`). **`ps`로 이 Unity 작업이 살아 있으면 같은 작업을 새로 시작하지 말고 끝나기를 기다린다.** integration/r5 브랜치를 직접 커밋하거나 push하지 않는다(통합 에이전트와 충돌). 그 에이전트가 끝나서 Unity 프로세스가 사라지고 integration-r5에 더 이상 커밋이 생기지 않으면 그때 이어받는다.
- **16:00 통합 에이전트 최종 보고**: HEAD `99ff288`. 컴파일 0, EditMode 1632/1632. 실제 맵 캡처(`integration-r5-cap/Logs/worldfx/`, 15:38)에서 래그돌·피·탄피·옆걸음 합격. **영향 묶음 PlayMode는 결과가 없다**: 1차는 Unity `Illegal instruction: 4`로 죽었고, 재실행은 **시스템 디스크 가득 참(ENOSPC)**으로 중단됐다. 시스템 Data 볼륨이 99%, 여유 1.7GB다(WorkSpace 볼륨은 78GB 여유). 큰 캐시: `~/Library/Caches` 13GB(codex 5.4G, Google 1.6G, VSCode.ShipIt 1.0G 등). **삭제는 준환 승인을 받은 뒤에만.** 공간을 확보하면 영향 묶음 PlayMode부터 다시 돌린다. 기존 실패 TeamNetPlayTests 2개는 main에서도 똑같이 실패한다. 봇 재측정에는 Mac 서버 빌드(`tools/unity.sh build server`)가 먼저 필요하다. `-cap` 워크트리는 6821c0b에 있으니 99ff288로 옮겨야 한다.
- **16:10 추가**: 영향 묶음 PlayMode 결과 118/122. 회귀는 WaitingIsland 3개(봇 출현 보호 필터와 대기실 필터가 서로 재귀 → StackOverflow)이고, `MatchDirector.cs`에서 AddBot을 ServerProtect보다 앞으로 옮겨 수정했다. 재실행이 통과하면 integration/r5에 커밋하라고 통합 에이전트에 지시해 두었다(`WorldFxCaptureTests.cs.meta`도 같이). LobbyPlayTests 1개는 포트 7770 동시 사용 때문으로 보이며 재실행으로 확인한다. 디스크 여유는 4.9GB까지 회복했다. 통합 에이전트가 전체 PlayMode 결과를 이 문서 끝에 덧붙이게 했다.
- **16:16 추가**: `-cap`의 옛 실행은 종료했다. pm 워크트리에서 WaitingIsland·Lobby 재실행 중(16:15 시작). 통합 에이전트는 전역 규칙("commit 전에 묻는다")을 들어 **MatchDirector 수정과 `WorldFxCaptureTests.cs.meta`를 커밋하지 않고 대기 중**이다. 새 마스터는 재실행 결과를 확인하고 준환 승인을 받아 커밋한다. 메시지는 "fix(island): register bot in waiting room before spawn protection (filter recursion)". 그다음 최신 커밋으로 전체 PlayMode(!Long)를 돌리고, main 푸시와 배포로 이어간다.
- **새 마스터가 할 일:**
  1. `git -C .claude/worktrees/integration-r5 log --oneline -15`로 어디까지 병합됐는지 본다. `Logs/test-*.xml`의 수치와 `ps`로 Unity 실행 여부를 확인한다. 진행이 멈췄으면 새 서브 에이전트로 이어서 한다.
  2. 통과하면 main에서 `git merge --ff-only integration/r5`(안 되면 merge). 100MB 블롭을 검사하고(MASTER_HANDOFF_3 §1-B-1 명령) `git push origin main`.
  3. **배포**: 같은 커밋으로 서버(`tools/unity.sh build server-linux-arm64`)·Mac·Windows 빌드를 **3개 워크트리에서 병렬**로 돌린다(Library는 `cp -c`). `tools/release.sh --mac --win --version r5-<해시>` → `sublevel-web/scripts/release_upload.sh` → sha256 확인. 서버는 `docs/DEPLOY_GAME_SERVER.md` 절차대로 `releases/r4`로 올리고, **접속자 0을 확인한 뒤** `current`를 교체하고 restart한다. 로그 version이 클라이언트와 같은지 확인한다. 문제가 있으면 r3로 롤백. ExecStart는 80인 기본값(`--dedicated --port 7770 --start-seconds 60`).
  4. 배포 뒤 봇 재측정(독점 창, 약 44분): bot-slices 보고서의 명령 5개(`tools/bot_stats.sh 430 easy 2` / `hard 1` / `normal 1` / `normal "1 2" --perf-squad` / `mix 1`)를 통합 빌드에서 돌린다. 합격하면 #375 #376 #377과 #373(캡처 3장 필요)을 닫는다.
  5. 미커밋 파일을 커밋한다: main 폴더 `CLAUDE.md`(M4 Pro 수정), `docs/HUMAN_TODO.md`(청취·시각 4건), `docs/ASSET_LEDGER.md`(C-0011, char-anim 브랜치에도 있으니 중복 확인), `docs/MASTER_HANDOFF_3.md`·`4.md`, `docs/handoff/NEXT_MASTER_PROMPT.md`. `trash/`와 `docs/CLOSED_OLD_ISSUES_*`, `docs/research/closed_old_issues_*`는 커밋하지 않는다.

## 2. 오늘 닫은 이슈 (근거 코멘트 있음)
#2 #294 #205 #206 #230(감사), #340 #293 #394(문서), #199 #288 #287(무기 연출), #197 #198 #201(피·탄흔·흔들림), #196(래그돌), #268 #267 #270(열쇠·오염), #219 #223 #302(애니). 열린 이슈는 아침 99개에서 약 79개로 줄었다. 나머지는 **실제로 미구현**이다(감사 결과 "구현됐는데 열린 것"은 거의 없었다).

## 3. 남은 후속 (r5 뒤)
- #200 장전: 1인칭에서 손에 든 탄창이 화면 밖에 있고, 3인칭에서는 새 탄창을 쥔 손이 빈손이다.
- 물 튀김이 흰 방울로만 보이고 소리가 없다. 탄흔이 밤에도 밝다.
- 소총 미끄러짐 평균 1.17 m/s, 조준 왼쪽 옆걸음 2.8 m/s.
- 엘리베이터 안 3인칭 카메라가 일부 승강장에서 문과 벽에 가린다(문·카 벽에 충돌체가 없음).
- 지도에서 "내 위치" 라벨이 지역명과 겹친다.
- 대기 섬:
  - 외형이 '놀이동산 느낌'이 아니다. 준환이 답하지 않았으니 기본 방침은 밝은 천막·깃발·조명줄·다양한 옷차림.
  - 점프 발판이 녹슨 검은 판이다.
  - 대기 봇이 전원 같은 옷이다.
  - 봇의 승강기 탑승이 안 된다.
  - #413~#419는 클라이언트 2개 실접속과 PERF 측정 뒤에 닫는다. #420은 종단 검증.
- #272 판 통계: 봇 재측정 데이터가 필요하다.
- 사이렌·가이거 소리 클립이 없다(훅만 있음).
- 준환 미답변: 칩 부활 #269를 접었는지(확인 전 코드 삭제 금지), NavMesh 57MB(#426), Apple 공증.
- 크레딧에 실존 총기명 "Glock19_reload"(원 제목)가 나온다. RISKS에 올렸다(법적 판단은 사람 몫).

## 4. 참고
- Mixamo 옆걸음 14개는 `AssetStaging/Mixamo/Strafe/`에 있다(git-ignored, 원본 재배포 금지). 새 워크트리에는 `cp -c -R`로 넣는다.
- 이 세션에서 Claude in Chrome 도구는 붙지 않았다. Chrome이 필요한 일은 `claude --chrome`으로 새 세션을 열어서 한다.

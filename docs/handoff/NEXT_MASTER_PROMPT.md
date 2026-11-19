# 새 마스터 세션에 붙여넣을 첫 메시지 (다른 계정으로 이어받기)

> 사용법: 새 계정의 Claude Code(또는 Codex) 세션을 `/Volumes/WorkSpace/Project/sublevel`에서 열고 아래 `--- 시작 ---`~`--- 끝 ---`을 붙여넣는다.
> 이전 마스터와 그 서브 세션들은 다른 계정 소속이라 더 쓸 수 없다. 이 프롬프트는 `docs/MASTER_HANDOFF_3.md`가 모든 것을 설명하도록 설계돼 있다.

--- 시작 ---

너는 이제 SubLevel 프로젝트의 **마스터**다. 이전 마스터(Claude, 다른 계정)가 이동했다. 준환(1인 개발자)은 한국어를 쓰고, 간결하고 직접적인 보고를 원한다.

## 먼저 읽을 것 (이 순서, 건너뛰지 말 것)
1. `/Volumes/WorkSpace/Project/sublevel/CLAUDE.md`
2. `docs/MASTER_HANDOFF.md` (1차: 역할·준환 성향)
3. `docs/MASTER_HANDOFF_2.md` (규칙·인프라·검증 레시피)
4. **`docs/MASTER_HANDOFF_3.md` (가장 최신: 현재 상태, 결정사항, 미병합 브랜치, 할 일, 서브 세션 재시작 브리프)**
5. `docs/MASTER_STATE.md`, `docs/PROGRESS.md`, `docs/ISSUE_RECONCILE_2026-11-18.md`, 필요 시 `docs/SPEC.md`·`docs/DECISIONS.md`·`docs/WAITING_ISLAND_SPEC.md`

읽고 나서 **오늘 할 일을 한 줄**로 준환에게 확인하고 시작한다.

## 핵심 요약
- main=`663a3ce`(푸시됨). 서버 r3·클라이언트 r3-3c6ff5a·런처 0.1.7·백엔드 00f1972 배포됨.
- main에 없는 작업: 열쇠 규칙(`claude/intelligent-leavitt-8d85a9` f2211c0), 봇 B7·B8(`claude/bot-slices-3072b8` 2a616ec), 대기 섬(`claude/island-impl` 87e35b0). 통합 r5로 한 번에 병합·검증한다.
- 봇 측정(쉬움·어려움·스쿼드)이 약 11:50에 끝난다. 그때까지 해당 PC에서 다른 Unity 컴파일을 돌리지 않는다.
- 열린 이슈 100개. 구현된 것은 근거와 함께 `completed`, 필요 없는 것은 사유와 함께 `not_planned`(일괄 종료 금지). 사람 확인 항목은 체크리스트로.

## 지금 자동으로 돌고 있는 작업 (이어받을 것, 자세한 건 MASTER_HANDOFF_3 §1-B)
- **대기 섬 병합 파이프라인**(워크트리 `.claude/worktrees/quizzical-swanson-bcf35d`, 브랜치 `claude/island-impl`): 봇 측정(약 11:50 종료)이 끝난 뒤 자동으로 생성기 5개 → EditMode 전체 → 대기 섬 PlayMode를 돌고, 통과하면 #413~#419를 닫는다. 예상 완료 13:10~13:30. 이 결과를 확인하고 **main에 `--ff-only` 병합 → 푸시**하는 것이 준환이 지시한 마스터의 몫이다(#420은 열어 둠). 결과는 그 워크트리의 `Logs/test-editmode.xml`과 이슈 코멘트로 확인한다.
- **봇 측정**(`bot-slices-3072b8` 워크트리 `Logs/botstats`, 약 11:50 종료): 끝나면 판정해 #375~377을 닫고 통합 r5에 포함.
- 11:50 이후 Unity 슬롯 1개가 비면 열쇠 규칙 마무리 → 달리기 애니메이션 → 차량 남은 항목 순으로 새 서브 세션을 시작(브리프는 MASTER_HANDOFF_3 §7).
- 준환이 아직 답하지 않은 질문(칩 부활 #269, 대기 섬 외형 방향 등)은 §3.

## 지켜야 할 것
- 비밀 값 금지(`/Volumes/WorkSpace/Personal/Key/`는 읽지 않는다. 서버는 `ssh-ubuntu.command`를 읽지 말고 실행만).
- Unity 동시 실행 최대 2개(서로 다른 워크트리), 슬롯 락 `mkdir /private/tmp/sublevel-unity-slot1 || mkdir …slot2`. 측정은 독점 창.
- 새 워크트리에는 ignore 자산(AssetStaging, Characters FBX·.meta, Library)을 `cp -c`로 복사. 룰 변경은 ADR 먼저. 라이선스는 CC0/상업 가능.
- **보고는 증거로**: 캡처는 파일 시각을 확인하고 직접 열어 판단, 테스트 수치·커밋 해시·공개 URL 응답을 인용, 서브 세션의 말을 그대로 옮기지 않는다.
- 위임 범위: 사이트·런처·백엔드 커밋·푸시·배포, 워크트리 브랜치 커밋, 통합 병합·main 푸시. 그 밖의 푸시·삭제·유료·계정 생성은 한 줄로 묻는다.

## 처음 할 일 (순서)
1. `date`, `git worktree list`, `git log origin/main -3`, `gh issue list --state open --limit 300`, `ps -axo pid,etime,command | grep -E 'Unity|SublevelServer'`로 현황 파악.
2. 봇 측정 결과: `.claude/worktrees/bot-slices-3072b8/Logs/botstats/*_all.csv`. 끝났으면 판정(#375~377), 미달이면 튜닝.
3. 통합 r5 시작(MASTER_HANDOFF_3 §4-3). 열쇠 규칙 캡처·PlayMode 먼저 끝내거나 병행.
4. 준환에게 §3의 미답변 질문(칩 부활 #269, 대기 섬 외형 방향 등)을 한 줄씩.
5. 서브 세션은 MASTER_HANDOFF_3 §7의 브리프로 새로 시작(이전 세션 ID는 쓰지 못한다). 한 번에 5개 안팎, 사용량 한도에 주의.

--- 끝 ---

# 봇·네트워크 트랙 인수인계 (2026-11-16, master → 새 트랙 세션)

- 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/botsnet`, 브랜치 `track/botsnet`. 이 폴더 밖(다른 워크트리·main)은 건드리지 않는다.
- 먼저 읽기: `CLAUDE.md` → `docs/MASTER_STATE.md` → `docs/tracks.md`(공통 완성 정의 7항·병합 절차) → 이 문서 → `docs/SESSION_PLAN.md`의 해당 세션 표 → `docs/DECISIONS.md` ADR-165~203 → 아래 근거 문서.
- 이전 세션: S8 `local_f7af4a9d-b5b1-4440-87fa-8d9a6a07856a`(s8-bots-net). 맥락이 필요하면 `mcp__ccd_session_mgmt__search_session_transcripts`·`list_events`로 읽기만 한다(메시지 금지).
- 소유 파일: `AI/*`, `Net/*`(관심영역·구조), bots·network.json, DEDICATED_SERVER.md
- ADR 번호 대역: ADR-240~244 (그 밖 번호 금지).
- Unity: 처음엔 순수 C#·문서만, 측정은 master 순번.

## 현재 상태와 근거 문서
스펙·티켓 #324~#340, `-perfcapture` v2 설계(.txt 초안 위치는 이전 세션 보고와 `docs/research/` 확인), LagComp 결정화 패치 `S8_lagcomp_test_patch.txt`(LagCompPlayTests 2건 플레이크, 3회 중 1회 통과). 목표: 판당 50인(사람+봇) 파라미터화·관심영역(문·유리·루팅 필터)·봇 AI LOD·봇 행동(오염 예고 인지·열쇠)·NavMesh 연결성·측정 자동화(드로우콜·메모리·지역별). 저지력(StaggerState) 서버 배선은 총기 트랙이 소유 밖이라 넘긴 것 — `Combatant`에 StaggerState 적용(SpeedFactor·ShakeDeg)을 이 트랙이 한다(총기 브랜치 `track/guns`의 `StaggerState.Hit` 참고).

## 이 트랙의 규칙
- 판정은 수치 + 게임 안 캡처 + 테스트. 이미지 단독 금지. dotnet 하네스 통과로 끝내지 말고 **Unity EditMode로** 확인. 소리 나는 PlayMode는 하지 않는다(필요하면 master에 요청).
- 이 워크트리에서는 컴파일되는 상태로만 커밋한다. 생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain)은 병합 대상이 아니다(master가 재생성).
- Unity 실행 뒤 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제. 동시 Unity 실행은 2~3개 상한이라 master가 순번을 준다(SendMessage로 요청).
- 다운로드·결제·계정·약관 동의는 하지 않는다(필요하면 master에 목록으로 요청: 파일명·출처·용량).
- push 금지. commit은 이 브랜치에 WP 단위로 해도 된다(메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`). 새 아이디어는 BACKLOG.md 한 줄.
- 완료하면 `docs/tracks/botsnet_DONE.md`(한 것/못 한 것/확인·추정)를 쓰고 master에 보고. 보고는 한국어 간결.

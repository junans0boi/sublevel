# 비인게임 UI 트랙 인수인계 (2026-11-16, master → 새 트랙 세션)

- 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/ui`, 브랜치 `track/ui`. 이 폴더 밖(다른 워크트리·main)은 건드리지 않는다.
- 먼저 읽기: `CLAUDE.md` → `docs/MASTER_STATE.md` → `docs/tracks.md`(공통 완성 정의 7항·병합 절차) → 이 문서 → `docs/SESSION_PLAN.md`의 해당 세션 표 → `docs/DECISIONS.md` ADR-165~203 → 아래 근거 문서.
- 이전 세션: 신규(S9 미시작). 맥락이 필요하면 `mcp__ccd_session_mgmt__search_session_transcripts`·`list_events`로 읽기만 한다(메시지 금지).
- 소유 파일: `LobbyScreen`, `MainMenuBuilder`, `CreditsScreen`, `GameSettings`, ui.json, `docs/research/ui_mockups/`
- ADR 번호 대역: ADR-245~249 (그 밖 번호 금지).
- Unity: 처음엔 설계·시안 정합·순수 데이터 위주, Unity 구현은 master 순번(UI는 EditMode+캡처로 확인).

## 현재 상태와 근거 문서
확정 시안 `docs/research/ui_mockups/adopted.html`(로비 2 + style B), `UI_SPEC.md`, `docs/bible/data/ui.json`. 목표: 메인 메뉴·로비(솔로/스쿼드)·방 만들기·설정·크레딧·결과 화면 Unity 구현, 인게임 HUD(오염 경고·열쇠·칩) 최소 추가, 해상도별 캡처, 한국어 텍스트. 캐릭터·차량 크레딧 문구는 `character_DONE.md`와 ledger를 참고(Credits.cs는 CC-BY 전용).

## 이 트랙의 규칙
- 판정은 수치 + 게임 안 캡처 + 테스트. 이미지 단독 금지. dotnet 하네스 통과로 끝내지 말고 **Unity EditMode로** 확인. 소리 나는 PlayMode는 하지 않는다(필요하면 master에 요청).
- 이 워크트리에서는 컴파일되는 상태로만 커밋한다. 생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain)은 병합 대상이 아니다(master가 재생성).
- Unity 실행 뒤 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제. 동시 Unity 실행은 2~3개 상한이라 master가 순번을 준다(SendMessage로 요청).
- 다운로드·결제·계정·약관 동의는 하지 않는다(필요하면 master에 목록으로 요청: 파일명·출처·용량).
- push 금지. commit은 이 브랜치에 WP 단위로 해도 된다(메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`). 새 아이디어는 BACKLOG.md 한 줄.
- 완료하면 `docs/tracks/ui_DONE.md`(한 것/못 한 것/확인·추정)를 쓰고 master에 보고. 보고는 한국어 간결.

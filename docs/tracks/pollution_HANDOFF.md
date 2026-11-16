# 오염·열쇠·칩 트랙 인수인계 (2026-11-16, master → 새 트랙 세션)

- 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/pollution`, 브랜치 `track/pollution`. 이 폴더 밖(다른 워크트리·main)은 건드리지 않는다.
- 먼저 읽기: `CLAUDE.md` → `docs/MASTER_STATE.md` → `docs/tracks.md`(공통 완성 정의 7항·병합 절차) → 이 문서 → `docs/SESSION_PLAN.md`의 해당 세션 표 → `docs/DECISIONS.md` ADR-165~203 → 아래 근거 문서.
- 이전 세션: S6 `local_aef496c8-11c7-4fe1-9f90-37be86bb45e7`(s6-pollution). 맥락이 필요하면 `mcp__ccd_session_mgmt__search_session_transcripts`·`list_events`로 읽기만 한다(메시지 금지).
- 소유 파일: 오염 코어(Core 신규), `ElevatorTravel`·열쇠·칩·팀 규칙, pollution·match.json, 관련 테스트
- ADR 번호 대역: ADR-235~239 (그 밖 번호 금지).
- Unity: 처음엔 **순수 C#·문서만**(Unity는 master 순번). 순수 코어 Unity EditMode 확인은 순번을 요청해 받는다.

## 현재 상태와 근거 문서
순수 C# 코어와 테스트는 완료(Unity 통과). 설계 `docs/research/s6_pollution_key_chip_spec.md`, 맵과의 형식 합의 `s6_map_data_format.md`(맵 트랙 `track/map`이 IslandGenerator로 지역 그래프를 만들었다 — 참고용으로 `git show track/map:...`만, 병합 전엔 직접 의존하지 말 것). 남은 것: 서버 연결·복제, 연출(붉은 안개·가이거·지도 점멸), 방독, 열쇠 상태 머신(WP7), 칩 부활(WP8), 최소 HUD(ui.json 항목), 시뮬 대조(평균 22.1분, 이탈률 25/28%).

## 이 트랙의 규칙
- 판정은 수치 + 게임 안 캡처 + 테스트. 이미지 단독 금지. dotnet 하네스 통과로 끝내지 말고 **Unity EditMode로** 확인. 소리 나는 PlayMode는 하지 않는다(필요하면 master에 요청).
- 이 워크트리에서는 컴파일되는 상태로만 커밋한다. 생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain)은 병합 대상이 아니다(master가 재생성).
- Unity 실행 뒤 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제. 동시 Unity 실행은 2~3개 상한이라 master가 순번을 준다(SendMessage로 요청).
- 다운로드·결제·계정·약관 동의는 하지 않는다(필요하면 master에 목록으로 요청: 파일명·출처·용량).
- push 금지. commit은 이 브랜치에 WP 단위로 해도 된다(메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`). 새 아이디어는 BACKLOG.md 한 줄.
- 완료하면 `docs/tracks/pollution_DONE.md`(한 것/못 한 것/확인·추정)를 쓰고 master에 보고. 보고는 한국어 간결.

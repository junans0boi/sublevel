# 맵 트랙 인수인계 (2026-11-16, master → 새 트랙 세션)

- 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/map`, 브랜치 `track/map`, 기준 `2865778`. 이 폴더 밖(다른 워크트리·main)은 건드리지 않는다.
- 먼저 읽기: `CLAUDE.md` → `docs/MASTER_STATE.md` → `docs/tracks.md`(공통 완성 정의 7항·병합 절차) → 이 문서 → `docs/SESSION_PLAN.md`의 해당 세션 표 → `docs/DECISIONS.md` ADR-165~203.
- 이전 세션: S1 `local_c3aeeaa9-f001-4eb8-9024-9d4f19176966`(s1-map) (맥락이 필요하면 `mcp__ccd_session_mgmt__search_session_transcripts`·`list_events`로 읽기만 한다. 메시지를 보내지 않는다.)
- 소유 파일: `Core/World/*`, `Editor/WorldBuilder*`, `MapBaker`, `WorldMeshes`, `WorldVegetation`, map·towns·roads·terrain.json, 관련 EditMode 테스트
- ADR 번호 대역: ADR-204~209 (그 밖 번호 금지).

## 현재 상태 (MASTER_STATE §4 기준, 확인한 것은 이전 세션 보고)
A1 측정, #240(.bytes+LFS), 규칙 데이터화, BuildingKind 13종, 루팅 가중, 표면 태그, Ridge 연결, 도로 코어, 상가·빌라·아파트 생성기까지 완료(커밋됨). 건물 외관 형상·학교 생성기는 이전 세션의 `.txt` 초안 상태이며 main에서는 커밋돼 있지 않을 수 있으니 `docs/research/11_s1_map_plan.md`와 초안 파일 위치를 확인한다. 게임은 아직 신맵(청람도)이 아니라 옛 1024m 월드(지역 4, 건물 73)에 새 코어가 들어간 중간 상태다. 열린 위험: 411채 메모리·용량(플레이어 빌드 재측정), 건물 외관 품질(상자 느낌). 완성 정의는 tracks.md 맵 행.

## 이 트랙의 규칙
- 판정은 수치 + 게임 안 캡처 + 테스트. 이미지 단독 금지. dotnet 하네스 통과로 끝내지 말고 **Unity EditMode로** 확인. 소리 나는 PlayMode는 하지 않는다(필요하면 master에 요청).
- 컴파일 안 되는 초안은 `.txt`로 두지 말고 이 워크트리 안에서는 브랜치에 커밋 가능한 완성 상태로 둔다(워크트리가 분리돼 있어 다른 트랙에 영향 없음). 단 승인 없이 `git push` 금지.
- 생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain asset)은 병합 대상이 아니다. 병합 뒤 master가 재생성한다.
- Unity 실행: `tools/unity.sh`(이 워크트리 안). 끝나면 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제. 동시 Unity 실행은 2~3개가 상한이라 무거운 작업은 순차.
- 첫 Unity 실행은 Library APFS 클론에서 시작한다. 재임포트에 걸린 시간을 보고에 적는다.
- 새 아이디어는 BACKLOG.md 한 줄. 범위 밖 작업 금지. commit은 이 브랜치에 WP 단위로 해도 된다(준환 위임: master가 병합 전 검토). 메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- 완료하면 `docs/tracks/map_DONE.md`(한 것/못 한 것/확인·추정)를 쓰고 master에 보고한다. 보고는 한국어 간결.

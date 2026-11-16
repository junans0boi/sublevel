# 차량 트랙 인수인계 (2026-11-16, master → 새 트랙 세션)

- 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/vehicles`, 브랜치 `track/vehicles`, 기준 `2865778`. 이 폴더 밖(다른 워크트리·main)은 건드리지 않는다.
- 먼저 읽기: `CLAUDE.md` → `docs/MASTER_STATE.md` → `docs/tracks.md`(공통 완성 정의 7항·병합 절차) → 이 문서 → `docs/SESSION_PLAN.md`의 해당 세션 표 → `docs/DECISIONS.md` ADR-165~203.
- 이전 세션: S10 `local_745bdb3f-4cd9-45e6-b682-514561785614`(s10-vehicles) (맥락이 필요하면 `mcp__ccd_session_mgmt__search_session_transcripts`·`list_events`로 읽기만 한다. 메시지를 보내지 않는다.)
- 소유 파일: `Editor/VehicleBuilder`·`VehicleArt`·`VehicleModels`, `docs/VEHICLE_SPEC.md`, vehicles.json
- ADR 번호 대역: ADR-220~224 (그 밖 번호 금지).

## 현재 상태 (MASTER_STATE §4 기준, 확인한 것은 이전 세션 보고)
스펙·ADR-198~203·이슈 #342~#350. 폐차 소품은 의도된 것(바운드 0.19m 묻힘)이며 접지 패치는 `.txt` 초안(ADR-201). 신맵 배치(ADR-198)는 맵 트랙 완성 뒤 맞춘다. 차량 소리 PlayMode 테스트 4건은 단독 통과(긴 실행 환경 문제 추정, ADR-203). 완성 정의는 tracks.md 차량 행. 소리 나는 PlayMode는 master 요청 없이는 돌리지 않는다.

## 이 트랙의 규칙
- 판정은 수치 + 게임 안 캡처 + 테스트. 이미지 단독 금지. dotnet 하네스 통과로 끝내지 말고 **Unity EditMode로** 확인. 소리 나는 PlayMode는 하지 않는다(필요하면 master에 요청).
- 컴파일 안 되는 초안은 `.txt`로 두지 말고 이 워크트리 안에서는 브랜치에 커밋 가능한 완성 상태로 둔다(워크트리가 분리돼 있어 다른 트랙에 영향 없음). 단 승인 없이 `git push` 금지.
- 생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain asset)은 병합 대상이 아니다. 병합 뒤 master가 재생성한다.
- Unity 실행: `tools/unity.sh`(이 워크트리 안). 끝나면 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제. 동시 Unity 실행은 2~3개가 상한이라 무거운 작업은 순차.
- 첫 Unity 실행은 Library APFS 클론에서 시작한다. 재임포트에 걸린 시간을 보고에 적는다.
- 새 아이디어는 BACKLOG.md 한 줄. 범위 밖 작업 금지. commit은 이 브랜치에 WP 단위로 해도 된다(준환 위임: master가 병합 전 검토). 메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- 완료하면 `docs/tracks/vehicles_DONE.md`(한 것/못 한 것/확인·추정)를 쓰고 master에 보고한다. 보고는 한국어 간결.

# 캐릭터 트랙 인수인계 (2026-11-16, master → 새 트랙 세션)

- 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/character`, 브랜치 `track/character`, 기준 `2865778`. 이 폴더 밖(다른 워크트리·main)은 건드리지 않는다.
- 먼저 읽기: `CLAUDE.md` → `docs/MASTER_STATE.md` → `docs/tracks.md`(공통 완성 정의 7항·병합 절차) → 이 문서 → `docs/SESSION_PLAN.md`의 해당 세션 표 → `docs/DECISIONS.md` ADR-165~203.
- 이전 세션: S3 `local_6931d230-2672-40e9-980d-88b6180c6ae7`(s3-character) (맥락이 필요하면 `mcp__ccd_session_mgmt__search_session_transcripts`·`list_events`로 읽기만 한다. 메시지를 보내지 않는다.)
- 소유 파일: `Editor/CharacterKit`·`CharacterImport`, characters.json, 캐릭터 모델·프리팹, `sublevel_staging/`(저장소 밖)
- ADR 번호 대역: ADR-225~229 (그 밖 번호 금지).

## 현재 상태 (MASTER_STATE §4 기준, 확인한 것은 이전 세션 보고)
시험 Swat이 Humanoid로 인식(본 55), ADR-192·197. FBX는 `CharacterImport.SyncStaging`으로 `AssetStaging/mixamo/`에서 복원(**git 무시 `Roster_*.fbx`는 이 워크트리에 없으니 먼저 SyncStaging 실행**). 로스터 미구현. 애니 트랙은 이 트랙이 병합된 뒤 시작하므로 인터페이스는 `docs/research/16_s3_s4_interface.md`를 따른다. 완성 정의는 tracks.md 캐릭터 행. Mixamo 약관·비공개 저장소 조항 때문에 FBX 커밋 금지.

## 이 트랙의 규칙
- 판정은 수치 + 게임 안 캡처 + 테스트. 이미지 단독 금지. dotnet 하네스 통과로 끝내지 말고 **Unity EditMode로** 확인. 소리 나는 PlayMode는 하지 않는다(필요하면 master에 요청).
- 컴파일 안 되는 초안은 `.txt`로 두지 말고 이 워크트리 안에서는 브랜치에 커밋 가능한 완성 상태로 둔다(워크트리가 분리돼 있어 다른 트랙에 영향 없음). 단 승인 없이 `git push` 금지.
- 생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain asset)은 병합 대상이 아니다. 병합 뒤 master가 재생성한다.
- Unity 실행: `tools/unity.sh`(이 워크트리 안). 끝나면 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제. 동시 Unity 실행은 2~3개가 상한이라 무거운 작업은 순차.
- 첫 Unity 실행은 Library APFS 클론에서 시작한다. 재임포트에 걸린 시간을 보고에 적는다.
- 새 아이디어는 BACKLOG.md 한 줄. 범위 밖 작업 금지. commit은 이 브랜치에 WP 단위로 해도 된다(준환 위임: master가 병합 전 검토). 메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- 완료하면 `docs/tracks/character_DONE.md`(한 것/못 한 것/확인·추정)를 쓰고 master에 보고한다. 보고는 한국어 간결.

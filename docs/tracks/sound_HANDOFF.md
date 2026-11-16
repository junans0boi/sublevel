# 사운드 트랙 인수인계 (2026-11-16, master → 새 트랙 세션)

- 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/sound`, 브랜치 `track/sound`. 이 폴더 밖(다른 워크트리·main)은 건드리지 않는다.
- 먼저 읽기: `CLAUDE.md` → `docs/MASTER_STATE.md` → `docs/tracks.md`(공통 완성 정의 7항·병합 절차) → 이 문서 → `docs/SESSION_PLAN.md`의 해당 세션 표 → `docs/DECISIONS.md` ADR-165~203 → 아래 근거 문서.
- 이전 세션: S5 `local_86508b96-5ffa-45df-879e-e2484a586469`(sound-sheet). **준환이 이전 세션과 직접 협의했으므로 협의 내용(청취 결과·선호)을 먼저 이전 세션 기록에서 읽어 반영한다.**. 맥락이 필요하면 `mcp__ccd_session_mgmt__search_session_transcripts`·`list_events`로 읽기만 한다(메시지 금지).
- 소유 파일: `Gameplay/CombatantAudio`, `DoorAudio`, `ElevatorAudio`, `AmbientBeds`, `SoundRules`, audio.json, AUDIO.md
- ADR 번호 대역: ADR-250~254 (그 밖 번호 금지).
- Unity: 처음엔 오디오 편집·데이터·문서만(소리 나는 실행은 하지 않는다 — 준환 허락 때만).

## 현재 상태와 근거 문서
설계 `docs/research/13_sound_wiring_design.md`, 후보 `09_sound_candidates.md`, 다운로드 체크 `10_sound_download_checklist.md`, ledger 초안 `14_sound_ledger_draft.md`, ledger S-0008~22(걸음 클립 14표면 반영, 미청취). 목표: 걸음 소리 품질, 총성·전투음(신규 총 21종: 총기 트랙이 모델만 했고 소리는 이 트랙), 차량 소리, 공간음향(층 차폐·벽 폐색), 크레딧(CC-BY는 Credits.cs). "사운드가 이상하다"는 준환의 지적 원인 점검. CC-BY 예외는 ADR-177 범위(Fr3yr 재장전 1건).

## 이 트랙의 규칙
- 판정은 수치 + 게임 안 캡처 + 테스트. 이미지 단독 금지. dotnet 하네스 통과로 끝내지 말고 **Unity EditMode로** 확인. 소리 나는 PlayMode는 하지 않는다(필요하면 master에 요청).
- 이 워크트리에서는 컴파일되는 상태로만 커밋한다. 생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain)은 병합 대상이 아니다(master가 재생성).
- Unity 실행 뒤 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제. 동시 Unity 실행은 2~3개 상한이라 master가 순번을 준다(SendMessage로 요청).
- 다운로드·결제·계정·약관 동의는 하지 않는다(필요하면 master에 목록으로 요청: 파일명·출처·용량).
- push 금지. commit은 이 브랜치에 WP 단위로 해도 된다(메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`). 새 아이디어는 BACKLOG.md 한 줄.
- 완료하면 `docs/tracks/sound_DONE.md`(한 것/못 한 것/확인·추정)를 쓰고 master에 보고. 보고는 한국어 간결.

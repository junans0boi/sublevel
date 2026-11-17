# MASTER_HANDOFF.md — 새 master 세션 인수인계 (2026-11-17 10시 기준)

> 새 세션은 CLAUDE.md → 이 파일 → `docs/tracks.md` → `docs/DECISIONS.md` 순으로 읽는다. 이 문서는 master가 직접 확인한 것과 추정을 구분해 적었다.
> 보고는 한국어·간결하게, 판정은 수치+게임 안 캡처+테스트(이미지 단독 금지), commit·push·패키지·구조 변경·다운로드·결제·약관은 준환에게 먼저 묻는다(준환이 "나에게 묻지 말고 알아서 해"라고 한 뒤로 병합·commit·빌드는 master가 처리, **push는 한 번도 안 했고 계속 금지**).

## 1. 지금 위치 (확인)
- main = `9808e69`. 10개 트랙(map, materials, guns, vehicles, character, animation, pollution, botsnet, ui, sound)이 전부 main에 병합됨. 병합 안 된 commit 0 (animation만 아래 회귀 수정 작업 중).
- EditMode 1408/1408 통과(빌드 5 직전). 월드 재생성 결정성 확인(해시 동일). mac 빌드 5 성공: `Build/play5/Sublevel.app`.
- 작업 트리에 **커밋 안 한 재생성물**이 있다(프리팹 M1·Vehicles·World 재질·World.unity·World_Meshes.bytes·NavMesh·Terrain·MainMenu.unity). 정리해서 한 번에 commit할 것.

## 2. 최우선 미해결: 빌드 5 회귀 (준환 플레이 결과)
- 증상: 다리·상체는 정상인데 ① 허리가 1cm처럼 가늘다 ② 시작할 때 뒤를 보고 서 있다 ③ 걸을 때도 반대를 본다 ④ 펀치 때 허리가 반대로 돌아가 앞으로 펀치가 나간다.
- 확인: 직전 병합(2a18ab0, 발 접지 재작성)의 `Gait.cs`는 다리·골반 높이만 다뤄 이 증상을 설명하지 못한다. `Character.controller` 변경은 줄 순서만 바뀐 것이라 원인 아님.
- 추정: 클립 임포트(`keepOriginalOrientation`)·UAL1 import meta 훼손·펀치 때 Torso 회전 부호. t-animation이 7f4dc20↔HEAD 이분(bisect)으로 조사 중(워크트리 `sublevel-wt/animation`, 수정 파일 47개, commit 없음, 마지막 확인 시점 `UAL1_Standard.fbx.meta`·`CharacterAnimClips.cs` 수정 중).
- 병합 시 주의: UAL1 meta는 전에 훼손→복원된 적 있다(`18d3270`). 병합 전 `git diff`로 meta 확인. 프리팹·`ProjectSettings/AudioManager.asset`은 `git restore`, `Assets/InitTestScene*` 삭제.
- 완료되면: 병합 → TestBoxBuilder.Build → EditMode → mac 빌드 → `Build/play6` → 준환에게 전달.

## 2-1. 빌드 5(`Build/play5`) 테스트 현황
- 준환이 확인한 것(플레이 결과): 다리·상체 정상, 위 ①~④ 회귀 4건. "바닥에 박혀 이동"이 사라졌는지는 **명시적으로 답하지 않았다**(수치상으로만 해소: 평지 서기 오차 0.004m, 걷기 골반 내림 0.080m 일정, 발 가라앉음 ≤2cm, 15모델 전부. 월드 Terrain 위 직접 측정은 아직 없음).
- 아직 준환이 보지 못한 것(빌드 4에서 넘어온 확인표): ① 파밍(건물 안 총·아이템, 옥상) ② 미니맵 선명도 ③ 펀치 연출·피격 표시 ④ 걷기 중 견착 ⑤ 스페이스 브레이크·드리프트 ⑥ 80인 ⑦ 3인칭 운전 카메라 ⑧ 1인칭 앞유리 시야 ⑨ 차량 14+오토바이 6 보임.
- 자동 검증(빌드 5 직전, master 확인): EditMode 1408/1408, `TestBoxBuilder.Build` rc=0, mac 빌드 Success. `-captureall` 캡처는 빌드 5에서는 돌리지 않았다(빌드 4에서만 로비 캡처 확인).
- 빌드 6에서는 위 회귀 4건 + 확인표 9건 + 박힘 해소를 한 번에 확인하게 한다. 결과는 항목 번호로 받으면 해당 트랙에 바로 넘길 수 있다.

## 3. 세션 구성 (새 세션에서 다시 만들어야 할 것)
워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/<track>`, 브랜치 `track/<track>`. 트랙별 인수인계는 `docs/tracks/<track>_HANDOFF.md`(할 일)와 `_DONE.md`(한 일). 기존 세션 ID(참고용, 새로 만들면 무효):

| 트랙 | 기존 세션 | 상태 | 비고 |
|---|---|---|---|
| animation | local_1c61563b | **작업 중**(회귀 조사) | 발 접지 수정 병합됨, 위 §2 |
| botsnet | local_7deedb59 | 멈춤 | 열린 이슈 17개(T4~T14) |
| map | local_f0112d9f | 멈춤 | 열린 11개 |
| vehicles | local_381df271 | 멈춤 | 열린 9개(S10 T1~T10) |
| guns | local_3462f712 | 멈춤 | 열린 10개 |
| ui | local_7eeeefec | 멈춤 | 장비 모양 개선 등 |
| character | local_a99178c3 | 멈춤 | 열린 11개 |
| pollution | local_e6c89aa5 | 멈춤 | 열린 10개 |
| sound | local_8103da28 | 멈춤 | 열린 14개, 준환 청취 미완 |
| materials | local_7415027e | 멈춤 | 열린 13개 |
- 새 트랙 세션은 해당 워크트리 cwd에서 시작하고 먼저 `git merge main`으로 갱신, 그 트랙의 `_HANDOFF.md`를 읽게 한다. Unity는 동시에 최대 2~3개(RAM 24GB), 소리 나는 PlayMode는 준환 허락 때만.
- 트랙 ADR 대역(충돌 방지): 맵 204–209, 재질 210–214, 총기 215–219, 차량 220–224, 캐릭터 225–229, 사운드 250–254, 애니 255–259 + 이후 개별 번호. **사용 중 최대 ADR-262**(다음은 263).

## 4. 끝난 것 (확인)
- 10트랙 병합, 청람도 신맵(6지역·17마을·411건물·20역×4엘리베이터=80슬롯), 80인 판(≤64명+봇 ≥16, ADR-259), 매칭 큐(솔로·스쿼드, 대기창 30초, 봇 채움), 차량 14대+오토바이 6대, 로스터 15모델(Mixamo+UBC), 로비 3D 엘리베이터+실제 장착 캐릭터(준환이 "마음에 든다" 확인), 루팅 재분배·옥상 루팅(ADR-255/256), 미니맵 4096px, 근접·조준·차량 카메라 수정, 발 접지 재작성(ADR-262, 평지 박힘 수치상 해소).
- 라이선스: AssetValidator 0 problems, UAL1(CC0) LICENSE/ledger 정리.

## 5. 못한 것·미확인
- **준환이 직접 확인하지 못한 항목**(빌드 4/5): 파밍·옥상 루팅, 미니맵 선명도, 펀치 연출, 걷기 중 견착 문제, 브레이크·드리프트, 3·1인칭 운전 카메라, 차량이 월드에 보이는지, 80인.
- PERF: 로비 p99 약 10.6~11.5ms(목표 ≤10ms, 에디터 측정, 플레이어 빌드 조용한 머신 재측정 필요 `-perflobby <절대경로>`), 서버 80인 p99 재측정(P0/P3).
- 로비→전용 서버 매칭 왕복 미테스트, 파티원 묶기(리더만 등록), 결과 화면(오염 데이터 대기).
- 봇: 팀·칩 임무(T9), 오염 구역 목표·방독 사용(T7), 도시 관심 지점(T10), 열쇠 삽입 표본 2건뿐.
- 사운드를 직접 들어본 적 없음, IL2CPP 재빌드 미검증.
- 계단·경사 걷기에서 스윙 발이 모서리를 5~17cm 파고듦(BACKLOG: 스윙 발 클리어런스 IK). 월드 Terrain 위 직접 측정은 못 함(TestBox 합성 지형만).
- 열린 GitHub 이슈 144개(S1~S10 + 번호 없는 36개). 우선순위는 제목으로만 추정했다.

## 5-1. 준환이 알려준 규칙·선호 (메모리에도 있음)
- 로비 UI는 목업 그대로 복사 금지: 실제 3D 엘리베이터 + 실제 장착 캐릭터, 주황 단일 포인트 팔레트.
- 판정은 엄격하게, 전체 캡처+코드를 먼저 진단하고 한 번에 고친다(batch-diagnose-then-fix).
- 에셋 라운드(Mixamo·에셋스토어)는 준환이 로그인한 새 세션에서, 재배포 라이선스를 확인하고 후보를 제안한 뒤 준환이 고른다.
- 준환이 플레이 중인 게임 프로세스는 절대 죽이지 않는다(빌드 폴더 `Build/play*`). 한 번 죽여서 사과한 적 있음.
- 정기 보고(5분 간격)는 세션 한정 cron이라 새 세션에서는 다시 걸어야 한다. 새 변화가 없으면 한 줄로 "변화 없음".

## 6. 파이프라인 요약
- Unity: `tools/unity.sh {run|run-gfx|test|build}`. 렌더링이 필요한 썸네일·씬 생성은 `run-gfx`(nographics는 빈 이미지).
- 병합 후 절차: 병합 → `WorldBuilder.Build` 두 번(`World_Meshes.bytes` 해시 비교) → `MainMenuBuilder.Build`(run-gfx) → `TestBoxBuilder.Build` → EditMode → `build mac` → `Build/playN` 복사 → `-captureall <dir>`로 캡처.
- Unity가 git 무시된 FBX 때문에 orphan `.meta`를 지울 수 있다. 병합 후 `git diff --diff-filter=D`로 삭제된 meta 확인(`CharacterImport.SyncStaging`).
- 로그: `~/Library/Logs/STEADY TO VIVID STUDIO/Sublevel/Player.log`.

## 7. 새 master의 첫 행동 (제안)
1. 이 문서와 `docs/tracks.md`, `DECISIONS.md`(ADR 262까지) 읽기.
2. `git -C sublevel-wt/animation log main..HEAD`와 status로 animation 회귀 수정 상태 확인(commit이 있으면 §2 절차로 병합, 없으면 animation 세션 재개/재생성).
3. 준환이 빌드 5에서 확인하지 못한 항목을 §5 목록으로 다시 요청.

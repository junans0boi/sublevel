# MASTER_STATE — 새 master 세션 인수인계

## 최신 상태 — 2026-11-18 15:42 (Codex)

- 후속 사용자 지시로 Claude 대화 12개·현행 기획을 분석하고 구형 기획 이슈 7개(#55 #250 #251 #296 #324 #336 #338)를 `not_planned`로 종료했다. 현재 OPEN 136개, 대기섬 13개는 보류 유지(나머지 123개). 후속 이슈에 유효 요구 보존, 상세는 `docs/ISSUE_RECONCILE_2026-11-18.md` 3차 점검.
- 주의: 런처는 공개 0.1.6(b7db8d6)이며 `sg_` API 토큰을 준다. main LoginEnv는 서명 토큰만 처리한다. 원본 mp-client worktree의 토큰 교환·Social/Ticket 코드와 bot worktree B7은 미커밋으로 남아 있다. 이번 분석에서 통합·빌드·배포는 하지 않았다.
- 앞선 지시로 병합·빌드 후 중단했고, 이후 위의 분석·기획 이슈 정리만 재개했다. 다음 구현 작업·push·배포는 미수행.
- 로컬 `main` = `731eb29`(`integration/r2` ff-only 병합), `origin/main`보다 16커밋 앞섬. 통합 브랜치의 `.unity` LFS 추적 제거를 승인 범위에서 복구했고 main 조상 이력은 유지했다.
- WorldBuilder → CharacterImport.SyncStaging → CharacterAnimClips.Reimport → TestBoxBuilder → MainMenuBuilder 순차 성공. 전체 EditMode **1510/1510**, `Logs/test-editmode.xml`(2026-11-18 06:31:49Z~06:36:03Z).
- 클라이언트 빌드: `Build/mac/Sublevel.app` **Succeeded 3493 MB**, `Build/win/Sublevel.exe` **Succeeded 952 MB**. 로그 `Logs/build-mac.log`, `Logs/build-win.log`. 빌드 후 Unity 프로세스 없음. 플레이 실기 확인은 하지 않음.
- 생성된 씬·프리팹·월드 자산과 문서 변경은 미커밋. 기존 미커밋 자료는 `/tmp/sublevel-main-premerge-20261118.tar.gz`에 백업했고 보존했다. 통합 워크트리의 이전 생성물은 `stash@{0}`(`pre-lfs-migration integration-r2 generated outputs`)에 보존.
- #424: 파티 크기 전달 경로는 추가했으나 구성원과 매치 참가자의 안정적인 매핑은 남음. #345·#347·#349·#375·#389도 열린 상태로 유지. 로비 Protocol=2이므로 서버 갱신 전 기존 서버와 호환되지 않을 수 있음.
- 이어갈 때 `docs/MASTER_HANDOFF_2.md`와 이 최신 절을 우선한다. 아래는 11월 16일 과거 기록이며 현재 상태가 아니다.

## 이전 인수인계 — 2026-11-16

> 새 master는 이 문서 → `CLAUDE.md` → `docs/MASTER_BRIEF.md` → `docs/SESSION_PLAN.md` → `docs/SPEC.md` → `docs/DECISIONS.md`(ADR-165~203) → `docs/bible/` 순으로 읽는다. 충돌하면 SPEC이 이기고, 룰 변경은 ADR이 먼저다.
> 준환(1인 개발자, 한국어, 간결·직접 보고 선호)이 "서브 세션이 뒤죽박죽이다"라고 판단해 **트랙별 git worktree로 분리 → 각 트랙을 끝까지 완성 → master가 병합**하는 방식으로 바꾸기로 했다.

## 1. 지금 위치 (확인한 것)
- 브랜치 `main`, 작업 트리 깨끗, HEAD = `46afb44`. **push는 한 번도 하지 않았다**(push는 준환 승인 필요). 이 세션의 로컬 commit: `8ee0fa4`(문서) `459531b` `b88d215` `7e6b26d` `c493ddd` `cba26bb` `617842e` `c16fb4c` `4a4f7bc` `46afb44`.
- EditMode 마지막 전체 실행: **1221/1221 통과**(재질·건물 반영 후). PlayMode `!Long`(HEAD c493ddd+S2 변경): 467건 중 409 통과, 6 실패 → `CoreLoopTests`는 수정·16/16 통과(`617842e`), 차량 소리 4건은 단독 실행 시 통과(긴 실행 환경 문제 추정), `LagCompPlayTests` 2건은 3회 중 1회만 통과(타이밍 의존 추정, 회귀 증거 없음; 결정화 패치는 S8 노트 `S8_lagcomp_test_patch.txt`).
- mac 빌드: `Build/mac/Sublevel.app`(18:45, c493ddd+S2 미커밋 상태) 존재. 이후 변경은 미반영. **밤하늘로 나오는 문제**(맑은 낮 조명 ADR-193 미적용)는 S2가 원인 확인했으나 이 문서 작성 시점에 미해결(S2 노트 확인).
- 게임은 **신맵(청람도)이 아니다**: 옛 1024m 월드(지역 4, 건물 73)에 새 `Ridge` 산악·재질·표면 소리·건물 생성기 코어가 들어간 중간 상태. 새 건물 종류는 월드에 아직 배치되지 않았다.

## 2. 확정·승인된 결정(준환)
- 판당 50명, 솔로/스쿼드(4), 신맵 C2(6지역·마을 17·건물 약 411), 하이브리드 오염, 열쇠 엘리베이터, 칩 부활, 일반 총기 16종(+보급 5), Humanoid 전환, Poly Haven 재질(ADR-168~176). 비인게임 UI 시안(`docs/research/ui_mockups/adopted.html`)은 확정, 구현은 슬라이스 이후.
- **게임 이름: SUBLEVEL(서브레벨) 확정**(상표 검색은 출시 전 사람).
- 승인: `BuildingKind` 5→13종 확장, World_Meshes `.bytes`+LFS(앞으로의 파일만), 패키지는 필요 시 개별 승인, Mixamo 시험 Swat 다운로드(`AssetStaging/mixamo/`), Poly Haven 7종 다운로드(`AssetStaging/polyhaven/`, 약 46MB), Freesound 약 63개(`AssetStaging/audio/`), CC-BY 예외(ADR-177, Fr3yr 재장전 1건), 세부 선택은 master 위임(재질·총기 모델·캐릭터는 추천안으로 **임시 확정**, 게임에서 보고 교체 가능).
- 슬라이스 우선: R1 청람읍권 + R0 북령 산악 먼저(건물 187채), 이후 나머지 4개 지역.
- **건물은 코드 조립 유지**(외부 팩 불사용; Asset Store EULA는 저장소 반입 조건 불명, HDRP 팩은 URP 비호환).

## 3. 이번 세션에서 배운 운영 규칙(어기면 같은 문제가 반복된다)
1. **dotnet 하네스 통과 ≠ Unity(Mono) 통과**: `RoadGraph`의 딕셔너리 열거 버그가 하네스에서만 통과했다. 순수 C#도 Unity EditMode로 한 번은 확인한다.
2. **다른 세션의 Unity 실행 중에는 `Assets/` 안 코드를 컴파일되는 완성 상태로만 둔다**(미완성은 `.txt` 초안). 워크트리 분리 후에도 한 워크트리 안에서는 동일.
3. **생성물**(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain asset)은 **병합하지 말고 병합 후 `WorldBuilder.Build`로 재생성**한다. `.bytes` 해시 불변이 결정성 기준이다.
4. 판정은 **수치 + 게임 안 캡처 + 테스트** 세 가지. 이미지 한 장으로 판정 금지. (내 첫 "폐차 소품" 판단은 맞았고 서브 세션의 반박이 틀렸다 — 이미지·코드 추정은 Unity 확인 전에 단정하지 않는다.)
5. 소리 나는 PlayMode는 준환이 소리를 듣는 중이면 금지(준환이 사운드를 서브 세션과 직접 협의했다). 빌드 후 측정·PlayMode는 준환이 허락한 때만.
6. 다운로드·"Add to My Assets"·가입·약관 동의는 준환 승인 필요(파일명·출처·용량·저장 위치 제시). 저장은 git 무시되는 `AssetStaging/`.
7. ADR 번호: **ADR-203까지 사용됨, 다음은 ADR-204**. 서브 세션마다 번호 대역을 미리 배정한다(충돌 방지).
8. commit/push: 로컬 commit은 이번 세션에서 master 판단으로 했고 push는 안 했다. 새 master는 준환에게 WP 단위로 commit 승인을 받는 것이 원칙(CLAUDE.md). 구조 변경(워크트리 폴더·.gitattributes)은 승인 필요.
9. `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`와 `Assets/InitTestScene*` 삭제를 Unity 실행 뒤 매번 한다(분류기가 서브 세션의 `git restore`를 막는 경우가 있어 master가 대신 한다).

## 4. 트랙(워크트리) 구성과 완성 정의
공통 완성 정의: ① 구현 + 테스트 우선(EditMode, 필요 시 PlayMode) ② 게임 안 캡처 + 수치 ③ PERF_BUDGET 판정 ④ ASSET_LEDGER·LICENSE·크레딧 ⑤ ADR/정의서(`docs/bible`) ⑥ 소유 파일 밖 변경 없음 ⑦ 트랙 브랜치에서 EditMode 전체 통과.

| 트랙 | 브랜치(제안) | 세션 | 상태 | 완성 정의(트랙별 추가) |
|---|---|---|---|---|
| **맵** | `track/map` | S1 (`local_c3aeeaa9…`) | A1 측정·#240·규칙 데이터화·13종·루팅·표면 태그·Ridge 연결·도로 코어·상가/빌라/아파트 생성기 완료. 외관 형상·학교는 `.txt` 초안 | 신맵(6지역·마을 17·411채) 생성·재생성 결정성·NavMesh·도로 12도·다리 5·고개 5, 건물 12종 외관 품질(로우폴리 상자 벗어나기), 411채 메모리·용량 판정(플레이어 빌드), 정의서 정정 반영 |
| **재질** | `track/materials` | S2 (`local_5200f1da…`) | 테이블·임시 선택·`PlasterInner` 교체·7종 다운로드 완료. 반입·ledger 미완, 밤하늘 | Poly Haven 7종 반입+ledger, 맑은 낮 조명(게임 안), 건물·도로·지형 전/후 캡처, 용량 ≤ 예산 |
| **차량** | `track/vehicles` | S10 (`local_745bdb3f…`) | 스펙 `docs/VEHICLE_SPEC.md`·ADR-198~203·이슈 #342~#350. 폐차 소품은 의도된 것(바운드 0.19m 묻힘), 접지 패치 `.txt` | 소품 접지 수정, 신맵 차량 배치·수량, 차량 소리 테스트 안정화, 크레딧(CC BY 4.0), 이슈 #90·#99·#237 |
| **총기** | `track/guns` | S7 (`local_8b3fe944…`) | 21종 스탯·저지력 코어 완료. 모델·루팅 연결 미완(모델 임시 확정, `placeholder/`) | 모델 반입+ledger, 루팅 연결, 저지력 서버 판정, 상표 검토 목록(사람) |
| **캐릭터** | `track/character` | S3 (`local_6931d230…`) | 시험 Swat이 Humanoid로 인식(본 55). `CharacterImport.SyncStaging`로 FBX 비커밋. 로스터 미구현 | 로스터 모델(Mixamo+UBC)·LOD·비율·소총 길이, 반입 방식(커밋 안 함), 크레딧 |
| **애니** | `track/animation` | S4 (`local_f747ebd3…`) | 설계·ADR-186~191·이슈 #298~#314. 구현은 캐릭터 이후 | 레이어·IK·감사 이슈 #218~#238, 테스트 재작성(수치·프레임 시퀀스) |
| **UI(비인게임)** | `track/ui` | S9 미시작 | 시안 확정, 구현 안 함 | 로비·메뉴·설정·크레딧·결과, 스쿼드 흐름, 인게임 HUD(오염 경고·열쇠·칩) |
| 오염·열쇠·칩 | `track/pollution` | S6 (`local_aef496c8…`) | 순수 C# 코어·테스트 완료(Unity 통과), 게임 연결 미구현 | 서버 연결·복제·연출·HUD, 지역 그래프(맵과 형식 합의 `s6_map_data_format.md`) |
| 봇·네트워크 | `track/botsnet` | S8 (`local_f7af4a9d…`) | 스펙·티켓 #324~#340, `-perfcapture` v2 설계(.txt), LagComp 결정화 패치(.txt) | 50인 파라미터화·관심영역·봇 행동, 측정 자동화(드로우콜·메모리·지역별) |
| 사운드 | `track/sound` | S5 (`local_86508b96…`), **준환이 직접 협의** | 걸음 클립 14표면 반영(미청취), ledger S-0008~22, CC-BY 항목은 클립 반입 때 `Credits.cs` | 준환 청취 결과 반영, 총성·전투음, 크레딧, "사운드가 이상하다" 원인 점검(S5 대기열에 지시 있음) |

## 5. 병합 순서와 충돌 지점
- 순서: **맵 → 재질 → 총기 → 차량 → 캐릭터 → 애니 → (오염·봇) → UI → 사운드**(사운드는 준환 협의 결과에 따라 앞당길 수 있음).
- 충돌 지점: `WorldBuilder*.cs`, `BuildingKind`/`Buildings*`, `world.json`, `materials.json`, `Player.prefab`(S3 `slopeLimit` 40 이미 반영), `DECISIONS.md`·`SPEC.md`·`ASSET_LEDGER.md`·`Credits.cs`, `.gitattributes`. 생성물은 재생성.
- 병합 후 매번: EditMode 전체, 재생성 결정성, 새 빌드, 필요 시 PlayMode.

## 6. 워크트리 만드는 방법(승인 필요, 아직 안 만들었다)
- 폴더는 저장소 밖 형제 디렉터리(예: `/Volumes/WorkSpace/Project/sublevel-wt/<track>`), `git worktree add -b track/<name> <path> 46afb44`.
- **Library 복제는 APFS 클론(`cp -cR`)**으로 하면 거의 즉시·무용량이다(Library 8.5GB). 첫 Unity 실행에서 재임포트 시간이 줄어든다(확인 필요: 일부 캐시는 경로 의존).
- git-lfs 필요(`git lfs ls-files`로 `World_Meshes.bytes` 확인). `AssetStaging/`은 git 무시라 워크트리에 복사해야 한다(심볼릭 링크 가능).
- RAM 24GB라 **Unity 동시 실행은 2~3개가 현실적 상한**(추정). 나머지는 문서·초안 작업.
- 세션 이동은 서버 도구가 아니라 "새 세션을 해당 폴더에서 시작"이 안전하다(`cwd` 지정).

## 7. 열린 위험·미결정
- 411채 메모리·용량(조각 평균 상가 187·빌라 309·아파트 501·학교 318, 아파트 최대 617/620), 플레이어 빌드 재측정 필요(A1은 에디터 추정치). 건물 외관 품질(현재 상자 느낌).
- 밤하늘(조명) 문제, 걸음 소리 품질(미청취), LagComp 플레이크, 차량 소리 테스트(긴 실행 환경).
- 상표·이름 검토(VK-9, 총기 가칭, 한국군 군용차 등 실존 이름), Mixamo 비공개 저장소 포함 조항, Sonniss 약관(준환 확인 전 사용 금지), 경사 ADR-195(slopeLimit 40) 가정, 대역폭 목표(측정 후), HRTF.
- 한국 전통 건축 에셋(화성행궁·광화문 등 무료 12종 조사): 마을 건물 대체는 부적합, 랜드마크 1~2개 시험은 낮은 우선순위(BACKLOG). 사찰은 종교 랜드마크 보류와 충돌.
- 새 마스터가 지켜야 할 준환 선호: 한국어 간결, 확인한 것/추정 구분, 이미지 단독 판정 금지, 결정은 master가 위임받아 하되 구조 변경·commit·push·다운로드·결제는 준환에게 묻는다.

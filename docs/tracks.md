# tracks.md — 트랙별 워크트리 완성 정의와 병합 절차 (2026-11-16, 새 master 초안 · 준환 승인 대기)

> 근거: `MASTER_STATE.md` §4~6, `SESSION_PLAN.md`, ADR-165~203. 충돌하면 SPEC이 이기고 룰 변경은 ADR이 먼저다.
> 한 트랙 = 브랜치 `track/<name>` = 워크트리 `/Volumes/WorkSpace/Project/sublevel-wt/<name>` = 세션 1개. 소유 파일 밖은 수정하지 않는다.

## 0. 공통 완성 정의 (모든 트랙, 7항 전부 ☑이어야 병합 요청)
| ☐ | 항목 | 증거 |
|---|---|---|
| 1 | 구현 + 테스트 우선 | EditMode 전체 통과(트랙 브랜치). 순수 C#도 **Unity EditMode로** 확인(dotnet 하네스 단독 금지) |
| 2 | 게임 안 캡처 + 수치 | 이미지 단독 판정 금지. 수치·테스트·캡처 3종 |
| 3 | PERF_BUDGET 판정 | 위반이면 병합 금지 |
| 4 | ASSET_LEDGER·LICENSE·크레딧 | 기록 없는 에셋 커밋 금지 |
| 5 | ADR·`docs/bible` 갱신 | ADR 번호는 아래 대역만 사용 |
| 6 | 소유 파일 밖 변경 없음 | `git diff --stat main...track/<name>`으로 확인 |
| 7 | 인수인계 노트 | 트랙 폴더 `docs/tracks/<name>_DONE.md`(한 장: 한 것·못 한 것·확인/추정) |

ADR 대역(충돌 방지): 맵 204–209 · 재질 210–214 · 총기 215–219 · 차량 220–224 · 캐릭터 225–229 · 애니 230–234 · 오염 235–239 · 봇 240–244 · UI 245–249 · 사운드 250–254. (현재 최대 ADR-203. **ADR-193이 두 행 중복**이라 병합 전에 한쪽 번호를 정정해야 한다.)

## 1. 트랙별 추가 완성 정의
| 트랙 | 브랜치 | 소유 | 완성 정의(공통 외) | 병합 순서 |
|---|---|---|---|---|
| **맵** (S1) | `track/map` | `Core/World/*`, `Editor/WorldBuilder*`, `MapBaker`, `WorldMeshes`, `WorldVegetation`, map·towns·roads·terrain.json | 청람도 6지역·마을 17·건물 약 411 생성, 재생성 시 `World_Meshes.bytes` 해시 불변, NavMesh 연결, 도로 경사 ≤12°·다리 5·고개 5, 건물 12종 외관(상자 탈피), **플레이어 빌드로** 411채 메모리·용량 재측정, 학교·외관 `.txt` 초안 컴파일 가능 상태로 승격, 정의서 정정(ADR-196) | 1 |
| **재질** (S2) | `track/materials` | `WorldMaterials`, `RenderSetup`, `SceneLook`, `LookDevBuilder`, materials.json, 재질 에셋 | Poly Haven 7종 반입+ledger, **밤하늘 해결**(맑은 낮 ADR-193 게임 안 확인), 건물·도로·지형 전/후 같은 구도 캡처, 텍스처 용량 ≤ 예산 | 2 |
| **총기** (S7) | `track/guns` | `Core/Weapon*`, weapons·ammo·attachments.json, 총기 모델(`placeholder/`) | 21종(+근접 4) 모델 반입+ledger, 루팅 연결(맵 가중과 일치), 저지력 서버 판정, 상표 검토 목록(사람 몫으로만 기록) | 3 |
| **차량** (S10) | `track/vehicles` | `VehicleBuilder/Art/Models`, VEHICLE_SPEC, vehicles.json | 정적 소품 접지 패치 적용(ADR-201), 신맵 배치·수량(ADR-198), 차량 소리 테스트 4건 안정화(ADR-203), CC BY 4.0 크레딧, 이슈 #90·#99·#237 | 4 |
| **캐릭터** (S3) | `track/character` | `CharacterKit`, `CharacterImport`, characters.json, 캐릭터 모델·프리팹 | 로스터 모델(Mixamo+UBC)·LOD·비율·소총 길이, FBX 비커밋 유지(`SyncStaging`로 복원 검증), 50체 렌더 PERF, 크레딧 | 5 |
| **애니** (S4) | `track/animation` | `CharacterAnimator*`, `CombatantMotion`, `MixamoBake`, animation.json | 레이어·IK, 감사 이슈 #218~#238, 테스트 재작성(관절 각도·프레임 시퀀스 수치), 50인 Animator PERF. **캐릭터 병합 뒤 시작** | 6 |
| **오염·열쇠·칩** (S6) | `track/pollution` | 오염 코어, `ElevatorTravel`, 열쇠·칩·팀 규칙, pollution·match.json | 서버 연결·복제, 지역 그래프(`s6_map_data_format.md`로 맵과 합의), 연출·최소 HUD(ui.json 항목), 시뮬 대조(평균 22.1분, 이탈률 25/28%) | 7 |
| **봇·네트워크** (S8) | `track/botsnet` | `AI/*`, `Net/*`, bots·network.json | 50인 파라미터화·관심영역·봇 행동, 측정 자동화(드로우콜·메모리·지역별), LagComp 결정화 패치(`S8_lagcomp_test_patch.txt`) 적용·3회 통과 | 7 |
| **UI(비인게임)** (S9) | `track/ui` | `LobbyScreen`, `MainMenuBuilder`, `CreditsScreen`, `GameSettings`, ui.json | 확정 시안(`adopted.html`) → 로비·메뉴·설정·크레딧·결과, 해상도별 캡처, 인게임 HUD(오염·열쇠·칩). **시작은 슬라이스 플레이 확인 후** | 8 |
| **사운드** (S5) | `track/sound` | `CombatantAudio`, `DoorAudio`, `ElevatorAudio`, `AmbientBeds`, `SoundRules`, audio.json | **준환 직접 협의 중 — 시작 전 준환에게 먼저 묻는다.** 청취 결과 반영, 총성·전투음, `Credits.cs`(CC-BY), 소리 점검 | 9(협의에 따라 앞당김) |

## 2. 병합 절차 (매 트랙 공통, 한 번에 하나)
1. 트랙 세션이 완성 정의 7항 + `*_DONE.md` 제출 → master가 `git diff --stat main...track/X`로 소유 범위 확인.
2. **master가 준환에게 commit·병합 승인**을 받는다(push는 별개, 한 번도 하지 않았음).
3. main에서 `git merge --no-ff track/X`. 충돌 지점은 `WorldBuilder*`, `BuildingKind`/`Buildings*`, `world.json`, `materials.json`, `Player.prefab`, `DECISIONS/SPEC/ASSET_LEDGER/Credits.cs`, `.gitattributes`. 문서류는 양쪽 합치고, **생성물(`World.unity`, `World_Meshes.bytes`, NavMesh, Terrain)은 병합하지 않고 재생성**.
4. 병합 뒤: `tools/unity.sh run WorldBuilder.Build`(재생성) → `.bytes` 해시 두 번 비교(결정성) → `test editmode` 전체 → `build mac` → 캡처 1세트(수치 병기). 소리 나는 PlayMode는 **준환 허락 때만**.
5. 통과하면 다음 트랙 워크트리를 main으로 rebase/merge해 갱신한 뒤 그 트랙 병합.
6. Unity 실행 뒤 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제(master 몫).
7. 실패하면 병합을 되돌리지 않고(되돌릴 수 없는 작업 금지) `git merge --abort` 또는 새 수정 커밋으로 처리.

## 3. 동시 Unity 실행 상한
RAM 24GB 기준 **2~3개(추정)**. 동시에 Unity를 돌리는 트랙은 맵·재질·캐릭터 우선, 나머지는 문서·초안·순수 C# 작업. 무거운 빌드·베이크는 한 번에 하나.

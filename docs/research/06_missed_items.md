# 06. 계획이 놓친 것 (감사 보고, 2026-11-16)

범위: 준환의 5개 계획(리얼 캐릭터 / 밀집 도시 / 걸을 수 있는 산 / Poly Haven 재질 / 애니 수정) 때문에 **연쇄로 깨지거나 새로 필요해지는 것**.
방법: 코드·데이터·문서만 읽음(실행·수정 없음). 경로 약어 `S/`=`Assets/_Project/Scripts/`, `E/`=`Assets/_Project/Editor/`, `D/`=`Assets/_Project/Data/Resources/GameData/`.
다른 트랙(캐릭터 선택 / 도시 레이아웃 / 산 / 맵 에셋 조달 / 애니 감사)과 겹치는 내용은 뺐다. 여기서는 "그 작업이 다른 곳에 일으키는 일"만 다룬다.

## 0. 한 줄 결론
가장 큰 구멍은 **"크기"가 이미 ADR로 막혀 있고(ADR-052: 1024m, 4km 비권고) 인원(16명+봇 23)·구역 모델(지역 4개 고정)·상한 상수(루팅 250, 바위 900)가 전부 1km² 기준으로 박혀 있다**는 점이다. 도시를 키우기 전에 "얼마나 키울지 + 몇 명이 뛰는지"부터 정하지 않으면 나머지가 다 틀어진다.

---

## 1. BLOCKER (결정 없이 시작하면 재작업)

### B1. 맵 크기 ↔ 인원 ↔ 이동수단이 서로 안 맞는다
- 근거: `D/world.json` Size 1024, 지역 4개(A산악 r320 / B습지 r300 / C마을 r280 Lots 30 / D공장 r300), 육지 약 0.6~0.7km². ADR-052(`docs/DECISIONS.md:63`)는 "4km는 도보 게임·M1 성능에 맞지 않음". `docs/research/open-world-tech.md:38-47`: 4km = 833배 면적, "스트리밍 필수·지금은 비권고", 2x2km도 "측정 필요".
- 인원: `rules.json` Match `MaxPlayers 16, BotCount 15, MaxBots 23`. 지금은 31명/육지 약 0.65km² = **약 48명/km²**(PUBG Karakin 16, Sanhok 6.3, Erangel 1.6명/km², 같은 문서 :41). 3~4km(9~16km²)로 키우면 39명 기준 2.4~4.3명/km²로 **밀도가 10~20배 떨어져** 교전 빈도와 봉쇄 템포가 전부 바뀐다. 인원을 PUBG식으로 늘리면 봇은 서버 CPU(감지 레이캐스트 0.15초 주기, `BotController.cs:314,427`)와 NavMesh 비용이 든다.
- 결정: (a) 목표 면적(0.7 → 2x2km 정도), (b) 목표 인원/봇 수, (c) 도보 vs 차량(차량은 이미 구현됨, 아래 I6).
- 권장 기본값: **2x2km 이하 한 장 + 인원 24~32(사람+봇), 봇 30 상한**부터. 4km는 스트리밍이 필요한 별도 프로젝트로 분리하고 ADR-052를 갱신(규칙 5).

### B2. 구역(봉쇄) 모델이 "지역 4개 + 지역당 엘리베이터 3~4대"로 하드코딩
- 근거: `S/Core/World/WorldRules.cs:27` `MinElevators 3, MaxElevators 4`, `:28` `MinElevatorSpacing 80`, `:30` `LotRange 60`(엘리베이터에서 60m 안에 건물 2채). `S/Core/World/WorldGenerator.cs:41-46` 단지 간격 표가 6개, 하믈릿 3개로 고정. `Lockdown.cs`는 지역 단위 상태머신(경고 300초 + 지역당 약 3.5분 + 최종 지역 소구역), 지역 수 4~7 가정(`RadiationStageScale` 7칸).
- 도시를 PUBG처럼 키우면 "마을 하나"가 지금 지역 하나보다 커진다. 지역을 원으로 두고(`Radius`) 지역 수를 늘리면 `ElevatorCount` 규칙이 위반이고, 지역을 그대로 크게 하면 한 지역을 닫는 시간이 이동 시간과 안 맞는다(걸어서 지역 가로지르기 > 경고 45초).
- 결정: 봉쇄 단위를 (1) 지금처럼 지역(= 섬 하나에 지역 6~9개로 더 쪼갬), (2) 지역 + 지역 내부 소구역을 일반 단계로 승격, (3) 배그식 원으로 교체 중 어느 것인가. (3)은 SPEC §3 변경이라 ADR 필수.
- 권장 기본값: (1) 지역을 늘리고 엘리베이터 규칙(최대 4)은 지역 크기에 비례하도록 규칙 파라미터화. 시간표(`LockdownRules`)는 지역 수·이동 시간에 맞춰 ADR로 재조정.

### B3. 건물 생성기가 종류 5개뿐 → "한국 80년대 도시"를 못 만든다
- 근거: `S/Core/World/BuildingPlan.cs:6` `enum BuildingKind { House, Warehouse, FactoryHall, Shed, ElevatorStation }`. 층수는 `Buildings.cs:72` 1~2층(깊이 7.8m 이상이고 50%)뿐, `Buildings.cs`는 1,102줄, 가구 `Furniture.cs` 545줄. 상가·빌라·아파트·교회·학교 같은 종류, 옥상, 지하, 담장 있는 마당(compound)은 없다.
- **그리고 이 enum이 다른 시스템의 열쇠로 쓰인다**(B4). 새 종류를 추가하면 연쇄 수정 지점이 최소 5곳.
- 결정: 건물을 계속 코드 조립으로 갈지, 외부 모델(프리팹) + 코드가 만든 내부로 갈지(맵 에셋 트랙과 연동). 코드 조립을 유지하면 `BuildingKind` 확장 + 종류별 `Buildings.FootprintLimits`, `SurfaceFor`, 루팅 점, 문·창 규칙.
- 권장 기본값: 종류 3~4개만 추가(상가 2~3층, 빌라/연립, 담 있는 주택 단지, 창고 변형). 80년대 감성은 재질·지붕·소품·간판 변주로.

### B4. 발소리·탄흔 재질이 "게임오브젝트 이름 → BuildingKind"로 결정된다 (조용히 Concrete로 폴백)
- 근거: `S/Gameplay/CombatantAudio.cs:229-235` `hit.collider.name != "Colliders"`면 Concrete, 루트 이름이 `StartsWith("Station")`이거나 `Enum.TryParse(name 끝 "_" 뒤)` 실패하면 Concrete. `S/Net/BulletSurfaces.cs:24-30` 같은 방식(House=Wood, 나머지=Metal). 표면 6종뿐: `S/Core/SoundRules.cs:7` `Concrete, Metal, Wood, Grass, Dirt, Water`. 지형은 4겹(풀/흙/바위/진흙)의 레이어 → `SoundRules.TerrainSurface`.
- 영향: Poly Haven 재질로 바꿔도 소리는 안 바뀐다(재질이 아니라 건물 종류로 정하므로 "아스팔트·자갈·타일·카펫·철제 계단·콘크리트 옥상"이 전부 Concrete/Metal). 새 건물 이름 규칙(`LotNN_Kind`)을 바꾸거나 프리팹을 쓰면 **테스트 없이 전부 Concrete**가 된다(`PlayMode/FootstepSurfaceTests.cs`가 기존 4종만 확인).
- 결정: 표면 판정을 "머티리얼/콜라이더 태그"로 옮길지(BulletSurfaceTag가 이미 있음) + 표면 종류 확장(Gravel, Asphalt, Tile, Carpet 등) 범위.
- 권장 기본값: 재질 → 표면 매핑 표를 하나 만들고(머티리얼 이름 또는 `BulletSurfaceTag`), 건물 이름 파싱은 폐기. 표면은 +3종(Gravel, Asphalt/Road, Tile) 정도. 오디오 클립은 CC0로(ledger).

### B5. 저장소/에셋 크기: 생성 메시가 텍스트 YAML이라 건물 수에 선형, 커밋마다 통째로 새 blob
- 근거(실측): `World_Meshes.asset` 42,102,976B(HEAD에서도 42MB, **LFS 아님**: `.gitattributes`는 `Vehicles/*_Meshes.asset`만 LFS), `World.unity` 32MB(HEAD 32,066,148B), `World_NavMesh.asset` 5.2MB, `World_Terrain.asset` 8.8MB. 이 파일들이 지금 `git diff --stat` 기준 World.unity 약 92만 줄 변경, World_Meshes 12.3만 줄 변경: **재생성할 때마다 사실상 전체가 바뀐다**(위치/ID 무작위). 이력: World_Meshes 12회, World.unity 14회 커밋. `.git` 512MB(팩 149MB + LFS 342MB). ADR-160(`DECISIONS.md:170`)은 "ForceText라 정점이 16진 2자, 이진 저장 시도 → 임포트 때 다시 텍스트로 바뀜 → 포기". 이슈 #240 열림.
- 추정: 지금 약 60~75동(world.json Lots 합 60 + 하믈릿)에 42MB = **동당 약 0.6MB**(가구·소품 포함). 도시 수십 동 x 여러 개 = 200~300동이면 **World_Meshes 120~200MB, World.unity 100MB+의 텍스트**. 에디터 임포트(YAML 파싱)·git diff·클론 시간이 문제. 이번 주 텍스처만 `Art/Textures` 173MB(jpg 54개, LFS) + Mixamo/Quaternius UAL 등 캐릭터 113MB.
- 결정: (a) 생성 결과를 **커밋하지 않고**(Library 캐시처럼) 빌더로 재생성(시드 결정적이므로 가능; 대신 CI/새 기기에서 빌드 시간) vs (b) 건물을 프리팹/메시 에셋으로 분리해 변경 파일을 최소화, (c) 이진 직렬화(.asset을 ForceBinary 옵션 에셋 또는 `.bytes`로 직접 저장해 런타임 로드).
- 권장 기본값: **(c) 메시를 `.bytes`(자체 포맷) 또는 에디터 외부 바이너리로 저장 + LFS 추적**, 씬은 건물 루트 프리팹 참조. 최소한 `World_Meshes.asset`/`World.unity`를 LFS 패턴에 추가(규칙: 되돌릴 수 없는 이력 재작성은 금지이므로 앞으로만 적용, 이미 들어간 blob은 그대로).

---

## 2. IMPORTANT (큰 영향, 순서는 도시 작업 중에 결정)

### I1. 상한 상수가 1km² 기준 (조용히 잘림)
- 근거: `E/WorldBuilder.cs:55` `MaxLootSpawns = 250`, `PickLoot`(:498)이 건물마다 한 점씩 순환하다 250에서 자름 → 건물 300동이면 **건물당 1개 미만**, 마지막 건물들은 0개. `E/WorldBuilder.Structures.cs:30` `MaxRocks = 900`. `E/WorldBuilder.cs:31` `FarClip 700`, `FogEnd 640`. 하이트맵 `HeightmapResolution 1025`/`AlphamapResolution 1024`(알파맵 1m/px: 새 Poly Haven 지형 재질을 가까이서 보면 경계가 뭉개짐).
- 결정/기본값: 상수를 `world.json`으로 옮기고(데이터화) 루팅은 "건물당 점 수 x 종류별 가중"으로 계산. 상한은 면적·건물 수에서 도출. 루팅 NetworkObject 수 상한은 아래 N2 측정으로 정함.

### I2. 루팅 테이블이 전역 1개 + 건물 종류별 차이 없음
- 근거: `D/loot.json` 단일 테이블(가중치만). `S/Core/Loot.cs` `LootTable.Roll`은 점의 위치/종류를 모름. `Buildings.cs:1007` `Loot(room, area, y, count, ...)`는 개수만 정함. PUBG의 핵심인 "핫존(군사/학교/창고) 고급 루팅 vs 외곽 민가 저급"이 없다. 방어구 3단·조준경·가방 등이 모든 건물에서 같은 확률.
- 영향: 밀집 도시에선 **거점 가치 차이**가 교전 지점을 만든다. 없으면 어디나 똑같아서 이동 동기가 사라짐(특히 면적이 커질수록).
- 결정: 건물 종류/지역별 가중 테이블 + 3단 장비는 특정 종류에만. 룰 변경이므로 ADR.
- 권장 기본값: `loot.json`에 `Tier`(1~3)와 `BuildingKind`별 가중 곱, 지역별 배수. 시드 결정성 유지(`LootTable.Plan(spawnPoints, seed)` 확장).

### I3. 봇이 길찾기는 하지만 "도시 전투"에 필요한 능력이 없다
- 근거: `AI/BotController.cs` NavMeshAgent를 서버에서 붙임, `NavMesh.CalculatePath` 코너를 한 점씩 따라감(:34, :868), 닫힌 문은 0.25초마다 열기(:264), 막히면 포기(`BotStats` 스톨 집계, `#77`). `NavMeshLink`/`OffMeshLink` **0건**(grep). `BotRules`: ViewDistance 70m, RoamRadius 35m, `LootRadius 25m`. 창문 넘기(parkour)·사다리·옥상 진입 경로를 봇은 모른다(`WalkSlopeDeg 40`, `agentClimb 0.3`).
- 영향: (1) 건물 안 2층은 계단이라 닿지만 **창문 사격/넘기는 못 함**. (2) 산에서 슬로프 40° 이하는 가지만 엄폐(바위·능선) 활용 로직 없음(`BotController.cs:984` 단순 엄폐 탐색). (3) 도시가 커지면 `PickRoam`의 "관심 지점"이 수렴점 3개/지역뿐(`D/world.json ConvergencePoints 3`)이라 봇이 **도시 전체를 안 돈다**. (4) 구조적 스톨(좁은 골목 + 차량/장애물)이 늘면 `BotStats`가 폭증.
- 결정: 봇 도시 행동의 범위(건물 단위 목적지·창문·차량 회피)와 봇 수 상한.
- 권장 기본값: 관심 지점을 "건물 입구 + 지붕 위 + 골목 교차점"으로 자동 생성(생성기 출력), NavMesh 연결성 테스트를 도시 전체로 확장, 창문/차량은 이번엔 제외(BACKLOG).

### I4. NavMesh가 한 장(5.2MB)이고 한 번에 구움
- 근거: `E/WorldBuilder.cs:395-430` 지형+모든 건물/바위/나무를 `NavMeshBuildSource` 한 덩어리로, 복셀 `AgentRadius/3`(0.117m), 타일 256. 4개 이하 지역·1km²에서 5.2MB. `S/Gameplay/ZoneNavMesh.cs`는 `NavMesh.AddNavMeshData` 한 번.
- 영향: 면적 x4 → 약 20MB, x16 → 80MB(저장소+메모리). 베이크 시간·메모리가 `M1 16GB`에서 한계일 수 있음. 걸을 수 있는 산(`WalkSlopeDeg 40`)은 복셀 단위로 좁은 능선이 끊기며 "닿는 곳/안 닿는 곳"이 늘어 연결성 테스트가 자주 깨질 수 있음.
- 권장 기본값: 타일 단위 부분 베이크(건물 수정 시 그 타일만), 연결성 테스트를 `WorldRules`에 이미 있는 방식으로 도시/산 전부에. 베이크 시간을 `PERF_BUDGET`에 항목으로 추가(현재 판정 방법 없음).

### I5. 성능 예산이 도시에서 깨질 가능성이 크다 (규칙 6 = 머지 금지)
- 근거(`docs/PERF_BUDGET.md`): 드로우콜 ≤2,500 / 삼각형 ≤1.5M / SetPass ≤60 / p99 ≤10ms / 텍스처 4K 금지 / 메모리 ≤3GB(**아직 판정 방법 없음**, "잠정") / 로딩 ≤5초(**잠정**) / 빌드 ≤4GB. 최근 실측(`:140~`, #150): World 한 지점에서 **555콜 / 68만 삼각형 / SetPass 32** (건물 약 60동, 시점은 엘리베이터 옆). `Textures`는 `AssetImportRules.MaxTextureSize 2048` 압축 사용(`E/AssetImportRules.cs:12,28`).
- 도시는 시야 안 건물이 5~10배(700m far clip, 안개 640m) → 삼각형 1.5M 초과 가능. 건물당 LODGroup(실내/껍질, `Structures.cs:23-24,326`)은 있으나 **오클루전 컬링 베이크 없음**(Editor 코드에 없음), 정적 배칭 일부러 끔(`WorldBuilder.cs:138-145`: 바위 복사 때문에 500MB). PBR 재질이 건물당 8~15종 x (알베도+노멀+ARM) → 고유 텍스처 수·메모리 급증(현재 `Prefabs/World/Materials` 파일 154개).
- 그림자: `WorldBuilder.cs:207` "Shadow distance stays 50m: overcast light hides the missing far shadows". **Kloofendal(맑음)로 바뀐 지금은 이 가정이 깨진다**(아래 I8). 도시에서 50m 그림자 경계가 도드라짐.
- 결정: 도시용 예산 재조정 ADR(어디서 재는지: 가장 무거운 시점 3곳, 예 도시 중심 지붕 위/교차로/산 능선에서 도시 조망)과 오클루전/카메라 컬링 방침.
- 권장 기본값: (1) 측정 시점을 "가장 무거운 3곳"으로 고정하고 `World_RendersWithinBudget`류 테스트에 추가 (2) 건물 외벽 껍질 메시를 머티리얼당 1메시로 유지, 실내는 LOD로 끄기(이미 있음) (3) 오클루전 베이크(건물 내부를 occluder로) 도입 여부는 측정 후. (4) 메모리·로딩 판정법을 먼저 만든다(미정 항목이 이제 가장 위험).

### I6. 도로/차량: 도로가 "지역당 트리"이고 도시 골목과 차량 크기가 안 맞는다
- 근거: `WorldGenerator.cs:18` "Roads form a tree in each region", `D/world.json` RoadWidth 6 / SpurWidth 4.5 / PathWidth 2.5 / MaxRoadSlopeDeg 12. 차량 `S/Net/VehicleSpawner.cs`는 "주차 지점당 1대" + 4m 셀 `RoadMask`(도로 밖은 속도 x0.85). `rules.json` Vehicles MassKg 1350, 최대 90km/h. `PavedRoads.cs`. 격자형 시가지·교차로·막다른 골목·사거리 U턴 없음.
- 영향: 좁은 골목에서 차량이 끼고 봇이 끼며(`BotStats`), 모퉁이 건물 코너 충돌, 산길(12° 한계)과 걸을 수 있는 경사(40°)의 불일치. 지도(MapBaker)의 도로 표현도 영향.
- 결정: 차량을 도시 안에서 어디까지 허용(도시 외곽 도로만? 골목은 보행 전용?).
- 권장 기본값: 도시 중심은 차량 진입 가능한 "주도로 + 광장"만, 골목은 폭 4m 이하로 두고 차량 충돌체가 못 들어가게(생성기 규칙).

### I7. 월드 생성 시간 / 결정성 / 되돌리기 검증 시간
- 근거: `WorldBuilder.Generate`는 plan 생성→검증→터레인→건물→NavMesh→저장을 1회에 수행(로그에 단계별 초 출력). `WorldRules.Validate`는 건물 쌍 비교 O(n²)(`WorldRules.cs:131-154`), 건물 내부는 `Buildings.cs:906-909`처럼 가구마다 루팅 점 전수 검사. 시드 결정적(`world.json Seed 20261113`), 그러나 **생성 결과가 씬에 직렬화**되므로 "같은 시드면 같은 맵"은 씬 파일이 진실.
- 영향: 건물 5배면 생성·NavMesh 베이크·저장이 5배 이상(베이크는 선형이 아님). 한 번 돌릴 때마다 M1 13"에서 수 분 + 열 이슈(`PERF_BUDGET`에 "장비가 뜨거울 때 불안정" 기록 있음). 그리고 `World.unity` 전체 재직렬화 → B5의 커밋 크기.
- 권장 기본값: 도시를 "블록 단위 부분 재생성"이 가능하게 쪼갬(블록 시드 고정) + 베이크 시간 로그를 ADR/PERF에 기록. 순차 실행 원칙(CLAUDE.md)은 유지.

### I8. 맑은 HDRI(Kloofendal)로 바꿨는데 월드는 "흐린 하늘" 가정으로 조정돼 있다
- 근거: `E/SceneLook.cs:16`(미커밋 변경)에서 `HdriPath`가 `OvercastSoilPuresky` → `KloofendalPartlyCloudy`. 그런데 `E/WorldBuilder.cs:39-41` `FogColor ... the overcast HDRI's horizon`, `:43` `OutdoorAmbient 0.8, SunIntensity 1.25, SunShadowStrength 0.8`(`:193` "Outdoors under an overcast sky"), `:207` 그림자 50m. 낮/밤/비/안개는 런타임 `WorldSky`(`S/Core/WorldSky.cs`)가 시간 키프레임으로 덮어씀(`SkyAmbient`), 하지만 **씬 베이스 값과 `Sky.mat`는 HDRI 하나**. `ReflectionProbe`/`LightProbe`는 `CombatantView`/`ThrowableView`에만 있고 월드 실내용은 없음.
- 영향: 건물 실내(밀폐)가 맑은 하늘 환경 반사/앰비언트를 받아 **밝게 새거나**(PBR 스무스니스 있는 Poly Haven 재질에서 더 두드러짐) 반대로 어두워짐. `World_InteriorGrade.asset`, `InteriorLights.cs`로 보정 중이나 건물 종류가 늘면 재검증 필요. 시간대 키(`Keys`)와 HDRI의 태양 방향이 일치하지 않으면 그림자 방향 ≠ 하늘의 해.
- 결정: 하늘/조명 진실 원천 하나(WorldSky 키프레임 vs HDRI) 정하기.
- 권장 기본값: HDRI는 "낮 구름 질감"용 스카이박스로만 쓰고 태양 방향·색·세기는 `WorldSky` 키프레임이 정함(이미 방향). 실내는 `ReflectionProbe`를 건물 종류당 1개(블렌드)로 굽고, 그림자 거리 80~100m + 캐스케이드 재조정(그래픽 옵션 "높음" 80m, `GraphicsOptions.cs:25`).

### I9. 네트워크: 문·유리는 Broadcast 전수, 루팅은 NetworkObject, 도시 규모에서 한계 측정 없음
- 근거: `docs/MP_AUDIT_WORLD.md`: 문(`DoorChanged`)·유리(`GlassShattered`)·매치·구역 상태는 **NetworkObject 없이 Broadcast로 모든 클라이언트에게**, 접속 시 열린 문/깨진 유리를 `Quiet`로 일괄 전송. `S/Net/DoorSystem.cs`는 문 리스트를 **선형 탐색**(`ClosedBetween` `:218-223`, `ServerBotOpen` `:196-205`, `IndexOf` `:170,191`)하고 봇 소리 처리마다 호출(`BotController.cs:394-396`). 관심영역(`Interest`, `rules.json`): 전투원 300m / 차량 350m / **루팅 100m** / 투척물 200m. 실측(`PERF_BUDGET.md:`네트워크 대역폭): 16+봇20에서 클라이언트당 8.5KB/s, 보이는 아이템 253→30.
- 영향: 문 수천 개(건물 200동 x 5)에 봇 30마리가 0.25초마다 전수 스캔 → 매 프레임 수만 회(아직 괜찮을 수 있으나 측정 없음). 늦은 접속자가 받는 문 상태가 건물 수에 선형. **문·유리는 관심영역을 안 탐**(전 맵 브로드캐스트) → 문 상태 변경(봇이 자주 엶)이 모든 클라이언트로 전송. 루팅 1,500개를 서버가 시작에 전부 NetworkObject로 스폰 → 접속 직후 스폰 폭주.
- 결정: 문/유리를 관심영역/공간 해시로 분할할지, 루팅 스폰을 지연(플레이어 접근 시)로 할지.
- 권장 기본값: `DoorSystem`에 건물/셀 단위 공간 해시(서버 선형 스캔 제거) + 문/유리 Broadcast를 건물 소속 + 관심영역 필터. `InterestBandwidthTests`를 "건물 200동·루팅 1,500·봇 30" 시나리오로 재측정(합격 기준을 ADR로).

### I10. 서버 측 적중 판정은 캡슐 + 위치 이력 → 도시 복잡도에는 둔감하지만 비용은 사격마다 `SyncTransforms`
- 근거: `S/Core/LagComp.cs`는 위치 이력 링버퍼, `S/Net/LagCompensation.cs:54,82` `Physics.SyncTransforms()`로 몸을 과거 위치로 옮겨 레이캐스트 후 복구. 부위 판정은 캡슐 높이 기준(`rules.json Fire.Zones StandingHeight 1.8, HeadMeters 0.3`).
- 영향: **리얼 캐릭터 교체(계획 1)는 서버 판정과 무관**(리그가 아니라 캡슐 기준이라 안전). 다만 건물 콜라이더가 수만 개로 늘면 `SyncTransforms` 비용과 `Physics.Raycast` 비용이 올라간다(건물은 한 "Colliders" 자식에 박스 다수).
- 권장 기본값: 영향 낮음(관찰만). 건물당 콜라이더를 박스 합치기/병합 기준(건물당 N개 이하)으로 테스트에 추가.

---

## 3. NICE / 확인할 것

- **N1. 미니맵/지도 해상도**: `S/Core/MapProjection.cs` `PictureSize 2048`, `GridStep 128`("8x8 over 1km"). 2km면 1px=1m로 건물 윤곽이 뭉개짐, 4km면 불가. 줌 단계/타일 지도 또는 해상도 4096(메모리 +64MB, 텍스처 규칙 4K 금지와 충돌 → 예외 ADR) 필요. `E/MapBaker.cs`는 건물 풋프린트를 그림(`:235-244`) → 도시에서는 블록 단위 표현으로.
- **N2. 보행 지형**: 산 슬로프(걷기 40°, `WorldRules.WalkSlopeDeg`)와 `CharacterController` slopeLimit/stepOffset 값이 일치하는지 확인 필요(코드는 `AgentSlope`만 지정). **발 IK 없음(#235)** → 경사에서 발이 뜬다(캐릭터 트랙과 연동하되 지형 경사 기준은 여기서). 낙하 데미지(안전 3m, 치명 12m)와 절벽/산의 상호작용, 수영 장벽(BarrierDepth 5m)과 새 해안.
- **N3. 식생/바위 수**: 줄기만 GameObject(CapsuleCollider)이고 풀/잎은 `RenderMeshInstanced`(`VegetationDraw.Chunk 1023`, 셀 64m). 면적 x4 = 나무 GameObject 수 x4(TreesPerHectare 8~40). 지금도 `MaxRocks 900`. 산이 "엄폐 있는 지형"이 되려면 바위 수/충돌체 정책(식생이 총알·시야를 안 막음, `open-world-tech`)을 정해야 함.
- **N4. 오디오**: `AmbientBeds`는 가장 가까운 지역 종류 + 지붕 유무 + 해안 근접(`AmbientRules`)으로 혼합. 도시 한가운데에서는 "마을 소음/차/멀리 총성 반향"이 없음(지역 4종). 실내 반향(`ReverbZone`)이 없다. 발소리 6종(B4). 총성 거리 필터는 있으나 건물 사이 반사 없음.
- **N5. 접근성/설정**: `GraphicsOptions`의 시야거리 4단(300/500/700/900), 그림자 4단. 도시에서 낮음 프리셋(약 30m 그림자)이 어떻게 보이는지 확인 필요. 색약/자막/UI 크기 옵션은 `SettingsCatalog` 범위 밖 (이미 있는 이슈 없음).
- **N6. 테스트/CI 시간**: 테스트 217개 파일(`Tests/EditMode` + `PlayMode`), World를 로드하는 PlayMode 테스트 20개 이상(`WorldSceneTests`, `InterestBandwidthTests`, `ReplicationWorldTests` 등). 월드가 커지면 각 테스트의 씬 로드/NavMesh 로드가 5배. 매치 완주 테스트(`MatchDedicatedEndToEndTests`)는 지역 이동 시간이 늘어 타임아웃(`BotElevatorExitTests` 등)에 걸릴 수 있다. PERF A 측정은 "장비가 뜨거우면 불안정"(PERF_BUDGET M1.6a-fix)이라 도시 측정은 쿨다운 절차 필요.
- **N7. 라이선스/ledger**: 규칙 1. Poly Haven/ambientCG는 CC0라 안전하지만 **ledger 항목은 에셋 단위**: 이번 주 jpg 54개 + HDRI + Mixamo 클립 17종 + Quaternius. `AssetValidator`가 `LICENSE.txt` 누락 시 **빌드 실패**(PERF_BUDGET:`M1 ... SynthDoors` 사례) → 새 폴더마다 LICENSE.txt. Mixamo는 `RISKS_OPEN_QUESTIONS` 미체크 항목(Adobe Additional Terms 원문 미확인, 규칙 8: 사람 판단) → **릴리스 전 반드시 체크**. `Art/Characters/Mixamo`는 현재 git 미추적(`?? Assets/_Project/Art/Characters/Mixamo/`): 커밋 전 약관 확인 대상.
- **N8. 빌드 크기**: 현재 mac 221MB / win 123MB(PERF_BUDGET) → 한도 4GB. 안전. 텍스처 2K x (알베도+노멀+ARM) x 수십 종은 Compressed로 약 0.5~1GB까지 가능성 → 메모리(≤3GB 잠정)가 먼저 병목.
- **N9. 날씨 x 새 재질**: 비 오는 날 젖은 지면(#207 ADR-163: 젖은 땅/웅덩이)은 기존 재질 가정. Poly Haven 재질로 바꾸면 젖음 셰이더 파라미터(smoothness 증가 등) 재조정.
- **N10. 엘리베이터 투입**(`Deployment.cs`): 엘리베이터는 지역당 3~4대, 시작 시 한 엘리베이터 1명, 투입 지역 상한 `ceil(전체/지역수)+1`(ADR-055). 인원을 늘리면 엘리베이터 수가 부족하다(지금 합계 13대로 인원 16+봇 15=31 > 13, 다중 팀 한 대 배치 로직 확인). 지역 간 이동 사이클 8초/쿨 60초는 큰 지역에서 짧음(`DeploymentRules.TravelSeconds`).

---

## 4. 프로세스(CLAUDE.md 규칙) 관점의 놓친 것
1. **ADR 선행(규칙 5)**: 맵 면적 확대(ADR-052 갱신), 봉쇄 모델 변경(SPEC §3, B2), 루팅 테이블 변경(I2), PERF 예산·측정 방법 변경(ADR-028/041)은 전부 ADR이 먼저. 현재 미커밋 ADR-165~167만 있음(에셋 라운드·소총 포즈·사망 애니).
2. **ledger(규칙 1)**: 에셋 추가마다 `ASSET_LEDGER.md` 기록 없으면 커밋 금지. 이번 주 텍스처/HDRI/캐릭터가 아직 커밋 전이라 **커밋 전 ledger와 `AssetLedgerTests` 통과 확인**.
3. **워크플로(wayfinder → to-spec → to-tickets)**: 이 계획은 "한 세션을 넘는 큰 덩어리"라 wayfinder 지도 대상이다. 기존 지도 이슈 `#55 [Map] M4까지`(wayfinder:map, OPEN)는 4지역 1km² 가정이라 **새 지도(M-City)로 따로 만들고 #55는 닫거나 연결**. 규칙: 지도 없이 도시 바로 구현 금지.
4. **열린 이슈와 겹침**(`gh issue list`): #240(World_Meshes 크기, B5와 동일), #230(캐릭터 비율/외형), #195(캐릭터 1종), #225(엎드리기), #227(웅크리기), #223(좌우/후진), #231(대기), #235(발 IK), #234(제자리 회전), #228(점프), #209(상호작용 물체: 사물함·서랍), #210(루트 모델 복셀풍), #213(투입 직후 60초), #90/#99(차량). 도시 작업이 건물 종류를 늘리면 #209와 #210이 같이 걸린다(상호작용 물체, 루트 모델 스타일).
5. **이슈에 없는 것**: 위 B1~B5, I1~I10에 해당하는 이슈가 **하나도 없다**(#240 제외). 티켓화 필요.
6. **성능 예산(규칙 6)**: 메모리(≤3GB)·로딩(≤5초)·GC는 "판정 방법 없음 / 잠정"이라 **지금은 위반해도 막을 장치가 없다**. 도시 작업 전에 판정 방법을 만들지 않으면 규칙 6이 사실상 무력.
7. **사용자 확인 규칙(전역 CLAUDE.md)**: 파일 구조 변경(건물 프리팹 분리, 메시 저장 방식 변경), 의존성 추가(예: 오클루전/ProBuilder/Spline 패키지), commit/push 전 반드시 질문. 이번 계획은 거의 모든 단계가 해당.

---

## 5. 결정 요약표 (준환이 답할 것)
| # | 질문 | 권장 기본값 |
|---|---|---|
| D1 | 맵 면적과 인원 | 2x2km 이하, 사람+봇 24~32, 봇 30 상한. 4km는 별 프로젝트 |
| D2 | 봉쇄 단위 | 지역을 6~9개로 늘리고 엘리베이터 규칙 파라미터화 |
| D3 | 건물 생성 방식 | 코드 조립 유지 + 종류 3~4개 추가, 외부 모델은 소품만 |
| D4 | 표면(발소리/탄흔) | 머티리얼/태그 매핑 표, 표면 +3종 |
| D5 | 생성 결과 저장 | 메시 `.bytes` + LFS, 씬은 프리팹 참조(앞으로만) |
| D6 | 루팅 구조 | 종류·지역별 가중 + 3단 장비 제한(ADR) |
| D7 | 성능 측정 시점/메모리 판정 | 가장 무거운 3곳, 메모리·로딩 판정 먼저 구축 |
| D8 | 하늘/조명 진실 원천 | WorldSky 키프레임, HDRI는 스카이박스, 실내 리플렉션 프로브 |
| D9 | 차량 | 주도로·광장만, 골목 보행 전용 |
| D10 | 문/유리 네트워크 | 건물 단위 + 관심영역 필터 |

---

## 6. CHECKLIST (순서 = 의존성. 다른 트랙과 겹치지 않는 "연쇄 작업"만)

### 0단계: 결정 + 문서 (코드 전에)
- [ ] D1 맵 면적·인원·봇 상한 결정, ADR-052 갱신(규칙 5)
- [ ] D2 봉쇄 단위 결정, SPEC §3 영향 ADR
- [ ] wayfinder 지도(M-City) 생성: #55와 관계 정리. 그릴링 → to-spec → to-tickets
- [ ] I1~I10, B1~B5에 대응하는 GitHub 이슈 생성(현재 #240 외 없음)
- [ ] 미커밋 변경(Textures 173MB, Mixamo, Quaternius, HDRI) 커밋 전 ledger·약관·LICENSE.txt 확인(N7), 커밋 여부는 준환에게 질문

### 1단계: 측정 인프라 (먼저 안전망)
- [ ] PERF: 메모리·로딩·GC 판정 방법 구현(`PERF_BUDGET.md` 잠정 항목), ADR로 합격선
- [ ] PERF: "가장 무거운 3 시점" 렌더 테스트 + 플레이어 빌드 B 측정(도시 임시 블록으로 먼저)
- [ ] 생성 시간·NavMesh 베이크 시간·씬/메시 크기를 빌드 로그에 항상 기록(회귀 감시)
- [ ] 쿨다운 절차 문서화(측정은 빌드 직후 금지, PERF M1.6a-fix 기록 근거)

### 2단계: 저장/빌드 구조 (도시가 커지기 전)
- [ ] D5: World_Meshes/World.unity 저장 방식 결정(이진/LFS/프리팹), 파일 구조 변경이므로 준환 승인(전역 규칙)
- [ ] `.gitattributes` LFS 패턴에 `World_*.asset`, 큰 텍스처 형식 추가(앞으로만, 이력 재작성 금지)
- [ ] #240을 이 작업과 통합(ADR-160 재검토)

### 3단계: 생성기 상수·규칙 데이터화
- [ ] `MaxLootSpawns 250`, `MaxRocks 900`, FarClip/Fog, Heightmap/Alphamap 해상도를 `world.json`으로(I1)
- [ ] `WorldRules`의 엘리베이터 개수/간격/LotRange 파라미터화(B2)
- [ ] 단지/하믈릿 간격 표(6칸·3칸) 데이터화
- [ ] 알파맵 해상도·지형 재질 타일링 재조정(Poly Haven 지형, I1)

### 4단계: 건물 종류 확장의 연쇄 수정
- [ ] `BuildingKind` 확장 시 `FootprintLimits`, `SurfaceFor`, `Buildings.Generate`, `WorldRules.LotFootprint`, MapBaker, `BulletSurfaces`, `CombatantAudio.SurfaceAt` 일괄 수정(B3·B4)
- [ ] D4: 표면 판정을 머티리얼/태그 기반으로 이전 + 표면 종류 확장 + `FootstepSurfaceTests` 확장
- [ ] 새 표면 오디오 클립(CC0) 조달 + ledger
- [ ] 건물당 콜라이더 개수 상한 테스트 추가(I10)

### 5단계: 루팅·봇·구역
- [ ] D6: 루팅 테이블에 건물 종류/지역/단계 반영(ADR), `LootTable.Plan` 확장, 시드 결정성 테스트
- [ ] `PickLoot` 재설계(건물 수 비례), 루팅 스폰 지연 여부 결정(I9)
- [ ] 봇 관심 지점 자동 생성(건물 입구/옥상/교차로), NavMesh 연결성 테스트 도시·산 전체(I3·I4)
- [ ] `BotStats` 스톨 수치 기준을 도시 시나리오 테스트에 추가(`#77` 연장)
- [ ] 엘리베이터 수/투입 인원 분배 재검토(N10), 지역 간 이동 시간 재조정
- [ ] `LockdownRules` 시간표 재조정(지역 수·이동 시간 기반, ADR)

### 6단계: 네트워크
- [ ] `DoorSystem` 공간 해시(선형 스캔 제거), 문/유리 Broadcast를 관심영역과 연결(I9)
- [ ] `InterestBandwidthTests` 도시 규모 시나리오(건물 200동, 루팅 1,500, 봇 30)로 재측정
- [ ] 지연 접속자 전송량(열린 문/깨진 유리) 측정

### 7단계: 렌더/조명
- [ ] D8: WorldSky 키프레임 vs HDRI 정리, `FogColor`/`OutdoorAmbient`/그림자 거리 재조정(I8)
- [ ] 실내 `ReflectionProbe`(건물 종류당) 및 `World_InteriorGrade` 재검증
- [ ] 오클루전 컬링 도입 여부를 측정 결과로 결정(의존성 추가면 준환 승인)
- [ ] 그림자 거리/캐스케이드 도시 기준 재측정, 그래픽 옵션 프리셋 재확인(N5)

### 8단계: 지도/UI/오디오/마감
- [ ] 지도 해상도·줌 정책(N1), 미니맵 도시 표현
- [ ] 도시 앰비언스·실내 반향(N4)
- [ ] 비 젖음 셰이더를 새 재질에 재조정(N9)
- [ ] D9 차량 정책(골목 보행 전용 규칙)을 생성기 규칙으로(I6)
- [ ] 전체 테스트 시간/타임아웃 재조정(N6), 매치 완주 테스트 타임아웃
- [ ] 릴리스 전 체크리스트에 Mixamo 약관·한국 상표 검토 유지(규칙 8, N7)

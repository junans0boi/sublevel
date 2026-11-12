# 야외 오픈 맵 기술 조사 (2026-11-13)

대상: Unity 6000.3.28f1 / URP 17.3 / FishNet 4.7.3 / 헤드리스 생성 / 기준 장비 M1 13".
질문: 160x120m 회색 상자 구역을 **지형·도로·물로 이어진 여러 지역이 있는 야외 맵**으로 바꾸려면 무엇을, 어떤 순서로, 코드만으로 만들 수 있는가.

표기 규칙
- **[문서]** 직접 연 공식 문서에 적힌 내용(링크 첨부). **[로컬]** 이 저장소의 패키지 캐시·설정 파일에서 직접 확인. **[계산]** 산술로 얻은 값.
- **미확인** = 문서로 확인하지 못함. **프로토타입 필요** = 문서로는 판단할 수 없고 M1에서 재야 함.
- Unity 문서는 `docs.unity3d.com/6000.3/...` 주소가 `docs.unity.com/en-us/engine/6000.3/...`로 넘어간다. 아래 링크는 실제로 열린 주소다.

## 0. 결론 요약

| 항목 | 권고 |
|---|---|
| 크기(1차) | **1024 x 1024m 지형 타일 1장**, 그 안에 바다로 둘러싸인 섬(육지 약 0.6~0.7km²). 현재 구역 D(0.019km²)의 약 30~35배 |
| 확장 | 같은 생성기를 타일 단위로 설계해 **2x2 타일(2048m)** 까지. 4x4km는 도보 전용·M1 기준에서는 권하지 않음 |
| 지형 | Unity `Terrain` 1장, heightmap 1025(1m 간격) 또는 2049(0.5m), `TerrainLayer` **4개 이하**, `drawInstanced = true` |
| 씬 구조 | 단일 씬. 스트리밍·타일 분할은 2km로 키울 때 도입 |
| 그리기 비용 | 안개 + far clip 600~800m, `LODGroup`, 건물은 머티리얼별 메시 합치기, 그림자 거리 제한. GPU Resident Drawer는 Forward+ 전환이 필요해 **측정 후 결정** |
| 물 | 직접 쓴 HLSL URP 셰이더(투명, 깊이 페이드·노멀 스크롤·프레넬). 바다 = 맵 경계 |
| 도로 | 스플랫 칠하기 + 스플라인 따라 높이 평탄화(1차). 메시는 다리·포장 구간만 |
| 건물 | 코드 생성 모듈(벽 패널·바닥·계단·지붕) + 타일링 PBR + 월드 UV. 임포트 모델은 소품만 |
| 내비메시 | 지금처럼 `NavMeshBuilder.BuildNavMeshData`로 에디터에서 구워 에셋 저장. voxel/tile 크기 조정 |
| 네트워크 | 단일 씬 + `DistanceCondition`(또는 `GridCondition`)을 ObserverManager 기본 조건에 추가 |
| 조명 | 실시간 방향광 1개 + HDRI 환경광, 라이트맵 없음, 시간 고정 |
| 데이터 | `WorldSpec`(지역·POI·도로·수위·시드) → `WorldGenerator`(순수 C#) → `WorldRules.Validate` → `WorldBuilder`(에디터). ZoneDBlockout/ZoneLayout 구조 그대로 확대 |

**먼저 해결할 문서 충돌(ADR 필요, CLAUDE.md 규칙 5)**: SPEC §3~5는 "구역 7개가 엘리베이터 허브로 이어지고 구역 단위로 봉쇄"이고, TECH 원칙 7·PERF_BUDGET은 "구역별 씬 Additive, 활성 구역 1개(이동 중 2개)"다. 이어진 야외 맵은 이 둘을 바꾼다. 구현 전에 "구역 = 한 맵 안의 지역", "엘리베이터 = 지표 투입 지점", "활성 구역 수 예산 → 가시 거리 예산"으로 고치는 ADR이 있어야 한다.

## 1. 맵 크기

도보 이동 시간 [계산] (걷기 4.5m/s, 달리기 7.0m/s — `rules.json` Locomotion)

| 한 변 | 면적 | 변 횡단 걷기 / 달리기 | 대각선 걷기 / 달리기 | 구역 D 대비 면적 |
|---|---|---|---|---|
| 160x120m (현재) | 0.019km² | 36초 / 23초 | 44초 / 29초 | 1배 |
| 1km | 1km² | 3.7분 / 2.4분 | 5.2분 / 3.4분 | 52배 |
| 2km | 4km² | 7.4분 / 4.8분 | 10.5분 / 6.7분 | 208배 |
| 4km | 16km² | 14.8분 / 9.5분 | 21분 / 13.5분 | 833배 |

참고 밀도: PUBG는 Karakin 2x2km에 64명, Sanhok 4x4km에 100명, Erangel 8x8km에 100명([PCGamesN 맵 목록](https://www.pcgamesn.com/playerunknowns-battlegrounds/pubg-map)). [계산] Karakin 16명/km², Sanhok 6.3명/km², Erangel 1.6명/km². 이 맵들은 차량이 있다. 우리는 차량이 M5 이후(SPEC §5)라 **도보 전용**이다.

| 크기 | 전투원 수(권고) | 도보 전용 플레이감 | M1 내장 GPU | 판단 |
|---|---|---|---|---|
| 1x1km | 16~24 (플레이어+봇) | 지역 4개가 각 300~400m, 지역 간 이동 1~2분. 교전 간격이 짧음 | 안개·far clip으로 한 화면에 300~600m만 그리면 예산 안일 가능성 높음(프로토타입 필요) | **1차 권고** |
| 2x2km | 40~64 | 끝에서 끝 5~10분. 봉쇄 템포를 느리게 잡아야 함 | 지형 타일 4장, 내비메시·봇 수 4배. 가능하나 측정 필요 | 2차 확장 목표 |
| 4x4km | 100 | 차량 없이는 이동만 10~20분 | 봇 100, 내비메시 16배, 스트리밍 필수 | 지금은 비권고 |

M1이 한 프레임에 그릴 수 있는 양은 맵 크기가 아니라 **가시 거리 안의 드로우콜**이 정한다. 실측상 비용은 드로우콜 1,000개당 약 1.8ms이고 삼각형 영향은 작다(PERF_BUDGET 실측 근거). 따라서 크기 선택의 실제 제약은 (a) 봇 수와 서버 로직, (b) 내비메시·빌드 시간, (c) 메모리다.

권고: **1024m 타일 1장으로 시작**하되 생성기 입력을 "타일 좌표 + 타일 크기"로 받아 2x2 타일로 늘릴 수 있게 한다. 섬 모양이라 실제 육지는 0.6~0.7km²이고 나머지는 얕은 물·바다다.

## 2. 코드로 만드는 Unity Terrain (batchmode)

### 2-1. API와 순서

| 단계 | API | 확인한 사실 |
|---|---|---|
| 데이터 생성 | `new TerrainData()`, `heightmapResolution`, `size` | `heightmapResolution`은 33/65/129/257/513/1025/2049/4097 중 하나로 고정된다 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata). 폭·길이 1~100,000, 높이 1~10,000 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-other-settings) |
| 높이 | `SetHeights(x, y, float[,])` | 값 0~1, 배열은 **[y,x]** 순서. 호출마다 LOD·식생 정보를 전부 다시 계산해 비싸다 → 한 번에 전체를 넣는다 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata/setheights) |
| 재질 | `terrainLayers`, `alphamapResolution`, `SetAlphamaps(x, y, float[,,])` | 배열은 **[y,x,layer]**. 문서 예제가 `GetSteepness`로 경사에 따라 두 레이어를 섞는다 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata/setalphamaps) |
| 구멍 | `SetHoles`, `holesResolution` | 스크립트로 만들 때 구멍 마스크 해상도 = heightmap 해상도 - 1. 구멍은 Terrain Collider에서 자동 제외. URP 에셋의 **Terrain Holes**가 꺼져 있으면 빌드에서 사라진다 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-height/terrain-basic-sculpting/terrain-paint-holes). 현재 `Game_URP.asset`은 `m_SupportsTerrainHoles: 1` [로컬] |
| 나무 | `treePrototypes`, `SetTreeInstances(instances, snapToHeightmap)` | API 존재 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata). Tree Editor는 Built-in 전용, SpeedTree는 URP 지원 [문서](https://docs.unity.com/en-us/engine/6000.6/manual/creating-environments/script-terrain/terrain-trees.md) (6000.6 페이지) |
| 풀 | `detailPrototypes`, `SetDetailResolution`, `SetDetailLayer`, `DetailPrototype.useInstancing`/`usePrototypeMesh` | Detail Resolution 0~4048, Per Patch 8~128(권장 16) [문서](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-other-settings), 프로토타입 속성 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/detailprototype) |
| 오브젝트 | `Terrain.CreateTerrainGameObject(data)` | Collider 포함 생성 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terrain) |
| 렌더 설정 | `heightmapPixelError`, `basemapDistance`, `drawInstanced`, `detailObjectDistance`, `detailObjectDensity`, `treeDistance`, `treeBillboardDistance`, `treeMaximumFullLODCount`, `shadowCastingMode` | 전부 스크립트 속성. `drawInstanced` 기본값은 false [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terrain) |
| 저장 | `AssetDatabase.CreateAsset(terrainData, ...)`, TerrainLayer도 에셋으로 | 일반 에디터 API(미확인: alphamap 텍스처가 TerrainData의 하위 에셋으로 자동 저장되는지는 프로토타입에서 확인) |

### 2-2. 해상도 선택

| 지형 1024m 기준 | 샘플 간격 | 용도 | 메모리 [계산] |
|---|---|---|---|
| heightmap 1025 | 1.0m | 1차 권고. 도로 폭 6m를 6샘플로 평탄화 | 16bit 가정 시 2.1MB(미확인: 내부 포맷. CPU·GPU·물리 사본을 합치면 수 배) |
| heightmap 2049 | 0.5m | 도로 가장자리·건물 대지가 거칠면 올림 | 8.4MB |
| heightmap 4097 | 0.25m | 불필요 | 33.6MB, `float[,]` 임시 배열 67MB |
| alphamap 1024 | 1.0m | 도로 경계가 흐림 | 레이어 4개당 4.2MB |
| alphamap 2048 | 0.5m | **권고** | 레이어 4개당 16.8MB. `float[,,]` 임시 배열 4레이어 67MB → 구역을 나눠 넣는다 |

### 2-3. URP 지형 셰이더 한계

| 항목 | 내용 |
|---|---|
| 레이어 수 | URP·Built-in은 **텍스처 패스당 4 레이어**, 패스 수 제한 없음. 패스가 늘 때마다 지형 렌더 시간이 늘어난다. "최대 성능을 위해 타일당 4 레이어로 제한" [문서](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-textures/class-terrain-layer). URP 패키지에 `TerrainLitAdd.shader`(Add Pass)가 실제로 있다 [로컬] |
| 높이 기반 블렌드 | Mask Map의 B 채널 높이로 가장 높은 레이어만 그림 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/materials-and-shaders/built-in/shaders-in-universalrp/reference/shader-terrain-lit). "레이어 5개 이상이면 무시"는 검색 결과로만 봤고 6000.3 페이지에는 없었다(미확인) |
| Mask Map | R 메탈릭, G AO, B 높이, A 스무스니스 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-textures/class-terrain-layer). ambientCG 맵을 `SceneLook.PackMask`와 같은 방식으로 한 번 더 패킹하면 된다 |
| 픽셀 노멀 | `Enable Per-pixel Normal`은 **Draw Instanced가 켜져 있을 때만** 동작 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/materials-and-shaders/built-in/shaders-in-universalrp/reference/shader-terrain-lit) |
| 원경 | `basemapDistance` 밖은 미리 합성한 저해상도 basemap 사용 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terrain) |

권고 레이어 4개: **풀 / 흙(길·습지) / 암석(경사) / 자갈·아스팔트(도로·대지)**. 지역 차이는 5번째 레이어가 아니라 레이어 비율·색 변주·소품으로 낸다. 칠하기 규칙: 경사 > 32° → 암석, 수위 +0.5m 이하 → 흙(젖은), 도로 마스크 → 자갈, 나머지 풀. 경계는 노이즈로 흩뜨린다.

### 2-4. 나무와 풀

| 방식 | 장점 | 문제 |
|---|---|---|
| Terrain 나무 인스턴스(`SetTreeInstances`) | 데이터가 가볍고 거리·빌보드 설정이 지형에 있음 | Tree Distance/Billboard Start/Max Mesh Trees는 SpeedTree에는 적용 안 됨 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-other-settings). **일반 메시 프리팹 나무가 URP에서 자동 빌보드로 바뀌는지는 미확인**. Tree Editor는 Built-in 전용, SpeedTree Modeler는 외부 도구(구매·계정 → 규칙 3으로 사용 불가) |
| 일반 GameObject 프리팹 + `LODGroup` | LOD를 직접 통제, GPU Resident Drawer 대상이 됨(4장) | 오브젝트 수만큼 Transform. 1km²에 3,000~6,000그루 수준이면 감당 가능(프로토타입 필요) |

권고: **1차는 GameObject + LODGroup**(LOD0 메시 / LOD1 단순 메시 / LOD2 교차 쿼드 / Culled). 교차 쿼드 텍스처는 같은 CC0 나무 메시를 batchmode에서 카메라→RenderTexture로 찍어 만든다(우리 에셋의 파생물이며 AI 생성이 아님, ASSET_LEDGER에 "수정" 기록). 나무 줄기에는 Capsule Collider. Terrain 나무와의 비용 비교는 2단계에서 잰다.
풀은 **1차에서 넣지 않는다**. Detail은 패치마다 드로우콜이 생기고(패치 크기를 키우면 배치 수가 줄어든다 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata)) 투명 겹침이 내장 GPU에 부담이다. URP 패키지에 `WavingGrass`·`TerrainDetailLit` 셰이더는 있다 [로컬]. 넣는다면 `detailObjectDistance` 40~60m, 인스턴스 메시 모드로 3단계 이후 측정하며 추가.
에셋 위험: 세미리얼(SPEC §9)에 맞는 CC0 나무는 Poly Haven 스캔 모델(무거움)과 Quaternius/Kenney(로우폴리, 화풍 불일치) 사이에서 골라야 한다 → 2단계 전에 조사 티켓.

### 2-5. 헤드리스에서 안 되는 것·주의

| 항목 | 상태 |
|---|---|
| `-nographics`로 GI 굽기 | **불가** [문서](https://docs.unity.com/en-us/engine/6000.3/manual/unity-editor/command-line-arguments/editor) |
| `-nographics`로 지형 생성·저장 | 미확인, **프로토타입 필요**. 지형 heightmap·alphamap·basemap은 GPU 텍스처와 엮여 있다. 과거 "Draw Instanced 지형이 batchmode+nographics 빌드에서 안 그려짐" 이슈가 검색에 나오나 이슈 페이지가 열리지 않아 내용 미확인 |
| `tools/unity.sh run` | 현재 **항상 `-nographics`** 로 실행한다 [로컬]. 지형·임포스터 굽기·스크린샷이 들어가는 월드 빌더는 그래픽이 필요하므로 `-nographics` 없는 실행 경로(예: `run-gfx` 서브커맨드, 또는 지금처럼 PlayMode 테스트 경로)가 필요하다 |
| 코드로 만든 TerrainData의 나무가 플레이어 빌드에서 안 보이는 이슈 | 검색에 제목만 나옴, 페이지 미열람(미확인) → 2단계에서 **플레이어 빌드로** 나무 표시를 확인 |
| `-quit` + 비동기 | 비동기 코드가 돌 때 `-quit`은 멈춤을 일으킬 수 있다 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/unity-editor/command-line-arguments/editor). 굽기는 동기 API(`BuildNavMeshData`, `Lightmapping.Bake`)를 쓴다 |
| 브러시 도구(Paint Trees 등) | GUI 전용. 전부 `TerrainData` API로 대체 가능(위 표) |

## 3. 지형 1장 / 이웃 타일 / 씬 스트리밍

| 구조 | 내용 | 적합한 크기 | 비용 |
|---|---|---|---|
| **지형 1장, 단일 씬** | 가장 단순. 내비메시·네트워크·테스트가 지금 구조 그대로 | ≤ 1km (2km도 heightmap 2049로 1m 간격이면 가능) | 1km: 지형 데이터 수십 MB 이하 [계산] |
| 타일 N장 + `SetNeighbors`, 단일 씬 | 타일마다 레이어 4개를 따로 쓸 수 있음(지역별 재질). 이음매 LOD를 맞추려면 **모든 타일에서** `SetNeighbors`를 호출해야 함 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terrain/setneighbors). 경계 높이 샘플을 같게 만들어야 함 | 2km (1024m x 4) | 타일 수에 비례 |
| Additive 씬 스트리밍 | 타일·건물군을 씬으로 나눠 거리로 로드/언로드 | 4km 이상 | 로딩 끊김 관리, FishNet 씬 조건·서버는 전부 로드 필요, 테스트 복잡도 큼 |

권고: **1차는 지형 1장·단일 씬.** 생성기는 타일 좌표를 받게 만들어 2km 때 2x2 타일 + `SetNeighbors`로 간다. 스트리밍은 도입하지 않는다(서버=호스트가 어차피 전체 충돌·내비메시를 들고 있어야 하고, 메모리 예산 3GB 대비 1km 지형은 작다). 메모리 실측은 PERF_BUDGET에 아직 판정 방법이 없으므로 1단계에서 플레이어 빌드 RSS를 처음으로 기록한다.

## 4. M1 비용 통제

현재 설정 [로컬]: Forward(`m_RenderingMode: 0`), SRP Batcher 켬, GPU Resident Drawer 끔, 그림자 거리 50m·캐스케이드 1·2048·소프트 끔, Depth/Opaque Texture 끔, MSAA 끔.

| 수단 | 사실 | 자동화 | 권고 |
|---|---|---|---|
| 안개 + far clip | `RenderSettings.fog*`, `Camera.farClipPlane`(일반 API, 문서 미열람) | 쉬움 | **필수.** far 600~800m, 안개 색을 HDRI 지평선 색에 맞춤. SPEC 3-2의 "산악 최종전 안개"와도 맞음 |
| 지형 LOD | `heightmapPixelError`(LOD 전환 시 최악 팝 픽셀 수 근사), `basemapDistance` [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terrain) | 쉬움 | pixel error 8~12, basemap 150~250m에서 시작해 측정 |
| SRP Batcher | 같은 셰이더·다른 머티리얼에 유효, CPU 쪽만 빨라짐 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset) | 이미 켬 | 유지. 머티리얼 종류는 12개 안팎으로 |
| 메시 합치기 | 건물 1동을 머티리얼별 메시 2~4개로(`Mesh.CombineMeshes`, 일반 API) | 쉬움 | **필수.** 드로우콜이 비용의 주범(1,000콜 ≈ 1.8ms 실측) |
| `LODGroup` | LOD Cross Fade는 URP 에셋 옵션 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset) | 쉬움(코드로 `SetLODs`) | 나무·건물·큰 소품. 소품은 Culled 거리로 잘라냄 |
| GPU Resident Drawer | 요건: **Forward+**, 컴퓨트 셰이더 지원 API(OpenGL ES 제외), MeshRenderer, `BatchRendererGroup Variants = Keep All`(빌드 시간 증가), SRP Batcher 켬 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/in-urp/reduce-draw-calls-urp/reduce-rendering-work-on-cpu/gpu-resident-drawer). 제외: `MaterialPropertyBlock` 사용, 실시간 GI, Light Probe Proxy Volume, 인스턴스별 콜백 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/in-urp/reduce-draw-calls-urp/reduce-rendering-work-on-cpu/make-object-compatible-gpu-rendering). 코드상 막는 플랫폼은 visionOS와 RawBuffer 미지원 플랫폼뿐 [로컬: `GPUResidentDrawer.Validator.cs`] | 설정은 코드로 가능 | Metal은 요건에 해당하나 **문서에 Metal/Apple Silicon 명시는 없음(미확인)**. Forward → Forward+ 전환은 지금까지의 실측을 전부 다시 재야 하는 변경. **3단계에서 A/B 측정 후 결정(프로토타입 필요)**. Terrain 나무·디테일이 대상인지는 미확인(문서는 "GameObject + MeshRenderer"만 말함) |
| GPU 오클루전 | GPU Resident Drawer 필요. 가려짐이 많고 같은 메시가 많을 때 유효, 효과가 작으면 오히려 느려짐 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/in-urp/gpu-culling) | 설정만 | GRD를 채택할 때만 함께 측정 |
| 구운 오클루전(Umbra) | `StaticOcclusionCulling.Compute()` 존재 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/staticocclusionculling). 방·복도처럼 구획이 뚜렷한 장면에 가장 유효, 런타임에 CPU·메모리 사용 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/cameras/occlusion-culling/occlusion-culling). batchmode 동작은 문서에 언급 없음(프로토타입 필요) | 가능해 보임 | 야외는 이득이 작다. **1~3단계 보류**, 건물 밀집 지역에서 예산이 부족할 때 시도 |
| 그림자 | Max Distance, Cascade Count("늘리면 성능 저하"), Soft Shadows(타일 기반 GPU에서 영향 큼) [문서](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset). Apple GPU는 타일 기반 | 쉬움 | 거리 60~80m, 캐스케이드 2, 소프트 끔 유지. 그림자 패스는 거리 안의 물체를 한 번 더 그리므로 드로우콜 예산에 포함해 잰다 |
| 나무 임포스터 | 2-4 참조 | 가능(RT 캡처) | LOD2로 사용 |
| SSAO | 프레임의 절반(약 1ms, PERF_BUDGET M1.5) | — | 야외에서 예산이 모자라면 가장 먼저 끔(기존 방침) |
| Render Scale / FSR | Render Scale < 1에서 업스케일 필터 선택 가능 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset) | 쉬움 | 최소 사양(8GB 내장 GPU)용 품질 옵션으로 남겨 둠 |

드로우콜 예산 배분(안, 합계 ≤ 2,500): 지형 150 · 건물 600 · 나무 400 · 소품 500 · 캐릭터·무기 400 · 그림자 패스 350 · 물·하늘·기타 100.

## 5. 물 (Shader Graph 없이)

| 항목 | 내용 |
|---|---|
| 가능 여부 | 가능. `.shader` 텍스트 파일을 에디터 스크립트가 쓰고 `Shader.Find` + `new Material`로 머티리얼 생성(기존 `SceneLook` 방식). Shader Graph GUI 불필요 |
| 깊이 페이드 | `DeclareDepthTexture.hlsl`의 `SampleSceneDepth(uv)`로 장면 깊이를 읽어 수면과의 깊이 차로 투명도·색을 정한다. URP 에셋의 **Depth Texture를 켜야** 한다 [문서](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/shaders/writing-custom-shaders-urp/writing-shaders-urp/reconstruct-world-position) (6000.0 페이지), `_CameraDepthTexture` 선언은 URP 17.3 패키지에 그대로 있음 [로컬]. 현재 `m_RequireDepthTexture: 0` |
| 굴절 | `_CameraOpaqueTexture`(Opaque Texture, 다운샘플 옵션 있음) [문서](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset). 전체 화면 복사 1회가 추가됨 → **1차는 쓰지 않는다**(깊이 기반 색만으로 충분) |
| 구성 | Transparent 큐, ZWrite Off, 노멀맵 2장 스크롤, 프레넬로 하늘색(환경 큐브맵 샘플) 혼합, 깊이 차로 얕은 곳 투명·깊은 곳 불투명, 해안선 거품은 깊이 차 임계값 |
| 비용 | 드로우콜 1~2, 물이 덮는 화면 픽셀만큼 투명 셰이딩 + 깊이 텍스처 준비 비용. SSAO(깊이 소스)가 이미 깊이를 만들고 있어 추가분이 작을 수 있으나 **프로토타입 필요**(물이 화면의 절반을 덮는 해안 시점에서 측정) |
| 노멀 텍스처 | ambientCG에 물 노멀 CC0가 있는지 미확인. 없으면 노이즈로 코드 생성(절차적 텍스처는 AI 생성이 아님) |

바다를 경계로 만드는 법
1. 섬 마스크: 높이 = 노이즈 지형 x 중심에서의 거리 감쇠. 가장자리로 갈수록 수위 아래로 내려가 타일 끝에서는 수심 10m 이상.
2. 수면: y = `WaterLevel`의 큰 평면(맵보다 훨씬 크게, 또는 카메라 추종). 안개가 수평선을 가린다.
3. 얕은 물(수심 ≤ 약 0.6m)은 걸을 수 있는 습지·해안. 그보다 깊으면 **내비메시에서 제외**하고, 플레이어는 수심 약 1.2m 등심선에서 막는다. 방법은 (a) 등심선을 따라 생성한 보이지 않는 벽 콜라이더, (b) `Game.Core`의 높이 함수로 서버가 이동을 거부. (a)가 기존 CharacterController 흐름을 덜 건드린다. 어느 쪽이든 **게임 룰이므로 ADR**(SPEC §12 "깊은 물 비범위"와 일치).
4. 호수·습지는 같은 수위를 쓴다(수위 1개). 고도가 다른 호수는 1차 범위 밖.

## 6. 도로

| 방식 | 구현 | 장점 | 단점 |
|---|---|---|---|
| **스플랫 + 높이 평탄화** | 도로 = 폴리라인. 각 heightmap 샘플에서 폴리라인까지 거리 d를 구해 d < 반폭이면 도로 높이(폴리라인 따라 완만하게 보간)로, 반폭~반폭+어깨폭은 원래 높이와 보간. alphamap도 같은 거리로 도로 레이어를 칠함 | 메시·z-fighting·콜라이더·내비메시 처리가 전혀 없음. 드로우콜 0 추가 | 가장자리가 alphamap 해상도(0.5~1m)만큼 흐림. 차선·연석 불가 |
| 생성 메시 | 스플라인 따라 띠 메시를 지형 위 2~3cm에 생성 | 또렷한 가장자리, 차선 텍스처 | 지형 LOD가 멀리서 메시를 뚫고 올라옴(pixel error와 충돌), 그림자·내비메시 소스 처리 추가 |

권고: **1차는 스플랫 + 평탄화**(비포장·자갈길로 설정상 자연스러움). 공장·마을 내부의 포장면은 건물 대지와 같은 "평탄화된 패드 + 콘크리트 슬래브 박스"(기존 `SceneLook.WorldBox`)로 만든다. 다리는 메시. 도로 경사 상한(예: 12°)을 검증 규칙에 넣는다.

## 7. 건물·거점

| 방식 | 판단 |
|---|---|
| **코드 생성 모듈** | 권고. 부품: 벽 패널(문·창 개구부 포함), 바닥 슬래브, 계단/경사로, 지붕, 기둥. 기존 `WorldBox`(월드 스케일 UV로 텍스처 반복)의 확장. 건물 1동을 머티리얼별로 합쳐 드로우콜 2~4. 콜라이더는 부품 단위 Box(합친 MeshCollider보다 싸고 내비메시 소스로도 명확) |
| 트림시트 | 전용 아틀라스 텍스처가 있어야 한다. CC0 트림시트 확보 여부 미확인. **타일링 PBR(ambientCG) + 월드 UV + 가장자리용 얇은 띠 부품**으로 비슷한 효과를 낸다 |
| 임포트 모델(CC0) | 대부분 실내가 없거나 화풍이 다르다(ASSETS.md: Kenney는 "카툰이면 소품 한정"). **소품(드럼통·팔레트·울타리·차량 잔해)에만** 사용 |

실내를 들어갈 수 있고 길찾기가 되게 하는 규칙(검증기에 넣는다)
- 문 폭 ≥ 1.2m, 높이 ≥ 2.2m(에이전트 반경 0.35m·높이 1.8m 기준 [로컬: `ZoneDBuilder`]). 층고 ≥ 2.8m(TPP 카메라 충돌 여유).
- 계단은 경사로 콜라이더(≤ 35°, 현재 내비메시 경사 한계)로. 단차 ≤ 0.3m.
- 건물 대지는 지형을 평탄화하고 바닥 슬래브를 지면보다 0.1~0.2m 높게 + 기초 스커트로 틈을 가림. 문 앞 2m는 장애물 금지(기존 "경사로 진입로" 규칙의 재사용).
- 건물마다 출입구 ≥ 2(막다른 건물 방지), 거점마다 엄폐물 수 하한(기존 규칙 재사용).
- 건물 종류는 데이터(평면 격자 + 층수 + 개구부 목록)로 6~10종, 시드로 변형.

## 8. 내비메시

현재: AI Navigation 패키지 없이 내장 `NavMeshBuilder.CollectSources`(PhysicsColliders) → `BuildNavMeshData` → 에셋 저장, 구역 D 16,773m² [로컬].

| 사실 | 출처 |
|---|---|
| 소스 종류에 `Terrain`(TerrainData)이 있다. Mesh 소스는 원점에서 100,000 단위 이내 | [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshbuildsourceshape) |
| 타일 단위로 병렬 빌드해 최대 메모리를 통제. `tileSize` 단위는 voxel, **기본 256(정적 굽기에 적합)**, 런타임 갱신은 32~128 권장. `overrideTileSize`를 켜야 적용 | [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshbuildsettings/tilesize) |
| `voxelSize`/`overrideVoxelSize`, `maxJobWorkers`, `minRegionArea`, `preserveTilesOutsideBounds` 설정 존재 | [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshbuildsettings) |
| 증분 갱신 `UpdateNavMeshData` / `UpdateNavMeshDataAsync` 존재 | [문서](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshBuilder.html) |
| 지형 구멍은 Terrain Collider에서 제외됨(내비메시 반영은 6000.3 페이지에 없음, 미확인) | [문서](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-height/terrain-basic-sculpting/terrain-paint-holes) |

규모 추정 [계산] (기본 voxel = 반경/3이라는 통상값 가정 — 미확인. 반경 0.35m → 0.117m, 타일 256voxel ≈ 30m)

| 크기 | 타일 수(전체 면적) | voxel 열 수 | 굽기 시간·메모리 |
|---|---|---|---|
| 구역 D | 약 20 | 1.4M | 현재 문제 없음 |
| 1km | 약 1,150 | 73M | **프로토타입 필요** (구역 D의 약 50배) |
| 2km | 약 4,500 | 294M | 프로토타입 필요 |
| 4km | 약 18,000 | 1,176M | 내장 빌더 단일 굽기로는 비현실적일 가능성 |

권고
- **에디터에서 굽고 에셋으로 저장**(지금 방식 유지). 런타임 굽기는 로딩 시간과 호스트 메모리를 쓰므로 하지 않는다.
- `overrideVoxelSize`로 voxel 0.175m(반경/2) → 열 수 약 44%로 감소. 문 폭 1.2m가 유지되는지 검증 테스트로 확인.
- 깊은 물 제외: 수위 - 0.6m 아래를 덮는 `ModifierBox` 소스를 NotWalkable로 추가(동작은 프로토타입 필요). 급경사는 `agentSlope` 35°가 자동 제외.
- 나무: GameObject + Capsule Collider면 `CollectSources`가 수집한다. Terrain 나무 인스턴스가 수집되는지는 미확인(GameObject 방식을 권하는 이유 중 하나).
- 2km로 갈 때: 타일별 `NavMeshData` 여러 장을 `NavMesh.AddNavMeshData`로 올리는 방법이 있으나 경계 연결 동작은 미확인 → 그때 조사.
- 검증: 기존처럼 구운 내비메시 위에서 "모든 POI·투입 지점·루팅 지점이 서로 도달 가능"을 PlayMode 테스트로.
- 대안: Recast 직접 사용·서드파티(A* Pathfinding Project는 유료 → 규칙 3). 내장 빌더가 1km를 감당하면 불필요.

## 9. 네트워크(FishNet)와 좌표 정밀도

| 항목 | 사실 | 권고 |
|---|---|---|
| 관찰자 조건 | ObserverManager의 Default Conditions가 모든 NetworkObject에 붙는다. "거의 모든 게임이 최소 Scene 조건을 써야 한다" [문서](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/observermanager.md). 조건은 모두 통과해야 관찰자가 된다 [문서](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/network-observer.md) | 단일 씬이므로 Scene 조건 + 거리 조건 |
| `DistanceCondition` | 클라이언트 소유 오브젝트가 Maximum Distance 안에 있어야 보임. Hide Distance Percent로 경계 깜빡임 방지(100 + 0.5 → 150에서 숨김). Timed 조건(주기 평가) [문서](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/scriptableobjects/observerconditions/distancecondition.md). 4.7.3 패키지에 포함 [로컬] | 전투원 350~400m(= 캐릭터 가시 거리 이상), 루팅 100~150m |
| `GridCondition` + `HashGrid` | 거리 조건보다 덜 정확하지만 더 싸다. HashGrid를 NetworkManager에 둬야 함. Grid Axes(기본 XY → **XZ로 바꿔야 함**), Accuracy [문서](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/scriptableobjects/observerconditions/gridcondition.md) · [문서](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/observermanager/hashgrid.md) · 기본값 [로컬] | 루팅처럼 수가 많은 정적 오브젝트에 |
| 호스트 표시 | Update Host Visibility: 호스트 클라이언트에게 안 보여야 할 오브젝트의 렌더러를 숨김 [문서](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/observermanager.md) | 켠다(호스트도 클라이언트와 같은 화면을 봐야 테스트가 의미 있음). 드로우콜도 줄어듦 |
| 네트워크 LOD | 먼 오브젝트 동기화 빈도 감소. **Pro 전용** [문서](https://fish-networking.gitbook.io/docs/guides/features/level-of-detail.md) | 사용 안 함(유료 → 규칙 3). ColliderRollback도 문서 목차에 Pro로 표시됨 → M6 지연 보상 계획에 영향, RISKS에 올릴 것 |
| 루팅 수 | 구역 D 32개 → 1km²면 수백~천 개 | 전부 NetworkObject면 접속·스폰 비용이 큼. 거리/그리드 조건으로 줄이되, 부족하면 "서버가 가진 표 + RPC" 방식으로 전환(프로토타입 필요) |
| 봇 | 서버 로직. 플레이어에게서 먼 봇은 판단 주기를 낮춤(자체 구현) | 4단계에서 봇 16~24로 서버 프레임 측정 |

부동소수점 [계산]: float32 가수 24bit. 좌표 1,024~2,048m에서 최소 눈금 0.12mm, 2,048~4,096m에서 0.24mm. **맵 중심을 원점에 두면** 4km 맵도 ±2km 범위라 0.12~0.24mm — 떨림·물리 문제 없음. 원점 이동(floating origin) 불필요. FishNet NetworkTransform의 위치 압축 정밀도는 미확인(4단계에서 맵 가장자리에 세운 캐릭터의 원격 위치 오차를 잰다).

## 10. 조명

| 선택 | 내용 | 판단 |
|---|---|---|
| **실시간만** | 방향광 1개(그림자) + HDRI 스카이박스 환경광 + 전역 반사 큐브맵. 지금 `SceneLook`이 하는 방식 그대로. 환경광 프로브는 `SkyAmbient`가 시작 때 다시 계산 [로컬] | **권고.** 맵을 다시 생성해도 굽기가 없다 |
| 라이트맵 굽기 | `Lightmapping.Bake()`는 동기 [문서](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/lightmapping/bake). `-nographics`에서는 불가 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/unity-editor/command-line-arguments/editor). GPU 라이트매퍼는 macOS에서 가용 메모리 판단이 어려워 CPU 폴백 가능성이 높다 [문서](https://docs.unity.com/en-us/engine/6000.3/manual/lighting-overview/direct-and-indirect-lighting/lightmapping/baking-before-runtime/gpuprogressive-lightmapper) | 1km² 굽기 시간은 미확인이나 M1에서 길 것. 생성형 맵과 맞지 않음. **하지 않는다** |
| 반사 프로브 | 전역 커스텀 큐브맵(현재). 실내용 프로브는 굽기 또는 런타임 1회 렌더가 필요(미확인) | 1차는 전역만 |
| 시간대 | 고정(흐린 낮, 현재 HDRI `overcast_soil_puresky`) | 유지. 흐린 하늘은 그림자 의존이 낮아 그림자 거리를 줄여도 티가 덜 남 |

알려진 약점: GI가 없으면 **실내가 하늘 환경광으로 평평하고 밝게** 보인다. 완화 순서: SSAO(이미 있음) → 실내 머티리얼을 어둡게 → 건물별 반사 프로브 → (마지막) 건물 내부만 라이트 프로브/APV. 5단계에서 스크린샷으로 판단.

## 11. 기존 구역 개념의 이전

현재 구조 [로컬]: `ZoneDBlockout.Build()`(순수 C#, 결정적) → `ZoneLayout`(블록·수렴 지점·엘리베이터·루팅·랜드마크) → `ZoneLayoutRules.Validate` → `ZoneDBuilder`(에디터: 씬·내비메시) → `ZoneDTests`.

제안 구조(같은 모양을 한 단계 위로)

| 계층 | 위치 | 내용 |
|---|---|---|
| `WorldSpec` (데이터, JSON) | `Assets/_Project/Data` | 시드, 타일 크기·개수, `WaterLevel`, 최대 고도, 지역 목록(종류·중심·반경·고도 성향), POI 목록(종류·지역·건물 수), 도로 그래프(POI 간 연결), 밀도(나무·엄폐·루팅) |
| `WorldGenerator` (순수 C#, `Game.Core`) | `Scripts/Core` | 높이 함수 `Height(x,z)`, 스플랫 가중치, 도로 폴리라인, 건물 배치, 나무·소품 위치, 게임 지점. 출력 `WorldLayout` |
| `WorldLayout` | `Scripts/Core` | 지역별 `ZoneLayout`에 해당하는 묶음: 수렴 지점, 투입 지점, 소구역 다각형, 루팅 지점, 랜드마크 + 높이 격자·도로·건물 목록 |
| `WorldRules.Validate` | `Scripts/Core` | 아래 규칙. EditMode 테스트에서 여러 시드로 실행 |
| `WorldBuilder` (에디터) | `Editor` | TerrainData·레이어·물·건물 메시·나무·내비메시·씬 저장 |
| `WorldTests` (PlayMode) | `Tests` | 내비메시 도달성, 렌더 예산(시점 여러 곳), 스폰 |

개념 대응

| 지금 | 야외 맵에서 |
|---|---|
| 구역(Zone) | **지역(Region)**: 산악·공장·마을·습지/해안. 봉쇄 단위 |
| 수렴 지점 3~4곳, 반경 6m 빈 터 | 지역마다 3~4곳 유지(광장·야드·능선 평지). `ClearRadius` 규칙 그대로 |
| 엘리베이터 3~4대(외벽) | 지역마다 3~4곳의 **투입 지점**(지표로 올라오는 승강구 구조물). 간격·수렴 지점과의 거리·출구 앞 빈 공간 규칙 그대로. "구역 간 이동 수단"으로서의 엘리베이터가 남는지는 룰 결정(ADR) |
| 소구역(부분 봉쇄) | 지역을 4~8개 다각형(또는 격자 칸)으로 분할. 봉쇄는 지역 → 소구역 순 |
| 루팅 지점 | 건물 실내·POI 주변에 밀도 규칙으로 생성 |
| 외벽 | 바다(등심선 경계) |

검증 규칙(추가분): 모든 POI·투입 지점이 수위 위 + 경사 한계 이하 / 도로 경사 ≤ 상한 / 도로가 모든 POI를 연결(그래프 연결성) / 건물 대지 평탄도 / 문 앞 빈 공간 / 지역 간 이동 시간 상·하한(달리기 기준) / 건물·나무·소품 수 상한(드로우콜 예산에서 역산, 현재 `MaxBlocks = 600`의 확대판) / 깊은 물로 고립된 육지 없음.

결정성: `Game.Core`는 UnityEngine 비의존이라 `Mathf.PerlinNoise`를 쓸 수 없다 → 정수 해시 기반 value/gradient 노이즈를 직접 구현. 맵은 **에디터에서 한 번 생성해 에셋으로 굽고** 서버·클라이언트가 같은 에셋을 쓰므로 mac/win 사이 부동소수 결정성은 필요 없다(재생성 재현성만 필요 → EditMode 테스트로 "같은 시드 = 같은 해시" 확인). 시드로 매치마다 맵이 바뀌는 구조는 범위 밖.

## 12. 단계별 계획

각 단계는 걸어 다닐 수 있고 수치가 남는다. 무거운 작업은 순차 실행, 측정은 장비를 식힌 뒤(PERF_BUDGET M1.6a-fix 교훈).

| 단계 | 만드는 것 | 완료 시 측정 | 주요 위험 |
|---|---|---|---|
| **0. ADR + 스파이크** | 구역→지역 룰 ADR. 빈 1025 지형 1장을 코드로 생성·저장·로드하는 최소 스크립트. `-nographics` 유무 비교, 플레이어 빌드에서 표시 확인 | 지형 생성 시간, 에디터 방식 렌더 p99(지형만), 플레이어 빌드 RSS(첫 메모리 기록) | 헤드리스에서 지형 에셋 생성이 막힘 → 그래픽 있는 batchmode 경로 추가 |
| **1. 지형 + 물 + 경계** | `WorldSpec`/`WorldGenerator`/`WorldRules` 뼈대, 섬 높이, 레이어 4개 스플랫, 물 셰이더, 안개·far clip, 깊은 물 경계, 내비메시 1km 굽기. 플레이어가 섬을 걸어 다님 | 판정 A를 **시점 4곳**(산 정상 조망, 해안, 평지, 물 근접)에서. 내비메시 굽기 시간·에셋 크기. B 60Hz | 내비메시 굽기 시간/메모리, 물 + 깊이 텍스처 비용, 지형 패치 드로우콜 |
| **2. 도로 + 식생** | 도로 그래프 → 평탄화·스플랫, 나무(LODGroup + 임포스터), 바위. Terrain 나무 인스턴스와 A/B | 나무 3,000 / 6,000그루에서 A. 숲 한가운데 시점. 그림자 거리 50 / 80m 비교 | 세미리얼 CC0 나무 확보, 나무 드로우콜, 알파 겹침 |
| **3. 거점 1곳(공장)** | 건물 모듈 생성기, 공장 지역 1곳을 구역 D의 수렴 지점·투입 지점·엄폐 규칙으로 재구성, 실내 진입·내비메시 | 거점 중심 시점에서 A, 실내 도달성 테스트. **GPU Resident Drawer(Forward+) A/B** | 건물 드로우콜, Forward+ 전환 시 전체 재측정, 실내 조명 품질 |
| **4. 전 지역 + 게임 지점 + 네트워크** | 마을·산악·습지 거점, 지역별 소구역·루팅·투입 지점, `DistanceCondition`, 더미/봇 16~24 배치 | A(전 지역 대표 시점), B/C, 호스트 서버 프레임 시간, 관찰자 전환 시 끊김, 메모리 | 루팅 NetworkObject 수, 봇 서버 비용, 빌드·테스트 시간 증가 |
| **5. 외형 다듬기** | 소품, 머티리얼 변주, 실내 조명 보정, (선택) 풀 | 최종 게이트 A/B/C + win 빌드 | 예산 초과 시 SSAO → 그림자 거리 → 식생 밀도 순으로 줄임 |

기존 큐와의 관계: M2-2(봇)·M2-3(투입)은 **1단계 이후**에 하면 새 맵 위에서 바로 만들 수 있다. 구역 D 씬은 회귀 테스트용으로 남긴다.

### 상위 위험
1. **내비메시 1km 굽기**가 M1에서 수 분~수십 분이거나 메모리를 넘음 → voxel 0.175m, 육지 경계로 bounds 축소, 그래도 안 되면 지역별 분할 굽기.
2. **드로우콜**: 건물·나무·그림자 패스 합계가 2,500을 넘음 → 메시 합치기·LOD·안개 거리로 해결되지 않으면 Forward+ + GPU Resident Drawer(M1에서의 실효는 프로토타입 필요).
3. **헤드리스 지형 파이프라인**: `-nographics`에서 지형/basemap/임포스터 생성이 안 될 수 있고, 코드 생성 TerrainData의 나무가 빌드에서 빠지는 과거 이슈(미확인)가 있다 → 0단계에서 플레이어 빌드로 확인.
4. **룰 충돌**: SPEC·TECH·PERF_BUDGET의 "구역 씬/엘리베이터 허브/활성 구역 1개" 전제가 바뀜 → ADR 없이는 착수 불가. 엘리베이터 정체성(세계관)도 사람 결정 필요.
5. **외형 품질**: 세미리얼에 맞는 CC0 나무·지형 텍스처 확보, GI 없는 실내의 평평한 조명, 4레이어 지형의 단조로움 → 2·5단계 스크린샷 자체 검수 + 준환 직접 확인.

그 밖: 루팅 NetworkObject 수(4단계), FishNet 지연 보상(ColliderRollback)이 Pro 표시(M6), 메모리 판정 방법 부재(PERF_BUDGET 잠정).

## 출처(직접 연 페이지)

Unity 6000.3
- [Editor command line arguments](https://docs.unity.com/en-us/engine/6000.3/manual/unity-editor/command-line-arguments/editor)
- [TerrainData](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata) · [SetHeights](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata/setheights) · [SetAlphamaps](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terraindata/setalphamaps)
- [Terrain](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terrain) · [Terrain.SetNeighbors](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/terrain/setneighbors) · [DetailPrototype](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/detailprototype)
- [Terrain Settings reference](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-other-settings) · [Terrain Layer](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-textures/class-terrain-layer) · [Paint holes](https://docs.unity.com/en-us/engine/6000.3/manual/creating-environments/script-terrain/terrain-height/terrain-basic-sculpting/terrain-paint-holes)
- [URP Terrain Lit shader](https://docs.unity.com/en-us/engine/6000.3/manual/materials-and-shaders/built-in/shaders-in-universalrp/reference/shader-terrain-lit) · [URP Asset reference](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset)
- [GPU Resident Drawer](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/in-urp/reduce-draw-calls-urp/reduce-rendering-work-on-cpu/gpu-resident-drawer) · [GRD 호환 조건](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/in-urp/reduce-draw-calls-urp/reduce-rendering-work-on-cpu/make-object-compatible-gpu-rendering) · [GPU occlusion culling](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/in-urp/gpu-culling)
- [Occlusion culling](https://docs.unity.com/en-us/engine/6000.3/manual/cameras/occlusion-culling/occlusion-culling) · [StaticOcclusionCulling](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/staticocclusionculling)
- [NavMeshBuilder](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshBuilder.html) · [NavMeshBuildSettings](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshbuildsettings) · [tileSize](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshbuildsettings/tilesize) · [NavMeshBuildSource](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshbuildsource) · [NavMeshBuildSourceShape](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshbuildsourceshape)
- [Lightmapping.Bake](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/lightmapping/bake) · [Progressive GPU Lightmapper](https://docs.unity.com/en-us/engine/6000.3/manual/lighting-overview/direct-and-indirect-lighting/lightmapping/baking-before-runtime/gpuprogressive-lightmapper)

다른 버전 페이지(6000.3 대응 페이지를 열지 못함)
- [Reconstruct world space positions in a shader in URP (6000.0)](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/shaders/writing-custom-shaders-urp/writing-shaders-urp/reconstruct-world-position)
- [Trees (6000.6)](https://docs.unity.com/en-us/engine/6000.6/manual/creating-environments/script-terrain/terrain-trees.md) · [Tree LOD (6000.2)](https://docs.unity3d.com/6000.2/Documentation/Manual/terrain-Tree-LOD.html)

FishNet
- [Observers](https://fish-networking.gitbook.io/docs/guides/features/observers.md) · [ObserverManager](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/observermanager.md) · [NetworkObserver](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/network-observer.md)
- [DistanceCondition](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/scriptableobjects/observerconditions/distancecondition.md) · [GridCondition](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/scriptableobjects/observerconditions/gridcondition.md) · [HashGrid](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/observermanager/hashgrid.md) · [Level of Detail (Pro)](https://fish-networking.gitbook.io/docs/guides/features/level-of-detail.md)

기타
- [PCGamesN: PUBG 맵 크기·인원](https://www.pcgamesn.com/playerunknowns-battlegrounds/pubg-map) (2차 자료)

로컬 확인: `Library/PackageCache/com.unity.render-pipelines.universal@*/ShaderLibrary/DeclareDepthTexture.hlsl`, `.../Shaders/Terrain/*`, `com.unity.render-pipelines.core@*/Runtime/GPUDriven/GPUResidentDrawer.Validator.cs`, `com.firstgeargames.fishnet@*/Runtime/Observing/Conditions/*`, `Assets/_Project/Settings/Game_URP.asset`, `Game_URP_Renderer.asset`, `tools/unity.sh`, `Assets/_Project/Editor/ZoneDBuilder.cs`.

열지 못해 인용하지 않은 것: Unity Issue Tracker의 지형 관련 두 이슈(이전 후 404), Unity 6.3의 Trees/Tree LOD 페이지, Camera·RenderSettings(안개)·LODGroup·ReflectionProbe 스크립트 레퍼런스.

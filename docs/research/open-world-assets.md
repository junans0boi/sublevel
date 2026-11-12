# 야외 오픈월드 맵 에셋 조사 (CC0 · 계정 없이 직접 다운로드)

조사일 2026-11-13. 조사만 했고 에셋 팩은 내려받지 않았다(페이지·API·HEAD 요청·미리보기 썸네일 몇 장만).
목표: 1~2 km 야외 맵(산악 / 공장 / 마을 / 습지·해안), 세미리얼, M1 내장 GPU 1080p 60 fps.

표기 규칙
- **직접** = 계정·로그인·결제 위젯·약관 동의 없이 URL로 받을 수 있음(이번에 응답 확인).
- **사람** = 사람이 브라우저에서 받아야 함.
- **미확인** = 이번 조사에서 1차 출처로 확인하지 못함.
- 스타일 적합: 좋음 / 보통 / 나쁨 (세미리얼 기준).
- Poly Haven 에셋 페이지는 `https://polyhaven.com/a/<id>` 형식(대장 A-0004와 같은 형식). 개별 페이지는 열지 않았고 수치는 전부 공개 API 응답에서 가져왔다.

---

## 0. 한눈에 보는 결론

| 항목 | 결론 |
|---|---|
| 지형 바닥 PBR | **충분**. ambientCG + Poly Haven, 둘 다 CC0 + 직접 다운로드 |
| 바위·절벽 | **충분하지만 무겁다**. Poly Haven 스캔 모델은 2만~290만 tris → 감량 필수 |
| 나무 | **가장 큰 구멍**. Poly Haven 나무는 31만~1,740만 tris(렌더용). 게임용 CC0 세미리얼 나무는 직접 다운로드로는 사실상 없음 |
| 덤불·풀 | 보통. Yughues(OpenGameArt) 덤불·풀 팩이 게임용(400~2,000 tris) |
| 공장·산업 소품 | **좋음**. Poly Haven 모듈 벽·울타리·배관 + Yughues 방벽·탱크·드럼통·팔레트 |
| 주택·컨테이너·차량 | **없음**. 코드로 만든 상자형 모듈 + PBR 머티리얼이 현실적 |
| 하늘 | 충분. Poly Haven puresky HDRI 59종 |
| 물 | CC0 물 노멀맵 직접 다운로드 **못 찾음** → 코드로 생성 |
| 높이맵 | USGS 3DEP는 퍼블릭 도메인 + 직접 다운로드. 그래도 1~2 km 맵은 **절차 생성이 주, 실측은 선택** |
| 애니메이션 | Quaternius UAL은 itch.io 위젯 → **사람**. 대안: **Mesh2Motion(CC0, GitHub 직접)** 에 점프 3종·좌우 게걸음·뒷걸음 있음 |

---

## 1. 지형 바닥 PBR

### 1.1 출처와 라이선스

| 출처 | 라이선스 원문 | 다운로드 방법 | 비고 |
|---|---|---|---|
| ambientCG — https://docs.ambientcg.com/license/ | "You can copy, modify, distribute and perform the assets, even for commercial purposes, all without asking permission." (CC0 1.0) | **직접**. `https://ambientcg.com/get?file=<AssetId>_<1K|2K>-JPG.zip` → 302로 실제 zip. 예: `https://ambientcg.com/get?file=Grass001_1K-JPG.zip` (10.7 MB 확인) | 목록 API: `https://ambientcg.com/api/v2/full_json?id=<AssetId>&include=downloadData` (크기·링크 포함). 전체 2,934개(머티리얼 2,041) |
| Poly Haven — https://polyhaven.com/license | "You can use our assets for any purpose, including commercial work." (CC0) | **직접**. `https://dl.polyhaven.org/file/ph-assets/Textures/jpg/<1k|2k>/<id>/<id>_<diff|nor_gl|arm|rough|disp>_<1k|2k>.jpg` (2K 한 장 HEAD 200 확인) | 파일 목록 API: `https://api.polyhaven.com/files/<id>`, 전체 목록: `https://api.polyhaven.com/assets?t=textures` (869개) |

위험
- Poly Haven **API 약관**(https://raw.githubusercontent.com/Poly-Haven/Public-API/master/ToS.md): "By accessing and using the Poly Haven Public API … you agree to comply with the terms" / "all API calls must be made with a unique \"Referer\" header or user-agent that matches your software name". 상업 사용은 명시적으로 허용("free … for any purpose, including commercial use"). API를 쓰면 약관에 묵시 동의하는 구조이므로, 꺼림칙하면 API 없이 `dl.polyhaven.org` 직접 URL만 써도 된다. 쓸 때는 고유 User-Agent를 붙일 것.
- ambientCG의 `creationMethod` 값은 PBRProcedural / PBRPhotogrammetry / PBRApproximated / PBRMultiAngle / UnknownOrOther 뿐이다(API 전수 확인). AI 생성 분류는 없다. 다만 "AI 미사용" 문구 자체는 **미확인**.
- ambientCG API v3 주소도 200을 돌려주지만 내용은 보지 않았다(**미확인**).

### 1.2 무게

| | 1K | 2K |
|---|---|---|
| ambientCG zip (Color+NormalGL+NormalDX+Roughness+Displacement+AO 묶음) | 5~11 MB | 15~40 MB |
| Poly Haven 낱장 JPG (diff / nor_gl / arm) | 0.7~1.2 / 0.9~1.5 / 0.2~0.9 MB | 2.6~4.5 / 3.8~5.7 / 1.0~3.7 MB |

Poly Haven은 필요한 맵 3장만 받으면 재질당 1K 약 2~3.5 MB. `arm` = AO·Roughness·Metal 패킹이라 URP 마스크맵 변환이 쉽다.

### 1.3 후보 (전부 CC0 · 직접)

| 용도 | ambientCG (AssetId) | Poly Haven (id) | 적합 |
|---|---|---|---|
| 풀 | Grass001, Grass004, Grass005 (절차 생성형) | grass_path_2, grass_path_3, aerial_grass_rock | 보통 — ambientCG 풀은 잔디밭 느낌. 야생 초지는 Ground037·aerial_grass_rock 혼합 권장 |
| 흙·진흙 | Ground054, Ground071, Ground087, Ground106, Ground109 (사진측량) | brown_mud, brown_mud_02, brown_mud_leaves_01, brown_mud_rocks_01, dirt_floor | 좋음 |
| 자갈 | Gravel040, Gravel041, Gravel043 | gravel_ground_01, gravelly_sand, ganges_river_pebbles | 좋음 |
| 바위·절벽 | Rock030, Rock035, Rock051, Rock063, Rock064 | rock_face, rock_face_03, cliff_side, rocky_terrain_02, aerial_rocks_02 | 좋음 |
| 모래·해안 | Ground080, Ground079S | coast_sand_01~05, coast_sand_rocks_02, aerial_beach_01~03, damp_sand | 좋음 |
| 숲 바닥 | ScatteredLeaves001~009, Ground068(이끼) | forrest_ground_01, forest_floor, forest_leaves_02~04, leaves_forest_ground | 좋음 |
| 아스팔트·도로 | Asphalt031, Asphalt033, Road007, Road012A (+ RoadLines 69종 데칼) | asphalt_02, road_damaged, worn_asphalt, gravel_road | 좋음 |
| 콘크리트 | Concrete048, Concrete049, (이미 사용: Concrete015, 034) | concrete_pavement_02, dirty_concrete | 좋음 |
| 눈(선택) | Snow011~015 | snow_01~05, snow_field_aerial | 좋음 |

- Poly Haven `aerial_*`, `rocky_terrain*`, `coast_sand*`는 실제 크기가 15~90 m로 넓어서(API `dimensions`) 원경 타일 반복이 덜 보인다. 근경용(1~3 m)과 섞어 쓰면 좋다.
- M1 권장: 지형 타일당 레이어 4개 이하, 1K. (URP 지형은 레이어 4개를 넘으면 패스가 추가된다 — 일반 지식, 프로젝트에서 실측 필요.)

### 1.4 그 외 텍스처 사이트

| 사이트 | 확인 내용 | 판정 |
|---|---|---|
| ShareTextures — https://www.sharetextures.com/p/license | "custom CC0-based license" / "No automated downloads, hotlinking, or embedding direct downloads in third-party apps" | **에이전트 사용 불가**(자동 다운로드 금지). 순수 CC0도 아님 |
| cgbookcase — https://www.cgbookcase.com/textures | "The textures are published under the CC0 1.0 license" | 라이선스 OK. 직접 다운로드 URL 형식 **미확인** |
| Texture Ninja — https://texture.ninja/ | 자바스크립트 앱이라 본문을 읽지 못함 | **미확인** |
| 3dmodelscc0 — https://www.3dmodelscc0.com/ | 다른 도메인(alexisoctans.com)으로 넘어감 | **사이트 소멸로 보임. ASSETS.md에서 빼야 함** |

---

## 2. 바위·절벽

| 후보 | 출처 | tris (API `polycount`) | 형식 · 크기 | 적합 | 위험 |
|---|---|---|---|---|---|
| rock_07 / rock_09 | Poly Haven | 27,830 / 23,280 | FBX 1.2 MB + 텍스처 1K 2.6 MB | 좋음 | 수십 개 뿌리려면 2~5천 tris LOD 필요 |
| rock_face_01 / 02 | Poly Haven | 20,174 / 29,566 | FBX 0.8 MB, 1K 합계 4.2 MB | 좋음 — 절벽 면 | LOD 없음(API `lods` 없음) |
| moon_rock_01~07 | Poly Haven | 6,194~18,668 | 미측정 | 보통 — 가볍지만 달 암석 색 → 머티리얼 교체 | |
| boulder_01, stone_01 | Poly Haven | 123,976 / 71,916 | FBX 6.5 MB | 좋음 | 감량 필수 |
| namaqualand_boulder_02~06, cliff_01/02 | Poly Haven | 110,746~363,896 | cliff_01 FBX 2.9 MB | 좋음 — 건조 지대 색 | 감량 필수 |
| mountainside | Poly Haven | 287,755 | FBX 13.8 MB, 1K 합계 17.8 MB | 좋음 — 큰 절벽 한 덩이 | 감량 필수 |
| rock_moss_set_01 / 02 | Poly Haven | 63,127 / 57,647 (세트 합) | 미측정 | 좋음 — 이끼 낀 돌 묶음 | |
| coast_rocks_*, coastal_cliff_04 등 해안 암석 | Poly Haven | 100만~288만 | 미측정 | 쓰지 말 것(렌더용) | |
| Yughues rocks-02, rocks-03, free-lowpoly-rocks-set01/02 | https://opengameart.org/content/rocks-02 | 미확인(같은 작가 다른 팩은 수백~2천 tris) | rocks_02.7z 20 MB | 좋음 예상(미리보기 미열람) | 디퓨즈/노멀/스펙큘러 TGA → URP 변환 필요 |
| Yughues Free Photogrammetric Rock | https://opengameart.org/content/free-photogrammetric-rock | 미확인 | 7z 145 MB | 미확인 | 무거움 |

- Poly Haven 모델 URL: `https://dl.polyhaven.org/file/ph-assets/Models/fbx/<1k|2k>/<id>/<id>_<1k|2k>.fbx` + `textures/` 하위 파일(목록은 `https://api.polyhaven.com/files/<id>`의 `include`).
- **주의**: FBX 묶음의 노멀·러프니스는 **EXR**이다. glTF 묶음은 JPG다. 메시는 FBX로 받고 텍스처는 `Diffuse / nor_gl / arm` 키의 JPG를 따로 받는 편이 가볍다.
- API의 `lods: true`가 무엇을 뜻하는지(파일 안에 LOD 메시가 있는지)는 **미확인**.
- ambientCG 3D 모델 34개는 빵·과일뿐이고 환경용은 `3DTreeStump001`, `3DStick001` 둘뿐이다.

---

## 3. 나무·덤불·풀

### 3.1 Poly Haven 식생 (CC0 · 직접, 그러나 무겁다)

| id | tris | FBX 크기(1K 텍스처 포함) | 판정 |
|---|---|---|---|
| pine_tree_01 | 17,427,094 | 704 MB | 사용 불가 |
| fir_tree_01 | 7,853,731 | 271 MB | 사용 불가 |
| tree_small_02 | 4,652,585 | 96 MB | 사용 불가 |
| island_tree_01 / 02 / 03 | 3.7M / 1.76M / 4.76M | 02 = 54 MB | 사용 불가 |
| jacaranda_tree | 312,356 | 165 MB | 그대로는 불가 |
| quiver_tree_01 / 02 | 269,522 / 153,887 | 미측정 | 사막 수종, 감량하면 가능 |
| fir_sapling, pine_sapling_small | 433,021 / 398,144 | 미측정 | 불가 |
| shrub_01 / 02 / 03 / 04 | 282,224 / 52,317 / 16,955 / 47,813 | 02 = 2.7 MB, 03 = 2.2 MB | 03만 근경 소량 가능 |
| fern_02, weed_plant_02, nettle_plant | 6,232 / 17,062 / 46,195 | fern_02 = 1.7 MB | fern_02 좋음 |
| grass_medium_01 / 02, grass_bermuda_01 | 1.6M / 1.04M / 223,596 | 01 = 5 MB | 불가(풀 한 무더기에 백만 tris) |
| tree_stump_01 / 02, dead_tree_trunk | 41,046 / 62,345 / 101,802 | 미측정 | 감량 후 소품으로 |

솔직한 평가: Poly Haven 나무는 모양은 가장 좋지만(썸네일 확인: 사진 같은 활엽수) 영화·건축 렌더용이다. 숲 수백 그루를 M1에서 돌릴 수 없다. 자동 감량도 잎 카드가 뭉개져서 결과가 나쁘다.

### 3.2 게임용 후보

| 후보 | 출처 | 라이선스 | 다운로드 | 무게 | 적합 | 위험 |
|---|---|---|---|---|---|---|
| Yughues Bushes | https://opengameart.org/content/bushes | CC0 (페이지 표기) | **직접** `https://opengameart.org/sites/default/files/bushes.7z` (13 MB) | 5종, 384~2,044 tris, TGA 1024 (diffuse+opacity/normal/specular/translucency) | **좋음** — 사진 텍스처 알파 카드(미리보기 확인) | 2012~2015년 제작, 스펙큘러 워크플로 |
| Yughues Free Hedges / Tiny Weeds / Grass Pack 01~03 | https://opengameart.org/content/free-hedges , https://opengameart.org/content/tiny-weeds , https://opengameart.org/content/grass-pack-01 | CC0 | **직접** (12 MB / 3.7 MB / 1.5 MB) | 미확인 | 좋음 예상(미열람) | 〃 |
| Yughues 야자·열대 식물 (free-palm-treez-v3, tropical-shrubs 등) | https://opengameart.org/users/yughues 목록 | 미확인(개별 페이지 안 열음) | 미확인 | 미확인 | 해안 구역 후보 | |
| ambientCG 잎·가지 아틀라스: LeafSet001~030, Foliage001~008, PineNeedles001 | ambientCG API | CC0 | **직접** (1K zip 2.3~5 MB) | 스캔 아틀라스 | 좋음 — 나무를 직접 만들 때 잎 재료 | 모델이 아니라 텍스처다 |
| ambientCG Bark012~016 / Poly Haven 나무껍질 텍스처 24종 | 〃 | CC0 | **직접** | 1K 9~10 MB | 좋음 | |
| Quaternius Ultimate Nature / Stylized Nature MegaKit | https://quaternius.com/packs/ultimatenature.html | CC0 | 구글 드라이브 / itch 위젯 | — | **나쁨** — 단색 로우폴리, 이미 거절된 스타일 | |
| Kenney Nature Kit | https://kenney.nl/assets/nature-kit | CC0 | 직접(10.5 MB) | — | **나쁨** — 카툰 | |
| Unity "Terrain Asset Pack \| Terrain Sample" | https://assetstore.unity.com/packages/3d/environments/landscapes/terrain-asset-pack-terrain-sample-145808 | "Standard Unity Asset Store EULA" (CC0 아님), FREE, Unity Technologies | **사람**(Unity 계정) | 1.6 GB, v2.1.0 (2026-09-17), URP 호환 표기 | 미확인 — 페이지 설명이 비어 있어 나무 포함 여부를 확인 못 함 | EULA 검토는 사람 몫 |
| Unity "URP Terrain \| Terrain Sample Project" | https://assetstore.unity.com/packages/3d/environments/landscapes/unity-urp-terrain-terrain-sample-project-213197 | 〃 | **사람** | 2.8 GB, v1.1.0, URP 6000.3 호환 표기 | 미확인(내용물 표기 없음) | 〃 |

검색에서 걸린 함정(쓰면 안 됨): summerengine.com의 "CC0 realistic oak/birch", meshy.ai 나무, aitextured.com 텍스처 — **AI 생성물**이다(절대 규칙 2 위반).

### 3.3 현실적인 대안 (나무)

1. **코드/블렌더로 나무 직접 제작**: 줄기 = 원뿔대 메시 + 나무껍질 PBR(ambientCG Bark, Poly Haven bark). 잎 = ambientCG LeafSet/Foliage 아틀라스를 붙인 교차 카드 20~60장. 한 그루 1,500~4,000 tris, 원경은 빌보드 임포스터. 수종 3~4개 × 변형 3개면 숲이 된다. 전부 CC0 재료 + 자체 제작이라 대장 기록이 깔끔하다.
2. Poly Haven `fir_tree_01` 등에서 **잔가지(twig) 텍스처만** 가져와 1번의 카드에 쓰기(`twig_diff / twig_alpha / twig_nor_gl` 맵이 따로 제공됨, 1K). 모양이 훨씬 사실적이다.
3. 사람이 Unity Terrain Sample 팩을 받아 내용 확인(나무가 들어 있으면 가장 빠른 길. CC0는 아님).
4. 첫 패스는 숲을 "덤불 + 바위 + 드문 나무"로 구성해 나무 수 자체를 줄인다.

---

## 4. 건물·산업·마을 소품

### 4.1 쓸 만한 것 (CC0 · 직접)

| 후보 | 출처 | tris | 크기 | 적합 | 위험 |
|---|---|---|---|---|---|
| modular_factory_facade | Poly Haven | 175,014 (키트 전체) | FBX 3.8 MB, 텍스처 포함 1K 66 MB / glTF 1K 17.6 MB | **좋음** — 벽돌 공장 외벽 모듈(창·문·차고문), 썸네일 확인 | 텍스처 세트 5개(brick/doors/garage/trim/windows) → 1K로 제한 |
| modular_urban_apartments_facade | Poly Haven | 118,215 | 미측정 | 좋음 — 마을용 아파트 외벽 | 〃 |
| modular_chainlink_fence | Poly Haven | 89,232 (키트) | 1K 16 MB | 좋음 | 철망을 메시로 만든 듯 → 알파 카드로 교체 검토 |
| modular_industrial_pipes_01 / modular_pipes | Poly Haven | 12,340 / 47,428 | 미측정 | 좋음 | |
| modular_electricity_poles | Poly Haven | 200,610 (키트) | 미측정 | 좋음 | 감량 |
| concrete_road_barrier / _02 | Poly Haven | 80,776 / 42,578 | 1K 8 MB | 좋음 — 엄폐물 | 감량 필수(상자 하나에 8만 tris) |
| street_lamp_01 / 02, security_light | Poly Haven | 30,610 / 20,338 / 4,668 | 01 = 3.6 MB | 좋음 | |
| covered_car | Poly Haven | 12,592 | 1K 3.5 MB | **좋음 — 천 덮인 차라 상표 문제가 없다** | 한 종류뿐 |
| Barrel_01/02, barrel_03, propane_tank, small_lpg_tank, old_tyre, portable_generator, exterior_aircon_unit, utility_box_01/02, metal_trash_can, plastic_crate_03, cardboard_box_01, hand_truck, fire_hydrant | Poly Haven | 1,473~86,316 | 미측정 | 좋음 | 라벨·상표 유무는 임포트 때 육안 확인(**미확인**) |
| modular_wooden_pier, lifebuoy | Poly Haven | 84,780 / 10,728 | 미측정 | 좋음 — 해안 | |
| rollershutter_door / window_01~03, modular_fire_escape, modular_metal_gutter, modular_airduct_* | Poly Haven | 1,104~45,090 | 미측정 | 좋음 — 상자형 건물에 붙이는 디테일 | |
| Yughues Modular building | https://opengameart.org/content/modular-building-asset-0 | 34조각, 조각당 12~154 tris | 7z 70 MB, .obj+.fbx, TGA 4096 (diffuse/normal/specular) | **좋음** — 낡은 벽돌·콘크리트 공장 벽(미리보기 확인), 매우 가벼움 | 4K TGA → 1K~2K로 축소. 스펙큘러 워크플로 |
| Yughues Concrete barriers / Concrete pipes / Hesco box | https://opengameart.org/content/concrete-barriers , https://opengameart.org/content/concrete-pipes , https://opengameart.org/content/hesco-box | 미확인 | 62 MB / 4.7 MB / 24 MB | 좋음 예상(미열람) | 〃 |
| Yughues Fuel-oil tank / Gas tank / Metal barrels / Wood pallet / Trashes | https://opengameart.org/content/fueloil-tank-pack , https://opengameart.org/content/gaz-tank-pack , https://opengameart.org/content/metal-barrels-pack , https://opengameart.org/content/wood-pallet-v1-pack , https://opengameart.org/content/free-trashes | 미확인 | 6 / 5.9 / 22 / 3 / 25 MB | 좋음 예상(미열람) | 드럼통·쓰레기 텍스처에 실존 상표가 있는지 확인 필요(**미확인**) |

Yughues 파일은 전부 `https://opengameart.org/sites/default/files/<파일명>` 직접 링크이고 HEAD 200을 확인했다. `.7z`가 많다(macOS `bsdtar`로 풀 수 있음). 페이지 라이선스 표기는 CC0, 작성자 Yughues(Nobiax).

### 4.2 쓰면 안 되는 것 (스타일)

| 출처 | 확인 | 판정 |
|---|---|---|
| Kenney City Kit Industrial / Commercial / Suburban / Roads, Car Kit, Survival Kit, Modular Buildings, Watercraft | https://kenney.nl/assets/city-kit-industrial 샘플 이미지 확인: 단색·둥근 모서리 장난감 도시. CC0("all game assets on the asset pages are public domain licensed (CC0)" — https://kenney.nl/support). 직접 다운로드 가능: `https://kenney.nl/download-link/<hex("assets/<slug>")>` 가 서명된 URL을 JSON으로 준다(Referer 헤더 필요, zip 2~10 MB) | **나쁨**. Kenney 3D 키트 중 세미리얼에 맞는 것은 없다. Retro Urban Kit은 미열람 |
| KayKit City Builder Bits 등 | https://kaylousberg.itch.io/city-builder-bits : "Textured using a single gradient atlas texture (1024x1024)" / CC0 / itch 위젯 | **나쁨**(그라디언트 로우폴리) + 사람 |
| Quaternius Buildings, Cars, Modular Streets, Public Transport, Zombie Apocalypse Kit, Survival | https://quaternius.com/faq.html : "Most packs are textured using an Atlas Texture, where a single image with colored squares is used". 구글 드라이브 폴더 배포 | **나쁨**(단색 로우폴리) |
| Quaternius Downtown City MegaKit | https://quaternius.com/packs/downtowncitymegakit.html — itch 위젯 | 사람 + 스타일 미열람 |
| Sketchfab CC0 | 다운로드는 로그인 필요(일반적으로 알려진 사실, 이번에 API로는 확인 실패 — **미확인**) | 사람. 개별 모델 URL은 조사하지 못함 |

### 4.3 모듈 건물: 코드 상자 + PBR이 현실적인가? → **그렇다**

- CC0 직접 다운로드로 얻을 수 있는 "완성된 세미리얼 건물"은 없다. 있는 것은 외벽 모듈(Poly Haven 2종, Yughues 1종)뿐이다.
- 창고·주택·컨테이너·담장은 **코드로 만든 상자/박공지붕 메시 + 타일링 PBR**이 가장 일관되고 가볍다(지금 공장 블록아웃 방식의 연장).
- 재료(ambientCG, CC0 · 직접): CorrugatedSteel 15종(창고 벽·지붕·컨테이너), SheetMetal 4, MetalWalkway 14, Bricks 132, Facade 26(창문 포함 외벽 타일), PaintedPlaster 18, WoodSiding 13, Planks 60, RoofingTiles 27, Concrete 62, PavingStones 156, Fence 12(알파 울타리), Door 2, ManholeCover, RoadLines 69, AsphaltDamage, Leaking(누수 얼룩 데칼), GraffitiSet001, Rust.
- Poly Haven 지붕 텍스처 23종(corrugated_iron, box_profile_metal_sheet, clay_roof_tiles 등), factory_wall.
- 상자 건물이 밋밋해 보이지 않게 하는 요소: 지붕 처마·테두리 돌출, 창문 깊이, 배관·덕트·에어컨·셔터(4.1의 Poly Haven 소품), 얼룩 데칼, 버텍스 컬러 변화.
- 컨테이너: 상자 + 골함석 노멀 + PaintedMetal 색 3~4종 + 끝면 문 디테일(코드). 로고·글자는 넣지 않는다.

---

## 5. 하늘 (Poly Haven HDRI, CC0 · 직접)

URL: `https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/<1k|2k|4k>/<id>_<res>.hdr`. 실측(overcast_soil_puresky): 1K 1.2 MB, 2K 4.6 MB, 4K 16.9 MB. 다른 HDRI의 크기는 미측정.
"pure skies" 분류 59종(API 확인). 낮 시간대 후보:

| 분위기 | id (API 태그) |
|---|---|
| 흐림(현재 사용) | overcast_soil_puresky |
| 흐림 대안 | kloofendal_overcast_puresky, mud_road_puresky(한낮·흐림), kloofendal_misty_morning_puresky |
| 구름 조금 | kloofendal_48d_partly_cloudy_puresky, kloofendal_38d_partly_cloudy_puresky, sunflowers_puresky, kloppenheim_03_puresky, farm_field_puresky |
| 맑음 | kloofendal_43d_clear_puresky, qwantani_noon_puresky, autumn_field_puresky, pizzo_pernice_puresky |

넓은 야외에서는 방향광 그림자가 지형을 읽기 쉽게 하므로 "구름 조금" 1종을 흐림과 비교해 볼 만하다.

---

## 6. 물

| 후보 | 결과 |
|---|---|
| ambientCG | 물 표면 재질 없음(태그 water는 누수 데칼·젖은 바닥뿐). Foam001~003은 "Foam" 분류지만 바다 거품인지 **미확인**(스펀지류일 가능성) |
| Poly Haven | 물 텍스처 없음 |
| OpenGameArt (CC0, "water normal") | 맞는 결과 없음 |
| 웹 검색 | aitextured.com, summerengine.com = AI 생성 → 금지. cadhatch.com = 라이선스 불명 |

**대안(권장)**: 물 노멀맵 2장과 거품 마스크를 **에디터 스크립트로 생성**(고정 시드 노이즈/파동 합성 → 높이 → 노멀, 타일링). `FabricNoise.png`와 같은 "자체 제작, AI 아님" 범주다. 셰이더는 URP Shader Graph 또는 HLSL로 노멀 2장 스크롤 + 깊이 페이드 + 가장자리 거품. 젖은 물가는 Poly Haven damp_sand / coast_sand 계열로 처리.

---

## 7. 높이맵: 실측 데이터 vs 절차 생성

| 데이터 | 라이선스 원문 | 다운로드 | 해상도 · 크기 | 판정 |
|---|---|---|---|---|
| USGS 3DEP (The National Map) — https://www.usgs.gov/faqs/what-are-terms-uselicensing-map-services-and-data-national-map | "Map services and data downloaded from The National Map are free and in the public domain. There are no restrictions" (출처 표기는 요청 사항) | **직접** `https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/current/n38w123/USGS_13_n38w123.tif` (HEAD 200) | 1/3 각초(약 10 m), 1°×1° 타일 223 MB. 1 각초(약 30 m) 타일 28.5 MB | **사용 가능**. 미국 지형만 |
| NASA SRTM | "data provided from a NASA-led mission are licensed as Creative Commons Zero (CC0)" — https://www.earthdata.nasa.gov/engage/open-data-services-software-policies/data-use-guidance | 공식 경로는 Earthdata 로그인(→ 사람). 시도한 직접 URL은 404 | 약 30 m | 라이선스 OK, 공식 다운로드는 사람 |
| AWS Terrain Tiles (SRTM·3DEP 등 재배포) — https://registry.opendata.aws/terrain-tiles/ | "Attribution is required for many terrain tile data providers" — https://github.com/tilezen/joerd/blob/master/docs/attribution.md | **직접** `https://s3.amazonaws.com/elevation-tiles-prod/skadi/N37/N37E128.hgt.gz` (HEAD 200, 15 MB) | 약 30 m | 출처가 지역마다 다름. 미국·SRTM 구간은 USGS 표기로 충분, 그 외 지역은 개별 확인 필요 |
| Copernicus DEM GLO-30 — https://registry.opendata.aws/copernicus-dem/ | "available on a free basis for the general public under the terms and conditions of the Licence" (CC0 아님, 표기 의무 있음. 라이선스 PDF 본문은 **미확인**) | **직접** `https://copernicus-dem-30m.s3.amazonaws.com/Copernicus_DSM_COG_10_N37_00_E128_00_DEM/Copernicus_DSM_COG_10_N37_00_E128_00_DEM.tif` (HEAD 200, 45 MB) | 30 m, 표면 모델(건물·수목 포함) | 약관 문서 검토가 필요 → 보류 권장 |
| ambientCG Terrain001~005 | CC0 | **직접** `https://ambientcg.com/get?file=Terrain001_2K.exr` (16.9 MB) | 2K EXR 높이맵(절차 생성으로 보임, `creationMethod` UnknownOrOther) | 사용 가능 — 바로 쓸 수 있는 높이맵. 실제 크기 **미확인** |

판단
- 1~2 km 맵에서 30 m 격자는 가로 33~66 샘플뿐이라 의미가 없다. 10 m(USGS 1/3")도 100~200 샘플 — 큰 윤곽만 준다.
- GeoTIFF를 읽으려면 변환 도구(GDAL 등)가 필요하고, 게임 플레이(엄폐·시야·도로 경사)에 맞춰 어차피 고쳐야 한다.
- **권장: 절차 생성이 주**(구역 마스크 + 능선·계곡 노이즈 + 도로·건물 부지 평탄화, 고정 시드, 에디터 스크립트 — "구역 레이아웃은 스크립트로 생성" 원칙과 일치). 실측이 필요하면 USGS 1/3" 한 조각을 큰 윤곽 참고용으로만 쓴다.

---

## 8. 애니메이션 (8방향 이동, 점프 시작/공중/착지)

| 후보 | 라이선스 원문 | 배포 방식 | 내용 | 판정 |
|---|---|---|---|---|
| Quaternius Universal Animation Library — https://quaternius.itch.io/universal-animation-library | "Free to use in personal, educational and commercial projects. (CC0 License)" / itch 표기 "No generative AI was used" | quaternius.com의 "Download here" 버튼도 **itch.io 결제 위젯**(`Itch.attachBuyButton`)이다. 직접 zip 없음. 유료 등급은 Patreon/itch | 120+ 클립, "Locomotion in 8 directions, jog, sprint, push, crawling, swimming", 루트모션 유/무 2벌(v3.0, 2026-06-16), Unity용 내보내기 포함. Standard zip 15 MB. 점프 클립 이름은 **미확인** | **최선. 사람이 받아야 함** |
| Universal Animation Library 2 — https://quaternius.itch.io/universal-animation-library-2 | 〃 (CC0) | itch 위젯 | 130+ 클립(파쿠르, 근접 콤보, 좀비 이동 등). Standard zip 17 MB | 선택. 사람 |
| **Mesh2Motion** — https://github.com/Mesh2Motion/mesh2motion-app | "The art assets (3d models, rigs, animations) are all licensed under CC0." (README), `LICENSE-CC0.MD`: "All 3d models, blend files, rigs, animations" | **직접** `https://raw.githubusercontent.com/Mesh2Motion/mesh2motion-app/main/static/assets/human/animations/<이름>.glb` (jump-start.glb 65 KB, HEAD 200) | 사람 클립 약 270개, 각 50~65 KB. 관련 클립: `walk`, `jog`, `sprint`, `walk-backwards`, `strafe-left`, `strafe-right`, `jump-start`, `jump-air`, `jump-land`, `jump-running`, `freefall`, `crouch-idle`, `crouch-walk`, `rifle-idle`, `rifle-walk`, `rifle-run`, `rifle-fire`, `rifle-reload`, `pistol-*`, `roll`, `death-a~d`, `grenade-throw`, `climb-ladder`, `swim-*` | **에이전트가 지금 쓸 수 있는 유일한 후보**. 대각선 4방향은 없음 → 전·후·좌·우 4클립을 2D 블렌드 트리로 섞어 8방향 구성 |
| CMU Mocap — https://mocap.cs.cmu.edu/ | "You may include this data in commercially-sold products, but you may not resell this data directly, even in converted form." / FAQ: "may be copied, modified, or redistributed without permission" | FAQ 페이지의 zip 직접(`allasfamc.zip` 등, 크기 미확인). "Please don't crawl this database!" | ASF/AMC, C3D 원시 모캡. 루프·정리 안 됨, 게임용 아님 | CC0는 아니지만 상업 사용 허용. 정리 비용이 커서 **비추천** |
| Kenney Animated Characters | CC0 | 직접 | 클립 수 적고 카툰 캐릭터용. 내용 미열람 | 비추천 |
| Rokoko 무료 모캡 — https://www.rokoko.com/mocap/free-mocap-assets | "263 high-quality character animation assets that you can use in any animation, VFX, game, 3D project you want, including for commercial use." (라이선스 전문 **미확인**) | 이메일 입력 폼(마케팅 수신 동의 포함) → **사람** | 내용 미확인 | 사람 + 약관 검토 |
| Mixamo — https://www.mixamo.com | **미확인**(Adobe FAQ가 403) | Adobe 계정 로그인 → **사람** | 8방향 스트레이프·점프 풍부 | ASSETS.md대로 blocked 유지 |

Mesh2Motion 위험
- 클립별 원작자·제작 방식(손 애니메이션/모캡)은 저장소에 표기가 없다 → **미확인**. 대장에는 저장소 단위 CC0 + 커밋 해시를 기록할 것.
- 형식이 GLB뿐이다. Unity에 넣으려면 glTF 임포터 패키지 또는 블렌더 변환(FBX)이 필요하다(패키지 라이선스는 P 대장에 추가).
- 골격이 현재 Swat 캐릭터와 다르다 → Humanoid 리타깃 검증 필요(`static/assets/human/skeleton.glb` 19 KB).
- 품질은 UAL보다 낮을 가능성이 크다(README: "I am also not the best animator"). 임시 대체로 보고, 사람이 UAL을 받아 주면 교체한다.

사람이 눌러야 하는 것(UAL): 페이지 열기 → "Download Now" → 금액 0으로 두고 "No thanks, just take me to the downloads"(itch 표준 문구, 이번에 직접 확인은 못 함) → `Universal Animation Library[Standard].zip` 받기 → `AssetStaging/`에 넣기. 계정은 필요 없다(HUMAN_TODO.md 기존 항목과 동일).

---

## (a) 첫 패스 추천 목록 (1~2 km 맵, 전부 CC0 · 직접)

| 분류 | 고를 것 | 예상 용량 |
|---|---|---|
| 지형 레이어(1K) | 풀: Poly Haven aerial_grass_rock + ambientCG Grass004 / 흙: brown_mud_leaves_01 / 자갈: gravel_ground_01 / 바위: rocky_terrain_02 + ambientCG Rock051 / 모래: coast_sand_rocks_02 / 숲 바닥: forrest_ground_01 / 아스팔트: asphalt_02 + ambientCG RoadLines / 콘크리트: 기존 Concrete015 | 약 40 MB |
| 바위 | Poly Haven rock_07, rock_09, rock_face_01, rock_face_02, boulder_01(감량), mountainside(감량) + Yughues rocks-02 | 약 60 MB |
| 식생 | Yughues bushes, free-hedges, tiny-weeds, grass-pack-01 + Poly Haven fern_02, shrub_03 + **자체 제작 나무 3~4종**(ambientCG Bark + LeafSet/Foliage, Poly Haven fir_tree_01의 twig 텍스처) | 약 50 MB |
| 공장 | Poly Haven modular_factory_facade, modular_chainlink_fence, modular_industrial_pipes_01, concrete_road_barrier_02, Barrel_01, propane_tank, portable_generator, covered_car + Yughues modular-building, concrete-barriers, concrete-pipes, hesco-box, fueloil-tank-pack, metal-barrels-pack, wood-pallet | 약 250 MB(원본), 임포트 후 1K로 축소 |
| 마을 | 코드 상자 건물 + ambientCG Bricks / PaintedPlaster / WoodSiding / RoofingTiles / Facade / CorrugatedSteel / Door + Poly Haven street_lamp_01, utility_box_01, rollershutter_door, modular_electricity_poles | 약 60 MB |
| 해안·습지 | Poly Haven modular_wooden_pier, damp_sand, brown_mud + 코드 생성 물 노멀 | 약 15 MB |
| 하늘 | overcast_soil_puresky 유지 + kloofendal_48d_partly_cloudy_puresky 2K 비교 | 약 5 MB |
| 지형 형상 | 절차 생성(고정 시드). 필요하면 ambientCG Terrain001 2K EXR 참고 | — |
| 애니메이션(임시) | Mesh2Motion: walk, jog, sprint, walk-backwards, strafe-left/right, jump-start/air/land, crouch-idle/walk, rifle-* | 약 1 MB |

공통 작업: 스캔 모델은 임포트 때 LOD 생성·감량(바위 2~5천 tris 목표), 텍스처 1K 기본, EXR 대신 JPG 맵 사용, Yughues TGA는 축소 + 스펙큘러→스무스니스 변환, 모든 항목 ASSET_LEDGER 기록, 라벨·상표 육안 점검.

## (b) 사람이 받아야 하는 목록

| 우선 | 무엇 | URL | 절차 |
|---|---|---|---|
| 1 | Quaternius Universal Animation Library (Standard, 15 MB, CC0) | https://quaternius.itch.io/universal-animation-library | Download Now → 0원 → 다운로드 → `AssetStaging/` |
| 2 | Universal Animation Library 2 (Standard, 17 MB, CC0) | https://quaternius.itch.io/universal-animation-library-2 | 〃 |
| 3 | Unity Terrain Asset Pack (1.6 GB, 무료, Asset Store EULA) — 나무·풀이 들어 있는지 확인용 | https://assetstore.unity.com/packages/3d/environments/landscapes/terrain-asset-pack-terrain-sample-145808 | Unity 계정 로그인 → Add to My Assets → EULA 판단은 사람 |
| 4 (선택) | Unity URP Terrain Sample Project (2.8 GB) | https://assetstore.unity.com/packages/3d/environments/landscapes/unity-urp-terrain-terrain-sample-project-213197 | 〃 |
| 5 (선택) | Rokoko 무료 모캡 263개 | https://www.rokoko.com/mocap/free-mocap-assets | 이메일 입력 → 메일 링크. 약관 확인 필요 |
| 6 (선택) | Quaternius Downtown City MegaKit (스타일 확인 후) | https://quaternius.com/packs/downtowncitymegakit.html | itch 위젯 |
| — | Sketchfab CC0(차량 잔해, 컨테이너, 주택) | 개별 URL 조사 못 함 | 로그인 후 CC0 필터로 검색. 상표·AI 생성 여부를 사람이 확인 |
| — | Mixamo | https://www.mixamo.com | Adobe 로그인. UAL을 받으면 불필요 |

## (c) 구멍과 우회책

| 구멍 | 왜 | 우회 |
|---|---|---|
| 게임용 세미리얼 **나무** | Poly Haven은 31만~1,740만 tris, 다른 CC0 나무는 로우폴리 카툰이거나 AI 생성 | 3.3의 자체 제작(껍질 PBR + 스캔 잎 아틀라스 카드 + 빌보드 LOD). 사람이 Unity Terrain 팩 내용 확인 |
| **주택·창고 완성 건물** | CC0 직접 다운로드에는 외벽 모듈뿐 | 코드 상자 건물 + PBR + Poly Haven 디테일 소품(4.3) |
| **컨테이너** | 세미리얼 CC0 직접 다운로드 못 찾음 | 코드 상자 + CorrugatedSteel / PaintedMetal |
| **차량(엄폐물)** | Kenney·Quaternius는 카툰. Poly Haven은 covered_car 하나 | covered_car 색·크기 변형 반복 + 사람이 Sketchfab CC0 잔해 차량 탐색. 그 전까지는 방벽·컨테이너로 엄폐 구성 |
| **물 노멀·거품** | CC0 직접 다운로드 없음 | 코드 생성(6장) |
| **대각선 스트레이프 클립** | Mesh2Motion에 없음. UAL은 사람 | 4방향 블렌드 트리, UAL 도착 후 교체 |
| 풀 지형 디테일 메시 | Poly Haven 풀은 백만 tris | Yughues grass pack + 지형 디테일(교차 쿼드)용 알파 텍스처 |
| 바위 LOD | 스캔 원본이 무겁다 | 임포트 파이프라인에서 감량(Unity 메시 LOD 자동 생성 기능 지원 여부는 6000.3에서 확인 필요 — **미확인**) |

## 정리가 필요한 기존 문서 내용 (이 조사에서는 수정하지 않음)

- ASSETS.md의 "3dmodelscc0": 사이트가 다른 도메인으로 넘어간다 → 삭제 후보.
- ASSETS.md "Kenney Car Kit / Quaternius LowPoly Cars": 스타일 불일치(카툰) → 차량 계획 재검토.
- ASSETS.md에 OpenGameArt의 **Yughues(Nobiax)** 팩과 **Mesh2Motion**을 승인 소스로 추가할 만하다.
- ShareTextures는 자동 다운로드 금지 약관이 있어 에이전트 소스로 부적합.

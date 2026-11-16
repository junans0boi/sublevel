# 05. 맵 리소스 — 합법적으로 쓸 수 있는 것 (1980년대 한국 + 동남아 열대)

조사일 2026-11-16. 조사만 했다(코드·에셋 수정/다운로드 없음). 확인 수단: Poly Haven 공개 API, Sketchfab 검색 API, OpenGameArt·Quaternius 페이지, 웹 검색, 기존 `docs/research/open-world-assets.md`(2026-11-13, 중복 내용은 요약만).
표기: **확인** = 오늘 1차 출처(API/페이지)로 확인. **지식** = 일반 지식이며 오늘 재확인 안 함. **미확인** = 못 봄.
범위 밖(다른 트랙): 캐릭터, 마을 배치안, 산, 애니메이션, 누락 감사. Poly Haven **텍스처 선별은 배치안 확정 후**라 여기선 적합성만 짧게 쓴다.

---

## 0. 결론 (한 눈에)

1. **"완성된 한국 80년대 건물/동남아 건물" CC0 에셋은 없다.** 유료(CGTrader 등 로열티프리 한옥·동남아 주택)만 있고, 1980년대 한국 서민 주택은 아예 안 나왔다(웹 검색, 오늘). → **건물은 코드로 생성 + 텍스처**가 맞다.
2. **Poly Haven 모델은 소품·조각용으로 쓴다.** 건물/나무는 없다(야자·대나무·바나나 나무 없음, 나무는 31만~1,740만 tris). 한국 소품 2종(`korean_*`)이 있다.
3. **열대 나무(야자·대나무·바나나)는 CC0 중 사진 텍스처 게임용은 Yughues(OpenGameArt) 야자 정도**, 그 외는 Sketchfab **CC-BY**(크레딧 필요, 이미 차량 8종이 같은 방식으로 대장에 있음). 가장 일관된 길은 **기존 나무 제작기(WorldVegetation)를 확장해 야자/바나나/대나무를 자체 제작**.
4. Quaternius/Kenney/KayKit은 CC0지만 단색·그라디언트 로우폴리 → 세미리얼 톤과 충돌(이미 ADR로 거절된 스타일). 소품 일부만 가능.
5. Unity Asset Store 무료는 EULA(CC0 아님). 열대 나무 무료 팩이 있으나 **사람 계정·EULA 판단 필요**, 에이전트는 담지 않는다.

---

## 1. 현재 프로젝트가 이미 가진 것 (ASSET_LEDGER·코드 확인)

| 분류 | 있는 것 | 출처/라이선스 |
|---|---|---|
| PBR 텍스처 | ambientCG 1K 20종(A-0001~28: Concrete, Bricks097, PaintedPlaster017, CorrugatedSteel005, RoofingTiles014B, Planks039, Ground037/054/081, Rock051, Asphalt015, Bark012/014 …) + 2K 5종(T-0002) + Poly Haven 2K 13종(T-0001: aerial_grass_rock, grass_ground, dirt_floor, gravel_*, concrete_wall_003, asphalt_02/05, rusty_metal_02, oak_wood_planks …) | CC0 |
| 모델 | Poly Haven rock_07, rock_09, rock_moss_set_01 (A-0021~23, LOD 내장) | CC0 |
| 하늘 | Poly Haven HDRI 2종 (A-0004, A-0005) | CC0 |
| 차량 | 8종 Sketchfab (Fairheaven/Lightbody/Shvan '80s 계열 가상 차종, 오토바이 2, 승용 팩) V-0001~8 | **CC BY 4.0** — 크레딧 화면 반영 규칙이 이미 있음 |
| 식생 | **자체 제작**(ADR-070): 활엽·침엽·덤불·풀·꽃·갈대, ambientCG 껍질+잎 아틀라스 카드+빌보드 LOD (`WorldVegetation.cs`, `Scripts/Core/World/Vegetation.cs`) | CC0 재료 + 자체 코드 |
| 건물 | **코드 생성 박스 조립**: `BuildingKind {House, Warehouse, FactoryHall, Shed, ElevatorStation}`, 가구·방 (`Buildings.cs` 1,102줄), 지붕 프리즘(`WorldMeshes.AddPrism`), 마을 디테일(차양·간판·울타리(picket)·우편함·가로등·급수탑·광고판·잔해, `WorldBuilder.Town.cs`), 전신주+전선(`WorldBuilder.Dressing.cs`) | 자체 코드 |
| 구역 | `RegionKind {Mountain, Wetland, Town, Factory}` (`WorldSpec.cs`) | — |

**없는 것(이번 주제의 구멍)**: 야자·바나나·대나무·열대 활엽, 고상(高床) 가옥, 논/밭 지형, 비닐하우스, 감시탑, 학교·병영 건물종, 태양광 패널, 다리, 사원/유적, 붉은 토양 재질, 한국 소품(소화기·공중전화·장독).
참고: `PropKind`·`BuildingKind`에 종 추가는 파일 구조/게임 룰이 아니라 코드 확장이지만, 신규 enum/파일은 사전 확인 대상(CLAUDE.md 전역 규칙).

---

## 2. 후보 소스 상세

### 2.1 Poly Haven 모델 (CC0 · 확인 2026-11-16)

라이선스 원문: https://polyhaven.com/license — "All assets … licensed as CC0 … You can use our assets for any purpose, including commercial work … You can redistribute them" (확인). 사설 저장소에 원본 커밋 OK. API(`api.polyhaven.com`)는 고유 User-Agent 요구(기존 조사: 약관).
모델 전체 522종을 API로 훑었다. **건물·차량·야자·대나무·바나나 나무는 없다.** 관련 있는 것만(tris = API `polycount`, 크기 = 가로·세로·높이 mm→m 환산 불가 항목은 생략):

| 용도 | id | tris | 적합/주의 |
|---|---|---|---|
| **한국 소품** | `korean_fire_extinguisher_01` | 9,913 | 한글 "소화기" 받침 — **좋음**. 썸네일에 **실제 제조사 라벨**이 보임 → 텍스처 라벨 부분 지우기 필요(상표 위생) |
| 〃 | `korean_public_payphone_01` | 48,132 | 80~90년대 공중전화. 전면 **통신사 로고로 보이는 마크** → 라벨/마크 제거 후 사용, 4.8만 tris는 LOD 필요 |
| 열대 식물(지피) | `pachira_aquatica_01` 76,914 / `anthurium_botany_01` 4,134 / `calathea_orbifolia_01` 16,688 / `fern_02` 6,232 / `bananas`(열매 송이) 46,868 | | 정글 하층부 채우기. pachira는 감량(LOD) 필요, fern_02·anthurium은 바로 가능 |
| 식민지풍 성채 | `modular_fort_01` | 28,218 | 모듈 석조 벽 — 동남아 유적/요새 조각으로 **보통**(스타일은 유럽 성) |
| 목재 부두/다리재 | `modular_wooden_pier` | 84,780 | 강변 선착장. 다리 부재로 재가공 |
| 담장·전신·시설 | `modular_chainlink_fence` 89,232(키트), `modular_electricity_poles` 200,610(키트), `modular_electric_cables` 42,078, `utility_box_01/02` 4.4k/6.3k, `street_lamp_01/02` 30.6k/20.3k, `modular_fire_escape` 11.3k, `rollershutter_door/window_*` ~1.1k, `modular_metal_gutter` 16.5k, `water_manhole_cover` 6.3k | | 키트 통째는 무겁다 → **조각 하나만 뽑아 쓰기**(전신주 애자·변압기, 철망 한 칸) |
| 병영·창고 소품 | `old_military_crate` 10.5k, `wooden_military_crate` 23k, `wooden_crate_01/02` 6.6k/5.2k, `barrel_01/02/03` 1.5~2.7k, `old_tyre` 2.9k, `propane_tank` 5.2k, `plastic_crate_*`, `industrial_plastic_container` 7.1k | | **좋음** (상표 라벨 육안 확인) |
| 농가·마을 소품 | `wooden_bucket_01/02` 5~7k, `ceramic_pot` 3.6k(옹기 대용), `planter_pot_clay` 3.1k, `wooden_picnic_table` 10.2k(평상 대용), `wooden_ladder` 8.5k, `watering_can_metal_01` 11.8k, `wooden_broom` 14k, `stone_fire_pit` 3.9k, `folding_wooden_stool` 6.3k, `tire_pump`, `hand_truck`, `barrel_stove` 11.4k | | 좋음 |
| 바위·그루터기 | 기존 + `tree_stump_01/02`(4.1만/6.2만), `dead_tree_trunk*`(10만+) | | 감량 후 |
| 쓰지 말 것 | `island_tree_01~03`(1.76M~4.76M), `fir/pine_*`(785만~1,740만), `grass_medium_*`(100만+), `jacaranda_tree`(31만, 24 m) | | 렌더용. 가장 비슷한 열대 나무(island_tree)도 M1 불가 |

Poly Haven **텍스처**(적합성만): `clay_roof_tiles(_02/_03)`·`ceramic_roof_01`(기와), `asbestos_sheet(_02)`·`corrugated_iron(_02)`·`box_profile_metal_sheet`(슬레이트·함석 지붕), `concrete_block_wall(_02/_03)`·`concrete_wall_00x`·`brick_wall_*`·`clay_plaster`·`beige_wall_00x`(벽), `bamboo_wall(_02/_03)`·`bamboo_veneer`(고상 가옥 벽), `brown_mud*`·`clay_floor_001`(흙길/논둑), `moss`류. 열대 지붕(야자잎 thatch)과 **붉은 토양(laterite)** 은 목록에서 확실한 것 **미확인** → 기존 흙 텍스처 색조 편향(파생 가공)으로 해결.

### 2.2 Quaternius (CC0 · 확인: FAQ "licensed under CC0 … attribution is not necessary")
https://quaternius.com/faq.html . 파일은 FBX/OBJ/Blend, 대부분 **단색 팔레트 아틀라스**(FAQ 인용). 배포는 itch/구글 드라이브 위젯 → **사람 다운로드**. 사설 저장소 재배포: CC0라 OK.

| 팩 | 내용(공식 설명) | 쓸모 |
|---|---|---|
| Ultimate Crops (`ultimatecrops`) | 작물 100+종 × 성장 5단계 | 밭 소품 **가능**하나 스타일 불일치 → 형태만 참고, 밭은 카드/절차 |
| Farm Buildings (`farmbuildings`) | 농장 건물 세트 | 스타일 나쁨 |
| Ultimate Textured Buildings / Simple Buildings | 아틀라스 모듈 건물 | 나쁨(스타일) |
| Ultimate Modular Ruins | 모듈 유적·던전 | 유적 조각 형태 참고용, 스타일 나쁨 |
| Ultimate Nature / Stylized Nature MegaKit / Textured Fantasy Nature | 나무·바위·식물 110~150종 | 나쁨(Ghibli풍 로우폴리) |
| Survival, Zombie Apocalypse, Cars, Public Transport, Modular Streets, Downtown City MegaKit | 소품·차·도시 | 스타일 나쁨, 소품 한두 개만 |
이미 쓰는 Quaternius는 총기·캐릭터·애니(W-0001, C-0001~4)뿐.

### 2.3 Kenney · KayKit (CC0, 지식 + 기존 조사)
Kenney: "all game assets … are public domain (CC0)" (https://kenney.nl/support, 기존 조사). 오늘 https://kenney.nl/assets 목록에는 Mini Forest, City Kit(Roads/Industrial), Mini Shop 등 **카툰 단색**. KayKit(City Builder Bits 등)은 1024 그라디언트 아틀라스 로우폴리(기존 조사 확인). **둘 다 세미리얼 맵 톤과 불일치 → 비권장.** 도로 마킹/간판 형태 참고 정도.

### 2.4 OpenGameArt (파일별 라이선스 · 확인 2026-11-16)
| 후보 | URL | 라이선스 | 내용 | 평가 |
|---|---|---|---|---|
| **Free palm treeZ v3** (Yughues) | https://opengameart.org/content/free-palm-treez-v3 | **CC0** | 야자 5종, 텍스처 1장, `PACK V3.7z` 19.7 MB. 폴리 수 **미확인**(v2 저폴리판 451 tris ← 원본 1,032) | **좋음(사진 텍스처 카드)**. 기존 조사의 Yughues 풀·덤불과 같은 계열, TGA 스펙큘러→URP 변환 필요 |
| Palm tree v2 low poly edition | https://opengameart.org/content/palm-tree-v2-low-poly-edition | **CC0** | 451 tris, 512 텍스처, zip 1.4 MB | 원경용/LOD |
| **Tropical shrubs** (Yughues) | https://opengameart.org/content/tropical-shrubs | **CC0** | 열대 관목, `tropical shrubs.7z` 4.6 MB, 폴리 **미확인** | 좋음 예상(미리보기 영상만) |
| Exotic Palms / Strange Palms (FabinhoSC) | https://opengameart.org/content/exotic-palms-lowpoly-style | CC0 | 로우폴리 그라디언트 | 스타일 나쁨 |
| Low-poly palm tree (NoLogoGames), Textured low poly palm (CC-BY-SA) | — | CC-BY 3.0 / **CC-BY-SA 4.0** | | **SA 금지**(수정물 동일조건 공개 의무) |
| Yughues Modular building, bushes, hedges, rocks | 기존 조사(2026-11-13) | CC0 | 낡은 콘크리트·벽돌 모듈, 덤불 | 기존 결론 유지 |
다운로드는 로그인 불필요한 직접 링크(`opengameart.org/sites/default/files/…`)지만 **.7z**라 압축 해제 도구 필요(기존 조사: `bsdtar`). 한국 건축물/동남아 건축물 CC0는 검색에서 **없음**(오늘).

### 2.5 Sketchfab (API 검색 · 확인 2026-11-16)
API: `api.sketchfab.com/v3/search?type=models&license=cc0|by&downloadable=true` 가 로그인 없이 응답(확인). **실제 다운로드는 Sketchfab 로그인 필요 → 사람 몫**(에이전트는 계정 생성/로그인 안 함, 규칙 3).
- **CC0(downloadable)**: 박물관 스캔(Minneapolis Institute of Art, Cleveland Museum of Art, Małopolska) 수만~수십만 tris 유물, "JUNGLE 3202~3230"(vinciplay, 1~5만 tris, 용도 **미확인**), "Bridge Plank/Fence Wood — Low Poly Medieval"(Avramania, 24~584 tris), 대나무 스캔(ffish.asia/floraZia, 30만~197만 tris, 일본 종 — 감량 불가 수준). 우리 필요(주택·야자·고상가옥·논)에는 **해당 없음**. MIA의 아시아 조각상(CC0, ~5만 tris)은 종교 상징 → **임의 사용 보류**(문화 민감, RISKS 올림).
- **CC-BY 4.0/3.0(크레딧 필요)** — 정책상 허용(LICENSE_POLICY "CC-BY, 크레딧 화면 자동 반영"), 프로젝트에 선례 있음(V-0001~8). 후보(ID는 Sketchfab URL 끝 32자, 확인 2026-11-16):

| 필요 | 모델 (작가) | tris | 메모 |
|---|---|---|---|
| 고상 가옥 | Stilt house in DenHung (CanTho, Vietnam) — Wang Sh D, `73b917a1…` | 6,600 | 메콩 고상. 품질/텍스처 **미리 확인 필요** |
| 고상 가옥(참고) | Bahay Kubo Balikat Ulo — LowPolyBoy, `5b03a62f…` | 37,351 | 스타일화(stylized) — 톤 불일치, 형태 참고 |
| 야자/열대 | Palm Trees — YadroGames `55690379…` 2,857 / Coconut Palm — evolveduk 7,432 / Banana Plant — evolveduk `85695b82…` 4,402 / bamboo — evolveduk `a02bf0e3…` 2,180 / **Tropical Plants Pack M02P** — MozzarellaARC `2f093afb…` 46,575(야자·바나나·고사리·몬스테라) | | 게임용 저폴리. 룩이 세미리얼인지 **썸네일 확인 필요** |
| 감시탑 | Guard Tower [Free Asset] — Lex713 2,268 / Old Wooden Watchtower — Blenderust 664 / Wooden Watchtower — MaX3Dd 446 | | 중세풍이 대부분 → 한국 DMZ형은 절차 생성이 맞음 |
| 다리 | Wooden Bridge Pack — padmadev_designs 15,648 / Bridge — atillakurt 5,014 | | 절차 생성 권장 |
| 태양광 | Solar Panel — rivetech 1,854 | | 단순 박스+텍스처로 충분 |
| 담장 | Chain link fence pack — TampaJoey 17,554 / Modular Urban Fence Pack 39,031 | | Poly Haven 철망 키트와 중복 |
| 병영 | Small Barracks 4,476 / BARRACKS 3,481 | | 저폴리 스타일 → 비권장 |
| 학교 | school building — swjeta 20,580 | | 형태 **미확인** |
| 유적 | Old Ruined Temple — Muhammet Gündel 20,026 | | 유럽풍일 수 있음 **미확인** |
| 80년대 차량 | DanielZhabotinsky '65~'96 가상 차종 24종(2~26k tris) | | **이미 8종 사용 중**. 같은 작가라 크레딧 정리 쉬움 |
⚠ **상표 위생**: 실존 차종/브랜드 이름이 모델명에 있는 것(Suzuki Carry, Honda Acty, MTZ-80, Datsun 등)은 **쓰지 않는다**(LICENSE_POLICY).
⚠ AI 생성 여부는 API에 필드가 없고 태그(`noai` 등)가 일부만 있음 → 사람이 확인(Bahay Kubo는 `noai` 태그 확인).
⚠ 한국 문화재청(KHS_Asset) CC-BY 모델 다수는 조선 시대 복식·유물 → 1980년대 맵과 무관.

### 2.6 itch.io · 기타 CC0 (오늘 웹 검색)
동남아/한국 CC0 팩은 **없음**. 나온 것은 Stylized Asian Village(WizP, 라이선스 미표기 **미확인**), Mini Japan 등 스타일화·일본풍. 유료: CGTrader 한옥/동남아 주택 팩(로열티프리, 1980년대 서민주택 아님). **구매는 규칙 3(결제 금지)** → blocked.

### 2.7 Unity Asset Store / 지형·식생 도구 (지식 + 오늘 검색, **EULA는 오늘 원문 미확인**)
- 라이선스: Standard Unity Asset Store EULA — 계정 필요, 게임에 포함·상업 배포 가능, 에셋 **원본 재배포 불가**. 사설 저장소 커밋은 보통 허용되나 해석은 **사람 판단(needs-license-review)**. 에이전트는 받기/약관 동의를 **하지 않는다**.
- 무료 열대 후보(검색 결과, 가격/호환 **재확인 필요**): Mobile Palm Pack (URP) ~100–170 tris, LOD 없음 / Palm Tree Pack Free v1.2 / Coconut Palm Tree Pack(리뷰상 HDRP 문제, URP는 **미확인**). 유료(Tropical Grove $8.49, Tropical Island Vegetation $9.50, 등)는 **승인 없이는 불가**.
- Unity Tree Creator(Terrain 나무 에디터): **URP에서 셰이더 미지원(by design)**, 공식 대안은 SpeedTree (검색 확인: Unity 6.0 문서·이슈트래커). → 쓸 수 없음. SpeedTree 무료 샘플의 사용 약관은 **미확인**.
- Unity Terrain Sample 팩(기존 조사): 1.6 GB, EULA, 나무 포함 여부 미확인 → 사람이 열어 확인해야 가치 판단 가능.

---

## 3. 필요 항목별 판정 — 커버리지 매트릭스

판정: **A**=에셋 사용 / **P**=텍스처로 코드 생성 / **H**=혼합 / **X**=없음. 노력: S/M/L.

| 필요 | 판정 | 최선 소스 | 라이선스 | 노력 | 메모 |
|---|---|---|---|---|---|
| 저층 콘크리트·벽돌 주택 | **P** | 기존 `BuildingKind.House` + Poly Haven `concrete_block_wall`, `brick_wall_*`, `clay_plaster` / ambientCG Bricks, PaintedPlaster | CC0 | M | 80년대 한국 주택 CC0 모델 없음(오늘 검색). 평지붕 슬래브·옥상 물탱크·철제 대문은 코드 |
| 기와/슬레이트/함석 지붕 | **P** | `AddPrism` 박공 + `clay_roof_tiles`, `asbestos_sheet`, `corrugated_iron`, 기존 RoofingTiles014B·CorrugatedSteel005 | CC0 | S | 이미 박공 프리즘 있음 → 재질만 |
| 학교 | **P** | 신규 BuildingKind(긴 3층 박스 + 창 열 + 운동장) + `modular_urban_apartments_facade`(창 조각) | CC0 | M | 건물종 추가 = 사전 확인 |
| 병영/막사 | **P** | 함석 퀀셋/박공 창고 + `old_military_crate`, `barrel_*` | CC0 | M | Sketchfab 병영은 로우폴리라 비권장 |
| 농지(밭) | **P** | 지형 페인트 레이어 + 작물 열(카드: ambientCG Foliage/Leaf) | CC0 | M | Quaternius Crops는 스타일 불일치 |
| 논 | **P** | 계단식 평탄화 + 물 평면 + 벼 카드(풀 카드 재활용) | 자체 코드 | M | 모델 없음. 물 셰이더는 기존 물 계획과 공유 |
| 비닐하우스 | **P** | 아치 프레임 + 반투명 비닐 재질 | 자체 코드 | S | 오늘 Sketchfab 후보 품질 미흡 |
| 감시탑 | **P** | 박스+기둥 코드(콘크리트/목재 판) | CC0 텍스처 | S | Sketchfab CC-BY 중세풍 |
| 철조망/울타리/담 | **H** | 코드 picket(기존) + Poly Haven `modular_chainlink_fence` 한 칸 | CC0 | S | 키트 8.9만 tris → 1칸만 |
| 전신주 | **H** | 기존 코드 + `modular_electricity_poles` 애자·변압기 조각 | CC0 | S | 전선 이미 있음 |
| 태양광 패널 | **P** | 코드 박스 + 패널 텍스처 | 자체 | S | 80년대엔 시대착오 → 기획 결정 필요 |
| 한국 소품(소화기·공중전화) | **A** | Poly Haven `korean_*` | CC0 | S | **상표 라벨 텍스처 제거** 필수 |
| 야자·코코넛 | **H** | Yughues Free palm treeZ v3(CC0) 또는 자체 제작 확장 | CC0 / 자체 | M | CC-BY 대안: YadroGames, evolveduk |
| 정글 활엽·바나나·대나무·고사리 | **H** | 자체 제작(껍질·잎 카드) + Poly Haven fern_02, anthurium, calathea, pachira(감량) + evolveduk bamboo/banana(CC-BY) | CC0/CC-BY | M~L | 나무 구멍은 기존 조사와 동일 |
| 고상 가옥 | **P**(+참고 A) | 코드 박스+기둥+박공, 벽 `bamboo_wall`, 지붕 함석 | CC0 | M | Sketchfab DenHung은 CC-BY 대체 후보 |
| 사원·유적 | **H** | 코드 석조 모듈(돌 텍스처) + `modular_fort_01` 일부 + 바위 | CC0 | L | 불상 등 종교 상징은 임의 금지 |
| 붉은 토양 | **P** | 기존 Ground* 색조 편향(파생) | CC0 | S | 전용 laterite 텍스처 미확인 |
| 강/다리 | **H** | 코드 다리(콘크리트 상판·목교) + `modular_wooden_pier` | CC0 | M | 하천은 지형+물 |
| 차량(80년대) | **A**(기존) | V-0001~8 | CC-BY | — | 농업 차량(트랙터·경운기)은 **X**: 상표 없는 CC0/CC-BY 못 찾음 → 코드 박스 |
| 하늘·바위 | **A**(기존) | A-0004/5, A-0021~23 | CC0 | — | |

---

## 4. 승인 요청 Top 10 (추천 순)

| # | 항목 | 소스·라이선스 | 이유 | 누가 |
|---|---|---|---|---|
| 1 | **건물은 코드 생성 유지 + Poly Haven/ambientCG 텍스처 확장**(기와·슬레이트·콘크리트블록·대나무벽) | CC0 | CC0 완성 건물 부재, 일관성·성능 | 에이전트 |
| 2 | **열대 식생 자체 제작 확장**(야자·바나나·대나무·버트레스 정글 나무) | CC0 텍스처+코드 | 나무 구멍의 정공법, 크레딧 없음 | 에이전트 |
| 3 | Poly Haven `korean_fire_extinguisher_01`, `korean_public_payphone_01` (라벨 제거) | CC0 | 한국 분위기 즉효, 직접 다운로드 | 에이전트 |
| 4 | Poly Haven 병영·농가 소품 묶음(old_military_crate, wooden_crate, barrel, ceramic_pot, wooden_bucket, picnic_table, ladder, water_manhole_cover, utility_box) | CC0 | 소품 밀도 상승 | 에이전트 |
| 5 | Poly Haven 열대 지피(fern_02, anthurium, calathea_orbifolia, pachira 감량) | CC0 | 정글 하층 | 에이전트 |
| 6 | Yughues **Free palm treeZ v3** + Tropical shrubs (OpenGameArt) | CC0 | CC0 사진 텍스처 야자 | 에이전트(7z 해제) |
| 7 | Poly Haven `modular_electricity_poles`/`chainlink_fence`/`wooden_pier`에서 **조각만** | CC0 | 한국 시설·부두 디테일 | 에이전트 |
| 8 | Sketchfab CC-BY: Stilt house DenHung, Banana Plant/bamboo(evolveduk), Palm Trees(YadroGames) — **썸네일로 톤 확인 후** | CC-BY | 크레딧 화면 선례 있음 | **사람**(로그인 다운로드) |
| 9 | 붉은 토양·논둑·계단식 지형 재질은 **기존 텍스처 색조 편향 파생** | CC0 | 새 에셋 불필요 | 에이전트 |
| 10 | Unity Asset Store 무료 Mobile Palm Pack (URP) 확인만 | Asset Store EULA | 원경 야자 대체 | **사람**(EULA 판단) |

비권장: Quaternius/Kenney/KayKit 건물·자연 팩(스타일), 유료 CGTrader/Fab 팩(결제 금지), MIA 종교 조각상, 실존 차종명 모델.

---

## 5. 리스크·라이선스 메모

- Poly Haven `korean_*` 모델의 **라벨/통신사 마크** 텍스처 수정은 "수정함"으로 대장 기록(규칙 1).
- Sketchfab CC-BY는 모델 페이지 라이선스를 대장에 **API 확인일과 함께** 기록(V-0001~8 방식), 크레딧 화면 자동 반영 경로 재사용.
- CC-BY-SA(OpenGameArt 일부)는 수정물 공개 의무 → 금지.
- 한국 1980년대 시대 고증 소재(공중전화, 슬레이트, 새마을 표식 등)는 **실존 기관 로고/글자 금지**: 간판은 기존처럼 글자 없는 판.
- 문화·종교 상징(사원, 불상)은 법·문화 검토를 RISKS_OPEN_QUESTIONS에 올릴 것(규칙 8).
- 사설 저장소 재배포: CC0 전부 OK. CC-BY는 크레딧 조건 하에 OK. Asset Store EULA는 사람 판단.
- 이 조사에서 **지원 못 한 것**: Sketchfab 다운로드 API(로그인), Asset Store EULA 원문 재확인, itch.io 개별 팩 페이지, Yughues 야자 폴리 수.

---

## 6. 체크리스트 (타 트랙과 비중복)

조사/확인
- [ ] Sketchfab CC-BY 후보 6종 썸네일·텍스처 품질 사람이 확인(DenHung, evolveduk 3종, YadroGames, Tropical Plants M02P) — 톤 불일치면 폐기
- [ ] Yughues palm treeZ v3 / tropical shrubs: 폴리 수·텍스처 확인(스크래치에 받아 확인, 저장소 반입 전)
- [ ] Poly Haven `korean_public_payphone_01` / `korean_fire_extinguisher_01` 텍스처에서 상표·로고 위치 확인 후 제거 방안
- [ ] Poly Haven 텍스처 중 야자잎 thatch·laterite(붉은 흙) 존재 여부 재확인(배치안 확정 후 선별)
- [ ] Unity Asset Store 무료 Mobile Palm Pack: URP/LOD/EULA 사람 확인(담기 금지)
- [ ] ASSETS.md 갱신 제안: Yughues·Mesh2Motion 승인 소스 추가, "3dmodelscc0" 삭제(기존 조사에서 이월)

구현(승인 후, PLAN에 티켓화)
- [ ] 지붕 재질 확장: 기와/석면슬레이트/함석 3종 + 박공 프리즘에 적용 (S)
- [ ] 신규 건물종: 학교, 병영, 고상 가옥, 비닐하우스, 감시탑 (파일/enum 추가 → 사전 확인) (M~L)
- [ ] 논·밭·계단식 지형 레이어와 평탄화 규칙 (WorldTerrain 확장) (M)
- [ ] 열대 식생: 야자·바나나·대나무·버트레스 정글 나무 생성기 (WorldVegetation) (M~L)
- [ ] 다리(콘크리트/목재)·부두 생성기 (M)
- [ ] 전신주 애자·변압기, 철망 1칸 조각 임포트 + LOD (S)
- [ ] Poly Haven 소품 임포트 파이프라인(감량 LOD, 라벨 점검, ASSET_LEDGER 행) (M)
- [ ] 붉은 토양 파생 텍스처(색조 편향) + 지형 레이어 4개 이하 확인 (S)
- [ ] 크레딧 화면에 신규 CC-BY 항목 반영 확인 (S)
- [ ] 성능 예산(PERF_BUDGET) 대비 신규 식생·소품 인스턴스 수 측정 (M)

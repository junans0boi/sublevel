# 재질·그래픽

> 상태: 확정 · 요약: 맵 재질은 `materials.json` 데이터 테이블(17행)이 정한다(ADR-185). Poly Haven 2K 13종을 반입해 쓰고, 미반입 3행(farm_field·school_yard·solar)과 테이블 밖 3세트(Rock051·Ground054·PaintedMetal014)는 ambientCG 1K. 선택은 모두 임시 확정(status temp, 준환 위임). 받아 둔 미사용 소스·1K 대체분 정리는 #322.

## 현재 재질 파이프라인

| 상태 | 구성 | 지금 상태 | 한계 | 근거 |
|---|---|---|---|---|
| 확정 | WorldMaterials(에디터) | WorldMaterials가 `Data/Resources/GameData/materials.json`(MaterialTable, #313)을 읽어 행마다 소스 세트로 URP Lit 재질을 만든다. 소스가 Art/Materials에 없으면 `fallback`(옛 ambientCG 1K)을 쓴다. arm 한 장(Poly Haven)과 분리 맵(ambientCG)을 같은 마스크(_mask.png)로 포장(#311). 행의 tint는 옛 세트와 밝기를 맞추는 값. | farm_field·school_yard·solar는 표에 있으나 WorldMaterials가 아직 쓰지 않는다(TableIds 13개만). | Assets/_Project/Editor/WorldMaterials.cs:30-58; Assets/_Project/Data/Resources/GameData/materials.json |
| 확정 | 쓰는 소스(2026-11-20 main) | Poly Haven 2K 13종: grass_ground, gravel_road, asphalt_02, brick_wall_001, concrete_wall_003, rusty_corrugated_iron, clay_roof_tiles_03, oak_wood_planks, concrete_floor_worn_001, rusty_metal_sheet, concrete_pavement, beige_wall_002, old_linoleum_flooring_01 (ledger A-0029~A-0041). ambientCG 1K 3종(테이블 밖): Rock051, Ground054, PaintedMetal014. | 나머지 ambientCG 1K 10종(Ground037·Ground081·Asphalt015·Bricks097·PaintedPlaster017·CorrugatedSteel005·RoofingTiles014B·Planks039·Concrete047A·Metal022)은 fallback으로만 남음 → 정리 #322. | Assets/_Project/Editor/WorldMaterials.cs:31-32; docs/ASSET_LEDGER.md (A-0029~A-0041) |
| 확정 | 색 변주 | 벽돌·미장·철판·지붕 각 5색 틴트, 강조색 5개(#207)로 한 거리가 같은 집의 반복처럼 보이지 않게. | 정점 색·데칼 변주 없음. | Assets/_Project/Editor/WorldMaterials.cs:45-52 |
| 확정 | 지형 4층 | Grass(grass_ground) / Dirt(gravel_road) / Rock(Rock051) / Mud(Ground054). 셰이더 Game/WorldTerrain(URP Terrain/Lit + 옆면 투영 바위 층). 알파맵 1024(1 m/px), 하이트맵 1025. | 트라이플래너·데칼은 #320에서 추가(닫힘). | Assets/_Project/Editor/WorldMaterials.cs:360-366 |
| 확정 | Poly Haven 바위 모델 | rock_07, rock_09, rock_moss_set_01(FBX + 1K JPG color/normal/arm). PackRockMask가 arm을 마스크로 변환, RockTint(0.82,0.88,0.92)로 회색 쪽으로. | 바위는 arm 경로가 이미 있다. | Assets/_Project/Editor/WorldMaterials.cs:35-39,69-70,101-105; docs/ASSET_LEDGER.md:29-31 |
| 확정 | 물 | 자체 셰이더 Game/WorldWater(깊이 맵 1024, 범위 4 m). | 논·강 여울용 별도 재질 없음. | Assets/_Project/Editor/WorldMaterials.cs:17 |
| 확정 | 하늘 HDRI | Poly Haven Kloofendal 48d Partly Cloudy(2K, A-0005, 준환 선택 2026-11-16)로 교체(이전은 OvercastSoilPuresky). SkyExposure 0.7, AmbientIntensity 0.38, ReflectionIntensity 0.35. | WorldBuilder는 아직 흐린 하늘 가정(아래 조명 불일치). | Assets/_Project/Editor/SceneLook.cs:16-20; docs/ASSET_LEDGER.md:12 |
| 확정 | 조명·안개 상수 | FarClip 700, 안개 90~640 m, 안개색 (0.60,0.62,0.64) 흐린 하늘 수평선, 실외 앰비언트 0.8, 태양 세기 1.25, 그림자 강도 0.8, 그림자 거리 50 m(URP 에셋 값). | 맑은 HDRI와 어긋남(06 문서 I8). 실내 리플렉션 프로브 없음 → PBR 실내가 밝게 샘. | Assets/_Project/Editor/WorldBuilder.cs:46-50,193-207 |
| 확정 | 낮/밤/날씨 | WorldSky 시간 키프레임이 태양·색을 덮어쓴다(서버가 시드로 동기화). | 씬 베이스 값과 Sky.mat은 HDRI 하나. 진실 원천을 하나로 정해야 함(결정 m_sky). | Assets/_Project/Scripts/Core/WorldSky.cs:24-84 |
| 확정 | 포스트·그래픽 옵션 | 전역 Volume 프로파일 + 런타임 밝기 볼륨(ColorAdjustments). 그림자 거리 끔/30/50/80 m, 캐스케이드 1~2, 텍스처 밉 제한(낮음 2/보통 1/높음 0), 시야 300/500/700/900 m, SSAO 켜기·끄기. | 세부 포스트 설정값(블룸 등)은 프로파일 에셋에 있어 이번에 읽지 않았다(확인 필요). | Assets/DefaultVolumeProfile.asset; Assets/_Project/Scripts/Gameplay/GraphicsOptions.cs:9-30,205-215 |
| 확정 | 임포트 규칙 | 텍스처 최대 2048(MaxTextureSize) 강제. | 4K 금지 규칙과 일치. 압축 형식(KTX2)은 별도 확인 필요. | Assets/_Project/Editor/AssetImportRules.cs:12,27 |

## 표면 분류 12개 (02_map_layout §5 기준)

| 상태 | 분류 | 쓰이는 곳 | 지금 쓰는 것(코드) | 신규 필요/후보 | Poly Haven 선택 | 근거 |
|---|---|---|---|---|---|---|
| 확정 | 아스팔트 도로 | 읍내 주도로·순환로 | asphalt_02 | 새 아스팔트 / 갈라진·낡은 / 차선 데칼. 받아 둔 asphalt_02, asphalt_05 | asphalt_02 (임시 확정) | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 포장 보도·콘크리트 | 상가 앞·광장·시장 | concrete_pavement, concrete_floor_worn_001 | 보도블록(받아 둔 PavingStones152), 거친 콘크리트(concrete_floor_worn_001, Concrete049) | concrete_pavement·concrete_floor_worn_001 (임시 확정) | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 흙길·자갈 | 농로·골목·공장 마당·운동장 | gravel_road (+ 지형 Mud는 Ground054) | 마른 흙, 자갈, 젖은 흙(dirt_floor, gravel_ground_01, gravel_road 받아 둠) | gravel_road (임시 확정) | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 외벽 | 주택·상가·아파트 | concrete_wall_003, brick_wall_001 (5색 틴트 유지) | 노출 콘크리트, 시멘트 모르타르, 벽돌(적/갈; Bricks113A 받음), 타일 외장, 목재 판자, 칠 벗겨진 페인트. 후보 이름: concrete_block_wall, brick_wall_*, clay_plaster, beige_wall | concrete_wall_003·brick_wall_001 (임시 확정) | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 지붕 | 주택·창고·공장 | clay_roof_tiles_03, rusty_corrugated_iron | 기와(한국 1980, 청/적), 골함석, 평 콘크리트, 슬레이트. 후보 이름: clay_roof_tiles, ceramic_roof_01, asbestos_sheet, corrugated_iron, box_profile_metal_sheet | clay_roof_tiles_03·rusty_corrugated_iron (임시 확정) | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 실내 바닥 | 방 | oak_wood_planks, old_linoleum_flooring_01 | 장판/비닐 느낌, 마루(oak_wood_planks, Planks040 받음), 타일, 콘크리트 | oak_wood_planks·old_linoleum_flooring_01 (임시 확정) | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 금속 | 공장·컨테이너·울타리 | rusty_metal_sheet, PaintedMetal014 | 녹슨 철판(rusty_metal_02 받음), 철망(알파), 페인트 금속(metal_plate, Metal064 받음) | rusty_metal_sheet (임시 확정) | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 농지 | 논·밭·비닐하우스 | 없음(farm_field 행은 WorldMaterials 미연결) | 논(물 반사+모심기 줄), 밭 고랑, 비닐. 붉은 토양·논둑은 기존 흙 색조 파생(05 문서) | farm_furrows 선택, 미다운로드 → Ground054 대체 | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 식생 | 가장자리·가로수·벼 | grass_ground(지면) + 자체 제작 껍질·잎 카드(ADR-070) | 야자·바나나·대나무·고사리(05 문서: 자체 제작 + fern_02 등 Poly Haven 모델) | grass_ground (지면, 임시 확정). 모델은 별도 트랙 | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 제안 | 물 | 저수지·강·논 | Game/WorldWater(자체 셰이더) | 셰이더 재활용, 논용 얕은 물 변형 | 해당 없음 | docs/research/02_map_layout.md:79 |
| 확정 | 태양광 | 외곽 단지 | 없음(solar 행 미연결) | 패널(프레임+유리)은 모델/자체 제작 | ambientCG SolarPanel003 선택, 미다운로드 → PaintedMetal014 대체 | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |
| 확정 | 학교·막사 마감 | 운동장, 본관 | beige_wall_002 (운동장 미연결) | 운동장 흙 + 선(데칼), 페인트 벽 | beige_wall_002 (임시 확정), 운동장 baseball_playground 미다운로드 | Assets/_Project/Data/Resources/GameData/materials.json; Assets/_Project/Editor/WorldMaterials.cs:31-32 |

## 받아 둔 텍스처(저장소 밖)

| 상태 | 이름 | 출처 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | Poly Haven 2K 13종 | aerial_grass_rock, grass_ground, dirt_floor, gravel_ground_01, gravel_road, concrete_wall_003, concrete_floor_worn_001, asphalt_02, asphalt_05, rusty_metal_02, metal_plate, oak_wood_planks, rock_boulder_dry (T-0001, CC0) | 6종은 반입·사용(A-0029~A-0034). 7종(aerial_grass_rock, dirt_floor, gravel_ground_01, asphalt_05, rusty_metal_02, metal_plate, rock_boulder_dry)은 미사용. 정리 #322. | docs/ASSET_LEDGER.md:74 |
| 확정 | ambientCG 2K 5종 | Concrete049, Metal064, Planks040, Bricks113A, PavingStones152 (T-0002, CC0) | 5종 모두 미사용(Poly Haven 소스로 대체). 정리 #322. | docs/ASSET_LEDGER.md:75 |
| 확정 | HDRI 2종 | OvercastSoilPuresky(A-0004), KloofendalPartlyCloudy(A-0005) | Art/HDRI 9.6 MB. Kloofendal이 현재. | docs/ASSET_LEDGER.md:11-12 |

## 텍스처·용량 예산

| 상태 | 항목 | 지금 | 제안 | 근거 |
|---|---|---|---|---|
| 확정 | 기존 규칙 | 텍스처 4K 금지, 임포트 최대 2048. 메모리 ≤3 GB(판정 방법 없음, 잠정), 빌드 ≤4 GB(현재 mac 221 MB, win 123 MB). | 메모리·로딩 판정 방법을 먼저 만든다(06 문서 I5). | docs/PERF_BUDGET.md:14,18; Assets/_Project/Editor/AssetImportRules.cs:12 |
| 확정 | 저장소 용량 | Art/Textures 173 MB, Art/Characters 113 MB가 미커밋. jpg/png/hdr/fbx/wav는 LFS 추적. World_Meshes.asset 42 MB·World.unity 32 MB는 텍스트라 LFS 아님(이슈 #240). | 쓰는 텍스처만 남기고 나머지는 저장소 밖 보관. 커밋 전 ledger·LICENSE.txt 확인(AssetValidator는 누락 시 빌드 실패). | git 상태; .gitattributes:10-19 |
| 제안 | 해상도 상한 | 1K 20종 + 2K 일부 혼재. | 기본 2K, 지형 4K 소수(4K는 예외 ADR 필요)로 제안. VRAM 예측: 2K 압축(BC7) 알베도+노멀+ARM 한 세트 약 16 MB(밉 포함 추정) x 수십 종이면 0.5~1 GB(06 문서 N8). | docs/research/MASTER_PLAN.md:15 |
| 확정 | 렌더 예산 | 한 지점 555콜 / 68만 삼각형 / SetPass 32. 한도 2,500콜·1.5M·60. | 재질 종류가 늘면 SetPass가 먼저 오른다. 외벽 껍질은 재질당 1메시, 가장 무거운 3 시점에서 측정. | docs/PERF_BUDGET.md:140; docs/research/06_missed_items.md:I5 |

## 조명·포스트 정리 (같은 작업 묶음에 포함)

재질만 바꾸면 어울리지 않는다. 하늘이 맑은 Kloofendal로 바뀌었는데 WorldBuilder 상수는 흐린 하늘 기준이다.

- 제안: HDRI는 구름 질감용 스카이박스로만 쓰고, 태양 방향·색·세기는 WorldSky 키프레임이 정한다(이미 그 방향).
- 제안: 안개색을 맑은 하늘 수평선으로, 앰비언트 0.8·태양 1.25 재조정, 그림자 거리 80~100 m + 캐스케이드 재조정(그래픽 옵션 '높음' 80 m, GraphicsOptions.cs:25).
- 제안: 건물 종류당 ReflectionProbe 1개(블렌드)로 실내 반사 누출 차단, World_InteriorGrade·InteriorLights 재검증.
- 제안: 비 젖음(ADR-163)의 스무스니스 파라미터를 새 재질에 맞춰 재조정.
- 디테일 3종: 지형 블렌딩/트라이플래너, 데칼(얼룩·균열·젖음), 정점 색 변주.

## 정해야 할 것

- [x] **표면 전부를 arm 한 장(AO·Rough·Metal) 경로로 통일** (확정) → 통일 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 통일(ambientCG도 arm으로 변환) / 혼용 유지  · 추천: 통일
  - 근거: PackRockMask가 이미 있어 확장 가능. 혼용하면 코드 경로가 둘이다(MASTER_PLAN §1).
- [x] **Poly Haven 후보 선정(분류 12개 x 후보 3~5)** (확정) → 분류별 시트 추천 1개를 임시 확정(materials.json status temp) [준환 위임 2026-11-16, 스펙 #306 — 소스 ID만 바꾸면 교체 가능]
  - 선택지: Chrome으로 직접 보고 시트 작성 후 준환 선택 / 에이전트가 일괄 선택  · 추천: 시트 후 준환 선택
  - 근거: docs/research/07_polyhaven_candidates.md 시트. 취향 판단이 걸려 있어 준환이 게임 안에서 보고 바꿀 수 있다(#306 완료 기준 5).
- [x] **해상도 상한** (확정) → 전부 2K [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 2K 기본 + 지형 4K 소수 / 전부 2K / 전부 1K  · 추천: 전부 2K
  - 근거: 4K는 규칙 위반이라 예외 ADR이 필요하다.
- [x] **하늘/조명 진실 원천** (확정) → WorldSky 키프레임 + HDRI는 스카이박스 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: WorldSky 키프레임 + HDRI는 스카이박스 / HDRI 중심  · 추천: WorldSky 키프레임 + HDRI는 스카이박스
  - 근거: 06 문서 D8. 시간대·날씨가 이미 서버 동기화되는 쪽이 키프레임.
- [x] **받아 둔 173 MB 정리** (확정) → 쓰는 것만 남김 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 쓰는 것만 남김 / 전부 유지(LFS)  · 추천: 쓰는 것만 남김
  - 근거: 선택이 끝난 뒤 정리. 커밋은 준환 승인.
- [x] **디테일 3종(블렌딩·데칼·정점 색) 범위** (확정) → 재질 교체 후 별도 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 이번 라운드 포함 / 재질 교체 후 별도  · 추천: 재질 교체 후 별도
  - 근거: 한 번에 올리면 어색해진다는 목표(MASTER_PLAN §0)와 렌더 예산 사이 균형.


# 재질·그래픽

> 상태: 확정 · 요약: 현재 ambientCG 13종 파이프라인·Poly Haven 바위·HDRI, 표면 12분류, 조명 불일치, 받아 둔 173 MB 텍스처와 용량 예산, Poly Haven 선택은 미정.

## 현재 재질 파이프라인

| 상태 | 구성 | 지금 상태 | 한계 | 근거 |
|---|---|---|---|---|
| 확정 | WorldMaterials(에디터) | ambientCG 1K 세트를 읽어 URP Lit 재질로 만든다. 러프니스/메탈 분리 맵을 마스크(_mask.png: R 메탈, A 스무스니스)로 합친다. 산출물은 Prefabs/World(재질 152개 파일, 약 81 MB). | 맵 형식이 ambientCG(분리) 기준. Poly Haven은 arm(AO·Rough·Metal 한 장). | Assets/_Project/Editor/WorldMaterials.cs:6-32 |
| 확정 | 쓰는 ambientCG ID(13) | Ground037, Ground081, Rock051, Ground054, Asphalt015, Bricks097, PaintedPlaster017, CorrugatedSteel005, RoofingTiles014B, Planks039, Concrete047A, Metal022, PaintedMetal014 | 1K 해상도. 모두 CC0. | Assets/_Project/Editor/WorldMaterials.cs:30-33 |
| 확정 | 색 변주 | 벽돌·미장·철판·지붕 각 5색 틴트, 강조색 5개(#207)로 한 거리가 같은 집의 반복처럼 보이지 않게. | 정점 색·데칼 변주 없음. | Assets/_Project/Editor/WorldMaterials.cs:45-52 |
| 확정 | 지형 4층 | Grass(Ground037) / Dirt(Ground081) / Rock(Rock051) / Mud(Ground054). 셰이더 Game/WorldTerrain(URP Terrain/Lit + 옆면 투영 바위 층). 알파맵 1024(1 m/px), 하이트맵 1025. | 가까이서 보면 알파맵 경계가 뭉개진다(06 문서 I1). 트라이플래너/블렌딩으로 타일 반복 감추기 미구현. | Assets/_Project/Editor/WorldMaterials.cs:298-304; Assets/_Project/Editor/WorldMaterials.cs:18 |
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
| 제안 | 아스팔트 도로 | 읍내 주도로·순환로 | Asphalt015(ambientCG) | 새 아스팔트 / 갈라진·낡은 / 차선 데칼. 받아 둔 asphalt_02, asphalt_05 | 미정(Chrome 선택 단계) | docs/research/02_map_layout.md:70 |
| 제안 | 포장 보도·콘크리트 | 상가 앞·광장·시장 | Concrete047A | 보도블록(받아 둔 PavingStones152), 거친 콘크리트(concrete_floor_worn_001, Concrete049) | 미정 | docs/research/02_map_layout.md:71 |
| 제안 | 흙길·자갈 | 농로·골목·공장 마당·운동장 | Ground081, Ground054 | 마른 흙, 자갈, 젖은 흙(dirt_floor, gravel_ground_01, gravel_road 받아 둠) | 미정 | docs/research/02_map_layout.md:72 |
| 제안 | 외벽 | 주택·상가·아파트 | Bricks097, PaintedPlaster017(5색 틴트) | 노출 콘크리트, 시멘트 모르타르, 벽돌(적/갈; Bricks113A 받음), 타일 외장, 목재 판자, 칠 벗겨진 페인트. 후보 이름: concrete_block_wall, brick_wall_*, clay_plaster, beige_wall | 미정 | docs/research/02_map_layout.md:73 |
| 제안 | 지붕 | 주택·창고·공장 | RoofingTiles014B, CorrugatedSteel005 | 기와(한국 1980, 청/적), 골함석, 평 콘크리트, 슬레이트. 후보 이름: clay_roof_tiles, ceramic_roof_01, asbestos_sheet, corrugated_iron, box_profile_metal_sheet | 미정 | docs/research/02_map_layout.md:74; docs/research/05_map_assets.md:56 |
| 제안 | 실내 바닥 | 방 | Planks039, Concrete(실내) | 장판/비닐 느낌, 마루(oak_wood_planks, Planks040 받음), 타일, 콘크리트 | 미정 | docs/research/02_map_layout.md:75 |
| 제안 | 금속 | 공장·컨테이너·울타리 | Metal022, PaintedMetal014, CorrugatedSteel005 | 녹슨 철판(rusty_metal_02 받음), 철망(알파), 페인트 금속(metal_plate, Metal064 받음) | 미정 | docs/research/02_map_layout.md:76 |
| 제안 | 농지 | 논·밭·비닐하우스 | 없음 | 논(물 반사+모심기 줄), 밭 고랑, 비닐. 붉은 토양·논둑은 기존 흙 색조 파생(05 문서) | 미정 — 논/밭 재질은 확실한 후보 미확인 | docs/research/02_map_layout.md:77 |
| 제안 | 식생 | 가장자리·가로수·벼 | 자체 제작(ambientCG 껍질·잎 카드, ADR-070) | 야자·바나나·대나무·고사리(05 문서: 자체 제작 + fern_02 등 Poly Haven 모델) | 모델은 별도 트랙 | docs/research/02_map_layout.md:78 |
| 제안 | 물 | 저수지·강·논 | Game/WorldWater(자체 셰이더) | 셰이더 재활용, 논용 얕은 물 변형 | 해당 없음 | docs/research/02_map_layout.md:79 |
| 제안 | 태양광 | 외곽 단지 | 없음 | 패널(프레임+유리)은 모델/자체 제작 | 해당 없음 | docs/research/02_map_layout.md:80 |
| 제안 | 학교·막사 마감 | 운동장, 본관 | PaintedPlaster017 | 운동장 흙 + 선(데칼), 페인트 벽 | 미정 | docs/research/02_map_layout.md:81 |

## 이번 주 받아 둔 텍스처 (미사용, 173 MB)

| 상태 | 이름 | 출처 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | Poly Haven 2K 13종 | aerial_grass_rock, grass_ground, dirt_floor, gravel_ground_01, gravel_road, concrete_wall_003, concrete_floor_worn_001, asphalt_02, asphalt_05, rusty_metal_02, metal_plate, oak_wood_planks, rock_boulder_dry (T-0001, CC0) | Diffuse·nor_gl·Rough 3장씩 JPG, 폴더 약 101 MB. 아직 재질에 연결되지 않음(ledger 비고: '적용 예정'). | docs/ASSET_LEDGER.md:74 |
| 확정 | ambientCG 2K 5종 | Concrete049, Metal064, Planks040, Bricks113A, PavingStones152 (T-0002, CC0) | Color·NormalGL·Roughness, 폴더 약 73 MB. | docs/ASSET_LEDGER.md:75 |
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

## 정해야 할 것 (Poly Haven 선택은 Chrome으로 나중에)

- [x] **표면 전부를 arm 한 장(AO·Rough·Metal) 경로로 통일** (확정) → 통일 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 통일(ambientCG도 arm으로 변환) / 혼용 유지  · 추천: 통일
  - 근거: PackRockMask가 이미 있어 확장 가능. 혼용하면 코드 경로가 둘이다(MASTER_PLAN §1).
- [ ] **Poly Haven 후보 선정(분류 12개 x 후보 3~5)** (미정) 
  - 선택지: Chrome으로 직접 보고 시트 작성 후 준환 선택 / 에이전트가 일괄 선택  · 추천: 시트 후 준환 선택
  - 근거: 취향·분위기(한국 1980 vs 열대) 판단이 걸려 있다. 각 후보: ID, 링크, 해상도(1k/2k/4k), 타일링, 용량, 적합 이유. 지금은 미정(open).
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


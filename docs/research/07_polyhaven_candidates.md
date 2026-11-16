# 07. Poly Haven 재질 후보 시트 (ADR-174, 표면 12분류)

확인일 2026-11-16. 준환이 고르는 용도. 코드·에셋 변경 없음, 다운로드 없음.

## 확인 방법 (확인 / 추정 구분)
- **확인**: Poly Haven 공개 API(`api.polyhaven.com/assets`, `/files/<id>`; 읽기만, 약 100회)에서 해상도·맵 종류·2K JPG 파일 용량·실물 크기(dimensions)·태그를 읽음. Chrome(Claude in Chrome)으로 polyhaven.com에서 후보 약 85종 썸네일(구체 프리뷰)을 한 장에 모아 색·패턴을 눈으로 봄.
- **라이선스**: Poly Haven 전부 CC0 — https://polyhaven.com/license (확인 2026-11-16). ambientCG 전부 CC0 1.0 — https://docs.ambientcg.com/license/ (확인 2026-11-16).
- **추정**: 타일링 여부(아래 참고), '80년대 한국+열대' 적합성(썸네일 기준 주관 판단), 실제 게임 내 모습. 썸네일은 구체 렌더라 실제 평면 타일링 모습과 다를 수 있음. 실존 브랜드·로고가 보이는 재질은 본 후보 중 없음(썸네일 기준, 근접 확인은 안 함).
- **용량 표기**: 2K JPG 기준 `D`(Diffuse) / `N`(nor_gl) / `ARM`(AO·Rough·Metal 한 장) / `R`(Rough 단독) MB. **세트 = D+N+ARM**(Unity 쪽 마스크 패킹이 쉬움, WorldMaterials가 이미 arm 경로 있음 — 바위). 받아 둔 13종은 D·N·R 3장 형식이라 arm을 쓰려면 ARM만 추가 다운로드 필요.
- **해상도**: 아래 전부 1k/2k/4k/8k 제공(별표 `16k`는 16k까지). 상한 2K 정책이므로 2K만 표기. API에서 모든 후보가 arm 형식 제공(`arm` 맵 2k 확인).
- **타일링**: API에 타일링 필드 없음. Poly Haven 텍스처는 사이트 설명상 대체로 심리스 타일 — **개별 확인은 안 함(추정)**. `aerial_*`(항공 촬영 기반, 실물 크기 8~20 m)는 반복이 덜 보이는 대신 지면용.
- **실물 크기(cm)**: 한 타일이 현실에서 차지하는 폭. 작을수록(50~100 cm) 넓은 면에서 반복이 눈에 띔.
- 약점 열의 "받음" = 이미 받아 둔 Poly Haven 2K 13종(T-0001). "ACG받음" = 받아 둔 ambientCG 2K 5종(T-0002).

## 맨 위 요약: 분류별 추천 1개 + 2K 용량

| # | 분류 | 추천 | 세트 용량 | 보조(선택) |
|---|---|---|---|---|
| 1 | 아스팔트 | `asphalt_02` (받음) | 9.3 MB | `clean_asphalt` 10.7 |
| 2 | 보도·콘크리트 | `concrete_pavement` | 12.5 MB | `concrete_floor_worn_001` (받음) 2.0 |
| 3 | 흙길·자갈 | `gravel_road` (받음) | 7.7 MB | `dirt_floor` (받음) 11.6 |
| 4 | 외벽 | `concrete_wall_003` (받음) | 2.9 MB | `brick_wall_001` 3.6 / `beige_wall_002` 0.6 / `wood_plank_wall` 7.3 |
| 5 | 지붕 | `clay_roof_tiles_03` | 10.1 MB | 골함석 `rusty_corrugated_iron` 7.1 |
| 6 | 실내 바닥 | `old_linoleum_flooring_01` | 6.6 MB | `oak_wood_planks` (받음) 8.8 |
| 7 | 금속 | `rusty_metal_sheet` | 7.4 MB | `container_side` 6.3 |
| 8 | 농지 | `farm_furrows` (밭 고랑) | 11.0 MB | 논바닥 `mud_forest` 12.9 / `farm_soil` 12.5 |
| 9 | 식생(지면 풀) | `grass_ground` (받음) | 13.7 MB | `sparse_grass` 13.4 |
| 10 | 물 | **없음** (자체 셰이더 유지) | 0 | — |
| 11 | 태양광 | **Poly Haven 없음** → ambientCG `SolarPanel003` | zip 12.4 MB (전 맵 포함, 상한) | `SolarPanel001` 15.4 |
| 12 | 학교·막사 | 운동장 `baseball_playground` | 10.7 MB | 벽 `beige_wall_002` 0.6 + 4번 |

- **추천 1개씩 합계(1~9, 12; 11은 별도): 91.9 MB** (JPG 세트 합, 2K). 보조 4종(`brick_wall_001` 3.6, `rusty_corrugated_iron` 7.1, `oak_wood_planks` 8.8, `beige_wall_002` 0.6) 추가 시 +20.1 MB ≒ **112 MB**, 태양광 포함하면 +12.4 MB(zip 기준 상한).
- 주의: JPG 용량은 디스크/저장소 기준. VRAM은 별개(BC7 2K 한 세트 약 16 MB 추정, 06 문서 N8) → 추천 10종이면 VRAM 약 150 MB 안팎 **추정**.
- 받아 둔 Poly Haven 13종 중 후보로 올린 것은 `asphalt_02` `asphalt_05` `gravel_road` `gravel_ground_01` `dirt_floor` `concrete_wall_003` `concrete_floor_worn_001` `rusty_metal_02` `metal_plate` `oak_wood_planks` `grass_ground` `aerial_grass_rock`(+ `rock_boulder_dry`는 바위용, 후보 대상 아님). 못 쓰게 되는 것은 저장소 밖 정리 대상.

## 분류별 후보 (링크: `https://polyhaven.com/a/<ID>`)

공통 열: ID / 실물 크기 / D·N·ARM MB(세트) / 분위기 적합 / 약점.

### 1. 아스팔트 도로 (읍내 주도로·순환로)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [asphalt_02](https://polyhaven.com/a/asphalt_02) | 받음 | 300 | 3.1/4.9/1.3 (9.3) | 어두운 균일 회색, 읍내 도로 기본. ARM이 작아 가벼움 | 차선 없음(데칼 별도), 균일해 단조 |
| [asphalt_05](https://polyhaven.com/a/asphalt_05) | 받음 | 200 | 3.8/5.2/3.5 (12.5) | 따뜻한 갈색빛 — 열대 흙먼지 쌓인 도로 느낌 | 썸네일상 붉은 갈색이 강해 아스팔트로 안 읽힐 수 있음(색 보정 필요) |
| [clean_asphalt](https://polyhaven.com/a/clean_asphalt) | 신규 | 210 | 3.3/3.9/3.5 (10.7) | 푸른 회색의 새 포장 — 읍내 중심·학교 앞 | 80년대엔 깨끗함이 약간 과함 |
| [road_damaged_clean](https://polyhaven.com/a/road_damaged_clean) | 신규 | 220 | 3.4/4.0/3.2 (10.6) | 갈라지고 패인 낡은 도로 | 갈색 톤, 반복되면 균열이 눈에 띔 |
| [worn_asphalt](https://polyhaven.com/a/worn_asphalt) | 신규 | 200 | 3.6/5.9/4.4 (13.9) | 낙엽·잔가지·거친 자갈, 열대 외곽 길 | 낙엽 패턴이 반복돼 보임, 용량 큼 |

### 2. 포장 보도·콘크리트 (상가 앞·광장·시장)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [concrete_pavement](https://polyhaven.com/a/concrete_pavement) | 신규 | 180 | 3.8/5.2/3.5 (12.5) | 사각 시멘트 보도블록 줄눈, 한국 80년대 인도와 가장 닮음 | 용량 큼, 줄눈 격자가 규칙적 |
| [concrete_pavement_02](https://polyhaven.com/a/concrete_pavement_02) | 신규 | 180 | 4.2/5.4/2.7 (12.3) | 위와 비슷, 얼룩이 더 있음 | 위와 거의 같아 둘 중 하나만 |
| [concrete_pavers](https://polyhaven.com/a/concrete_pavers) | 신규 | 192 | 3.1/3.7/2.5 (9.3) | 맞물림 포장블록, 시장·광장 | 한국 80년대보다 현대적 |
| [concrete_floor_worn_001](https://polyhaven.com/a/concrete_floor_worn_001) | 받음 | 300 | 0.5/0.9/0.6 (2.0) | 매끈한 낡은 콘크리트, 용량 극소 | 디테일이 적어 가까이서 밋밋 |
| [concrete_tiles](https://polyhaven.com/a/concrete_tiles) | 신규 | 185 | 3.6/3.9/3.3 (10.8) | 콘크리트 타일 바닥 | 보도블록(위)과 역할 겹침 |
| ACG [PavingStones152](https://ambientcg.com/a/PavingStones152) | ACG받음 | — | zip 28.4(전 맵) | 붉은 벽돌 포장(썸네일) | ACG라 맵 분리형(R 별도) |

### 3. 흙길·자갈 (농로·골목·공장 마당)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [gravel_road](https://polyhaven.com/a/gravel_road) | 받음 | 200 | 2.9/3.6/1.2 (7.7) | 붉은 갈색 자갈흙 — 열대 라테라이트 농로 느낌, 용량 최소급 | 썸네일상 자갈 대비가 약함 |
| [dirt_floor](https://polyhaven.com/a/dirt_floor) | 받음 | 207 | 3.9/5.0/2.7 (11.6) | 황갈색 마른 흙, 골목·마당 | 용량 큼 |
| [gravel_ground_01](https://polyhaven.com/a/gravel_ground_01) | 받음 | 300 | 3.8/5.5/0.7 (10.0) | 회갈색 자갈 마당 | 노멀 5.5 MB로 무거움 |
| [dry_ground_01](https://polyhaven.com/a/dry_ground_01) | 신규 | 400 | 2.5/1.1/0.8 (4.4) | 크기 400 cm로 반복이 덜 보임, 가벼움 | 회녹색 — 마른 흙 색감이 약함 |
| [mud_cracked_dry_03](https://polyhaven.com/a/mud_cracked_dry_03) | 신규 | 150 | 3.9/5.0/3.3 (12.2) | 갈라진 마른 진흙, 건기 논바닥·저수지 둑 | 용량 큼, 갈라짐 반복 눈에 띔 |

### 4. 외벽 (콘크리트/벽돌/미장/목재)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [concrete_wall_003](https://polyhaven.com/a/concrete_wall_003) | 받음 | 300 | 1.1/1.0/0.8 (2.9) | 베이지 흰 낡은 시멘트 미장, 용량 극소, 틴트 변주 쉬움 | 디테일 약함(근접) |
| [concrete_wall_004](https://polyhaven.com/a/concrete_wall_004) | 신규 | 200 | 2.0/4.3/3.6 (9.9) | 회색 노출 콘크리트 판넬 줄눈(아파트·공장) | 노멀 4.3 MB로 무거움 |
| [concrete_block_wall](https://polyhaven.com/a/concrete_block_wall) | 신규 | 200 | 3.5/4.3/2.1 (9.9) | 어두운 콘크리트 블록(담·창고) | 어두워 틴트 시 탁해질 수 있음 |
| [brick_wall_001](https://polyhaven.com/a/brick_wall_001) | 신규 | 300 | 2.0/1.2/0.4 (3.6) | 거친 적벽돌, 매우 가벼움 | 현대 공장풍, 매끈한 색 변주 필요 |
| [brick_wall_003](https://polyhaven.com/a/brick_wall_003) | 신규 | 400 | 2.5/4.3/0.8 (7.6) | 크기 400 cm, 베이지 벽돌 | 현대 건물 태그(modern) |
| [peeling_painted_wall](https://polyhaven.com/a/peeling_painted_wall) | 신규 | 180 | 1.7/4.1/3.4 (9.2) | 칠 벗겨진 핑크~붉은 벽 | 색이 강해 반복 노출 시 어색 |
| [beige_wall_002](https://polyhaven.com/a/beige_wall_002) | 신규 | 300 | 0.2/0.3/0.1 (0.6) | 거친 베이지 도장 외벽, 사실상 공짜 용량 | 거의 단색(디테일 낮음) |
| [blue_plaster_weathered](https://polyhaven.com/a/blue_plaster_weathered) | 신규 | 200 | 2.7/3.0/1.5 (7.2) | 청록 풍화 미장 — 80년대 한국 대문·열대 건물 강조색 | 강조색 전용 |
| [wood_plank_wall](https://polyhaven.com/a/wood_plank_wall) | 신규 | 100 | 2.2/3.1/2.0 (7.3) | 가로 판자 — 수상가옥·창고 | 100 cm라 반복 잦음 |
| ACG [Bricks113A](https://ambientcg.com/a/Bricks113A) | ACG받음 | — | zip 26.5 | 썸네일상 베이지 석재 블록 벽(벽돌 아님) | 이름과 달리 벽돌로 안 읽힘 |

### 5. 지붕 (기와·골함석·슬레이트)
Poly Haven에 "roofing" 카테고리 23종 있음 → **기와 있음**. 다만 한옥 검은 곡선 기와는 없음(전부 서양/지중해식 평·겹 기와) — 아래는 80년대 서민 주택의 적·주황 시멘트·점토 기와로 읽히는 쪽.
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [clay_roof_tiles_03](https://polyhaven.com/a/clay_roof_tiles_03) | 신규 | 260 | 3.7/4.1/2.3 (10.1) | 주황~적색 불균일 점토 기와, 80년대 붉은 기와집 | 서양식 겹 기와(한국 기와 곡선 없음) |
| [clay_roof_tiles_02](https://polyhaven.com/a/clay_roof_tiles_02) | 신규 | 250 | 3.5/3.5/2.0 (9.0) | 선명한 주황 맞물림 기와 | 채도 높아 틴트 필요 |
| [clay_roof_tiles](https://polyhaven.com/a/clay_roof_tiles) | 신규 | 400 | 3.1/4.1/2.2 (9.4) | 크기 400 cm, 적갈색에 녹 얼룩 | 줄이 벽돌처럼 보임 |
| [ceramic_roof_01](https://polyhaven.com/a/ceramic_roof_01) | 신규 | 350 | 1.7/2.4/0.6 (4.7) | 이끼 낀 오래된 기와, 가벼움 | 녹황색 — 한국보단 지중해 |
| [grey_roof_tiles](https://polyhaven.com/a/grey_roof_tiles) | 신규 | 300 | 2.0/1.5/0.5 (4.0) | 회색+이끼 — 청회색 시멘트 기와 대용, 매우 가벼움 | 흐린 날 분위기(맑은 하늘엔 칙칙) |
| [asbestos_sheet](https://polyhaven.com/a/asbestos_sheet) | 신규 | 200 | 2.7/2.5/1.7 (6.9) | 짙은 갈색 골판 슬레이트 — 80년대 한국 슬레이트 지붕 연상 | 실제 슬레이트는 회색 쪽(이름은 석면; 게임 에셋이라 무관) |
| **골함석** [rusty_corrugated_iron](https://polyhaven.com/a/rusty_corrugated_iron) | 신규 | 200 | 2.6/2.8/1.7 (7.1) | 적갈 녹슨 골함석 — 열대·공장·창고 지붕 | 붉은 기가 강함 |
| [corrugated_iron](https://polyhaven.com/a/corrugated_iron) | 신규 | 112 | 2.6/3.5/1.9 (8.0) | 회색 새 골함석 | 112 cm라 반복 눈에 띔 |
| [worn_corrugated_iron](https://polyhaven.com/a/worn_corrugated_iron) | 신규 | 180 | 3.3/4.3/2.9 (10.5) | 닳은 회색 골함석 | 용량 큼 |
| [box_profile_metal_sheet](https://polyhaven.com/a/box_profile_metal_sheet) | 신규 | 200 | 1.5/2.5/2.9 (6.9) | 빨간 박스형 금속 지붕·공장 | 채도 매우 높음, 현대식 |
- 평 콘크리트 지붕은 별도 재질 없이 2번 콘크리트 재사용 권장(추정).

### 6. 실내 바닥 (방)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [old_linoleum_flooring_01](https://polyhaven.com/a/old_linoleum_flooring_01) | 신규 | 199 | 2.7/1.8/2.1 (6.6) | 노란 마름모 무늬 장판 — **80년대 한국 장판/비닐** 느낌에 가장 가까움(썸네일), 가벼움 | 무늬가 강해 방 전체에 쓰면 반복 눈에 띔 |
| [linoleum_brown](https://polyhaven.com/a/linoleum_brown) | 신규 | 200 | 2.8/2.5/2.9 (8.2) | 갈색 나무결 장판(무늬 약함) | 단색에 가까워 밋밋 |
| [oak_wood_planks](https://polyhaven.com/a/oak_wood_planks) | 받음 | 120 | 2.7/2.1/4.0 (8.8) | 연한 마루 | ARM 4.0으로 큼, 120 cm |
| [brown_planks_03](https://polyhaven.com/a/brown_planks_03) | 신규 | 100 | 2.4/2.4/0.6 (5.4) | 어두운 갈색 마루, 가벼움 | 100 cm 반복 |
| [interior_tiles](https://polyhaven.com/a/interior_tiles) | 신규 | 190 | 2.9/1.9/2.5 (7.3) | 베이지 대각 줄눈 타일(화장실·주방) | 서양풍 |
| [brown_floor_tiles](https://polyhaven.com/a/brown_floor_tiles) | 신규 | 170 | 3.6/1.4/3.2 (8.2) | 갈색 사각 타일 | 어두움 |
| [concrete_floor_painted](https://polyhaven.com/a/concrete_floor_painted) | 신규 | 200 | 2.5/4.3/1.1 (7.9) | 짙은 칠한 콘크리트(막사·창고) | 어두움 |
- 참고: `floor_tiles_02`는 이름과 달리 대리석 타일(태그 marble)이라 제외.

### 7. 금속 (공장·컨테이너·울타리)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [rusty_metal_sheet](https://polyhaven.com/a/rusty_metal_sheet) | 신규 | 200 | 2.8/2.1/2.5 (7.4) | 황녹색 녹슨 철판, 200 cm | 평범 |
| [container_side](https://polyhaven.com/a/container_side) | 신규 | 194 | 1.8/2.6/1.9 (6.3) | 녹색 골판 컨테이너 면 — 컨테이너 전용(로고 없음 확인은 썸네일만) | 컨테이너 외 용도 제한 |
| [rusty_metal_02](https://polyhaven.com/a/rusty_metal_02) | 받음 | 100 | 2.0/1.3/0.7 (4.0) | 주황 녹, 가벼움 | 100 cm라 큰 면에 반복 |
| [metal_plate](https://polyhaven.com/a/metal_plate) | 받음 | 50 | 2.7/2.5/3.9 (9.1) | 다이아몬드 체크 철판(바닥·계단) | 50 cm, ARM 3.9로 큼 |
| [painted_metal_shutter](https://polyhaven.com/a/painted_metal_shutter) | 신규 | 200 | 2.5/3.1/2.6 (8.2) | 회색 가로줄 셔터(상가 앞) | 셔터 전용 |
| [rusty_metal_grid](https://polyhaven.com/a/rusty_metal_grid) | 신규 | 180 | 2.5/2.5/1.9 (6.9) | 청녹 격자 판(울타리·설비) | 철망 알파 대체 아님 |
| [green_metal_rust](https://polyhaven.com/a/green_metal_rust) | 신규 | 100 | 0.8/0.4/0.5 (1.7) | 녹색 페인트 녹, 극소 용량 | 100 cm |
| [factory_wall](https://polyhaven.com/a/factory_wall) | 신규 | 300 | 1.1/0.7/0.7 (2.5) | 녹색 줄무늬 공장 벽 | 녹색 강함 |
| ACG [Metal064](https://ambientcg.com/a/Metal064) | ACG받음 | — | zip 24.0 | 붉은 도장 손상 금속 | ACG 형식 |
- 철망(알파)은 텍스처 사이트에 없음(자체 제작, 02 문서와 동일).

### 8. 농지 (논·밭·비닐하우스)
Poly Haven에 `farm_furrows` `farm_soil` 두 종이 있어 **밭은 있음**, **논(물)·비닐은 없음**.
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [farm_furrows](https://polyhaven.com/a/farm_furrows) | 신규 | 210 | 2.9/5.0/3.1 (11.0) | 가로 고랑 진 밭(썸네일) — 밭 고랑 | 고랑 방향·간격이 타일 단위라 큰 밭엔 반복 |
| [farm_soil](https://polyhaven.com/a/farm_soil) | 신규 | 200 | 2.8/5.9/3.8 (12.5) | 거친 갈아엎은 흙(밭 일반) | 용량 큼, 노멀 5.9 |
| [dry_mud_field_001](https://polyhaven.com/a/dry_mud_field_001) | 신규 | 300 | 1.4/2.3/0.9 (4.6) | 마른 밭흙/농로, 가벼움 | 어두운 회갈색 |
| [mud_forest](https://polyhaven.com/a/mud_forest) | 신규 | 235 | 3.7/5.9/3.3 (12.9) | 어두운 젖은 진흙 — 논바닥 아래층(물 셰이더 아래) | 용량 큼 |
| [aerial_mud_1](https://polyhaven.com/a/aerial_mud_1) | 신규 | 800 | 2.1/3.0/0.8 (5.9) | 800 cm 크기, 바퀴자국 진흙 — 논둑·농로, 반복 적음 | 항공형이라 근접 해상도 낮음 |
| [mud_cracked_dry_riverbed_002](https://polyhaven.com/a/mud_cracked_dry_riverbed_002) | 신규 | 200 | 3.2/5.6/3.6 (12.4) | 갈라진 논바닥(수확 후) | 용량 큼 |
| **ACG 대안(밭 고랑)** [Ground094A](https://ambientcg.com/a/Ground094A) / [Ground100](https://ambientcg.com/a/Ground100) | — | — | zip 19.1 / 21.2(전 맵) | 물결 능선 모래흙 — 고랑 대용(썸네일, 추정) | 농경지로 단정 못 함, 색 보정 필요 |
- 논: 질감은 `mud_forest` + 기존 `Game/WorldWater` 얕은 변형으로 구성(재질 신규 아님). 모심기 줄·비닐하우스 비닐은 텍스처 없음 → 자체 제작/데칼(**없음**).

### 9. 식생 (지면 풀; 모델·카드는 별도 트랙)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [grass_ground](https://polyhaven.com/a/grass_ground) | 받음 | 251 | 3.8/6.0/3.9 (13.7) | 올리브~갈색 풀, 열대 건기 느낌 | 용량 큼(노멀 6.0) |
| [aerial_grass_rock](https://polyhaven.com/a/aerial_grass_rock) | 받음 | 1500 | 2.6/3.8/0.9 (7.3) | 크기 15 m, 반복 거의 없음 — 지형 레이어 최적 | 항공형, 근접 해상도 낮음, 바위+이끼 섞임 |
| [sparse_grass](https://polyhaven.com/a/sparse_grass) | 신규 | 200 | 3.7/5.9/3.8 (13.4) | 듬성듬성 풀+흙, 가장자리·길가 | 큼 |
| [leafy_grass](https://polyhaven.com/a/leafy_grass) | 신규 | 200 | 4.8/6.0/4.1 (14.9) | 잎 섞인 풀 — 열대 숲 가장자리 | 가장 큼 |
| [withered_grass](https://polyhaven.com/a/withered_grass) | 신규 | 200 | 4.6/6.2/4.8 (15.6) | 마른 풀(건기) | 가장 큼 |
| [grass_path_2](https://polyhaven.com/a/grass_path_2) | 신규 | 100 | 2.8/2.5/0.8 (6.1) | 풀 사이 길 | 100 cm 반복 |
- 야자·바나나·벼는 텍스처가 아니라 모델/카드 — 이 시트 범위 밖.

### 10. 물
Poly Haven 텍스처에 물 재질 **없음**(카테고리·키워드 검색 확인). 자체 셰이더 `Game/WorldWater` 유지.

### 11. 태양광
Poly Haven 텍스처에 태양광 패널 **없음**(키워드 `solar` 검색 확인). **ambientCG 대안**(CC0, https://docs.ambientcg.com/license/ 확인 2026-11-16):
| ID | 링크 | 2K-JPG zip | 적합 이유 | 약점 |
|---|---|---|---|---|
| SolarPanel003 | https://ambientcg.com/a/SolarPanel003 | 12.4 MB | 파란 셀 격자(썸네일) | zip에 전 맵 포함(실사용 맵은 더 작음), 맵 분리형(R·메탈 별도) |
| SolarPanel001 / 002 / 004 | https://ambientcg.com/a/SolarPanel001 (002, 004 동일 형식) | 15.4 / 15.7 / 14.9 | 004는 'rough' 태그(거친 표면) | 변형이 비슷 |
- 프레임·유리 반사는 모델/재질 설정 몫(텍스처 아님).

### 12. 학교·막사 마감 (운동장·본관)
| ID | 상태 | 크기 | D/N/ARM (세트) | 적합 이유 | 약점 |
|---|---|---|---|---|---|
| [baseball_playground](https://polyhaven.com/a/baseball_playground) | 신규 | 300 | 2.8/4.3/3.6 (10.7) | 밟힌 흙 운동장(태그 dirt·trampled·footprints) | 발자국이 반복됨, 선 데칼 별도 |
| [clay_floor_001](https://polyhaven.com/a/clay_floor_001) | 신규 | 200 | 0.5/0.5/0.4 (1.4) | 매끈한 점토 — 극소 용량, 운동장 중앙 대용 | 디테일 거의 없음 |
| 벽: [beige_wall_002](https://polyhaven.com/a/beige_wall_002) / [beige_wall_001](https://polyhaven.com/a/beige_wall_001) | 신규 | 300 | 0.2/0.3/0.1 (0.6) / 0.2/0.8/0.4 (1.4) | 본관 연한 베이지 도장 | 거의 단색 |
| 벽 대안: [peeling_painted_wall](https://polyhaven.com/a/peeling_painted_wall) | 신규 | 180 | (9.2) | 칠 벗겨진 낡은 학교 느낌 | 색 강함 |
| 마감: 4번 `concrete_wall_003` 틴트 | 받음 | 300 | (2.9) | 막사 회반죽 | — |

## 못 찾았거나 약한 분류
- **물**: 없음(셰이더).
- **태양광**: Poly Haven 없음 → ambientCG SolarPanel001~004.
- **논(물 반사)·모심기 줄·비닐하우스 비닐**: 텍스처 없음. 논바닥은 `mud_forest`+물 셰이더, 나머지는 자체 제작.
- **한옥식 검은 곡선 기와**: 없음. 지금 후보는 서양식 점토 기와뿐.
- **철망(알파)·차선 데칼**: 텍스처 사이트에 없음.
- **타일링 개별 확인**: 안 함(추정). 선택 후 Unity에서 반복 육안 확인 필요.
- **다음 단계 제안**: 준환이 고르면 S2가 선택된 ID만 2K `arm`(+diffuse, nor_gl)로 받아 `ASSET_LEDGER.md`에 기록(T-0001 행 갱신).

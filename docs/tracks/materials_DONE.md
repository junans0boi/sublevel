# 재질 트랙 완료 노트 (2026-11-16, 브랜치 `track/materials`)

## 한 것 (판정: 수치 + 캡처 + 테스트)
1. **Poly Haven 7종 반입 + ledger**: `beige_wall_002 brick_wall_001 clay_roof_tiles_03 concrete_pavement old_linoleum_flooring_01 rusty_corrugated_iron rusty_metal_sheet` → `Assets/_Project/Art/Materials/`, ledger A-0035~41, 폴더별 LICENSE.txt (ADR-211). 연결: 벽돌·골함석·기와는 월드에 적용, 보도·학교벽·리놀륨은 `Pavement/SchoolWall/FloorLino` 접근자만 추가(맵 트랙이 호출).
2. **밤하늘 해결 (ADR-210)**: 원인 = 시간대·날씨 설정 기본값이 "무작위"(7~24시 시작·시계 진행, 비·안개 45%). 기본을 낮·맑음으로. 장면 안 확인은 `SceneLook.CaptureGameSky`(EnvironmentView 실제 로직, 소리 없음): 낮 태양 1.50·고도 43°·앰비언트 0.60·안개 0.70/0.77/0.86(ADR-193 값), 밤은 0.22·0.23·하늘 0.12(`materials_captures/sky.txt`, `sky_*.jpg`).
3. **전/후 같은 구도 캡처** `docs/tracks/materials_captures/*_before_after.jpg` (왼쪽 전, 오른쪽 후): 벽돌 집, 골함석 창고, 도로(마을 거리), 지형(전체·산·풀밭). 전 = 건물은 7종 없이 같은 빌드(레거시 폴백), 도로·지형은 S2가 남긴 ambientCG 시절 `Logs/world_before`(카메라 동일).
4. **용량**: 7종 약 46 MB(LFS), 임포트 2048 압축. 장면 텍스처 메모리 318→401 MB(+83 MB, 예산 ≤3 GB). 렌더 시간은 7종 유무 A/B 동일(같은 부하 하에서 hub 평균 ≈10 ms 대 ≈10 ms; 유휴 시 5 ms 수준, 측정은 머신 부하 영향 큼).
5. **테스트**: Unity EditMode 전체 **1222/1222 통과**(신규 `WorldSkyTests.Defaults_AreClearDay_AndThatLookIsBright`). 월드 재생성(`WorldBuilder.Build`) 정상 저장 3회. 첫 Unity 실행(Library 클론) 재임포트 약 20초.

## 못 한 것 / 확인 못 한 것
- **PlayMode 실행 안 함**(소리 규칙). 게임 안(플레이) 확인은 에디터 오프스크린 렌더 + EnvironmentView 적용으로 대체. 실제 빌드·클라이언트 확인은 master 요청 필요. 저장된 사용자 설정(PlayerPrefs)이 "무작위"면 기본값 변경이 안 먹는다(설정에서 바꾸거나 기본으로 되돌리기).
- 기와·리놀륨·보도·학교벽은 현재 월드 배치가 없거나 적어 캡처에 안 보임. tint는 1(미조정).
- 전체 지형 전경에서 "후"가 약간 황갈색 쪽(풀 tint 2.17/2.42/2.27는 S2 값). 취향 조정 대상(materials.json tint).
- 맑은 낮 조명의 그림자 거리·캐스케이드·실내 프로브(ADR-193 후반부)는 이 트랙에서 건드리지 않음(`GraphicsOptions`·URP 소유 밖). 렌더 ms 측정이 부하로 불안정해 확정 못 함.
- `materials.json`은 `docs/bible/data/materials.json`(다른 문서)과 별개 파일이다. 후자는 갱신하지 않았다.

## 병합 시 충돌 가능 지점
- `Scripts/Gameplay/SettingsCatalog.cs` (소유 밖, 시간대·날씨 기본값 2줄)
- `Editor/WorldMaterials.cs` (map 트랙이 `Brick/Steel/RoofTiles` 호출부나 새 건물 재질을 건드리면)
- `Data/Resources/GameData/materials.json`(map이 행을 추가·변경하면), `docs/DECISIONS.md`(ADR-210·211 말미 추가), `docs/ASSET_LEDGER.md`(A-0035~41)
- 생성물(`World.unity`, `World_Meshes.asset`, `Prefabs/World/*`)은 되돌렸다. 병합 뒤 `WorldBuilder.Build`로 재생성 필요. 마스크 PNG(`Prefabs/World/Textures/*_mask.png`)는 Build가 다시 만든다(커밋 안 함).
- 참고: ADR-193이 두 행 중복(tracks.md 지적) — 이 트랙은 재질 쪽 ADR-193(맑은 낮 조명)을 가리킨다.

## 운영 메모
- Unity 실행 뒤 Roster_Swat `.meta`가 지워진다(캐릭터 FBX가 워크트리에 없음) → 매번 `git restore -- Assets/_Project/Art/Characters ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`.
- `WorldBuilder.Build`는 종료 시 Mono segfault가 날 때가 있으나 씬은 이미 저장돼 있다(`&&` 체인에 주의).

## 맵 트랙용: `WorldMaterials` 슬롯·접근자 표 (재질 수 줄이기 작업 시 충돌 참고)
`Set(name, sourceId, tint, tileX, tileY)`가 재질 1개(`World_<name>`)를 만든다. 소스는 `materials.json` 행(`Src("<table id>")`) 또는 고정 ID. **변주가 있는 것은 이름마다 별개 재질**이라 재질 수의 주원인이다(변주 5 × 종류).

| 접근자 | 재질 이름 | 소스 | 변주 | 용도 |
|---|---|---|---|---|
| `Brick(v)` | World_Brick0~4 | wall_brick (brick_wall_001) | 5 | 벽돌 외벽 |
| `Plaster(v)` | World_Plaster0~4 | PaintedPlaster017(고정) | 5 | 미장 외벽 |
| `PlasterInner` | World_PlasterInner | 무텍스처 단색 | 1 | 실내 벽·천장 |
| `Steel(v)` | World_Steel0~4 | roof_tin (rusty_corrugated_iron) | 5 | 창고·공장 외벽 |
| `SteelRoof` | World_SteelRoof | roof_tin | 1 | 철판 지붕 |
| `RoofTiles(v)` | World_RoofTiles0~4 | roof_tile (clay_roof_tiles_03) | 5 | 기와 지붕 |
| `Accent(v)` | World_Accent0~4 | PaintedMetal014 | 5 | 덧문·차양·간판·우편함 |
| `Trim(light)` | World_TrimLight / TrimDark | wall_plaster / floor_wood | 2 | 개구부 테두리·울타리 |
| `FenceWhite` `Wire` `BinGreen` | 동명 | floor_wood / PaintedMetal014 | 1씩 | 울타리·전선·쓰레기통 |
| `Planks` `PlanksDark` `Shelving` `Crate` | 동명 | floor_wood | 1씩 | 마루·선반·상자 |
| `Bedding` | World_Bedding | wall_plaster | 1 | 침구 |
| `Decor(DecorMat)` | World_Decor{Wood, WoodDark, Fabric, Fabric2, Metal, Paint, Warn, Ceramic, Screen, Crate, Lamp} | floor_wood / wall_plaster / metal_rust / PaintedMetal014 | 11 | 가구 부품(ADR-147) |
| `Concrete` `ConcreteDark` `ShaftConcrete` | 동명 | pavement_alt (concrete_floor_worn_001) | 3 | 콘크리트·승강기 통로 |
| `PaintedMetal` `RustyMetal` | 동명 | PaintedMetal014 / metal_rust | 2 | 금속 |
| `Asphalt` `DirtRoad` | 동명 | asphalt / dirt_road | 2 | 도로 |
| `Pavement` `SchoolWall` `FloorLino` | 동명 | pavement / school_wall / floor_lino | 1씩 | **신규, 호출처 없음**(맵 트랙이 사용) |
| `Door()` `DoorStripe()` `DoorSeam()` `ShaftLamp()` `Road(paved)` `Rock(set)` `Terrain()` `Water(...)` | | 자체 | | 문·램프·도로 셰이더·바위·지형·물 |

- 합계 개략: 변주 계열(Brick·Plaster·Steel·RoofTiles·Accent) 25 + 단일 약 30 = 건물 재질 슬롯 후보 55~60. 줄이려면 변주 수(`Variants = 5`)나 틴트를 머티리얼이 아니라 **정점 색/MaterialPropertyBlock**로 옮기는 쪽이 큰 효과(이 트랙 미구현, BACKLOG 후보).
- 이 트랙이 바꾼 줄: `Brick`/`Steel`/`RoofTiles`의 소스 ID(고정 ambientCG → `Src(...)`), `Pavement/SchoolWall/FloorLino` 추가, `TableIds`에 3행, `MetalScale`에 arm 세트 2종, `PackArm` metalScale 인자. 맵 트랙이 위 시그니처를 유지하면 호출부는 충돌 없음.
- 남은 항목: 위 "못 한 것" 절 참고(PlayMode 게임 안 확인, 신규 3종 배치 후 tint 조정, ADR-193 그림자·프로브).

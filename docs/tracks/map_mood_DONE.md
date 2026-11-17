# map_mood DONE (2026-11-17, ADR-282~285)

PUBG풍 분위기 1단계(에셋 없이). 브랜치 `claude/sad-wilbur-5f22b6`, main cf1a437 기준.

## 변경 파일
- `Scripts/Core/World/Vegetation.cs`: 초지 나무(`MeadowDensity`·`AddMeadow`), `Clearance` 추출, `TallMeadow`, 덤불 군락·상한 16000.
- `Editor/WorldBuilder.Vegetation.cs`(풀 맵 tall 굽기, `ViewStats` 추출), `WorldBuilder.cs`(마른 땅 흙 비율 0.36→0.16), `WorldVegetation.cs`(가을 잎 2변종, 풀 `_DryTint`), `WorldMaterials.cs`(Grass/Dirt 기본 틴트).
- 셰이더: `WorldGroundTint.hlsl`(신규 공유), `WorldTerrainPasses.hlsl`, `WorldTerrain.shader`, `WorldTerrainBase.shader`(신규, URP 베이스패스 복사), `WorldFoliage.shader`. 이 셰이더 파일은 소유 목록 밖이지만 거리·지역별 색 변주에 필요했다.
- `Editor/WorldBuilder.Mood.cs`(신규): 전/후 캡처·수치 도구(`WorldBuilder.MoodCheck`, `MOOD_TAG`).

## 수치 (에디터, `docs/tracks/map_mood_captures/stats_*.txt`)
| | 전 | 후 |
|---|---|---|
| 식물 | 53,832 (활엽 20,403·침엽 30,829·덤불 2,600) | 64,171 (22,120·31,465·10,586) |
| 몸통 충돌체 | 51,232 | 53,585 (+4.6%) |
| 풀 맵 tall 칸 | 0 | 20,176 (5.4%) |
| 초지 시야(244점×8방향) | 평균 86.3 m, 28% 30 m 안 차단 | 79.1 m, 32% |
| 눈높이 뷰 식생 | ~109k 삼각형, ~50 드로 | ~127k, ~55 |
| CPU 제출 p99 (에디터, 눈높이 뷰) | 7.46 ms | 8.59 ms |
색: 항공 `air1`에서 노랑 48%→39%, 초록 10%→15%, 주황·적 18%→23%, 색상 표준편차 17.5→22.4(변주 증가). 눈높이 `eye0` 초록 52%→27%·주황 10%→34%(공장 지역 붉은 흙+가을 잎).

## PERF 판정
- 드로콜 ≤2500·삼각형 ≤1.5M: 식생 추정치만 측정(+18k 삼각형, +5 드로). 장면 전체 삼각형·드로는 배치 모드에서 못 재서 **미측정**, 증가분이 작아 예산 위반 가능성은 낮다고 판단(확정 아님).
- 렌더 p99 ≤10 ms: 에디터 CPU 제출 8.59 ms(경계, 노이즈 큼). 플레이어 빌드 실측 필요.
- 충돌체 +2,353(+4.6%).

## 검증
- 결정성: `WorldBuilder.Build` 2회 `World_Meshes.bytes`·`World_Grass.bytes` SHA 동일(메시 3fce956b…는 전/후도 같음: 이번 변경은 메시를 바꾸지 않음).
- EditMode 1428 중 1411 통과, 17 실패는 모두 CharacterAnimator/Roster(이 워크트리에 Mixamo FBX 없음, 맵 세션 때와 같은 원인, 이번 변경과 무관).
- 캡처 `docs/tracks/map_mood_captures/{before,after}_{eye0-3,tall_stand,tall_crouch,tall_prone,air0_R0,air1_R1,air2_R2,air_wide}.jpg`(1280px). 동일 구도.

## 한계·준환이 직접 볼 것
- 근거리 지면(110 m 안)은 아직 갈색/주황 톤이 크다(초지 레이어 텍스처 자체가 갈색 계열). 진짜 연두~노랑 초지는 2단계 텍스처가 필요하다.
- 110 m 베이스맵 경계의 색 이음매, 공장 지역 언덕이 너무 주황인지, 가을 나무 비율(활엽의 40%) 확인.
- 키 큰 풀은 갈대 메시 재사용(0.9~1.5 m): 웅크리면 가려지고 서면 넘겨 보임. 무릎~허리 전용 메시는 BACKLOG.
- 생성물(World.unity·메시·NavMesh·Terrain·풀 맵·재질·terrainlayer, 가을 잎 재질)은 커밋하지 않음: master가 `WorldBuilder.Build`로 재생성.
- 2단계 후보 정리: `docs/ASSET_ROUND_2026-11-17_terrain_mood.md`.

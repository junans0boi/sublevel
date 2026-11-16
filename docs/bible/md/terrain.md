# 산악·지형

> 상태: 제안 · 요약: C2 지형: 최고 203 m, 걷기 가능(35도 이하) 86%. 봉우리 5, 능선 3, 능선 교전지 3, 채석장 2, 강 2. 걷기 목표는 03_mountains.md 제안값(35~38도, 88% 이상).

## 지형 개요

현재 생성기(확정): 산악 구역 A는 중심 (520, 800) 반경 320 m, 정상 약 100 m이고 높이가 도로까지의 거리 하나에 곱해져 방사형 원뿔이 된다. 능선선·안부·테라스·절벽이 없다(03 §1). 플레이어 컨트롤러 slopeLimit은 45도인데 걷기 규칙은 40도라 불일치다(Player.prefab:85, WorldRules.cs:37).

C2의 지형은 설계 페이지의 노이즈(능선 폴리라인 3개 + 봉우리 가우시안 + 안부 낮춤)로 만든 높이장이며 Unity 지형이 아니다. 실제 구현은 03_mountains.md §2.4 레이어 합성(골격 능선 + 도메인 워프 + 테라스 + 협곡 + 절벽 + 안식각 스무딩)을 따른다.


## 현재 생성기 사실

| 상태 | 항목 | 값 | 근거 |
|---|---|---|---|
| 확정 | 지역 경계 능선 높이 | 34 m, 반폭 22 m, 경계당 고개 2개(폭 18 m) | Assets/_Project/Scripts/Core/World/WorldSpec.cs:42,56 |
| 확정 | 산악 구역 A | 중심 (520, 800), 반경 320 m, BaseHeight 18, Relief 84(정상 약 100 m) | WorldSpec.cs:62-63 |
| 확정 | 걷기 경사 규칙 | 40도(WalkSlopeDeg), 연결성만 검사(비율·최대 구배 검사 없음) | Assets/_Project/Scripts/Core/World/WorldRules.cs:37 |
| 확정 | 컨트롤러 slopeLimit | 45도(걷기 규칙 40도와 불일치) | Assets/_Project/Prefabs/M1/Player.prefab:85 |
| 확정 | 대지 경사 한도 | 20도 | WorldRules.cs:36 |
| 확정 | 도로 경사 한도 | 12도 | WorldSpec.cs:49 |
| 확정 | 지형 방식 | Unity Terrain 1장, heightmap 1025(1 m), pixelError 4 | docs/research/03_mountains.md §1 (WorldBuilder.cs 상단 상수) |

## C2 지형 수치

| 상태 | 항목 | 값 | 메모 | 근거 |
|---|---|---|---|---|
| 제안 | 최고 고도 | 약 203 m (동령봉 근처) | 해수면 0 기준. 현재 산악 정상 약 100 m의 2배 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 걷기 가능 면적(35도 이하) | 86% (페이지 측정) | 03 목표 88% 이상에는 2%p 모자란다 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 등고선 간격 | 20 m |  | docs/research/map_options/option_C2_notes.md |
| 제안 | 육지 | 약 3.23 km2 |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 높이장 해상도 | 5 m 격자(페이지) | Unity는 1 m heightmap이 기준 | docs/research/map_options/option_C2_river_mountain.html |

## 걷기·경사 목표 (03_mountains.md 제안)

| 상태 | 대역 | 경사 | 목표 면적비 | 용도 | 근거 |
|---|---|---|---|---|---|
| 제안 | 평지 | 0~8도 | 15~20% | 도로, 마당, 테라스, 안부 | docs/research/03_mountains.md §2.1 |
| 제안 | 완사면 | 8~20도 | 35~40% | 주 이동 사면, 교전 거리 | docs/research/03_mountains.md §2.1 |
| 제안 | 중사면 | 20~30도 | 25~30% | 걷되 느려짐 | docs/research/03_mountains.md §2.1 |
| 제안 | 급사면 | 30~38도 | 8~12% | 지름길, 암석 엄폐 필수 | docs/research/03_mountains.md §2.1 |
| 제안 | 절벽/벽 | 38도 초과 | 5% 이하, 의도된 곳만 | 장벽, 조망 배경. 반드시 우회로 | docs/research/03_mountains.md §2.1 |
| 제안 | 걷기 가능 | 35~38도 이하 | 35도 이하 88% 이상, 38도 이하 95% 이상 | 컨트롤러 한계(45도)보다 낮게 둔 여유 | docs/research/03_mountains.md §2.1 |
| 제안 | 도로 | 최대 12도, 평균 6~9도 |  | 헤어핀 곡률반경 12 m 이상, 구간 경사 6~10도, 3~5단 | docs/research/03_mountains.md §2.1 |
| 제안 | 이동 속도 | 15도 이하 1.0, 30도 0.7, 38도 0.45, 38도 초과 미끄러짐 |  | slopeLimit 45 -> 40도로 낮춤 | docs/research/03_mountains.md §2.1 |

## 지형 구조물 규모 (제안)

| 상태 | 요소 | 값 | 이유 | 근거 |
|---|---|---|---|---|
| 제안 | 능선 폭 | 15~40 m 평탄에 가까운 길, 길이 150~300 m | 능선 위를 달릴 수 있어야 한다 | docs/research/03_mountains.md §2.3 |
| 제안 | 안부 | 정상 -15~30 m, 폭 25~40 m | 도보 횡단 통로 | docs/research/03_mountains.md §2.3 |
| 제안 | 테라스 | 폭 30~80 m, 경사 5도 미만, 단차 10~15 m, 3~5개 | 교전·마당·폐허 위치 | docs/research/03_mountains.md §2.3 |
| 제안 | 협곡 | 깊이 3~6 m, 폭 6~12 m, 길이 80~200 m | 사각지대·측면 이동 | docs/research/03_mountains.md §2.3 |
| 제안 | 절벽 | 높이 6~20 m, 길이 20~60 m, 3~6개, 우회로 필수 | 장벽·조망 | docs/research/03_mountains.md §2.3 |
| 제안 | 정상 접근로 | 최소 3종: 스위치백 도로, 협곡/능선 도보, 사면 직등 | 경로 다양성 | docs/research/03_mountains.md §2.3 |

## 능선·봉우리·교전지·채석장

| 상태 | 이름 | 종류 | 위치(x, y m) | 고도 m | 메모 | 근거 |
|---|---|---|---|---|---|---|
| 제안 | 백운 능선 | 능선 | (250,640) - (1000,340) - (2100,540) |  | 북쪽 산맥, 진폭 165 m, 폭 150 m. 강 발원 협곡을 만든다. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동령 능선 | 능선 | (1560,700) - (1740,1300) - (1640,1650) |  | 동안 남북 능선, 진폭 105 m. 동천 평야와 동령 고원을 가른다. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 서령 능선 | 능선 | (280,1300) - (900,1330) - (1050,1500) |  | 서남쪽, 진폭 92 m. 서남 포구와 청람읍권을 가른다. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 백운봉 | 봉우리(조망) | (680, 430) | 194 | 북쪽 최고봉급. 오버워치 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 서령봉 | 봉우리(조망) | (460, 620) | 152 | 북서 봉우리 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동령봉 | 봉우리(조망) | (1970, 440) | 198 | 최고봉(약 198 m) | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 솔뫼봉 | 봉우리(조망) | (1610, 760) | 121 | 동령 능선 북단, 솔뫼 안부 곁 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 망루봉 | 봉우리(조망) | (660, 1050) | 108 | 청람읍 서쪽 조망대 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 백운 능선 교전지 | 능선 교전지 | (1300, 342) |  | 샘골재 근처, 북령 산악 동쪽 끝 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동령 능선 교전지 | 능선 교전지 | (1735, 1330) |  | 동령재와 남령재 사이 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 서령 능선 교전지 | 능선 교전지 | (470, 1262) |  | 서령재 서쪽, 솔밭 근처 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동령 채석장 | 채석장 | (2090, 600) r105 |  | 채석리 옆, 사무동 중등급 루팅 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 남석 채석장 | 채석장 | (1990, 1330) r90 |  | 남석 구릉(55 m) 곁, 사무동 중등급 루팅 | docs/research/map_options/option_C2_river_mountain.html |

## 강·해안

| 상태 | 이름 | 형태 | 메모 | 근거 |
|---|---|---|---|---|
| 제안 | 청람강 | 북 (1170, 150)에서 남 (1310, 1910)까지 10점 곡선. 반폭 8 -> 30 m(강폭 16 -> 60 m), 하상 고도 42 -> -2 m, 사면 기울기 0.85 -> 0.3 | 섬을 남북으로 가르는 경계이자 병목. 제방 때문에 수변 엄폐 낮음. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동천 | (1700, 1250) -> (1318, 1400) 5점. 반폭 5 -> 9 m, 하상 30 -> 1 m | 동안 지류. 동천교와 얕은 여울이 있다. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 해안 | 타원형 기본 + 노이즈 + 삼각 파형, 해수면 0 m | 현재 생성기는 반경 420 m 둥근 사각형 해안(확정, WorldSpec.cs:39). | docs/research/map_options/option_C2_river_mountain.html |

## 엄폐 밀도 (사면대역별, 제안)

| 상태 | 사면 대역 | 암석/ha | 나무/ha | 통나무/ha | 폐허/ha | 차량/ha | 비고 | 근거 |
|---|---|---|---|---|---|---|---|---|
| 제안 | 평지/테라스 0~8도 | 12 | 25 | 4 | 0.5 | 0.6 | 폐허+차량을 20 m 안에 3~5개 군집 | docs/research/03_mountains.md §3.1 |
| 제안 | 완사면 8~20도 | 20 | 45 | 6 | 0.1 | 0.2 | 주 교전대, 엄폐 간격 12 m 이하 | docs/research/03_mountains.md §3.1 |
| 제안 | 중사면 20~30도 | 30 | 20 | 3 | 0 | 0 | 암석이 주 엄폐 | docs/research/03_mountains.md §3.1 |
| 제안 | 급사면 30~38도 | 25 | 0 | 0 | 0 | 0 | 나무 금지(현 30도 컷 유지), 큰 바위 | docs/research/03_mountains.md §3.1 |
| 제안 | 능선/안부 | 15 | 10 | 2 | 0.2 | 0.1 | 가장 노출, 조망 보상으로 엄폐를 일부러 적게 | docs/research/03_mountains.md §3.1 |

## 엄폐 목표와 현재 상한

- 목표: 완사면·평지 어디서든 반경 12 m 안에 가슴 높이 이상 엄폐 1개 이상, 급사면은 20 m 이내(03 §3.1). 암석 높이 1.2 m 이상이 60% 이상.
- 현재(확정): 산 구역 암석 60/ha, 나무 40/ha, 합계 8000 미만(테스트), 나무 상한 2200, 덤불 2600. 암석 최대 경사 35도, 나무 30도. 출처 03 §1 (WorldGenerator.Scatter, Vegetation.cs). 육지가 3.23 km2로 커지면 총량 재산정이 필요하다.
- 오버워치: 정상선 2곳 + 테라스 외곽 3곳, 한 위치가 모든 POI를 동시에 못 보게. 시야 반경 150~250 m, 8방향 평균 가시거리 60~180 m.

## 테스트 전략 (03 §5, EditMode)

| 상태 | 테스트 | 단언 | 근거 |
|---|---|---|---|
| 제안 | T1 Mountain_SlopeBandsMatchTargets | 0~8도 12% 이상, 35도 이하 88% 이상, 38도 초과 5% 이하 | docs/research/03_mountains.md §5 |
| 제안 | T2 Mountain_MeanSlopeAndSpread | 평균 14~20도, 표준편차 7도 이상, 방위별 비율 1.3 이상(원뿔 방지) | docs/research/03_mountains.md §5 |
| 제안 | T3 Mountain_HasSeveralRidgesAndSaddles | 정상 1~3, 안부 2 이상, 평탄 테라스(900 m2 이상) 3 이상 | docs/research/03_mountains.md §5 |
| 제안 | T4 Mountain_RoadsAreSwitchbacks | 산 도로 최대 12도 이하, 평균 9도 이하, 120도 이상 방향 전환 2회 이상 | docs/research/03_mountains.md §5 |
| 제안 | T5 EveryPoiReachableOnFoot | 35도 이하 걷기 격자에서 모든 엘리베이터·대지·전망점이 도로 허브와 연결 | docs/research/03_mountains.md §5 |
| 제안 | T6 ClimbTimeUnderLimit | 산 기슭 도로에서 각 전망점까지 A* 소요 70 s 이하 | docs/research/03_mountains.md §5 |
| 제안 | T7 TwoDisjointRoutesToSummit | 경로 하나를 막아도 두 번째 경로 존재(겹침 60% 미만) | docs/research/03_mountains.md §5 |
| 제안 | T8 CliffsHaveBypass | 길이 40 m 이상 절벽마다 반경 80 m 안에 우회로 | docs/research/03_mountains.md §5 |
| 제안 | T9 CoverDensityPerSlopeBand | 대역별 엄폐 밀도가 표 범위, 가장 가까운 엄폐까지 95백분위 12 m(완사면)/20 m(급사면) 이하 | docs/research/03_mountains.md §5 |
| 제안 | T10 NoCoverFloatingOrBuried | 암석·나무 y가 Height와 0.5 m 이내, 도로·대지 위 아님 | docs/research/03_mountains.md §5 |
| 제안 | T11 Height_NoOneMeterSpike | 이웃 샘플 고저차 1.4 m 이하(콜라이더 이빨 방지) | docs/research/03_mountains.md §5 |
| 제안 | T12 Terrain_DeterministicHash | 같은 시드 격자 해시 동일(서버·클라이언트 일치) | docs/research/03_mountains.md §5 |
| 제안 | T13 Overwatch_SightlineBand | 전망점 8방향 평균 가시거리 60~180 m, 한 곳이 모든 POI를 못 본다 | docs/research/03_mountains.md §5 |
| 제안 | T14 Mountain_ScatterBudget | 총 스캐터 8000 미만(3.23 km2에서는 재산정), 산 암석 >= 마을의 2배 | docs/research/03_mountains.md §5 |

## 구현·측정 메모

- Terrain 유지(1 m heightmap 1장). 절벽·오버행은 프리팹 메시(암석 군집)로 처리. 2.4 x 2.0 km는 heightmap 2049~4097이 필요할 수 있어 별도 측정이 필요하다(미측정).
- 결정론: 지형은 WorldPlan.Height()가 단일 기준(FillHeights_MatchesHeight 테스트). 새 코드도 사칙연산과 Sqrt만 쓰고 삼각함수는 테이블화한다(03 §2.4).
- 미측정: 현재 경사 분포는 코드 읽기 추정이다(03 A1). 능선 위 샷의 드로우콜·삼각형(PERF_BUDGET 2,500/1.5M)도 측정 전이다.

## 정해야 할 것

- [x] **걷기 한계를 35도(88%)로 할까 38도로 할까?** (확정) → 35도, 88% 이상 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 35도, 88% 이상 / 38도, 95% 이상  · 추천: 35도, 88% 이상
  - 근거: 컨트롤러 slopeLimit을 45 -> 40도로 낮추고 걷기 규칙과 일치시키는 ADR이 필요(03 B1). 페이지 현재값 86%는 목표에 2%p 부족.
- [x] **경사별 이동 속도 계수를 도입할까?** (확정) → 예(15도 1.0 -> 38도 0.45) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 예(15도 1.0 -> 38도 0.45) / 아니오  · 추천: 예(15도 1.0 -> 38도 0.45)
  - 근거: 현재 코드에 slope/slide 처리가 없다(03 §1). 캐릭터 트랙과 합의 필요.
- [x] **최고 203 m를 허용할까(MaxHeight 120 m는 현재값)?** (확정) → 허용(MaxHeight 상향) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 허용(MaxHeight 상향) / 낮춰서 120~150 m  · 추천: 허용(MaxHeight 상향)
  - 근거: 03 §2.3은 70~100 m를 말했지만 이는 1024 m 맵 기준. 2.4 km 맵에서는 비례가 다르다.
- [ ] **경사 대역·규모 수치(03 §2.1~2.3)를 ADR로 확정할까?** (미정) 
  - 선택지: 예 / 측정(A1) 후 확정  · 추천: 측정(A1) 후 확정
  - 근거: 경사 분포는 추정이다. A1/A2(현재 경사 측정, 컨트롤러 실측) 결과로 조정될 수 있다(03 §8).
- [x] **heightmap 해상도를 1 m 유지할까 상향할까?** (확정) → 1 m 유지(타일 분할) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 1 m 유지(타일 분할) / 2049 이상  · 추천: 1 m 유지(타일 분할)
  - 근거: 크기가 4.6배. Terrain 타일 분할/스트리밍 비용은 미측정이라 측정 후 결정.


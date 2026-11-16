# 도로·다리·고개

> 상태: 제안 · 요약: C2 도로망: 노드 22·엣지 29·순환 8, 합 약 12.2 km. 다리 5 + 얕은 여울 1, 차량 고개 5, 보행 안부 3. 일부 구간이 12도를 넘는 것이 알려진 문제(open).

## 도로 체계

도로 등급 3종: 국도(간선·순환, 폭 12 m) 약 2.8 km, 포장 지방도(8 m) 약 8.2 km, 비포장 농로·임도(4 m) 약 1.3 km. 포장·국도만으로 19개 노드(마을 14곳과 고개·교차로)가 이어지고 포장·국도 엣지 하나가 끊어져도 전체가 이어진다(페이지 검사 cutEdges 0, `RoadGraphTests`로 재확인). 소촌 3곳(갯말·솔밭·하동)은 비포장 소로로만 이어진다(ADR-196, 정정: 이전 문구 '모든 마을이 포장·국도만으로'는 틀렸다).

고개 구간은 도로를 사인파 스위치백(진폭 58 m, 주기 150 m)으로 만들고 도로 경사를 11도 한도로 절토한다. 아래 경사는 절토 후 30 m 창 기준 최대/평균이다.

- 현재 생성기(확정): Trunk 6 m / Spur 4.5 m / Path 2.5 m, 경사 12도 이하, 지역당 트리 형태, 지역 경계 고개 2개. 출처 WorldSpec.cs:55-57.
- 도로 데이터 출처는 설계 페이지의 EDGES(수정하지 않은 원본)이며 길이는 곡선 길이를 노드에서 추출한 값이다.

## 도로 구간 (29개)

| 상태 | 구간 | 등급 | 길이 m | 최대 경사 도 | 평균 경사 도 | 절토 최대 m | 스위치백 | 다리·여울 | 12도 초과 | 근거 |
|---|---|---|---|---|---|---|---|---|---|---|
| 제안 | 청람읍 - 새재 | 국도 | 538 | 9.4 | 1.7 | 3 |  | 청람교 51m |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 새재 - 논실 | 국도 | 303 | 10.8 | 3.7 | 4 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 논실 - 남령재 | 국도 | 208 | 7.5 | 4 | 2 | 스위치백 | 동천교 29m |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 남령재 - 광석리 | 국도 | 329 | 18.1 | 3.4 | 4 | 스위치백 |  | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 광석리 - 남포 | 국도 | 611 | 10.8 | 3.2 | 2 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 남포 - 교차로 | 국도 | 442 | 9.6 | 3.7 | 2 |  | 하구교 78m |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 교차로 - 해옥포 | 국도 | 385 | 4.3 | 1.6 | 1 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 솔밭 - 서령재 | 비포장 | 276 | 12.1 | 6.7 | 16 |  |  | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 솔밭 - 학전 | 비포장 | 293 | 16.4 | 3.6 | 2 |  |  | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 학전 - 청람읍 | 포장 | 508 | 9.9 | 2.1 | 1 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 청람읍 - 달뫼 | 포장 | 236 | 11.9 | 6.6 | 8 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 달뫼 - 서령재 | 포장 | 407 | 8.7 | 4.6 | 5 | 스위치백 |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 서령재 - 해옥포 | 포장 | 650 | 12.7 | 3.9 | 3 | 스위치백 |  | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 학전 - 북령읍 | 포장 | 486 | 10.2 | 3.5 | 1 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 청람읍 - 북령읍 | 포장 | 452 | 11.2 | 3.2 | 6 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 북령읍 - 북령고개 | 포장 | 433 | 10.1 | 2 | 5 | 스위치백 |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 북령고개 - 북해리 | 포장 | 263 | 14.5 | 5.1 | 9 | 스위치백 |  | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 북해리 - 샘골재 | 포장 | 789 | 12.3 | 5.7 | 35 | 스위치백 | 발원교 47m | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 샘골재 - 샘골 | 포장 | 343 | 15.6 | 5.2 | 10 | 스위치백 |  | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 북령읍 - 샘골 | 포장 | 606 | 14.2 | 7 | 10 |  | 북강교 39m | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 샘골 - 새재 | 포장 | 583 | 11 | 4.4 | 3 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 샘골 - 채석리 | 포장 | 532 | 12.3 | 6.8 | 10 |  |  | 12도 초과 | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 채석리 - 양지 | 포장 | 387 | 11.3 | 7.1 | 13 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 새재 - 마루재 | 포장 | 340 | 7.2 | 2.8 | 2 | 스위치백 |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 마루재 - 양지 | 포장 | 639 | 8.7 | 2.2 | 3 | 스위치백 |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 양지 - 광석리 | 포장 | 519 | 11 | 3.6 | 7 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 논실 - 하동 | 비포장 | 283 | 9.8 | 6.3 | 3 |  | 동천 여울(여울) 40m |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 하동 - 남포 | 비포장 | 247 | 5.9 | 3.4 | 1 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 갯말 - 교차로 | 비포장 | 157 | 3.5 | 1.6 | 1 |  |  |  | docs/research/map_options/option_C2_river_mountain.html |

## 다리·여울

| 상태 | 이름 | 도로 | 길이 m | 메모 | 근거 |
|---|---|---|---|---|---|
| 제안 | 청람교 | 청람읍 - 새재 (국도) | 51 | 수도 병목. 다리 폭 12 m, 강폭 약 45 m. 앞에 방벽 5개와 감시탑. 구교(보행교 3 m)와 상류 얕은 여울이 우회로. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 북강교 | 북령읍 - 샘골 (포장) | 39 | 북쪽 강 구간. 강폭이 좁은 상류. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 발원교 | 북해리 - 샘골재 (포장) | 47 | 강 발원 협곡. 최상류. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동천교 | 논실 - 남령재 (국도) | 29 | 지류 동천 위. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 하구교 | 남포 - 교차로 (국도) | 78 | 청람강 하구. 가장 긴 다리. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동천 여울 | 논실 - 하동 (비포장) | 40 | 다리 없이 얕은 여울. 설계 메모의 "다리 4 + 여울"은 실제 다리 5(청람·북강·발원·동천·하구) + 여울 1로 집계된다. | docs/research/map_options/option_C2_river_mountain.html |

## 차량 고개 5곳 (스위치백)

| 상태 | 이름 | 좌표(x, y m) | 도로 | 메모 | 근거 |
|---|---|---|---|---|---|
| 제안 | 북령고개 | 1010, 345 | 북령읍 - 북령고개 - 북해리 | 백운 능선을 넘는 북쪽 통로. 구간 최대 14.5도. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 샘골재 | 1400, 355 | 북해리 - 샘골재 - 샘골 | 강 동안 북쪽 통로. 절토가 가장 큼(최대 35 m), 최대 15.6도. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 동령재 | 1735, 1090 | 새재 - 마루재 - 양지 | 동령 능선을 넘어 동안으로. 마루재 마을 위. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 남령재 | 1698, 1445 | 논실 - 남령재 - 광석리 | 남동으로. 남령재 - 광석리 구간 최대 18.1도가 전체 최대. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 서령재 | 640, 1245 | 달뫼 - 서령재 - 해옥포 | 서령 능선을 넘어 서남 포구로. | docs/research/map_options/option_C2_river_mountain.html |

## 보행 안부 3곳 (차량 불가)

| 상태 | 이름 | 도보 경로(좌표) | 메모 | 근거 |
|---|---|---|---|---|
| 제안 | 서령 북안부 | 학전(530, 880) - (520, 505) - (560, 390) | 서령 능선 북쪽 보행 우회로. 학전에서 북령으로. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 서령 안부 | 달뫼(880, 1190) - (885, 1325) - (900, 1440) | 서령 능선을 도보로 넘는 서남 연결. | docs/research/map_options/option_C2_river_mountain.html |
| 제안 | 솔뫼 안부 | 새재(1520, 1090) - (1656, 850) - (1990, 670) | 동령 능선 보행 통로. 새재에서 채석리로. | docs/research/map_options/option_C2_river_mountain.html |

## 알려진 문제: 12도 초과 구간

절토 후에도 9개 구간이 현재 규칙의 도로 한도 12도(MaxRoadSlopeDeg, WorldSpec.cs:49)를 넘는다: 남령재-광석리 18.1도, 솔밭-서령재 12.1도, 솔밭-학전 16.4도, 서령재-해옥포 12.7도, 북령고개-북해리 14.5도, 북해리-샘골재 12.3도, 샘골재-샘골 15.6도, 북령읍-샘골 14.2도, 샘골-채석리 12.3도.

설계 메모의 "구간 최대 약 12~16도"보다 실제 최대(남령재 - 광석리 18.1도)가 크다. 30 m 창 최대값이라 국소 값이고, 평균 경사는 모두 7.1도 이하다. 이 값들이 차량 한계를 넘는지는 Unity에서 측정해야 안다.


## 정해야 할 것

- [x] **12도를 넘는 구간(최대 18.1도)을 어떻게 할까?** (확정) → 스위치백/절토를 늘려 12도로 맞춘다 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 스위치백/절토를 늘려 12도로 맞춘다 / 고개 구간만 한도를 14~15도로 완화(ADR) / 그대로 두고 Unity에서 측정 후 결정  · 추천: 스위치백/절토를 늘려 12도로 맞춘다
  - 근거: 현재 한도 12도는 차량 한계로 정한 값(WorldSpec.cs:49). 완화하면 차량 코드와 NavMesh 설정까지 영향.
- [x] **순환(국도 루프)을 별도 도로 종류 Ring으로 만들까?** (확정) → 기존 Trunk 확장 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 예 / 기존 Trunk 확장  · 추천: 기존 Trunk 확장
  - 근거: 02 §4는 B안에서 Ring을 제안했지만 C2는 강·고개가 노드를 정하므로 Trunk에 다리·고개 개념을 일반화하는 편이 작다.
- [x] **다리를 오염 단계에서 폐쇄 가능한 요소로 쓸까?** (확정) → 다리 폐쇄 없음(준환이 오염 시스템 결정에서 확정) [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 폐쇄 가능(오염 지역 진입 차단) / 항상 개방  · 추천: 폐쇄 가능(오염 지역 진입 차단)
  - 근거: MASTER_PLAN §6 결정 항목 5(다리 폐쇄)와 연결. 규칙 변경이라 ADR 필요.
- [x] **다리마다 NavMeshLink를 두는 것을 전제로 할까?** (확정) → 예 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 예 / 아니오  · 추천: 예
  - 근거: 현재 NavMeshLink 0건(MASTER_PLAN §8). 다리 의존 맵이라 봇 길찾기에 필수.


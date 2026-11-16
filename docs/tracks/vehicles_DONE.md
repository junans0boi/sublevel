# 차량 트랙 DONE (2026-11-16) — 미완료 항목 있음

## Unity 확인 결과 (2026-11-16, 확인함)
- EditMode 전체 **1231/1231 통과**(신규 10 포함, 컴파일 오류 0), 약 2분. 로그 `Logs/test-editmode.log/.xml`.
- 접지 실측 `VehicleBuilder.MeasureProps`(인메모리, 씬 미저장; 옛 월드 지형 기준): 정상 소품 19대 중 5점 접지 오차 최대 **0.021m**(기준 0.03), 피벗=바퀴 바닥(localMinY |값| ≤0.007)으로 확인. 폐차 9대는 프리팹 재생성(BuildPrefabsOnly) 후 바운드 하단 −0.138~+0.044m(이전 최악 −0.278, Sport_Wreck −0.252→−0.112). 로그 `Logs/run-VehicleBuilder_MeasureProps.log`(재생성 전: `Logs/measureprops_before_prefab_rebuild.log`).
- 생성 프리팹은 규칙대로 `git restore`(병합 후 master가 재생성).

## 한 것 (브랜치 `track/vehicles`)
- ADR-201 접지: `Scripts/Core/PropGrounding.cs`(+meta) + `PropGroundingTests`, `VehicleBuilder.PlaceProps` 적용, 폐차 오프셋 −0.22 → −0.08. 초안 roll 부호 오류 수정(ADR-220).
- ADR-198 배치: `Scripts/Core/VehicleSpread.cs`(지역별 1대 우선 + 최원점) + `VehicleSpreadTests`, `AddToWorld`가 사용.
- CC BY 4.0 크레딧: 이미 완료 확인(ledger V-0001~8 ↔ `Credits.cs` 8건, `VehicleModelTests.Credits_CoverEveryLedgerRowThatNeedsCredit`이 대조). 코드 변경 없음.
- ADR-220 기록.

## 못 한 것 (사유)
| 항목 | 사유 / 다음 |
|---|---|
| 위 코드 컴파일·EditMode 통과 | Unity 금지 → master 순번에 EditMode 요청 필요(`test editmode`) |
| 소품 접지 실측(5점 bounds.min.y vs SampleHeight ≤0.03) | Unity. 프리팹 피벗이 바퀴 바닥(y=0)이라는 가정은 **추정** |
| 수량 14+6 적용 | 신맵 병합 후 rules.json SpawnCount 변경 + 지역 인덱스 확정, 오토바이 R0 가중 |
| ADR-203 소리 4건 | 코드 변경 없는 결론. UNITY_CLI.md 문구는 소유 밖 → master가 추가. 재현은 소리 PlayMode라 master 요청 시에만 |
| #90·#99 캡처 판정 | 기존 캡처는 소리 나는 PlayMode 테스트(VehicleFxCaptureTests 등)뿐이고 run-gfx 캡처 메서드가 없다. 새 메서드가 필요(미작성). ADR-146·157은 이미 마감 기록 |
| #237 1인칭 손 | `CombatantAnimation.Vehicle.cs`(애니 트랙 S4 #312 소유) → 이 트랙 범위 밖, 애니 트랙으로 넘김 |

## 병합 영향
- 새 파일 2(Core) + 테스트 2, 수정 VehicleBuilder·VehicleModels·DECISIONS. Core/ 새 파일은 소유 목록 밖이나 추가만(충돌 없음 예상).
- 병합 후 `VehicleBuilder.BuildPrefabsOnly`로 소품 프리팹 재생성 필요(씬은 어차피 재생성).

## 2차 (main 병합 2e3b0e8 이후, 순수 C#·데이터만 — Unity 미실행, **테스트 미확인**)
- 수량 14+6 적용(rules.json·VehicleRules), 첫 6대는 지역별 차, 오토바이는 균등 분포.
- 다리·여울 ±10m 후보 제외, 소품 비틀림 게이트 0.03m(ADR-221).
- 새 EditMode 테스트: `VehicleWorldPlacementTests`(섬 plan에서 20대·지역별 1대·간격 40m·도로 옆·부지 겹침 0·다리/여울 10m), `FuelTests` 도로 16km 커버, `PropGroundingTests` 비틀림.
- 연료 재산정은 계산(ADR-221). 제리캔 배치 보장은 맵 트랙 LootPlacement 소관(요청 필요).
- 아직 Unity 확인 필요: 위 새 테스트(특히 20대 확보 여부 — 후보 부족이면 실패로 드러남), `MeasureProps` 신맵 재측정(오차 ≤0.03·겹침 0), 도로 경사 10° 구간 통행.

## 이슈 상태 (GitHub 모두 OPEN, 확인함)
- #90 차량 피해: 코어(ADR-074·157)는 마감 기록이 있고 잔여는 캡처 판정(T6). 이 트랙에서 닫지 않음.
- #99 1인칭 실내 밝기: ADR-146·157에서 조명 보정 기록, 잔여는 캡처 판정(T7). 닫지 않음.
- #237 1인칭 손: 애니 트랙(S4 #312) 소유, 이 트랙 범위 밖.

## ADR-221 전제 (master 전달)
지역별 제리캔 최소 1개(총 ≥6) 보장은 맵 트랙 `LootPlacement`에 확인·수정이 요청된 상태다. 이 보장이 없으면 연료 계산(시작 35%+제리캔 6개 ≥ 도로 16km×1.5)이 성립하지 않는다. 맵 쪽 반영 확인은 병합 때 master가 한다.

## 3차: 신맵 Unity 확인 (확인함, main + track/map(LootGuarantee) 병합 3c77a60 기준)
- EditMode 전체 1310/1317. 실패 7건은 전부 `CharacterRosterTests`(캐릭터 트랙 에셋·FBX 의존, 차량 무관). 신규 `VehicleWorldPlacementTests` 3, `PropGroundingTests` 7, `VehicleSpreadTests` 4, `LootGuaranteeTests` 6, `FuelTests` 16km 1 전부 통과. 로그 `Logs/test-editmode.xml`.
- `MeasureProps`(섬 plan 높이, 씬 미사용): 도로 옆 지정 차량 20대(지역별 4/4/3/2/3/4, 6지역 모두 ≥1), 정상 소품 접지 오차 최대 **0.005m**(≤0.03), 소품·지정 차량 AABB 겹침 **0**(보수적). 로그 `Logs/measureprops_island.log`.
- 폐차 바운드 하단 −0.066~−0.195는 **옛 프리팹 기준**(BuildPrefabsOnly 미실행, 생성물 복원). −0.22→−0.08 오프셋 효과는 이전 측정(−0.112)로 확인했고, 병합 뒤 master 재생성 때 반영.
- 남은 것: 도로 경사 ≤10° 구간 통행(PlayMode 소리 있음 → 미실행), #90·#99 캡처 판정.

## 4차 (2026-11-17): 핸드브레이크·드리프트, 운전 카메라 (ADR-222)
확인함(Unity, 소리 없음: PlayMode는 AudioListener.volume=0):
- 드리프트 PlayMode `VehicleDriftTests` 6/6 통과(차·오토바이: 슬라이드, S 브레이크 0.93g/0.84g). 로그 `Logs/drift_final_playmode.log`. 값은 ADR-222.
- 카메라 EditMode `VehicleCameraTests` 14/14: 5차종 chase 거리 6.99~7.23m(범위 5.5~7.5), yaw 0/45/90/137°에서 동일, 오토바이 4~5.5m. 옛 방식은 yaw 45°에서 8.85~9.42m(+2.3m)였음(`[camera-old]` 로그).
- EditMode 전체(병합 main 658cd2b + 이 작업): 1372/1386. 실패 14건은 `CharacterAnimatorTests` 7 + `CharacterRosterTests` 7(캐릭터·애니 트랙 에셋 의존, 차량 코드와 무관 — 원인 미확인).
못 한 것:
- 플레이어 빌드 연속 프레임 캡처(3인칭·1인칭 정지/주행/회전)는 하지 못함(빌드·캡처 미실행). 1인칭 "도로가 화면 중앙" 판정도 캡처가 필요해 **미확인**. 코드는 탑승 시 시선을 정면 수평으로 맞추고 FOV 78°를 적용했으나 효과는 캡처로 봐야 한다.
- 차종별 눈 위치 값은 손대지 않음(기존 좌석 눈 + 핸들 아래 보정 유지, 추정).
- "드론 시점" 전체 원인 미재현: yaw 의존 +2.3m는 확인했으나 아주 먼 거리의 원인은 따로 있을 수 있다(World에서 진입 시 거리 로그 필요).
- 실제 키보드(Input System) 입력은 스크립트 입력으로 대체했다(KeyBinds Space→Jump 경로는 코드 확인만).

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

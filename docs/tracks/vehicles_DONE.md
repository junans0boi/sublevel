# 차량 트랙 DONE (2026-11-16) — 미완료 항목 있음, 병합 요청 전 Unity 확인 필요

## 한 것 (브랜치 `track/vehicles`, Unity 미실행 — 컴파일·테스트는 **미확인**)
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
| #90 차량 피해 잔여 | 캡처 판정이 필요(Unity). 미착수 |
| #99 1인칭 실내 밝기 | 캡처 판정이 필요(Unity). 미착수 |
| #237 1인칭 손 | `CombatantAnimation.Vehicle.cs`(애니 트랙 S4 #312 소유) → 이 트랙 범위 밖, 애니 트랙으로 넘김 |

## 병합 영향
- 새 파일 2(Core) + 테스트 2, 수정 VehicleBuilder·VehicleModels·DECISIONS. Core/ 새 파일은 소유 목록 밖이나 추가만(충돌 없음 예상).
- 병합 후 `VehicleBuilder.BuildPrefabsOnly`로 소품 프리팹 재생성 필요(씬은 어차피 재생성).

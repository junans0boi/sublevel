# PlayerController 저지력 소비 1줄 (소유 밖 → 병합 시 master/소유 트랙이 적용)

`Gameplay/PlayerController.cs` ~423행, `CombatantHealing.MoveFactorOf` 곱하는 줄 바로 아래:

```csharp
speedFactor *= Combatant.StaggerMoveFactorOf(_combatant); // stopping power (ADR-182, ADR-240)
```
조준 흔들림: 카메라 조준 오프셋에 `Combatant.StaggerShakeDegOf(_combatant)`(도) 반경의 무작위 흔들림을 더한다(시야 튕김·이동 정지 없음).

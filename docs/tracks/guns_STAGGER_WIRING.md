# 저지력(Stagger) 서버 배선 요구사항 — 봇·네트워크 트랙 인수용 (2026-11-16)

규칙·수치는 끝나 있다(ADR-182, ADR-216): `Core/WeaponStopping.cs`의 `StoppingRules`·`WeaponStopping.Of`·`StaggerState`. 남은 일은 `Combatant`(Net, 소유 밖)에 붙이는 것. 아래는 **확인한 코드 위치**와 **요구하는 인터페이스**.

## 확인한 현재 구조
- 피격 지점: `Combatant.Ballistics.cs:115` — 펠릿(탄)마다 `target.ServerTakeDamage(new DamageInfo{ Zone, WeaponId, Attacker, … })`. 샷건은 펠릿마다 호출된다.
- 모든 서버 피해의 합류점: `Combatant.ServerTakeDamage(in DamageInfo)` (`Combatant.cs:553`), 피해가 실제로 들어간 뒤 `ServerDamaged?.Invoke(...)`.
- 플레이어 이동은 **소유 클라이언트가 계산**한다: `PlayerController.cs:420` `speedFactor`에 `CombatantHealing.MoveFactorOf/BoostSpeedOf`를 곱함. 봇은 `BotController.cs:629/696`에서 NavMeshAgent 속도를 정한다. 그래서 "서버가 이동을 쥔다"가 아니라 **서버가 상태를 정하고 복제 → 소유 클라·봇이 곱한다**.

## 요구 인터페이스 (제안)
1. `Combatant`에 필드 `readonly StaggerState _stagger = new StaggerState();` (서버 전용 권위 상태).
2. `ServerTakeDamage` 안, `applied > 0` 직후·`Push()` 전에 **Cause == Shot 이고 WeaponId > 0일 때만**:
   `_stagger.Hit(GameData.Weapons.Find(dealt.WeaponId), dealt.Zone, NowServer);` (불·익사·근접은 저지력 없음 — 근접은 `WeaponStopping.Of`가 이미 `default` 반환). 사망·넉다운 시 `_stagger.Clear()`.
3. 복제: `SyncVar<StaggerNet>` (struct `{ float ShakeDeg; float Slow; float Remain; }`)를 서버가 **`_stagger` 값이 바뀔 때만** 갱신(Remain = 남은 초). 클라는 로컬 시계로 Remain을 줄인다(재동기화 없이 0.5초 안에 끝남).
4. 소비자 (정적 헬퍼 `CombatantStagger.MoveFactorOf(Combatant)`, `CombatantHealing.MoveFactorOf`와 같은 모양):
   - 플레이어: `speedFactor *= CombatantStagger.MoveFactorOf(_combatant)` (= `1 − Slow` while Remain>0, 아니면 1).
   - 봇: `_agent.speed *= 같은 값`.
   - 조준 흔들림: 소유 클라가 카메라/조준 오프셋에 `ShakeDeg` 반경의 무작위 흔들림을 더한다(시야 튕김·이동 정지 없음. ADR-182 §6). 발사 방향에도 반영할지는 **보류**(현재 규칙은 "조준 흔들림" = 화면 조준). 서버 판정 탄도에 넣으면 별도 ADR.
5. 시계: 서버 `Time.timeAsDouble` 하나만 사용. `StaggerState`는 시계를 모른다(`now` 인자).

## 테스트 기대값 (EditMode는 이미 통과: `WeaponStoppingTests`, PlayMode/서버 테스트는 이 트랙이 작성)
| 시나리오 | 기대 |
|---|---|
| 저지력 10 총 몸통 1발 | 서버 `_stagger.SpeedFactor(t)==0.65`, `ShakeDeg==4`, 0.5초 뒤 1.0/0 |
| 저지력 1(Brisk) 피격 | 효과 없음(`Active==false`) |
| 샷건(Duo, 9펠릿 몸통 9발 같은 틱) | 효과 1회분(합산 없음), 시간만 갱신 |
| 약→강 순서 연속 | 강한 쪽으로 교체 / 강→약은 무시 |
| 팔다리 피격 | 몸통의 ½ |
| 불·익사 피해 | 효과 없음 |
| 사망 후 | `Clear`, 복제 값 0 |
| 클라 복제 | 피격자 소유 클라의 `MoveFactorOf`가 서버 값과 일치(오차 0.01) |

## 하지 않은 것
- `Combatant`·`PlayerController`·`BotController` 수정(소유 밖). 위 항목을 구현하는 트랙이 `Core/WeaponStopping.cs`를 바꿀 일은 없다(필요하면 총기 트랙에 요청).

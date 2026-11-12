# TECH — 기술 스택과 아키텍처

## 선택
| 영역 | 선택 | 메모 |
|---|---|---|
| 엔진 | Unity 6.3 LTS (6000.3.x 최신 패치) | 지원 ~2027-12. 6.0 LTS는 2026-10 종료. Hub의 Recommended가 다른 버전이어도 6000.3 사용 |
| 렌더 | URP | M1 13" 대응 |
| 넷코드 | Fish-Net (무료, CCU 제한 없음, 서버 권위, 클라 예측, 지연 보상) | 대안: Mirror, NGO, Photon Fusion(무료 100CCU) |
| 입력 | Input System | 리바인딩 지원 |
| 카메라 | Cinemachine(교체 가능 컴포넌트) | TPP/FPP |
| 테스트 | Unity Test Framework (EditMode/PlayMode) | 헤드리스 |
| 호스팅(M6) | VPS/Edgegap 자체 호스팅 | Unity Multiplay는 2026-03-31 종료 |
| 폴백 | Godot 4 | M0 게이트 실패 시 |

## 라이선스(엔진)
Unity Personal: 매출+펀딩 20만 달러 이하 무료, Pro $2,310/년/seat, 런타임 수수료 폐지. **고용 상태에 따른 자격은 미확인** → RISKS_OPEN_QUESTIONS. 서드파티 라이선스는 에셋과 동일하게 대장 기록.

## 아키텍처 원칙
1. **서버 권위 우선**: 상태 변경은 서버(싱글에서는 호스트)가 결정. 클라이언트는 입력/표시.
2. **로직/표현 분리**: 존 상태머신, 데미지, 루팅, 칩, 봇 의사결정은 **순수 C# 클래스**(asmdef `Game.Core`, UnityEngine 비의존)로 → EditMode 테스트.
3. **데이터 주도**: 무기/아이템/구역/루팅 테이블은 ScriptableObject/JSON. 코드 수정 없이 밸런스 변경.
4. **시드 기반 랜덤**: 매치 시드로 폐쇄 순서·출구·열쇠 위치를 재현 가능하게(테스트/버그 재현).
5. **카메라는 컴포넌트**: TPP/FPP 교체, 충돌, 실내 처리 독립.
6. **풀링**: 총알/이펙트/사운드 소스 오브젝트 풀. GC 할당 최소화(프레임 중 할당 0 목표).
7. **씬 구성**: 구역별 씬(Additive 로드), 한 번에 활성 구역 1개(이동 중 2개).

## 폴더/어셈블리
```
Assets/_Project/
  Scripts/Core/      (asmdef Game.Core, 순수 C#)
  Scripts/Gameplay/  (asmdef Game.Gameplay, MonoBehaviour)
  Scripts/Net/       (asmdef Game.Net, Fish-Net)
  Scripts/AI/        (봇)
  Editor/            (SceneBuilder, BuildScript, Setup, AssetValidator)
  Tests/EditMode/  Tests/PlayMode/
  Art/ Audio/ Prefabs/ Scenes/ Data/
```

## 성능 기법(기본값)
GPU Instancing/SRP Batcher, LOD, 오클루전 컬링, 텍스처 KTX2 1K~2K, 라이트맵 베이크 + 제한된 실시간 라이트, 구역 단위 로드/언로드, 오디오 동시 발음 수 제한. 수치는 PERF_BUDGET.md.

## 보안/무결성(멀티 전환 시)
서버 검증(이동 속도, 사격 간격, 데미지), 치트 방지는 최소한부터. M6에서 별도 티켓.

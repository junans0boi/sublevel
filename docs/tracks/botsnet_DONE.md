# 봇·네트워크 트랙 DONE (중간 보고, 2026-11-16) — 완성 정의 **미달**

## 한 것 (commit 3개, 컴파일 미확인: Unity 미실행)
- **저지력 서버 배선** `35b91e9`, ADR-240: `Net/Combatant.Stagger.cs`(서버 `StaggerState`, 복제 `SyncVar<Vector3>`+카운터, `StaggerMoveFactorOf/ShakeDegOf`), `Core/StaggerNet.cs`(`StaggerHits.Register`, `StaggerReplica`), `ServerTakeDamage` 훅(Shot만, 사망·넉다운 Clear), 봇 `_agent.speed` 반영. 플레이어 1줄은 소유 밖 → `botsnet_STAGGER_PLAYER_PATCH.md`.
- **50인 파라미터화(T1)** `d9f757f`, ADR-241: `Participants 50·MinBots 10·MaxHumans 40`, 한도 23/24→49/50, `BotFill.Plan`, `--participants`, rules.json·DEDICATED_SERVER 갱신.
- **LagComp 결정화** `8fd0283`, ADR-244: S8 패치 A·B 적용(고정 프레임·손실 0·샷별 모델 로그).

## 확인한 것
- dotnet 하네스(Core만 컴파일·단언): Stagger 등록/무시/복제 미러, BotFill 8케이스, ServerArgs 범위, MatchRules 기본값 — 전부 OK.
- EditMode 테스트 파일 작성: `StaggerNetTests`, `BotFillTests`, `ServerArgsTests` 갱신(24→50, 25→41). **Unity EditMode로는 아직 안 돌림.**

## 못 한 것 / 추정
- Unity 필요: EditMode 전체, `Combatant.Stagger.cs`·`DedicatedServer` 컴파일(구조체 대신 Vector3 SyncVar로 위험 줄임), LagComp 3회 통과, 저지력 PlayMode(서버·클라 복제 오차 0.01).
- 50인 엘리베이터 배정(`Teams.Deploy`) 수용은 미확인.
- 미착수: T2 이름·난이도 혼합, T3~T4 측정 하네스·측정, T5/T6(측정 게이트), T7~T10 봇 오염·열쇠·팀·관심점(S6·S1 API 선행), T11 NavMesh 연결성, T12 통계, T13 PERF 자동화, LagComp 손실 2% 통계 테스트(HIT_FLOOR는 측정 후).
- ADR 242·243 미사용.

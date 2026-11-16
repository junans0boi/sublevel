# 오염·열쇠·칩 트랙 DONE (2026-11-16, 중간 보고 — 완성 정의 미충족)

브랜치 `track/pollution`. Unity 미실행 구간(순수 C#·문서·dotnet 하네스)까지.

## 한 것 (커밋 1개 + 미커밋 1개)
| 항목 | 파일 | 증거 |
|---|---|---|
| 맵→오염 변환 `IPollutionTerrain`·`PollutionGridBuilder`(그리드·인접·면적 가중·해시), 그리드 원점 | `Core/PollutionGridBuilder.cs`, `PollutionFront.cs` | `PollutionWiringTests` |
| 서버 집계 `PollutionMatch`(스냅샷·클라 재계산·해시 비교)·`PollutionExposure`(0.5초 단계·방독) | `Core/PollutionMatch.cs` | 테스트 6건 |
| HUD 모델 `PollutionHud`(배너 카운트다운·비네팅·방독 시간) | `Core/PollutionHud.cs` | 테스트 1건 |
| WP8 `ChipBoard`(드롭·운반자 사망·입금·만료·보관함 파괴·부활·팀 상태) | `Core/ChipBoard.cs` | 테스트 2건 |
| WP7 `KeyElevatorSystem`(승강장 N개·팀 쿨다운·도착 재배정·무효 반환·충전) | `Core/KeyElevatorSystem.cs` | 테스트 2건 |
| ADR-235~239 | `docs/DECISIONS.md` | |
| 서버 `PollutionDirector`(방송·늦은 접속·피해 적용·방독 API) | `Net/PollutionDirector.cs` | **미커밋: 컴파일 미확인**(Unity 필요) |

수치: dotnet 하네스 17/17 통과(새 테스트 포함 전체가 아니라 `PollutionWiringTests` 단독; 기존 41건은 S6에서 Unity 통과).

## 못 한 것 (Unity 순번 필요)
- Unity EditMode 전체 통과 확인, `PollutionDirector` 컴파일 확인·커밋
- 연출(붉은 안개·가이거 훅·지도 점멸·HUD 그리기)과 게임 안 캡처, PlayMode 멀티 복제 테스트
- 방독 아이템(루팅·인벤토리), 열쇠 아이템 배치(루팅 점·보급상자), 칩 드롭·보관함 오브젝트와 Net 연결(`Combatant`·`ElevatorRides`·`KeyHolder` 수정)
- T10 기존 Lockdown/통로 제거(맵 병합 뒤)
- 맵 어댑터(`IPollutionTerrain` 구현)는 `track/map` 병합 뒤 맵 데이터로 작성
- 열쇠 이탈률 25/28% 시뮬 대조(JS 몬테카를로, PlayMode 통계 필요)

## 확인 / 추정
- 확인: 순수 로직(위 테스트). 판 길이·전선 시뮬 대조는 S6 결과(평균 22.1분 대조) 그대로.
- 추정: 클라이언트 해시 일치(플랫폼 부동소수), 그리드 계산 시간(수십 ms), `IBroadcast`의 `int[]` 직렬화.

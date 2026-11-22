# 연습/멀티 동작 통일

- 날짜: 2026-11-23
- 이슈: #460~#468 일부
- ADR: -
- 커밋: 1cebaf8
- main 머지: 916f298
- 배포: v0.3.1

## 한 일
- `IsServerInitialized ? ServerX : CmdX` 지름길 제거, 호스트도 원격과 같은 문 검증·거절 로그, 전용 서버 수영·익사. 분석 docs/MODE_PARITY_AUDIT.md.

## 검증
- ModeParityPlayTests 7/7(재실행 2회).

## 남은 것·주의
- #459 실서버 문은 다음 플레이의 [Doors] refused 로그로 판정.

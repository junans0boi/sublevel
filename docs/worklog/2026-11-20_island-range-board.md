# 대기섬 사격장 부스 + 전광판 TOP3 순환

- 날짜: 2026-11-20
- 이슈: #421, #422
- ADR: ADR-501, ADR-502
- 브랜치: `claude/island-range-board-1aa22b` (커밋 7fe57e4)
- main 머지: ca7b151

## 한 일
- 부스 3개: 부스가 빌려주는 총으로 부스 안 표적만 사격, 나가면 회수, 본 판 이월 없음. 서버 권위.
- 전광판: 카운트다운 / 번지 TOP3 / 사격 TOP3 / 투입 지역 / 팁을 6초씩 순환, 기록은 방 안에서만.

## 검증
- EditMode 1674, 대기섬 PlayMode 14 통과. code-review 지적 사항 수정.

## 남은 것·주의
- LobbyNet Protocol 6.

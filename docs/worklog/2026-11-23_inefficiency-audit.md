# 비효율 코드 정리

- 날짜: 2026-11-23
- 이슈: #469~#476 일부
- ADR: -
- 커밋: 7a109cb
- main 머지: bfc870f
- 배포: v0.3.1

## 한 일
- 40봇 매치 프레임당 GC 5,056→2,790 B, World 로드 경고 제거, LagComp 기록 탐색, 발소리 클립 캐시. 문서 docs/INEFFICIENCY_AUDIT.md.

## 검증
- MatchGcAllocTests 통과.

## 남은 것·주의
- 남은 항목은 문서의 로드맵.

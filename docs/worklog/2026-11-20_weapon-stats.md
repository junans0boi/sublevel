# 총기 스탯 차별화 + 인벤토리 툴팁

- 날짜: 2026-11-20
- 이슈: #427
- ADR: ADR-493
- 브랜치: `claude/weapon-stats-ccb2d0` (커밋 f563bcb)
- main 머지: 8ce04cb

## 한 일
- 총기마다 고유 프로필(발사 모드·TBS·피해·탄속·반동·사거리·탄창). AR=자동+단발, DMR=단발, SR=볼트, SMG=빠른 자동.
- 클래스 라벨(AR/DMR/SR/SMG/SG)·발사 모드·탄종·막대 그래프 툴팁을 인벤토리·바닥 목록·장착 칸에 표시.
- 참고 수치는 docs/research/weapon_stats_reference.md(수치·출처만, 실명 미사용).

## 검증
- EditMode 1651/1651, 신규 PlayMode 3개 통과. 툴팁 캡처 직접 확인.

## 남은 것·주의
- SR 볼트 필드는 bolt-action 브랜치와 같은 데이터를 건드림(머지 시 확인).

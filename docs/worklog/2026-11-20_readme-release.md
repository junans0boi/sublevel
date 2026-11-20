# 저장소 4개 README + GitHub Releases

- 날짜: 2026-11-20
- 이슈: #434
- ADR: ADR-530
- 브랜치: `claude/readme-release-90e7d3` (커밋 8a7f383)
- main 머지: 69704c4

## 한 일
- sublevel(공개) 한국어 README, web·backend·launcher 개발자용 README(환경 변수는 이름만).
- `v*` 태그를 올리면 Actions가 Release 생성. 게임은 `tools/release.sh --github`.
- 첫 Release: sublevel v0.1.0(프리릴리스), web·backend v0.1.0, launcher v0.1.7(Windows exe 첨부).

## 검증
- 백엔드 테스트 46/46, 웹 빌드, 워크플로 YAML 확인. GitHub에서 4개 Release 직접 확인.

## 남은 것·주의
- 게임 main은 1042b41까지 push됨.

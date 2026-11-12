# KICKOFF — 새 Claude Code 세션 첫 프롬프트

> 준환이 새 폴더에서 Claude Code를 실행한 뒤 아래 블록을 **그대로** 붙여넣는다. 이 패키지(CLAUDE.md, docs/, tools/, .gitignore, .gitattributes)는 먼저 그 폴더에 복사해 둔다.

```
이 저장소의 CLAUDE.md를 읽고, 거기 적힌 순서대로 docs/ 문서를 모두 읽어라.
그다음 docs/SETUP.md의 "초기 세팅"을 위에서 아래로 수행한다.
특히 SETUP.md 2단계에서 grilling, to-spec, wayfinder, to-tickets, implement 스킬(mattpocock/skills)을 반드시 설치하고,
setup-matt-pocock-skills 로 이슈 트래커와 docs 위치를 설정해라.
세팅이 끝나면 docs/ROADMAP.md의 M0(기술 검증)부터 docs/WORKFLOW.md의 루프대로 자율 진행한다.
나에게 질문하지 않는다. 모호한 점은 가정을 DECISIONS.md에 기록하고 진행한다.
막히면(결제/계정/약관/되돌릴 수 없는 결정) 티켓에 blocked 라벨과 사유를 남기고 다음 티켓으로 넘어가라.
타이틀이 끝날 때마다 docs/PROGRESS.md를 갱신하고 한국어로 3줄 이내 요약을 남겨라.
```

## 세션이 새로 열릴 때마다 (재개 프롬프트)
```
CLAUDE.md와 docs/PROGRESS.md를 읽고, 열린 wayfinder 지도와 티켓을 확인한 뒤 중단된 지점부터 WORKFLOW.md 루프를 이어서 진행해라.
```

## 사람이 미리 해둘 일 (에이전트가 못 하는 것)
- [ ] Unity Hub 설치 + Unity 계정 로그인 + **개인(Personal) 라이선스 활성화** (GUI 1회)
- [ ] Unity 6000.3.x 설치 (모듈: macOS Mac IL2CPP, Windows Build Support (IL2CPP + Mono)) — SETUP.md 참조
- [ ] GitHub 저장소 생성 + `gh auth login` (이슈 트래커로 GitHub Issues 사용 시)
- [ ] Claude Code 로그인
- [ ] (선택) Windows 테스트 PC 준비

# NEXT_SESSION_PROMPT — 준환이 새 Claude Code 세션에 붙여넣을 프롬프트

## 설치 방법 (준환)
1. 이 zip을 풀어서 `docs/` 아래에 그대로 넣는다. 결과:
   ```
   docs/handoff/README.md
   docs/handoff/ROLE_AND_COLLABORATION.md
   docs/handoff/HISTORY_AND_LESSONS.md
   docs/handoff/PLAYTEST_LOG.md
   docs/handoff/NEXT_SESSION_PROMPT.md
   docs/GAME_FEEL_SPEC.md
   docs/UI_SPEC.md
   docs/NEXT_TITLES.md
   docs/HUMAN_TODO.md
   ```
   (이미 `docs/HUMAN_TODO.md` 같은 이름이 있으면 덮어쓰지 말고 세션에게 병합하라고 하세요.)
2. 새 터미널에서 `cd /Volumes/WorkSpace/Project/sublevel && claude`
3. 아래 블록을 붙여넣는다.

## 붙여넣을 프롬프트
```
상담용 웹 채팅 Claude가 하던 역할(방향 설정, 결과 검수, 다음 지시문 작성)을 이제 네가 이어받는다. 새 문서 세트가 docs/ 아래에 들어 있다.
1) CLAUDE.md와 docs/PROGRESS.md를 읽은 뒤 docs/handoff/README.md의 순서대로 handoff 문서를 모두 읽어라.
2) CLAUDE.md의 "읽는 순서"에 docs/handoff/README.md를 추가하고, docs/DECISIONS.md에 ADR로 "상담 채팅의 역할을 프로젝트 세션이 이어받음, 문서 세트 위치"를 기록해라. 이미 있는 docs/HUMAN_TODO.md 등과 이름이 겹치면 병합해라. 커밋하고 push해라(force push 금지).
3) 이번 세션에서는 docs/NEXT_TITLES.md의 1번 타이틀 "M1.6a-fix"만 진행한다. 준환의 최신 플레이 피드백은 docs/handoff/PLAYTEST_LOG.md의 PT-3이다. 코드와 빌드를 직접 확인하고, 스크린샷을 뽑아 직접 열어 검수해라. 자동 테스트 통과만으로 완료라고 하지 마라.
4) 나에게 질문하지 마라. 모호하면 가정을 ADR로 기록해라. ROLE_AND_COLLABORATION.md의 결정 권한표와 페이스 규칙(한 세션 한 타이틀)을 지켜라.
5) 끝나면 PROGRESS.md, PLAYTEST_LOG.md, HUMAN_TODO.md를 갱신하고, 한국어 3줄 요약과 "직접 확인 목록"을 낸 뒤 멈춰라. 다음 타이틀로 넘어가지 마라.
```

## 이후 세션용 재개 프롬프트
```
CLAUDE.md와 docs/PROGRESS.md를 읽고, docs/handoff/README.md의 읽는 순서대로 필요한 문서를 읽어라. docs/handoff/PLAYTEST_LOG.md의 최신 피드백을 확인한 뒤 docs/NEXT_TITLES.md의 다음 타이틀 하나만 진행해라. 끝나면 3줄 요약과 직접 확인 목록을 내고 멈춰라.
```

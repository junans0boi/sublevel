# handoff/ — 기획 상담 채팅 → 프로젝트 세션 인수인계 (2026-11-12 기준)

지금까지 준환(Junzzang)은 **웹 채팅의 Claude(이하 "상담 채팅")**와 기획·방향·검수를 했고, 구현은 이 저장소의 Claude Code 세션들이 했다. 상담 채팅은 코드와 빌드를 볼 수 없어서 결과를 사진/로그 붙여넣기로만 판단했다. **이제 그 역할을 이 저장소의 세션이 이어받는다.** 이 폴더는 상담 채팅만 알고 있던 것을 옮긴 문서다.

## 이 폴더의 문서
| 파일 | 내용 |
|---|---|
| `README.md` | 이 문서. 읽는 순서와 역할 변경 요약 |
| `ROLE_AND_COLLABORATION.md` | 새 세션의 역할, 준환의 성향, 결정 권한표, 토큰·페이스 규칙, 보고 형식 |
| `HISTORY_AND_LESSONS.md` | 결정 경위, 막힌 곳과 해결, 반복하지 말 실수 |
| `PLAYTEST_LOG.md` | 준환의 직접 플레이 피드백 기록과 처리 상태 |
| `NEXT_SESSION_PROMPT.md` | 준환이 새 세션에 붙여넣을 첫 프롬프트 |

## 같이 넣는 문서 (docs/ 바로 아래)
| 파일 | 내용 |
|---|---|
| `docs/GAME_FEEL_SPEC.md` | 조준·기울이기·피드백·이동 감각 기준(배그 참조) |
| `docs/UI_SPEC.md` | HUD, 인벤토리(Tab), Esc 메뉴, 메인 메뉴 기준 |
| `docs/NEXT_TITLES.md` | 앞으로의 타이틀 큐와 각 타이틀의 완료 조건·사람 확인 목록 |
| `docs/HUMAN_TODO.md` | 사람만 할 수 있는 일 |

## 읽는 순서
1. `CLAUDE.md` → `docs/PROGRESS.md` (현재 상태)
2. `docs/handoff/ROLE_AND_COLLABORATION.md` → `HISTORY_AND_LESSONS.md` → `PLAYTEST_LOG.md`
3. 작업할 타이틀의 `docs/NEXT_TITLES.md` 항목과 관련 SPEC(`GAME_FEEL_SPEC.md`, `UI_SPEC.md`)
4. 필요할 때만 `docs/SPEC.md`, `ROADMAP.md`, `DECISIONS.md` 등

## 핵심 변화
- 이전: 상담 채팅이 지시문을 쓰고 세션이 실행 → 이제: **세션이 스스로 계획·구현·자체 검수**하고 준환에게는 결과와 "직접 확인 목록"만 보고한다.
- 상담 채팅은 앞으로 큰 방향/전략 질문용으로만 남는다. 프로젝트 판단은 이 저장소의 문서(SPEC, DECISIONS, 이 폴더)가 진실의 원천이다.
- 이 문서 세트를 저장소에 넣었으면 `CLAUDE.md`의 "읽는 순서"에 `docs/handoff/README.md`를 한 줄 추가한다.

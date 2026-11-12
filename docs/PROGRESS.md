# PROGRESS — 진행 상황 (타이틀 종료 때마다 갱신)

## 현재 위치
- 마일스톤: M0 (시작 전)
- 진행 중 타이틀: 없음
- 열린 지도 이슈: 없음

## 환경 (SETUP 완료 후 채움)
| 항목 | 값 |
|---|---|
| Unity 버전 | |
| 설치 모듈 | |
| 설치된 스킬 | mattpocock-skills 1.3.1 (플러그인, user scope): grill-with-docs, grill-me, to-spec, wayfinder, to-tickets, implement, tdd, code-review, setup-matt-pocock-skills 확인 |
| 이슈 트래커 | GitHub Issues (junans0boi/sublevel, private), 라벨 생성 완료 |
| mac 빌드 | |
| win 빌드 | |
| EditMode 테스트 | |

## 완료 타이틀 로그
| 날짜 | 마일스톤 | 타이틀 | 결과 | 요약(3줄 이내) |
|---|---|---|---|---|

## blocked 목록 (사람 필요)
| 날짜 | 티켓 | 사유 | 사람이 할 일 |
|---|---|---|---|

## 환경 이슈 / 시도한 접근
- 2026-11-12: node/npx 미설치 → 스킬 설치는 방법 A(플러그인)로 성공해 영향 없음. npx가 필요해지면 재검토.
- 2026-11-12: 플러그인을 세션 도중 설치해 해당 세션에서 Skill 호출 불가(Unknown skill). SKILL.md를 직접 읽어 따름. 새 세션부터 정상 호출 예상.
- 2026-11-12: 6000.3.28f1 설치를 시도했으나 사용자 지시로 중단 → 설치된 6000.6.8f1 사용(ADR-018). /Applications/Unity/Hub/Editor/6000.3.28f1 에 불완전한 폴더가 남아 있음(Unity.app 없음, 사람이 정리).

## 알려진 문제

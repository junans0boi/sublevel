# PROGRESS — 진행 상황 (타이틀 종료 때마다 갱신)

## 현재 위치
- 마일스톤: M0 (진행 중: SETUP 완료, M0-1~3 일부)
- 진행 중 타이틀: 없음
- 열린 지도 이슈: 없음 (blocked: #1 win/IL2CPP 모듈, #2 Fish-Net 라이선스)
- 다음 세션: KICKOFF.md의 재개 프롬프트 사용(스킬 플러그인이 새 세션에서 로드됨)

## 환경 (SETUP 완료 후 채움)
| 항목 | 값 |
|---|---|
| Unity 버전 | 6000.6.8f1 (ADR-018, 사용자 지시로 6000.3 설치 안 함) |
| 설치 모듈 | Mac Standalone(Mono), WebGL, Documentation. **Mac IL2CPP / Windows 모듈 없음** |
| 설치된 스킬 | mattpocock-skills 1.3.1 (플러그인, user scope): grill-with-docs, grill-me, to-spec, wayfinder, to-tickets, implement, tdd, code-review, setup-matt-pocock-skills 확인 |
| 이슈 트래커 | GitHub Issues (junans0boi/sublevel, private), 라벨 생성 완료 |
| mac 빌드 | 성공 (Mono, 77MB, 45s) — IL2CPP는 모듈 없어 불가 |
| win 빌드 | **blocked** — Windows Build Support 모듈 미설치 |
| EditMode 테스트 | 1/1 통과 (헤드리스) |

## 완료 타이틀 로그
| 날짜 | 마일스톤 | 타이틀 | 결과 | 요약(3줄 이내) |
|---|---|---|---|---|
| 2026-11-12 | M0 | 환경 세팅(SETUP 0~5) | 부분 완료 | 스킬·GitHub 트래커·Unity 6000.6.8f1 프로젝트(URP/Fish-Net 4.7.3/Input/Cinemachine/테스트) 구성, EditMode 1/1·mac(Mono) 빌드 성공.<br>win 빌드·IL2CPP는 모듈 미설치로 blocked(#1), Fish-Net 라이선스 검토 필요(#2).<br>다음: 새 세션에서 M0-4 씬 PoC → 프레임타임 캡처 → Fish-Net 스모크 루프 시작. |

## blocked 목록 (사람 필요)
| 날짜 | 티켓 | 사유 | 사람이 할 일 |
|---|---|---|---|
| 2026-11-12 | win 빌드 | Windows Build Support 모듈 미설치 + 추가 설치 금지 지시 | Hub에서 6000.6.8f1에 Windows Build Support(Mono) 추가 설치 또는 설치 허용 지시 |
| 2026-11-12 | IL2CPP 전환 | Mac IL2CPP 모듈 미설치 | Hub에서 Mac Build Support (IL2CPP) 추가 설치 또는 Mono 유지 결정 |

## 환경 이슈 / 시도한 접근
- 2026-11-12: node/npx 미설치 → 스킬 설치는 방법 A(플러그인)로 성공해 영향 없음. npx가 필요해지면 재검토.
- 2026-11-12: 플러그인을 세션 도중 설치해 해당 세션에서 Skill 호출 불가(Unknown skill). SKILL.md를 직접 읽어 따름. 새 세션부터 정상 호출 예상.
- 2026-11-12: 6000.3.28f1 설치를 시도했으나 사용자 지시로 중단 → 설치된 6000.6.8f1 사용(ADR-018). /Applications/Unity/Hub/Editor/6000.3.28f1 에 불완전한 폴더가 남아 있음(Unity.app 없음, 사람이 정리).

## 알려진 문제
- 2026-11-12: Fish-Net 4.7.4 태그는 Unity 6000.6에서 컴파일 실패(업스트림 #1110) → 4.7.3 고정(ADR-019).
- 2026-11-12: IL2CPP 모듈 없음 → Mono 백엔드로 mac 빌드 성공(ADR-020).
- 2026-11-12: 6000.3.28f1 불완전 설치 폴더(약 12GB, Unity.app 없음)가 /Applications/Unity/Hub/Editor/ 에 남아 있음. 사람이 Hub에서 정리.

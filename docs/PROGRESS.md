# PROGRESS — 진행 상황 (타이틀 종료 때마다 갱신)

## 현재 위치
- 마일스톤: M0 (진행 중: SETUP 완료, M0-1~3 완료)
- 진행 중 타이틀: 없음
- 열린 지도 이슈: 없음 (blocked: #2 Fish-Net 라이선스 검토. #1은 6000.3.28f1 전환으로 해결·종료)
- 다음 세션: KICKOFF.md의 재개 프롬프트 사용(스킬 플러그인이 새 세션에서 로드됨)

## 환경 (SETUP 완료 후 채움)
| 항목 | 값 |
|---|---|
| Unity 버전 | 6000.3.28f1 (ADR-021) |
| 설치 모듈 | mac-il2cpp, windows-mono (windows-il2cpp는 사람이 나중에 추가) |
| 설치된 스킬 | mattpocock-skills 1.3.1 (플러그인, user scope): grill-with-docs, grill-me, to-spec, wayfinder, to-tickets, implement, tdd, code-review, setup-matt-pocock-skills 확인 |
| 이슈 트래커 | GitHub Issues (junans0boi/sublevel, private), 라벨 생성 완료 |
| mac 빌드 | 성공 (IL2CPP, 앱 193MB, 6분 9초) |
| win 빌드 | 성공 (Mono, 94MB, 32s) |
| EditMode 테스트 | 1/1 통과 (6000.3.28f1, 헤드리스) |

## 완료 타이틀 로그
| 날짜 | 마일스톤 | 타이틀 | 결과 | 요약(3줄 이내) |
|---|---|---|---|---|
| 2026-11-12 | M0 | 환경 세팅(SETUP 0~5) | 완료(일부 후속 있음) | 스킬·GitHub 트래커·Unity 6000.3.28f1 프로젝트(URP/Fish-Net 4.7.3/Input/Cinemachine/테스트) 구성, EditMode 1/1·mac(IL2CPP)·win(Mono) 빌드 성공.<br>Fish-Net 라이선스 검토 필요(#2). win IL2CPP는 사람이 모듈 추가 후 전환.<br>다음: 새 세션에서 M0-4 씬 PoC → 프레임타임 캡처 → Fish-Net 스모크 루프 시작. |

## blocked 목록 (사람 필요)
| 날짜 | 티켓 | 사유 | 사람이 할 일 |
|---|---|---|---|

## 환경 이슈 / 시도한 접근
- 2026-11-12: node/npx 미설치 → 스킬 설치는 방법 A(플러그인)로 성공해 영향 없음. npx가 필요해지면 재검토.
- 2026-11-12: 플러그인을 세션 도중 설치해 해당 세션에서 Skill 호출 불가(Unknown skill). SKILL.md를 직접 읽어 따름. 새 세션부터 정상 호출 예상.
- (폐기) 6000.3.28f1 설치를 시도했으나 중단 → 설치된 6000.6.8f1 사용(ADR-018). /Applications/Unity/Hub/Editor/6000.3.28f1 에 불완전한 폴더가 남아 있음(Unity.app 없음, 사람이 정리).

## 알려진 문제
- 2026-11-12: Fish-Net 4.7.4 태그는 Unity 6000.6과 6000.3.28f1 모두에서 컴파일 실패(업스트림 #1110) → 4.7.3 고정(ADR-019 유지).
- 2026-11-12: (폐기) 6.6에는 IL2CPP 모듈이 없어 Mono 사용(ADR-020) → 6000.3.28f1 전환으로 해소.
- 2026-11-12: 6000.3.28f1은 Hub 헤드리스 설치가 정상 완료돼 있었음(앞선 '불완전' 기록은 확인 실수). 사용자 결정으로 6000.3.28f1 전환(ADR-021). 6.6 프로젝트는 ../sublevel-6.6-backup 에 보관.
- 2026-11-12: mac IL2CPP 빌드 폴더가 2.2GB(배포 앱 193MB + *_BackUpThisFolder_ButDontShipItWithYourGame 2.0GB). 빌드 크기 예산 측정 시 앱 번들만 계산.

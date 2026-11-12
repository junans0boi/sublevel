# PROGRESS — 진행 상황 (타이틀 종료 때마다 갱신)

## 현재 위치
- 마일스톤: M0 (M0-1~5 완료, **M0-6 구현 진행 중(사용자 지시로 중단)**, 남음: M0-7, M0-8)
- 열린 이슈: 지도 #6, 스펙 #10, 티켓 #12(구현·테스트 5/5 통과, 리뷰 반영분 커밋됨, 이슈 미종료). #11·#7·#8·#9 종료. blocked: #2 Fish-Net 라이선스 검토
- 상태: Fish-Net 4.7.3 두 NetworkManager 스모크(연결·스폰 관측·SyncVar 복제·클라 쓰기 불변) PlayMode 5/5, EditMode 4/4 통과. code-review 지적 반영(ADR-024 수정, try/finally, 같은 포트 재연결 테스트).
- 스킬 호출 규칙: ADR-022(user-only 스킬은 SKILL.md 직접 읽기).
- 다음에 할 일(순서대로):
  1. `Assets/DefaultPrefabObjects.asset`이 Unity 실행 때마다 스모크 프리팹을 등록해 수정됨 → 커밋하지 말고 `git checkout bf6fa76 -- Assets/DefaultPrefabObjects.asset`로 되돌릴 것(현재 작업트리에 변경 남아 있을 수 있음).
  2. mac 빌드(`tools/unity.sh build mac`) 성공 확인(DoD), GLOSSARY에 용어(스모크 픽스처, 서버 권위 검증) 추가.
  3. 리서치 문서를 main으로 이관(`git show research/fishnet-headless-host-client:docs/research/fishnet-headless-host-client.md` → docs/research/), 그 후 원격·로컬 브랜치 `research/fishnet-headless-host-client` 정리(force push·PR 없이 `git push origin --delete`).
  4. #12·#10·#6 종료, 이 문서에 스모크 결과 로그 첨부(Logs/net-smoke/*.log 내용 요약 또는 발췌), 한국어 3줄 요약.
- 미반영 리뷰 지적(선택): NetSmokeFixtures 전역 네임스페이스, SmokeNetObject 릴리스 전 제거/스트립(ADR-024 기록).

## 환경 (SETUP 완료 후 채움)
| 항목 | 값 |
|---|---|
| Unity 버전 | 6000.3.28f1 (ADR-021) |
| 설치 모듈 | mac-il2cpp, windows-mono (windows-il2cpp는 사람이 나중에 추가) |
| 설치된 스킬 | mattpocock-skills 1.3.1 (플러그인, user scope): grilling, wayfinder, to-spec, to-tickets, implement, tdd, code-review 등 (호출 규칙은 ADR-022) |
| 이슈 트래커 | GitHub Issues (junans0boi/sublevel, private), 라벨 생성 완료 |
| mac 빌드 | 성공 (IL2CPP, 앱 193MB, 6분 9초) |
| win 빌드 | 성공 (Mono, 94MB, 32s) |
| EditMode 테스트 | 1/1 통과 (6000.3.28f1, 헤드리스) |

## 완료 타이틀 로그
| 날짜 | 마일스톤 | 타이틀 | 결과 | 요약(3줄 이내) |
|---|---|---|---|---|
| 2026-11-12 | M0 | 환경 세팅(SETUP 0~5) | 완료(일부 후속 있음) | 스킬·GitHub 트래커·Unity 6000.3.28f1 프로젝트(URP/Fish-Net 4.7.3/Input/Cinemachine/테스트) 구성, EditMode 1/1·mac(IL2CPP)·win(Mono) 빌드 성공.<br>Fish-Net 라이선스 검토 필요(#2). win IL2CPP는 사람이 모듈 추가 후 전환.<br>다음: 새 세션에서 M0-4 씬 PoC → 프레임타임 캡처 → Fish-Net 스모크 루프 시작. |
| 2026-11-12 | M0 | M0-4/5 헤드리스 씬 + 프레임타임/스크린샷 캡처 (#3~#5) | 완료 | FrameStats(순수 C#, EditMode 4/4) + PlayMode 캡처 테스트 1/1 통과. Logs/perf/{frametime.json,screenshot.png} 생성·육안 확인.<br>batchmode에서 WaitForEndOfFrame 불가 → 카메라→RenderTexture 캡처 사용.<br>측정값(빈 Plane 씬, 1280x720, 헤드리스): avg 0.20ms/p99 0.23ms — 실제 PERF 기준선 아님(씬이 비어 있음). 다음: M0-6. |

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

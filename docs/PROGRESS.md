# PROGRESS — 진행 상황 (타이틀 종료 때마다 갱신)

## 현재 위치
- 마일스톤: M0 (M0-1~6 완료. 남음: M0-7 에셋 파이프라인, M0-8 PERF 확정) → 이후 M1(사용자 지시: M1까지 진행)
- 진행 중 타이틀: M0-7
- 열린 지도 이슈: 없음 (blocked/검토: #2 Fish-Net 라이선스 검토)
- 스킬 호출 규칙: ADR-022(user-only 스킬은 SKILL.md 직접 읽기).

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
| 2026-11-12 | M0 | M0-6 Fish-Net 서버 권위 스모크 (#6~#12) | 완료 | 단일 프로세스에서 서버 전용+클라 전용 NetworkManager 2개(Tugboat 127.0.0.1)로 연결·스폰 관측·SyncVar 복제·클라 쓰기 불변을 검증. PlayMode 6/6, EditMode 4/4, mac 빌드(IL2CPP) 성공.<br>근거 문서: docs/research/fishnet-headless-host-client.md, ADR-023/024. 테스트 픽스처는 Fish-Net 프리팹 생성기에서 제외(Assets/FishNet.Config.XML).<br>알려진 점: SmokeNetObject는 릴리스 전 제거/스트립 대상. 빌드 후 ProjectSettings의 스크립팅 백엔드 값이 바뀌므로 커밋하지 않고 되돌림. |

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

## M0-6 스모크 테스트 결과 로그 (2026-11-12, `tools/unity.sh test playmode`, batchmode)
결과: PlayMode `result="Passed" total="6" passed="6" failed="0"` (NetSmokeTests 5 + PerfCaptureTests 1). 출처: `Logs/net-smoke/*.log`
```
[ServerAndClient_Connect]
ok: server started
ok: client connected+authenticated
[Teardown_ThenReconnectOnSamePort]
ok: server started
ok: client connected+authenticated
ok: server started
ok: client connected+authenticated
[ServerSpawn_IsObservedOnClient]
ok: server started
ok: client connected+authenticated
server spawned ObjectId=0
ok: spawn observed on client
[ServerSyncVarChange_ReachesClient]
ok: server started
ok: client connected+authenticated
server spawned ObjectId=0
ok: spawn observed on client
server set Counter=42
ok: Counter==42 on client
[ClientSyncVarWrite_DoesNotChangeServer]
ok: server started
ok: client connected+authenticated
server spawned ObjectId=0
ok: spawn observed on client
ok: Counter==7 on client
client attempted Counter=999
server authority held: Counter==7 on both
```

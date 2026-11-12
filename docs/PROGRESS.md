# PROGRESS — 진행 상황 (타이틀 종료 때마다 갱신)

## 현재 위치
- 마일스톤: M0, M1, M1.5, M2-1 완료. **M1.6 게임 감각 진행 중: M1.6a 조준·이동 감각 완료**(ADR-043~046). 사용자 지시에 따라 M1.6a에서 멈춤. 다음은 M1.6b 전투 피드백 → M1.6c 기본 UI → M2-2 봇 AI — 시작하려면 지시 필요.
- 진행 중 타이틀: 없음. 열린 이슈: 없음(#2 Fish-Net 라이선스 검토 제외).
- 조작(M1.6a 이후): WASD 이동, Shift 달리기, Space 점프, 마우스 조준, 좌클릭 사격, **우클릭 꾹 = 견착 / 우클릭 탭 = 조준 켜고 끄기**, **Q/E 누르는 동안 기울이기**, F 줍기, R 재장전, 1/2 무기 전환, H 회복, V 시점 전환, 사망 후 Enter 재시작, **Esc 게임 종료**. (Q 어깨 전환은 없어짐: 기울인 쪽으로 카메라가 넘어간다)
- 빌드: `Build/mac/Sublevel.app`, `Build/win/`(압축본 `Build/Sublevel-win64.zip`). 구역 D에서 시작하려면 `-scene ZoneD`.
- 수치 조정: 조준·기울이기·반동은 `Assets/_Project/Data/Resources/GameData/rules.json`(Aim, Lean, Recoil)과 `weapons.json`(RecoilPitchDeg, RecoilYawDeg).
- 주의: Unity 실행·빌드 후 `ProjectSettings/{ProjectSettings,GraphicsSettings,QualitySettings}.asset`이 저절로 바뀜 → 커밋하지 않고 되돌린다. 원본 다운로드는 `AssetStaging/`(gitignore).

## 사람이 직접 확인할 항목 (M1.6a, Windows 빌드로)
자동 테스트는 "상태가 바뀌는지"만 본다. 아래는 눈·귀·손으로만 알 수 있다.
1. **재실행**: 게임을 Esc로 끄고 다시 실행하면 새 매치인가(루팅이 그대로 있고 체력 100). 창을 Alt+F4나 닫기 버튼으로 끈 뒤에도 그런가. 끈 뒤 작업 관리자에 `Sublevel.exe`가 남아 있지 않은가(남아 있으면 알려 줄 것 — mac에서는 확인 불가).
2. **두 번 실행**: 게임을 켜 둔 채 한 번 더 실행해도 두 번째가 자기 매치를 새로 시작하는가.
3. **견착**: 무기를 든 채 우클릭을 누르고 있으면 카메라가 어깨로 붙고 시야가 좁아지며, 떼면 돌아오는가. 누르고 0.2초 뒤에 견착되는 지연이 거슬리지 않는가.
4. **조준**: 우클릭을 짧게 누르면 눈 위치 시점으로 바뀌고 다시 짧게 누르면 돌아오는가. 꾹/탭 구분(0.2초)이 손에 맞는가.
5. **반동**: 쏠 때 시점이 위로 튀고 돌아오는 정도가 무기별로 적당한가(산탄총이 가장 큼). 조준 시 덜 튀는가.
6. **기울이기**: Q/E를 누르는 동안 몸·카메라가 기울고 떼면 돌아오는가. 엄폐물 옆에서 기울여 쏘면 탄이 엄폐물을 넘어가는가. TPP와 FPP(V) 모두. 기울인 쪽으로 카메라가 넘어가는 것이 배그 느낌과 맞는가. 벽에 붙어 기울일 때 카메라가 벽을 뚫지 않는가.
7. **대각선 이동**: W+A, W+D, S+A, S+D로 움직일 때 다리가 일그러지지 않는가.
8. **다리 속도**: 걷기(4.5m/s)는 다리가 약 1.6배, 달리기(7.0m/s)는 약 2.4배로 재생된다. 다리가 지나치게 빨라 보이면 이동 속도를 낮출지(rules.json) 결정 필요.
9. **발소리**: 발이 닿는 순간에 소리가 나는가. 느린 조준 이동이나 게걸음에서 발소리가 빠지지 않는가.
10. **상체 자세**: 빈손은 평범한 걷기·달리기, 무기 들고 이동은 총을 내린 자세, 조준·사격 때만 겨누는 자세인가. 사격 후 약 0.9초 뒤 총을 내리는 것이 자연스러운가.
11. **총 위치**: 총을 내린 자세에서 총이 손에서 벗어나 보이지 않는가(파지 오프셋은 조준 자세 기준, IK 없음).
12. **피격·사망**: 맞을 때 움찔하는 동작과 사망 동작이 어색하지 않은가.

## 재실행 시 이어지던 문제 (ADR-044)
- **원인**: 게임이 고정 포트 7770에 호스트 서버를 열고 같은 포트로 접속했다. 앞서 실행한 게임 프로세스가 살아 있으면 새 실행은 서버를 못 열고(로그 "Bind exception: Address already in use") 클라이언트만 앞 프로세스의 매치에 접속해, 진행 중이던 매치를 이어서 보게 된다. 게임에 종료 키·메뉴가 없었고 전체 화면·백그라운드 실행이라 앞 프로세스가 남기 쉬웠다.
- **재현**(수정 전 빌드, mac에서 확인): 빌드를 실행해 둔 채 한 번 더 실행 → 두 번째 실행의 전투원 수가 4가 아니라 5(앞 매치의 4 + 새 플레이어).
- **수정**: 매치마다 비어 있는 포트를 골라 쓴다(기본 포트가 비면 그대로). Esc로 종료. 종료 시 서버·클라이언트 연결을 닫는다. 수정 후 같은 재현 절차에서 두 번째 실행의 전투원 4, 루팅 10, 바인드 오류 없음.
- **남은 확인**: Windows에서 창을 닫아도 프로세스가 남는 경우가 있는지는 mac에서 볼 수 없다(위 확인 항목 1). 남더라도 새 실행은 자기 매치를 시작한다.

- blocked/검토(사람): #2 Fish-Net 라이선스 검토, 무기 이름 상표 검토(RISKS), 무기 형상·총성 라이선스 표기 확인(RISKS "M1.5 중 누적"), windows-il2cpp 모듈 추가.
- 스킬 호출 규칙: ADR-022. 안개 없는 작은 타이틀은 지도 생략(ADR-025).
- M2-2 시작 전 알아둘 것: 구역 D에는 아직 훈련용 더미가 수렴 지점에 서 있고 플레이어는 첫 엘리베이터 위치에서 바로 시작한다(임시). 건물 지붕·서브존·열쇠·출구·칩 보관함 위치 없음. 구역 D는 사람이 직접 걸어 보지 않았다(크기감·엄폐 밀도는 자동 규칙으로만 확인). `ZoneInfo.Current`는 구역 하나만 로드된 경우 전제. 그 밖에 이전부터 이어지는 것: (1) 사람이 키보드·마우스로 직접 플레이한 확인은 아직 없음(자동 테스트는 입력 주입 방식) (2) 플레이어 빌드에서 66ms 끊김 1회 관측, 원인 미조사 (3) 샷건은 산탄마다 이펙트 RPC를 보냄(9회/발) — 봇이 늘면 묶어 보내기 (4) 소지품 SyncVar가 슬롯·탄종별로 흩어져 있어 탄종/슬롯 추가 시 여러 파일 수정 필요 (5) 무기·효과음 매핑이 코드에 있음, 사운드 풀링·믹서 없음(ADR-038) (6) 무기 파지는 조준 자세 기준 오프셋뿐이라 달리기·피격 자세에서는 손과 어긋남, 빈손도 조준 자세 (7) 효과음은 귀로 확인하지 않음(재생 호출만 테스트) (8) SSAO가 프레임 비용의 절반 — 구역 D에서 빠듯하면 먼저 끈다.

## 환경 (SETUP 완료 후 채움)
| 항목 | 값 |
|---|---|
| Unity 버전 | 6000.3.28f1 (ADR-021) |
| 설치 모듈 | mac-il2cpp, windows-mono (windows-il2cpp는 사람이 나중에 추가) |
| 설치된 스킬 | mattpocock-skills 1.3.1 (플러그인, user scope): grilling, wayfinder, to-spec, to-tickets, implement, tdd, code-review 등 (호출 규칙은 ADR-022) |
| 이슈 트래커 | GitHub Issues (junans0boi/sublevel, private), 라벨 생성 완료 |
| mac 빌드 | 성공 (IL2CPP, 앱 222MB) |
| win 빌드 | 성공 (Mono, 123MB) |
| 테스트 | EditMode 66/66, PlayMode 34/34 (M1.6a 종료 시점) |

## 완료 타이틀 로그
| 날짜 | 마일스톤 | 타이틀 | 결과 | 요약(3줄 이내) |
|---|---|---|---|---|
| 2026-11-12 | M0 | 환경 세팅(SETUP 0~5) | 완료(일부 후속 있음) | 스킬·GitHub 트래커·Unity 6000.3.28f1 프로젝트(URP/Fish-Net 4.7.3/Input/Cinemachine/테스트) 구성, EditMode 1/1·mac(IL2CPP)·win(Mono) 빌드 성공.<br>Fish-Net 라이선스 검토 필요(#2). win IL2CPP는 사람이 모듈 추가 후 전환.<br>다음: 새 세션에서 M0-4 씬 PoC → 프레임타임 캡처 → Fish-Net 스모크 루프 시작. |
| 2026-11-12 | M0 | M0-4/5 헤드리스 씬 + 프레임타임/스크린샷 캡처 (#3~#5) | 완료 | FrameStats(순수 C#, EditMode 4/4) + PlayMode 캡처 테스트 1/1 통과. Logs/perf/{frametime.json,screenshot.png} 생성·육안 확인.<br>batchmode에서 WaitForEndOfFrame 불가 → 카메라→RenderTexture 캡처 사용.<br>측정값(빈 Plane 씬, 1280x720, 헤드리스): avg 0.20ms/p99 0.23ms — 실제 PERF 기준선 아님(씬이 비어 있음). 다음: M0-6. |
| 2026-11-12 | M0 | M0-6 Fish-Net 서버 권위 스모크 (#6~#12) | 완료 | 단일 프로세스에서 서버 전용+클라 전용 NetworkManager 2개(Tugboat 127.0.0.1)로 연결·스폰 관측·SyncVar 복제·클라 쓰기 불변을 검증. PlayMode 6/6, EditMode 4/4, mac 빌드(IL2CPP) 성공.<br>근거 문서: docs/research/fishnet-headless-host-client.md, ADR-023/024. 테스트 픽스처는 Fish-Net 프리팹 생성기에서 제외(Assets/FishNet.Config.XML).<br>알려진 점: SmokeNetObject는 릴리스 전 제거/스트립 대상. 빌드 후 ProjectSettings의 스크립팅 백엔드 값이 바뀌므로 커밋하지 않고 되돌림. |
| 2026-11-12 | M0 | M0-7 에셋 임포트 파이프라인 + 대장 검증 (#13, #14) | 완료 | Art/Audio 임포트 규칙(최대 2048·압축·읽기 비활성·노멀/마스크 이름 규칙), ASSET_LEDGER 미기록·LICENSE.txt 누락 시 빌드/CLI 실패. EditMode 12/12.<br>KTX2는 Unity 네이티브 압축으로 해석(ADR-026), 에셋 단위 규칙(ADR-027).<br>별도 code-review는 M1 리뷰 때 함께 수행 예정. |
| 2026-11-12 | M0 | M0-8 PERF_BUDGET 측정·확정 (#15) | 완료 | 부하 씬 7단계 실측으로 드로우콜 ≤2,500·삼각형 ≤1.5M·렌더 ≤10ms 확정(ADR-028). PlayMode 7/7, mac·win 빌드 성공.<br>한계: 에디터 오프스크린 측정 → M1 게이트에서 플레이어 빌드로 재확인.<br>메모리·로딩·빌드 크기는 잠정치. |
| 2026-11-12 | M1 | 코어 루프 (#16~#25) | 완료 | 테스트 박스에서 호스트로 이동·TPP/FPP 카메라·루팅·무기 2슬롯·사격·재장전·회복·더미 교전·사망·재시작이 동작. 체력·사격·탄약·루팅은 서버 결정(ADR-029), 수치는 JSON(ADR-032).<br>EditMode 28/28, PlayMode 14/14, mac(IL2CPP)·win(Mono) 빌드 성공. mac 플레이어 빌드 실측 평균 0.86ms·p99 3.73ms(1080p), 빌드 안에서 플레이어·더미·루팅 스폰 확인.<br>code-review 2축 수행 후 반영(사격 시작점 서버 고정, 재시작 서버 요청, 시드 난수, 교체 무기 드롭, 테스트 보강). 세부 룰 가정은 ADR-034. |
| 2026-11-12 | M1.5 | 에셋 도입 (#26~#32) | 완료 | 캐릭터(Swat)+애니메이션 8상태, 무기 4종 모델(손·바닥 루팅), 테스트 박스 PBR·HDRI·주광/보조광·포스트프로세싱, 총성·재장전·발소리·피격음 적용. 전부 CC0 직접 다운로드, 대장 11건·검증 0건.<br>EditMode 35/35, PlayMode 17/17, mac·win 빌드 성공. mac 빌드 평균 2.0ms·p99 10~13.5ms(무제한), 60Hz에서 누락 프레임 0. 에디터 렌더 p99 5.82ms.<br>code-review 2축 반영(이동 중 피격 애니메이션, 죽은 코드 제거, ADR-코드 수치 일치, 라이선스 사본 보관). 가정은 ADR-036~038. |
| 2026-11-12 | M2 | M2-1 구역 D 블록아웃 (#33~#36) | 완료 | 160x120m 공장 블록아웃을 코드로 생성: 구획 4개(조립 홀·창고 홀·적재 야드·동력 광장)마다 수렴 지점, 네 변에 엘리베이터 4곳, 중2층+경사로 2개, 블록 155개. 기본 도형·단색·내장 스카이박스만 사용(외부 에셋 없음). 내비메시를 굽고 모든 엘리베이터→수렴 지점·루팅 지점·중2층 경로를 헤드리스로 검증.<br>EditMode 55/55, PlayMode 25/25, mac·win 빌드 성공. 구역 D: 렌더 p99 7.18ms, 60Hz 평균 16.67·p99 17.6ms(누락 0), 제한 해제 평균 1.9ms. PERF_BUDGET에 판정 조건 명문화(ADR-041).<br>code-review 2축 반영(경사로 발치를 막던 기계 이동, 구획별 엄폐·경사로 진입 규칙, 상자 위 내비메시 고립 영역 제거, 측정 헬퍼 상태 복원). |
| 2026-11-12 | M1.6 | M1.6a 조준·이동 감각 (#37~#42) | 완료(사람 확인 대기) | 재실행 시 이어지던 원인(고정 포트로 앞 프로세스 매치에 접속)을 재현해 수정. 우클릭 꾹=견착·탭=조준(이동 속도·탄퍼짐·반동·FOV는 데이터), Q/E 기울이기(눈·사격 시작점·카메라·상체, TPP/FPP), 2D 이동 블렌드, 발 접지 발소리, 상체 레이어(준비·조준·빈손). 새 외부 에셋 없음.<br>EditMode 66/66, PlayMode 34/34, mac·win 빌드 성공. 60Hz 평균 16.67·p99 17.8ms(누락 0), 제한 해제 평균 1.9~2.0ms, 렌더 p99 8.03ms(테스트 박스)·9.72ms(구역 D).<br>code-review 2축 반영(걸음 속도 측정 방식, 다리만 재생 속도 조절, 피격 시 상체, 벽 옆 기울이기 카메라, 반동 배율·상체 기울임·블렌드 구성 테스트 추가). 클립 고유 속도가 게임 이동 속도보다 느려 다리 재생이 빨라짐 — 사람 확인 항목 8. |

## blocked 목록 (사람 필요)
| 날짜 | 티켓 | 사유 | 사람이 할 일 |
|---|---|---|---|
| 2026-11-12 | #26 | Quaternius Universal Base Characters / Universal Animation Library는 itch.io 결제 위젯(0원 구매 절차)으로만 배포 | 필요하면 직접 받아 `AssetStaging/`에 넣기. 현재는 Ultimate Modular Men(애니메이션 내장)으로 대체 |
| 2026-11-12 | #26 | Mixamo(계정·약관), Sonniss GameAudioGDC(약관 동의·대용량 번들) | 쓰려면 사람이 약관 확인 후 파일 제공. 효과음은 직접 받을 수 있는 CC0 소스로 진행 예정 |

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

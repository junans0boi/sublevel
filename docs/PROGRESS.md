# PROGRESS — 진행 상황 (타이틀 종료 때마다 갱신)

## 최신 상태 — 2026-11-18 15:42 (Codex)

- 후속 분석·기획 이슈 정리: Claude 대화 12개와 저장소/공개 URL 대조. 구형 기획 7개를 종료해 **OPEN 136개**, 대기섬 13개 보류 유지. 유효한 잔여 요구는 후속 이슈에 보존했다. 상세: `ISSUE_RECONCILE_2026-11-18.md` 3차 점검.
- 런처 공개 0.1.6/백엔드 00f1972 확인. 게임의 `sg_` 토큰 교환·Social/Ticket 코드 및 봇 B7은 원본 worktree 미커밋 상태이며 main 통합과 종단 검증이 남아 있다. 이번 후속 작업은 분석·문서·이슈 정리만 수행.
- `integration/r2`를 `main`에 ff-only 병합: `731eb29`. 여울 수심 수정과 큐 파티 크기 전달 보완 포함. `*.unity` LFS 복구 완료.
- 병합 후 World → 캐릭터 staging → 애니메이션 → TestBox → MainMenu 재생성 완료. 전체 EditMode **1510/1510 통과**(`Logs/test-editmode.xml`).
- macOS 빌드 성공: `Build/mac/Sublevel.app`(3493 MB, 3분 8초), Windows 빌드 성공: `Build/win/Sublevel.exe`(952 MB, 1분 22초). 근거: `Logs/build-mac.log`, `Logs/build-win.log`의 `[BuildScript] ... Succeeded`.
- 병합·빌드 작업은 준환 요청으로 로컬 빌드까지 진행한 뒤 중단했고, 이후 위의 분석·기획 이슈 정리만 재개했다. push·배포는 하지 않았으며 생성물과 문서 변경은 미커밋 상태로 보존.
- #424는 실제 파티 구성원과 매치 참가자의 안정적인 매핑·종단 검증이 남음. 로비 프로토콜이 2이므로 서버도 같은 버전으로 갱신해야 함.

- 2026-11-19 문서(claude/docs-r5): `DEDICATED_SERVER.md` 인자 범위를 80인 기준(--bots 0..79, --max-players 1..64, --participants 2..80, MinBots 16)으로 고치고 동시 방 수 계산법 추가. 정의서 bots/network를 BotNames·BotMix·BotPollutionPlan 구현과 80인 기준으로 갱신(`python3 tools/bible_build.py`). BACKLOG에 봇 차량 운전·NavMeshLink·은닉(#340).
- 2026-11-19 문서(claude/docs-r5): `AUDIO.md`에 현재 연결 상태 표, 정의서 audio를 Freesound 녹음(S-0008~0045) 기준으로 갱신, ledger S-0001~0007에 교체/사용 표시, RISKS에 크레딧 원 제목 상표 질문(#293).

아래는 이전 기록이다.

> **2026-11-15 상태 한 줄**: 싱글 PUBG 격차(G1~G20, G17 제외)와 멀티플레이 #158~#172가 끝나 main 9e9c472·ADR-145까지 반영됐고, 열린 이슈는 #2(사람)·#55·#90·#99·#173·#188·#189뿐이다 — 아래 "현재 위치" 이하는 2026-11-13~14 기록이며 최신은 `handoff/RESUME_ON_NEW_MAC.md` 10장.

## 현재 위치 (2026-11-13 15:40 갱신)
- 진행 방식: 마스터 세션이 서브 에이전트를 병렬로 돌리고(코드 작성은 작업 사본에서 Unity 없이), 통합 에이전트 하나가 Unity로 컴파일·테스트·캡처·빌드한다(ADR-047, 052). 이슈 지도는 #55.
- 게임 시작: 메인 메뉴 → 1km 야외 섬(World). 무작위 지역의 엘리베이터에서 투입(문 3초, 보호 6초).
- 구현·검증된 것(통합 테스트 통과, 캡처 확인): 조준(견착/탭)·기울이기·앉기(C)·엎드리기(Z)·무장 해제(X, 이동 가속)·점프·주먹(맨손 좌클릭) / 무기 5종(권총·기관단총·소총·산탄총·저격소총 Sentinel), 구경 4종, 5칸 슬롯, 단발/연사(B) / 조준경 8종(반사·투영 조준 사이트, 2·3·4·6·8·15배율, 6배 이상 휠로 가변 배율) / 헬멧·조끼 3단계, 사망 전리품, 파편 수류탄·연막탄(G, T 전환), 시간 회복(붕대·응급키트·구급상자), 가방 용량, 탄약 자동 줍기 / 헤드샷, 낙하 피해, 킬 로그, 승패 화면 / HUD v2(미니맵·5칸 띠·방위·지역 카드), Tab 인벤토리(4구역, 끌어다 놓기, 아이콘), M 전체 지도, Esc 메뉴, 환경 설정(탭 5개) / 봇 AI(줍기·교전·엄폐·회복) / M3 지역 봉쇄(경고→봉쇄→폭격→폐쇄, 통로 차단기, 방사능, 열쇠) / 전체 한글(Noto Sans KR).
- 진행 중: 지역 간 엘리베이터 이동(#76), 나무·풀·맵 마감(#71), M4 풀 매치 자동 테스트와 봇 16~24명(#77).
- 보류(사용자 결정): 조준경 마감(#83).
- 최근 테스트(조준경 통합 32924f1): EditMode 384/384, PlayMode 188 중 183 통과(렌더 예산은 부하 높을 때 불안정, 포트 테스트는 수동 실행 게임과 충돌).
- 최근 테스트(#71/#83/#92/#93/#89 통합 479a7f5 + 테스트 정리, 2026-11-14): EditMode 498/498(무기 id 21~24 기대값 갱신). PlayMode(!Long) 229 중 224 통과 후 실패 3건 수정(근접 무기 반동 0 허용, 기절 사망 시 Knocked 해제 1프레임 대기, World 숲 렌더는 B 에이전트 동시 실행 부하로 p99 11.3ms였고 단독 재실행에서 통과). 재실행(관련 4개 클래스) 25/25.
- 최근 테스트(940a953, 차량 프리팹 재빌드 7a7c7cc 포함, 2026-11-14): PlayMode(!Long) 234 중 233 통과, 실패 0, Skip 1(VehicleCaptureTests 촬영용). 저격 40m 테스트는 사선 위 소품을 꺼서 판정 불가 해소. World 렌더 p99 3.8~5.9ms, 숲 4.3ms.
- 최근 테스트(UI 수정 #101 반영 후, 2026-11-14): EditMode 498/498. PlayMode(!Long) 233 중 229 통과, 실패 2(VehicleFuelTests 2건: 차 프리팹에 연료 탱크 없음, 차량 담당 B 소관), Inconclusive 1, Skip 1. Long(#77) 1/1 통과(매치 233초, 실시간 117초). World 렌더 p99 5.0~6.9ms, 숲 6.8ms.
- 성능(#71 나무·풀, 에디터 오프스크린 1080p, 단독 실행): World 지역 A 평균 4.4ms/p99 8.8ms(드로우 556, 삼각형 688k, SetPass 42), B 3.9/6.4, C 4.0/5.5, D 3.6/6.2, 숲(20m 안 나무 15) 4.6/6.6 -> 모두 p99 10ms 이하, 풀 거리·밀도 변경 없음. 부하 중 측정은 p99가 흔들린다(11.3ms).
- 성능: World, 1920x1080, 봇 8~9 — 60Hz p99 약 17.7ms(통과), 제한 해제 평균 2~2.4ms.
- 빌드: `Build/mac/Sublevel.app`(유니버설, macOS 12+), `Build/Sublevel-win64.zip`, 옮기기용 `~/Desktop/Sublevel-mac.zip`.
- 환경: git-lfs 필요(PNG는 LFS). Unity 실행 후 ProjectSettings 3개 파일은 되돌린다(EditorBuildSettings는 유지).

## 사람이 직접 확인할 항목 (M1.6a-fix, Windows 빌드로)
세션이 빌드 화면 저장본으로 확인한 것: 조준선이 상태별로 달라짐, 조준·FPP에서 무기가 보임, 기울이기 폭, 점프 포즈, 옆·대각선 이동 때 다리·상체 방향, 이동 중 조준 시 총 방향(위에서 본 화면). **정지 화면으로는 알 수 없는 것**이 아래다.
1. **기울이기**: Q/E가 이제 과하지 않은가. 너무 작아졌다면 `Lean.OffsetMeters`(0.25)·`BodyRollDeg`(10)·`EyeViewRollDeg`(6)를 올린다. TPP에서 카메라가 기울지 않고 어깨를 넘어가지 않는 것이 배그 느낌과 맞는가.
2. **엄폐물 옆 기울이기**: 눈 이동이 0.25m로 줄었다. 엄폐물 옆에서 기울여 쏘면 여전히 탄이 넘어가는가(오른쪽·왼쪽 모두). TPP에서 왼쪽으로 내다볼 때 카메라가 엄폐물에 가리는 것이 거슬리는가.
3. **점프**: 뜰 때·떠 있을 때·착지 때 자세가 어색하지 않은가(전용 점프 동작이 아니라 달리기 동작의 한 순간을 멈춘 포즈다). 제자리 점프에서 다리가 너무 벌어져 보이는가. 착지 소리 크기는 적당한가.
4. **조준선**: 지향 사격 때 네 갈래 선, 견착 때 좁아짐, 조준(탭) 때 점 하나 — 잘 보이는가. 산탄총은 넓고 소총은 좁은가.
5. **조준 시점의 무기**: 우클릭 탭 또는 V(FPP)에서 무기가 화면에 보이는가. 팔 없이 총만 떠 있는 것이 임시로 봐줄 만한가. 총 위치가 조준점을 가리지 않는가.
6. **옆·대각선 이동**: A/D, W+A, W+D, S+A, S+D에서 다리가 가는 방향으로 돌고 상체·총은 앞을 보는가. 허리가 과하게 비틀려 보이는가. 방향을 바꿀 때 다리가 도는 속도가 자연스러운가.
7. **이동 중 조준**: 걸으면서 견착하면 총이 조준점을 향하는가(이전에는 오른쪽으로 약 25° 틀어져 있었다).
8. **다리 속도**: 걷기·달리기에서 다리가 이전보다 느리게 돈다(평소 약 3.2, 달리기 약 4.3걸음/초). 대신 발이 조금 미끄러진다. 어느 쪽이 덜 거슬리는가(`Gait.LegPaceFollow`: 0이면 미끄러짐만, 1이면 예전처럼 빠른 다리).
9. **발소리**: 걸음과 맞는가, 점프 중에는 안 나고 착지 때 한 번 나는가.
10. **M1.6a에서 넘어온 미확인**: Esc·Alt+F4·닫기 버튼으로 끈 뒤 다시 켜면 새 매치인가, 작업 관리자에 `Sublevel.exe`가 남지 않는가. 꾹/탭 0.2초 구분이 손에 맞는가. 무기별 반동과 조준 시 반동 감소. 총을 내린 자세에서 총이 손에서 벗어나 보이지 않는가.

## 알려진 한계 (M1.6a-fix)
- 점프·게걸음 전용 애니메이션이 없다(Swat 파일에 없음). 점프는 멈춘 포즈, 옆 이동은 다리 돌림 + 허리 비틀기(ADR-048, 049). 정확히 옆으로 갈 때 발이 조금 미끄러진다.
- 눈 위치 시점의 무기는 팔 없이 떠 있고 반동·재장전 동작이 없다. 조준경·가늠쇠 정렬도 없다(무기 윗면 + 점).
- 조준선은 이동·연사에 따라 벌어지지 않는다(서버 탄퍼짐이 조준 상태에만 좌우되므로 그대로 표시). HUD 본체는 여전히 좌하단 텍스트(M1.6c), Esc는 즉시 종료(M1.6c), 총알·피격 효과 없음(M1.6b).
- 렌더 예산 A(구역 D)는 장비가 뜨거우면 10ms를 넘는다(PERF_BUDGET "M1.6a-fix 게이트 실측"). 식힌 뒤 5회 통과.
- `-feelcapture`·`-perfcapture`가 일반 빌드에 들어 있다(릴리스 전 제거, RISKS).

## 재실행 시 이어지던 문제 (ADR-044)
- **원인**: 게임이 고정 포트 7770에 호스트 서버를 열고 같은 포트로 접속했다. 앞서 실행한 게임 프로세스가 살아 있으면 새 실행은 서버를 못 열고(로그 "Bind exception: Address already in use") 클라이언트만 앞 프로세스의 매치에 접속해, 진행 중이던 매치를 이어서 보게 된다. 게임에 종료 키·메뉴가 없었고 전체 화면·백그라운드 실행이라 앞 프로세스가 남기 쉬웠다.
- **재현**(수정 전 빌드, mac에서 확인): 빌드를 실행해 둔 채 한 번 더 실행 → 두 번째 실행의 전투원 수가 4가 아니라 5(앞 매치의 4 + 새 플레이어).
- **수정**: 매치마다 비어 있는 포트를 골라 쓴다(기본 포트가 비면 그대로). Esc로 종료. 종료 시 서버·클라이언트 연결을 닫는다. 수정 후 같은 재현 절차에서 두 번째 실행의 전투원 4, 루팅 10, 바인드 오류 없음.
- **남은 확인**: Windows에서 창을 닫아도 프로세스가 남는 경우가 있는지는 mac에서 볼 수 없다(위 확인 항목 1). 남더라도 새 실행은 자기 매치를 시작한다.

- blocked/검토(사람): #2 Fish-Net 라이선스 검토, 무기 이름 상표 검토(RISKS), 무기 형상·총성 라이선스 표기 확인(RISKS "M1.5 중 누적"), windows-il2cpp 모듈 추가.
- 스킬 호출 규칙: ADR-022. 안개 없는 작은 타이틀은 지도 생략(ADR-025).
- M2-2 시작 전 알아둘 것: 구역 D에는 아직 훈련용 더미가 수렴 지점에 서 있고 플레이어는 첫 엘리베이터 위치에서 바로 시작한다(임시). 건물 지붕·서브존·열쇠·출구·칩 보관함 위치 없음. 구역 D는 사람이 직접 걸어 보지 않았다(크기감·엄폐 밀도는 자동 규칙으로만 확인). `ZoneInfo.Current`는 구역 하나만 로드된 경우 전제. 그 밖에 이전부터 이어지는 것: (1) 사람이 키보드·마우스로 직접 플레이한 확인은 아직 없음(자동 테스트는 입력 주입 방식) (2) 플레이어 빌드에서 66ms 끊김 1회 관측, 원인 미조사 (3) 샷건은 산탄마다 이펙트 RPC를 보냄(9회/발) — 봇이 늘면 묶어 보내기 (4) 소지품 SyncVar가 슬롯·탄종별로 흩어져 있어 탄종/슬롯 추가 시 여러 파일 수정 필요 (5) 무기·효과음 매핑이 코드에 있음, 사운드 풀링·믹서 없음(ADR-038) (6) 무기 파지는 조준 자세 기준 오프셋뿐이라 달리기·피격 자세에서는 손과 어긋남 (7) 효과음은 귀로 확인하지 않음(재생 호출만 테스트) (8) SSAO가 프레임 비용의 절반 — 구역 D에서 빠듯하면 먼저 끈다.

## 환경 (SETUP 완료 후 채움)
| 항목 | 값 |
|---|---|
| Unity 버전 | 6000.3.28f1 (ADR-021) |
| 설치 모듈 | mac-il2cpp, windows-mono (windows-il2cpp는 사람이 나중에 추가) |
| 설치된 스킬 | mattpocock-skills 1.3.1 (플러그인, user scope): grilling, wayfinder, to-spec, to-tickets, implement, tdd, code-review 등 (호출 규칙은 ADR-022) |
| 이슈 트래커 | GitHub Issues (junans0boi/sublevel, private), 라벨 생성 완료 |
| mac 빌드 | 성공 (IL2CPP, 앱 222MB) |
| win 빌드 | 성공 (Mono, 123MB) |
| 테스트 | EditMode 72/72, PlayMode 37/37 (M1.6a-fix 종료 시점) |

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

| 2026-11-13 | M1.6 | M1.6a-fix 기울이기·점프·조준 표시 (#43~#48) | 완료(사람 확인 대기) | PT-3 반영: 기울이기 축소(0.25m·10°, TPP 카메라 어깨 고정), 점프 포즈와 착지 소리, 상태별 조준선, 조준·FPP에서 무기 표시, 다리 재생 속도 제한. 자체 검수(빌드 화면 저장)에서 옆 이동 때 몸 전체가 옆을 보고 이동 중 조준 시 총이 25° 틀어지는 문제를 찾아 다리 돌림 방식으로 수정(ADR-049). 새 외부 에셋 없음.<br>EditMode 72/72, PlayMode 37/37, mac·win 빌드 성공. 60Hz 평균 16.67·p99 17.7~17.8ms(누락 0, 1080p 창), 제한 해제 평균 2.1~2.2ms, 렌더 p99 7.7~8.8ms(구역 D, 식힌 뒤 5회 — 뜨거울 때는 10ms 초과 관측).<br>code-review 2축 반영(지면 탐지를 구 캐스트로, 공중 피격 시 착지 누락, 사망 시 무기 표시, 실행 순서, 벽 기울이기 테스트가 제한을 실제로 검증하도록). |
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

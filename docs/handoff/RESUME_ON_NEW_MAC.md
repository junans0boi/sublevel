# 다른 맥북에서 이어가기 (2026-11-14 기준)

## 1. 환경 (새 맥에서 한 번)
1. `brew install git-lfs && git lfs install` (PNG 등은 LFS. 없으면 clone/commit이 실패한다)
2. Unity Hub에서 **6000.3.28f1** 설치 (모듈: Mac IL2CPP, Windows Mono). 경로 `/Applications/Unity/Hub/Editor/6000.3.28f1/` 이어야 `tools/unity.sh`가 그대로 동작한다.
3. `git clone https://github.com/junans0boi/sublevel.git` → `main`
4. 이전 맥 바탕화면의 `sublevel-nongit-backup.tgz`를 새 맥으로 옮겨 **프로젝트 루트에서** `tar -xzf` → `AssetStaging/`(깃에 안 올라가는 원본 에셋, 차량 8종 포함)이 복원된다. `memory/`는 Claude 메모리 사본(필수 아님), `shots/integ2`는 검수 스크린샷.
5. Claude Code를 프로젝트 루트에서 실행하고 첫 프롬프트로: "CLAUDE.md부터 읽고 docs/handoff/RESUME_ON_NEW_MAC.md의 '다음 할 일'을 이어서 진행해".
6. 첫 Unity 실행은 Library 재생성으로 오래 걸린다(10분 이상 가능). 이후 `ProjectSettings/{ProjectSettings,GraphicsSettings,QualitySettings}.asset` 변경은 되돌린다(`git checkout --`), EditorBuildSettings는 유지.

## 2. 저장소 상태 (main = 4f2941b 이후)
- 모든 커밋과 작업 중이던 파일까지 `main`에 푸시됨. 옛 worktree 브랜치는 전부 main에 병합된 상태(무시해도 됨).
- **Unity에서 검증된 것**: 엘리베이터 상승, 이동 디테일(자동 달리기·프리룩·천천히 걷기·파쿠르·유리), 핑, 차량 코어(임시 모델), M4 풀매치(자동 플레이) — EditMode 451/451, PlayMode 207/210(실패 2는 포트·부하 환경 문제).
- **코드만 있고 Unity 컴파일·테스트·캡처가 아직 없는 것**: 조준경 마감 #83(ADR-071), 나무·풀 #71(ADR-070), 팀 모드 #92(ADR-072), 근접 무기 #93(ADR-073), 차량 모델 교체 #89(커밋 e064997), 연료·제리캔·경적 #91(ADR-075, 미완성 WIP), 차량 피해 #90(ADR-074, `VehicleDamageRules.cs`만 있음, 미완성 WIP).
- 마지막 EditMode 결과(WIP 직전): 498개 중 2 실패 — `ItemIconTests.Get_AfterBuilderRun_LoadsEverySprite`(근접 아이콘 weapon_21~24 미생성: `tools/unity.sh run-gfx ItemIconBuilder.Build`), `SniperRifleTests.WeaponTable_IdsAreUnique...`(근접 무기 id 21~24가 추가돼 기대값 갱신 필요).
- PlayMode: `VehicleTests.Car_Driver_CannotFireTheRifle_ButFiresThePistol`이 "local player spawned" 타임아웃 — #89 이후 TestBox 재빌드/프리팹 문제 가능성(확인 필요).

## 3. 다음 할 일 (순서대로, 사용자 승인된 방향)
1. **컴파일 확인**: `tools/unity.sh run ...` 또는 `test editmode`로 컴파일 오류부터. 미완성 #90/#91 파일이 오류를 내면 완성하거나 임시로 제외.
2. **통합 한 번에**: TestBox/ZoneD/World 씬 재빌드(`VehicleBuilder`, `WorldBuilder.Build`, TestBoxBuilder 진입점은 docs/UNITY_CLI.md·각 ADR 참고), 아이콘 재생성, 맵·미니맵 재굽기(섬 윤곽이 #71에서 바뀜), 전체 테스트, 성능 예산 확인(World 렌더 p99 ≤ 10ms — 숲·풀 때문에 넘으면 풀 거리·밀도, LOD부터 줄임).
3. **차량 모델 육안 검수**: `FeelCapture -vehshots` 또는 `VEH_SHOTS=<폴더> VEH_SCENE=TestBox tools/unity.sh test playmode Game.Tests.VehicleCaptureTests`로 차 5종+오토바이 2종 외관·운전석 1/3인칭·라이더 캡처 → 사용자에게 스크린샷 전달. (사용자가 임시 모델 외관에 강하게 불만이었으므로 최우선.)
4. **#90 차량 피해**(탄흔·유리 금/깨짐·찌그러짐·파츠 이탈·연기→불→폭발→잔해·타이어 펑크)와 **#91 연료·HUD·엔진음** 완성.
5. 열려 있는 정리: #77 Long 테스트(`TEST_CATEGORY=Long tools/unity.sh test playmode`), 관전 '사망' 띠와 승리 패널 겹침, 전체 지도 왼쪽 패널 잘림, 오토바이 그립/왼발 벌어짐, 완료된 이슈(#67~#82) 닫기.
6. 후속: 지역 간 엘리베이터 #76, 인벤토리/HUD Codex 브리프(docs/handoff/CODEX_*.md).

## 4. 작업 규칙 요약 (CLAUDE.md가 우선)
- 한국어 보고(간결), 코드·커밋은 영어. 모든 작업은 GitHub 이슈로 등록. 사용자에게 묻지 말고 판단해서 ADR로 기록.
- 브랜치 없이 main에서 서브에이전트가 파일 소유권을 나눠 작업, **Unity는 한 번에 한 프로세스**.
- 에이전트는 30분 안에 보이는 결과(커밋+캡처)를 내게 하고 ~20분마다 진행을 직접 확인한다(사용자는 장시간 침묵을 매우 싫어함).
- 결과는 스크린샷으로 보여 준다. 임시 모델/미검증 항목은 보고할 때 명시한다.
- 라이선스: CC0/CC-BY만, 에셋마다 `docs/ASSET_LEDGER.md` + 폴더 `LICENSE.txt`, 실존 브랜드 금지(차량 엠블럼·번호판 텍스처는 제거/교체함), AI 생성 에셋 금지.
- 사용자 맥에 켜 둔 `Build/mac/Sublevel.app`이 7770 포트를 잡으면 포트 테스트가 실패한다.

## 5. 멈춘 작업의 정확한 지점 (2026-11-14 00:00 직전, 사용자가 에이전트를 직접 중단)
| 작업 | 멈춘 지점 | 디스크에 남은 것 |
|---|---|---|
| #89 차량 모델 교체 | 모델 7종 가져오기·프리팹·VehicleRig/VehicleFit·캡처 코드(`FeelCapture.Vehicles.cs`, `VehicleCaptureTests`)까지 끝나 e064997로 커밋됨. 캡처 PNG는 **한 장도 못 뽑음**(캡처 폴더가 비어 있었음). PlayMode `Car_Driver_CannotFire...` 실패 1건 미해결 | `Art/Vehicles/*`, `Prefabs/M1/Vehicles/*`, 에셋 장부 행 |
| #91 연료·제리캔·HUD·경적·엔진음 | `Core/Fuel.cs`, `Net/VehicleFuel.cs`, `Net/VehicleHorn.cs`, `Net/CombatantJerrycan.cs`, 제리캔 로트/인벤토리/아이콘 수정까지. **차량 HUD(Gameplay)·엔진음·테스트·ADR-075 미완** | 위 파일(미컴파일 WIP 커밋) |
| #90 차량 피해 | `Core/VehicleDamageRules.cs`만 작성. 탄흔·유리·찌그러짐·파츠 이탈·화재·폭발·타이어 **미구현**, ADR-074 미작성 | 위 파일 1개 |
| 통합(#71·#83·#92·#93) | 코드·타입체크만 통과. Unity 컴파일/테스트/캡처 **전부 미실행**. 근접 아이콘 생성과 일부 테스트 기대값 갱신 필요 | main에 커밋됨 |

## 6. 이슈 현황 (junans0boi/sublevel)
- 열림: #89(모델) #90(피해) #91(연료·HUD) #92(팀) #93(근접) #77(M4 롱테스트) #71(나무·풀) #83(조준경) #76(지역 간 엘리베이터), #67~#82 중 구현 끝났지만 아직 안 닫힌 것들, 지도 #55, 라이선스 검토 #2(사람).
- 닫음: #84 #85 #86 #87 (통합 검증 후).
- ADR 번호: 069 차량 구현 가정, 070 식생, 071 조준경, 072 팀, 073 근접, 074 피해(예약), 075 연료(예약).

## 7. 2026-11-14 03:15 세션 종료 시점 (새 맥 첫 세션)
- 완료: 통합(#71 #83 #92 #93), #91 연료·HUD·엔진음, #94 #95 #101 UI, #77 롱테스트, 차량 좌석 실측(ADR-077)·손/자세/카메라(ADR-078), #90 피해 1·2차(ADR-074), #76 엘리베이터(ADR-076·079·080).
- 남은 일: DirtBike 1인칭 정면에서 핸들바 안 보임(눈 높이, VehicleModels.cs) / #90 폭발 섬광 고리 모양·불 밑단 초록 기·찌그러짐 가시성 확인·오토바이 탄흔·떠 있는 흰 구체/흰 판 / PlayMode 실패 1건 `VehiclePosePlayTests.SeatedOnATiltedMotorcycle_TheEyeStaysAtTheSeatMarker`(기울기 15.7° < 20°) / #76 봇이 승강장 6.6m 앞에서 멈춤 원인, 원격 2인 테스트(접속 경로 필요) / #88 미착수 / 사람 확인: 엔진음·엘리베이터 소리 청취, GoblinV12 엠블럼·번호판 라이선스.
- 작업 방식: 전수 점검(사진 대조표 + 코드) → 한 번에 수정 → 캡처 1회로 메인이 직접 엄격 판정. 복사본 ../sublevel-b, -c, -d 는 병렬용(삭제해도 됨).

## 8. 2026-11-14 플레이 후기 세션 종료 시점 (토큰 소진으로 중단)
- 완료·push: #105 이동 중 UI·앉기, #106 3D 사운드(ADR-086), #107 #110 #113 인벤토리(ADR-081·082), #108 파쿠르 밖→안(ADR-088), #109 #112 #88 오토바이 기울기·로드킬(ADR-084·085), #111 양손 로우레디(ADR-083), #114 바닥 아이템 모델, G5 줍기 목록 #116(ADR-093), PUBG 차이 분석 `docs/PUBG_GAP_V2.md`.
- **중단된 작업 (미커밋, 테스트 미완 — 각 복사본 작업 트리에만 있음. 이어서 하려면 해당 복사본에서 diff 검토 → 테스트 → 커밋)**:
  - `../sublevel-b`: G1 총기 손맛·TTK(ADR-089·090, Aiming/Fire/WeaponDef/Loadout/DamageRules/weapons.json) + G2 방어구·가방(ADR-091, Armor/Carry/CombatantGear/loot.json/LootMeshes/ItemIcons) 섞여 있음.
  - `../sublevel-c`: #121 조준 견착·앉아걷기 발 뜸 + G3 이동 수치(ADR-095·096, Locomotion/Stance/CombatantAnimation*) + G4 소리 단서(ADR-092, CombatantAudio/SpatialAudio/Audio·ASSET_LEDGER) 섞여 있음.
  - `../sublevel-d`: #115 모서리 매달리기(ADR-087, 레지 테스트 통과·캡처 전) + G6 데스캠·관전(ADR-094, SpectateCamera/ResultScreen/MatchState) 섞여 있음.
- 다음 순서: 위 WIP 마무리 → P1(G7 부착물, G8 회복·부스트, G9 투척물, G10 탄도·영점, G11 문, G12 수영, G13 기절·업기, G14 설정) → 리뷰 빌드.
- 병렬 규칙: scratchpad의 agent_rules.md 내용 = 복사본별 Unity 잠금, 소유 파일만 수정, `git commit -- <paths>`, rebase --autostash.

## 9. 2026-11-14 자율 진행 지시
- 사용자 지시: 싱글 티켓(스펙 #124, 지도 #125, 티켓 #129~#144, 발소리 버그) 완료 → 추가할 콘텐츠 판단 → 문제 없으면 **묻지 말고 멀티플레이를 스스로 grilling → to-spec → wayfinder → to-tickets 로 이슈 등록 후 구현** → 마지막에 만든 것과 스크린샷 보고.
- 멀티 후보(스펙 #124 범위 밖에 적어 둠): 음성 채팅, 재접속, 지연 보정, 부정행위 방지, 파티·로비, 기절·업기(G13), 팀 핑(G15), 감정 표현(G19).

## 10. 2026-11-15 세션 종료 시점 (main 9e9c472 기준, ADR 최대 145)
- **완료·push (이번 라운드 포함)**: 싱글 PUBG 격차 G1~G20(G17 무기 종류 제외, `PUBG_GAP_V2.md` 갱신), 멀티플레이 #158~#172(접속·로비·예측·지연 보상·복제 감사 A~C·재접속·관심 영역·전용 서버·팀 기능·서버 검증·완주 테스트, ADR-121~143), 인벤토리 전면 개편 #179(ADR-140), 이번 라운드: #182 오토바이 날아감(d10e594), #155 차량 폭발 섬광·떠 있는 decal·헬멧(4ac4031), #180 #181 주차 차량 스로틀·봇 겹침(9e9c472), #183 배그식 투척 조작·#185 인벤토리 사용(ADR-144, 3356e4a), #184 #147 #146 수영 자세·바다 수영대(ADR-145, 3f21256), #186 #187 총기 감사(정조준 30/30 회귀 테스트 2690ec5).
- **열린 이슈**: #2(Fish-Net 라이선스, 사람), #55(지도 — 하위 #90 #99가 남아 있어 유지), #90(차량 찌그러짐 가시성·폭발음 확인), #99(Goblin 1인칭 실내 어두움), #173(업기 안내 문구 키 연동·춤 포즈·핑 라벨 등), #188(결과 화면 배그식 개편), #189(줍기 목록 캡처 검증). 오래된 스펙/지도 #49~#59 #76 #89 #91 #124 #125 #143 #156 #157은 증거와 함께 닫음.
- **사람이 할 일 체크리스트**:
  1. 소리 청취: 합성 엔진음·경적·문소리·발소리 재질·총성 2층·소음기·폭발음을 귀로 확인(합성음 품질은 자동 테스트로 판정 불가).
  2. **실제 PC 2대 이상 멀티플레이 테스트**: 호스트 방 만들기 → 다른 PC가 IP:포트 참가 → 한 매치 완주, 지연·끊김·재접속, 전용 서버 빌드(`docs/DEDICATED_SERVER.md`). 지금까지는 한 프로세스 안 localhost 100 ms / 2 % 손실 주입으로만 검증.
  3. **Fish-Net 라이선스 검토(#2)**, 무기 이름·형상 상표 검토 등 `HUMAN_TODO.md` 법적 항목, 릴리스 전 `-feelcapture` 등 개발용 인자 제거(RISKS).
  4. 직접 플레이로 손맛(조준·반동·수영·오토바이)을 PUBG와 비교해 피드백.
- **Unity·잠금 규칙**: Unity는 항상 복사본 루트에서 잠금 래퍼(`scratchpad/ulock.sh`, 직접 `tools/unity.sh` 금지)로 실행하고 한 번에 하나씩 순차 실행. PlayMode/네트워크 테스트는 포트 7770을 복사본끼리 공유하므로 `mkdir /tmp/sublevel-playmode.lock`을 잡고 실행 후 `rmdir`. 배치 모드에서 `WaitForEndOfFrame`은 돌아오지 않으니 쓰지 않는다. 실행 뒤 `ProjectSettings/{ProjectSettings,GraphicsSettings,QualitySettings}.asset`과 Unity가 다시 저장한 `.mat`은 되돌리고 `Assets/InitTestScene*.unity`는 지운다. 커밋은 `git add <명시 경로>` + `git commit -- <경로>`(`-A` 금지), push는 `git pull --rebase --autostash origin main && git push origin main`, force push 금지, DECISIONS.md 충돌은 두 행 모두 유지.

## 11. 2026-11-16 세션 종료 시점 (main a314742 기준, ADR 최대 164)
**이번 라운드에 끝난 것** (자세한 건 `git log cc800ca..HEAD`와 각 ADR): 오토바이 날아감(#182), 차량 폭발·헬멧(#155), 주차 차량(#180)·엘리베이터 봇(#181), 스포츠카 탑승(#215), 배그식 투척(#183, ADR-144), 인벤토리 사용(#185), 수영·해안 물(#184 #147 #146, ADR-145), 결과 화면(#188), 문 부수기(#214, ADR-147), 실내 가구·조명·밀도(#190 #191 #239), 건물 외관(#204), 지면·식생·환경음(#192-#194), 차량 조명·타이어·엔진음(#202 #203), 발자국·비·야생동물·밤(#205-#208, ADR-163), 캐릭터: 권총/소총 자세, 장전·꺼내기·집어넣기, 1인칭 팔·손, 투척·치료·줍기·피격 동작.
**검증 상태:** EditMode 971/971. PlayMode 470 중 3건 실패였던 것을 a314742에서 수정(AimAndLeanTests 12번째 `StartingWhileAnotherProcessHoldsThePort`는 게임이 포트 7770을 잡고 있으면 실패 — 게임을 끄고 재실행). 최신 빌드(00:49, a47c6b5)에는 a314742의 "엘리베이터 승강 중 수영 판정" 수정이 없음 → 새 빌드 필요.
**열린 이슈 핵심:** #205(풀 눌림 눈 확인)·#206(앉은 새), #226·#219, #227 웅크려 걷기, #223 좌우/후진 다리, #234 제자리 회전, #228 달리기 점프, #231 숨쉬기, #230 캐릭터 모델 교체, #195 캐릭터 1종, #196 사망 연출, #209-#213 상호작용 물체/HUD, #240 World_Meshes.asset 42MB(직렬화 방식 결정 필요), #90, #99, #173, #2(사람).

### 다음 세션의 첫 일: 모델·애니메이션·에셋 보강 (준환 지시, 2026-11-16)
준환의 불만: 전체가 로우폴리처럼 보이고 캐릭터 애니메이션이 어색함. Mixamo는 쓴 적 없음(캐릭터+애니 = Quaternius Swat.fbx CC0, ASSET_LEDGER C-0001, 나머지는 코드 포즈).
**준환이 새 세션의 웹 브라우저에서 Mixamo와 Unity Asset Store에 직접 로그인한다.** 에이전트는 다음을 한다.
1. 브라우저 도구(Claude in Chrome 또는 내장 브라우저, 스킬 `anthropic-skills:chrome-browser`/`built-in-browser` 먼저 읽기)로 로그인된 상태를 **확인만** 한다. 비밀번호·토큰을 묻거나 저장하지 않는다.
2. **배포 가능 여부를 약관 원문에서 확인**하고 후보마다 기록한다(출처 URL·확인 날짜·핵심 문구). 확인 항목: 상업 게임 포함 허용 / 소스 저장소 공개·재배포 허용 여부(우리 저장소 junans0boi/sublevel은 **PRIVATE**) / 모델·애니메이션 단독 재배포 금지 조항 / 크레딧 표기 / 실존 상표·총기 이름. Asset Store 에셋은 Unity 전용 EULA(CC0 아님), Mixamo는 Adobe 약관. 법적 최종 판단은 사람(CLAUDE.md 규칙 8, RISKS_OPEN_QUESTIONS 체크리스트에 올린다).
3. **후보를 찾아 준환에게 보여 주고, 준환이 고른 것만** 받는다. 후보는 실사 계열 위주: Mixamo(캐릭터 1~2종 + 무기 든 달리기·장전·앉기·엎드리기·점프·수영 등 애니메이션 세트), Asset Store 무료 실사 캐릭터·건물·소품·지면 PBR, 로그인 없이 구할 수 있는 Poly Haven/ambientCG(CC0 텍스처·HDRI·스캔), Sketchfab CC0/CC-BY. 후보표: 이름, 미리보기 URL, 가격(무료만), 라이선스 요약, 폴리 수, 리깅/리타게팅 가능 여부(현재 Swat 뼈대 vs Humanoid), 예상 작업량.
4. 준환이 승인한 항목만: (a) 계정 약관 동의/"구매(무료 획득)" 클릭은 **준환이 해당 항목을 명시 승인한 뒤에만**. 유료 결제는 하지 않는다. (b) ASSET_LEDGER.md에 출처·라이선스·날짜 기록 후 커밋(규칙 1). (c) 라이선스가 소스 공개를 막으면 저장소가 private이어도 `.gitignore`/별도 보관을 고려하고 준환에게 묻는다.
**규칙 예외 기록:** CLAUDE.md 규칙 3(계정 생성·약관 동의 금지)에 대해 준환이 2026-11-16에 "내가 로그인한다"고 허락했다. 새 계정 생성·유료 결제는 여전히 금지. 이 예외를 ADR 한 행으로 남길 것.
**병행 가능한 일(로그인 불필요):** PBR 텍스처 교체(지면·벽·도로·바위, Poly Haven/ambientCG CC0), URP 렌더 설정(그림자·SSAO·색 보정·대기), CC0 애니메이션 팩(Quaternius Universal Animation Library 등) 조사.

### 이번 세션 교훈
- 에이전트가 남기는 대기용 셸 루프(`until ... sleep`)가 몇 시간씩 살아 있음 → 세션 시작/종료 때 `ps`로 확인하고 정리.
- 한 복사본(`sublevel-d` 등)을 두 에이전트가 같이 쓰면 `Logs/`와 잠금이 겹침.
- 에이전트가 이슈 번호를 틀리게 아는 경우가 있음 → 항상 `gh issue view`로 제목 확인.
- 실행 중인 게임이 UDP 7770을 잡으면 네트워크 PlayMode가 실패.

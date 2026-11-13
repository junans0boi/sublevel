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

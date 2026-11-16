# 애니메이션 트랙 DONE (2026-11-16, 브랜치 `track/animation`)

ADR-230~234. 판정은 수치 + 연속 프레임 CSV + 테스트(ADR-191). 정지 이미지 단독 판정 없음.

## 한 것 (확인 = Unity 실행·테스트 결과)
- **로스터 연결**: 플레이어 = MixamoSwat, 봇 = 12모델 `Bot_<id>` 프리팹(`MatchDirector._botVariants`, 봇 n = n mod 12; `MatchDirector.cs`·`MatchKit.cs` 소유 밖 additive 3줄). `CharacterKit.AddTo`가 로스터 프리팹을 `Visual`로 쓴다. 본 조회는 `Animator.GetBoneTransform`(손가락·머리·배낭·헬멧·인벤토리 미리보기 포함). (ADR-230)
- **컨트롤러 4레이어**(`CharacterControllerBuilder`, 새 파일): Base(Locomotion·Air·Prone·Death) / Upper(Humanoid 마스크) / Action(재장전 3종, 무기 재장전 시간에 맞춰 속도) / Additive(숨쉬기·피격 플린치). 숨쉬기·플린치·펀치는 Humanoid 근육 곡선을 코드로 만든 임시 클립(ledger K-0001).
- **클립**: Mixamo 17종 Humanoid 재임포트(`CharacterAnimClips.Reimport`), 베이크 없음. 실측 걷기 1.15·달리기 3.05·후진 0.96 m/s. `Logs/anim_probe.csv`에 클립별 관절 높이.
- **왼손**: `LeftHandGrip`(무기 자식, 런타임 생성)이 왼 팜 목표. 어깨 조준·달리기에서 팜-그립 최대 3.0cm(기준 5cm), 총구-시선 2.2도.
- **재장전**: Action 레이어 클립, 왼손이 총에서 0.4~0.6m 떨어졌다 3cm 안으로 복귀.
- **엎드림**(#225): `ProneFiringRifle` 클립. 엉덩이 0.133·머리 0.29m·몸통 89.7도·총구 오차 0.9도(전: 총구 -50도, 판자 자세). 수영은 기존 피치 몸 유지. (ADR-231)
- **발 IK**(#235): 접지 캐스트 + 골반 내림 + 발바닥 기울임. 25도 경사 -1.3/-1.2cm, 35도 -2.7/-2.7cm, 평지 웅크림 발 0.125/0.121(눌림 없음). 평지 기준 복원 + 경사 보정을 원점 기울기로 섞은 최종 판을 재측정해 확인(2026-11-16). (ADR-232)
- **LOD/PERF**(#50인): Animator 50체(수동 Update) Base만 2.25ms·4레이어 2.43ms, 봇 44체 코드 애니메이션 0.32ms. 20m 안 전부 / 40m 안 IK 없음 / 밖 골격 컬. (ADR-233, `AnimatorPerfTests`)
- **테스트 재작성**: `CharacterAnimatorTests` 13건(레이어·마스크·Humanoid 클립·관절 높이·각도), `AnimationJointTests` 8건(팜-그립·총구·발 올림·재장전·엎드림·웅크림·대각 이동·경사 2종), `RigBones`로 본 이름 조회 제거(AimAndLean·Stance·SlotsAndProne·CombatantPresentation). EditMode 전체 **1235/1235 통과**(마지막 전체 실행, 발 IK 마지막 수정 전).
- 문서: ADR-230~234, ASSET_LEDGER K-0001, BACKLOG 3줄, bible `animation.json` + `bible_build.py`.

## 감사 이슈 상태
| 이슈 | 상태 |
|---|---|
| #225 엎드림 | 클립으로 해결(수치 위). 기어가기 팔다리·눕기 전환 클립 없음 |
| #231 대기 | RifleIdle 클립(양발 접지 0.124/0.124, 호흡 Additive). 해결 |
| #235 발 IK | 경사 해결(수치), 계단 미측정 |
| #226 3인칭 ADS/재장전 | 재장전 클립화, ADS는 기존 코드(총구 오차 2.2도) |
| #227 웅크림 | 코드 포즈 유지, 머리 1.27m·양발 접지 확인. 클립은 캡슐과 안 맞아 미채택 |
| #218 권총(소총 부분) | 왼손 LeftHandGrip 완료, 권총 세트 미완 |
| #222 스프린트 · #223 방향 이동 · #228 점프 · #234 제자리 회전 · #232 기울이기 · #233 난간 · #236 수영 · #219 근접 | **미완**: 해당 클립 없음, 기존 코드 포즈를 Humanoid에 올려 동작은 함(ADR-234). 격차 클립 목록은 BACKLOG |
| #230 #237 #238 | 범위 밖 |

## 못 한 것 / 확인 못 한 것
- 최종 판 재측정: `AnimationJointTests`·`StancePlayTests`·`AimAndLeanTests` 29/29 통과(소리 없음). 달리기 발 0.079~0.208m, 팜-그립 3.0cm.
- 방향 2D 블렌드·점프·스프린트 클립(위), 장전 중 마그 모델 소품, 계단 측정, 12모델 전부 PlayMode 캡처(Swat·UBC·Brian 프로브만).
- PlayMode 회귀(마지막 전체 묶음 146건): 143 통과. 실패 3건 — `ReplicationBodyTests` 2건은 묶음 실행에서만 실패, 단독 재실행 통과(멀티 클라이언트 하네스 타이밍). `StancePlayTests.Jumping_GetsUpFromACrouch…`는 가속 중 0.4s 시점 속도를 재는 타이밍 의존 테스트(4.0~6.2 변동, 단독 2회 통과). 애니 코드와 무관하다는 증거는 단독 통과뿐(추정).
- 오디오: 모든 PlayMode는 `ProjectSettings/AudioManager.m_DisableAudio=1`로 돌린 뒤 `git restore`(래퍼 스크립트). 소리 나는 테스트는 돌리지 않았다.
- 소유 밖 변경: `MatchDirector.cs`(+2줄), `MatchKit.cs`, `TestBoxBuilder.cs`(봇 변종·`_proneHeadAhead`), `CharacterKit.cs` 재작성, `BackpackVisual/ArmorVisual/InventoryPreview`(Humanoid 본 조회), `Core/Stance.cs`(`ProneArmCounterDeg` 삭제), `StanceTests`·`StancePlayTests`·`AimAndLeanTests` 등 본 조회·수치 기준 수정, 재생성물 `Bot*.prefab`·`Player.prefab`·`TestBox.unity`·`DefaultPrefabObjects.asset`(병합 시 master가 `TestBoxBuilder.Build`로 재생성).
- `MixamoBake`·`Baked/Mx_*.anim` 삭제는 준환 확인 대기(지금은 안 쓰이고 컴파일만 됨).
- 머지 뒤: `CharacterImport.SyncStaging` → `CharacterAnimClips.Reimport` → `TestBoxBuilder.Build`.

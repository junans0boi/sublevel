# 애니메이션

> 상태: 확정 · 요약: Swat(Generic)+Mixamo 구운 클립에서 Humanoid로 교체하는 계획: 레이어 구조, 클립 있음/없음, 엎드림 85/40·왼팔 늘어짐·소총 크기의 원인, IK, 다시 쓸 테스트.

## 현재 상태와 교체 방향

지금 캐릭터는 Quaternius Swat(Generic 리그)이다. Swat은 발이 정강이의 자식이 아니고 다리가 Hips가 아닌 Body 아래라 Unity Humanoid 리타겟이 안 된다. 그래서 Mixamo 소총 클립을 오프라인으로 Swat에 구워(MixamoBake) 일반 클립 17종을 만들었다(ADR-166). 구운 결과에 결함이 있고 코드 포즈가 많아, 준환 결정으로 캐릭터와 애니메이션을 모두 Humanoid로 교체한다(MASTER_PLAN §4).

- 제안 캐릭터: Mixamo 사실풍 8~12종(봇·상대) + 이미 저장소에 있는 Quaternius UBC Superhero 남·여 2종(플레이어 보조 스킨). 폴백은 UBC 단독(CC0). Mixamo 다운로드·약관 읽기는 준환(규칙 3·8).
- Humanoid가 되면 Mixamo/UAL 클립을 런타임 리타겟으로 직접 쓰고 MixamoBake와 Baked/Mx_*.anim 17개는 폐기 대상(삭제는 준환 확인 후).
- Animation Rigging 패키지는 추가하지 않고 OnAnimatorIK를 쓰는 것을 권장(의존성 추가는 먼저 묻는다).
- 실제 캡처로 현재 트리를 다시 확인하지는 않았다. 아래 결함 원인은 04 감사의 코드 읽기 결과다.

## 레이어 구조 (현재 vs 계획)

| 상태 | 레이어 | 현재 | 계획(Humanoid) | 근거 |
|---|---|---|---|---|
| 확정 | Base | Locomotion 2D 블렌드(Idle/Walk/Run/Run_Back 등) + Death 상태, 파라미터 Move, Pace, AirPhase. 기어가기 위상은 코드 추가(Gait.cs). | 이동 2D 블렌드(전/후/좌/우/대각) + Stance 파라미터(서기·웅크림·엎드림) + 사망. | Assets/_Project/Prefabs/M1_5/Character.controller:88-136; docs/research/04_animation_audit.md:78-80 |
| 확정 | Upper | Lowered/Aim/Shoot/Hit/Air + 펀치 상태. 소총류는 이 레이어를 꺼 두고(CombatantAnimation.cs:289) 코드로 오른손 Reach + 왼손 Grip 2본 IK(PoseRifle)를 매 프레임 건다. | 조준·사격 레이어(소총 마스크 = Humanoid 상체 AvatarMask). | Assets/_Project/Prefabs/M1_5/Character.controller:148-166; docs/research/04_animation_audit.md:22-25 |
| 제안 | Action | 없음(장전·꺼내기·투척·치료는 코드 포즈 ReloadMotion 등) | 장전·꺼내기·집어넣기·치료·투척(트리거, 위 몸 마스크). 손 동작을 클립이 담당. | docs/research/04_animation_audit.md:93-94 |
| 제안 | Additive | 없음 | 숨쉬기·피격·반동 가산. | docs/research/04_animation_audit.md:93-94 |
| 제안 | 시선(위아래) | RifleHold.FollowedPitch/TorsoPitchShare 수식으로 Swat 척추에 피치를 나눔. | 같은 수식을 유지하고 Humanoid Spine/Chest/Neck(GetBoneTransform)에 분배. LateUpdate. | docs/research/04_animation_audit.md:96; docs/research/MASTER_PLAN.md:151 |

## 클립 인벤토리

| 상태 | 클립 | 있음/없음 | 메모 | 근거 |
|---|---|---|---|---|
| 확정 | 서기 대기·조준 대기·달리기·걷기·후진 | 있음 | RifleIdle, Swat_RifleAimingIdle_skin, RifleRun, RifleWalk, RifleWalkBack (Mixamo FBX 16개, 현재 모두 Generic 임포트: meta의 animationType: 2). | docs/research/04_animation_audit.md:98-101 |
| 확정 | 옆걸음 | 부분 | RifleSideStep 한 방향. 반대 방향은 미러, 대각은 블렌드(제안). 지금은 다리를 90도 돌리는 코드 흉내(ADR-049). | docs/research/04_animation_audit.md:101 |
| 확정 | 사격 서기 / 장전(서기·웅크림·엎드림) | 있음 | FiringRifleStand, ReloadingRifleStand, ReloadRifleCrouch, ProneReloadingRifle | docs/research/04_animation_audit.md:102-103 |
| 확정 | 웅크림 이동·대기 | 있음 | RifleCrouchWalk, RifleCrouchWalkBack, RifleKneelIdle. 웅크림 옆걸음 없음. | docs/research/04_animation_audit.md:104 |
| 확정 | 엎드림 사격/장전 | 있음(미연결) | ProneFiringRifle, ProneReloadingRifle를 구웠지만 어느 상태에도 연결 안 됨. 엎드려 기기·눕기·일어나기 전환은 없음. | docs/research/04_animation_audit.md:105 |
| 확정 | 꺼내기/집어넣기 | 있음 | RiflePullOut, RiflePutAway | docs/research/04_animation_audit.md:106 |
| 확정 | 사망 | 1종 | RifleDeath(Mx_RifleDeath로 교체됨, ADR-167). 방향별 사망은 카탈로그 확인. | docs/research/04_animation_audit.md:107; docs/DECISIONS.md:177 |
| 제안 | 없는 것(격차) | 없음 | 스프린트(총 내림), 점프·착지, 제자리 회전(#234), 피격, 근접, 투척, 치료·먹기, 권총 세트, 수영, 난간·파쿠르, 차량 앉기, 에모트(#173). 출처 후보: Mixamo / Quaternius UAL(UAL1·UAL2 Humanoid로 이미 임포트, 권총 클립 포함 여부는 확인 필요). | docs/research/04_animation_audit.md:108-120 |
| 확정 | 기존 Swat 클립 | 있음 | Idle/Walk/Run/Run_Back, HitRecieve, Punch_Left/Right, Death. 교체 후에는 Quaternius 쪽을 폐기. | docs/research/04_animation_audit.md:74; Assets/_Project/Prefabs/M1_5/Character.controller:10-360 |
| 확정 | 구운 클립 | 17개 | Mx_FiringRifleStand, ProneFiringRifle, ProneReloadingRifle, ReloadRifleCrouch, ReloadingRifleStand, RifleAimingIdle, RifleCrouchWalk(+Back), RifleDeath, RifleIdle, RifleKneelIdle, RiflePullOut, RiflePutAway, RifleRun, RifleSideStep, RifleWalk, RifleWalkBack. 폐기 예정. | Assets/_Project/Prefabs/M1_5/Baked/ |

## 알려진 결함과 원인

| 상태 | 현상 | 원인 | 해결 방향 | 근거 |
|---|---|---|---|---|
| 확정 | 엎드리면 몸이 약 35도로 떠 있음(85도 눕힘 + 40도 머리 들기) | 모델 전체를 ProneModelPitchDeg 85도 앞으로 눕히고(몸통 선이 지면 위 5도), 그 위에 Turn(Torso, -ProneHeadLiftDeg 40)으로 가슴·머리를 다시 40도 세운다. 눈 높이 0.4 m(EyeDropMeters 1.2)에 머리를 맞추는 설계값이라 의도된 값이다. 팔은 armUp으로 45도 보정, 다리는 Swat 걷기 클립 그대로라 무릎이 굽어 발이 뜬다. | 클립 기반 엎드림(Humanoid)으로 교체, 85/40/45/0.15 코드와 armUp 삭제. 임시로 40→10도는 하지 않는다(테스트가 고정, 교체 확정). | docs/research/04_animation_audit.md:13-24; Assets/_Project/Scripts/Gameplay/CombatantAnimation.cs:250,476; Assets/_Project/Scripts/Core/Stance.cs:54-55 |
| 확정 | 구운 엎드림 클립을 샘플하면 캐릭터가 서 있음, RifleRun 8위상의 다리가 모두 같음(확신도 중간, 시트 한 장) | MixamoBake.Bake가 Hips 회전을 Hips 뼈에만 쓰고, Swat 다리는 Hips가 아닌 Body 아래라 눕는 회전이 다리에 전달되지 않는다. | Humanoid로 가면 사라진다(MixamoBake 폐기). | docs/research/04_animation_audit.md:19-20 |
| 확정 | 소총 스프린트·장전 때 왼팔이 늘어짐(젤리 팔) | 스프린트에서 SprintSupportRelease 0.9가 왼손 IK 가중치를 0.1로 낮춰 IK를 포기하면 Swat Run 클립의 팔이 그대로 남는다. 장전은 탄창 위치가 총에서 아래 17~30 cm(허리춤)라 팔이 아래로 내려간 모양이 된다(ReloadMotion.cs:34). 코드에 0.5 값을 쓰는 다른 프로파일도 있어 실제 사용 값 재확인 필요. | 스프린트 전용 로우레디 클립(왼손이 총을 잡음) + OnAnimatorIK 왼손 IK 가중치 유지, 장전은 클립이 손 동작을 가지고 IK는 해제. | docs/research/04_animation_audit.md:26-33; Assets/_Project/Scripts/Gameplay/CombatantAnimation.Rifle.cs:286; Assets/_Project/Scripts/Core/Locomotion.cs:99,112 |
| 확정 | 소총(Sentinel)이 거대함 | 저격 모델이 없어 AssaultRifle_1을 1.05 m(키 1.8 m의 58%)로 늘려 씀. 실물 AK류 0.87 m(48%). 권총 0.24 m(실물 0.19 m +25%). 머리 0.30 m가 실제 0.23 m 수준이라 몸통·팔이 짧아 보임(스타일화 5.5등신). | 무기 길이를 실물 비례로(AK 0.87, 권총 0.19), 무기 모델 교체 트랙과 합의. 키 대비 소총 ≤ 50%를 캡처 판정 기준으로. | docs/research/04_animation_audit.md:35-47; Assets/_Project/Editor/CharacterKit.cs:29-34 |
| 확정 | 엎드림 + 소총: 클립과 코드 회전이 겹침 | 엎드림이면 _rifleWanted=false(Rifle.cs:131)라 Upper 레이어의 Mx_RifleAimingIdle이 눕힌 모델 위에 얹히고 armUp 코드 회전이 한 번 더 쌓인다. | 클립 하나로 정리. | docs/research/04_animation_audit.md:33 |
| 확정 | UBC·UAL이 이미 Humanoid로 설정돼 있다는 01 문서의 설명 | meta에는 animationType: 2가 적혀 있다. Unity의 ModelImporter 열거에서 2는 Generic(Humanoid는 3)이다(Unity 열거 값 기억에 의존한 판단, 에디터 확인 필요). Mixamo FBX와 UAL 모두 같은 값이라 지금은 전부 Generic일 가능성이 높다. | Humanoid 전환 시 임포트 규칙에서 animationType을 Humanoid로 바꾸고 Avatar isValid를 EditMode 테스트로 확인(01 문서 I2). | Assets/_Project/Art/Characters/Mixamo/RifleRun.fbx.meta:102; Assets/_Project/Art/Characters/QuaterniusUniversal/UAL1/UAL1_Standard.fbx.meta:102; docs/research/01_character.md:14 |
| 제안 | 스프린트 전용 클립 없음 | RifleRun 클립에 코드 틸트로 대용. | Mixamo 소총 스프린트 변형 확인. | docs/research/04_animation_audit.md:116 |

## IK 계획

| 상태 | 항목 | 내용 | 근거 |
|---|---|---|---|
| 제안 | 왼손 파지 | OnAnimatorIK에서 AvatarIKGoal.LeftHand를 총의 LeftHandGrip 소켓에 SetIKPositionWeight. 스프린트에서도 가중치 유지(클립이 손을 총에 둠). 장전 중에는 해제. | docs/research/04_animation_audit.md:92-93; docs/research/01_character.md:64-65 |
| 제안 | 무기 소켓 | HandBone 'Wrist.R' 문자열 → animator.GetBoneTransform(HumanBodyBones.RightHand) 자식 소켓 + 무기별 LeftHandGrip/RightHandGrip(WeaponVisual.Fit 재계산). MeleeKit.AttachToHand도 같은 소켓. | docs/research/04_animation_audit.md:58; docs/research/01_character.md:8-9 |
| 제안 | 발 IK | Humanoid SetIKPosition(LeftFoot/RightFoot) + 접지 레이캐스트(#235). FootContact 이벤트는 유지(발소리 FootDown이 의존). | docs/research/04_animation_audit.md:94; docs/research/MASTER_PLAN.md:151 |
| 제안 | 이동 방향 | 8방향 Mixamo 클립이 있으면 2D 프리폼 블렌드로 #223 해결, 없으면 _legs.TurnDeg(다리 yaw) 유지. | docs/research/04_animation_audit.md:96 |
| 제안 | 차량 좌석 | 좌석 오프셋·손/발 IK는 모델 비례가 달라 재측정(ADR-077/078), 앉은 Humanoid 포즈 + 핸들 손 IK. | docs/research/04_animation_audit.md:55 |
| 확정 | 피격 판정과의 관계 | 높이 기반이라 뼈 의존 없음. 새 모델 키 1.8 m 맞추고 HeadMeters 0.3 → 0.23~0.25 재조정. | docs/research/01_character.md:9; Assets/_Project/Scripts/Core/HitZones.cs |

## 교체 시 영향받는 코드

| 상태 | 위치 | 해야 할 일 | 근거 |
|---|---|---|---|
| 확정 | Editor/CharacterKit.cs | 전면 재작성: Humanoid Avatar, GetBoneTransform, AvatarMask, 컨트롤러 생성, 무기 소켓. | docs/research/04_animation_audit.md:50-53 |
| 확정 | CombatantAnimation.cs 외 .Rifle/.Gait/.Items/.Melee/.Swim/.Ledge/.Emote/.Vehicle | 본 슬롯을 Humanoid 본으로 교체. 눕기·웅크림·난간을 클립으로 대체하면 해당 코드 삭제. 얇은 코드 레이어(에임 오프셋·립 롤·발 보정)만 유지. 공개 속성 PronePose, CrouchPose, RiflePose, UpperState, BaseState, FootLow 유지로 PlayMode 수정 최소화. | docs/research/04_animation_audit.md:54 |
| 확정 | Editor/MixamoBake.cs + Baked/Mx_*.anim | 폐기(삭제는 준환 확인 후). | docs/research/04_animation_audit.md:51 |
| 확정 | BackpackVisual, InventoryPreview.cs:167, WeaponVisual, FirstPersonArms, 프리팹 Player/Bot/TrainingDummy | 마운트 본·무기 손 소켓 교체, 프리팹은 TestBoxBuilder.Build로 재생성(차량 프리팹은 git restore). 1인칭 팔은 변경 없음(후속으로 실제 팔 여부는 결정). | docs/research/04_animation_audit.md:56-62 |
| 제안 | AssetImportRules.cs | 새 FBX 임포트 규칙(Humanoid, 루트 모션 off, 루프 플래그)과 LICENSE/ledger 확인 추가. | docs/research/04_animation_audit.md:128 |

## 다시 써야 할 테스트 (맹목적 수정 금지)

| 상태 | 테스트 | 고정하고 있는 것 | 처리 | 근거 |
|---|---|---|---|---|
| 확정 | StanceTests, SlotDataTests (EditMode) | 엎드림 85/40/45/0.15, ProneModelBackShift 0.45 m, 웅크림 무릎 86도·0.2427 m (SlotDataTests는 rules.json의 85/40) | 코드를 지우는 시점에 함께 삭제/이관. | docs/research/04_animation_audit.md:127-129 |
| 확정 | RifleHoldTests, ReloadMotionTests | 코드 포즈 수식 | 코드 포즈를 쓰는 동안만 의미. 클립으로 바뀌면 ReloadMotion과 함께 삭제. | docs/research/04_animation_audit.md:130-131 |
| 확정 | CharacterGaitTests, VehiclePoseTests | 다리 회전·보행 속도 순수 수학, 좌석 수식 | 유지(모델 무관 수학이면). | docs/research/04_animation_audit.md:130 |
| 확정 | CharacterAnimatorTests | Swat 클립 이름과 컨트롤러 구조 | CharacterKit 재작성 때 새 구조로 같은 의도를 다시 표현. | docs/research/04_animation_audit.md:132 |
| 확정 | SlotsAndProneTests:481-511, AimAndLeanTests, StancePlayTests:97, CombatantPresentationTests:83 | 모델 피치 == 85도, 엉덩이 높이 < 0.45 m, 총이 수평, 본 이름 Hips/Wrist.R 하드코딩 | 결과 기반 단언(엎드림 중 머리/가슴 높이 상한, 총이 시선과 평행, 양손이 총에 닿음, 발이 지면에 닿음)으로. 본 이름은 GetBoneTransform으로. | docs/research/04_animation_audit.md:135-140 |
| 확정 | CharacterCaptureTests (427장) | 사람 눈 판정용 캡처 | 새 모델로 재촬영해 엎드림·소총 달리기·장전·스프린트 판정, 총 길이 ≤ 키의 50%. | docs/research/04_animation_audit.md:141 |

## 정해야 할 것

- [x] **캐릭터 구성** (확정) → Mixamo 8~12종 + UBC 2종 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: Mixamo 사실풍 8~12종 + UBC 2종 / UBC 단독(CC0 폴백)  · 추천: Mixamo 8~12종 + UBC 2종
  - 근거: MASTER_PLAN §4-1. Mixamo는 폴리 수·텍스처·약관(Adobe Additional Terms 미확인) 확인이 선행. 준환이 시험용 FBX 1종을 받아야 한다.
- [x] **Animation Rigging 패키지** (확정) → 추가 안 함 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 추가 안 함(OnAnimatorIK) / 추가(MultiAimConstraint)  · 추천: 추가 안 함
  - 근거: 의존성 추가는 먼저 묻는다(전역 규칙). MASTER_PLAN §9-7 기본값도 '추가 안 함'.
- [x] **1인칭 팔** (확정) → 지금 프리미티브 유지 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 지금 프리미티브 유지 / 새 캐릭터 팔 메시 사용  · 추천: 지금 프리미티브 유지
  - 근거: 변경 범위를 줄이기 위해(제안). MASTER_PLAN §4-8 미정.
- [x] **사망 래그돌** (확정) → 클립만 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 클립만 / 클립 + 선택적 래그돌  · 추천: 클립만
  - 근거: 래그돌 코드가 없다. CharacterJoint 에디터 스크립트가 필요해 범위가 커진다(제안).
- [x] **8방향 이동** (확정) → Mixamo 8방향 클립 받기 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: Mixamo 8방향 클립 받기 / 다리 yaw 회전 유지  · 추천: Mixamo 8방향 클립 받기
  - 근거: #223(후진·좌우)을 닫으려면 방향별 클립이 필요하다.
- [x] **무기 길이 재설정** (확정) → 실물 비례 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 실물 비례(AK 0.87, 권총 0.19) / 현재 유지  · 추천: 실물 비례
  - 근거: 캐릭터 교체 후 비례가 바뀌므로 무기 모델 트랙과 함께.


# 04. 애니메이션 감사: 엎드리기 / 소총 한 팔 / 소총 크기 / Humanoid 교체 영향

작성 2026-11-16. 코드와 기존 캡처만 읽었다(Unity 실행 없음, 코드·에셋 변경 없음).
증거 이미지: `Logs/charshots/*.png`(08:46 촬영), `Logs/lookdev/mx_*.png`(08:56, Mixamo 구운 클립 시트).
주의: 사용자가 가진 빌드(00:49, a47c6b5)는 Mixamo 연동 이전 코드다. 아래 (b)의 원인은 그 코드와 현재 작업 트리에서 모두 성립한다(코드 경로가 같음). 현재 트리의 새 캡처는 안 찍었다(확신도는 항목별로 표시).

## 1. 원인 분석

### (a) 엎드리면 몸이 약 35도로 떠 있음 — 파라미터 문제 + 구운 엎드리기 클립 미사용
증거(코드):
- `CombatantAnimation.cs:250` 모델 전체를 `ProneModelPitchDeg = 85` 만큼 앞으로 눕힌다(몸통 선이 지면에서 5도 위).
- `CombatantAnimation.cs:476` 그 위에 `Turn(Torso, -ProneHeadLiftDeg(40) * _prone, right)`로 가슴과 머리를 40도 다시 세운다. 85 - 40 이 아니라, 누운 선(5도)에서 40도를 더 들어 올리므로 가슴 선이 지면에서 약 35~45도다. 사용자가 본 "35도"와 일치한다.
- `Stance.cs:55-58` 주석이 의도를 말한다: 머리를 눈(캡슐) 위치에 두려고 머리를 들어 올림(`ProneModelBackShift 1.35`로 모델을 뒤로 밈). 눈 높이 0.4 m(`EyeDropMeters 1.2`)에 머리를 맞추는 설계라서, 머리가 뜨는 것이 설계값이다.
- 팔은 `armUp = -(85-40) * _prone * _upperWeight`(`:497`)로 45도 되돌려 총을 수평으로 맞춘다 → 가슴을 올린 만큼 팔 자세가 클립과 어긋난다.
- 다리는 베이스 레이어의 Swat Idle/Walk 클립 그대로(`Gait.cs:115-117` 기어가기 위상만 추가) → 사진처럼 무릎이 굽어 발이 떠 있다.
증거(이미지): `Logs/charshots/C_prone_idle_side.png` — 하체는 지면과 거의 평행, 가슴·머리가 올라가 스핑크스 자세, 두 번째 손은 총 밑에 매달려 있음.
추가 발견: `Mx_ProneFiringRifle.anim`, `Mx_ProneReloadingRifle.anim`은 구웠지만 어떤 상태에도 연결되지 않았다(핸드오프 §12). 게다가 `Logs/lookdev/mx_ProneFiringRifle.png`(시트)에서 이 클립을 샘플하면 캐릭터가 **서 있다**. `MixamoBake.Bake`는 Hips 회전을 Hips 뼈에만 쓰고(`tHips.rotation`), 다리는 Hips가 아니라 `Body` 아래에 있어(`MixamoBake.cs:7-9 주석`) 눕는 회전이 다리에 전달되지 않는다. 같은 시트에서 RifleRun의 8개 위상도 다리 포즈가 전부 같다(다리 구움 실패 의심. 확신도 중간: 시트 한 장으로 본 것). 그러므로 지금의 구운 클립을 엎드리기에 그냥 연결하면 안 된다 → Humanoid로 가면 이 문제는 사라진다.
분류: 파라미터(40도 머리 들기, 설계값) + 모델 문제(Swat 스켈레톤이 Mixamo 눕기를 못 받음). 임시 완화는 `ProneHeadLiftDeg` 40→10 이하와 `ProneModelBackShift` 재측정이지만 `StanceTests`/`SlotDataTests`가 85/40/45/0.15를 고정해 놓았다(테스트 목록 §4). 교체가 정해졌으니 임시 수정은 권하지 않는다.

### (b) 소총 달리기·장전 때 한 팔이 늘어짐 — 코드 포즈 설계(왼손 놓기)와 장전 손 위치가 같은 자세가 됨
확신도: 높음(코드 + `B_rifle_sprint_1_quarter.png`, `E_reload_rifle_t0040_side.png`).
- 소총류는 위 레이어(Upper)를 끄고(`CombatantAnimation.cs:289` `showUpper = armed && !_rifleWanted`) 오른손 Reach + 왼손 Grip 2본 IK를 `PoseRifle()`가 매 프레임 건다. 베이스 레이어에서 Swat Run/Walk 클립의 팔이 계속 돈다.
- 달리기(스프린트 쪽): `Rifle.cs:286` `support = _rifle * (1 - SprintSupportRelease * _sprintShown * (1 - _ready))`. `rules.json`에 RifleHold 항목이 없어 기본값 `SprintSupportRelease = 0.9`(`Locomotion.cs:99`)가 쓰인다. 스프린트(속도 5.7 m/s 이상, 걷기 4.7/스프린트 6.3, `TrackRifle`의 InverseLerp(걷기*1.05 ~ 스프린트*0.9))에서 왼손 IK 가중치가 0.1 → `Reach`의 `Vector3.Lerp(wrist.position, target, weight)`는 IK를 거의 포기하고 Swat Run 클립의 팔이 그대로 남는다. 그 클립은 팔이 몸통 옆에 살짝 굽은 채 약하게 흔들릴 뿐이라 "굽은 젤리 팔"로 보인다. 오른손은 IK로 총 위치에 고정(총은 한 손으로 세워 듦)이라 총만 서 있다. 사진: 총이 거의 수직, 왼손이 허벅지 옆.
- 장전: `ReloadMotion.cs:34` 탄창 가방 위치 `aU = wU - 0.17`(손이 총에서 아래 17~30 cm = 허리춤). `Grip(...)`이 왼손을 거기로 보내지만 그 지점이 팔을 아래로 뻗으면 닿는 곳이라 팔이 그냥 내려간 모양이 된다. 탄창은 0.3~0.7 구간에만 큐브로 보이고(`ShowMag`), 손가락·손목 동작이 없어서 장전으로 읽히지 않는다. 사진 t=0.40 초: 왼손 허리춤, 총은 오른손 하나로 위를 향함.
- "같은 자세"인 이유: 두 경우 모두 왼손이 총을 떠나 허리 높이의 Swat 팔 자연 위치로 간다. 한 번에 고칠 수 있는 원인은 아니고(둘 다 코드 포즈 설계), 클립이 없어서다.
- 새 Mixamo Upper 클립이 만든 문제는 아님: 현재 트리에서 소총은 Upper 클립을 안 쓴다. 단 `mx_RifleIdle.png`/`mx_RifleRun.png`/`mx_ReloadingRifleStand.png` 시트에서 구운 클립의 한쪽 팔이 전 위상에서 아래로 매달려 있다(왼팔 리타겟 실패 추정, 확신도 중간). 이 클립을 Upper로 쓰는 경우(엎드림·탑승·권총이 아닌 비소총 무장, `rifleWanted=false`)에도 같은 증상이 나온다.
- 엎드림 + 소총은 `_rifleWanted=false`(`Rifle.cs:131`, prone<0.5)이므로 Upper 레이어의 Mx_RifleAimingIdle이 눕힌 모델 위에 얹히고 `armUp` 코드 회전이 한 번 더 쌓인다 → 코드 회전과 클립이 겹치는 곳(a와 b 공통).

### (c) AK-47 같은 소총이 거대함 — 모델 + 길이 파라미터 + 캐릭터 비율
측정(`CharacterKit.Weapons`, 캐릭터 1.8 m):

| id | 이름 | 길이(m) | 실제 대응 | 키 대비 | 실제 대비 |
|---|---|---|---|---|---|
| 1 | VK-9 권총 | 0.24 | 글록 0.19 | 13% | +25% |
| 2 | Ordo SMG | 0.50 | MP5 0.49 | 28% | 정상 |
| 3 | Warden 불펍 | 0.75 | AUG 0.79 | 42% | 정상 |
| 4 | Breacher 샷건 | 0.95 | 1.0 | 53% | 정상 |
| 5 | Sentinel | **1.05** | AK-47 0.87 / SVD 1.23 | **58%** | AK 기준 +21% |

- "AK처럼 보이는" 것은 `AssaultRifle_1.fbx`를 id 5(Sentinel, 저격수 대용)로 1.05 m로 늘려 쓴 것이다(`CharacterKit.cs:33-34` 주석에 저격 모델이 없어 대신 쓴다고 적혀 있음). 1.8 m 인간 기준 AK는 0.87 m(48%). 길이 파라미터만 0.87로 줄여도 21% 작아진다.
- 이미지 실측(`A_idle_sniper_side.png`): 키 427 px, 소총 247 px(1.04 m) → 비율은 코드값과 일치. 머리(헬멧 포함) 72 px = 0.30 m, 실제 0.23 m 수준(1.8 m의 17%, 실제 13%). 몸통(어깨~골반) 0.53 m. 큰 머리가 1.8 m를 채워서 몸통과 팔이 상대적으로 짧고(스타일화 5.5등신), 소총이 몸에 비해 커 보인다.
- 메시 자체도 Quaternius 스타일화(두껍고 단순)라 높이/두께가 실물보다 굵다.
- 분류: 파라미터(Sentinel 1.05 m, 권총 0.24 m) + 모델(스타일화 비율, 큰 머리). `Weapon.Length`는 가장 긴 변 기준 균등 스케일(`InstantiateWeapon`)이다. 실사 캐릭터 교체 후엔 무기 모델도 실물 비례 모델로 같이 바꿔야 효과가 난다(무기 소싱은 다른 트랙).

### (d) Swat 모델의 한계(교체 이유 확인)
`MixamoBake.cs:7-9`: Swat은 발이 정강이 자식이 아니고 다리가 Hips가 아닌 `Body` 아래에 있어 Unity Humanoid 리타겟이 안 된다. 그래서 오프라인 월드 방향 리타겟을 구웠고 위 (a)(b) 같은 결함이 남았다.

## 2. Humanoid 캐릭터 교체 시 영향 목록

공통 전제: 새 캐릭터는 Mixamo 호환 Humanoid(`mixamorig:*`). 현재 Mixamo FBX 16개는 `animationType: 2`(Generic)로 임포트돼 있다(`Mixamo/RifleRun.fbx.meta:102`) → Humanoid로 다시 임포트해야 한다.

| 위치 | 의존 방식 | 해야 할 일 |
|---|---|---|
| `Editor/CharacterKit.cs` (521줄) | Swat.fbx 경로, 클립 이름(Idle/Walk/Run/Run_Back/HitRecieve/Punch_*), 본 이름(`Wrist.R`, `Torso/Chest/Abdomen`, `Foot.L`, `Hips`, JumpBoneNames), 아바타 없이 경로로 클립 바인딩, 무기 `Forward/Up`(조준 포즈 손목 기준), 컨트롤러 생성, 마스크 | 전면 재작성. Humanoid Avatar 사용, `GetBoneTransform`으로 본 조회, 마스크는 `AvatarMask` Humanoid 부위, 무기는 `RightHand` 소켓 + 무기별 그립 트랜스폼 |
| `Editor/MixamoBake.cs` (195줄) | Swat 리타겟 구움 | 폐기(Humanoid는 런타임 리타겟). `Prefabs/M1_5/Baked/Mx_*.anim` 16개 삭제 |
| `Editor/MeleeKit.cs`, `MeleeVisual/MeleeModel/MeleeMeshes` | 손목 앞 그립 오프셋 | 새 손 소켓 기준 재측정 |
| `Editor/TestBoxBuilder.cs`(`:32,:240-256`), `LookDevBuilder`, `ItemIconBuilder`, `TrainingRangeBuilder`(더미는 박스 프리미티브, 영향 작음) | `CharacterKit.AddTo`/`Weapons` 호출, `_jumpBones`/`_spine`/`_hips` 직렬화 필드 채움 | 필드 이름 유지하거나 새 필드로 교체. Player/Bot 프리팹(`Prefabs/M1/*.prefab`) 재생성(`TestBoxBuilder.Build`). 차량 프리팹이 같이 바뀌므로 `git restore -- Assets/_Project/Prefabs/M1/Vehicles` |
| `CombatantAnimation.cs` (606줄) | 본 슬롯(`Torso,Thigh,Shin,Foot,Arm`) + `_hips` + `_jumpBones` 인스펙터 필드, `_legs.TurnDeg`로 모델 yaw, 85도 눕힘, 크라우치 무릎 코드(`CrouchThighDeg/ShinDeg`), 발 접지 보정 `GroundCrouchedFeet`, `HipsYaw` | 본 슬롯은 Humanoid 본으로 채우기. 눕기/크라우치/난간을 클립으로 대체하면 해당 코드 삭제. 얇은 코드 레이어(에임 오프셋·립 롤·발 보정)만 유지 |
| `.Rifle.cs` (319줄) | `Deep(...,"Wrist/LowerArm/Finger*")`, 어깨 요 측정, Swat 손목 바인드 회전, 손가락 말기 `CloseHand`, 2본 IK `Reach/TwoBone/Grip` | `OnAnimatorIK`의 `SetIKPosition/Rotation(LeftHand)` + 총 `LeftHandGrip` 소켓으로 교체. 손가락은 Humanoid 손 포즈(클립 or 머슬) |
| `.Vehicle.cs` (472줄) | 손가락 이름(`Index2.L`...), 손·팔 IK(스티어링/핸들바), `FindHead`, 시트 포즈 `PoseSeated`(cs:537) | 본 이름 → `HumanBodyBones`. 좌석 실측(ADR-077/078)은 모델 비례가 달라 재측정 |
| `.Gait.cs`, `.Items.cs`(투척·치료·줍기), `.Melee.cs`, `.Swim.cs`, `.Ledge.cs`, `.Emote.cs` | 전부 `Turn(bone, deg, axis)` 코드 포즈, 본 인덱스 의존 | 클립이 있는 것(투척·치료·줍기·에모트·근접)은 클립으로 대체, 없는 것(난간·수영)은 `HumanBodyBones` 기준 코드 유지 |
| `Gameplay/BackpackVisual.cs` | `TorsoNames {"Torso","Chest","Abdomen"}`로 마운트 | `Spine2`/`UpperChest` 본으로 마운트, 스케일 재측정 |
| `Gameplay/InventoryPreview.cs:167` | `Deep(clone,"Wrist.R")`로 무기 손 | `RightHand` 소켓 |
| `Gameplay/WeaponVisual.cs` | `_fitByWeaponId`(조준 포즈에서 잰 회전/중심/크기), `ShowAtEye`, 무기를 손 아래 부착 | Fit 재계산(CharacterKit 쪽). 1인칭 `ShowAtEye`는 카메라 기준이라 영향 작음 |
| `Gameplay/FirstPersonArms.cs` | 프리미티브 튜브 + 자체 IK. Swat 본 미사용 | 변경 없음(색상만 새 의상에 맞춤). 원하면 후속으로 실제 팔 메시 |
| `Core/HitZones.cs` | 높이 구간만(`StandingHeight 1.8`, `HeadMeters 0.3`) | 코드 변경 없음. 새 모델 키 1.8 m로 스케일. `HeadMeters` 0.3→0.23~0.25 재조정(rules.json). 본별 히트 판정은 아직 없음 |
| 래그돌/사망 | 래그돌 코드 없음(`Rigidbody`는 차량·엘리베이터·루트뿐). 사망은 베이스 클립 `Death`(현 `Mx_RifleDeath`) | Mixamo Death 클립(Humanoid). 래그돌은 선택 사항(코드로 `CharacterJoint` 생성 에디터 스크립트 필요) |
| 차량 좌석 | 시트 오프셋과 손/발 IK가 Swat 팔다리 길이 기준(ADR-077/078) | 재측정 + 앉은 Humanoid 포즈 |
| 봇/더미 프리팹 | `Bot.prefab`, `Player.prefab`, `TrainingDummy.prefab`(수정됨 상태)은 `Visual` 자식에 같은 모델. `BotPrefabPatch.cs`는 비주얼 무관 | 프리팹 재생성으로 해결 |
| `AssetValidator` | 에셋 규칙 검사(캐릭터 전용 규칙 없음, 임포트 규칙은 `AssetImportRules`) | 새 FBX 임포트 규칙(Humanoid, 루트 모션 off)과 LICENSE/장부 확인 추가 |
| 방어구 | `CombatantGear` 데이터만(헬멧 메시 없음), Swat 헬멧은 모델에 포함 | 새 모델의 헬멧 유무에 맞춰 별도 트랙(데이터 변경 없음) |
| 라이선스 | Mixamo 약관은 사람 확인 필요(RISKS) | 새 캐릭터/클립을 `ASSET_LEDGER.md`에 기록 후 커밋 |

### 제안 아키텍처
1. Humanoid Avatar의 `Animator` 한 개. 컨트롤러는 `AnimatorOverrideController`가 아니라 직접 Mixamo 클립(Humanoid 임포트)을 쓰는 새 컨트롤러를 `CharacterKit`에서 코드로 생성. 무기별로 소총/권총 클립을 바꿔야 할 때만 Override를 쓴다. Humanoid 덕분에 UAL(Quaternius Universal)·다른 Mixamo 캐릭터 클립과 호환.
2. 레이어: Base(이동 2D 블렌드 forward/strafe, 서기/웅크리기/엎드리기 상태 전환 `Stance` 파라미터) / Upper(조준·사격, 소총 마스크) / Action(장전·꺼내기·집어넣기·치료·투척, 트리거, 위 몸 마스크) / Additive(숨쉬기·피격).
3. 조준 오프셋(위아래 시선): `LateUpdate`에서 `GetBoneTransform(Spine/Chest/UpperChest/Neck/Head)`에 피치 분배(기존 `RifleHold.FollowedPitch`·`TorsoPitchShare` 수식 재사용). Animation Rigging 패키지는 **추가하지 않는 쪽을 권장**(CLAUDE.md: 의존성 추가는 먼저 묻기). 필요하면 사용자에게 묻고 `MultiAimConstraint`.
4. 왼손 파지: `OnAnimatorIK`(레이어 IK Pass 켜기)로 `AvatarIKGoal.LeftHand`를 총의 `LeftHandGrip` 소켓에 `SetIKPositionWeight`. 스프린트에서는 가중치를 클립이 내리게 하는 것이 아니라 스프린트 전용 로우레디 클립(왼손이 총을 잡고 있는 클립)을 쓰고 IK 가중치는 유지 → (b)의 원인 제거. 장전은 클립이 손 동작을 가지고, IK는 총이 손을 따라가지 않도록 장전 중 해제.
5. 발 IK: Humanoid `SetIKPosition(LeftFoot)` + 접지 레이캐스트(현 `FootContact`·`GroundCrouchedFeet` 대체).
6. 다리 방향: 8방향 클립이면 `_legs.TurnDeg` 전체가 필요 없지만, 클립이 8방향이 없으면 현 방식(다리 yaw 회전)을 유지(리타겟된 Humanoid 모델은 Hips 아래 다리라 문제 없음). #223 후진/좌우를 개선하려면 Mixamo 방향별 클립 필요.

## 3. 배그식 애니메이션 세트와 출처

저장소에 이미 있는 것은 `Assets/_Project/Art/Characters/Mixamo/` 16개 FBX. 이름은 파일명 기준. "카탈로그 확인" 표시는 Mixamo 사이트의 이름이 정확히 맞는지 사람이 확인해야 함(내가 확인 못함).

| 필요 동작 | 출처 | 상태 |
|---|---|---|
| 서기 대기 / 조준 대기 / 달리기 / 걷기 / 후진 | `RifleIdle`, `Swat_RifleAimingIdle_skin`(조준 대기), `RifleRun`, `RifleWalk`, `RifleWalkBack` | 있음 |
| 옆걸음 | `RifleSideStep`(한 방향) | 부분. 반대 방향은 미러(Humanoid Mirror 플래그), 대각선은 블렌드 |
| 사격 서기 | `FiringRifleStand` | 있음 |
| 장전: 서기/웅크림/엎드림 | `ReloadingRifleStand`, `ReloadRifleCrouch`, `ProneReloadingRifle` | 있음 |
| 웅크림 이동/대기 | `RifleCrouchWalk`, `RifleCrouchWalkBack`, `RifleKneelIdle`(웅크림 대기로 사용) | 있음(웅크림 옆걸음 없음 → 걷기 블렌드) |
| 엎드림 사격/장전 | `ProneFiringRifle`, `ProneReloadingRifle` | 있음. 엎드려 기어가기(전진/후진)와 엎드림 대기·눕기/일어나기 전환은 없음 → Mixamo 카탈로그 확인("Crawl"류) |
| 꺼내기 / 집어넣기 | `RiflePullOut`, `RiflePutAway` | 있음 |
| 사망 | `RifleDeath` | 있음(1종). 앞/뒤/옆 방향별 사망은 카탈로그 확인 |
| 스프린트(총 내림) | 없음 | 격차. Mixamo "Rifle Run" 변형 또는 `RifleRun` 위 코드 틸트 |
| 점프/낙하/착지 | 없음 | 격차. Mixamo "Rifle Jump" 계열(카탈로그 확인). 현재 코드 tuck/Air 상태를 대체 |
| 제자리 회전 | 없음 | 격차(#234). Mixamo "Turn" 계열(카탈로그 확인) |
| 피격 반응 | Swat `HitRecieve`(구 캐릭터 클립) | 격차. Mixamo 피격 클립 필요(카탈로그 확인) |
| 근접(휘두르기/들기) | 없음 | 격차. Mixamo 근접·펀치 클립 또는 코드 유지(`.Melee.cs`) |
| 투척 | 없음 | 격차. Mixamo 투척 클립 또는 코드 유지(`.Items.cs`) |
| 치료/먹기/마시기 | 없음 | 격차. 코드 유지 또는 Mixamo(카탈로그 확인) |
| 권총 대기/이동/장전 | 없음(소총 세트만) | 격차. Quaternius UAL(`Art/Characters/QuaterniusUniversal`, CC0, 권총 클립 포함 여부는 확인 필요) 또는 Mixamo 권총 세트 |
| 수영 | 없음 | 격차. Mixamo 수영 클립(카탈로그 확인). 안 되면 `.Swim.cs` 코드 유지 |
| 난간 오르기/파쿠르 | 없음 | 격차. Mixamo 클라이밍 계열(카탈로그 확인) 또는 코드 유지 |
| 차량 탑승(운전/동승/오토바이) | 없음 | 격차. Mixamo 앉기 클립 + 손 IK로 핸들·핸들바 |
| 에모트 | 없음 | 격차(#173). 클립 추가 |

이동 방향: 현재 좌우/후진은 다리 90도 회전(`_legs.TurnDeg`, `MaxLegTurnDeg 60`)으로 흉내(ADR-049). Humanoid에서도 이 방식을 유지할 수 있으나 8방향 클립(전/후/좌/우/대각) Mixamo 세트를 받으면 2D 프리폼 블렌드로 교체해 #223을 닫는 것이 맞다.

## 4. 코드 포즈 값을 고정한 테스트(맹목적으로 고치지 말 것)

EditMode(수치 고정, Swat 값):
- `StanceTests.cs:131-142,152-190` — 85 / 40 / 45(`ProneArmCounterDeg`) / `ProneBodyHeight 0.15` / `ProneModelBackShift` 도달 거리 0.45 m / 크라우치 무릎 86도·0.2427 m 드롭. 엎드림·웅크림을 클립으로 바꾸면 이 테스트 자체가 대상 아님(코드·규칙을 지울 때 함께 삭제).
- `SlotDataTests.cs:53-54` — rules.json의 Prone 값 85/40.
- `CharacterGaitTests.cs`(12 매치) — 다리 회전·보행 속도 규칙, 모델과 무관한 순수 수학이면 유지.
- `RifleHoldTests.cs`, `ReloadMotionTests.cs` — `RifleHold`/`ReloadMotion` 순수 수학. 코드 포즈를 쓰는 동안만 의미. 클립으로 바뀌면 `ReloadMotion`과 함께 삭제 대상.
- `VehiclePoseTests.cs`(7) — 좌석 자세·손 위치 수식(ADR-077/078).
- `CharacterAnimatorTests.cs`(4~5개) — Swat 클립 이름과 컨트롤러 구조(Locomotion 블렌드, 공중 상태, Upper 마스크, 펀치 클립): `CharacterKit` 재작성 시 재작성 필요(새 컨트롤러 구조 기준으로 같은 의도를 다시 표현).

PlayMode(씬에서 실제 본 위치·각도 측정, Swat 본 이름 `Hips`, `Wrist.R` 하드코딩):
- `SlotsAndProneTests.cs:481-511` — 엎드림 때 모델 피치 == ProneModelPitchDeg, 모델 up이 시선과 평행, 엉덩이 높이 < 0.45 m, 총이 수평. 클립 기반 엎드림이면 "엎드림 중 머리/가슴 높이 상한, 총이 시선 방향" 같은 결과 기반 단언으로 바꿔야 한다.
- `AimAndLeanTests.cs`(15 매치, `Hips`, `Wrist.R` 조회), `StancePlayTests.cs:97`, `CombatantPresentationTests.cs:83`(무기가 `Wrist.R` 자식인지) — 본 이름/부모 가정. 새 소켓 이름으로.
- `MeleeWeaponPlayTests`, `PunchPlayTests`(5), `MovementExtrasTests`, `LedgeCaptureTests`, `EmotePlayTests`, `SwimPlayTests`, `VehiclePosePlayTests`, `ReplicationBodyTests` — 포즈 상태 속성(`PronePose`, `UpperState`, `BaseState`, `FootLow`)과 캡처. 상태 머신 이름과 공개 속성을 유지하면 대부분 통과한다 → 새 구현도 같은 공개 속성(`PronePose`, `CrouchPose`, `RiflePose` 등)을 노출하도록 설계하면 테스트 수정을 최소화.
- 캡처 테스트(`CharacterCaptureTests` 등 FeelCapture)는 사람 눈 판정용이라 그대로 새 모델 검증에 재사용.

## 5. 체크리스트 (순서대로, 다른 트랙과 겹치지 않게)

선행(다른 트랙 결과 필요): 캐릭터 모델 선택(다른 에이전트)이 끝난 뒤 아래를 시작.

단기 확인 / 임시 완화(교체 전에 사용자가 쓸 빌드 개선이 필요할 때만)
- [ ] 새 빌드로 (a)(b)를 다시 촬영해 현재 트리 상태 확정(`CHAR_SHOTS=Logs/charshots tools/unity.sh test playmode Game.Tests.CharacterCaptureTests`, 잠금 `/tmp/sublevel-playmode.lock`, 끝난 뒤 `git restore -- ProjectSettings`, `Assets/InitTestScene*` 삭제). 이번 감사에서는 안 돌렸다.
- [ ] (선택) 임시: `ProneHeadLiftDeg` 40→10 이하, `ProneModelBackShift` 재측정, `SprintSupportRelease` 0.9→0.2. 해당 테스트(§4) 값 갱신 필요. 교체가 결정됐으므로 하지 않아도 됨.
- [ ] (선택) 임시: Sentinel 길이 1.05→0.87 m, VK-9 0.24→0.19 m(`CharacterKit.Weapons`) + 프리팹 재생성 + `ItemIconBuilder` 재실행.

Humanoid 전환 준비
- [ ] 새 캐릭터 FBX와 Mixamo 16개 FBX를 Humanoid로 임포트하는 규칙을 `AssetImportRules.cs`에 추가(`animationType: Humanoid`, 클립은 새 캐릭터 Avatar 복사, 루트 모션 off, 클립별 루프 플래그: RifleIdle/Run/Walk/WalkBack/SideStep/CrouchWalk/CrouchWalkBack/KneelIdle/AimingIdle는 루프).
- [ ] 새 캐릭터 높이 1.8 m(발~정수리) 확인, 머리 크기 실측 후 `HitZoneRules.HeadMeters`(rules.json) 재조정.
- [ ] 의존성 결정: Animation Rigging 패키지 추가 여부를 사용자에게 먼저 묻는다(권장: 추가하지 않고 `OnAnimatorIK`).

CharacterKit 재작성(`Editor/CharacterKit.cs`)
- [ ] `AddTo`: Humanoid Avatar `Animator`, 마스크(Humanoid 상체), 본 조회를 `GetBoneTransform`으로, `Visual` 구조체 필드(`Spine, Hips, FootLeft/Right, JumpBones`)는 이름 유지.
- [ ] 무기 소켓: `RightHand` 아래 무기 부모 + 무기별 `LeftHandGrip`/`RightHandGrip` 자식(`WeaponVisual.Fit` 재계산). 조준 기준 포즈는 새 `Mx` 아닌 Humanoid 조준 대기 클립에서 측정.
- [ ] 컨트롤러 재생성: Base(이동 2D 블렌드 + Stance 블렌드), Upper(조준/사격), Action(장전·꺼내기·집어넣기), 사망. `MixamoBake.cs`와 `Baked/Mx_*.anim` 삭제(삭제는 사용자 확인 후).
- [ ] `TestBoxBuilder.Build`로 Player/Bot/TrainingDummy 프리팹 재생성(`git restore -- Assets/_Project/Prefabs/M1/Vehicles`).

CombatantAnimation 개편(공개 속성 유지: `PronePose`, `CrouchPose`, `RiflePose`, `UpperState`, `BaseState`, `FootLow` 등)
- [ ] 엎드리기: `ProneModelPitchDeg/HeadLift/BackShift/BodyHeight`와 `armUp` 코드 삭제, `Mx_ProneFiringRifle` 대신 Humanoid 엎드림 클립 + 엎드림 대기/기기/사격/장전, 눕기·일어나기 전환(없으면 Mixamo 카탈로그 확인 후 추가).
- [ ] 웅크리기: `CrouchThighDeg/ShinDeg`, `GroundCrouchedFeet` 삭제, 웅크림 대기/이동 클립 블렌드(#227).
- [ ] 소총 파지: `Reach/TwoBone/Grip/CloseHand`를 `OnAnimatorIK` 왼손 IK로 교체. `SprintSupportRelease` 개념 삭제(스프린트 클립이 두 손으로 총을 든다). 장전·꺼내기 손 동작은 클립이 담당(`ReloadMotion.cs`, 탄창 큐브 `ShowMag`는 클립 이벤트로 대체하거나 유지).
- [ ] 위아래 시선 분담: `RifleHold.FollowedPitch`/`TorsoPitchShare` 수식을 Humanoid Spine/Chest/Neck에 분배(코드 유지, 본 조회만 교체).
- [ ] 이동 방향: 8방향 Mixamo 클립이 있으면 2D 프리폼 블렌드(#223 닫기), 없으면 `_legs.TurnDeg` 유지.
- [ ] 발 IK: 접지 레이캐스트 + `AvatarIKGoal.LeftFoot/RightFoot`(#235), `FootContact` 이벤트는 유지(발소리 `FootDown`이 의존).
- [ ] 점프·착지·제자리 회전·피격·사망 방향 클립 연결(#228, #234, #224, #196).

기타 시스템 포팅
- [ ] 근접(`.Melee.cs`), 투척·치료·줍기(`.Items.cs`), 난간(`.Ledge.cs`), 수영(`.Swim.cs`), 에모트(`.Emote.cs`): 클립 확보 가능한 것은 클립, 나머지는 `HumanBodyBones` 기준 코드로 이식.
- [ ] 차량 좌석(`.Vehicle.cs`, ADR-077/078): 새 모델로 좌석 오프셋 재측정, 앉은 클립 + 핸들·핸들바 손 IK, 손가락 이름 하드코딩(`FingerNames`) 제거.
- [ ] `BackpackVisual`(마운트 본), `InventoryPreview.cs:167`(무기 손) 본 이름 교체.
- [ ] 권총: 권총 클립 출처 결정(UAL 또는 Mixamo), 한 손/두 손 클립 + 왼손 IK(`PistolHold`).
- [ ] 무기 길이를 실물 비례로 재설정(AK 0.87, 권총 0.19 등) — 무기 모델 교체 트랙과 합의 후.

테스트·검증
- [ ] §4 목록대로 코드 포즈 수치 테스트(StanceTests, SlotDataTests, ReloadMotionTests, 일부 RifleHoldTests)는 해당 코드를 지우는 시점에 함께 삭제/이관, 결과 기반(엎드림 머리 높이 상한, 총이 시선과 평행, 양손이 총에 닿음, 발이 지면에 닿음) 단언으로 교체. 맹목적 재작성 금지.
- [ ] PlayMode 본 이름 하드코딩(`Hips`, `Wrist.R`: AimAndLean, StancePlayTests, SlotsAndProneTests, CombatantPresentation)을 `Animator.GetBoneTransform`으로.
- [ ] 캡처: `CharacterCaptureTests` 전체(427장)를 새 모델로 다시 찍고 사람 눈으로 판정(특히 엎드림·소총 달리기·장전·스프린트·장전 중 왼손), 총 크기(키 대비 소총 길이 ≤ 50%).
- [ ] `ASSET_LEDGER.md`, `DECISIONS.md`(새 ADR: Humanoid 전환, MixamoBake 폐기, ADR-166 대체), `RISKS_OPEN_QUESTIONS.md`(Mixamo 약관) 기록.
- [ ] 성능: 같은 틱에 IK 여러 개가 돌아가므로 `PERF_BUDGET.md`(봇 수 × `OnAnimatorIK`) 확인.

미확인/한계: (1) 현재 작업 트리로 새 캡처를 찍지 않았다(08:46 캡처는 Mixamo 연동 직전/직후 혼재). (2) 구운 클립 시트의 다리 정지·왼팔 처짐·엎드림 클립 서있음은 정지 이미지 한 장으로 본 것이라 확신도 중간. (3) Mixamo 카탈로그의 클립 이름은 사이트를 열어 확인하지 않았다.

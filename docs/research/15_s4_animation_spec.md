# 15. S4 애니메이션 — 스펙 · 지도 · 티켓 · 테스트 재작성 계획 (2026-11-16)

세션 S4(WP 10 후반). 근거: ADR-173(확정), ADR-180·181(S3), **ADR-186~191(이 문서의 가정)**, `04_animation_audit.md`, `animation.json`, S3 노트(지도 #252, 티켓 #253~#262), `CharacterProbe` 결과.
코드·에셋 변경 없음(Unity 미실행). 구현은 S3의 Humanoid 임포트(#255) 이후, master 순번 7.

GitHub: 지도 #298, T1 #299, T2 #300, T3 #301, T4 #302, T5 #303, T6 #304, T7 #305, T8 #307, T9 #309, T10 #310, T11 #312, T12 #314.

## 0. 확인한 것 / 추정
| 구분 | 내용 |
|---|---|
| 확인 | 시험 Mixamo Swat은 Unity 6.3에서 Humanoid(Avatar `isValid=True`, 본 55/55). 키 1.780 m, 머리 18.2%(5.49등신), hips 0.965, 다리 0.848(허벅지 0.418·정강이 0.392·발~바닥 0.123), 팔 어깨~손끝 0.783(위 0.277·아래 0.279·손 0.227), 어깨폭 0.369. 삼각형 19,450(예산 15k 초과 → S3 LOD0 결정). 눈높이 추정 1.658(규칙 1.6 → S3 T5 정규화). |
| 확인 | 총 길이/팔 길이: 권총 31%, SMG 64%, 불펍 96%, 샷건 121%, AR 134%(S3 #258). 키 대비 AR 59%. 소총 파지 IK가 팔 길이에 의존하므로 무기 길이는 S3·S7와 맞춘다. |
| 확인 | 현재 코드: `CombatantAnimation*.cs` 9파일 2,597줄(본 이름·`Turn(bone,deg,axis)` 코드 포즈 중심), `CharacterKit` 521줄, `MixamoBake` 195줄. 컨트롤러 `Character.controller`는 레이어 2개(Base 1D 블렌드 + Upper). 감사 04 §1~§4가 원인·테스트 목록을 정리. |
| 확인 | 기존 Mixamo 소총 FBX 16개는 저장소에 이미 있고(ledger C-0005) 모두 Generic 임포트. 신규 클립은 아직 0개(`AssetStaging/mixamo`에는 시험 캐릭터 1종뿐). |
| 추정 | 16클립이 Humanoid 재임포트에서 Hips 루트 모션 없이 제자리로 구워질지(클립별 `Root Transform` 설정), 손가락·손목이 소총 파지에서 맞을지는 Unity 임포트 전까지 모름. 장전 클립에서 왼손 위치 곡선(`LeftHandIK`)을 클립에 심을 수 있을지(FBX 임포트 커브) 미확인. |
| 추정 | Mixamo 카탈로그의 정확한 클립 이름(격차 목록)은 사이트에서 확인하지 않았다(감사 04와 동일). |

## 1. 스펙 (to-spec)
**목표.** 캐릭터 애니메이션을 Humanoid 기반 클립 구동으로 바꿔, 감사 이슈의 구조적 원인(코드 포즈·베이크 결함·IK 포기)을 제거한다. 수직 슬라이스(R1+R0)에서 서기·웅크림·엎드림·이동·스프린트·점프·장전·사망이 모두 클립으로 보이고, 소총을 두 손으로 든다.

**범위.** ADR-186. 슬라이스 필수 10건, 슬라이스 후 7건, 범위 밖 3건.

**범위 밖.** 모델·소켓·LOD(S3), 비율 #230(S3), 그림자 #238(S2), 사운드(S5), 1인칭 팔 교체, 래그돌, 봇 AI.

**완료 기준 (슬라이스 WP).**
1. EditMode: 컨트롤러 구조·Humanoid 임포트 규칙·순수 규칙(`Core`) 통과, 기존 971 유지(코드 포즈 고정 테스트는 §5대로 교체).
2. PlayMode(소리 없음, `/tmp/sublevel-playmode.lock`): 결과 기반 단언(§5) 통과.
3. 수치: 자세별 머리·가슴·엉덩이 높이, 총-시선 각도, 양손-총 거리, 발-지면 거리를 CSV로 남긴다. 연속 프레임 시퀀스 캡처(이동 8방향·스프린트·엎드림 진입·점프·장전) + 수치 병기, 정지 이미지 단독 판정 금지(ADR-191).
4. PERF: 50인 Animator 비용(화면 안 N명 IK·레이어) 측정 후 PERF_BUDGET 판정.
5. ASSET_LEDGER·`animation.json`·`python3 tools/bible_build.py` 갱신, 폐기 코드(`MixamoBake`·`Baked/Mx_*`)는 준환 확인 뒤 삭제.

## 2. 설계

### 2.1 레이어 (ADR-188)
| # | 레이어 | 내용 | 마스크 | 구동 |
|---|---|---|---|---|
| 0 | Base | 2D Freeform Directional(`MoveX`,`MoveZ`) × 자세. 상태: `Stand`·`Crouch`·`Prone`(블렌드 트리 각각), `Air`(Rise/Fall/Land, `AirPhase` 시간), `Death`. 스프린트는 Stand 트리의 가장 바깥 반경 클립(저자세 로우레디). | 전신 | `CombatantMotion.LocalVelocity`, `CombatantStance` |
| 1 | Upper | 조준 유지·사격(`Aim`,`Shoot`). 무기 없음/권총/소총 별 상태. 스프린트 중 Upper 가중 0(Base의 로우레디가 두 손 파지를 가짐) | Humanoid 상체(Spine~Head, 양팔, 손가락) | 기존 `_upper` 상태기계 이름 유지 |
| 2 | Action | 트리거 `Reload`·`Draw`·`Holster`·`Heal`·`Throw`·`Melee`. 장전은 자세별 클립. 끝나면 가중 0 | Humanoid 상체 | `ReloadMotion`/`ItemMotion` 타이밍 규칙(Core, 순수 C#) 유지 |
| 3 | Additive | 숨쉬기 루프(약), 피격 플린치 | 전신(가산) | 피격 이벤트 |
| — | 시선 | 레이어 아님. `LateUpdate`에서 Spine/Chest/UpperChest/Neck에 피치 분배 | — | `RifleHold.FollowedPitch`·`TorsoPitchShare` 재사용 |

`Character.controller`는 `CharacterControllerBuilder.Build()`(새 파일, master 승인 필요)가 코드로 생성. `CharacterKit`은 이 빌더를 호출만 한다.

### 2.2 IK (ADR-189)
- **왼손**: `OnAnimatorIK(layer)` → `SetIKPosition/Rotation(LeftHand)` = 총 `LeftHandGrip`. 가중치 = `gripWeight`(기본 1) × (1 − Action 레이어의 `LeftHandIK` 해제 커브). 스프린트에서도 1 유지(로우레디 클립이 이미 그 근처에 있으므로 IK 보정이 작다).
- **오른손**: 클립 소유. 총은 `WeaponSocket`(RightHand 자식, S3)에 붙고 오프셋은 `CharacterDef`. 조준 시선 보정은 척추 피치 분배가 담당(팔 직접 비틀기 `armUp` 삭제).
- **발**: `OnAnimatorIK`에서 `LeftFoot/RightFoot` 위치·회전 + 접지 레이캐스트(발당 1개), 골반 오프셋은 낮은 발 기준. 경사 한도(`slopeLimit`)·웅크림·엎드림에서는 가중 감쇠. 근거: #235(경사·계단), #232(기울이기에서 발 고정).
- **비용 한도(가정)**: 20 m 안 12명까지 IK, 이후 0. 측정 후 확정.

### 2.3 상태 ↔ 클립 인벤토리 매핑 (기존 16클립 + 격차)
| 상태/기능 | 이슈 | 클립(있음 = 저장소 Mixamo FBX) | 비고 |
|---|---|---|---|
| 서서 대기(조준 안 함) | #231 | `RifleIdle` 있음 | 호흡·체중 이동은 Additive Breath + 클립 자체 |
| 서서 조준 대기 | | `Swat_RifleAimingIdle_skin`(clip) 있음 | Upper |
| 걷기/달리기/후진 | #223 | `RifleWalk`·`RifleRun`·`RifleWalkBack` 있음 | |
| 좌/우 이동, 대각 | #223 | `RifleSideStep` 1방향 있음 → 미러+대각 블렌드. 전/후 대각은 **격차** | 폴백: `LegHeading` 유지(ADR-190) |
| 스프린트(총 내림) | #222 | **격차**(Mixamo "Rifle Run" 변형 후보, 카탈로그 미확인) | 임시: `RifleRun` + Upper 가중 0 + 코드 상체 숙임(Additive) |
| 사격 | | `FiringRifleStand` 있음 | Upper `Shoot` |
| 웅크림 이동/대기 | #227 | `RifleCrouchWalk`·`RifleCrouchWalkBack`·`RifleKneelIdle` 있음. 웅크림 좌우 없음 → 걷기 블렌드 | |
| 엎드림 사격 | #225 | `ProneFiringRifle` 있음 | |
| 엎드림 장전 | | `ProneReloadingRifle` 있음 | |
| 엎드려 기기/눕기·일어나기 | #225 | **격차**("Crawl", "Lying Down/Stand Up" 계열, 카탈로그 미확인) | 임시: 정지 엎드림 + 위상 속도 연동 |
| 장전(서기/웅크림) | | `ReloadingRifleStand`·`ReloadRifleCrouch` 있음 | Action |
| 꺼내기/집어넣기 | | `RiflePullOut`·`RiflePutAway` 있음 | Action |
| 사망 | | `RifleDeath` 있음. 방향별 **격차** | ADR-186: 방향별은 BACKLOG |
| 점프·착지 | #228 | **격차**(Rifle Jump 계열) | 임시: 현재 `Air` 코드 tuck 유지 |
| 제자리 회전 | #234 | **격차**(Turn 계열) | 슬라이스 후 |
| 피격 | #224 닫힘 | 현재 Swat `HitRecieve` → **격차**(Mixamo 피격) | Additive 플린치로 대체 가능(코드/가산) |
| 권총 대기·이동·장전 | #218 | **격차**(UAL 우선 검토, CC0) | 슬라이스 후 |
| 근접/투척/치료 | #219 | **격차**. 투척·치료는 현재 코드(`.Items.cs`, #220 #221 닫힘 → 회귀 금지) | 슬라이스 후 |
| 수영/난간/차량 앉기/에모트 | #236 #233 #237 #173 | **격차** | 슬라이스 후, 코드 유지 |

### 2.4 클립 임포트 규칙 (AssetImportRules, ADR-187)
`animationType: Humanoid`, `Avatar Definition: Copy From Other Avatar`(S3 Mixamo 캐릭터 Avatar), `loopTime`: Idle/Walk/Run/WalkBack/SideStep/CrouchWalk/CrouchWalkBack/KneelIdle/AimingIdle만, `lockRootRotation/Height/Position`(Original, Bake Into Pose: 이동·루프류), `Root Motion` off(`Animator.applyRootMotion=false`는 `CharacterKit`이 이미 사용: 확인 필요), 30 fps. 규칙 파일은 S4·S2가 모두 만질 수 있어 master 중재(SESSION_PLAN §2 S2 "AssetImportRules 확장").

## 3. wayfinder 지도
```
D1 설계·스펙·ADR(이 문서) ─┬─► T1 S3↔S4 인터페이스 문서 (Unity 불필요, #254와 합의)
                           └─► T10 격차 클립 시트 → 준환 선택 → 다운로드·ledger [BLOCKED 준환]

S3 #255 시험 Swat Humanoid ─► T2 클립 Humanoid 재임포트·규칙 (Unity 7번)
                                ─► T3 CharacterControllerBuilder + 레이어 4개 + 테스트 재작성 (EditMode)
                                     ├─► T4 Base 방향 이동·스프린트·대기 (#223 #222 #231)
                                     ├─► T5 웅크림·엎드림 클립 (#227 #225)
                                     ├─► T6 점프·착지 (#228)  ◄ T10(점프 클립)
                                     └─► T7 Action + 왼손 IK/LeftHandGrip (#218 소총, #226 일부, 감사 b) ◄ S3 #259 소켓
T3..T7 ─► T8 시선 분배·기울이기·제자리 회전 (#232 #234)
        ─► T9 발 IK (#235) + 50인 Animator PERF
        ─► T11 슬라이스 후: 난간·수영·근접·권총·차량 IK (#233 #236 #219 #218 ADR-077/078)
T4..T9 ─► T12 마감: 테스트 정리·MixamoBake 폐기(준환 확인)·문서·code-review
```
Fog: 클립 커브로 `LeftHandIK` 해제가 되는지, 16클립의 루트 모션, 방향 이동 격차 클립, 50인 IK 비용.

## 4. 티켓 초안 (to-tickets → GitHub)
| ID | 제목 | 선행 | Unity | 증거 |
|---|---|---|---|---|
| T1 | S3↔S4 인터페이스 문서(소켓·리타게팅·`LeftHandGrip`·`CharacterDef`) | 없음 | 아니오 | `16_s3_s4_interface.md` + S3 확인 |
| T2 | 기존 Mixamo 16 클립 Humanoid 재임포트 + `AssetImportRules` 규칙 | S3 #255 | 예 | 클립 목록 로그(루프·루트 모션·길이), EditMode 규칙 테스트 |
| T3 | `CharacterControllerBuilder` + 레이어 4개 + `CharacterAnimatorTests` 재작성 | T2 | 예 | EditMode 통과, 구조 단언 |
| T4 | Base: 방향 블렌드·스프린트·대기(#223 #222 #231) | T3 | 예 | 8방향 발-지면·속도 수치 + 프레임 시퀀스 |
| T5 | 웅크림·엎드림 클립화 + 코드 포즈 삭제(#227 #225) | T3 | 예 | 자세별 높이 CSV(머리·가슴·엉덩이), 총-시선 각 |
| T6 | 점프·착지(#228) | T3, T10 | 예 | 도약·낙하·착지 프레임 시퀀스 + 접지 시각 수치 |
| T7 | Action 레이어 + 왼손 IK + `LeftHandGrip`(#218 소총, #226 일부) | T3, S3 #259 | 예 | 양손-총 거리 수치(장전 제외 전 구간 ≤ 5 cm), 장전 프레임 시퀀스 |
| T8 | 시선 피치 분배 + 기울이기 + 제자리 회전(#232 #234) | T4, T5 | 예 | 피치/롤 각도 수치, 발 고정 거리 |
| T9 | 발 IK + 50인 Animator PERF(#235) | T4, S3 #260 | 예 | 경사/계단 발-지면 거리, ms 측정, PERF 판정 |
| T10 | 격차 클립 시트(Mixamo/UAL) → 준환 선택 → ledger | 없음 | 아니오 | 후보 시트 `docs/research/`. **BLOCKED: 준환 선택·다운로드** |
| T11 | 슬라이스 후: 난간·수영·근접·권총·차량 IK(#233 #236 #219 #218) | T7, T10 | 예 | 이슈별 수치·시퀀스 |
| T12 | 마감: 테스트 정리·`MixamoBake` 폐기·`animation.json`·code-review | T4~T9 | 예 | 전체 EditMode/PlayMode, 삭제 승인 기록 |

## 5. 테스트 재작성 계획 (맹목 수정 금지)
분류 근거: 감사 04 §4(코드로 확인). 각 행은 "그 코드를 지우는 WP에서만" 바꾼다.

| 테스트 | 현재 고정 대상 | 처리 | 새 단언(결과 기반) |
|---|---|---|---|
| `CharacterAnimatorTests`(EditMode 4개) | Swat 클립 이름·1D 블렌드·Upper 마스크 | **재작성(T3)** | 레이어 4개, Base 2D FreeformDirectional(좌우·전후 클립 존재·중복 없음), `Air` 시간 파라미터, 상체 마스크에 다리 비활성·팔 활성, Action 트리거 목록, 모든 이동 클립 루프·루트 모션 없음 |
| `StanceTests`(131-190) | 85/40/45/0.15, 무릎 86도 | **삭제(T5)** 코드 삭제와 같은 WP | 엎드림: 머리 높이 ≤ 0.45 m, 몸통 축이 지면과 ≤ 15도(Humanoid Hips→Head), 양발 지면 ≤ 8 cm. 웅크림: 머리 높이 1.1~1.3 m |
| `SlotDataTests`(53-54) | rules.json Prone 85/40 | **삭제(T5)** + rules.json 키 정리(S4 소유 밖 → master) | — |
| `CharacterGaitTests` | 다리 회전·보행 속도(순수 수학) | **유지**(`LegHeading` 폴백 삭제 전까지). 삭제 시 방향 블렌드 속도 임계 테스트로 이관 | `Core` 순수 규칙 |
| `RifleHoldTests`·`ReloadMotionTests` | 코드 포즈 수학 | **부분 유지**: `FollowedPitch`/`TorsoPitchShare`는 시선 분배로 유지, `ReloadMotion` 손 위치는 클립 구동 후 삭제 | 시선 분배: 총이 시선과 ≤ 3도 |
| `VehiclePoseTests`(7) | 좌석 자세·손 위치 수식 | **T11까지 유지** | 앉은 Humanoid 골반 높이·손-핸들 거리 |
| PlayMode `SlotsAndProneTests:481-511` | 모델 피치 == 85, 엉덩이 < 0.45 | **재작성(T5)** | 위 엎드림 수치 + 총-시선 평행 |
| PlayMode `AimAndLeanTests`(15)·`StancePlayTests:97`·`CombatantPresentationTests:83` | `Hips`·`Wrist.R` 이름·부모 | **재작성(T3/T7)** | `Animator.GetBoneTransform(...)`, 무기가 `WeaponSocket` 자식 |
| `MeleeWeaponPlayTests`·`PunchPlayTests`·`MovementExtrasTests`·`LedgeCaptureTests`·`EmotePlayTests`·`SwimPlayTests`·`VehiclePosePlayTests`·`ReplicationBodyTests` | 공개 속성(`PronePose`·`UpperState`·`BaseState`·`FootLow`) | **유지**(ADR-188: 속성명 유지). 깨지는 것만 개별 원인 확인 후 수정 | |
| `CharacterCaptureTests`·`PoseCaptureTests` 등 캡처 | 사람 눈 | **유지·확장**: 연속 프레임 + CSV 출력 추가 | 프레임 시퀀스 |
신규(T4~T9): `PoseMetrics` 헬퍼(PlayMode 테스트 보조, 본 높이·각도·거리를 CSV로 기록).

## 6. 위험·열린 질문
- `CombatantAnimation` 9파일 2.6k줄 재작성은 한 번에 못 한다 → 상태별(자세/이동/장전) 점진 교체, 각 단계에서 PlayMode 공개 속성을 유지(ADR-188).
- 소켓·`BackpackVisual`·`InventoryPreview`·`MeleeKit`·`WeaponVisual.Fit`은 S3·S7 소유 파일과 접촉 → master 중재.
- 신규 클립 다운로드·약관은 준환 몫. 격차 클립이 안 오면 T6·T4(스프린트)는 임시 코드 보강으로 슬라이스를 통과시킨다.
- `rules.json`·`AssetImportRules`·`TestBoxBuilder`는 S4 소유 목록 밖 → 변경 필요 시 master 경유.

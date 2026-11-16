# 16. S3 ↔ S4 인터페이스 (캐릭터 모델 ↔ 애니메이션) — 제안 v1 (2026-11-16)

S3 노트(`S3_character_spec_and_tickets.md` §2 인터페이스)를 받아 S4가 구체화했다. S3 #254(T2)와 합의되면 이 문서를 단일 출처로 한다. 충돌 시 ADR-173·180·181·186~191.

## 1. 본 접근
- 이름 문자열(`Wrist.R`, `mixamorig:RightHand`) 금지. 항상 `Animator.GetBoneTransform(HumanBodyBones.*)`.
- 쓰는 본: Hips, Spine, Chest, UpperChest(없으면 Chest), Neck, Head, Left/RightUpperArm·LowerArm·Hand, Left/RightUpperLeg·LowerLeg·Foot, 손가락(손 모양 클립이 없을 때만).
- 소유: S3 `CharacterKit`이 `HumanoidSockets` 구조체(위 본 Transform 캐시 + 소켓)를 `Visual`에 채운다. S4 `CombatantAnimation`은 이 구조체만 읽고 본 이름을 알지 못한다(기존 직렬화 필드 `_spine`·`_hips`·`_footLeft/Right`·`_jumpBones` 이름은 유지해 프리팹 재생성이 쉽다).

## 2. 소켓 (S3가 만들고 S4가 읽음)
| 소켓 | 부모 | 용도 | 규약 |
|---|---|---|---|
| `WeaponSocket` | RightHand | 총·근접 모델 | 위치·회전 오프셋은 `CharacterDef.weaponSocketOffset`(손 크기 보정). 총 프리팹 원점 = 오른손 파지점 |
| `LeftHandGrip`(총 프리팹 자식) | 총 | 왼손 IK 목표 | 위치·회전 모두 사용. 총마다 S7가 제공(없으면 S3가 모델별 임시값을 `weapons.json` 데이터로) |
| `RightHandGrip`(총 프리팹 자식, 선택) | 총 | 오른손 파지 보정 | S4는 쓰지 않음(클립이 소유). 총을 소켓에 맞추는 데만 S3 사용 |
| `HeadSocket` | Head | 헬멧·완장·머리띠 | 방어구 3등급(S3 A3) |
| `BackSocket` | UpperChest/Chest | 백팩(`BackpackVisual`) | 스케일은 `CharacterDef.height` 비례 |
| `EyePoint` | Head | 카메라·시선 | 캡슐 눈 1.6 m 유지(S3 T5) |
S4 요구: 소켓은 런타임에 이름으로 찾지 않고 `Visual`에서 참조로 받는다. 프리팹 생성(`TestBoxBuilder.Build`)이 채운다.

## 3. 리타게팅·임포트
- 모든 로스터 모델 FBX는 Humanoid. Avatar는 **모델별 생성**(`Create From This Model`), 클립은 **공유**(`Copy From Other Avatar` = S3 기준 Mixamo Swat Avatar). UBC·UAL(Humanoid)와 Mixamo 클립이 같은 컨트롤러를 쓴다.
- 루트 모션 off: 클립 임포트 규칙은 S4가 `AssetImportRules` 제안(master 승인 후 적용). `Animator.applyRootMotion=false`.
- 모델 스케일은 S3가 키 1.8 m로 정규화(FBX `globalScale`·프리팹 스케일). **S4는 모델 스케일에 의존하는 상수가 없어야 한다**: 발 IK·손 IK 거리는 `Animator.humanScale` 또는 `CharacterDef.height` 대비 비율로 쓴다.
- 손 크기·팔 길이가 모델마다 달라 왼손 IK가 총에서 떨어질 수 있다: IK는 위치 가중치 1을 쓰되 팔이 닿지 않으면(`hintWeight`/최대 팔 길이 초과) 가중치를 줄이는 대신 **총 전체를 소켓 오프셋으로 당긴다**(S3 `weaponSocketOffset`가 키·팔 길이 비례). 판정: 양손-총 거리 ≤ 5 cm (S4 T7).

## 4. CharacterDef (S3) 필드 중 S4가 쓰는 것
`height`(1.8), `eyeHeight`(1.6), `weaponSocketOffset`(pos, rot), `handSizeScale`, `lodDistances`(S3 LOD), `hasHelmetMask`(헬멧 소켓 미사용). S4는 읽기만 하고 쓰지 않는다.

## 5. LOD·성능 약속
- LODGroup(S3): LOD0 ≤ 15k, LOD1 약 5k, LOD2 약 1.5k, 100 m 컬. S4: LOD1 이상에서 Additive 레이어·발 IK·손가락 업데이트 끔(`CombatantAnimation.LodLevel` 입력은 S3의 LODGroup 현재 LOD를 읽거나 거리로 대체).
- `Animator.cullingMode = CullUpdateTransforms`(화면 밖). 서버 헤드리스: 히트는 높이 기반이므로 `AlwaysAnimate` 필요 여부를 S3·S4가 함께 측정(S3 노트 §2-5).
- IK 한도(ADR-189): 20 m 안 12명. `CharacterDef`는 IK에 영향 없음.

## 6. 소유권 경계 (충돌 방지)
| 파일 | 소유 | 비고 |
|---|---|---|
| `CharacterKit`(모델·소켓·프리팹) | S3 | 컨트롤러 생성은 S4 `CharacterControllerBuilder`로 분리(master 승인) |
| `CharacterControllerBuilder`, `CharacterAnimator*`, `CombatantAnimation*`, `CombatantMotion`, `MixamoBake` | S4 | |
| `BackpackVisual`·`InventoryPreview`·`MeleeKit`·`WeaponVisual.Fit`·`TestBoxBuilder` | 본 이름 교체 필요 → **master 중재**(S3 T7 영향 목록에 포함) | |
| `AssetImportRules`·`rules.json` | master 중재 | S4가 필요한 규칙을 문서로 제안 |

## 7. 합의 확인 항목 (S3에 묻는 것)
1. `HumanoidSockets` 구조체를 `Visual` 필드로 두고 S4가 직렬화 필드 이름(`_spine`·`_hips`·`_footLeft/Right`)을 유지해도 되는가.
2. Avatar 복사 기준 모델 = Mixamo Swat(시험 FBX)로 확정해도 되는가(UBC는 자체 Avatar, 클립 공유).
3. `LeftHandGrip`을 S7가 총 프리팹에 넣기 전까지 S3가 `weapons.json`의 임시 오프셋으로 제공하는가.
4. #255 결과(눈높이 1.658 → 1.6 정규화 방식)가 키·소켓 위치에 주는 영향은 S3가 알려 달라.

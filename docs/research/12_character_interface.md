# 설계 #12 — 캐릭터 ↔ 애니메이션 인터페이스 (S3 → S4) (2026-11-16)

대상: S4(애니) 세션. 근거: ADR-173·180·181, 01·04 보고서, T3 실측(`Logs/s3_probe.txt` 요약은 §6). 이 문서의 약속을 바꾸려면 S3와 master에 알린다. **확인**/추정 표시.

> **단일 출처 전환(2026-11-16)**: 소켓 이름·필드는 S4 제안 `docs/research/16_s3_s4_interface.md`를 따른다(`HeadSocket`·`BackSocket`·`EyePoint`·`weaponSocketOffset`, 완장은 `ArmBandSocket`). 이 문서의 §5(FBX 비커밋 설계)·§6(실측)·§7(LOD)은 S3 소유로 유지.

## 1. 모델 규약
- 로스터의 모든 모델 FBX는 **Humanoid**(`animationType = Human`, `avatarSetup = CreateFromThisModel`). **확인**: Mixamo Swat은 55/55 본 매핑, isValid=True (T3). 기본 임포트는 Generic이므로 임포트 규칙에서 바꾼다(`AssetImportRules`는 S2 소유 → master 경유로 `CharacterRoot` 하위만 Human).
- **Avatar는 모델마다 하나**, 클립은 모델 간 공유(Humanoid 리타게팅). 클립 FBX(스킨 없음)는 임포트 시 `avatarSetup = CopyFromOther`가 아니라 **자기 Avatar를 만들고**(Mixamo 클립은 같은 뼈대) 재생 시 Unity가 리타겟한다. 클립 FBX의 Humanoid 임포트는 S4가 소유하고 이 가정은 **추정**(테스트 필요).
- 루트 모션 없음(위치는 캡슐/컨트롤러). 클립 임포트 `Bake Into Pose`·루프 플래그는 S4.
- 단위: FBX `fileScale 0.01`(cm) → 임포트 후 m. **확인**: 시험 Swat 키 1.78 m. 캡슐 1.8/눈 1.6 규칙은 유지, 모델은 `CharacterDef.HeightScale`로 키 1.8에 정규화(S3 T5).

## 2. 본 접근 (이름 문자열 금지)
`animator.GetBoneTransform(HumanBodyBones.X)`만 쓴다. `Wrist.R` 등 하드코딩은 폐기. 코드에서 쓰는 곳은 한 곳(`HumanoidSockets`)으로 모은다.

| 용도 | 본 |
|---|---|
| 무기 오른손 | RightHand |
| 왼손 파지 IK 목표 | LeftHand (`AvatarIKGoal.LeftHand`) |
| 헬멧·모자 | Head |
| 배낭 | UpperChest(없으면 Chest) |
| 상체 레이어 마스크 | Spine 이상, 양팔 (AvatarMask `Humanoid` 체크박스, 본 이름 불필요) |
| 발 IK | LeftFoot/RightFoot (`AvatarIKGoal`) |
| 시선 | `SetLookAtPosition`(Head/Eyes) |

## 3. 소켓 (S3 제공, S4 사용)
`CharacterKit`이 인스턴스 생성 시 아래 자식 Transform을 만든다(이름 고정). 캐릭터마다 손 크기 차는 `CharacterDef.HandOffset`으로 보정.
| 소켓 | 부모 | 쓰임 |
|---|---|---|
| `WeaponSocket` | RightHand | 총·근접·투척물 부착 지점 |
| `HeadSlot` | Head | 헬멧·팀 머리띠 |
| `BackSlot` | UpperChest | 배낭 |
| `ArmBandSlot` | LeftUpperArm | 팀 완장 |
무기 프리팹은 `RightHandGrip`(총 쪽 오른손 기준점, WeaponSocket에 맞춤)과 **`LeftHandGrip`**(S4가 `OnAnimatorIK`에서 LeftHand 목표로 읽음)을 자식으로 가진다. 무기 쪽 소유는 S7, 위치 데이터는 `weapons.json` 확장 후보.
소총 길이: 기존 `CharacterKit.Weapons` 값(Warden 0.75 / Breacher 0.95 / Sentinel 1.05 m)은 시험 Swat 팔 길이 0.78 m 대비 96/121/134%로 **실물 범위**(확인한 것은 수치뿐, "거대함"은 Quaternius Swat 스케일 때문이라는 추정은 미검증).

## 4. 캐릭터 정의 데이터 (`CharacterDef`, S3 소유)
`id`, 프리팹 경로, `HeightScale`, `HandOffset`(위치·회전), 틴트 색, 소품 슬롯 허용(헬멧·완장·배낭), 성별/체형 태그, LOD 거리, `MaskType`(None/Mask/FullHelmet), 라이선스 id(ledger). 스폰은 시드로 선택(인접 중복 금지). S4는 정의의 프리팹에서 Animator만 보면 된다(컨트롤러는 공유, `RuntimeAnimatorController` 한 개).

## 5. 파일 보관과 빌드 (Mixamo FBX를 커밋하지 않는 설계)
조건: 신규 Mixamo FBX는 저장소에 커밋하지 않는다(비공개 저장소 포함 문구 미확인, master 지시). 약관 중 AI/ML 학습 금지는 "에셋으로 모델을 학습시키지 않는다"이므로 게임 빌드와 무관하나, 에이전트가 FBX 내용을 외부 서비스에 보내지 않게 한다.
- **원본**: `AssetStaging/mixamo/`(git 무시). 준환이 받은 파일을 거기에 둔다.
- **반입(재현 가능)**: `CharacterKit.SyncStaging`(에디터 메서드, `tools/unity.sh run CharacterKit.SyncStaging`)가 staging → `Assets/_Project/Art/Characters/MixamoRoster/`로 복사하고 Humanoid 임포트 설정 후 `.meta`를 만든다.
- **gitignore 제안**: `Assets/_Project/Art/Characters/MixamoRoster/*.fbx`와 추출 텍스처는 무시하고 **`.meta`와 프리팹은 커밋**한다. `.meta` GUID가 고정되므로 프리팹 참조가 깨지지 않는다. 새 클론은 `SyncStaging` 한 번이면 복원된다.
- **빌드**: 빌드 머신이 같은 staging을 가진 경우에만 모델이 들어간다. `BuildScript`가 staging 누락이면 **명확히 실패**(조용히 빈 캐릭터 금지). 빌드 산출물(번들된 게임)은 Adobe FAQ상 허용(상업 게임), 원본 FBX 단독 배포는 금지 → 빌드 결과에 원본 `.fbx`가 아니라 임포트된 에셋만 들어간다.
- **한계**: 기존 Mixamo FBX 17개(C-0005)는 이미 git 추적 중이며 이력을 지우는 일은 규칙 4(되돌릴 수 없는 작업)로 하지 않는다. 신규만 이 설계를 따른다. 최종 판단(추적 유지 vs 이전)은 사람: RISKS_OPEN_QUESTIONS 항목.
- **ledger**: 파일이 프로젝트에 들어갈 때(SyncStaging 시점) C-00xx 행 추가(출처·약관 확인일·수정=데시메이트 여부). 이 설계 시점에는 쓰지 않는다.

## 6. 측정 기준값 (시험 Swat, T-pose, 확인)
키 1.780 m / 머리 메시 0.324 m(18%) / 눈 추정 1.66 m / 엉덩이 0.965 m / 위팔 0.277·아래팔 0.279·손 0.227 / 어깨 너비 0.369 / 삼각형 19,450(body 11,194 + head 8,256) / diffuse 1024², normal·specular 512².

## 7. LOD (T8)
`LODGroup` LOD0 ≤15k(원본 Swat 19,450 → 감축 필요), LOD1 ≈5k, LOD2 ≈1.5k, 100 m 컬. 임포터는 `_LOD0/_LOD1` 이름 규칙으로 LODGroup을 만든다(`AssetImportRules` 주석 확인). S4 요구: **LOD가 바뀌어도 같은 Avatar·본 이름**이어야 하므로 데시메이트는 스킨 메시만 바꾸고 본은 공유. 서버 헤드리스는 `AlwaysAnimate` 유지(별도 실측).

## 8. S4와 합의가 필요한 열린 항목
1. 클립 FBX Humanoid 임포트 방식(위 §1 추정 검증).
2. 왼손 IK 가중치 규칙(스프린트·장전 중)과 `LeftHandGrip` 데이터 위치(무기 프리팹 vs weapons.json).
3. `HumanoidSockets` 위치 보정을 S3(캐릭터별 `HandOffset`)와 S4(IK 오프셋) 중 어디서 하는지 — 제안: 손 위치 보정은 S3, 동작별 보정은 S4.
4. 테스트: `t_kit`·`t_other` 영향 목록은 T7에서 S3가 작성, 재작성은 S4가 모션 관련 테스트, S3가 임포트·소켓 테스트를 맡는다.

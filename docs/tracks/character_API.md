# 캐릭터 API (애니 트랙용, 2026-11-16, `track/character` 기준)

## 프리팹·복원
- 프리팹 `Assets/_Project/Prefabs/Roster/Roster_<Id>.prefab`. Id 12종: `MixamoSwat` `MixamoBrian` `MixamoLouise` `MixamoJames` `MixamoMegan` `MixamoKate` `MixamoJosh` `MixamoAdam` `MixamoMartha` `MixamoAlex` `UbcMale` `UbcFemale`.
- FBX·원본 텍스처는 git 무시. 새 클론: `tools/unity.sh run CharacterImport.SyncStaging`(FBX 복원·Humanoid·Mesh LOD) → 프리팹은 이미 커밋돼 있어 `CharacterRoster.Build`는 모델·재질을 바꿀 때만 다시 돌린다. 재생성한 결과(프리팹·재질·`Prefabs/Roster/Textures/*.png`)는 커밋 대상.
- 구조: 루트(`CharacterDef`, `LODGroup`) → `Model`(FBX 인스턴스, **Animator Humanoid**, Avatar 모델별 생성). 루트가 +Z 정면, 발이 y=0. 루트 모션 끔, `cullingMode = CullUpdateTransforms`.

## `Game.Gameplay.CharacterDef` (루트에 있음, 읽기 전용으로 쓴다)
| 필드 | 값·의미 |
|---|---|
| `Height` / `EyeHeight` | 1.8 / **1.6** (캡슐 눈 높이. 메시 눈은 약 1.69라 1인칭 카메라는 메시 눈보다 낮다) |
| `Sockets` (`HumanoidSockets`) | 본 Transform 18개 + `WeaponSocket` `HeadSocket` `BackSocket` `EyePoint` |
| `Animator`, `Renderers` | Humanoid Animator, SkinnedMeshRenderer 전부 |
| `Variants` / `ApplyVariant(i)` | 외형 변형. Swat·UBC 9개, 민간 모델 4개(Base + 3). `Variants[i].Name` 참고 |
| `HandSizeScale` | 손바닥~중지 첫마디 거리 / 0.09 m |
| `WeaponSocketOffsetPos/Euler` | 지금은 0. 총마다 손 파지 보정이 필요하면 애니 트랙이 채운다 |
| `StartLod` | 빌더가 고른 첫 Mesh LOD(원본 LOD0가 15k 초과인 모델은 1~3) |
| `HasHelmetMask` | Swat만 true: 모델에 헬멧이 이미 있어 `ArmorVisual` 헬멧과 겹친다 |

## 소켓 (런타임에 이름으로 찾지 않는다, `Sockets.X`로 받는다)
- `WeaponSocket`: 오른손 자식. 위치 = 손목에서 가운뎃손가락 첫마디 방향 60%, 회전 = 손과 동일. 총 프리팹 원점을 여기에 둔다(소총을 든 캡처는 `CharacterRoster.PoseLowReady` + `RenderPosed` 참고).
- `HeadSocket`: 머리 본 자식, 위치 = 머리 꼭대기(정수리 y 1.8). 헬멧·완장·머리띠용.
- `BackSocket`: `UpperChest` 자식, 가슴에서 뒤로 0.12 m. 백팩용.
- `EyePoint`: 머리 본 자식, y = 1.6 고정, 정면 +Z.
- `LeftHandGrip`은 총 프리팹(총기 트랙) 몫이다. 여기서는 만들지 않았다.

## LOD·컬 (ADR-227)
- 형상 LOD는 Unity 내장 Mesh LOD(자동). `Renderer.meshLodSelectionBias = StartLod`가 프리팹에 이미 들어 있다.
- `LODGroup`은 단일 단계, 100 m 컬(`QualitySettings.lodBias` 1 기준, 2면 200 m). 4 m 삼각형 7.0~14.5k.
- 애니: LOD1 이상에서 Additive·발 IK·손가락을 끄려면 거리(또는 `LODGroup`의 `GetLODs`가 아니라 카메라 거리)로 판단한다. Mesh LOD는 현재 몇 단계인지 API가 없다(추정).

## 기존 `ArmorVisual`·`BackpackVisual`과의 호환 (확인한 것)
- 둘 다 본 **이름**으로 마운트를 찾는다(`"Head"`, `"Torso"/"Chest"/"Abdomen"`). 프리팹에 `Head`(UBC는 원래 본), `Chest` 별칭 Transform을 넣어 붙는다. 헬멧은 머리 본, 조끼·배낭은 `Chest` 별칭(= UpperChest) 아래에 붙어 애니를 따라간다.
- 헬멧 오프셋 상수(머리 본 +0.07 m)는 Swat 기준이다. 별칭은 정수리 −0.16 m 지점에 있어 Mixamo 10종은 눈이 가려지지 않는다. **UBC 2종은 본 이름이 이미 `Head`라 별칭을 못 넣어 브림이 y 1.67 m로 메시 눈을 덮는다.** 고치려면 `ArmorVisual.Init`이 `CharacterDef.Sockets.HeadSocket − 0.16 m`를 쓰게 3줄 수정(애니 트랙, master 중재 대상).
- 검증: `CharacterRosterTests.WornGear_MountsOnRosterBones_AtHeadChestBack`(헬멧 본 아래, 조끼 y 1.08~1.46, 배낭 z −0.20 = 가슴 뒤).

## 애니 트랙이 알아둘 것
- 클립은 **Humanoid 공유**(ADR-186). 기준 Avatar는 Swat이지만 각 모델이 자기 Avatar를 가진다. 로스터 12종 모두 `isHuman`.
- 본 이름이 모델마다 다르다(`mixamorig:`, `mixamorig12:` 등, UBC는 UE식). 이름 문자열 대신 `Animator.GetBoneTransform` 또는 `Sockets`.
- T-포즈로 저장돼 있다. 정지 포즈 캡처가 필요하면 `CharacterRoster.PoseLowReady(def)`(월드 회전으로 팔만 굽힘, 클립 아님).
- 팔 길이는 키의 0.25~0.31배(소총 Warden 0.75 m = 팔의 1.33배)라 왼손 IK가 짧은 모델이 있다(UBC 0.27, James 0.25).
- 머리 비율(턱~정수리/키): Mixamo 0.137~0.158(머리카락 포함), UBC 0.128, Swat 0.142(헬멧 포함).

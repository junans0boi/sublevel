# 캐릭터 API (애니 트랙용, 2026-11-16, `track/character` 기준)

## 프리팹·복원
- 프리팹 `Assets/_Project/Prefabs/Roster/Roster_<Id>.prefab`. Id 15종: `MixamoSwat` `MixamoSteve`(철모 군복) `MixamoSwatGuy`(위장 전술복) `MixamoGasMask`(방독면) `MixamoBrian` `MixamoLouise` `MixamoJames` `MixamoMegan` `MixamoKate` `MixamoJosh` `MixamoAdam` `MixamoMartha` `MixamoAlex` `UbcMale` `UbcFemale`.
- FBX·원본 텍스처는 git 무시. 새 클론: `tools/unity.sh run CharacterImport.SyncStaging`(FBX 복원·Humanoid·Mesh LOD) → 프리팹은 이미 커밋돼 있어 `CharacterRoster.Build`는 모델·재질을 바꿀 때만 다시 돌린다. 재생성한 결과(프리팹·재질·`Prefabs/Roster/Textures/*.png`)는 커밋 대상.
- 구조: 루트(`CharacterDef`, `LODGroup`) → `Model`(FBX 인스턴스, **Animator Humanoid**, Avatar 모델별 생성). 루트가 +Z 정면, 발이 y=0. 루트 모션 끔, `cullingMode = CullUpdateTransforms`.

## `Game.Gameplay.CharacterDef` (루트에 있음, 읽기 전용으로 쓴다)
| 필드 | 값·의미 |
|---|---|
| `Height` / `EyeHeight` | 1.8 / **1.6** (캡슐 눈 높이. 메시 눈은 약 1.69라 1인칭 카메라는 메시 눈보다 낮다) |
| `Sockets` (`HumanoidSockets`) | 본 Transform 18개 + `WeaponSocket` `HeadSocket` `BackSocket` `EyePoint` |
| `Animator`, `Renderers` | Humanoid Animator, SkinnedMeshRenderer 전부 |
| `Variants` / `ApplyVariant(i)` | 외형 변형. Swat·UBC 9개, 민간 모델 4개(Base + 3), 군복 3종은 Base 1개. `Variants[i].Name` 참고 |
| `HandSizeScale` | 손바닥~중지 첫마디 거리 / 0.09 m |
| `WeaponSocketOffsetPos/Euler` | 지금은 0. 총마다 손 파지 보정이 필요하면 애니 트랙이 채운다 |
| `StartLod` | 빌더가 고른 첫 Mesh LOD(원본 LOD0가 15k 초과인 모델은 1~3) |
| `HasHelmetMask` | Swat·군복 3종 true: 모델에 헬멧(또는 방독면)이 이미 있어 `ArmorVisual` 헬멧과 겹친다 |

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

## 장비 비주얼 (헬멧·조끼·배낭, 절차적 로우폴리)
- 모델은 `WornGearMeshes`(Gameplay)가 코드로 만든다: 헬멧(3단계: 가벼운 철모 / 전술 헬멧 / 풀 커버), 조끼(곡면 앞·뒤 판+어깨끈, 2단계부터 탄창 파우치, 3단계 측면 판·어깨 패드), 배낭(둥근 본체+뚜껑·끈·앞/옆 포켓, 단계마다 커지고 3단계는 매트 롤). 색은 군용 톤(1 카키, 2 올리브, 3 검정).
- 한 조각 = 서브메시 1개 + 팔레트 재질 1개(`WornGearMeshes.Palette(tier)`, 단계별 공유) → 캐릭터당 장비 드로우콜 3. 지면 아이템(`LootMeshes`)은 같은 모양을 3 서브메시로 쓴다.
- 부착: `ArmorVisual`·`BackpackVisual`이 `WornGearMeshes.TryFit`으로 `CharacterDef.Sockets`(머리·UpperChest)에 붙인다. 머리 높이(`HeadSocket−Head`)로 헬멧 크기, 어깨 너비로 조끼·배낭 크기를 0.85~1.2배로 맞춘다. 본 이름 별칭 `Head`/`Chest`는 더 이상 필요 없다(프리팹에 남아 있어도 무해). `CharacterDef`가 없는 기존 Swat은 손으로 맞춘 상수 경로를 쓴다.
- 헬멧 챙은 정수리 −0.08 m(눈 위), 조끼 중심은 UpperChest, 배낭 바닥은 UpperChest −0.30 m·등 뒤 0.115 m. UBC 헬멧이 눈을 덮던 문제도 해결(소켓 기준이라 본 이름과 무관).
- 검증: `WornGearMeshesTests`(삼각형 ≤1,500/단계, 크기, 색), `CharacterRosterTests.WornGear_MountsOnRosterBones_AtHeadChestBack_AllTiers`(15모델 x 3단계 위치·스케일).

## 애니 트랙이 알아둘 것
- 클립은 **Humanoid 공유**(ADR-186). 기준 Avatar는 Swat이지만 각 모델이 자기 Avatar를 가진다. 로스터 15종 모두 `isHuman`.
- 본 이름이 모델마다 다르다(`mixamorig:`, `mixamorig12:` 등, UBC는 UE식). 이름 문자열 대신 `Animator.GetBoneTransform` 또는 `Sockets`.
- T-포즈로 저장돼 있다. 정지 포즈 캡처가 필요하면 `CharacterRoster.PoseLowReady(def)`(월드 회전으로 팔만 굽힘, 클립 아님).
- 팔 길이는 키의 0.25~0.31배(소총 Warden 0.75 m = 팔의 1.33배)라 왼손 IK가 짧은 모델이 있다(UBC 0.27, James 0.25).
- 머리 비율(턱~정수리/키): Mixamo 0.137~0.158(머리카락 포함), UBC 0.128, Swat 0.142(헬멧 포함).

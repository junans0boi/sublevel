# PlayerAppearance API (UI → 애니·캐릭터 트랙, ADR-247, 2026-11-16, `track/ui`)

로비에 서는 캐릭터와 게임에서 플레이하는 캐릭터는 같아야 한다. 외형은 하나의 값 `PlayerAppearance`로 저장·전달한다.

## 값 (`Game.Core.PlayerAppearance`, 순수 struct, `Assets/_Project/Scripts/Core/PlayerAppearance.cs`)
| 필드 | 의미 |
|---|---|
| `ModelId` | 로스터 15종 중 하나. 프리팹 `Assets/_Project/Prefabs/Roster/Roster_<ModelId>.prefab`. 목록 `PlayerAppearance.ModelIds`(MixamoSwat, MixamoSteve, MixamoSwatGuy, MixamoGasMask, MixamoBrian, MixamoLouise, MixamoJames, MixamoMegan, MixamoKate, MixamoJosh, MixamoAdam, MixamoMartha, MixamoAlex, UbcMale, UbcFemale). 모르는 id는 기본 모델(`MixamoSwat`)로 읽힌다 |
| `Variant` | 그 모델의 색 변형 index. **`CharacterDef.ApplyVariant(Variant)`에 그대로 넣는다**(내부에서 `% Variants.Length`) — 모델마다 변형 수가 다르다(Swat·UBC 9, 민간 4, 군복 3종 1) |
| `Helmet`, `Vest`, `Pack` | 몸에 보이는 장비 단계 0(없음)~3. `ArmorVisual.ShowTiers(helmet, vest)`, `BackpackVisual.Show(pack)`에 넣는다. 매치 중 실제 착용(루팅)이 이 값을 덮어쓴다: 시작 값일 뿐 |
| `Serialize()` / `Parse(text)` | `"MixamoSwat:3:0:0:0"` (model:variant:helmet:vest:pack). 잘못된 입력은 항상 유효한 값(기본 모델)으로 읽힌다 |
| `Normalized()`, `Equals` | 정규화(알려진 모델, 변형 ≥ 0, 장비 0..3), 값 비교 |

## 저장·읽기 (`Game.Gameplay.PlayerAppearanceStore`)
```csharp
var look = PlayerAppearanceStore.Load();      // 저장된 값, 없으면 PlayerAppearance.Default (옛 ui.char.index 키도 한 번 읽음)
PlayerAppearanceStore.Save(look);             // PlayerPrefs "player.appearance"
```
- **스폰은 `PlayerAppearanceStore.Load()`를 호출해 로컬 플레이어의 프리팹·`ApplyVariant`·장비를 정하면 된다.** 로비 3D(`LobbyStage`)도 같은 값을 읽는다. 멀티에서는 각 클라이언트가 자기 값을 서버로 보내고(문자열 `Serialize()`), 서버가 다른 플레이어에게 복제한다.
- 봇은 이 값을 쓰지 않는다(애니 트랙이 로스터를 고름).

## 파티 동기화 형식
`Game.Core.LobbyMember`에 `PlayerAppearance Appearance` 필드가 있다(`PartyLobby.Join(id, name, appearance)`, `SetAppearance(id, appearance)`). 네트워크 파티가 생기면 각 파티원이 `Serialize()` 문자열을 보내고 받는 쪽이 `Parse`해서 `SetAppearance`로 넣는다. 로비 3D는 `Members[i].Appearance`대로 자리마다 모델을 세운다. 지금은 네트워크 파티가 없어 로컬 테스트 데이터로만 채운다.

## 외형 화면
메인 메뉴 탭 02 "외형": 로스터 15종 전부 선택 가능, 변형·장비 선택, 3D 로비 캐릭터가 즉시 바뀌고 `PlayerAppearanceStore.Save`로 저장된다.

## 검증
EditMode `PlayerAppearanceTests`(저장·로드·형식·기본값·파티원 필드). 같은 값으로 찍은 로비 캡처와 인게임 캡처는 스폰이 이 API를 읽은 뒤 확인한다(스폰 쪽은 애니 트랙 소유).

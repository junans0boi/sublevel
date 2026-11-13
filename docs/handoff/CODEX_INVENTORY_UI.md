# Codex 세션용 작업 지시 — 인벤토리·HUD UI (ADR-054)

이 문서는 Codex 세션에 그대로 전달하는 지시서다. 상위 맥락은 `CLAUDE.md`와 `docs/handoff/README.md`.

## 역할 분담
| 담당 | 하는 일 |
|---|---|
| **Codex (당신)** | uGUI 화면 구현: 인벤토리 4구역, 끌어다 놓기, 아이콘 표시, HUD 탄약·장비 표시, 화면 테스트 |
| **마스터(Claude)** | 서버 데이터·RPC(구경별 탄약, 무기 슬롯 교체, 바닥→칸 장착), 아이콘 그림 렌더, Unity 실행·빌드·화면 검수, 브랜치 통합 |

## 절대 규칙
- **Unity를 실행하지 않는다**(`tools/unity.sh`, 에디터, 빌드 전부 금지). 마스터가 같은 프로젝트에서 Unity를 쓰는 중이라 두 개를 동시에 못 연다. 컴파일은 마스터가 한다 → 코드를 꼼꼼히 쓰고, 쓰는 API는 반드시 소스를 읽어 확인한다(C# 9, Unity 6000.3).
- **별도 브랜치에서 작업**: `git switch -c codex/inventory-ui` (또는 git worktree). main에 직접 커밋·push 금지. 끝나면 커밋하고 브랜치 이름과 해시를 보고한다.
- **수정 가능 파일**: `Assets/_Project/Scripts/Gameplay/{InventoryScreen,Hud}.cs`, 새 `Assets/_Project/Scripts/Gameplay/Inventory*.cs`, `Assets/_Project/Tests/PlayMode/{InventoryTests,HudTests}.cs`, 새 `Tests/PlayMode/InventoryDragTests.cs`. **그 밖의 파일(Core/, Net/, Editor/, 데이터 JSON, 프리팹, 씬)은 고치지 않는다.** 필요한 변경은 보고서의 "마스터에게 요청"에 적는다.
- 게임 안 글자는 **전부 한글**. 폰트는 `UiKit.Font`(Noto Sans KR). 코드·주석·커밋은 영어. 주석은 주변 코드처럼 짧은 "why" 위주.
- 외부 에셋 다운로드 금지, 런타임 `Shader.Find`·머티리얼 생성 금지(빌드에서 셰이더가 빠짐). 새 `.meta` 파일을 만들지 않는다.
- 프레임당 메모리 할당 금지: 글자·행은 값이 바뀔 때만 갱신, 행은 풀링. 테스트에서 `WaitForEndOfFrame` 금지(batchmode에서 멈춤, `yield return null` 사용).
- 사용자에게 질문하지 않는다. 모호하면 합리적으로 정하고 보고서에 가정으로 적는다.

## 읽는 순서
1. `CLAUDE.md` → `docs/handoff/README.md`(역할·성향) → `docs/PROGRESS.md`(현재 위치)
2. `docs/UI_SPEC.md` §1·§2·§6(HUD·인벤토리·가독성 기준) → `docs/GAME_FEEL_SPEC.md`는 건너뛴다
3. `docs/DECISIONS.md`의 **ADR-054(이번 작업의 결정)**, ADR-051·053(UI 방식·서버 권위)
4. 참고 이미지: 사용자가 준 배틀그라운드 인벤토리 화면 — "바닥 / 보유 / 장구류 / 총기" 4구역 구성과 끌어다 놓기 동작만 참고하고 **그대로 베끼지 않는다**(사람 실루엣 없음, 이름·그림·배치 모방 금지).
5. 코드: `Scripts/Gameplay/UiKit.cs`, `UiFocus.cs`, `InventoryScreen.cs`(현재 3열 클릭 방식), `Hud.cs`, `PlayerController.cs`(Screens 오브젝트에 화면 설치), `Scripts/Net/Combatant.cs`(서버 요청 `Cmd*`와 SyncVar), `Scripts/Core/{Loadout,Loot,WeaponDef}.cs`, `Scripts/Net/LootItem.cs`, 테스트 `Tests/PlayMode/{InventoryTests,HudTests}.cs`.

## 요구사항
### 1. 인벤토리(Tab) — 4구역
| 구역 | 내용 |
|---|---|
| **바닥(주변)** | `Combatant.PickupRange` 안의 `LootItem` 목록(`LootItem.All`). 아이콘 + 이름 + 수량. 비었으면 "주변에 아이템 없음" |
| **가방(보유)** | 구경별 탄약(9mm, 5.56mm, 12게이지, 7.62mm — 0개는 흐리게), 구급상자, 수류탄(있으면), 이후 추가될 소모품 자리. 칸마다 아이콘 + 수량 |
| **장구류** | 헬멧·조끼 카드(단계, 내구도 막대). 비어 있으면 빈 칸 표시 |
| **총기** | 슬롯 1·2 무기 카드: 무기 그림, 이름, 장전 탄수/예비 탄수, **쓰는 구경 배지**(그 구경의 예비 탄약이 곧 표시되는 수), 활성 슬롯 강조(`UiKit.Accent`) |
- 레이아웃과 시각은 **사람들이 편한 선에서 우리만의 특색**을 낸다: 어두운 산업 시설 톤, 절제된 색, 둥근 장난감 느낌 금지. 1920x1080 기준, 1280x720·2560x1440에서도 깨지지 않게(CanvasScaler 기준 해상도 1920x1080). 본문 글자 ≥ 18px 상당. 색만으로 구분하지 않는다.
- 아이템 정보: 위에 마우스를 올리면 작은 설명(이름, 종류, 수량, 구경 등).

### 2. 끌어다 놓기(핵심)
| 끌기 | 놓기 | 동작 |
|---|---|---|
| 바닥 아이템 | 가방 영역 | 줍기 |
| 바닥 무기 | 총기 슬롯 1·2 | 그 슬롯에 장착(있던 무기는 바닥으로) |
| 바닥 방어구 | 장구류 칸 | 장착(더 나쁜 것은 거부 표시) |
| 가방 아이템·총기·장구류 | 바닥 영역 또는 패널 바깥 | 버리기 |
| 총기 슬롯 | 다른 총기 슬롯 | 슬롯 교체 |
- 끄는 동안 마우스에 아이콘이 따라오고, 놓을 수 있는 칸은 강조·못 놓는 칸은 흐리게. 놓기 전에 Esc나 우클릭으로 취소. 화면이 닫히면 진행 중인 끌기를 취소한다.
- 클릭 동작도 유지: 바닥 항목 더블클릭 = 줍기, 가방 구급상자 더블클릭 = 사용, 무기 카드 클릭 = 활성 슬롯 전환(`CmdSelectSlot`), 우클릭 = 빠른 버리기. 기존 버튼(사용·버리기·장착)은 없애도 되지만 동작은 모두 남는다.
- 구현: `UnityEngine.EventSystems`의 `IBeginDragHandler/IDragHandler/IEndDragHandler/IDropHandler`. `UiKit.EnsureEventSystem()`이 이미 입력 모듈을 만든다. 드래그 아이콘은 최상단 캔버스에.

### 3. 서버와의 계약 (화면은 요청만 보낸다)
이미 있는 것(`Combatant`): `CmdPickup(NetworkObject)`, `CmdSelectSlot(int)`, `CmdUseMedkit()`, `CmdDropWeapon(int slot)`, `CmdDropAmmo(int ammoType)`, `CmdDropMedkit()`. 방어구 브랜치가 합쳐지면 `CmdDropArmor(int slot)`과 `CombatantGear`(`TierOf/DurabilityOf/MaxDurabilityOf/Has/FractionOf`)가 생긴다.
**마스터가 추가할 것**(아직 없음 — 당신은 이 이름대로 호출하고, 없으면 `InventoryBackend.cs`에 컴파일되는 임시 구현을 두고 `// [MASTER]`로 표시):
- `Combatant.CmdSwapWeaponSlots()` — 슬롯 0과 1 교체
- `Combatant.CmdPickupToSlot(NetworkObject loot, int slot)` — 바닥 무기를 지정 슬롯에 장착
- `AmmoType`이 4종(`Pistol9 = 0, Rifle556 = 1, Shotgun12 = 2, Rifle762 = 3` 예정)으로 바뀜: 이름이 확정될 때까지 `AmmoType`을 직접 쓰지 말고 `InventoryBackend`의 한 곳에서만 변환한다.
- 아이콘: `ItemIcons.Get(string key)` → `Sprite`(없으면 null; null이면 이름 첫 글자를 얹은 색 판으로 대체하는 폴백을 당신이 만든다). 키: `weapon_1`~`weapon_4`, `ammo_9mm`, `ammo_556`, `ammo_762`, `ammo_12ga`, `medkit`, `helmet_1`~`helmet_3`, `vest_1`~`vest_3`, `grenade`. 구현은 마스터가 하므로 당신은 이 시그니처를 가진 얇은 `ItemIcons` 정적 클래스를 `Scripts/Gameplay/ItemIcons.cs`에 폴백만 돌려주도록 만들어 둔다(마스터가 본체를 채운다).
- 화면이 읽는 값은 모두 `Combatant`의 SyncVar·읽기 전용 속성과 `LootItem`의 필드뿐. 서버 로직을 클라이언트에 복제하지 않는다.

### 4. HUD 보강(작게)
- 우하단 무기 패널의 탄약 줄에 **구경 이름**(예: "5.56mm")과 무기 그림을 표시. 좌하단에 방어구 상태(헬멧·조끼 단계와 내구도 막대)와 수류탄 수 자리(값이 없으면 숨김).
- 줍기 안내에 아이템 그림.
- 기존 `HudTests`는 계속 통과해야 한다(공개 읽기 전용 속성 이름 유지, 필요하면 추가).

### 5. 테스트(PlayMode, 씬 `TestBox`, `Tests/PlayMode/AimAndLeanTests.cs`의 ScriptedInput/LoadTestBox/TearDown 패턴)
- 끌어다 놓기는 이벤트 시스템을 거치지 않고 **같은 코드 경로를 부르는 공개 메서드**(`InventoryScreen.DropOnBag(source)` 등)로 검증: 바닥 아이템 → 가방 = 줍기(바닥에서 사라짐), 가방 → 바닥 = 새 `LootItem` 생성, 총기 교체, 불가능한 놓기는 아무 일도 없음.
- 화면이 닫히면 끌기 취소, 열려 있는 동안 플레이어 입력 차단(기존 동작) 유지.
- 기대값은 리터럴로(계산식 복사 금지). 서버 변경에 의존하는 테스트는 마스터 함수가 없을 때 `[Ignore("needs master server contract")]`로 표시.

## 끝났을 때 보고(한국어, 300단어 이내)
1. 브랜치 이름·커밋 해시, 바꾼/만든 파일 목록
2. 공개 API(`InventoryScreen`, `InventoryBackend`, `ItemIcons`)
3. **마스터에게 요청**: 필요한 서버 함수·데이터·아이콘 키, 연결해야 할 곳
4. 확인하지 못한 것(컴파일 불확실한 부분 포함)
5. 직접 확인 목록: 끌기가 손에 맞는지, 놓을 칸 강조가 보이는지 등

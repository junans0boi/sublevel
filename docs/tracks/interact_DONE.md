# interact — DONE (2026-11-17)

## 한 일
1. **시선 기반 줍기 후보 (ADR-276)**: 같은 층(−1.0~+1.6 m)·시선 열림(눈→아이템 레이캐스트 2점)·시선 콘(60°, 발 앞 0.9 m 제외)·3 m. 서버가 거리(+1.5 m 여유)·층·시선을 서버 눈 위치에서 재검증(`CmdPickup`, `CmdPickupToSlot`, `CmdAttachFromGround`). 규칙 값은 rules.json `Interact`. HUD 프롬프트·목록·`NearbyLoot`·인벤토리 주변 목록이 같은 후보. 선택은 시선 중심 우선(휠 후 1.5 s 고정).
2. **가시성 (ADR-277)**: World 씬 1262점 전수 측정 → 배치 문제 없음(박힘 0·안 보임 0, 99.6% 양호). 원인은 표시 → 종류색 바닥 링 `LootMarker`(거리에 따라 커짐, F 대상은 맥동). 숨김 해제가 총 모델 위에 상자를 되살리던 버그 수정.
3. **F 한 번 = 대상 하나 (ADR-278)**: 시선 중심에 더 가까운 쪽(아이템에 12° 가산). 문이 이기면 프롬프트·목록도 숨김.

## 변경 파일
- Core: `Scripts/Core/Interact.cs`(신규), `GameRules.cs`(+1)
- Net: `PickupSight.cs`(신규), `Combatant.Guard.cs`, `CombatantOptics.cs`, `DoorSystem.cs`(+`Bearing`), `GameData.cs`(+1)
- Gameplay: `LootMarker.cs`(신규), `LootView.cs`, `PickupList.cs`, `PlayerController.cs`, `InventoryScreen.cs`
- 데이터: `Data/Resources/GameData/rules.json`(`Interact`)
- 에디터: `Editor/LootVisibilityProbe.cs`
- 테스트: `EditMode/InteractRulesTests`(8), `EditMode/PickupSightTests`(7), `PlayMode/InteractLootPlayTests`(2)
- 문서: `DECISIONS.md` ADR-276~278, `BACKLOG.md` 3줄, `tracks/interact_REPORT.md`, `tracks/interact_loot_visibility.txt`, `tracks/interact_captures/`

## 수치
- 루팅 가시율: 수정 전·후 동일(배치 무수정) 1257/1262 양호, 5점 부분 가림, 박힘·완전 가림 0. 표시: 아이템 8 m 화면 폭 29 px → 링 포함 73 px.
- EditMode 1424개 중 1407 통과, 17 실패는 전부 `CharacterAnimatorTests`·`CharacterRosterTests`(이 워크트리에 Mixamo Roster FBX가 없어 Humanoid 아바타 null; 내 변경과 무관, main 워크트리에서 확인 필요).
- PlayMode 75개 중 72 통과. 실패 3: `DoorPlayTests.MapDoors_SitInsideTheirOpenings`(맵 문 4개, 맵 세션 소관), `InventoryFlowTests.LiveCharacter...`·`SlotsAndProneTests.Prone...`(Avatar null, 같은 FBX 부재).
- 내 신규 테스트 전부 통과(EditMode 15, PlayMode 2 + 기존 `PickupListPlayTests` 3).

## 캡처 (`docs/tracks/interact_captures/`)
- `world_06_W_Weapon.png`: 상판 위 총, 주황 링(F 대상)
- `world_01_A_Ammo.png`: 나무 바닥의 탄약상자, 노란 링
- `world_04_G_Helmet.png`: 옥상 헬멧, 파란 링
- `interact_floor_scenario.png`: 게임 화면(HUD 포함) — 방 안 아이템 1개만 프롬프트, 벽 뒤·2층·먼 방 아이템은 후보 아님

## 준환이 직접 확인할 것
1. 건물 1층에서 위 2층·벽 뒤·먼 방 아이템이 프롬프트에 안 뜨는가 (뜨던 것이 사라지고, 보이는 아이템은 계속 뜨는가).
2. 아이템 아래 색 링이 방 건너편에서 보이는가, 지나치게 튀지는 않는가(색·크기 조절은 `LootMarker` 상수).
3. 문 옆 아이템을 보고 F → 줍기만, 문을 보고 F → 문만. 어색한 각도(문 가장자리) 있는지.
4. 선반 높이(1.1~1.4 m) 아이템이 줍히는가.

## 남은 것·주의
- 생성물 커밋 안 함. 프리팹 변경 없음(마커는 코드로 추가) — 병합 후 재생성 불필요.
- 서버는 시선 콘을 검증하지 않음(층·벽·거리는 막음). 봇 줍기는 거리만(BACKLOG).
- Library는 메인에서 `cp -c`로 복제해 사용(.gitignore). Unity가 Mixamo meta를 삭제하는 문제는 실행 뒤 `git restore`로 복원.

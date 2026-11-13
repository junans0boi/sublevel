# Codex 세션용 작업 지시 — 인게임 HUD 개편 v2 (ADR-057)

상위 맥락: `CLAUDE.md`, `docs/handoff/README.md`. 지난 작업 지시서 `docs/handoff/CODEX_INVENTORY_UI.md`의 **절대 규칙 전부가 그대로 적용**된다(Unity 실행 금지, 별도 브랜치, 허용 파일만 수정, 한글 표시, 런타임 Shader.Find 금지, .meta 생성 금지, 프레임당 할당 금지, WaitForEndOfFrame 금지, 질문 금지).

## 브랜치
`main`에서 `git switch -c codex/hud-v2` (또는 git worktree — 마스터가 같은 폴더에서 Unity를 쓰는 중이면 **worktree를 권장**: `git worktree add ../sublevel-codex -b codex/hud-v2 main`). main에 직접 커밋·push 금지.

## 수정 가능 파일
`Assets/_Project/Scripts/Gameplay/Hud*.cs`(Hud.cs를 여러 파일로 나눠도 됨), 새 `Scripts/Gameplay/{Minimap,MinimapView,WeaponStrip,RegionCard,KillFeed}.cs`(KillFeed.cs는 기존 파일 — 위치·모양만 바꾼다), `Scripts/Gameplay/UiKit.cs`(새 공용 부품 추가만, 기존 함수 시그니처 유지), 테스트 `Tests/PlayMode/{HudTests,HudLayoutTests}.cs`. 그 밖의 파일은 수정 금지 → 보고서 "마스터에게 요청".

## 읽는 순서
1. `docs/DECISIONS.md` **ADR-057**(이번 결정), ADR-055(투입·스폰 보호), ADR-056(조준경), ADR-054(인벤토리)
2. `docs/UI_SPEC.md` §1·§6, `docs/PUBG_GAP.md` 2장 UI 행
3. 코드: `Scripts/Gameplay/{Hud,KillFeed,ResultScreen,UiKit,UiFocus,InventoryBackend,ItemIcons,PlayerController,WorldInfo}.cs`, `Scripts/Net/{Combatant,MatchState,CombatantGear,ThrowableUser}.cs`

## 레이아웃(위치는 익숙하게, 모양은 우리 것)
| 위치 | 요소 |
|---|---|
| 상단 중앙 | 방위 띠(이미 있음 — 모양만 "현장 단말기" 톤으로: 얇은 선, 고정폭 숫자, 북/동/남/서 한글 + 15° 눈금) |
| 우상단 | 생존 수·내 처치 수(작은 카드 2개), 그 아래 킬 로그(최근 5줄, 6초 뒤 흐려짐) — 현재 좌상단 킬 로그를 옮긴다 |
| 좌상단 | **지역 상태 카드(우리 고유)**: 현재 지역 이름(`WorldInfo`의 지역 한글 이름, 없으면 숨김), 상태(안전 — M3 전까지 고정), 남은 시간 자리, 가장 가까운 통로 방향 화살표(자리만) |
| 좌하단 | 솔로: 내 상태 묶음(자세 아이콘 서기/앉기, 방어구 2칸 단계·내구도, 스폰 보호 남은 초). 팀원 목록 자리는 비워 둔다(팀 모드 이후) |
| 하단 중앙 | 체력 막대(얇고 넓게, 회복 중 표시, 저체력 깜빡임 대신 테두리), 그 위에 현재 무기 이름 · 발사 모드 · `장전/예비` 큰 고정폭 숫자 · 구경 배지 |
| 우하단 | **미니맵**(정사각 220px, 모서리 꺾쇠, 북쪽 고정·내 방향 화살표 회전, 지역 경계선, 엘리베이터 표시, 지역 이름) + 그 위에 **무기 슬롯 띠 5칸**: (1)(2) 주무기 그림·조준경 표시, (3) 보조무기, (4) 근접(비어 있음 표시), (5) 투척(수류탄 그림·개수). 선택된 칸 강조 |
| 중앙 | 조준선·히트마커(기존 유지), 줍기 안내(아이콘 포함, 기존) |
| 조준(배율) | 조준경 오버레이는 **마스터 쪽 ScopeView가 그린다** — HUD는 배율 조준 중 조준선을 숨기기만 한다(`ScopeView.Active` 정적 bool을 읽음; 없으면 [MASTER] 임시 false) |
- 어두운 산업 톤, 앰버 강조 1색, 둥근 장난감 느낌 금지, 참조 게임 아이콘·배치 모양 모방 금지. 1280x720 / 1920x1080 / 2560x1440 모두 겹침 없이(CanvasScaler 1920x1080). 본문 ≥ 18px 상당. 색만으로 구분하지 않는다.
- 인벤토리·메뉴·결과 화면이 열리면 HUD는 겹치지 않게 숨기거나 흐린다(지난번 인벤토리 제목과 생존 카드가 겹쳤음).

## 서버·데이터 계약(마스터가 만든다 — 없으면 한 곳에 `// [MASTER]` 임시 구현)
- 무기 슬롯 5칸: `Combatant.SlotCount`(=5), `Combatant.WeaponInSlot(int)`, `MagInSlot(int)`, `ActiveSlot.Value`, `Combatant.OpticInSlot(int)`(조준경 id, 0=없음), `AttachmentNames.Of(int)`, 발사 모드 `Combatant.FireModeOf(int)`(이름 `"단발"/"연사"`)
- 투척: `ThrowableUser.Local?.Grenades.Value`
- 스폰 보호: `Combatant.SpawnProtectedUntil`(서버 시각 → 남은 초 계산 함수 `Combatant.SpawnProtectionLeft`)
- 미니맵 그림: `MapImage.Get()` → `Texture2D`(없으면 null → 격자 + 지역 경계만 그린다), 좌표 변환 `MapImage.WorldToUv(Vector3)`; 엘리베이터·지역·통로는 `WorldInfo.Current`
- 지역 상태: `ZoneStatus.Current`(M3 전에는 없음 → "안전" 고정)
- 이 이름들을 **HudBackend.cs 한 파일에서만** 읽고, 없는 것은 그 파일에 임시 구현.

## 테스트(PlayMode, 씬 `TestBox` — 미니맵은 `World` 씬이 있으면 `World`에서)
레이아웃 겹침 검사(각 영역 RectTransform이 서로 겹치지 않음, 3개 해상도), 슬롯 띠가 선택 슬롯을 따라감, 킬 로그가 우상단에 쌓임, 미니맵 화살표 회전(플레이어 Yaw 90° → 화살표 90°), 인벤토리가 열리면 HUD가 숨음. 기대값은 리터럴.

## 끝났을 때 보고(한국어, 300단어 이내)
브랜치·해시, 파일, 공개 API, 마스터에게 요청(계약 이름 그대로인지), 확인하지 못한 것, 직접 확인 목록.

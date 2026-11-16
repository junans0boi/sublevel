# UI(비인게임) 트랙 DONE (2026-11-16, `track/ui`)

## 한 것
- **로비 모델**(ADR-245): `Core/PartyLobby`·`AssemblyRules` + `PartyLobbyTests` 9건. EditMode 전체 1230/1230 통과(Unity).
- **새 로비 화면** `Gameplay/LobbyPanel`: 상단 탭(플레이·훈련장·커스텀 모드·설정·크레딧·종료), 파티 슬롯(솔로 1칸/스쿼드 4칸, 중앙 정렬), 조작반(모드 스위치·LED·시점 TPP/FPP·맵 카드·상태 버튼 1개와 사유 한 줄). 선택 상태는 색+✓ 병기. 솔로 시작은 기존 `MainMenu.StartGame` 경로.
- **MainMenu**: 메인 컬럼을 로비로 교체, 방 만들기·참가는 "커스텀 모드" 패널로 이동(`OpenHost/OpenJoin/Lobby` API와 기존 PlayMode 테스트 호환 유지, 코드 호출만 확인).
- **HUD 슬롯**(ADR-246): `HudHazardSlot`+`Core/HudHazardView`, `Hud.cs` **추가 3줄·기존 줄 수정 0**. 필드 목록 `docs/tracks/ui_HUD_SLOT.md`. PlayMode 테스트 1건(표시·숨김) 통과.
- **캡처**: `UiCaptureTests`(Explicit) 로 로비 솔로·스쿼드·커스텀·설정·크레딧 × 1920×1080·2560×1440·1920×1200·3440×1440 = 20장(`Logs/uishots`, 대표 6장은 `docs/bible/img/ui/`). 2/2 통과. 직접 보고 슬롯 숨김 버그·"나 (나)" 중복·솔로 슬롯 쏠림을 고쳐 재캡처. 소리 없음(메뉴).
- 구현 매핑·해상도 정책 `docs/research/ui_impl_map.md`.

## 못 한 것
- **결과 화면 정합**(순위·오염·열쇠 요약, 층 표시기): 미착수. 오염 트랙 데이터 필요.
- **설정 탭 5→4**(키 설정을 컨트롤에 병합), **크레딧 안내 박스**(수정 여부 문구·캐릭터 크레딧은 character_DONE 반영 후), 상단 탭 바를 설정·크레딧에도 적용: 미착수(현재 중앙 패널 그대로).
- LoadingScreen·AssemblyScreen(UI)·CustomRoomScreen 본체·ModalService·외형·훈련장 진입 화면: 미착수(`AssemblyRules` 로직만 있음).
- 스쿼드 실제 시작·초대: 팀 규칙·S6 WP8 병합 후. 지금은 선택 가능하되 사유를 표시하고 시작 불가.
- 파티 슬롯 실루엣은 네모 자리표시(캐릭터 트랙 모델 연결 전). 시안 승강장 배경·애니메이션 없음. 16:10/21:9는 캡처로 레이아웃 붕괴 없음만 확인(수치 측정 없음).

## 확인 / 추정
- 확인: 위 테스트·캡처. 21:9에서 UI가 64px 안전 영역 안에 있음(눈으로).
- 추정: 집결 30명/30초/2분은 제안값(준환 확인). PlayMode 전체 회귀는 돌리지 않음(MainMenuTests 등이 메인 컬럼 버튼 이름에 기대는지 미확인 → master 병합 뒤 `!Long` 기준선에서 확인 요망).

## 병합 주의
- 소유 밖 수정: `MainMenu.cs`(Build·Load 잠금·ShowCustom·Esc), `Hud.cs`(+3줄). `ResultScreen`·`SettingsScreen`은 손대지 않음.

## 정정 (3D 로비, ADR-247)
- 로비 배경은 시안 SVG 그림이 아니라 **실제 3D**: `Editor/LobbyStageBuilder`가 `WorldBuilder.Elevators`와 같은 조립법·같은 `WorldMaterials`(Door·PaintedMetal·RustyMetal·ShaftLamp·DoorStripe·Concrete)로 승강기 칸+역 벽+홀 바닥을 MainMenu 씬에 만든다(칸은 4인이 서도록 게임보다 넓힘 4.4 m, 모든 치수·재질 코드가 게임 것을 따름). 캐릭터는 `Roster_*.prefab` 15종, 저총 자세+소총, 숨쉬기 흔들림.
- 모든 비로비 화면은 같은 3D를 뒤에 두고 시안의 dim(.84)을 적용. 시안 배경 PNG·장면 SVG는 삭제(아이콘 8종만 유지, ledger U-0001 갱신).
- **PlayerAppearance**(모델 id·변형·장비 단계) 저장·읽기·파티원 필드·테스트 7건, API 문서 `ui_APPEARANCE_API.md`. 로비 내 자리 = 저장된 값, 외형 탭에서 15종 전부 선택 가능(가짜 잠금 없음, 시안의 24칸 중 9칸은 그리지 않음 — 시안은 "기본 12 + 해금 12"이지만 실제 로스터가 15종이라), 변형은 모델의 `CharacterDef.Variants`, 헬멧·조끼·배낭 단계 선택, 고르는 즉시 3D 캐릭터에 반영·저장, 취소는 진입 때 값 복원, 마우스 드래그로 회전.
- 시안과 다른 점: 3D 배경이라 시안의 평면 장면과 모양이 다르다(요청에 따른 의도). 승강장 타일은 3D 위 오버레이(바닥 표식), 파티장·준비 배지는 캐릭터 발 위치를 투영해 따라간다. 빈 자리는 점선 타일+초대 버튼.
- **인게임 외형 일치 검증은 못 함**: 스폰이 `PlayerAppearanceStore.Load()`를 읽는 코드는 애니 트랙 몫(API 문서). 로비 쪽 값은 `docs/bible/img/ui/lobby_look.txt`(저장된 값 MixamoSwatGuy 변형 1)와 로비 캡처로 확인.
- PERF: 로비 프레임 시간 테스트는 배치 모드에서 0.2 ms로 나와 실제 렌더 비용을 못 믿는다(미측정으로 기록). 모델 15종이 MainMenu 씬에 참조돼 메모리는 병합 뒤 플레이어 빌드로 재측정해야 한다.
- 되돌려야 할 것: `WorldMaterials.Prepare`가 `Prefabs/World/Materials`를 건드려 커밋에서 제외함.

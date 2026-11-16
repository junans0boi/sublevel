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

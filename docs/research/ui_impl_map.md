# 비인게임 UI 구현 매핑 (S9, 2026-11-16, `track/ui`)

기준: `docs/research/ui_mockups/adopted.html`(로비 ② 승강기 조작반 + 스타일 B 다크). 상태: ☐ 미착수 · ◐ 순수 로직만 · ✓ 완료.

| 시안 화면 | Unity 구현(대상) | 데이터/로직 | 상태 | 비고 |
|---|---|---|---|---|
| 스플래시·로딩 | `MainMenu` 불러오는 중 → 신규 `LoadingScreen`(로고+맵 팁+진행률) | — | ☐ | 층 표시기 없음 |
| 로비(솔로/스쿼드/매칭/파티원) | `LobbyScreen` **재작성**(방 만들기·IP 참가 UI 제거) | `Core/PartyLobby`(ADR-245) | ◐ | 상태 버튼 1개로 준비·시작·취소 통합 |
| 모드 선택 창 | `LobbyScreen` 모달 | `PartyLobby.SetMode/SetView` | ◐ | 파티원은 잠김+자물쇠 |
| 방 만들기·코드 참가(커스텀 모드) | 신규 `CustomRoomScreen`(기존 `LobbyNet` 호스트/참가 재사용) | `LobbyRules.TryParseAddress` | ☐ | 일반 플레이엔 방 목록 없음 |
| 집결 구역 | 신규 `AssemblyScreen` | `AssemblyRules`(최소 30/30초/최대 2분 = 제안) | ◐ | Deployment(문 3초·보호 6초) 앞 단계 |
| 설정 | `SettingsScreen`+`SettingsCatalog`(탭 5→4, 키는 조작 탭) | `GameSettings`, `BindAction/KeyBinds` | ☐ | `SettingsScreen`은 소유 밖 → master 확인 |
| 크레딧 | `CreditsScreen`(그대로 8건 + 안내 박스) | `Credits.Entries`(CC BY 전용) | ☐ | 캐릭터 크레딧은 character_DONE 후 |
| 결과 | `ResultScreen` 확장(순위·오염·열쇠 요약, 층 표시기) | 서버 결과 | ☐ | `ResultScreen` 소유 밖 |
| 훈련장 진입 | `MainMenu.StartTraining` 연결 | — | ☐ | |
| 인게임 HUD 최소(오염 경고·열쇠·칩) | `Hud` 신규 영역 1개(좌상 지역 카드 아래) | ADR-246 뷰 구조체 | ☐ | pollution 병합 후 연결 |
| 팝업(종료·오류·끊김) | 신규 `ModalService` | — | ☐ | |

## 소유 밖 파일
`MainMenu`·`SettingsScreen`·`ResultScreen`·`Hud`는 handoff 소유 목록에 없다. 로비 재작성은 `MainMenu`의 방 만들기/참가 버튼·`OpenHost/OpenJoin/StartLobbyMatch` 변경이 필요 → **master 승인 요청**(아래 SendMessage).

## 해상도 정책
1920×1080 Canvas Scaler 기준. 16:10 상하 여백(앵커 상·하단), 21:9 좌우 안전 영역 64px 안쪽, 배경만 확장. 캡처 세트: 1920×1080, 2560×1440, 1920×1200(16:10), 3440×1440(21:9) × 로비 4상태·설정·크레딧·결과.

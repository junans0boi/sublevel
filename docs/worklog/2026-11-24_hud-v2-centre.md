# HUD v2 하단 중앙: 활력 블록 + 차 계기판 재배치 (ADR-563)

- 날짜: 2026-11-24 / 워크트리 eloquent-ardinghelli-63b430 (미커밋)
- 기준: 준환 승인 목업 `docs/research/ui_mockups/hud_v2.html` (Version 2), 1920x1080 레이아웃 계약(마스터)

## 한 일
- `HudStatus`를 하단 중앙 **활력 블록**으로 다시 만듦(x 700–1220, 아래 36, 520x140, 검정 58%·모서리 6, 9-slice 둥근 사각형은 코드로 생성).
  - 칩: H 구급상자 n / J 집중 정제 n / 활력 음료 n (부스트 아이템은 ADR-099 오리지널 이름).
  - 줄(44): 헬멧·조끼 아이콘(ItemIcons `helmet_n`/`vest_n`) + 내구도 막대(30% 미만 빨강) + Lv, 탄약 상자, 효과 아이콘(숨 링은 물속만·25% 미만 빨강, 회복 + 는 부스트 1단계↑, 달리기는 3단계↑).
  - 부스트 4칸(20/40/30/10), 체력 막대 30(낮음 빨강 유지 + 피격 잔상 0.25 s 유지 후 감소 + 회복 중 초록 줄무늬·진행선).
  - 스폰 보호·화상·문 열림 문구는 칩 줄 오른쪽 끝.
- `Hud`: 옛 체력 패널·중앙 무기 패널 제거. 탄약은 활력 블록 줄 가운데. 탄약 상자는 확대 조준경·운전 중 숨김. 운전자는 무기 스택도 숨김(한 줄).
  - 테스트 API 유지: `HealthFill`, `LowHealthShown`, `AmmoText`, `AmmoTypeText`, `FireModeText`, `ActiveWeaponName`, `ActiveWeaponIconKey`(값만), `WeaponPanelVisible`(=탄약 상자), `HealingShown/HealProgress/HealingText`, `Status.*`. 새로 `DamageTrailFill`, `HealIncomingTo`, `Status.LungShown/BreathShown/HealEffectLit/RunEffectLit`.
- `ClusterLayout`: 회전계 = 분대 목록(36–336)과 활력 블록 사이, 활력 블록 왼쪽 끝 −32에 오른쪽 맞춤(1920: 368–668, 16:10은 축소). 속도계 = 활력 블록 +16부터 최대 200 폭(1920: 1236–1436). 변속등 = 활력 블록 위.
- 테스트: `VehicleClusterTests.Gauges_NeverCoverTheHud`를 계약 사각형(분대·활력·미니맵·구역)으로, 1920 C 스타일 정확 좌표 검사 추가. `HudLayoutTests` 영역 수 11→9. 캡처 `HudV2CaptureTests`(Explicit).

## 검증
- EditMode 1849/1849 통과.
- PlayMode 38/38: HudTests, HudLayoutTests, VehicleFuelTests, ScopeTests, HealingTests, HudV2CaptureTests(3).
- 캡처 `Logs/hud_v2/` 11장(분대/솔로 × 1920/1728, 피격 잔상, 구급상자 회복 중, 부스트 3단계, 잠수, 운전 1920/1728/2520). 목업과 대조: 배치·크기·색 일치. 첫 판정에서 방어구 아이콘이 작아 44x33으로 키움.
- 재현: `HUD_V2_SHOTS=Logs/hud_v2 tools/unity.sh test playmode Game.Tests.HudV2CaptureTests`

## 남은 것 / 주의
- 부스트 칸(20/40/30/10)과 `BoostRules` 단계(25씩)가 어긋남(ADR-563 (4)). 규칙을 칸에 맞출지 별도 결정.
- `SwimHud`의 화면 가운데 아래 숨 막대가 활력 블록 숨 링과 중복(파일 소유 밖이라 그대로 둠).
- 병합: sides 세션의 `BuildBottomSides`에 남은 `HudStatus.Build(root, …)` 두 줄은 지워야 컴파일됨(시그니처 변경). 병합 후 `HudLayoutTests` 영역 수는 10(구역 패널 추가).

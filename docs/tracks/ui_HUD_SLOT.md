# HUD 오염·열쇠·칩 슬롯 (UI → 오염 트랙, ADR-246)

Hud에는 독립 컴포넌트 `HudHazardSlot` 한 개만 추가했다(상단 중앙, 나침반 아래 96px). 오염 코드는 Hud 본체를 건드리지 말고 아래만 호출한다.

```csharp
Hud.Instance.Hazard.Set(new HudHazardView {
    Warn = HazardWarn.Warning,   // None 숨김 / Warning 경고 / Imminent 봉쇄 임박 / Sealed 봉쇄됨
    SecondsToClose = 65,         // 봉쇄까지 초, 모르면 -1
    Region = "해안 구역",         // 없으면 ""
    Keys = 1,                    // 보유 열쇠 수
    Chip = HudChip.Held });      // None / Held / Used
```
- 타입은 `Core/HudHazardView.cs`(순수 데이터). 값이 같으면 갱신하지 않으니 매 프레임 호출해도 된다.
- 표시: 한 줄 `[경고] 해안 구역 · 봉쇄까지 01:05` + `열쇠 ×1 · 칩 보유`. 색만으로 구분하지 않고 글자를 병기한다. 소리는 오염 트랙 담당.
- 값이 모두 비면 슬롯이 숨는다. 서버 권위 값을 클라이언트가 읽어 채운다(복제는 오염 트랙).
- 필요한 필드가 더 있으면 `HudHazardView`에 추가하되 UI 트랙에 알린다(병합 충돌 방지).
- Hud.cs 변경 줄 수: 필드 1 + 생성 1 + 프로퍼티 1 = 추가 3줄(기존 줄 수정 0).

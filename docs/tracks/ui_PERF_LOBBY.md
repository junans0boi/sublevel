# 로비 PERF 측정 시나리오 (플레이어 빌드, PERF_BUDGET A·B·C)

게이트: 다른 Unity·무거운 프로세스가 모두 멈춘 머신에서 mac 플레이어 빌드(IL2CPP)로 로비를 600프레임 이상 측정한다.

```bash
tools/unity.sh build mac     # Build/mac/Sublevel.app (master 순번)
Build/mac/Sublevel.app/Contents/MacOS/Sublevel -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 \
    -perflobby Logs/perf_lobby.json                 # vsync 끔: A(렌더)와 C(여유)
Build/mac/Sublevel.app/Contents/MacOS/Sublevel -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 \
    -perflobby Logs/perf_lobby_vsync.json -perfvsync  # 60 Hz 디스플레이에서 B(프레임)
```
- 결과 JSON: `Frame`(자연 프레임, Time.unscaledDeltaTime 600프레임: AvgMs/P99Ms/MaxMs), `Render`(카메라→1920×1080 RT + 1픽셀 리드백, UI 포함, 600프레임: PERF A), `DrawCalls`/`Triangles`(릴리스 빌드에선 0일 수 있음), `Characters`(4여야 함). 옆에 같은 이름의 `.png` 스크린샷.
- 판정: A = `Render.P99Ms ≤ 10`, 드로우콜 ≤ 2500, 삼각형 ≤ 1.5M. B = vsync 켠 `Frame.AvgMs ≤ 17.0` 그리고 `P99Ms ≤ 20`. C(여유) = vsync 끈 `Frame.AvgMs ≤ 8.3`(넘으면 `perf` 티켓).
- A를 못 맞추면 후속 최적화: 로비 후처리 단순화(에디터 측정 평균 -1.0 ms) → 같은 구도 캡처 평균 절대차로 룩 변화 수치 보고.
- 코드: `Scripts/Gameplay/LobbyPerfCapture.cs`(인자 `-perflobby <json>` 일 때만 동작, 일반 실행에는 영향 없음).
- 에디터 참고값(동시 Unity가 있던 환경, 잡음 큼): sync 평균 7.3~8.1 ms, p99 10.6~11.5 ms (`ui_DONE.md`).

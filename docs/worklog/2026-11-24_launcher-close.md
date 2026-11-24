# 런처: 게임 실행 후 런처가 닫히지 않음 (0.1.8)

- 날짜: 2026-11-24
- 저장소: sublevel-launcher (커밋 전, 변경은 미커밋 상태)
- 증상: Mac·Windows 0.1.7에서 '게임 실행 후 런처 닫기'(기본 켬)인데 게임을 켜도 런처가 남는다. 상태 문구는 "게임을 실행했습니다. 런처를 닫습니다."까지 나온다(준환 확인).

## 원인
- 0.1.7은 `launch_game`가 끝난 뒤 **웹뷰 JS**가 `setTimeout(800ms)` → `invoke("quit")` → `app.exit(0)`로 닫았다.
- 상태 문구가 떴으므로 JS는 닫기 분기까지 왔다. 실패 지점은 그 뒤, 게임 창이 런처를 덮은 상태에서 실행되는 JS 타이머·IPC 구간이다.
- 배제한 것(시험으로 확인): 명령 ACL(앱 명령은 기본 허용, `quit` 호출 성공), 살아 있는 자식 프로세스가 있을 때의 `app.exit`, single-instance·deep-link 플러그인, `updating` 플래그, closeAfter localStorage 값(이 Mac에서 켜짐).
- 창 없는 가짜 게임으로는 0.1.7 설치본도 정상 종료되어, 실제 게임 창이 덮은 상태의 실패는 재현하지 못했다. 즉 원인은 "게임 창이 뜬 뒤 웹뷰 JS가 멈추거나 늦어짐"으로 좁혔지만 직접 증명은 못 했다.

## 수정
- 닫기를 Rust로 옮김: `launch_game(close: bool)`이 게임 실행을 확인한 뒤 0.8초 뒤 `app.exit(0)`. 3초 안에 안 끝나면 `std::process::exit(0)`로 강제 종료(안전장치).
- `quit` 명령 삭제. JS는 `invoke("launch_game", { close })`만 하고 문구만 표시.
- 파일: `src-tauri/src/lib.rs`, `ui/main.js`, `ui/mock.js`, 버전 0.1.8(`package.json`, `src-tauri/Cargo.toml`, `Cargo.lock`, `tauri.conf.json`, `ui/index.html`).

## 검증
- 시험 장치(스크래치, 저장소 밖): 로컬 모의 서버(`SUBLEVEL_API_BASE`) + `SUBLEVEL_HOME`의 창 없는 가짜 게임(`sleep`) + 테스트 키체인 항목(`SUBLEVEL_KEY_SERVICE`) + 두 번째 인스턴스에 `sublevel://start`로 실행 트리거 → 런처 프로세스가 종료되는지 관찰.
  - 0.1.7 설치본: 5초 후 종료(코드 0) — 위 배제 근거.
  - 0.1.7 디버그 + 실행 직후 창 최소화: 종료됨(최소화만으로는 재현 안 됨).
  - 0.1.8 디버그: 4초 후 종료(코드 0), 게임 토큰 환경 변수 전달 확인.
  - 0.1.8 릴리스 번들(`npm run build`): 4초 후 종료(코드 0).
- `cargo test` 13개 통과. 번들: `src-tauri/target/release/bundle/macos/Sublevel.app`(4.83 MiB), `dmg/Sublevel_0.1.8_aarch64.dmg`(2.43 MiB).
- 시험 부작용: 초기 실행에서 키체인 접근 확인 창이 떴고(시험 항목, `-A`로 다시 만들어 해결), 디버그 런처 창이 몇 번 잠깐 보였다. 시험 키체인 항목은 삭제함.

## 남은 것·주의
- 실제 게임 창(전체 화면)이 덮은 상태에서의 종료는 미검증(사무실 환경이라 게임 창을 띄우지 않음). Mac에서 실제 게임으로 한 번 확인 필요.
- Windows는 CI 빌드 후 확인 필요(태그 필요, 이번 작업에서 안 함).
- '닫기 끔' 경로(close=false)는 코드만 확인, 실행 시험 안 함.
- 덤으로 본 것: 딥링크가 `me` 로딩 전에 오면 "로그인한 뒤…" 문구만 뜨고 시작이 버려진다(범위 밖).

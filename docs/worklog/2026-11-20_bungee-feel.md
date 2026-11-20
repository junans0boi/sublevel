# 번지점프 박진감·줄·내려오는 방법

- 날짜: 2026-11-20
- 이슈: #432
- ADR: ADR-498
- 브랜치: `claude/bungee-feel-ccd7c7` (커밋 4234dba)
- main 머지: df26fdb

## 한 일
- 자유낙하 40 m, 줄이 약 2g로 잡아챔. 머리부터 −80° 다이브 카메라, 속도 따라 FOV·떨림·바람 소리·비네트, 줄이 팽팽해질 때 본인 화면만 슬로모션.
- 줄(BungeeCordView, LineRenderer 1개)을 모든 플레이어가 봄. 새 네트워크 메시지 없음.
- 매달리면 F 또는 6초 뒤 자동으로 패드까지 내려가고 줄이 풀림(CmdBungeeLower).

## 검증
- EditMode 통과(Strafe FBX 누락 2건은 환경 문제), BungeeJumpPlayTests 2/2, WaitingIslandPlayTests 통과.

## 남은 것·주의
- main 머지 때 LobbyNet Protocol을 7로 맞춤(사격장 6 다음).
- HUD 문구는 캡처에 안 찍혀 테스트로만 확인, 소리는 직접 못 들음.

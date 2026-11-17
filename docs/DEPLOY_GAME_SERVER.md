# 게임 서버 배포 (오라클 vcn-main) — #383

비밀 값(키·접속 스크립트 내용) 없음. 서버 기반(Caddy·Tailscale)은 사이트 저장소 `docs/DEPLOY_SERVER.md`.

## 구성
- Ubuntu 24.04 aarch64, 2코어, 11GiB. 접속은 **tailnet IP `100.97.58.29:7770`**(UDP). 로비 7770, 매치 7771. 방화벽은 `tailscale0` 전체 허용 규칙에 의존하고 공개 UDP는 REJECT(추가 규칙 없음).
- 설치: `docs/deploy/install_game_server.sh` (전용 사용자 `sublevel`, `/opt/sublevel-game`, `/var/lib/sublevel-game`, `sublevel-game.service`: `Restart=on-failure`, `MemoryMax=6G`, `--bots 5`, 로그는 journald + `LogRateLimit`).
- 빌드 배치: `/opt/sublevel-game/releases/<이름>/` + 심볼릭 링크 `current`. 롤백은 링크를 이전 릴리스로 되돌리고 재시작.

## 배포 절차
Mac에서 서버 빌드(`tools/unity.sh build server-linux-arm64`) 후, **클라이언트와 같은 커밋**인지 확인한다(다르면 서버가 거절).
```bash
B=<worktree>/Build/server-linux-arm64
COPYFILE_DISABLE=1 tar --exclude='*_BackUpThisFolder_ButDontShipItWithYourGame' --exclude='*_BurstDebugInformation_DoNotShip' --exclude='libdecor*' --exclude='._*' -C "$B" -cf - . | zstd -T2 -10 -q -o server.tar.zst   # 약 115MB
# 접속 스크립트는 표준입력으로 원격 셸 명령을 받는다. 첫 줄 명령 + 이어지는 base64 스트림:
{ printf 'D=/opt/sublevel-game/releases/rN; sudo mkdir -p $D && sudo chown sublevel:sublevel $D && base64 -d | zstd -dc | sudo -u sublevel tar -x -C $D; exit\n'; base64 < server.tar.zst; } | <ssh-ubuntu.command>   # 약 15초
# 교체
sudo ln -sfn /opt/sublevel-game/releases/rN /opt/sublevel-game/current && sudo systemctl restart sublevel-game
```
확인: `systemctl is-active sublevel-game`, `ss -ulnp | grep 7770`, `sudo journalctl -u sublevel-game -n 50 | grep '\[Server\]'`.

## 결과 (2026-11-18)
- aarch64 Linux에서 바로 기동. `ldd` 누락 라이브러리 없음(`file` 명령은 서버에 없음). 설치 크기: r1 338MB, r2(cf90ce8) 395MB, 압축 87~115MB.
- 로그: `[Server] starting: port 7770 ... version 1.0/1` → `lobby open on port 7770`. `shutting down`은 SIGTERM에서 확인.
- 이미 해결된 경고: 맥 압축이 만든 `._*` 파일 때문에 `Failed to open plugin ... ._lib_burst_generated.so` → 위 `COPYFILE_DISABLE=1`·`--exclude='._*'`로 방지.
- **유휴(사람 0) 측정**: r1 RSS 162MB·CPU 2.3%(기동 34초), r2 RSS 238MB·CPU 2.8%(기동 30초), 스레드 25. 사람 접속 시 측정은 아래 체크리스트.
- **복구**: `systemctl kill -s SIGKILL` → 5초 뒤 새 PID로 active(r1·r2 모두), `SIGTERM`(restart)은 정상 종료 로그. 로그 로테이션은 journald 기본 보관 정책(서버 공용 설정은 건드리지 않음).
- **미검증**: 사람 접속·판 시작·종료·로비 복귀·포트 재개방, 사람 접속 시 CPU/RSS, 봇 파밍 로그, 공개 IP UDP 차단의 외부 확인, Windows 클라이언트.

## 접속 시험 체크리스트 (준환)
1. Mac에서 Tailscale 켜짐 확인(`tailscale status`에 `vcn-main`). 클라이언트는 서버와 **같은 커밋**(`r1-cf90ce8`).
2. 게임 메인 메뉴 → `참가` → `100.97.58.29:7770` 입력 → 로비 진입, 준비(또는 60초 대기).
3. 판이 시작되고 봇 포함 전투원이 보이는지, 총기 파밍·사격·이동이 정상인지 한 판을 끝까지 플레이. 결과 후 로비로 돌아오는지.
4. 세션이 도는 동안 에이전트가 서버에서: `ps -o pcpu,rss -p $(systemctl show -p MainPID --value sublevel-game)`을 10초 간격으로, `journalctl -u sublevel-game | grep '\[Server\]'`로 참가·시작·종료·재개방 확인.
5. 봇 파밍 확인이 필요하면 `--bots 15`로 `ExecStart`를 조정(`sudo systemctl edit`)하고 한 판 중 봇 무장 로그 확인.

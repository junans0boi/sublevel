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

## 서버 옵션 vs 80인 규칙 (SPEC §2, ADR-259)
- 규칙: 판당 80명(사람 최대 64 + 봇 최소 16). 기본값은 `rules.json` `Match`(Participants 80, MinBots 16, MaxPlayers 64).
- 지금 유닛은 `--bots 5 --max-players 16`(시험용)이라 80인 규칙과 다르다. **권장**: 두 인자를 빼서 기본값을 따른다(사람 1명이면 봇 79가 아니라 MaxBots 상한 내에서 채움).
- 위험: 2코어 ARM에서 80인 서버 부하는 미측정(Mac M1 측정만 있음). 그래서 단계적으로: ① 지금 인자로 사람 1 + 봇 5 한 판(기능 확인) → ② 인자를 빼 80인 한 판과 CPU 기록 → CPU가 포화면 `--participants 40` 등으로 낮춤. 변경은 `sudo systemctl edit sublevel-game`(ExecStart 재지정) 후 `restart`. 재시작은 사람이 없을 때만.

## 코드 대조 결과 (main 77102ca 기준)
- **프로토콜 불일치 주의**: 현재 코드 `LobbyNet.Protocol = 2`, `bundleVersion 1.0` → `CurrentVersion = "1.0/2"`. 서버에 올라간 r2는 `1.0/1`(로그)이라 **main에서 만든 클라이언트는 r2 서버에 거절된다.** 클라이언트를 새로 낼 때는 서버도 같은 커밋으로 다시 빌드·교체해야 한다(아래 절차).
- 서버 옵션(`DedicatedServer.cs`·`MatchRules`): `--participants`가 없으면 rules.json(80), `MaxPlayers = min(--max-players, MaxHumans=64)`, 봇은 `BotFill.Plan(humans, 80, MinBots 16)`. → **80인 규칙에 맞는 ExecStart = `--bots`·`--max-players` 없이** `--dedicated --port 7770 --start-seconds 60`(`--region`은 무작위). `--start-seconds 60`은 첫 사람 후 60초에 시작(매치 큐가 시작을 정하면 무관).
- **백엔드 매칭(`sublevel-backend`, t-backend 소관) 불일치**: `MATCH_SIZE` 기본 8, `MATCH_FILL_AFTER_S` 15. `service.ts`는 티켓 인원 합계 ≤ `size`로 묶고 `bots = size − players`를 매치에 기록한다. 80인 규칙이면 ① size=8이면 판이 8명짜리로 기록됨 ② size=80으로만 올리면 사람 묶음이 64명을 넘고 봇이 16 미만이 될 수 있음. **수정안(코드 기본값)**: 사람 상한 `maxHumans=64` 추가(묶음은 ≤64), `bots = max(size − players, 16)` 단 합계 80 유지, 기본 `size=80`·`fillAfter` 30초(MatchRules.QueueWindowSeconds=30과 일치). env가 아니라 `defaultMatchConfig` 기본값 변경. 게임 서버가 매치 기록의 bots를 쓰는지는 서버 `MatchQueue` 연동 확인 필요.

## 새 서버 빌드 → releases/rN 교체 체크리스트
1. (master가 Unity 창을 줄 때) 대상 커밋 워크트리에서 `tools/unity.sh build server-linux-arm64` → `file`이 아니라 `readelf -h`/Unity 로그로 ARM64 확인, 결과 폴더 `Build/server-linux-arm64`.
2. 같은 커밋으로 Mac 클라이언트 빌드(`tools/release.sh`) — **서버와 같은 커밋**, 로그의 버전 `1.0/N`이 서로 같은지.
3. 위 "배포 절차"로 압축·전송 → `releases/rN`(N은 이전+1), `COPYFILE_DISABLE=1`·`._*` 제외.
4. 사람이 없는 것을 확인(`journalctl | grep 'lobby:'`) 후 `current` 링크 교체 + `restart`. 새 로그의 `version`이 클라이언트와 같은지 확인.
5. `systemctl is-active`, `ss -ulnp | grep 7770`, 크래시 복구(SIGKILL) 재확인, 이상 시 `current`를 이전 `rN-1`로 되돌리고 `restart`(롤백).
6. 오래된 릴리스는 직전 1개만 남기고 `sudo rm -rf`(대상 경로 확인 후).

## 접속 시험 체크리스트 (준환)
1. Mac에서 Tailscale 켜짐 확인(`tailscale status`에 `vcn-main`). 클라이언트와 서버의 `version` 로그(`1.0/N`)가 같아야 한다.
2. 게임 메인 메뉴 → `참가` → `100.97.58.29:7770` 입력 → 로비 진입, 준비(또는 60초 대기).
3. 판이 시작되고 봇 포함 전투원이 보이는지, 총기 파밍·사격·이동이 정상인지 한 판을 끝까지 플레이. 결과 후 로비로 돌아오는지.
4. 세션이 도는 동안 에이전트가 서버에서: `ps -o pcpu,rss -p $(systemctl show -p MainPID --value sublevel-game)`을 10초 간격으로, `journalctl -u sublevel-game | grep '\[Server\]'`로 참가·시작·종료·재개방 확인.
5. 봇 파밍 확인이 필요하면 `--bots 15`로 `ExecStart`를 조정(`sudo systemctl edit`)하고 한 판 중 봇 무장 로그 확인.

## 실제 경로 핑 (#400, 2026-11-20)
Mac → 서버는 **DERP 중계(tok)**, 직접 연결 아님. ICMP 800회(tailnet): p50 48 / 평균 62 / p95 126 / p99 134 / 최대 315 ms, 손실 0%, 14%가 100 ms 초과(두 봉우리). 시험 결과·비교는 `docs/research/lagcomp_matrix.md` 끝.

## 포털 링크 (#386, 2026-11-20)
공식 포털(`Steady2Vivid-Web`, Next.js)의 `/work` 맨 위 SUBLEVEL 카드 → `https://sublevel.steady2vivid.kro.kr`. 배포 절차: 포털 저장소 `main`에 push → GitHub Actions `Deploy to Oracle Cloud`(빌드 → SCP → 서버에서 pm2 `steady2vivid-web` 재시작 → Caddy reload). 접속 정보는 저장소 Secrets에만 있다. 확인: `gh run list -R SteadyToVivid/Steady2Vivid-Web -L 1`이 success, `/work`에 sublevel 링크, 두 사이트 200.

# 전용 서버 (헤드리스) — #168, ADR-127

일반 스탠드얼론 빌드를 `--dedicated -batchmode -nographics`로 실행한다(창·로컬 플레이어 없음). Unity의 "Dedicated Server" 모듈은 이 장비에 설치돼 있지 않아 쓰지 않는다. 같은 빌드가 클라이언트도 겸한다(`--dedicated`가 없으면 평소 게임).

## 빌드

```bash
tools/unity.sh build server        # Build/server/SublevelServer.app (macOS, Mono, 약 1분)
tools/unity.sh build server-win    # Build/server-win/SublevelServer.exe (Windows 모듈 필요, 미검증)
```

Unity 에디터 메뉴: `Build > Dedicated Server (mac)`. 코드는 `Assets/_Project/Editor/BuildScript.cs`(`BuildServerMac`, `BuildServerWin`).

```bash
tools/unity.sh build server-linux-arm64   # Build/server-linux-arm64/SublevelServer (Linux ARM64, Server 서브타깃, IL2CPP, 약 3분)
```

오라클 ARM 서버용(#381, ADR-295). Unity Hub 모듈 `linux-server`·`linux-il2cpp`와 `com.unity.sdk.linux-arm64`·`com.unity.toolchain.macos-arm64-linux` 패키지가 필요하다. 올릴 것은 `SublevelServer`, `GameAssembly.so`, `UnityPlayer.so`, `SublevelServer_Data/`(약 340 MB); `*_BackUpThisFolder_*`(1.5 GB)는 올리지 않는다. 실행 확인은 #383. 자세한 내용: `docs/research/linux_arm64_server_build.md`.

## 실행

```bash
Build/server/SublevelServer.app/Contents/MacOS/Sublevel \
  -batchmode -nographics -logFile "$PWD/Logs/server.log" \
  --dedicated --port 7770 --bots 15 --region C --max-players 16 --start-seconds 60
```

(Windows: `SublevelServer.exe -batchmode -nographics -logFile server.log --dedicated ...`)

| 인자 | 뜻 | 기본 |
|---|---|---|
| `--dedicated` | 전용 서버로 실행 | (없으면 일반 게임) |
| `--port N` | 로비 포트(1..65534). 매치는 **N+1**(쓰는 중이면 빈 포트)에서 열린다. 방화벽에서 두 포트를 연다 | 7770 |
| `--bots N` | 매치를 채울 봇 수(0..49) | 사람이 늘면 줄며 전체 16명을 채움 |
| `--region ID` | 매치의 **최종 구역**(`A` 산악, `B` 습지, `C` 마을, `D` 공장, 또는 0부터의 번호). 모르는 값이면 경고 후 무시 | 무작위 |
| `--max-players N` | 한 방의 사람 수(1..40 = Participants−MinBots) | rules.json `Match.MaxPlayers`(16) |
| `--participants N` | 사람+봇 합계(2..50), 슬라이스·측정용. 봇은 사람이 비운 만큼 채우되 `MinBots`(10) 미만으로 안 줄어듦 | rules.json `Match.Participants`(50) |
| `--start-seconds N` | 첫 사람이 들어온 뒤 N초가 지나면 준비 안 한 사람이 있어도 시작(0 = 끔) | 60 |

잘못된 값은 `[Server] --port must be ...` 오류를 로그에 남기고 종료 코드 2로 끝난다(추측해서 고치지 않는다).
`-logFile -`는 로그를 표준 출력으로 보낸다. 서버가 하는 일은 모두 `[Server]` 접두사로 남는다.

## 동작

1. **로비**: `--port`에서 열린다(호스트가 없으므로 들어온 사람 모두가 준비해야 시작). 클라이언트는 메인 메뉴 `참가`에서 `서버IP:포트`를 입력한다. 버전·가득 참 거절은 방 만들기와 같다.
2. **시작**: 모두 준비하면, 또는 `--start-seconds` 타이머가 끝나면 시작한다. 매치(World 장면)가 포트+1에서 열리고 참가자가 자동으로 거기에 접속한다. 사람이 모두 자기 엘리베이터에 설 때까지(최대 60초) 문은 닫혀 있다.
3. **끝**: 매치가 결정되고 20초 뒤, 모두 떠난 지 30초 뒤(끊긴 사람의 재접속 보관 60초 포함, ADR-125), 또는 150초 동안 아무도 오지 않으면 로비로 돌아가 같은 포트를 다시 연다.
4. **종료**: `SIGTERM`/`SIGINT`(또는 `kill`) 시 `[Server] shutting down`을 남기고 소켓을 닫고 끝난다.

## 확인 방법

- EditMode `ServerArgsTests`(인자 파싱·시간 규칙).
- PlayMode `DedicatedServerPlayTests`: `Build/server`가 있으면 위 서버를 인자와 함께 실제로 띄우고(127.0.0.1만), 이 프로세스의 클라이언트가 로비로 들어가 준비 → 시작 → World 접속(플레이어 1 + 봇 2가 보임) → 나가면 서버가 빈 매치를 알아채고 로비로 돌아오는지 → `SIGTERM`으로 깨끗이 끝나는지 본다. 빌드가 없으면 건너뛴다.

## 아직 확인하지 않은 것

- Windows 서버 빌드와 실행, 다른 기기에서의 접속(방화벽·NAT), 두 사람 이상.
- 서버 CPU·메모리 예산(`docs/PERF_BUDGET.md`에 서버 항목 없음), 장시간 운영.

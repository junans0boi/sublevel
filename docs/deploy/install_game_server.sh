#!/usr/bin/env bash
# Sublevel 게임 서버 설치 (오라클 vcn-main, Ubuntu 24.04 aarch64) — #383. 비밀 값 없음.
# 서버에서 ubuntu 계정으로 실행: bash install_game_server.sh
# 공유 서버(Caddy·포털·DB)이므로 기존 서비스 재시작·포트·방화벽·journald 설정은 건드리지 않는다.
set -euo pipefail

# 1) 전용 비루트 사용자 (이미 있으면 건너뜀)
id sublevel >/dev/null 2>&1 || sudo useradd --system --no-create-home --shell /usr/sbin/nologin sublevel

# 2) 설치 경로: 빌드는 /opt/sublevel-game/current 에 풀고, 서버가 쓰는 쓰기 경로는 /var/lib/sublevel-game
sudo mkdir -p /opt/sublevel-game /var/lib/sublevel-game
sudo chown sublevel:sublevel /opt/sublevel-game /var/lib/sublevel-game

# 3) systemd 유닛. 로그는 stdout -> journald(기본 보관 정책 그대로), 폭주는 LogRateLimit으로만 제한.
sudo tee /etc/systemd/system/sublevel-game.service >/dev/null <<'UNIT'
[Unit]
Description=Sublevel dedicated game server (Unity IL2CPP aarch64)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=sublevel
Group=sublevel
WorkingDirectory=/opt/sublevel-game/current
Environment=HOME=/var/lib/sublevel-game
ExecStart=/opt/sublevel-game/current/SublevelServer -batchmode -nographics -logFile - --dedicated --port 7770 --bots 5 --max-players 16 --start-seconds 60
Restart=on-failure
RestartSec=5
KillSignal=SIGTERM
TimeoutStopSec=20
MemoryMax=6G
LogRateLimitIntervalSec=30s
LogRateLimitBurst=2000
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=/var/lib/sublevel-game

[Install]
WantedBy=multi-user.target
UNIT

sudo systemctl daemon-reload
echo "OK: 유닛 설치됨. 빌드 업로드 후: sudo systemctl enable --now sublevel-game"

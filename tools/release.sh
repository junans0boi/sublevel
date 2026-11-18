#!/usr/bin/env bash
# 게임 클라이언트 릴리스: 빌드 → zip → sha256·크기 → manifest.json → (선택) 서버 업로드. #402(L0).
# 사용: tools/release.sh [--mac] [--win] [--upload] [--version <이름>]   (플랫폼 생략 시 mac만)
#   Unity 빌드는 무겁다(순차 1개, 동기화 측정 같은 독점 창과 겹치지 않게). 비밀 값 없음.
#   버전 이름 기본값: <yyyymmddHHMM>-<커밋 7자리>. 게임-서버 호환은 Application.version/Protocol이 정하고(LobbyNet.CurrentVersion),
#   이 이름은 런처가 '새 파일이 있나' 비교하는 릴리스 라벨일 뿐이다.
# 출력: Build/release/<버전>/{sublevel-<플랫폼>-<버전>.zip, manifest.json}
# 업로드: ../sublevel-web/scripts/release_upload.sh <폴더> (서버가 sha256 재검증, 비밀 없음).
set -euo pipefail
cd "$(dirname "$0")/.."

PLATS=(); UPLOAD=0; VER=""
while [ $# -gt 0 ]; do
  case "$1" in
    --mac) PLATS+=(mac) ;;
    --win) PLATS+=(win) ;;
    --upload) UPLOAD=1 ;;
    --version) VER="${2:?--version <이름>}"; shift ;;
    *) echo "usage: $0 [--mac] [--win] [--upload] [--version <이름>]" >&2; exit 2 ;;
  esac
  shift
done
[ ${#PLATS[@]} -gt 0 ] || PLATS=(mac)
[ -n "$VER" ] || VER="$(date +%Y%m%d%H%M)-$(git rev-parse --short=7 HEAD)"
[[ "$VER" =~ ^[A-Za-z0-9_][A-Za-z0-9._-]{0,127}$ ]] || { echo "bad version name: $VER" >&2; exit 2; }

OUT="Build/release/$VER"; rm -rf "$OUT"; mkdir -p "$OUT"

# 같은 폴더에 'Backup/DoNotShip' 폴더가 생기는 Unity 빌드: 배포 zip에서 제외한다.
EXCL=(-x '*_BackUpThisFolder_ButDontShipItWithYourGame/*' -x '*_BurstDebugInformation_DoNotShip/*')

for p in "${PLATS[@]}"; do
  FILE="sublevel-$p-$VER.zip"
  case "$p" in
    mac)
      tools/unity.sh build mac
      [ -d Build/mac/Sublevel.app ] || { echo "Build/mac/Sublevel.app 없음" >&2; exit 1; }
      # ditto는 macOS의 확장 속성·심볼릭 링크를 유지한다(.app 번들이 깨지지 않게)
      ditto -c -k --sequesterRsrc --keepParent Build/mac/Sublevel.app "$OUT/$FILE" ;;
    win)
      tools/unity.sh build win
      [ -f Build/win/Sublevel.exe ] || { echo "Build/win/Sublevel.exe 없음" >&2; exit 1; }
      (cd Build/win && zip -qr "../../$OUT/$FILE" . "${EXCL[@]}") ;;
  esac
  echo "zipped $p: $(du -h "$OUT/$FILE" | cut -f1)"
done

# manifest.json (docs/launcher-api.md 계약: version == 폴더 이름)
python3 -I - "$OUT" "$VER" <<'PY'
import hashlib, json, os, re, sys, datetime
d, ver = sys.argv[1:3]
files = {}
for f in sorted(os.listdir(d)):
    m = re.fullmatch(r"sublevel-([a-z0-9_-]+)-" + re.escape(ver) + r"\.zip", f)
    if not m: continue
    h = hashlib.sha256()
    with open(os.path.join(d, f), "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""): h.update(chunk)
    files[m.group(1)] = {"file": f, "sha256": h.hexdigest(), "size": os.path.getsize(os.path.join(d, f))}
assert files, "no zip files"
json.dump({"version": ver, "released_at": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "files": files},
          open(os.path.join(d, "manifest.json"), "w"), indent=1)
print("manifest:", {k: (v["size"], v["sha256"][:12]) for k, v in files.items()})
PY

if [ "$UPLOAD" = 1 ]; then
  UP="../sublevel-web/scripts/release_upload.sh"
  [ -x "$UP" ] || UP="/Volumes/WorkSpace/Project/sublevel-web/scripts/release_upload.sh"
  bash "$UP" "$OUT"
fi
echo "release ready: $OUT"

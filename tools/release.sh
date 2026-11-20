#!/usr/bin/env bash
# 게임 클라이언트 릴리스: 빌드 → zip → sha256·크기 → manifest.json → (선택) 서버 업로드. #402(L0).
# 사용: tools/release.sh [--mac] [--win] [--upload] [--github <태그>] [--version <이름>]   (플랫폼 생략 시 mac만)
#   --github <태그>: 만든 zip·manifest를 그 태그의 GitHub Release(공개 저장소)에 첨부. 공개 배포 조건은 docs/RELEASE.md 'GitHub Releases'.
#   Unity 빌드는 무겁다(순차 1개, 동기화 측정 같은 독점 창과 겹치지 않게). 비밀 값 없음.
#   버전 규칙(ADR-535): 이름 = v<bundleVersion>[-접미사] (예: bundleVersion 0.2.0 → v0.2.0 = 게임 저장소 태그).
#   게임 서버가 받는 값 LobbyNet.CurrentVersion = <bundleVersion>/<Protocol>이라 같은 bundleVersion에서 나온다.
#   Protocol을 올렸으면 bundleVersion도 올린다(같은 이름의 릴리스는 서버가 EXISTS로 거절).
# 출력: Build/release/<버전>/{sublevel-<플랫폼>-<버전>.zip, manifest.json}
# 업로드: ../sublevel-web/scripts/release_upload.sh <폴더> (서버가 sha256 재검증, 비밀 없음).
set -euo pipefail
cd "$(dirname "$0")/.."

PLATS=(); UPLOAD=0; VER=""; GH_TAG=""
while [ $# -gt 0 ]; do
  case "$1" in
    --mac) PLATS+=(mac) ;;
    --win) PLATS+=(win) ;;
    --upload) UPLOAD=1 ;;
    --github) GH_TAG="${2:?--github <태그>}"; shift ;;
    --version) VER="${2:?--version <이름>}"; shift ;;
    *) echo "usage: $0 [--mac] [--win] [--upload] [--github <태그>] [--version <이름>]" >&2; exit 2 ;;
  esac
  shift
done
[ ${#PLATS[@]} -gt 0 ] || PLATS=(mac)
# 빌드 전에 확인: 태그의 Release가 있어야 첨부할 수 있다(태그 push → .github/workflows/release.yml이 만든다)
[ -z "$GH_TAG" ] || gh release view "$GH_TAG" --repo junans0boi/sublevel >/dev/null || { echo "GitHub Release $GH_TAG 없음" >&2; exit 1; }
BV="$(sed -n 's/^  bundleVersion: //p' ProjectSettings/ProjectSettings.asset)"
[[ "$BV" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "bundleVersion '$BV' is not X.Y.Z (ADR-535)" >&2; exit 2; }
[ -n "$VER" ] || VER="v$BV"
[[ "$VER" =~ ^v${BV//./\\.}(-[A-Za-z0-9._]{1,32})?$ ]] || { echo "version '$VER' must be v$BV[-suffix] (ADR-535)" >&2; exit 2; }

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
if [ -n "$GH_TAG" ]; then
  for f in "$OUT"/*.zip; do  # GitHub Release 파일 하나당 2 GiB 상한
    [ "$(stat -f %z "$f")" -lt 2147483648 ] || { echo "$f 가 2 GiB 이상이라 GitHub Release에 올릴 수 없음" >&2; exit 1; }
  done
  gh release upload "$GH_TAG" "$OUT"/*.zip "$OUT/manifest.json" --repo junans0boi/sublevel
fi
echo "release ready: $OUT"

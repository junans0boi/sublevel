#!/usr/bin/env bash
# Unity Editor 커맨드라인 래퍼. 사용법: tools/unity.sh {create|run|run-gfx|test|build|log|license-return} ...
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
mkdir -p Logs Build

# 실행 파일이 실제로 있는 6000.3.x 중 가장 높은 버전을 자동 선택 (UNITY_PATH로 덮어쓰기 가능). ADR-021
if [[ -z "${UNITY_PATH:-}" ]]; then
  VER=""
  for v in $(ls /Applications/Unity/Hub/Editor 2>/dev/null | grep '^6000\.3\.' | sort -V); do
    [[ -x "/Applications/Unity/Hub/Editor/$v/Unity.app/Contents/MacOS/Unity" ]] && VER="$v"
  done
  [[ -z "$VER" ]] && { echo "Unity 6000.3.x not found. See docs/SETUP.md step 3"; exit 2; }
  UNITY_PATH="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
fi

cmd="${1:-}"; shift || true
case "$cmd" in
  create)
    "$UNITY_PATH" -batchmode -nographics -quit -createProject "$ROOT" -logFile Logs/create.log -accept-apiupdate ;;
  run)
    "$UNITY_PATH" -batchmode -nographics -quit -projectPath "$ROOT" -executeMethod "${1:?Class.Method}" -logFile "Logs/run-${1//./_}.log" -accept-apiupdate ;;
  run-gfx)
    # Like run, but with a graphics device (needed to render screenshots).
    "$UNITY_PATH" -batchmode -quit -projectPath "$ROOT" -executeMethod "${1:?Class.Method}" -logFile "Logs/run-${1//./_}.log" -accept-apiupdate ;;
  test)
    mode="${1:?editmode|playmode}"; filter="${2:-}"
    args=(-batchmode -projectPath "$ROOT" -runTests -testPlatform "$mode" -testResults "Logs/test-$mode.xml" -logFile "Logs/test-$mode.log")
    [[ "$mode" == "editmode" ]] && args=(-nographics "${args[@]}")
    [[ -n "$filter" ]] && args+=(-testFilter "$filter")
    # TEST_CATEGORY: e.g. 'Long' (only the long match simulation, #77) or '!Long' (everything else).
    [[ -n "${TEST_CATEGORY:-}" ]] && args+=(-testCategory "$TEST_CATEGORY")
    "$UNITY_PATH" "${args[@]}" ;;
  build)
    case "${1:?mac|win}" in
      mac) "$UNITY_PATH" -batchmode -nographics -quit -projectPath "$ROOT" -buildTarget StandaloneOSX -executeMethod BuildScript.BuildMac -logFile Logs/build-mac.log ;;
      win) "$UNITY_PATH" -batchmode -nographics -quit -projectPath "$ROOT" -buildTarget StandaloneWindows64 -executeMethod BuildScript.BuildWin -logFile Logs/build-win.log ;;
      *) echo "mac|win"; exit 1 ;;
    esac ;;
  log)
    f="$(ls -t Logs/*.log 2>/dev/null | head -1)"; echo "== $f"; grep -nE "error CS|Exception|Build Finished|Tests? (passed|failed)" "$f" | tail -40 ;;
  license-return)
    "$UNITY_PATH" -batchmode -quit -returnlicense ;;
  *) echo "usage: $0 {create|run <Class.Method>|run-gfx <Class.Method>|test editmode|playmode [filter]|build mac|win|log|license-return}"; exit 1 ;;
esac

#!/usr/bin/env bash
# 80-player bot statistics (ADR-313, #377): runs the dedicated server bots-only for each level x seed and merges the rows.
# Usage: tools/bot_stats.sh [seconds=420] [levels="easy normal hard"] [seeds="1 2 3"] [extra server args...]
# Level "mix" runs the settings' level mix (no --perf-level), so the per-level kills/deaths compare the levels in one match.
# Needs `tools/unity.sh build server` first. One server at a time (M1 13"); never kills anything but its own server.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
SECS="${1:-420}"; LEVELS="${2:-easy normal hard}"; SEEDS="${3:-1 2 3}"; shift $(( $# < 3 ? $# : 3 ))
BIN="Build/server/SublevelServer.app/Contents/MacOS/Sublevel"
[[ -x "$BIN" ]] || { echo "no server build: tools/unity.sh build server"; exit 2; }
OUT="Logs/botstats"; mkdir -p "$OUT" Logs/perf
export SUBLEVEL_PERF_DIR="$ROOT/Logs/perf"
STAMP="$(date +%Y%m%d-%H%M%S)"
for level in $LEVELS; do
  for seed in $SEEDS; do
    tag="p0_${level}_s${seed}"
    rm -f "Logs/perf/botstats_${tag}.csv"
    lvl=(--perf-level "$level"); [[ "$level" == mix ]] && lvl=()
    "$BIN" -batchmode -nographics -logFile "$ROOT/$OUT/${STAMP}_${tag}.log" --dedicated --port 7770 \
      --perf "$tag" --participants 80 --bots 79 --region C --start-seconds 5 ${lvl[@]+"${lvl[@]}"} --perf-seed "$seed" "$@" &
    pid=$!
    sleep "$SECS"
    kill -TERM "$pid" 2>/dev/null || true; sleep 5; kill -9 "$pid" 2>/dev/null || true
    cp "Logs/perf/botstats_${tag}.csv" "$OUT/${STAMP}_${tag}.csv" 2>/dev/null || echo "no stats row for $tag"
  done
done
python3 - "$OUT" "$STAMP" <<'PY'
import csv, glob, sys
out, stamp = sys.argv[1], sys.argv[2]
rows = []
for f in sorted(glob.glob(f"{out}/{stamp}_*.csv")):
    if f.endswith("_all.csv"): continue
    rows += list(csv.DictReader(open(f)))
if rows:
    with open(f"{out}/{stamp}_all.csv", "w", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
    keys = ["level", "seed", "armed4min_all", "firstGunMedian_all", "engagements", "avgEngageSeconds", "firstEngageArmed", "stalledBots", "noiseInvestigated", "peekCycles", "avgExpose", "squadMedianM", "squadP90M", "hideShare", "frameMeanMs", "frameP99Ms"]
    print("| " + " | ".join(keys) + " |"); print("|" + "---|" * len(keys))
    for r in rows: print("| " + " | ".join(r.get(k, "") for k in keys) + " |")
    print(f"\nmerged: {out}/{stamp}_all.csv")
PY

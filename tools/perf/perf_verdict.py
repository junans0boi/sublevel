#!/usr/bin/env python3
"""PERF 판정 (#339): 측정값 JSON을 PERF_BUDGET 임계와 대조해 한 줄 판정을 낸다.
사용: tools/perf/perf_verdict.py measured.json   (키 일부만 있어도 됨, 없는 항목은 UNMEASURED = 통과 아님)
종료 코드: 0 전부 통과 / 1 하나라도 불합격 / 2 불합격은 없으나 미측정 있음(게이트 통과로 보지 않는다).
임계(최대값): 서버 S8-A2/A3 + A1 제안. 값을 바꾸려면 PERF_BUDGET + ADR이 먼저.
"""
import json
import sys

LIMITS = {
    "server_avg_ms": 10.0, "server_p99_ms": 25.0, "bot_ms": 4.0, "server_mem_gb": 1.5,
    "client_kb_per_s": 15.0, "server_mbit_per_s": 6.0,
    "client_mem_gb": 3.0, "load_s": 5.0, "navmesh_bake_s": 60.0, "navmesh_mb": 30.0,
}


def judge(measured):
    rows = []
    for key, limit in LIMITS.items():
        v = measured.get(key)
        state = "UNMEASURED" if v is None else ("PASS" if v <= limit else "FAIL")
        rows.append((key, v, limit, state))
    return rows


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 64
    with open(argv[1], encoding="utf-8") as f:
        rows = judge(json.load(f))
    for key, v, limit, state in rows:
        print(f"{state:10} {key:20} {'-' if v is None else v:>8} <= {limit}")
    fails = [r[0] for r in rows if r[3] == "FAIL"]
    unmeasured = [r[0] for r in rows if r[3] == "UNMEASURED"]
    if fails:
        print("VERDICT: FAIL (" + ", ".join(fails) + ")")
        return 1
    if unmeasured:
        print("VERDICT: INCOMPLETE, unmeasured: " + ", ".join(unmeasured))
        return 2
    print("VERDICT: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

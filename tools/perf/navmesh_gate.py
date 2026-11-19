#!/usr/bin/env python3
"""NavMesh 용량 게이트 (#337): 베이크 파일 크기(MB)를 JSON으로 내고, 있으면 perf_verdict 입력에 합친다.
사용: tools/perf/navmesh_gate.py [asset] [measured.json]  → navmesh_mb(와 기존 키)를 담은 JSON을 stdout에.
베이크 시간(navmesh_bake_s)은 Unity 로그에서 사람이 넣는다(측정 창 필요)."""
import json
import os
import sys

ASSET = "Assets/_Project/Prefabs/World/World_NavMesh.asset"


def with_navmesh_mb(measured, asset=ASSET):
    out = dict(measured)
    out["navmesh_mb"] = round(os.path.getsize(asset) / (1024 * 1024), 1)
    return out


if __name__ == "__main__":
    asset = sys.argv[1] if len(sys.argv) > 1 else ASSET
    base = json.load(open(sys.argv[2], encoding="utf-8")) if len(sys.argv) > 2 else {}
    print(json.dumps(with_navmesh_mb(base, asset)))

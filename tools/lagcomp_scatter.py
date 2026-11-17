#!/usr/bin/env python3
"""Reads Logs/net-multi/lagcomp-shot-scatter.csv (LagCompShotScatterTests, #401) and splits the hit scatter.
usage: tools/lagcomp_scatter.py file.csv [more.csv ...]
Per (rtt, interp): shots, hit %, mean/stdev of dt (rewound-to minus the time the copy showed, ms), and how much of dt's variance
comes from each part: dt = (exec - call) - model + copy_delay, i.e. uplink + (-model) + copy delay.
A shot is judged 'late' when |along error| exceeds the body's half width / speed (0.3 m / v)."""
import csv, statistics as st, sys, collections

HALF_WIDTH = 0.3

def cov(a, b):
    ma, mb = st.fmean(a), st.fmean(b)
    return sum((x - ma) * (y - mb) for x, y in zip(a, b)) / max(len(a) - 1, 1)

rows = []
for f in sys.argv[1:]:
    with open(f) as fh:
        rows += list(csv.DictReader(fh))
groups = collections.defaultdict(list)
for r in rows:
    groups[(int(r['rtt_ms']), int(r['interp']))].append({k: float(v) for k, v in r.items()})

print("rtt interp   n  hit%  v(m/s)  dt mean  dt sd | var share: uplink  model  copy | misses: tol-exceeded  other")
for (rtt, interp), g in sorted(groups.items()):
    n = len(g)
    hit = sum(x['hit'] for x in g) / n * 100
    v = st.fmean(x['speed_mps'] for x in g)
    dt = [x['dt_ms'] for x in g]
    up = [x['call_to_exec_ms'] for x in g]
    mo = [-x['model_ms'] for x in g]
    cd = [x['copy_delay_ms'] for x in g]
    var = st.pvariance(dt) if n > 1 else 0.0
    share = lambda a: (st.pvariance(a) + sum(cov(a, b) for b in (up, mo, cd) if b is not a)) / var * 100 if var > 0 and n > 1 else float('nan')
    misses = [x for x in g if x['hit'] == 0]
    tol = [m for m in misses if abs(m['along_ms']) / 1000 * m['speed_mps'] > HALF_WIDTH]
    print(f"{rtt:>3} {interp:>5} {n:>4} {hit:5.0f} {v:7.2f} {st.fmean(dt):8.1f} {st.pstdev(dt) if n > 1 else 0:6.1f} | {share(up):6.0f}% {share(mo):5.0f}% {share(cd):4.0f}% | {len(tol):>3} of {len(misses):<3}      {len(misses) - len(tol)}")
print("\nstdev (ms) of the parts: uplink / model / copy delay")
for (rtt, interp), g in sorted(groups.items()):
    if len(g) > 1:
        print(f"{rtt:>3} {interp:>5}  {st.pstdev([x['call_to_exec_ms'] for x in g]):5.1f} / {st.pstdev([x['model_ms'] for x in g]):5.1f} / {st.pstdev([x['copy_delay_ms'] for x in g]):5.1f}")

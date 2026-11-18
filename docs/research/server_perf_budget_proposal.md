# 서버(80인) 성능 예산 제안과 판정 방법 (#329 #337 #339)

적용·PERF_BUDGET 반영은 master 승인. 값 변경은 ADR 필요. 판정은 `python3 tools/perf/perf_verdict.py measured.json`(미측정은 통과가 아님, 종료코드 0/1/2).

| 항목 | 키 | 한도 | 지금 측정 |
|---|---|---|---|
| 서버 평균 프레임 | server_avg_ms | ≤10 ms | 미측정(80인) |
| 서버 p99 | server_p99_ms | ≤25 ms | 봇 spike 교전 창 26~28 ms → 초과 가능 |
| 봇 몫 | bot_ms | ≤4 ms | 미측정 |
| 서버 메모리 | server_mem_gb | ≤1.5 GB | 미측정 |
| 클라당 전송 | client_kb_per_s | ≤15 KB/s | 16인+봇20: 8.5 KB/s(PERF_BUDGET 네트워크 표) |
| 서버 송신 | server_mbit_per_s | ≤6 Mbit/s | 미측정 |
| 클라 메모리·로딩 | client_mem_gb / load_s | ≤3 GB / ≤5 s | 미측정 |
| NavMesh 베이크 | navmesh_bake_s / navmesh_mb | ≤60 s / ≤30 MB | 용량 57.4 MB **초과**(`tools/perf/navmesh_gate.py`), 시간 미측정 |

게이트: 봇 몫 >4 ms → #330(BotLod·경로 예산), 문·유리 전송 >1 KB/s/클라 → #331, NavMesh 용량 초과 → 타일 분할(맵 구조라 master 경유).
측정은 Unity 독점 창에서만: 결과를 JSON으로 모아 `navmesh_gate.py → perf_verdict.py` 순으로 판정.

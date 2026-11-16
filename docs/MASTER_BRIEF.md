# MASTER 세션 브리프 — 인게임 구현 총괄 (2026-11-16)

이 문서는 새 **master 세션**이 서브에이전트 여러 개를 운영해 인게임 변경을 구현하기 위한 지시서다. 먼저 `CLAUDE.md` → 이 문서 → `docs/SPEC.md` → `docs/DECISIONS.md`(ADR-165~176) → `docs/bible/`(웹 `docs/bible/index.html`, 데이터 `docs/bible/data/*.json`, md `docs/bible/md/`) 순으로 읽는다. 충돌하면 SPEC이 이기고, 규칙을 바꾸려면 ADR이 먼저다(CLAUDE.md 규칙 5).

## 0. 목표
SPEC(2026-11-16 개정)대로 게임을 바꾼다: **판당 50명(사람+봇), 솔로/스쿼드(4), 새 맵 C2(청람도: 강+산악, 6지역, 마을 17, 건물 약 411, 전 도시 국도 연결), 오염 시스템(하이브리드), 열쇠 엘리베이터 탈출, 칩 부활, 일반 파밍 총기 16종, Humanoid 캐릭터·애니메이션, Poly Haven 재질·맑은 하늘, 공간음향, 봇 50인 대응.** 비인게임 UI 시안(`docs/research/ui_mockups/adopted.html`)은 확정됐지만 **이번 범위가 아니다**(인게임 먼저). 인게임 HUD 시안은 아직 없다 — 필요한 HUD 추가는 최소한으로 하고 `docs/bible/data/ui.json`에 결정으로 올려 준환에게 묻는다.

## 1. 운영 규칙 (어기면 중단)
- **Unity를 쓰는 작업은 한 번에 하나**(프로젝트 잠금, M4 Pro 한 대). 서브에이전트가 여러 개여도 Unity 실행·빌드·테스트는 순차. `tools/unity.sh {run|run-gfx|test|build}`만 사용, PlayMode는 `/tmp/sublevel-playmode.lock`, 실행 뒤 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles` 등 Unity가 다시 저장한 파일을 되돌리고 `Assets/InitTestScene*` 삭제.
- 병렬 가능: 조사·설계·문서·데이터(JSON)·순수 C#(Core/) 단위 테스트 코드 작성(Unity 실행은 직렬화), 에셋 조사.
- **commit/push·의존성(패키지) 추가·디렉터리 구조 변경 전에 준환에게 묻는다.** 비밀키·.env 금지. 에셋 추가는 `docs/ASSET_LEDGER.md` 기록 필수(출처·라이선스·날짜), 기록 없는 에셋 커밋 금지. CC0/상업 가능만. AI 생성 에셋 금지. 실존 상표·총기·차종 이름 금지(상표 검토는 사람의 일 → `docs/RISKS_OPEN_QUESTIONS.md`).
- 게임 룰 변경은 ADR 먼저. 이미 확정된 결정은 `docs/DECISIONS.md` ADR-168~176와 `docs/bible/`의 확정 항목이다. 되돌리지 않는다.
- 새 아이디어는 `BACKLOG.md`에 한 줄. 범위 밖 작업 금지.
- 에이전트 보고는 "확인한 것 / 추정"을 구분. **스크린샷 한 장만으로 판정하지 않는다**(수치+게임 안 캡처+테스트). 이전 세션에서 시트 이미지 오판 전례가 있다.
- 성능 예산(PERF_BUDGET) 위반은 머지 금지(규칙 6). 411채·50인 규모는 **측정을 생성기 구현보다 먼저**(ADR-168).
- 사용자 대상 보고는 한국어, 코드·식별자·커밋은 영어. 커밋 메시지 끝에 `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- 마스터는 구현 티켓을 GitHub 이슈(`junans0boi/sublevel`)로 관리한다(CLAUDE.md의 wayfinder → to-spec → to-tickets → implement → code-review → close 루프). **결정이 이미 정의서에 확정된 항목은 grilling을 생략**하고 바로 wayfinder 지도를 만든다. 기존 이슈(#240 월드 용량, #223, #227, #232, #235 등)를 먼저 확인해 재사용한다.

## 2. 참고 자료 (읽는 곳)
- 조사 결론: `docs/research/01_character.md`(캐릭터), `02_map_layout.md`+`map_options/option_C2_*`(맵), `03_mountains.md`(지형), `04_animation_audit.md`(애니 감사), `05_map_assets.md`(에셋), `06_missed_items.md`(놓친 것 — **BLOCKER 5개**), `map_options/pollution_sim*`(오염 시뮬레이션 수치), `MASTER_PLAN.md`, `AGENT_PLAN.md`.
- 정의서 데이터: `docs/bible/data/{pollution,match,map,towns,roads,terrain,weapons,ammo,attachments,armor_items,vehicles,characters,bots,network,audio,materials,animation,ui,overview}.json`. 확정 결정은 `status: confirmed`, 위임 일괄 확정 로그는 `docs/bible/BULK_CONFIRMED_2026-11-16.md`(되돌릴 수 있음).

## 3. 작업 묶음(WP)과 순서
각 WP는 서브에이전트(또는 소규모 팀)가 맡고, 완료 기준은 "EditMode/PlayMode 통과 + 성능 예산 판정 + 게임 안 캡처 + ASSET_LEDGER/ADR/정의서 갱신 + 준환 확인 후 커밋"이다.

| WP | 내용 | 주요 근거 | 선행 | 병렬 |
|---|---|---|---|---|
| **0 기준선** | 현재 빌드·테스트 통과 확인(EditMode 971, PlayMode 일부), 클린 트리, #240(World_Meshes 용량) 방침 확인, **성능 측정 계획**(411채 가짜 지오메트리 드로우콜·메모리 측정 도구) | ADR-168, 06_missed_items | - | 조사 |
| **1 월드 규칙 파라미터화** | `WorldRules`/`WorldGenerator`의 하드코딩(지역 4개·엘리베이터 3~4대·MaxLootSpawns 250·MaxRocks 900·알파맵 1 m/px·맵 1024 m) → 데이터 파라미터, `BuildingKind` 5→약 12종(준환 승인 필요: enum/파일 변경) | ADR-168, map/towns/roads JSON | 0 | 문서 |
| **2 지형** | 산악 높이식(능선 골격·도메인 워프·테라스·협곡·절벽, 걷기 35도·88% 이상), 컨트롤러 slopeLimit(45) vs 규칙(40) 정리 ADR, EditMode 테스트 T1~T14 | `03_mountains.md`, terrain.json | 1 | 조사 |
| **3 도로 연결망** | 마을 전부를 잇는 연결 그래프(순환·다리 5·여울 1·차량 고개 5·보행 안부 3), 도로 경사 12도(시안은 일부 초과 → 재설계), 차량/NavMesh 연동 | roads.json, option_C2 | 2 | - |
| **4 도시 생성기** | `PlaceLots`→블록·골목, 건물 종류(주택·상가·빌라·아파트·학교·막사·공장·창고·시장·노점·감시탑·헛간), 층수 2~5·계단·옥상·창문, 소품·울타리·농지, 청람읍 90채 | towns.json, 05_map_assets | 3 | - |
| **5 재질·조명** | Poly Haven 중심 PBR(표면 12분류). **후보 선정은 Chrome으로 Poly Haven을 직접 보고 시트를 만든 뒤 준환이 고른다 → 그 전까지 BLOCKED**. 먼저 파이프라인(arm 통일·2K 상한·WorldBuilder 흐린 하늘 가정 보정: 안개·그림자 거리·앰비언트·실내 리플렉션 프로브) | materials.json, ADR-174 | 4 | 조사(시트) |
| **6 오염 시스템** | SPEC §3 상태 머신 교체: 지역 6개 오염 확정 순서(시드, 연결 보장), 전선 확산(지형 비용 그리드, 평지 60/도로 100/급경사 30 m/분, 강 ×0.6, 단계 +5%, 최종 ×0.7), 지속 데미지 2/3.2/5/8/12.6/20, 경고 105초, 방독 아이템, 서버 결정론+복제는 지역 단계·전선만, 최소 HUD 경고 | pollution.json, `pollution_sim_notes.md`, ADR-169 | 1 | 2~5와 병렬 설계 |
| **7 열쇠 엘리베이터** | 열쇠 6~8개(편중 0.5), 사용 순서(삽입→문 열림→30초→지역 선택 15초→무효 시 반환), 누구나 탑승·한 대 4명, 비오염 지역 선택, 전선 지역 승강장 90초 충전·팀 쿨다운 3분, 도착 보호 없음, ADR-055 투입(1인 1기) 유지 | ADR-170 | 6 | - |
| **8 스쿼드·칩 부활** | 솔로+스쿼드(4), 칩 부활(승강장 칩 보관함, 90초, 오염 확정 시 파괴, 비오염 지역 승강장 부활), 기절·부활과의 관계 | ADR-171, match.json | 7 | - |
| **9 총기 로스터** | 일반 파밍 16종(SMG 3, AR 4, DMR 3, SR 2, 샷건 2, 권총 2), 새 스탯 저지력(경직), Breacher→5발 탄창식 반자동, 더블 배럴, 보급 총기 4~6종, 탄 4종 유지. **모델은 CC0 조사 전까지 BLOCKED(임시 플레이스홀더는 `placeholder/`)**. 이름은 가칭, 상표 검토는 사람 | ADR-172, weapons.json | 0 | 조사 |
| **10 캐릭터·애니** | Humanoid 전환(Mixamo 사실풍+UBC, `sublevel_staging/`에 UBC·UAL 보관), 레이어 Base/Upper/Action/Additive, 왼손 IK+LeftHandGrip, 발 IK, 엎드림 재설계, 소총 실물 비례, 테스트 결과 기반 재작성. **준환이 시험용 Mixamo FBX 1종을 직접 받아 주기 전까지 BLOCKED**, Mixamo Additional Terms는 사람이 읽고 승인 | ADR-173, 01·04 보고서, characters/animation.json | 0 | 조사 |
| **11 봇 50인** | 인원 50(사람 적으면 봇 채움), 오염 예고 인지·이동, 열쇠 줍기·사용, 팀 구성, NavMesh(NavMeshLink는 이번 제외) | ADR-175, bots.json | 6,7 | - |
| **12 네트워크·성능** | 50인 서버 구조(프로세스 방당 1), 관심영역(문·유리·루팅 이벤트 필터), 봇 AI LOD, LagComp 비용, 오클루전·LOD·인스턴싱, 성능 예산 판정 자동화(메모리·로딩) | network.json, 06 | 1 | 측정 |
| **13 사운드** | 표면 태그 데이터화(이름 파싱 제거, 14종), 공간음향 설계(층 차폐·벽 폐색·잔향), **소스 후보 청취 시트는 준환이 직접 듣고 선택 → 그 전까지 후보 조사만** | audio.json | 12 | 조사 |
| **14 통합·플레이테스트** | 50인 통합 판, 밸런스, 새 빌드, 캡처 보고 | - | 전부 | - |

권장 진행: 0 → (1·9·10 조사/설계 병렬) → 1 → 2 → 3 → 4 → 6 → 7 → 8 → 11 → 12 → 5·13·9·10(블로커 해소 후) → 14. Unity 작업 순서는 위 표를 따르되, 블로커 때문에 막힌 WP는 건너뛰고 다음 WP를 진행한다.

## 4. 서브에이전트 운용
- **마스터**: wayfinder 지도·티켓 작성, WP 순서·블로커 관리, Unity 락 관리(동시에 하나), 충돌 조정, 준환 보고.
- **WP 리드(조사/설계)**: 한 문서만 소유 `docs/research/NN_*.md` 또는 해당 `docs/bible/data/*.json`.
- **구현 에이전트**: 티켓 승인 후에만, 소유 파일 목록을 받고 다른 WP 파일은 건드리지 않는다. 테스트 우선(tdd 스킬), 끝나면 diff 요약과 테스트/캡처 증거.
- **검증 에이전트**: 구현자와 다른 에이전트가 code-review 스킬로 리뷰(Standards+Spec). 확인한 것/추정 구분, 이미지 단독 판정 금지.
- 동시 실행 상한 4~5(비용·충돌), 보고 형식은 한국어 10줄 요약 + 체크리스트.

## 5. 준환이 해야 하는 일(BLOCKER, 마스터가 요청 목록으로 모아 한 번에 보고)
1. 시험용 Mixamo 캐릭터 FBX 1종 다운로드(WP10), Mixamo Additional Terms 원문 확인(규칙 8).
2. Poly Haven 재질 후보 시트 선택(WP5), 사운드 후보 청취 선택(WP13).
3. 신규 총기 이름·VK-9 상표 검토(사람), 게임 이름.
4. `BuildingKind` 확장 등 파일/enum 구조 변경 승인(WP1·4), 새 패키지가 필요하면 승인.
5. commit/push 승인(WP 단위로 묶어서 요청).
6. 정의서의 남은 미정: 대역폭 목표(측정 후), 경사 수치 ADR(측정 A1 후), HRTF(Unity 확인 후), Sonniss 약관.

## 6. 완료 판정(매 WP)
EditMode/PlayMode 통과, `AssetValidator`·ledger 통과, PERF_BUDGET 판정, 게임 안 캡처(스크린샷+수치), `docs/bible/` 상태 갱신, 필요 시 ADR, `docs/PROGRESS.md` 갱신, 새 빌드(`tools/unity.sh build mac`), 그리고 준환의 확인.

## 7. 첫 행동
1. 위 문서들을 읽고 오늘 할 일을 한 줄로 확인(CLAUDE.md 규칙). 2. WP 0(기준선·성능 측정 계획)을 실행. 3. wayfinder 지도(GitHub 이슈)를 만들고 준환에게 승인 요청. 4. 블로커 목록을 준환에게 먼저 보고. 5. 승인된 WP부터 서브에이전트를 배정.

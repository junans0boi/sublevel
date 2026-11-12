# DECISIONS — 결정 기록(ADR) 및 가정

형식: `ADR-번호 | 날짜 | 상태(확정/가정/폐기) | 결정 | 근거 | 영향`
에이전트가 가정을 둘 때는 상태 `가정`으로 추가한다.

| ID | 날짜 | 상태 | 결정 | 근거 |
|---|---|---|---|---|
| ADR-001 | 2026-11 | 확정 | 플랫폼은 PC 전용(macOS+Windows) | 사용자 결정, 웹 제외 |
| ADR-002 | 2026-11 | 확정 | 엔진 Unity 6.3 LTS(6000.3.x) + URP | LTS 지원 2027-12, 에셋 호환, M1 대응. Godot 4는 폴백 |
| ADR-003 | 2026-11 | 확정 | 넷코드 Fish-Net, 서버 권위 구조로 싱글 개발 | 무료/CCU 무제한/클라 예측. 싱글→멀티 전환 비용 최소화 |
| ADR-004 | 2026-11 | 확정 | 구현은 Unity Editor 커맨드라인(batchmode) | 사용자 결정. 래퍼 도구가 따로 있으면 교체 |
| ADR-005 | 2026-11 | 확정 | 루프: grilling→wayfinder 지도→to-spec→to-tickets→implement→close | 사용자 결정(기획서 도식은 묶음 표현) |
| ADR-006 | 2026-11 | 확정 | 봉쇄는 즉사 아닌 방사능 데미지 | 사용자 결정 |
| ADR-007 | 2026-11 | 확정 | 움직이는 미로 벽 폐기, 순차 폐쇄 채택 | 유저 피로도 |
| ADR-008 | 2026-11 | 확정 | 폐쇄 순서와 최종 구역은 매 판 랜덤 | 사용자 결정 |
| ADR-009 | 2026-11 | 확정 | 열쇠/출구/아이템 위치 랜덤, 구역별 열쇠 최소 보장 | 사용자 결정 |
| ADR-010 | 2026-11 | 확정 | 칩 부활은 팀 모드 한정, 보관함 90초 수거 | 사용자 결정 |
| ADR-011 | 2026-11 | 확정 | 세미리얼 아트, 로우폴리 장난감/극사실 모두 지양 | 사용자 결정 |
| ADR-012 | 2026-11 | 확정 | 라이선스 무결성(CC0/로열티프리, 상표 금지, AI 생성물 미포함) | 사용자 결정 |
| ADR-013 | 2026-11 | 확정 | MVP = 싱글+봇, 구역 D 먼저 → A | 범위 통제 |
| ADR-014 | 2026-11 | 가정 | 물은 얕은 물만, 차량/보트는 M5 이후 | 범위 통제 |
| ADR-015 | 2026-11 | 가정 | 이슈 트래커는 GitHub Issues(실패 시 docs/issues/) | 스킬 호환 |
| ADR-016 | 2026-11 | 확정 | 최종 구역은 서버가 시작 시 결정·숨김, 단계별로 후보 공개, 구역 내부는 소구역 순차 폭격 | 사용자 결정(배그식) |
| ADR-017 | 2026-11 | 확정 | 엘리베이터는 허브형(모든 구역 상호 연결) | 랜덤 폐쇄 순서에서도 연결 보장 |

## 에이전트 기록란
(새 ADR/가정을 아래에 추가)
| ADR-018 | 2026-11-12 | 폐기 | 엔진 버전을 **현재 설치된 Unity 6000.6.8f1**로 사용(ADR-002의 6000.3.x 요구를 대체). 추가 에디터 설치 안 함 | 사용자 지시("설치하지 말고 지금 설치된 버전으로"). 6000.3.28f1 설치는 중단됨 / 영향: LTS 지원 기간 이점(6.3 LTS ~2027-12)은 포기. 6.6은 6.3보다 지원 종료가 빠를 수 있으므로 릴리스 전 재검토(RISKS). 패키지 버전은 6000.6 기준으로 해석. `tools/unity.sh`는 설치된 최고 6000.x 자동 선택 **폐기(ADR-021로 대체)** |
| ADR-019 | 2026-11-12 | 확정 | Fish-Net은 **4.7.3**(태그)로 고정. 4.7.4 사용 안 함 | 4.7.4 태그 커밋(a3627529)이 Unity 6000.6에서 Yak 컴파일 에러(업스트림 이슈 #1110). 4.7.3은 정상 컴파일 확인 / 영향: 업스트림이 태그를 고치면 재검토. Packages/manifest.json에 `#4.7.3` / **2026-11-12 재확인: 6000.3.28f1에서도 4.7.4는 동일 Yak 컴파일 에러(15건) → 유지** |
| ADR-020 | 2026-11-12 | 폐기 | 스크립팅 백엔드를 당분간 **Mono**로 설정. IL2CPP는 모듈 설치 후 전환 | 설치된 6000.6.8f1에 Mac IL2CPP 모듈 없음, 사용자가 추가 설치 금지 지시(ADR-018) / 영향: 릴리스/성능 측정 전 IL2CPP 전환 필요(성능 수치 차이). PERF_BUDGET 확정은 IL2CPP 기준으로 재측정 **폐기(ADR-021로 대체)** |
| ADR-021 | 2026-11-12 | 확정 | 엔진은 **Unity 6000.3.28f1 (6.3 LTS)** 사용. ADR-002 복원. 프로젝트를 6000.3.28f1로 재생성(6.6 프로젝트는 저장소 밖 백업). 빌드 백엔드: mac=IL2CPP, win=**Mono**(windows-il2cpp 모듈은 사람이 나중에 추가 후 IL2CPP로 전환) | 사용자 결정. 6000.3.28f1은 이미 Hub로 설치 완료(mac-il2cpp, windows-mono 포함). Standalone 스크립팅 백엔드는 mac/win이 공유하므로 BuildScript가 빌드마다 명시 설정 / 영향: 패키지: URP 17.3.0, Input System 1.20.1, Cinemachine 3.1.4, Test Framework 1.6.0, Fish-Net 4.7.3. PERF 측정은 mac IL2CPP 기준. win IL2CPP 전환은 별도 티켓 |
| ADR-022 | 2026-11-12 | 확정 | user-only 스킬(wayfinder, to-spec, to-tickets, implement, implement-spec, grill-with-docs, setup-matt-pocock-skills)은 `~/.claude/plugins/cache/claude-plugins-official/mattpocock-skills/<버전>/skills/engineering/<이름>/SKILL.md`를 직접 읽어 그 절차를 그대로 따른다. 호출 가능한 스킬(grilling, tdd, code-review, prototype, research)은 Skill 도구로 호출한다. grilling 단계는 grill-with-docs 대신 grilling 사용 | 사용자 지시. 스킬 내용은 임의 변경 금지 / 영향: WORKFLOW/SETUP/CLAUDE.md 스킬명 정정 |
| ADR-023 | 2026-11-12 | 가정 | M0-6 스모크 통과 기준: 단일 프로세스 PlayMode에서 **NetworkManager 2개(서버 전용+클라 전용, Tugboat 127.0.0.1)** 로 (a) 연결 성립 (b) 서버 스폰 NetworkObject가 클라에 관측 (c) 서버가 바꾼 SyncVar가 클라에 도달 (d) 클라가 SyncVar 쓰기를 시도해도 서버 값 불변(서버 권위). 실시간 타임아웃 10초, 실패 시 단계 이름과 상태 로그 출력. 호스트 단일 NM은 부팅 확인용으로만 | #8 grilling(자가 해소). 근거: SPEC/TECH 서버 권위 원칙, research #7(호스트는 인스턴스 공유라 복제 검증 불가) / 영향: 별도 프로세스 2개 테스트·지연 시뮬은 M0 범위 밖 |
| ADR-024 | 2026-11-12 | 가정 | 코드 배치: `Scripts/Net`에 asmdef `Game.Net`(참조: FishNet.Runtime, GameKit.Dependencies; Game.Core는 필요해질 때 추가, 역참조 금지). 스모크용 NetworkBehaviour(SyncVar<int>)는 Game.Net에 두어 프로덕션 어셈블리 위빙을 함께 검증. 테스트 하네스(NM 생성/대기/정리)와 프리팹(에디터 스크립트로 생성, `Tests/PlayMode/Resources/NetSmoke`(Resources.Load 필요). 스모크 NetworkBehaviour는 릴리스 전 제거 또는 스트립 대상. Fish-Net이 생성하는 `Assets/DefaultPrefabObjects.asset`에 픽스처가 등록되지 않도록 커밋하지 않는다)은 `Game.Tests.PlayMode`에 둔다. Game.Core는 noEngineReferences 유지. 프리팹은 자체 제작 테스트 픽스처라 ASSET_LEDGER 대상 아님(`placeholder/` 불필요) | #9 grilling(자가 해소). 근거: TECH.md 저장소 구조, research #7 7절 / 영향: PlayMode asmdef에 Game.Net, FishNet.Runtime 추가 |
| ADR-025 | 2026-11-12 | 가정 | 안개(미결정)가 없는 작은 타이틀은 wayfinder 지도를 만들지 않고 스펙 이슈 + 티켓으로 바로 간다(M0-7, M0-8에 적용). 미결정이 있는 타이틀(M1 등)은 지도부터 | wayfinder SKILL.md: "no fog → you don't need a map". 사용자에게 묻지 않는 규칙에 따라 가정으로 기록 / 영향: WORKFLOW ② 단계가 타이틀에 따라 생략됨 |
| ADR-026 | 2026-11-12 | 가정 | 텍스처 "KTX2" 요구는 **Unity 플랫폼 네이티브 압축(Standalone: BC/DXT 계열, Compressed 품질) + 최대 2048**로 해석한다. KTX2 컨테이너는 쓰지 않는다 | PC 전용(Standalone)에서 Unity 임포터는 KTX2를 기본 지원하지 않고 런타임 이점도 없음. 목적(용량·VRAM 제한)은 네이티브 압축으로 달성 / 영향: PERF_BUDGET·TECH·ASSETS의 "KTX2" 표기는 이 뜻으로 읽는다 |
| ADR-027 | 2026-11-12 | 가정 | 에셋 단위는 `Assets/_Project/{Art,Audio}/<분류>/<에셋>/` 폴더. 각 폴더는 ASSET_LEDGER의 "프로젝트 경로"에 있어야 하고 `LICENSE.txt`를 동봉해야 한다. 위반 시 빌드 실패(AssetValidator) | ASSETS.md 8절, ASSET_LEDGER "검증 스크립트 요구" / 영향: 프로시저럴·자체 제작 콘텐츠는 Art/Audio 밖(Prefabs, Data, Scenes)에 둔다 |
| ADR-028 | 2026-11-12 | 확정 | PERF_BUDGET 확정: 렌더 ≤10ms(p99), 드로우콜 ≤2,500(최대 4,000), 삼각형 ≤1.5M(최대 2.5M), SetPass ≤60, GC ≤1KB/프레임. 메모리(≤3GB)·로딩(≤5초)·빌드 크기(≤4GB)는 잠정 | M0-8 실측(M1 13", 1080p, 에디터 오프스크린). 근거 표는 PERF_BUDGET.md / 영향: 플레이어 빌드 실측은 M1 게이트에서 재확인. 기준 장비 RAM은 16GB로 확인(문서의 8GB 가정 정정) |


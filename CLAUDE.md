# CLAUDE.md — 프로젝트 헌법 (매 세션 가장 먼저 읽기)

## 프로젝트
- 스튜디오: STEADY TO VIVID STUDIO / 개발자: 준환 (1인, 한국어 사용)
- 게임(가칭): 시설 기반 배틀로얄. 메이즈러너에서 영감을 받은 **오리지널** 세계관. 엘리베이터로 랜덤 구역에 투입 → 파밍 → 구역 순차 봉쇄 → 최후 생존.
- 플랫폼: **PC 전용 (macOS + Windows)**. 웹 아님.
- 개발 주체: **AI 에이전트(Claude Code)가 준환과 상의 없이 자율 구현**. 개발 장비: MacBook Pro 13" M1.
- 스택: **Unity 6.3 LTS (6000.3.x) + URP + Fish-Net**, 구현은 **Unity Editor 커맨드라인(Unity CLI, batchmode)**. 폴백: Godot 4 (M0 검증 실패 시).

## 읽는 순서
1. 이 파일 → 2. `docs/KICKOFF.md`(첫 프롬프트) → 3. `docs/SETUP.md`(환경·스킬 설치) → 4. `docs/WORKFLOW.md`(루프) → 5. `docs/PROGRESS.md`(현재 위치) → 6. 필요 시 SPEC/ROADMAP/TECH/UNITY_CLI/ASSETS/AUDIO/LICENSE_POLICY/DECISIONS/PERF_BUDGET/GLOSSARY/RISKS_OPEN_QUESTIONS.

## 절대 규칙 (어기면 작업 중단)
1. **라이선스 무결성**: 모든 에셋(모델·텍스처·사운드·폰트·코드 패키지)은 CC0/로열티프리 상업 사용 가능만. 실존 브랜드·총기·자동차 상표/이름/로고 금지. NC/Editorial/Reference-Only 금지. 에셋 추가 시 `docs/ASSET_LEDGER.md`에 **반드시** 기록(출처 URL·라이선스·수정 여부·날짜). 기록 없는 에셋은 커밋 금지.
2. **AI 생성 에셋은 최종 빌드에 포함하지 않는다.** (임시 플레이스홀더는 `placeholder/` 폴더에 두고 릴리스 전 제거)
3. **유료 구매·결제·계정 생성·약관 동의는 하지 않는다** → 티켓에 `blocked` 라벨 + 사유 기록 후 다음 티켓.
4. **되돌릴 수 없는 작업 금지**(force push, 저장소 삭제, 대량 삭제). 사용자 원본 파일은 복사본으로 작업.
5. **게임 룰 변경은 DECISIONS.md에 ADR로 남긴 뒤에만.** SPEC.md와 충돌하면 SPEC이 이긴다(바꾸려면 ADR).
6. **성능 예산(PERF_BUDGET.md) 위반 시 머지 금지.**
7. 비밀키/라이선스 시리얼/계정 정보를 저장소에 커밋하지 않는다.
8. 한국 저작권/상표 법적 검토는 사람의 일 → 릴리스 전 체크리스트(RISKS_OPEN_QUESTIONS.md)에만 올리고 임의 판단하지 않는다.

## 작업 방식 요약
- 타이틀(기능 단위) 하나마다: **grilling → wayfinder 지도 → to-spec → to-tickets → implement(테스트 우선) → code-review → issue close → PROGRESS.md 갱신 → 다음 타이틀**. 상세는 `docs/WORKFLOW.md`.
- 사용자에게 묻지 않는다. 모호하면 가장 합리적인 해석을 택해 **DECISIONS.md에 가정으로 기록**하고 진행. (예외: 되돌릴 수 없고 양쪽 다 합리적인 결정 → 준비 후 중단하고 요약 보고)
- 무거운 작업(빌드, 테스트, 베이크)은 M1 13" 부담 때문에 **한 번에 하나씩 순차 실행**.
- 모든 Unity 작업은 `tools/unity.sh`(또는 `docs/UNITY_CLI.md`의 명령)로 **헤드리스** 수행. 에디터 GUI 클릭이 필요한 작업은 코드(Editor 스크립트)로 대체.

## 언어
- 사용자 대상 보고/문서: 한국어, 간결하게. 코드/식별자/커밋: 영어.

## 저장소 구조(목표)
```
/CLAUDE.md
/docs/            ← 이 문서들
/tools/unity.sh   ← Unity CLI 래퍼
/Assets/ /Packages/ /ProjectSettings/   ← Unity 프로젝트(루트)
/Assets/_Project/{Scripts,Editor,Scenes,Art,Audio,Prefabs,Tests}
/placeholder/     ← 임시 에셋(릴리스 전 제거)
```

# WORKFLOW — 에이전트 자율 개발 루프

## 루프 (타이틀 = 로드맵의 기능 단위 하나)
```
① grilling  →  ② wayfinder 지도  →  ③ to-spec  →  ④ to-tickets  →  ⑤ implement  →  ⑥ close  →  (다음 타이틀)
```
> 기획서 도식은 "grilling → issue open(to-spec, wayfinder-map, to-tickets)"로 묶어 표현했다. 실제 스킬 실행 순서는 위와 같다: 지도를 먼저 만들고, 그 지도(이슈 번호)를 기준으로 스펙과 티켓을 만든다.

| 단계 | 스킬 | 에이전트가 하는 일 | 산출물 |
|---|---|---|---|
| ① grilling | `grilling` (Skill 도구 호출; 답은 docs/ 에서 찾음) | **스스로에게** 질문해 모호함 제거. 답은 SPEC/DECISIONS/GLOSSARY에서 찾고, 없으면 가장 합리적인 가정을 선택 | DECISIONS.md에 가정/결정 추가 (ADR) |
| ② wayfinder 지도 | `wayfinder` (user-only → SKILL.md 직접 읽기) | 타이틀 범위의 지도 이슈 생성(`wayfinder:map` 라벨), 리서치/프로토타입/그릴링/작업 티켓 후보 도출 | 지도 이슈 #N |
| ③ to-spec | `to-spec` (user-only → SKILL.md 직접 읽기) | 지도에서 구현 스펙 문서화 | 스펙 이슈 또는 `docs/` 스펙 |
| ④ to-tickets | `to-tickets` (user-only → SKILL.md 직접 읽기) | 스펙을 **수직 슬라이스** 티켓으로 분해(라벨 `wayfinder:*`), 의존 순서 부여 | 티켓 이슈들 |
| ⑤ implement | `implement`(user-only → SKILL.md 직접 읽기) + `tdd`, `code-review`(Skill 도구 호출) | 티켓 1개씩: 테스트 먼저 → 구현 → `tools/unity.sh test` → 성능 확인 → 커밋 | 커밋/PR |
| ⑥ close | — | 모든 티켓 DoD 충족 → 이슈 close → `PROGRESS.md` 갱신 → 한국어 3줄 요약 | 갱신된 문서 |

### 스킬 호출 규칙 (ADR-022)
- **Skill 도구로 호출 가능**: `grilling`, `tdd`, `code-review`, `prototype`, `research`.
- **user-only(Skill 도구 호출 불가)**: `wayfinder`, `to-spec`, `to-tickets`, `implement`, `implement-spec`, `grill-with-docs`, `setup-matt-pocock-skills`. `~/.claude/plugins/cache/claude-plugins-official/mattpocock-skills/<버전>/skills/engineering/<이름>/SKILL.md`를 직접 Read하여 그 지침 그대로 수행한다. 스킬 내용을 임의로 바꾸지 않는다.
- grilling 단계는 `grill-with-docs` 대신 `grilling`을 쓴다.

## Definition of Done (티켓 단위)
- [ ] 테스트 작성·통과 (`tools/unity.sh test editmode`, 필요 시 playmode)
- [ ] mac 빌드 성공 (타이틀 종료 시 win 빌드도)
- [ ] `PERF_BUDGET.md` 항목 측정 후 통과 (측정값을 PR/이슈에 첨부)
- [ ] 새 에셋은 `ASSET_LEDGER.md` 기록 완료
- [ ] 룰 변경 시 `DECISIONS.md` ADR 추가, `SPEC.md` 갱신
- [ ] 용어 추가 시 `GLOSSARY.md`
- [ ] `/code-review` 지적 사항 처리
- [ ] 커밋 메시지: `type(scope): summary` + 지정 Co-Authored-By/Claude-Session 줄

## 타이틀 종료 시 추가 게이트
- 로드맵 해당 마일스톤의 게이트 조건 확인(ROADMAP.md)
- 회귀: 전체 EditMode + PlayMode 스모크 통과
- `PROGRESS.md`에 날짜·타이틀·결과·다음 타이틀·알려진 이슈 기록

## 승인이 필요한 항목 → 하지 않고 `blocked`
유료 에셋/서비스 구매, 계정 생성·로그인·약관 동의, 호스팅(VPS) 계약, 라이선스 불명 에셋 사용, 되돌릴 수 없는 파괴 작업, 법적 판단, 게임 정체성을 바꾸는 룰 변경.
`blocked` 처리: 티켓에 라벨 + 사유 + 사람이 해야 할 일 1줄 → `RISKS_OPEN_QUESTIONS.md` 반영 → 다른 티켓 진행.

## 막혔을 때
1. 같은 접근을 3회 이상 반복하지 않는다. 2회 실패 시 원인 가설을 `PROGRESS.md` "환경 이슈"에 적고 다른 접근.
2. Unity에서 코드로 불가능한 작업이면 `UNITY_CLI.md` M0 증명 항목과 대조 → 폴백 판단 ADR.
3. 컨텍스트가 길어지면 PROGRESS.md를 최신화하고 세션을 끝낸다(재개 프롬프트로 이어감).

## 자원 규칙 (M1 13")
- 빌드/테스트/베이크는 순차 1개. 동시에 Unity 2개 금지.
- 장시간 작업은 백그라운드로 돌리고 로그 폴링.
- Library/ 폴더 삭제 금지(재임포트 시간 큼).

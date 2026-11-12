# SETUP — 초기 세팅 (에이전트가 위에서 아래로 수행)

각 단계 끝에서 검증 명령을 실행하고, 실패하면 원인을 `docs/PROGRESS.md`의 "환경 이슈"에 기록한다.

## 0. 전제 확인
```bash
sw_vers; uname -m            # macOS, arm64 (Apple Silicon)
git --version; gh --version; node --version; npx --version
gh auth status               # 미로그인이면 blocked 처리(사람 필요)
```

## 1. 저장소 초기화
```bash
git init -b main 2>/dev/null || true
chmod +x tools/unity.sh
git add CLAUDE.md docs tools .gitignore .gitattributes && git commit -m "chore: bootstrap handoff docs"
```
(커밋 메시지 끝에 시스템이 지정한 Co-Authored-By/Claude-Session 줄을 붙인다.)

## 2. 스킬 설치 (필수) — Matt Pocock `mattpocock/skills` (MIT)
이 프로젝트의 루프는 다음 스킬에 의존한다: **grilling(grill-me / grill-with-docs), to-spec, wayfinder, to-tickets, implement(+tdd, code-review), setup-matt-pocock-skills**.

방법 A(플러그인, 권장):
```bash
claude plugins install mattpocock-skills
# 세션 안에서라면: /plugin install mattpocock-skills
```
방법 B(npx) — A가 실패할 때만 (두 방법을 동시에 쓰지 않는다):
```bash
npx skills@latest add mattpocock/skills --skill "grill-with-docs" --skill "grill-me" --skill "to-spec" --skill "wayfinder" --skill "to-tickets" --skill "implement" --skill "tdd" --skill "code-review" --skill "setup-matt-pocock-skills" -a claude-code -y
```
설치 확인: 세션에서 `/skills` 또는 `ListSkills`로 위 스킬들이 보이는지 확인. 하나라도 없으면 `npx skills@latest add mattpocock/skills -a claude-code -y`(전체)로 재시도. 이름이 다르면(버전 변경) 실제 목록에서 가장 가까운 스킬을 찾아 `docs/DECISIONS.md`에 매핑을 기록.

### 2-1. 스킬 구성: setup-matt-pocock-skills 실행
- 이슈 트래커: **GitHub Issues** (`gh` 사용). 불가하면 `docs/issues/` 로컬 마크다운 트래커로 대체하고 ADR 기록.
- 라벨 생성: `wayfinder:map`, `wayfinder:research`, `wayfinder:prototype`, `wayfinder:grilling`, `wayfinder:task`, `blocked`, `needs-license-review`, `perf`.
- 문서 위치: 스펙/ADR은 `docs/`, 용어는 `docs/GLOSSARY.md`.
- 스킬이 질문을 하면 사용자에게 묻지 말고 이 문서의 값으로 답한다.

## 3. Unity 설치 확인
```bash
ls "/Applications/Unity/Hub/Editor/"     # 6000.3.x 폴더가 있어야 함
```
- 버전: **6000.3.x 최신 패치 (6.3 LTS)**. Hub의 "Recommended"가 6.6 등이어도 6000.3을 사용한다. (6.0 LTS는 2026-10 지원 종료, 6.3 LTS는 2027-12까지)
- 필요 모듈: Mac Build Support (IL2CPP), Windows Build Support (IL2CPP, Mono), (선택) Dedicated Server.
- 없으면 설치 시도: `"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install --version 6000.3.<patch> --changeset <changeset> --module mac-il2cpp --module windows-il2cpp --module windows-mono`
  (Hub CLI 실패/로그인 필요 시 → `blocked`, 사람에게 보고)
- 라이선스: 개인 라이선스 GUI 활성화는 사람 몫. 헤드리스 활성화가 필요하면 `docs/UNITY_CLI.md` "라이선스" 참조.

## 4. Unity 프로젝트 생성 (헤드리스)
```bash
tools/unity.sh create        # 프로젝트가 없을 때 -createProject 실행 (URP 3D 템플릿 시도 → 실패 시 기본 3D + URP 패키지 추가)
tools/unity.sh run Setup.ConfigureProject   # Editor 스크립트로 URP/Fish-Net/Test Framework 설정
```
- 패키지: Universal RP, Input System, Cinemachine, Test Framework, Addressables(선택), Fish-Net(OpenUPM 또는 GitHub UPM URL → `Packages/manifest.json` 직접 편집).
- 결정과 근거는 `docs/DECISIONS.md` ADR-001~에 기록.

## 5. 검증 (M0 게이트의 일부)
```bash
tools/unity.sh test editmode   # 빈 테스트 1개 통과
tools/unity.sh build mac       # 빈 씬 맥 빌드 성공
tools/unity.sh build win       # 빈 씬 윈도우 빌드 성공(교차 빌드)
```
실패 시 `docs/UNITY_CLI.md` 트러블슈팅. 3개 모두 통과해야 M0-1 완료.

## 6. 세팅 완료 보고
`docs/PROGRESS.md`의 "환경" 표를 채운다(Unity 버전, 스킬 목록, 트래커, 빌드 결과). 그 후 ROADMAP M0로.

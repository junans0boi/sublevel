# ASSET_LEDGER — 에셋 대장 (모든 에셋 필수 기록)

규칙: 행이 없는 에셋은 커밋 금지. 번들은 번들 단위 + 사용 파일 목록. 라이선스 원문/스크린샷은 `docs/licenses/<id>/`에 보관.

| ID | 분류 | 이름 | 출처 URL | 라이선스 | 크레딧 필요 | 수정 여부 | 프로젝트 경로 | 사용 구역/용도 | 확인일 | 확인자 |
|---|---|---|---|---|---|---|---|---|---|---|
| (예시) A-0001 | 텍스처 | Concrete 034 | https://ambientcg.com/... | CC0 | 아니오 | 해상도 축소 | Assets/_Project/Art/Materials/... | D 바닥 | 2026-MM-DD | agent |

## 분류 코드
A=환경/머티리얼, W=무기, C=캐릭터, N=애니메이션, V=차량, S=사운드, M=음악, F=폰트, U=UI, P=코드 패키지

## 검증 스크립트 요구 (M0-7)
`Assets/_Project/Editor/AssetValidator`가 `Assets/_Project/Art|Audio` 하위 폴더가 ledger에 존재하는지 검사하고, 없으면 빌드 실패.

## 코드 패키지 (P)
| ID | 분류 | 이름 | 출처 URL | 라이선스 | 크레딧 필요 | 수정 여부 | 프로젝트 경로 | 사용 구역/용도 | 확인일 | 확인자 |
|---|---|---|---|---|---|---|---|---|---|---|
| P-0001 | 코드 패키지 | Fish-Net 4.7.3 | https://github.com/FirstGearGames/FishNet | 커스텀(무료·로열티프리·게임 사용 허용, 경쟁 네트워킹 제품 금지). **needs-license-review** | 확인 필요 | 아니오 | Packages/manifest.json (git, 태그 4.7.3) | 넷코드 | 2026-11-12 | agent |
| P-0002 | 코드 패키지 | Universal RP 17.6.0 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 렌더링 | 2026-11-12 | agent |
| P-0003 | 코드 패키지 | Input System 1.20.1 | Unity Package Manager | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 입력 | 2026-11-12 | agent |
| P-0004 | 코드 패키지 | Cinemachine 6.6.0 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 카메라 | 2026-11-12 | agent |
| P-0005 | 코드 패키지 | Test Framework 1.8.0 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 테스트 | 2026-11-12 | agent |

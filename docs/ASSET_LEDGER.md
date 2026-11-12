# ASSET_LEDGER — 에셋 대장 (모든 에셋 필수 기록)

규칙: 행이 없는 에셋은 커밋 금지. 번들은 번들 단위 + 사용 파일 목록. 라이선스 원문/스크린샷은 `docs/licenses/<id>/`에 보관.

| ID | 분류 | 이름 | 출처 URL | 라이선스 | 크레딧 필요 | 수정 여부 | 프로젝트 경로 | 사용 구역/용도 | 확인일 | 확인자 |
|---|---|---|---|---|---|---|---|---|---|---|
| (예시) A-0001 | 텍스처 | Concrete 034 | https://ambientcg.com/... | CC0 | 아니오 | 해상도 축소 | Assets/_Project/Art/Materials/... | D 바닥 | 2026-MM-DD | agent |

## 분류 코드
A=환경/머티리얼, W=무기, C=캐릭터, N=애니메이션, V=차량, S=사운드, M=음악, F=폰트, U=UI, P=코드 패키지

## 검증 스크립트 요구 (M0-7)
`Assets/_Project/Editor/AssetValidator`가 `Assets/_Project/Art|Audio` 하위 폴더가 ledger에 존재하는지 검사하고, 없으면 빌드 실패.

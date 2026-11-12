# ASSET_LEDGER — 에셋 대장 (모든 에셋 필수 기록)

규칙: 행이 없는 에셋은 커밋 금지. 번들은 번들 단위 + 사용 파일 목록. 라이선스 원문/스크린샷은 `docs/licenses/<id>/`에 보관.

| ID | 분류 | 이름 | 출처 URL | 라이선스 | 크레딧 필요 | 수정 여부 | 프로젝트 경로 | 사용 구역/용도 | 확인일 | 확인자 |
|---|---|---|---|---|---|---|---|---|---|---|
| (예시) A-0001 | 텍스처 | Concrete 034 | https://ambientcg.com/... | CC0 | 아니오 | 해상도 축소 | Assets/_Project/Art/Materials/... | D 바닥 | 2026-MM-DD | agent |
| A-0001 | 텍스처(PBR) | Concrete 034 (1K) | https://ambientcg.com/view?id=Concrete034 | CC0 1.0 | 아니오 | 파일명 변경, 러프니스→스무스니스 마스크 패킹(`_mask.png`) | Assets/_Project/Art/Materials/Concrete034 | 테스트 맵 벽 | 2026-11-12 | agent |
| A-0002 | 텍스처(PBR) | Painted Metal 004 (1K) | https://ambientcg.com/view?id=PaintedMetal004 | CC0 1.0 | 아니오 | 파일명 변경, 메탈릭·스무스니스 마스크 패킹, 컬러맵 채도 45%·명도 50%로 낮춤 | Assets/_Project/Art/Materials/PaintedMetal004 | 테스트 맵 강조색 소품(엄폐물) | 2026-11-12 | agent |
| A-0003 | 텍스처(PBR) | Metal Plates 006 (1K) | https://ambientcg.com/view?id=MetalPlates006 | CC0 1.0 | 아니오 | 파일명 변경, 메탈릭·스무스니스 마스크 패킹 | Assets/_Project/Art/Materials/MetalPlates006 | 테스트 맵 엄폐물·벽 | 2026-11-12 | agent |
| A-0004 | HDRI | Overcast Soil (Pure Sky) 2K | https://polyhaven.com/a/overcast_soil_puresky | CC0 1.0 | 아니오 | 아니오(임포트 시 큐브맵 변환) | Assets/_Project/Art/HDRI/OvercastSoilPuresky | 테스트 맵 하늘·환경광 | 2026-11-12 | agent |
| A-0005 | 텍스처(PBR) | Concrete 015 (1K) | https://ambientcg.com/view?id=Concrete015 | CC0 1.0 | 아니오 | 파일명 변경, 러프니스→스무스니스 마스크 패킹(`_mask.png`) | Assets/_Project/Art/Materials/Concrete015 | 테스트 맵 바닥 | 2026-11-12 | agent |
| A-0006 | 텍스처(PBR) | Metal 032 (1K) | https://ambientcg.com/view?id=Metal032 | CC0 1.0 | 아니오 | 파일명 변경, 메탈릭·스무스니스 마스크 패킹 | Assets/_Project/Art/Materials/Metal032 | 테스트 맵 벽·구조물 | 2026-11-12 | agent |
| W-0001 | 무기 모델 | Quaternius Ultimate Guns Pack — 사용 파일: Bullpup_1.fbx | https://quaternius.com/packs/ultimategun.html | CC0 1.0 | 아니오 | 아니오. 상표·각인·텍스처 없음(단색 머티리얼), 게임 내 명칭은 오리지널(ADR-032) | Assets/_Project/Art/Weapons/QuaterniusUltimateGuns | 무기 외형 | 2026-11-12 | agent |
| C-0001 | 캐릭터+애니메이션 | Quaternius Ultimate Modular Men Pack — 사용 파일: Swat.fbx (애니메이션 24개 내장) | https://quaternius.com/packs/ultimatemodularcharacters.html | CC0 1.0 | 아니오 | 모델 파일은 그대로. 임포트 시 노멀 재계산(75°), 머티리얼은 프로젝트 머티리얼로 대체 | Assets/_Project/Art/Characters/QuaterniusModularMen | 플레이어·더미 외형 | 2026-11-12 | agent |

## 분류 코드
A=환경/머티리얼, W=무기, C=캐릭터, N=애니메이션, V=차량, S=사운드, M=음악, F=폰트, U=UI, P=코드 패키지

## 검증 스크립트 요구 (M0-7)
`Assets/_Project/Editor/AssetValidator`가 `Assets/_Project/Art|Audio` 하위 폴더가 ledger에 존재하는지 검사하고, 없으면 빌드 실패.

## 코드 패키지 (P)
| ID | 분류 | 이름 | 출처 URL | 라이선스 | 크레딧 필요 | 수정 여부 | 프로젝트 경로 | 사용 구역/용도 | 확인일 | 확인자 |
|---|---|---|---|---|---|---|---|---|---|---|
| P-0001 | 코드 패키지 | Fish-Net 4.7.3 | https://github.com/FirstGearGames/FishNet | 커스텀(무료·로열티프리·게임 사용 허용, 경쟁 네트워킹 제품 금지). **needs-license-review** | 확인 필요 | 아니오 | Packages/manifest.json (git, 태그 4.7.3) | 넷코드 | 2026-11-12 | agent |
| P-0002 | 코드 패키지 | Universal RP 17.3.0 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 렌더링 | 2026-11-12 | agent |
| P-0003 | 코드 패키지 | Input System 1.20.1 | Unity Package Manager | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 입력 | 2026-11-12 | agent |
| P-0004 | 코드 패키지 | Cinemachine 3.1.4 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 카메라 | 2026-11-12 | agent |
| P-0005 | 코드 패키지 | Test Framework 1.6.0 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 테스트 | 2026-11-12 | agent |

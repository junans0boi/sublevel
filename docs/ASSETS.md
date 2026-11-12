# ASSETS — 에셋 정책과 소스 (리서치 결과)

원칙: **법적 무결성 최우선**. 모든 사용 에셋은 `ASSET_LEDGER.md`에 기록. 라이선스 판단이 모호하면 사용하지 말고 `needs-license-review`.

## 1. 승인된 소스
| 소스 | 라이선스 | 용도 | 비고 |
|---|---|---|---|
| Poly Haven | CC0 | HDRI, PBR 텍스처, 모델 | 다운로드 시 페이지 라이선스 재확인 |
| ambientCG | CC0 | PBR 머티리얼(2000+) | 구역별 바닥/벽/금속 |
| Kenney | CC0 | 소품, 차량(Car Kit 45종), UI, 사운드 | 스타일이 카툰이면 소품 한정 |
| Quaternius | CC0 | Universal Base Characters, Universal Animation Library 1&2, 50+ LowPoly Guns | 캐릭터/애니메이션/총기 핵심 후보 |
| KayKit | CC0 | 소품/환경 | 스타일 확인 후 선택 |
| OpenGameArt / itch.io / Sketchfab | **파일별** | CC0·CC-BY만 | CC-BY는 크레딧 필수. NC/Editorial 제외 |
| Stein "Free Classic Weapons Pack", 3dmodelscc0 "Guns & Explosives" | CC0 | 총기·폭발물 | **실존 총 형상** → 이름/각인 제거·형상 수정 후 사용 |
| RenderCrate Modern Industrial Pack | 커스텀(Extended Use License) | 공장 소품 | **약관 읽고 확인 후**만 |
| MakeHuman | CC0 export | 캐릭터 베이스 | 누드 베이스 → 의상 제작 필요 |
| Mixamo | 중간 위험 | 애니메이션 | 계정/약관 필요 → **blocked 기본** |
| Fab / Unity Asset Store | 에셋별 | 무료/유료 | 에셋별 라이선스 확인, 유료는 승인 필요 |

## 2. 금지/보류
- **MetaHuman: 보류** (Epic 공식 EULA 확인 전 사용 금지)
- PSX Misc Gun Pack 등 **비상업(NC) 표기** 에셋
- 라이선스 불명, 출처 불명, 재배포 금지 에셋
- AI 생성 에셋을 최종 빌드에 포함
- 실존 상표(총기 제조사, 자동차 브랜드, 기업 로고) 포함 에셋

## 3. 총기 (MVP 7종 매핑)
| 슬롯 | 후보 소스 | 처리 |
|---|---|---|
| 권총 | Quaternius 50+ LowPoly Guns / Stein 팩 | 이름·각인 제거, 오리지널 명칭 |
| SMG | 〃 | 〃 |
| 돌격소총 | 〃 | 〃 |
| 샷건 | 〃 | 〃 |
| 저격소총 | (출시 전) | MVP 제외 |
| 투척물 | 3dmodelscc0 Guns & Explosives 등 | 파편/연막 |
| 유틸 | 직접 제작/CC0 소품 | 힐, 방어구, 열쇠, 칩 |
- 로우폴리 느낌이 강한 팩은 **텍스처/노멀/머티리얼을 PBR로 재작업**해 세미리얼 톤으로 통일. 어려우면 Blender로 직접 모델링(오리지널).

## 4. 캐릭터·애니메이션
- Quaternius Universal Base Characters + Universal Animation Library 1&2(CC0) 우선. 의상/장비는 CC0 소품 조합 또는 오리지널.
- 리깅 호환성은 M0/M1에서 검증(Humanoid 리타깃).

## 5. 맵·환경
- 구역별 머티리얼은 ambientCG/Poly Haven PBR, 하늘/조명은 Poly Haven HDRI.
- 공장(D): 산업 소품 CC0 + (검토 후) RenderCrate. 산악(A): 지형 + Poly Haven 암석/식생.
- 구역 레이아웃은 **스크립트로 생성 가능하게**(UNITY_CLI.md 원칙).

## 6. 차량(후순위, M5 이후)
- Kenney Car Kit(CC0, 45), Quaternius LowPoly Cars, Kenney Toy Car Kit 또는 **Blender 오리지널 디자인**.
- 국산차 요청이 있었으나 **브랜드명·로고·고유 디자인 모방 금지**(상표/디자인권 리스크). 일반 명칭(세단, 해치백, SUV, 픽업)만 사용.

## 7. 폰트/UI/아이콘
- SIL OFL 또는 CC0만(예: Google Fonts의 OFL). 대장에 기록.

## 8. 다운로드 절차 (에이전트 자율)
1. 소스 페이지에서 **라이선스 문구 확인**(스크린샷/텍스트를 ledger에 요약).
2. `Assets/_Project/Art/<카테고리>/<에셋>/` 에 저장, 원본 `LICENSE.txt` 동봉.
3. 임포트 설정(KTX2, LOD, 읽기 비활성)은 임포트 파이프라인(M0)이 일괄 처리.
4. `ASSET_LEDGER.md` 행 추가 → 검증 스크립트 통과 → 커밋.
- 네트워크 허용 목록 때문에 다운로드가 막히면 **blocked**(사람이 파일 제공).

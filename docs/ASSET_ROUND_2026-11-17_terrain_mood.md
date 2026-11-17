# 에셋 라운드 후보: 지형 분위기 2단계 (2026-11-17)

신규 에셋은 **다운로드하지 않았다.** 준환이 로그인·승인하는 라운드에서 고른다(법적 최종 판단은 사람, CLAUDE.md 규칙 8). 근거: 레퍼런스 `docs/research/map_mood_reference.md`(맵 세션 브랜치 7852b2e).
라이선스 기준: CC0 또는 재배포 가능, AI 생성물 아님. 사이트 약관은 `docs/ASSET_ROUND_2026-11-16.md`의 확인(Poly Haven·ambientCG CC0, Quaternius CC0)을 따른다. **팩별 라이선스·내용은 아래 '확인' 열대로, 미확인은 다운로드 전 해당 페이지에서 확인.**

| # | 필요 | 후보 | 출처 | 라이선스 | 확인 |
|---|---|---|---|---|---|
| 1 | 붉은 흙·마른 풀 텍스처 | ambientCG의 Ground/Clay/Grass 범주에서 붉은 점토·마른 풀 계열 고르기 | https://ambientcg.com/list?category=Ground | CC0(사이트 약관, 2026-11-16 확인) | 목록 페이지에서 붉은/마른 항목 ID를 찾지 못함(미확인). 라운드 때 직접 보고 ID 선택 |
| 2 | 같은 용도 | Poly Haven 지면 텍스처(red soil / dry grass 계열) | https://polyhaven.com/textures | CC0(2026-11-16 확인) | 개별 에셋 ID 미확인 |
| 3 | 바위 메시(판상·절리), 나무·덤불·풀 | Quaternius Stylized Nature MegaKit (Standard 무료 판) | https://quaternius.com/packs/stylizednaturemegakit.html | CC0 "개인·교육·상업 프로젝트"(페이지 확인 2026-11-17) | 116 모델(나무 40·식물/꽃 35·바위 27·풀·덤불). 야자수는 페이지에 언급 없음(미확인). 무료 Standard는 전체의 60~70% |
| 4 | 나무·바위·식생 보강, 야자수 가능성 | Kenney Nature Kit | https://kenney.nl/assets/nature-kit | CC0(페이지 확인 2026-11-17) | 파일 330개, 태그 nature/tree/rock/foliage. 야자수·절벽·덤불 포함 여부 미확인 |
| 5 | 야자수(해안) | 위 3·4에 없으면 Quaternius Ultimate Nature 계열 또는 Poly Haven 모델 | quaternius.com / polyhaven.com/models | 각 CC0 표기, 개별 확인 필요 | 야자 모델 존재 미확인 |

작업량 메모: 3·4는 저폴리 스타일이라 현재 잎 카드 식생(스캔 텍스처)과 화풍이 다르다. 한 세트로 통일하거나 바위 메시만 쓰는 것을 제안. 텍스처(1·2)는 `WorldMaterials`의 레이어 교체만으로 들어가며 Match 틴트(materials.json) 재측정이 필요하다. 모든 에셋은 `docs/ASSET_LEDGER.md` 기록 후 커밋.

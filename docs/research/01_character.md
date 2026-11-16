# 조사 #01 — 사실적 비율 캐릭터 (Humanoid + Mixamo 직결)

확인일: 2026-11-16. 법적 최종 판단은 사람(CLAUDE.md 규칙 8). 요약 도구 경유 인용은 "(원문 대조 필요)" 표시.

## 0. 현재 상태 (코드/저장소 확인)
- 현 캐릭터: `Art/Characters/QuaterniusModularMen/Swat.fbx` (Generic, 뼈 이름 `Wrist.R`). Mixamo 클립은 `MixamoBake.cs`로 오프라인 리타게팅.
- 무기 소켓: `Editor/CharacterKit.cs:15` `HandBone = "Wrist.R"` 한 곳에서 이름으로 찾아 무기·근접 모델을 손에 붙임 (`:389`). 교체 시 이 상수와 `MeleeKit.AttachToHand`만 Humanoid `RightHand` 기준으로 바꾸면 됨.
- 피격 부위: **뼈 기반이 아님.** `HitZones.At(hit.point.y - 발높이, EyeHeightOffset, zones)` (Combatant.Ballistics.cs:114, Combatant.cs:464) = 높이 기반. 따라서 새 캐릭터는 **키(눈높이)와 캡슐 높이만 맞추면** 되고 HitZone 뼈 의존 없음. `HumanBodyBones`/`GetBoneTransform` 사용 코드는 Scripts 안에 0건.
- **이미 저장소에 있음(미커밋)**: `Art/Characters/QuaterniusUniversal/BaseCharacters/` = Universal Base Characters **Standard(무료)**. `Superhero_Male_FullBody.fbx`, `Superhero_Female_FullBody.fbx`, 피부 텍스처(Dark/Light), 머리카락 2종, 눈. 임포트 설정 `animationType: 2`(= Humanoid)로 이미 되어 있음. FBX 문자열 확인: `spine_01~03, neck_01, Head, hand_r, index_01~04` 등 손가락 뼈 포함 (Humanoid 자동 구성 가능성 높음, 에디터에서 Avatar 유효성 실측 필요).

## 1. 후보

### 1a. Mixamo 자체 캐릭터 (내장 브라우저 패널에서 확인. 로그인 상태, 클릭·다운로드·약관 동의 없음)
카탈로그 108종(3페이지). 목록 화면은 폴리 수·텍스처 정보를 보여주지 않음 → **폴리/텍스처 임베드 여부는 미확인**(개별 캐릭터 선택·다운로드 시에만 확인 가능, FBX 다운로드는 사람 승인 필요). Claude in Chrome은 이번에도 사용하지 않음(내장 패널로 충분).
현대/사실풍 후보(이름만 확인, 외형은 썸네일 미검토):
- 군/전술: **Swat Guy, Swat**(현재 에셋과 동일 계열의 Mixamo 원본), **Alien Soldier**(SF, 제외), **Gas Mask**, **Survivor A Lusth**, **Prisoner B Styperek**, **Copzombie**(좀비, 제외)
- 민간 남성: Abe, Brian, Bryce, Brady, Jones, Joe, Josh, Chad, Alex, Roth, Drake, Maynard, Pete, Lewis, David, Ortiz, Steve, James, Leonard, Ty, Morak(?), Racer, Crypto(확인 필요)
- 민간 여성: Amy, Jennifer, Shannon, Jody, Martha, Michelle, Sophie, Jackie, Elizabeth, Suzie, Claire, Kate, Megan, Olivia, Louise, Lola, Girlscout, Sporty Granny, Aj, Kaya, Arissa
- 마네킹: Y Bot, X Bot, Mannequin, Dummy (플레이스홀더/실루엣용)
- 판타지/크리처/좀비 다수(제외).
**장점**: 표준 Mixamo 뼈대(`mixamorig:*`, 손가락 15뼈 포함) → Humanoid 자동 구성 + Mixamo 클립 **직접 사용**(베이크 불필요). 한 계정에서 다양한 인종·성별. **단점**: 인상이 "Mixamo 기본 캐릭터"로 널리 알려짐, 텍스처 해상도 중간(대체로 1~2K), 폴리 수 미확인, 재배포 제한(아래).

### 1b. Quaternius Universal Base Characters (UBC) — 이미 보유
- https://quaternius.com/packs/universalbasecharacters.html (확인 2026-11-16, 요약 도구 → 원문 대조 필요): CC0, 개인·교육·상업 허용. 평균 약 13k tris, Humanoid 리그, UAL과 호환. 페이지상 6개 베이스(Superhero/Regular/Teen × 남/여) + 머리 20종, **Standard(무료)는 전체의 60~70%**. 저장소 Standard 라이선스 파일: "일부 모델만 포함". 실제 보유분은 Superhero 남/녀 2종뿐.
- 장점: CC0(크레딧·재배포 제약 없음), 13k tris(예산 안), UAL 클립(이미 UAL1/UAL2 임포트)과 같은 뼈대 → 리타게팅 불필요. 머리·피부 텍스처 교체로 다양성.
- 단점: "Superhero" 체형은 이상화/스타일화 비율, 현대 군복 의상 없음(의상은 별도 모듈형 팩/Source 유료 필요). **Mixamo 클립은 뼈 이름이 달라 Humanoid 리타게팅으로 사용** (Humanoid끼리는 Unity 런타임 리타겟이므로 베이크 없이 가능).

### 1c. 기타
| 후보 | 라이선스 | 판정 |
|---|---|---|
| Kenney 캐릭터 (kenney.nl) | CC0 | 로우폴리 스타일화 → 방향(사실 비율)에 안 맞음. 비추천 |
| Quaternius Ultimate Modular Men/Women 등 | CC0 | 현재 Swat과 같은 스타일화 계열. 비추천 |
| Unity Asset Store 무료 (Free Scavenger 20.9k tris, Insurgent LITE 1.7k tris) | Asset Store EULA (https://unity.com/legal/as-terms, 2024-12-04) | 외형 부적합/이름 상표 위험(`ASSET_ROUND` 기록). 비추천 |
| Sketchfab CC0/CC-BY | 개별 확인 필요 | 리깅 품질·Humanoid 호환 편차 큼, 건별 심사 비용. 보조 수단으로만 |
| Ready Player Me | 서비스 종료 | **제외.** Netflix 인수, 2026-01-31 공개 서비스·API 종료 (https://genies.com/blog/ready-player-me-shutdown , 확인 2026-11-16) |
| MakeHuman / CC0 휴먼 생성기 | CC0 출력(MakeHuman 1.x 출력물 CC0 표기, 원문 확인 필요) | 오프라인 대량 변형 생산 가능. 리깅은 Blender 작업 필요, 2차 수단 |
| AI 생성 캐릭터 | — | **금지**(규칙 2) |

## 2. 라이선스 판정 (LICENSE_POLICY.md / CLAUDE.md 대조)
| 후보 | 판정 | 근거·조건 |
|---|---|---|
| Quaternius UBC / UAL | **허용(CC0)** | ledger 기록 필요(이미 기록 여부 확인). 크레딧 불요 |
| Mixamo 캐릭터+애니 | **조건부 허용 → 사람 검토 필요** | Adobe FAQ https://helpx.adobe.com/kr/creative-cloud/faq/mixamo-faq.html: 개인·상업·비영리, 비디오 게임 제작 로열티 프리. 커뮤니티 해석: 게임에 포함은 OK, **원본 FBX 단독 재배포는 금지**(https://community.adobe.com/t5/mixamo-discussions/mixamo-faq-licensing-royalties-ownership-eula-and-tos/td-p/13234775). 저장소는 **비공개 유지**, 빌드에는 번들. **Adobe Additional Terms 원문 미확인** → RISKS_OPEN_QUESTIONS의 사람 검토 항목으로 유지. 규칙 3(약관 동의 금지)상 Mixamo 사용 이력은 이미 있음(`Mixamo/LICENSE.txt`), 추가 약관 동의 화면이 나오면 중단 |
| Mixamo 캐릭터의 이름·로고 | 문제 없어 보임 | "Swat" 등 이름은 일반 명칭. 총기·상표 텍스처 포함 여부는 임포트 후 육안 확인 |
| Asset Store 무료 | 조건부 | 저장소 보관·백업 §5.2 해석 필요, 비추천이라 보류 |
| Ready Player Me | 불가 | 서비스 종료 |

AI 생성 에셋: Mixamo의 "Auto-rig"로 **사용자가 올린 캐릭터**를 리깅하는 경로가 있으나, 모델 출처가 AI면 금지. 우리 모델(CC0)만 업로드할 것.

## 3. 다양성 (몇 종, 어떻게)
- **UBC만**: 현재 2종(남/여 Superhero) × 피부 2 × 머리 2 = 8 조합. Regular/Teen 체형은 유료 Source 필요(가격 페이지 텍스트 미확인; 규칙 3상 구매 금지 → `blocked` 처리).
- **Mixamo**: 사실풍 20~30종을 무료로 다운로드 가능(사람이 대량 선별 승인 후). 개별 모델마다 의상이 고정(의상 교체 불가). 팀/봇 색 구분은 **머티리얼 틴트**(`MaterialPropertyBlock`, `_BaseColor` 곱 또는 마스크 텍스처 없을 땐 의상 영역 UV 분리 불가 → 전체 약한 틴트 + 팀 색 완장/머리띠 소품)로.
- 권장 조합: **Mixamo 사실풍 8~12종(남녀·체형 혼합) = 봇/상대**, **UBC 2종 = 플레이어 기본 스킨/아바타 변형**(머리·피부 조합으로 8개). 합쳐 20종 이상이면 100명 매치에서도 충분히 다양. 중복 허용 + 틴트.
- 의상 스왑(모듈식): Mixamo는 불가. UBC는 무료판에 의상 없음. 자체 소품(헬멧, 조끼, 가방)은 `Head`/`Spine` 소켓에 CC0 소품을 붙이는 방식이 현실적.

## 4. 기술 적합성
- **Humanoid**: Mixamo는 표준 지원(Unity 임포터 Humanoid → Configure에서 매핑 자동 성공이 일반적). UBC는 `animationType: 2` 이미 설정, 손가락 뼈 있음 → Avatar `isValid` 실측 필요.
- **손가락/무기 그립**: 둘 다 손가락 뼈 보유(Mixamo 15뼈, UBC `index_01~04` 등 확인). 3인칭 그립은 Humanoid `RightHand` 자식 소켓 + 필요 시 `AnimatorIK`(손 위치/회전) 또는 Mixamo 소총 클립 그대로. 왼손 보조 그립은 `SetIKPosition(LeftHand)`로 가능(현재 구현 여부는 애니 감사 트랙에서).
- **소켓**: Humanoid API `animator.GetBoneTransform(HumanBodyBones.RightHand/Head/Spine/Chest)`로 이름 의존 제거. 현재 코드는 이름 문자열 하나(`Wrist.R`)뿐이라 교체 비용 작음.
- **피격 부위**: 높이 기반이므로 영향 없음. 단, `EyeHeightOffset`, CharacterController 높이, 서있기/앉기/엎드리기 높이를 새 비율(보통 175cm 전후, 머리 1/7.5)에 맞춰 재검증. 헤드샷 존이 실제 머리 높이와 일치하는지 육안 캡처 확인.
- **폴리 예산**: PERF_BUDGET 삼각형(화면 내) 목표 ≤ 1.5M. 캐릭터 13k × 가시 40명 = 0.52M(34%) → 부담 큼. **LOD 필수**: LOD0 ≤ 15k(근거리 ≤ 15m, 최대 8명), LOD1 ~5k(≤ 40m), LOD2 ~1.5k(≤ 100m), 원거리 컬. 가시 평균 환산 ≈ 8×15k + 20×5k + 40×1.5k = 0.28M. Mixamo 모델의 폴리가 15k 초과면 Blender 데시메이트(CC0/Mixamo 수정 허용 범위, 수정 기록)나 Unity `Mesh LOD`(Unity 6 지원 `MeshLodGenerator`/Simplygon 아님, 내장 LOD Group + 자체 데시메이트)가 필요. 스킨 메시는 GPU 스키닝 + 서버 헤드리스는 애니메이터 `AlwaysAnimate` 유지(CharacterKit.cs:413 주석 기준) → 서버 100명 비용 별도 실측.
- 텍스처: 1~2K, 4K 금지(규칙). Mixamo 텍스처 4K가 섞이면 임포트 규칙이 막으므로 다운스케일.
- Mixamo 클립 재사용: 현재 `Mixamo/*.fbx`와 `MixamoBake.cs` 베이크 산출물은 Mixamo 캐릭터로 교체 시 **베이크 불필요**(Humanoid 클립 직접). UBC 사용 시에도 Humanoid 리타겟이라 베이크 불필요.

## 5. 추천 (순위)
1. **Mixamo 사실풍 캐릭터 풀(Humanoid + Mixamo 클립 직결) + 현재 보유 UBC 2종을 보조 스킨으로.** 이유: 사용자 요구(사실 비율, Humanoid, Mixamo 직결)에 직접 대응, 종류가 가장 많음, 뼈 매핑·클립이 이미 같은 표준. 리스크는 Additional Terms 미확인(사람 검토)과 폴리/텍스처 미확인 → 먼저 1~2종 시험 임포트로 검증.
2. **폴백: UBC(CC0) 단독** (Superhero 2종, 8 변형 + 소품). 이유: 라이선스가 가장 깨끗, 이미 저장소에 있고 Humanoid. 단점은 이상화 체형과 다양성 한계.

## 6. 체크리스트 (순서대로. 다른 트랙인 맵·산·맵 에셋·애니 시스템 감사·누락 감사와 겹치지 않음)

### 조사
- [ ] **R1 Mixamo 후보 선별 시트** — 누가: 준환(또는 Claude, 내장 패널 읽기만) / 방법: Mixamo Characters 108종 중 사실풍 12~16종을 썸네일로 고르고 `docs/ASSET_ROUND`에 이름 기록 / 검증: 목록의 이름이 위 1a에 있고, 판타지·좀비·SF·실존 상표 표시 캐릭터가 없음
- [ ] **R2 Mixamo Additional Terms 원문 읽기** — 누가: 준환(사람) / 방법: https://helpx.adobe.com/legal/product-descriptions/mixamo.html 등 Adobe 약관 PDF 열람, 재배포·모드·빌드 번들 조항 확인 / 검증: RISKS_OPEN_QUESTIONS에 "확인함/결론" 한 줄. 미확인이면 Mixamo 에셋은 출시 빌드 보류
- [ ] **R3 UBC Standard 원문 대조** — 누가: Claude / 방법: https://quaternius.com/packs/universalbasecharacters.html 페이지 직접 열람(요약 도구 아님)해 CC0 문구·포함 모델 수 캡처 / 검증: ASSET_LEDGER 행에 URL·날짜
- [ ] **R4 UBC 유료 Source 필요성 판단** — 누가: 준환 / 방법: Regular·Teen 체형이 필요한지 결정(결제는 사람만, 규칙 3) / 검증: 결정 결과 DECISIONS에 가정으로 기록

### 구현
- [ ] **I1 Mixamo 시험 캐릭터 1종 임포트** (파일 추가이므로 사전 확인 필요) — 누가: 준환이 Mixamo에서 FBX with Skin 다운로드(Claude는 다운로드 금지) / 방법: `Art/Characters/Mixamo/Characters/`에 넣고 Humanoid 설정 / 검증: Avatar `isValid == true`, 삼각형 수·텍스처 해상도·상표 텍스처 유무 기록
- [ ] **I2 UBC Humanoid 유효성 테스트** — 누가: Claude(EditMode 테스트) / 방법: `Superhero_Male/Female_FullBody.fbx`의 Avatar `isHuman`, 손가락 본 매핑, 키(m) 측정 / 검증: 테스트 통과 로그 + 높이/눈높이 값 기록
- [ ] **I3 키·캡슐·눈높이 정합** — 누가: Claude / 방법: 새 캐릭터 키 기준으로 `EyeHeightOffset`, 앉기/엎드리기 캡슐, 1인칭 카메라 위치 재산정 후 HitZones 테스트 갱신 / 검증: 기존 HitZones 유닛 테스트 + 머리/몸/다리 레이 캡처 3장
- [ ] **I4 소켓 일반화** — 누가: Claude / 방법: `CharacterKit.cs`의 `HandBone` 문자열 → `animator.GetBoneTransform(HumanBodyBones.RightHand)`로 교체, MeleeKit 동일 / 검증: 무기 4종 + 근접 장착 위치 캡처(3인칭·1인칭 각 1장), 테스트 통과
- [ ] **I5 클립 직결 전환** — 누가: Claude / 방법: Character.controller가 `MixamoBake` 산출물 대신 Humanoid 원본 Mixamo 클립(`Art/Characters/Mixamo/*.fbx`, Humanoid 설정)을 참조 / 검증: 이동·조준·재장전·피격·사망 재생 캡처, 오프라인 베이크 단계 삭제 가능 확인 (애니 시스템 감사 트랙과 범위 구분: 여기서는 "클립 참조 교체"만)
- [ ] **I6 LOD 3단계 + 컬링 + 예산 측정** — 누가: Claude / 방법: LOD Group(LOD0≤15k, LOD1≈5k, LOD2≈1.5k, 100m 컬), 봇 100명 시나리오 `PerfBaselineTests` 방식 측정 / 검증: 삼각형 ≤ 1.5M, 렌더 p99 ≤ PERF_BUDGET 목표, 서버 헤드리스 CPU 기록
- [ ] **I7 변형·틴트 시스템** — 누가: Claude / 방법: 캐릭터 풀 데이터(ScriptableObject)에 프리팹+머티리얼 틴트+소품 슬롯, 봇/플레이어 스폰 시 시드 기반 선택 / 검증: 20명 스폰 캡처에서 동일 모델이 인접하지 않고 팀 색 식별 가능
- [ ] **I8 ASSET_LEDGER·CREDITS 갱신** — 누가: Claude / 방법: 각 캐릭터별 출처 URL, 라이선스, 수정(데시메이트 등) 여부, 날짜 기록; Mixamo는 비-CC0 표기 / 검증: 기록 없는 에셋 0, `Quaternius Swat`는 삭제 대신 `placeholder/` 이동 후 릴리스 전 제거 목록에 추가
- [ ] **I9 Swat 구버전 제거 및 회귀 캡처** — 누가: Claude / 방법: 프리팹(Player/Bot/TrainingDummy)을 새 캐릭터로 교체 후 PlayMode 테스트 전체 + 대표 샷 / 검증: 테스트 전부 통과, 기존 캡처와 비교해 총기 위치·머리 위치 오차 없음

## 7. 불확실한 점
- Mixamo 개별 캐릭터의 폴리 수·텍스처는 목록에서 확인 불가(다운로드 필요).
- UBC 가격·유료 모델 구성은 웹 요약에 없음.
- Kenney/MakeHuman 라이선스는 본 조사에서 원문 미확인(비추천이라 생략).

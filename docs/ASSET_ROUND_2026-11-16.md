# 에셋 라운드 조사 기록 (2026-11-16)

확인일: 2026-11-16. 법적 최종 판단은 사람(CLAUDE.md 규칙 8) → RISKS_OPEN_QUESTIONS 체크리스트 대상.
주의: 일부 인용은 요약 도구 경유(원문 대조 필요)로 표시했다.

## 로그인 상태 확인 (비밀번호·토큰은 다루지 않음)
- 내장 브라우저 패널(Claude in Chrome은 미연결)에서 확인. Mixamo: 계정 메뉴에 "Log out" 표시 → 로그인됨. Asset Store: 헤더에 "내 에셋/저장된 에셋"·사용자 이니셜 표시 → 로그인됨.

## 약관 확인
| 대상 | 출처 | 상업 게임 | 비공개 저장소 | 단독 재배포 | 크레딧 |
|---|---|---|---|---|---|
| Mixamo | Adobe 공식 FAQ https://helpx.adobe.com/kr/creative-cloud/faq/mixamo-faq.html (2022-11-28 갱신, 확인 2026-11-16): "개인, 상업 및 비영리 프로젝트용 로열티 프리 … 비디오 게임 제작" | 허용 | 조문 없음. 팀 내 공유는 배포 아님(커뮤니티 글, 공식 아님) | 원본 캐릭터·애니메이션 파일 단독 배포 금지, 에셋 스토어·스톡용 패키지 금지(커뮤니티 FAQ https://community.adobe.com/t5/mixamo-discussions/mixamo-faq-licensing-royalties-ownership-eula-and-tos/td-p/13234775, 2022-09-29, 비공식). **Adobe Additional Terms 원문 PDF는 아직 못 읽음 → 받기 전 확인 필요** | 불요(커뮤니티 글) |
| Unity Asset Store 에셋 | Unity Asset Store Terms https://unity.com/legal/as-terms (Last updated Dec 4, 2024, WebFetch 요약) | §2.2.1(a) 실질적 독자 콘텐츠와 함께 제품에 포함 가능 | 저장소 조문 없음. §5.1 "복사 금지(일시적 복사 제외)", §5.2 백업 보관 허용, §2.4 협력자도 각자 라이선스 필요 → 1인 비공개 저장소는 가능성 높으나 사람 확인 | §2.2.1(b) 제품에 포함·내장된 형태로만 배포 | 에셋별 상이 |
| Poly Haven | https://polyhaven.com/license (확인 2026-11-16, 요약) | CC0, 상업 허용 | 허용(재배포 문구에서 추론) | 허용 | 불요 |
| ambientCG | https://docs.ambientcg.com/license/ (확인 2026-11-16, 요약) | CC0, 게임 포함 허용 | 허용 | 허용 | 불요 |
| Quaternius UAL / Base Characters | https://quaternius.com/packs/universalanimationlibrary.html , .../universalbasecharacters.html | CC0 "free to use in personal, educational and commercial projects" | 허용 | CC0 | 불요 |

## 후보 (무료만)
| # | 이름 | 링크 | 가격 | 라이선스 | 폴리 | 뼈대 | 작업량 |
|---|---|---|---|---|---|---|---|
| 1 | Quaternius Universal Animation Library 1 Standard | https://quaternius.itch.io/universal-animation-library | 무료 | CC0 | 45클립(전체 120+). 총기·수영 포함 여부 미확인 | Universal humanoid rig, Humanoid 리타게팅 | 중(0.5~1일) |
| 2 | Quaternius UAL 2 Standard | https://quaternius.itch.io/universal-animation-library-2 | 무료 | CC0 | 42클립(근접·파쿠르·좀비) | 같음 | 중 |
| 3 | Quaternius Universal Base Characters Standard | https://quaternius.com/packs/universalbasecharacters.html | 무료 | CC0 | 약 13k tris | UAL과 같은 뼈대(리타게팅 불요) | 중~상(캐릭터 교체 #230, 무기 소켓 재작업) |
| 4 | Mixamo 캐릭터+애니메이션 | https://www.mixamo.com | 무료(Adobe ID) | Adobe 약관(위) | 캐릭터 폴리 미확인 | Humanoid(Mixamo 표준 뼈대) | 중(애니 세트 수집 포함) |
| 5 | Free Scavenger (Asset Store) | https://assetstore.unity.com/packages/3d/characters/humanoids/humans/free-scavenger-261065 | 무료 | Asset Store EULA | 20,922 tris | Humanoid 호환 | 하(테스트용). 노숙자 외형이라 게임 용도엔 부적합 |
| 6 | Insurgent LITE (Asset Store) | https://assetstore.unity.com/packages/3d/characters/humanoids/humans/insurgent-lite-274543 | 무료 | Asset Store EULA | 1,742 tris(AK47 532) | Humanoid | 폴리 낮아 개선 효과 적음. 무기 이름 상표 주의 → 비추천 |
| 7 | Nora Squad Starter | https://assetstore.unity.com/packages/3d/characters/humanoids/fantasy/nora-squad-starter-413902 | 무료 | Asset Store EULA | 8.3k~10.8k | 미확인 | 판타지·로우폴리 → 비추천 |

텍스처(CC0, 로그인 불필요): Poly Haven `aerial_grass_rock grass_ground dirt_floor gravel_ground_01 gravel_road concrete_wall_003 concrete_floor_worn_001 asphalt_02 asphalt_05 rusty_metal_02 metal_plate oak_wood_planks`(1k~8k), ambientCG `Concrete049 Metal064 Planks040 Bricks113A PavingStones152`. URL 패턴 `https://polyhaven.com/a/<id>`, `https://ambientcg.com/a/<ID>`.

미확인: UAL Standard에 소총/권총 자세·재장전·엎드리기·수영이 있는지(zip 내부 클립명), Mixamo 캐릭터 폴리 수와 Additional Terms 원문.

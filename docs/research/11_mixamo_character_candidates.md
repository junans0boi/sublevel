# 조사 #11 — Mixamo 캐릭터 후보 시트와 임시 선택 (S3 T1 / #253)

확인일: 2026-11-16. 방법: 내장 브라우저(로그인 상태)에서 Characters 목록 3페이지(108종)를 **읽기만** 함. 다운로드·약관 클릭 없음.
한계: 이름은 페이지 텍스트로 확인(**확인**). 외형 판단은 저해상도 썸네일 인상(**추정**). 폴리 수·텍스처는 목록에 없어 **미확인** — 시험 Swat 실측(T3)은 삼각형 19,450·텍스처 1K/512(확인)이고 다른 캐릭터는 다운로드 후 같은 `CharacterProbe`로 측정한다.
선택은 준환 위임(master 2026-11-16)으로 **임시 확정**이며 시트 아래 교체 가능(ADR-182).

## 선택 기준
사실풍 현대 복장 / 얼굴이 보임 / 남녀 혼합 / 판타지·좀비·SF·카툰 제외 / 마스크·풀헬멧류는 최대 2종(ADR-181 A3).

## 임시 선택 10종 (슬라이스 4 → 확장 6)
| 단계 | 이름 | 성별(썸네일) | 인상 | 비고 |
|---|---|---|---|---|
| 슬라이스 | **Swat** (시험 FBX 보유) | 남 | 전술 장비, 마스크형 | 마스크 1종 쿼터. 삼각형 19,450(T3 확인) |
| 슬라이스 | **Brian** | 남 | 흰 셔츠·청바지 민간 | |
| 슬라이스 | **Louise** | 여 | 어두운 제복풍 | |
| 슬라이스 | **James** | 남 | 어두운 캐주얼/전술 | 얼굴은 썸네일로 불분명 |
| 확장 | Megan | 여 | 흰 티·청바지 민간 | |
| 확장 | Kate | 여 | 청색 셔츠·갈색 바지 | |
| 확장 | Josh | 남 | 어두운 재킷 | |
| 확장 | Adam | 남 | 밝은 상의 민간 | |
| 확장 | Martha | 여 | 네이비 재킷 | |
| 확장 | Alex | 남 | 어두운 모자·재킷 | |
슬라이스는 시험 Swat + Brian/Louise/James 3종 + UBC 2종 = 6종(A1).

## 예비(교체용, 미선택)
Roth(여, 흰 셔츠) · Shannon(여, 흰 상의) · Jennifer(여, 붉은 상의) · Joe(남, 정장) · Gas Mask(마스크 쿼터 2번째) · Drake(남, 어두움) · Steve/Ortiz/Suzie/David/Pete/Lewis(3페이지, 썸네일 미확인).

## 제외 (사유)
- 카툰·스타일화: Amy, Aj, Bryce, Jackie, Michelle, Sophie, Elizabeth, Timmy, Ty, Mousey, Sporty Granny, Girlscout, Big Vegas, Jolleen
- 판타지·크리처·좀비·SF: Maria, Erika Archer, Paladin, Knight, Castle Guard, Warrok, Mutant, Vampire, Ninja, Exo Gray/Red, Alien Soldier, Y Bot, X Bot 등 나머지 전부
- Dummy(주황 죄수복), Prisoner B Styperek: 팀 식별에 방해 → 필요 시 예비
- Mannequin/Passive Marker Man: 플레이스홀더용

## 다음
1. 다운로드는 준환의 **항목별 승인** 뒤(ADR-165). FBX With Skin, T-pose, 파일은 `AssetStaging/mixamo/`에만 둔다.
2. 받을 때마다 `Assets/_Project/Editor/CharacterProbe.cs` 방식으로 Humanoid·키·삼각형·텍스처 측정 → 15k 초과면 LOD0 데시메이트(T8).
3. 반입·ledger 기록은 프로젝트에 넣는 시점(설계: `docs/research/12_character_interface.md` §5).

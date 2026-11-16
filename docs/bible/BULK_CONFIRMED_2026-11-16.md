# 일괄 확정 로그 (2026-11-16, 준환 위임)

추천안을 그대로 적용한 결정 79개. 바꾸고 싶은 것은 정의서 '결정 대기'가 아닌 각 섹션에서 되돌리기(버튼) 또는 알려주세요.

| 섹션 | 결정 | 질문 | 적용한 답 |
|---|---|---|---|
| ammo | a1 | 탄 종류를 늘릴 것인가(현재 4종: 9/5.56/12게이지/7.62) | 4종 유지 |
| ammo | a2 | 탄 무게/소지량 균형 | 현재 유지 |
| ammo | a3 | 탄 종류 표기(숫자 구경)와 실존 총기 연상 | 일반 구경 표기 유지 |
| animation | an_char | 캐릭터 구성 | Mixamo 8~12종 + UBC 2종 |
| animation | an_rig | Animation Rigging 패키지 | 추가 안 함 |
| animation | an_fp | 1인칭 팔 | 지금 프리미티브 유지 |
| animation | an_rag | 사망 래그돌 | 클립만 |
| animation | an_dir | 8방향 이동 | Mixamo 8방향 클립 받기 |
| animation | an_gun | 무기 길이 재설정 | 실물 비례 |
| armor_items | x1 | 방어구 티어 수와 수치 | 현재 유지 |
| armor_items | x2 | 파편 수류탄이 루팅 표에 없는 문제 | 루팅 표에 추가 |
| armor_items | x3 | 회복류 종류 정리 | 현재 유지 |
| armor_items | x4 | 무게 시스템(Carry) 균형 | 현재 유지 |
| armor_items | x5 | 헬멧·조끼 티어별 이름(현재 '헬멧 1/2/3') | 번호 유지 |
| attachments | t1 | 조준경 종류가 8종, 다 필요한가(1x 2종 + 2/3/4/6/8/15배) | 유지 |
| attachments | t2 | 부착물 등급(1~3티어) 도입 | 단일 등급 유지 |
| attachments | t3 | 산탄총에 맞는 초크·탄창 부착물 | 추가 안 함 |
| attachments | t4 | 소음기 소리를 실제로 다르게(총성 파일 교체/감쇄) | 소음기용 별도 발사음 클립 추가 |
| audio | a_cc | CC-BY 소리를 크레딧 화면에 올려 쓸 것인가 | CC0만 |
| audio | a_mix | AudioMixer 도입(그룹·리버브·덕킹) | 도입 |
| audio | a_voice | 동시 발음 상한 | 32 |
| audio | a_surf | 표면 태그 확장 범위 | 14종 표 |
| bots | db1 | 50명 중 봇 비율은? | 사람이 적으면 봇으로 채우고 많으면 줄임(최소 봇 수 보장) |
| bots | db2 | 난이도는 판 전체 하나인가, 봇별로 섞는가? | 봇별 혼합(보통 중심, 쉬움·어려움 소수) |
| bots | db3 | 봇이 차량을 운전하는가? | 아니오(이번 범위 밖, BACKLOG) |
| bots | db4 | 봇이 창문·옥상·사다리를 쓰는가? | 이번엔 제외, 도시 지도 확정 후 NavMeshLink 후보만 표시 |
| bots | db5 | 봇 이름은 어떻게? | 가상 닉네임 풀 |
| characters | dc1 | Mixamo 약관 원문 검토 전에 Mixamo 캐릭터를 출시 빌드에 넣어도 되는가? | 검토 전까지 보류 |
| characters | dc2 | 플레이어 기본 아바타는 CC0(UBC)로 가고 Mixamo는 봇만 쓰는가? | 예 |
| characters | dc3 | 스태미나를 둘 것인가? | 없음 유지 |
| characters | dc4 | 새 캐릭터의 서기 키와 눈 높이 기준은? | 키 1.8 / 눈 1.6 유지(캡슐 기준) |
| map | d-approve | C2 수치(2.4 x 2.0 km, 마을 17, 건물 411, 지역 6, 승강장 20)를 이대로 승인할까? | 승인 |
| map | d-cap | 수도급 청람읍 건물 수는? | 90채(진입 70) |
| map | d-regname | 지역 이름 6개를 이대로 확정할까? | 그대로 |
| map | d-final | 오염이 수렴하는 최종 지역은 청람읍권 고정인가, 시드 랜덤인가? | 6개 중 무작위, 육로 연결 보장(준환 결정, 청람읍권 가중은 적용하지 않음) |
| map | d-adr | 하이브리드 오염 방식을 ADR로 먼저 기록하고 SPEC §3을 개정할까? | 예, 구현 전 ADR |
| map | d-relig | 종교 랜드마크(사당·사원 등)를 이번 라운드에서 보류할까? | 보류(RISKS에만 기록) |
| map | d-perf | 411채 성능 측정을 생성기 작업보다 먼저 할까? | 예(측정 먼저) |
| match | m_n | 한 판 인원 | 50 |
| match | m_zone | 봉쇄 단위 | 하이브리드(C) |
| match | m_chip | SPEC §6 칩 부활 처리 | SPEC에서 삭제하고 기절·부활로 확정 |
| match | m_len | 판 길이 목표 | 20~25분 |
| match | m_loot | 루팅 구조 | 지역·건물 종류별 가중 |
| match | m_score | 점수·크레딧(재화) 도입 | 도입 안 함 |
| materials | m_arm | 표면 전부를 arm 한 장(AO·Rough·Metal) 경로로 통일 | 통일 |
| materials | m_res | 해상도 상한 | 전부 2K |
| materials | m_sky | 하늘/조명 진실 원천 | WorldSky 키프레임 + HDRI는 스카이박스 |
| materials | m_keep | 받아 둔 173 MB 정리 | 쓰는 것만 남김 |
| materials | m_detail | 디테일 3종(블렌딩·데칼·정점 색) 범위 | 재질 교체 후 별도 |
| network | nd_cap | 50명 서버 구조 | 프로세스 방당 1개 |
| network | nd_tick | 틱레이트 | 30 Hz 유지 |
| network | nd_door | 문·유리 방송 방식 | 건물 소속+관심 영역 필터 |
| overview | o3 | 저장소 용량 방침(Git LFS 도입 또는 런타임 생성) | 월드 메시는 런타임 생성·저장 안 함 |
| roads | r-grade | 12도를 넘는 구간(최대 18.1도)을 어떻게 할까? | 스위치백/절토를 늘려 12도로 맞춘다 |
| roads | r-ring | 순환(국도 루프)을 별도 도로 종류 Ring으로 만들까? | 기존 Trunk 확장 |
| roads | r-bridge | 다리를 오염 단계에서 폐쇄 가능한 요소로 쓸까? | 다리 폐쇄 없음(준환이 오염 시스템 결정에서 확정) |
| roads | r-nav | 다리마다 NavMeshLink를 두는 것을 전제로 할까? | 예 |
| terrain | g-walk | 걷기 한계를 35도(88%)로 할까 38도로 할까? | 35도, 88% 이상 |
| terrain | g-speed | 경사별 이동 속도 계수를 도입할까? | 예(15도 1.0 -> 38도 0.45) |
| terrain | g-height | 최고 203 m를 허용할까(MaxHeight 120 m는 현재값)? | 허용(MaxHeight 상향) |
| terrain | g-hm | heightmap 해상도를 1 m 유지할까 상향할까? | 1 m 유지(타일 분할) |
| towns | t-cap | 청람읍 건물 수 90채(진입 70)를 확정할까? | 90채 |
| towns | t-names | 마을 17곳의 이름(청람읍·새재·해옥포·광석리 등)을 이대로 쓸까? | 그대로 |
| towns | t-relig | 사당·사원·교회 등 종교 랜드마크를 보류할까? | 보류(RISKS 기록만) |
| towns | t-kinds | 건물 종류를 5종에서 12종으로 확장해도 될까(BuildingKind 변경)? | 단계적으로 4종만 먼저 |
| towns | t-loot | 루팅 등급(상: 막사·공장 보급창)을 이대로 둘까? | 그대로 |
| ui | d_tone | UI 문구 어투 | 존댓말('~합니다') |
| ui | d_zoom | 지도 해상도 방식 | 2048 + 줌 단계 |
| ui | d_warn | 오염 예고 시간과 표시 수단 | 둘 다 |
| ui | d_shot | 정의서용 UI 캡처 촬영 | 구현 후 일괄 |
| vehicles | dv1 | 차량 인게임 표시 이름은 어떻게? | 종류만 표시(세단/밴 등) |
| vehicles | dv2 | 차량 색 변형을 둘 것인가? | 머티리얼 틴트로 3~5색 변형 |
| vehicles | dv3 | 50명 판에서 차량 대수는? | 면적 비례 증가(차 12~16, 바이크 6~8) |
| vehicles | dv4 | 차량 소리를 파일로 바꿀 것인가? | CC0 소리 파일 후보 조사 후 교체 |
| vehicles | dv5 | 오토바이 질량 190 vs 230 중 무엇이 물리에 쓰이는가? | rules 값을 데이터 한 곳으로 통일 |
| weapons | w2 | 저격소총 Sentinel 전용 모델 확보 | CC0 저격 모델 조사(자산 라운드) |
| weapons | w3 | TTK(몸통 기준) 목표 | 현재 수치 유지 후 플레이테스트로 조정 |
| weapons | w5 | 산탄 근접 전탄 216 피해(펠릿 9x24)와 헬멧 보정 | 유지 |
| weapons | w6 | 빈 총(탄 없음) 클릭 소리 추가 | 추가 |

## 일괄 확정에서 제외(사람이 해야 하거나 측정 후 확정)

- audio/a_son: Sonniss 번들 사용
- audio/a_hrtf: HRTF/공간화 플러그인
- materials/m_ph: Poly Haven 후보 선정(분류 12개 x 후보 3~5)
- network/nd_budget: 대역폭 목표
- overview/o1: 정식 게임 이름(가칭 → 확정)과 로고 방향
- overview/o4: Mixamo Additional Terms 원문 확인(사람이 읽기)
- terrain/g-adr: 경사 대역·규모 수치(03 §2.1~2.3)를 ADR로 확정할까?
- weapons/w4: 총 이름 실존 유사 여부 검토

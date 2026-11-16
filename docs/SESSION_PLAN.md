# SESSION_PLAN — 서브 세션 9개 구성과 체크리스트 (2026-11-16, 준환 확정: grilling Q1~Q5)

> 근거: `MASTER_BRIEF.md`(WP 번호), `SPEC.md`, ADR-165~176, `docs/bible/data/*.json`. 충돌하면 SPEC이 이긴다.
> 이 문서는 master가 세션을 띄울 때 브리프의 뼈대로 쓴다. 각 세션은 **자기 표만** 따르고 다른 세션 소유 파일은 건드리지 않는다.

## 0. 확정된 운영 결정 (grilling)
| # | 결정 | 내용 |
|---|---|---|
| Q1 | 세션 9개 | 맵·재질·캐릭터·애니·사운드·UI + **게임 규칙 3개**(오염·열쇠·칩 / 총기 / 봇 50인·네트워크) |
| Q2 | 수직 슬라이스 우선 | **R1 청람읍권 + R0 북령 산악** 2개 지역을 건물·재질·캐릭터·오염·열쇠까지 끝까지 구현·플레이 → 그다음 나머지 4개 지역 확장 |
| Q3 | 코드 충돌 | 조사·설계·문서·데이터(JSON)·순수 C#(Core/) 테스트는 병렬. **Unity를 건드리는 코드 변경은 한 번에 한 세션**(master가 순서 지정). 워크트리 없음 |
| Q4 | commit | 티켓마다 묻지 않고 **WP 하나가 테스트·캡처·성능 판정까지 끝났을 때** master가 한 번에 요청 |
| Q5 | UI | 비인게임 UI는 슬라이스 이후. 인게임 HUD는 각 규칙 세션이 최소로 추가하고 `ui.json`에 결정으로 올려 준환 확인 |

| 승인 | 2026-11-16 | 준환 승인: `BuildingKind` 5→약 12종 확장(S1), #240 World_Meshes 바이너리(.bytes)+LFS 추적(앞으로의 파일만, 이력 불변), 패키지는 필요할 때 개별 승인. **게임 이름: SUBLEVEL(서브레벨) 확정**(상표 검색은 출시 전 사람) |

## 1. 모든 세션 공통 체크리스트
| ☐ | 항목 | 기준 |
|---|---|---|
| ☐ | 시작 보고 | 읽은 문서와 오늘 할 일 한 줄 (CLAUDE.md 규칙) |
| ☐ | 단계 | grilling(정의서에 없는 것만, 한 번에 5문항 이내) → to-spec → wayfinder 지도 → to-tickets → 구현(tdd) → code-review(다른 에이전트) |
| ☐ | grilling 생략 | `status: confirmed` 항목은 묻지 않는다. 미정·open·proposed 중 구현에 필요한 것만 묻는다 |
| ☐ | 소유 파일 | 아래 표의 소유 목록 밖은 수정 금지. 필요하면 master에 요청 |
| ☐ | Unity | `tools/unity.sh`만, master가 준 순번에만 실행. PlayMode는 `/tmp/sublevel-playmode.lock`. 끝나면 `git restore -- ProjectSettings Assets/_Project/Prefabs/M1/Vehicles`, `Assets/InitTestScene*` 삭제 |
| ☐ | 라이선스 | 에셋은 `docs/ASSET_LEDGER.md`에 출처 URL·라이선스·수정·날짜. CC0/상업 가능만. AI 생성·실존 상표·총기·차종명 금지 |
| ☐ | 룰 변경 | ADR 먼저(`DECISIONS.md`, ADR-177부터 master가 번호 배정) |
| ☐ | 증거 | EditMode/PlayMode 통과 + 성능 예산 판정 + **게임 안 캡처 + 수치**(스크린샷 한 장 단독 판정 금지) + 확인한 것/추정 구분 |
| ☐ | 문서 갱신 | `docs/bible/data/*.json` 상태, 필요 시 ADR, `PROGRESS.md`, `python3 tools/bible_build.py` |
| ☐ | 금지 | commit·push·패키지 추가·디렉터리 구조 변경은 master 경유 준환 승인. 새 아이디어는 `BACKLOG.md` 한 줄 |
| ☐ | 보고 | 한국어 10줄 요약 + 체크리스트, 코드·커밋은 영어 |

## 2. 세션별 체크리스트

### S1. 맵 (WP 1~4 + A1 성능 측정 + #240)
소유: `Core/World/*`, `Editor/WorldBuilder*`, `MapBaker`, `WorldMeshes`, `WorldVegetation`, `map·towns·roads·terrain.json`, 관련 EditMode 테스트.
| ☐ | 단계 | 작업 | 증거(완료 기준) | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 건물을 코드 조립 유지 vs 프리팹, 층수 2~5·계단·옥상·창문 범위, 411채 확정 기준, 슬라이스 2개 지역의 마을 선택 | 답변 DECISIONS 가정 기록 | 없음 |
| ☐ | A1 | 411채 가짜 지오메트리(단순/상세)로 드로우콜·SetPass·삼각형·메모리·로딩·NavMesh 베이크 시간·씬 용량 측정, 메모리·로딩·베이크 판정 기준 제안 | 측정표 `docs/research/`, PERF_BUDGET 갱신 안, 건물 수·Terrain 분할·LOD/인스턴싱 결론 | 선행 없음(생성기보다 먼저, ADR-168) |
| ☐ | #240 | World_Meshes 43MB 해소 방침(바이너리 저장 `.bytes`/LFS 추적, 앞으로만) | 파일 크기 전/후, 재생성 결정성 테스트 | 저장 방식 변경 = 구조 변경 → master 경유 승인 |
| ☐ | WP 1 | `WorldRules` 하드코딩 → 데이터: 지역 수·엘리베이터 3~4대·MaxLootSpawns 250·MaxRocks 900·알파맵 1 m/px·맵 1024 m·FarClip/Fog | EditMode 테스트, 기존 971 유지 | `BuildingKind` 5→약 12종 enum 변경 승인 |
| ☐ | WP 1 | 루팅을 건물당 점 수×종류 가중으로(상한은 면적·건물 수에서 도출), 건물 종류별 가중 테이블(룰 변경이면 ADR) | 루팅 분포 테스트 | |
| ☐ | WP 2 | 산악 높이식(능선 골격·도메인 워프·테라스·협곡·절벽), 걷기 35도·지역 면적 88% 이상, 컨트롤러 `slopeLimit`(45) vs 규칙(40) 정리 ADR, 테스트 T1~T14 | `03_mountains.md` T1~T14 통과, 경사 통계 수치 | WP 1 |
| ☐ | WP 3 | 마을 연결 그래프(순환, 다리 5, 여울 1, 차량 고개 5, 보행 안부 3), 도로 경사 12도 재설계, 차량·NavMesh 연동 | 연결성·경사 EditMode 테스트, 도로 NavMesh 연결 | WP 2 |
| ☐ | WP 4 | `PlaceLots`→블록·골목, 건물 12종, 소품·울타리·농지, 청람읍 90채, **슬라이스: R1+R0만 먼저** | 건물 수·진입 수 표, 지역별 드로우콜, 게임 안 캡처(마을·골목·산) | WP 3, A1 결과 |
| ☐ | 표면 태그 | 건물 이름 파싱을 쓰지 않고 표면 판정 구조(재질/콜라이더 태그)를 **먼저** 정해 S2·S5에 넘김 | 구조 문서 + 기존 FootstepSurface 테스트 통과 | S5와 합의(master 중재) |
| ☐ | 위험 | NavMesh 한 장 5.2MB → 면적 증가, 타일 분할 필요성 / 봇 NavMeshLink 제외 | 베이크 시간·용량 수치 | |

### S2. 재질·조명 (WP 5)
소유: `Editor/WorldMaterials`, `RenderSetup`, `SceneLook`, `LookDevBuilder`, `materials.json`, 재질 에셋.
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 12분류 우선순위, 용량 예산(2K 상한, 쓰는 것만 남김), 지형 블렌딩·데칼·정점색 범위 | 답변 기록 | |
| ☐ | 조사 | Chrome으로 Poly Haven 직접 보고 12분류별 후보 시트(ID·링크·해상도·용량·80년대 한국/열대 적합 이유). **이미지만 보고 판정 금지, 수치 병기** | 후보 시트 `docs/research/` | **BLOCKED: 준환이 후보 선택** |
| ☐ | 파이프라인 | `arm` 통일(AO·Rough·Metal), 2K 상한, `AssetImportRules` 확장, 안 쓰는 텍스처 173MB 정리(대량 삭제 아님·준환 확인) | AssetValidator·ledger 통과 | 후보 선택 전에도 가능 |
| ☐ | 조명 | WorldBuilder 흐린 하늘 가정 보정(안개 색·그림자 거리 50m·앰비언트·실내 리플렉션 프로브), Kloofendal HDRI | 전/후 같은 구도 캡처 + 렌더 ms | 슬라이스 마을(S1 WP 4) 존재 |
| ☐ | 디테일 | 지형 트라이플래너, 데칼(얼룩·균열·젖음), 정점색 변주 | 렌더 예산 판정(드로우콜·VRAM) | |
| ☐ | 표면 연동 | 재질→표면 태그 매핑 표(S1·S5와 일치) | 매핑 테스트 | S1 표면 구조 |
| ☐ | 슬라이스 | R1·R0 재질 적용 | 캡처 + PERF 판정 | 후보 선택 |

### S3. 캐릭터 (WP 10 전반)
소유: `Editor/CharacterKit`, `characters.json`, 캐릭터 모델·프리팹, `sublevel_staging/`.
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 캐릭터 수(Mixamo 8~12 + UBC 2종), 의상·피부 변주, 헬멧·얼굴 방향, 피부 `skin: open` | 답변 기록 | |
| ☐ | 조사 | Mixamo 약관(Additional Terms) 정리 → 준환이 읽고 승인, 재배포 라이선스, UBC/UAL 확인 | 약관 요약 + ASSET_LEDGER 초안 | **BLOCKED: 준환이 시험용 Mixamo FBX 1종 다운로드** |
| ☐ | 시험 | 시험용 FBX 1종 Humanoid 임포트 + 캡슐(`cap`)·눈높이 호환, LOD 3단 | 임포트 로그, 비율 수치(키·머리·손), 게임 안 캡처 | 위 블로커 |
| ☐ | 비율 | 머리·헬멧 과대(#230), 맨살 손+장갑 불일치, 소총 실물 길이 비례 | 수치(총 길이·팔 길이) + 캡처 | |
| ☐ | 로스터 | 모델 N종·변형 8, 50인 대응 LOD·드로우 예산 | PERF 판정(캐릭터 50개 렌더) | S1 A1 도구 재사용 |
| ☐ | 호환 | `CharacterKit` 참조처(`t_kit`, `t_other`) 영향 목록, 테스트 재작성(맹목적 수정 금지) | EditMode 통과 | S4와 인터페이스 합의 |
| ☐ | 문서 | ledger, characters.json, 크레딧 화면 | | |

### S4. 애니메이션 (WP 10 후반)
소유: `CharacterAnimator*`, `CombatantMotion`, `MixamoBake`, `animation.json`, 애니 테스트.
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 감사 이슈 #218~#238 중 우선순위, 베이크 vs 직접 사용(ADR-173은 직접), FP 손·래그돌·방향 이동 | 답변 기록 | |
| ☐ | 설계 | 레이어 Base/Upper/Action/Additive(+Aim), 왼손 IK+LeftHandGrip, 발 IK, 엎드림 재설계, 차량 IK | 설계 문서 + 클립 인벤토리 매핑 | |
| ☐ | 테스트 재작성 | 맹목 수정 금지: stance·rifle·gait·animator·play·capture 테스트를 새 구조 기준으로 | EditMode/PlayMode 통과 | S3 Humanoid 임포트 |
| ☐ | 구현 | #223 좌우·후진, #222 스프린트, #225 엎드림, #227 웅크림, #228 점프, #231 대기, #232 기울이기, #233 난간, #234 회전, #235 발 IK, #236 수영, #218·#219·#226 | 이슈별 수치 + 게임 안 캡처(영상/연속 프레임) | 시험용 Mixamo FBX, S3 |
| ☐ | 판정 | 정지 이미지로 판정 금지: 프레임 시퀀스·관절 각도 수치 | | |
| ☐ | 성능 | 50인 Animator 비용(LOD·컬링) | PERF 판정 | |

### S5. 사운드 (WP 13)
소유: `Gameplay/CombatantAudio`, `DoorAudio`, `ElevatorAudio`, `AmbientBeds`, `Net/BulletSurfaces`(표면 부분), `SoundRules`, `audio.json`, `AUDIO.md`.
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 층 차폐·벽 폐색 비용 한도, 청취 시트 형식, HRTF(`a_hrtf` open), Sonniss 약관(`a_son` open) | 답변 기록 | |
| ☐ | 데이터화 | 표면 태그 14종(이름 파싱 제거), 이벤트 목록(소리·반경·층), 기존 FootstepSurface 4종 테스트 유지 | EditMode/PlayMode 통과 | S1 표면 구조 |
| ☐ | 공간음향 | 층 차폐(저역 통과), 벽 폐색 레이캐스트(샘플링·비용 제한), 실내/실외 리버브 존, 총성 3겹 | 레이캐스트 비용 수치, 시나리오 PlayMode 테스트(2층 발소리) | S1 건물 구조 |
| ☐ | 조사 | 소스 후보(Freesound·Sonniss·Kenney·OGA·BigSoundBank·itch.io·직접 합성), 라이선스, **청취 시트** | `docs/research/` 시트 | **BLOCKED: 준환이 직접 듣고 선택** |
| ☐ | 오염 소리 | 사이렌·방송·가이거 계수관 훅 정의(S6가 호출) | 인터페이스 문서 | S6 |
| ☐ | 50인 대응 | 동시 발음 32 상한(PERF), 풀링, 거리·반경 우선순위 | PERF 판정 | S8 관심영역 |

### S6. 오염·열쇠·칩 (WP 6~8)
소유: `Core/Lockdown*`→오염 코어(신규 Core), `ElevatorTravel`·열쇠·칩·팀 규칙, `pollution·match.json`, 관련 테스트. 슬라이스 후 전 지역.
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 열쇠 아이템을 기존 열쇠와 분리할지, 방독 아이템 종류·수치, 기절 중 수혈 vs 칩, 최소 HUD 경고 형태(ui.json) | 답변 기록 | |
| ☐ | 코어(순수 C#) | 지역 6개 오염 확정 순서(시드, 연결 보장), 전선 확산(지형 비용 그리드: 평지 60/도로 100/급경사 30 m/분, 강 ×0.6, 단계 +5%, 최종 ×0.7), 지속 데미지 2/3.2/5/8/12.6/20, 경고 105초 | EditMode 테스트(임의 시드·순서에서 남은 지역 연결성, 시뮬레이션 `pollution_sim` 수치와 일치) | **S1 도로·지역 그래프 데이터 형식** |
| ☐ | 서버/복제 | 서버가 시드+서버 시간 결정론, 클라는 지역 단계·전선만 복제 | 멀티 PlayMode 테스트 | S8 네트워크 구조 |
| ☐ | 연출 | 붉은 안개·가이거·화면 효과·지도 점멸·경고 사이렌(S5 훅) | 게임 안 캡처 + 수치 | 슬라이스 지역 |
| ☐ | 방독 | 방독면·해독제 소모, 전선 통과, 최종 지역 무효 | 테스트 | |
| ☐ | WP 7 | 열쇠 6~8개(편중 0.5), 사용 순서(삽입→문 열림→30초→지역 선택 15초→무효 시 반환), 누구나 탑승·한 대 4명, 비오염 지역만, 90초 충전·팀 쿨다운 3분, 도착 보호 없음, ADR-055 투입 유지 | 상태 머신 EditMode 테스트 + PlayMode 시나리오 | 오염 코어 |
| ☐ | WP 8 | 솔로+스쿼드(4), 칩 부활(승강장 보관함, 90초, 오염 확정 시 파괴, 비오염 지역 승강장 부활), 기절·수혈과 관계 정리 | 테스트 + 캡처 | WP 7 |
| ☐ | 시뮬 대조 | 판 길이 20~25분(시뮬 200시드 평균 22.1분) 재현, 열쇠 이탈률 7개 25%·8개 28% | 시뮬 대조표 | |
| ☐ | HUD | 오염 경고·열쇠·칩 HUD 최소 추가 + `ui.json` 결정 항목 | 준환 확인 | Q5 |

### S7. 총기 (WP 9)
소유: `Core/Weapon*`·`weapons·ammo·attachments.json`, 총기 모델(플레이스홀더 `placeholder/`).
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 보급 총기 4~6종 선정, 저지력 피격 반응 강도, 권총 강한 한 발 종류 | 답변 기록 | |
| ☐ | 데이터 | 일반 파밍 16종(SMG 3, AR 4, DMR 3, SR 2, 샷건 2, 권총 2) 스탯: 대미지·탄속·저지력·RPM·DPS·모드, 탄 4종 유지, Breacher→5발 탄창식 반자동, 더블 배럴 | `weapons.json` + EditMode 데이터 테스트(총마다 달라야 함) | |
| ☐ | 저지력 | 맞은 상대의 조준 흔들림·이동 감속(1~10) | 서버 판정 테스트 | 네트워크(S8) |
| ☐ | 모델 | CC0 조사. 임시 플레이스홀더는 `placeholder/` | ledger, 형상 상표 검토는 사람 | **BLOCKED: CC0 조사 결과·준환 선택**. 이름 가칭·상표 검토는 준환 |
| ☐ | 전투 | 루팅 분포(S1 루팅 가중)·보급상자 연동 | 분포 테스트 | S1 |
| ☐ | 성능 | 총기 모델 LOD·드로우 | PERF 판정 | |

### S8. 봇 50인·네트워크 (WP 11·12)
소유: `AI/*`, `Net/*`(관심영역·구조), `bots·network.json`, `DEDICATED_SERVER.md`.
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | grilling | 서버 CPU 예산, 방당 프로세스 1, 대역폭 목표(측정 후 확정), 봇 난이도 혼합 | 답변 기록 | |
| ☐ | 측정 | 50인(봇 포함) 서버 CPU·대역폭, 봇 감지 레이캐스트 0.15초 주기 비용, LagComp 비용 | 측정표(PERF 신규 항목) | S1 A1 도구 |
| ☐ | 구조 | 인원 16→50 파라미터화(`MaxPlayers`·`BotCount`·`MaxBots`), 사람 적으면 봇 채움(최소 봇 수 보장) | EditMode 테스트 | |
| ☐ | 관심영역 | 문·유리·루팅 이벤트 필터, 봇 AI LOD | 이벤트 수/초 측정 | |
| ☐ | 봇 행동 | 오염 예고 인지·이동, 열쇠 줍기·사용, 팀 구성, 도시 관심 지점 자동 생성(건물 입구·지붕·교차점) | PlayMode 시나리오 + 통계(`BotStats` 스톨) | S6 오염·열쇠, S1 도시 |
| ☐ | NavMesh | 도시 전체 연결성 테스트, 타일 분할 필요성, 베이크 시간 | 연결성 테스트 통과 | S1 |
| ☐ | 제외 | 봇 차량 운전·창문/옥상(NavMeshLink) | 후보만 표시 | ADR-175 |
| ☐ | 자동화 | PERF 판정 자동화(메모리·로딩) | | |

### S9. 비인게임 UI (슬라이스 이후)
소유: `Gameplay/LobbyScreen`, `Editor/MainMenuBuilder`, `CreditsScreen`, `GameSettings`, `ui.json`, `docs/research/ui_mockups/`.
| ☐ | 단계 | 작업 | 증거 | 선행/블로커 |
|---|---|---|---|---|
| ☐ | 시작 시점 | 슬라이스(S1·S2·S3·S6) 플레이 확인 뒤 시작. 그전에는 시안(`adopted.html`)만 유지 | master 판단 | 슬라이스 |
| ☐ | grilling | 화면 목록·우선순위, 스쿼드 로비·솔로/스쿼드 선택 흐름(WP 8 의존), 키 설정 | 답변 기록 | S6 WP 8 |
| ☐ | 구현 | 확정 시안 로비(adopted lobby 2 + style B) → Unity, 메인 메뉴·방 만들기·설정·크레딧·결과 | 게임 안 캡처 + 해상도별 확인 | |
| ☐ | 인게임 HUD | 오염 경고 UI·지도 해상도·미니맵 축척·50인 프리셋(`ui.json` proposed) | 준환 확인 | S6 |
| ☐ | 한국어 | 텍스트 정책, 접근성 | | |

## 3. 슬라이스 Unity 순번 (master 지정, 하나씩)
| 순 | 세션 | 작업 | 병렬로 가능한 비Unity 작업 |
|---|---|---|---|
| 0 | master | PlayMode `!Long` 기준선(EditMode 971/971은 확인 완료) | 전 세션 grilling·to-spec |
| 1 | S1 | A1 측정 | S6 코어 설계·테스트, S2 후보 시트, S5·S7 조사 |
| 2 | S1 | WP 1 + #240 | S6 코어 구현(순수 C#) |
| 3 | S1 | WP 2 지형(슬라이스 R0) | |
| 4 | S1 | WP 3 도로 → WP 4 R1·R0 건물 | S3·S4 설계 |
| 5 | S6 | 오염 코어 연결·WP 7·8 | S8 측정 계획 |
| 6 | S2 | 파이프라인·조명(후보 선택 후 재질) | |
| 7 | S3→S4 | 시험 FBX 임포트, Humanoid 전환 | **준환 Mixamo FBX 필요** |
| 8 | S8 | 50인 구조·봇 | |
| 9 | S5·S7 | 사운드 구현·총기 | 청취 선택·CC0 선택 필요 |
| 10 | master | 슬라이스 통합 플레이테스트(R1+R0) → 준환 확인 → 나머지 4개 지역 확장 → S9 UI | |

## 4. 준환 입력 목록 (BLOCKER)
| ☐ | 항목 | 필요한 세션 |
|---|---|---|
| ☐ | 시험용 Mixamo 캐릭터 FBX 1종 다운로드, Additional Terms 확인 | S3·S4 |
| ☐ | Poly Haven 재질 후보 선택 | S2 |
| ☐ | 사운드 후보 청취 선택, Sonniss 약관 | S5 |
| ☐ | 총기 모델 후보 선택, 신규 총기 이름·VK-9 상표 검토 (게임 이름은 SUBLEVEL 확정, 상표 검색은 출시 전) | S7 |
| ✅ | `BuildingKind` 확장·#240 저장 방식 변경 승인 완료(패키지는 필요 시 개별 승인) | S1 |
| ☐ | WP 단위 commit 승인 | master |
| ☐ | 미정: 대역폭(측정 후), 경사 ADR(A1 후), HRTF | S8·S1·S5 |

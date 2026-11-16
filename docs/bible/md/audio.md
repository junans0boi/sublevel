# 사운드

> 상태: 확정 · 요약: 현재 오디오(믹서 없음, 합성음 다수, 표면 6종과 이름 파싱 결함), '스타워즈 같다'는 불만, 목표 공간음향 설계, 후보 소스, 청취 시트 계획, 표면 태그 14종, 소리 이벤트 목록.

## 현재 오디오 시스템 인벤토리

| 상태 | 구성 | 지금 상태 | 한계 | 근거 |
|---|---|---|---|---|
| 확정 | 믹서 | AudioMixer 에셋 없음. 설정 값 4개(전체·효과음·환경음·UI)를 GameSettings가 들고 AudioListener.volume(전체)과 각 재생기에 곱한다. | 그룹별 차폐·압축·덕킹·리버브 버스를 쓸 수 없다. 섬광탄은 AudioListener.volume을 직접 낮춘다(FlashOverlay.cs:120-128). | Assets/_Project (*.mixer 검색 결과 0개); Assets/_Project/Scripts/Gameplay/GameSettings.cs:122-145,252 |
| 확정 | 3D 공간화 | 완전 3D(spatialBlend 1), 사용자 정의 감쇠 곡선(min/d 후 최대 거리에서 0), 도플러 없음. 범주별 거리(최소~최대 m): Body 2~40, Gunshot 12~750, Explosion 15~800, Vehicle 6~150, Elevator 3~60, Impact 3~60. | 좌우 패닝만 있고 높이(층)·차폐·잔향이 없다. HRTF/공간화 플러그인 사용 여부는 확인 필요. | Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:5-30 |
| 확정 | 총성 거리 처리 | 근거리층(30 m까지 단독)과 원거리층(200 m부터 단독)을 등출력 교차 페이드, 50 m부터 저역 통과 22 kHz → 1.2 kHz, 소음기는 4배 먼 소리로 취급. | 실내/실외 꼬리 구분 없음, 건물 사이 반사 없음(06 문서 N4). | Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:21,45-50; Assets/_Project/Scripts/Core/SoundRules.cs:76-98 |
| 확정 | 환경음 | 바람·새(낮)·귀뚜라미(밤)·물가·실내 룸톤을 전부 코드로 합성(AudioClip.Create), 지역 종류·지붕·해안 근접으로 섞고 총성에 덕킹(NoteLoud). | 지역 종류가 4개뿐, 도심 소음·원거리 총성 반향 없음. | Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:94-110; Assets/_Project/Scripts/Gameplay/AmbientBeds.cs:5-12 |
| 확정 | 발소리 | 표면 6종(Concrete, Metal, Wood, Grass, Dirt, Water) x Resources/Steps/<표면> 클립 5개(물 4개)를 이름순 정렬해 랜덤 재생. 범위 = 40 m x 자세계수 x 질주 1.25 x 표면 음량. | 아래 '표면 판정 결함' 참고. | Assets/_Project/Scripts/Core/SoundRules.cs:7,62-64; Assets/_Project/Scripts/Gameplay/CombatantAudio.cs:200-210 |
| 확정 | 총성 | 근거리 4종(권총·SMG·소총·산탄총, CC0 라이브러리를 잘라 사용) + 원거리 5종(저격 포함, 같은 라이브러리의 mid distance 녹음을 모노 변환·저역 통과·저음 반사 2개 추가해 파생). | 원본이 한 라이브러리의 단발 녹음이라 변형이 적다. Sentinel(저격 대용)은 소총 소리를 낮은 피치로 재사용(CombatantAudio.cs:25). 매 발 랜덤 피치 지터(PitchJitter) 적용, 변형 개수는 무기당 1개 추정. | docs/ASSET_LEDGER.md:39; Assets/_Project/Scripts/Core/SoundRules.cs:76-98 |
| 확정 | 장전 | OpenGameArt CC0 3개(권총·소총·산탄총). 다른 무기가 어느 것을 쓰는지는 확인 필요. | 탄피·격발 공이·빈 탄창 소리 없음(확인 필요). | docs/ASSET_LEDGER.md:40 |
| 확정 | 유리 깨짐 | 파일 없음. 노이즈와 짧은 고음 톤을 코드로 합성한 0.9초 충돌음 + 파편 14개. | 유리 두께별 변형 없음. 합성음이라 인공적으로 들릴 가능성(추정, 준환 지적 '스타워즈'와 일치). | Assets/_Project/Scripts/Gameplay/GlassFx.cs:7-17 |
| 확정 | 문 | 자체 합성 wav 6개(열림 3: 삐걱, 닫힘 3: 쿵+딸깍), 고정 시드 잡음·톱니파. | 합성음. | docs/ASSET_LEDGER.md:44; tools/derive_g11_audio.py:1-5 |
| 확정 | 탄 스침 | 합성: 1.2 ms 딱 소리 + 음높이가 내려가는 공명 대역 잡음, 3개. | 음높이가 내려가는 공명 잡음은 SF 레이저 소리와 비슷하다(추정). '스타워즈' 느낌의 유력한 후보. | docs/ASSET_LEDGER.md:43; tools/derive_g4_audio.py:1-5 |
| 확정 | 그 외 코드 합성 | 엘리베이터, 차량 엔진·경적·타이어, 근접, 투척 폭발, 천둥(StormFx), 앰비언스, 차량 파손 등이 코드로 합성된다. | '녹음 없음, 합성 위주'라서 실물감이 약하다. | Scripts/Gameplay 검색(AudioClip.Create 사용 파일 10개) |
| 확정 | 발소리 원본 | Kenney Impact Sounds의 footstep_concrete/grass/wood, 흙·자갈은 footstep_snow, 금속은 impactMetal_light를 대신 사용. 피격은 impactPunch. | 눈 발소리를 흙에 쓰는 등 표면과 소리가 어긋난다(코드/ledger에서 확인한 사실). 같은 팩이라 질감이 단일하다. | docs/ASSET_LEDGER.md:41 |
| 확정 | 오디오 파일 수 | wav 약 57개(발소리 29, 문 6, 총성 9, 재장전 3, 스침 3, 피격·종 7) — 라이선스 모두 CC0 또는 자체 제작(ledger S-0001~6). | Sonniss·Freesound는 아직 미사용. | Assets/_Project/Audio (파일 수 계산) |
| 제안 | 동시 발음·압축 | 정책 문서만: 동시 발음 32개, Vorbis 70%, 긴 앰비언스 스트리밍. | 구현 여부는 확인하지 않았다. | docs/AUDIO.md:31-34 |

## 표면 판정 결함 (이름 파싱)

| 상태 | 결함 | 내용 | 제안 수정 | 근거 |
|---|---|---|---|---|
| 확정 | 오브젝트 이름 파싱 | 발소리 표면이 콜라이더 이름 'Colliders' + 부모 이름 'Lot03_House'/'Station01'에서 건물 종류를 Enum.TryParse해 정한다. 실패하면 조용히 Concrete. 건물 이름 규칙이 바뀌거나 프리팹을 쓰면 전부 Concrete가 된다(06 문서 B4). | 재질→표면 매핑 표 한 개(머티리얼 이름 또는 콜라이더 태그). 이름 파싱 폐기. | Assets/_Project/Scripts/Gameplay/CombatantAudio.cs:218-240 |
| 확정 | 탄흔도 같은 방식 | House=Wood, 나머지 건물=Metal, 정거장=Concrete로 건물 종류만 본다. 지붕·소품 구분 없음(shortcut 주석). | 같은 표면 태그를 쓴다. BulletSurfaceTag가 이미 있어 확장하면 됨. | Assets/_Project/Scripts/Net/BulletSurfaces.cs:24-30 |
| 확정 | 계단 추정 | 폭 0.4 m 미만 박스를 계단으로 본다(shortcut 주석). 지붕·소품은 구분되지 않는다. | 계단·지붕·소품 박스에 태그. | Assets/_Project/Scripts/Gameplay/CombatantAudio.cs:237-240 |
| 확정 | 도로·지형 | 도로는 콜라이더가 없어 PavedRoads.Contains로 판정해 Concrete로 매핑. 지형은 레이어 이름에 Grass/Dirt/Mud가 들어 있는지로 판정, 없으면 Concrete. | 아스팔트·자갈·모래 등 표면 확장, 지형 레이어 id에 표면 태그를 명시. | Assets/_Project/Scripts/Gameplay/CombatantAudio.cs:221-226 |
| 확정 | 테스트 공백 | FootstepSurfaceTests가 기존 4종만 확인한다. 새 재질로 바뀌어도 소리는 안 바뀐다. | 표면 태그 표를 테스트 데이터로 삼는다. | docs/research/06_missed_items.md:B4 |
| 확정 | AI 청각 연동 | 봇의 발소리 청취(BotHearing.SurfaceAt)가 같은 함수를 쓴다. 표면 판정을 고치면 봇 청취 거리도 바뀐다. | 회귀 테스트(봇 감지 반경) 필요. | Assets/_Project/Scripts/Gameplay/CombatantAudio.cs:211-212 |

## 준환의 불만

총소리·걸음·창문 깨짐의 약 90%가 '스타워즈' 같다는 지적(MASTER_PLAN §2). 현실적이고 층·방향이 들리는 위치 기반 소리를 원한다. 후보를 에이전트가 모아 오면 준환이 전부 듣고 직접 고른다.

- 원인 후보(추정, 아직 청취 비교 없음): ① 총 외 대부분이 코드 합성음(유리·문·차량·엘리베이터·천둥·탄 스침), ② 탄 스침이 SF 레이저와 비슷한 하강 공명음, ③ 발소리가 한 팩(Kenney 임팩트 소리)에 눈·금속 충돌음을 대용해 질감이 단일, ④ 총성은 단발 녹음 몇 개라 변형이 적음.
- 완료 기준(제안): 같은 건물 2층에서 사격·걷기를 재생해 방향·층 구분이 되는 자동 테스트 + 준환 청취.

## 목표 설계 (MASTER_PLAN §2, 모두 제안)

| 상태 | 항목 | 설계안 | 비용/주의 | 근거 |
|---|---|---|---|---|
| 제안 | 방향·거리 | 소리 종류별 3D 감쇠 곡선(총성 수백 m, 발소리 30~40 m). HRTF 가능 여부 Unity 6 확인. | 공간화 플러그인은 의존성이면 준환 승인. | docs/research/MASTER_PLAN.md:34-37 |
| 제안 | 수직(층) | 같은 건물 다른 층이면 바닥 차폐(저역 통과+감쇠) + 계단·천장 방향 단서. 상대 높이 차를 오디오에 반영. | 층 판정 규칙(바닥 콜라이더 개수 또는 건물 층 정보) 필요. | docs/research/MASTER_PLAN.md:37 |
| 제안 | 차폐/폐색 | 소스–리스너 사이 벽 수만큼 저역 통과+볼륨 감소(레이캐스트 샘플링). | 50명 동시 사격에서 레이캐스트 예산 제한. | docs/research/MASTER_PLAN.md:38 |
| 제안 | 잔향 존 | 실내/계곡/도심 골목/숲/물가별 리버브. 총성은 근거리·중거리·원거리 꼬리 3겹, 실내/실외별 꼬리. | AudioMixer 또는 리버브 존 도입 필요(현재 없음). | docs/research/MASTER_PLAN.md:39 |
| 제안 | 표면별 발소리 | 표면 태그 x 속도(걷기/달리기/웅크림) x 신발/자세. 계단·금속 계단·유리 파편·젖은 땅·물 첨벙 포함. | 클립 수 증가(표면 12종 x 변형 5). | docs/research/MASTER_PLAN.md:40 |
| 제안 | 행동음 전부 | 총기별 발사·장전·격발 공이, 탄피, 근접, 문, 창문(두께별), 줍기, 치료, 차량, 부위별 피격, 수영, 낙하, 폭발·섬광탄 귀울림. | 소스 조사 범위가 가장 큼. | docs/research/MASTER_PLAN.md:41 |
| 제안 | 환경음 | 지역별 앰비언스(도심 바람·전선 윙윙, 숲, 강, 바다), 날씨, 오염 지대(가이거 계수관). | 현재 앰비언스 합성을 녹음으로 교체할지 결정. | docs/research/MASTER_PLAN.md:42 |
| 제안 | 믹서·우선순위 | 50명 동시 사격에서 폭주 방지: 보이스 제한, 거리 기반 우선순위, 서버가 소리 이벤트를 관심 영역 안에만 전송. | 관심 영역 300 m와 총성 750 m 범위 불일치(ADR-135) 처리. | docs/research/MASTER_PLAN.md:43 |
| 제안 | 네트워크 연동 | 소리는 이벤트로 동기화(발사), 걸음 간격은 클라이언트 계산. | 현재도 ObserversRpc로 관찰자에게만 간다(network 섹션). | docs/research/MASTER_PLAN.md:44 |

## 후보 소스·라이선스 (모두 미검증)

| 상태 | 소스 | 라이선스(문서 기준) | 용도 | 주의 | 근거 |
|---|---|---|---|---|---|
| 제안 | Freesound | 파일별. CC0 필터만(AUDIO.md). CC-BY는 크레딧 가능 여부 미정(MASTER_PLAN) | 발소리·유리·생활음 보충 | 미검증. 파일 하나마다 CC0 확인과 ledger 기록 필요. 다운로드는 준환 승인(전역 규칙). | docs/AUDIO.md:5-12 |
| 제안 | Sonniss GameAudioGDC 번들 | 로열티프리·상업 가능, 출처 표기 불필요(AUDIO.md 기준) | 총성·폭발·환경음·사이렌 대량 | 미검증. 2026 번들 약 7.47 GB. AI 학습 목적 사용 금지 조항. 원본 단독 재배포 금지(확인 필요). 약관 원문을 ledger에 보관. 번들 다운로드는 사람이 한다. | docs/AUDIO.md:5-12; docs/research/MASTER_PLAN.md:49 |
| 제안 | Kenney Audio | CC0 | UI·보조 | 미검증(스타일이 단순). 이미 임팩트 사운드 팩 일부 사용 중. | docs/AUDIO.md:5-12 |
| 제안 | OpenGameArt | 파일별, CC0만(AUDIO.md는 CC0/CC-BY) | 보충 | 미검증. 현재 재장전·물 발소리가 여기서 왔다. | docs/AUDIO.md:5-12 |
| 제안 | BigSoundBank | CC0(3,500+) | 환경음·생활음 | 미검증. | docs/AUDIO.md:5-12 |
| 제안 | itch.io 총성 팩 | 제작자별 | 총성 | 약관 개별 확인. | docs/AUDIO.md:5-12 |
| 제안 | 직접 합성(다층) | 자체 제작 | 총성 대안: 폭발음+금속+꼬리 | 실제 총기 녹음이 없을 때의 현실적 대안(MASTER_PLAN). 이미 쓰는 합성음이 불만의 원인이라 질을 따져야 함. | docs/research/MASTER_PLAN.md:50 |
| 확정 | 제외 | — | — | TunePocket, ElevenLabs 무료 산출물, 라이선스 불명, 상업 게임·영화 추출음, 비상업(NC)·BBC 류. | docs/AUDIO.md:14-15; docs/research/MASTER_PLAN.md:49 |

## 청취 시트 계획 (준환이 직접 고른다)

에이전트가 후보를 소리 종류별(총성 xN, 표면별 발소리, 유리 깨짐 …)로 모아 HTML 오디오 플레이어 한 장으로 만든다. 각 행에 라벨·라이선스·출처 링크·길이를 적고 A/B로 비교해 준환이 고른다. 다운로드는 임시 폴더에만, 고른 것만 저장소에 넣고 ASSET_LEDGER에 기록한다(CLAUDE.md 규칙 1).

- 순서: ① 소리 이벤트 목록 확정(아래 표) → ② 소스별 후보 조사(Freesound CC0 필터, Sonniss, Kenney, OpenGameArt) → ③ 종류당 3~5개 후보 → ④ 청취 시트 → ⑤ 준환 선택 → ⑥ 구현.
- 다운로드·약관 동의·결제는 에이전트가 하지 않는다(규칙 3). 사람이 할 일은 목록으로 모아 준환에게 전달.
- 시트에서 이미 쓰는 소리(현재 합성음)도 같이 넣어 비교 기준으로 삼는다(제안).
- 정의서의 audio 열(kind=audio)에 선택된 파일 경로를 채운다.

## 표면 태그 표 (제안, 14종)

| 상태 | 태그 | 대응 재질/출처 | 탄흔 | 발소리 메모 | 근거 |
|---|---|---|---|---|---|
| 제안 | Concrete | 콘크리트·보도블록·노출 콘크리트 | Concrete | 기본값. 지금도 폴백이라 '조용한 폴백'과 구분하려고 명시 태그 필수. | docs/research/02_map_layout.md:68-82 |
| 제안 | Asphalt | 아스팔트 도로(asphalt_02/05, Asphalt015) | Concrete | 지금은 Concrete로 처리(PavedRoads). | docs/research/02_map_layout.md:70 |
| 제안 | Gravel | 자갈·비포장(gravel_ground_01, gravel_road) | Ground | 소리가 크고 발소리 반경이 큼. | docs/research/02_map_layout.md:71 |
| 제안 | Dirt | 마른 흙(dirt_floor, Ground) | Ground | 지금 눈 발소리로 대체 중 -> 교체. | Assets/_Project/Art/Textures/PBR/PolyHaven |
| 제안 | Mud | 젖은 흙·진흙(습지, 해안) | Ground | 지금 Dirt로 통합. | Assets/_Project/Scripts/Core/SoundRules.cs:58-62 |
| 제안 | Grass | 풀(aerial_grass_rock, grass_ground) | Ground | 조용함(0.75). | Assets/_Project/Scripts/Core/SoundRules.cs:22-30 |
| 제안 | Sand | 모래 해안 | Ground | 신규. | docs/research/02_map_layout.md:71 |
| 제안 | Rock | 바위·절벽(rock_boulder_dry, Rock051) | Concrete | 지금 Concrete로 폴백. | Assets/_Project/Scripts/Core/SoundRules.cs:58-62 |
| 제안 | WoodFloor | 실내 마루(oak_wood_planks, Planks039/040) | Wood | 울림 1.15. | Assets/_Project/Scripts/Core/SoundRules.cs:22-30 |
| 제안 | WoodOutdoor | 부두·데크·울타리 판자 | Wood | 신규. | docs/research/02_map_layout.md:74 |
| 제안 | Tile | 타일·실내 장판/비닐 | Concrete | 06 문서가 +3종 중 하나로 권장. | docs/research/06_missed_items.md:B4 |
| 제안 | MetalPlate | 철판 바닥·공장(metal_plate, Metal064) | Metal | 울림 1.3. 지금은 금속 충돌음 대용. | Assets/_Project/Scripts/Core/SoundRules.cs:22-30 |
| 제안 | MetalStairs | 철제 계단 | Metal | 층 판정에 쓰이는 단서. 지금은 폭 0.4 m 미만 박스로 추정. | Assets/_Project/Scripts/Gameplay/CombatantAudio.cs:237-240 |
| 제안 | TinRoof | 골함석 지붕(corrugated_iron) | Metal | 옥상 발소리, 비 소리 연동. | docs/research/05_map_assets.md:56 |
| 제안 | Water | 얕은 물·논·강 여울 | Ground | 수영과 구분: 첨벙(얕음) / 수영(깊음). | Assets/_Project/Scripts/Core/SoundRules.cs:58-62 |
| 제안 | GlassShards | 깨진 유리 파편(이벤트로 일시 생성) | Glass | MASTER_PLAN §2 요구. | docs/research/MASTER_PLAN.md:40 |

## 소리 이벤트 목록 (누가 듣나 / 반경 / 층)

| 상태 | 이벤트 | 듣는 사람 | 반경(m) | 층(레이어) | 현재 | 근거 |
|---|---|---|---|---|---|---|
| 확정 | 발소리 | Body 범위 안의 모두(+봇 14 m) | 달리기 40, 질주 x1.25, 앉기 x0.4, 느린걷기 x0.25, 엎드림 x0.1; 표면 x0.75~1.3 | 표면 클립 1겹(+신발 층 제안) | 구현됨(6표면). 봇 청취 반경 14 m(rules.json Bot). | Assets/_Project/Scripts/Core/SoundRules.cs:14-37; Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:25 |
| 확정 | 총성 | 관찰자(관심 영역 300 m), 봇 80 m | 12~750(감쇠), 소음기 x4 멀리 | 근거리 + 원거리 2겹(제안: 중거리·실내/실외 꼬리 3겹) | 2겹 구현. | Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:26; Assets/_Project/Scripts/Core/SoundRules.cs:76-98 |
| 확정 | 탄 스침 | 탄이 3 m 안으로 지나간 사람(사수 6 m 이내 제외) | 3 | 합성 1겹 | 구현됨(합성). | Assets/_Project/Scripts/Core/SoundRules.cs:101 |
| 확정 | 유리 깨짐 | Impact 범위 3~60 m, 봇 40 m | 60 | 합성 1겹 + 파편 | 구현됨(합성). 두께별 변형은 제안. | Assets/_Project/Scripts/Gameplay/GlassFx.cs:7-17; Assets/_Project/Data/Resources/GameData/rules.json:Bot GlassHearingRadius |
| 확정 | 문 열림/닫힘 | Body 범위(제안) 내 | 추정 40 이하(확인 필요) | 파일 1겹 | 합성 wav. | docs/ASSET_LEDGER.md:44 |
| 확정 | 폭발(수류탄·차량) | Explosion 범위 | 15~800 | 합성 | 구현됨(합성). | Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:27 |
| 확정 | 차량 엔진·경적 | Vehicle 범위, 경적은 봇 60 m | 6~150 | 엔진 루프+경적 루프 | 합성 루프. | Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:28; Assets/_Project/Scripts/Core/VehicleFxRules.cs |
| 확정 | 엘리베이터 | Elevator 범위 | 3~60 | 합성 | 구현됨(합성). | Assets/_Project/Scripts/Gameplay/SpatialAudio.cs:29 |
| 확정 | 장전 | Body 범위 | 2~40 | 파일 1겹 | 3종, 몸 소스로 재생(발소리만큼만 들림, CombatantAudio.cs:279). 탄창·격발 분리는 제안. | docs/ASSET_LEDGER.md:40; Assets/_Project/Scripts/Gameplay/CombatantAudio.cs:279 |
| 확정 | 근접·주먹 | Body 범위 | 2~40 | 합성+impactPunch | 구현됨. | Assets/_Project/Scripts/Gameplay/MeleeAudio.cs |
| 제안 | 부위별 피격 | 피격자(전부), 근처 Body 범위(제안) | Body | 머리/몸/다리 3종 | 제안. | docs/research/MASTER_PLAN.md:41 |
| 제안 | 줍기·치료·수영·낙하 | Body 범위(제안) | Body | 1겹 | 제안(현재 구현 여부는 확인 필요). | docs/research/MASTER_PLAN.md:41 |
| 제안 | 섬광탄 귀울림 | 맞은 사람 | 개인 | 2D 고음+볼륨 덕킹 | 볼륨만 낮춤. 귀울림 소리는 제안. | Assets/_Project/Scripts/Gameplay/FlashOverlay.cs:120-128 |
| 제안 | 오염 경고(사이렌)·가이거 | 전원(사이렌), 오염 지대 안(가이거) | 전역/지역 | 2D 사이렌, 가이거 루프(강도=오염 DPS) | 봉쇄 배너 소리 훅은 있음(ZoneBanner). 소리 내용은 제안. | docs/research/MASTER_PLAN.md:42,149-165 |
| 확정 | 환경 앰비언스 | 리스너 | 2D | 바람·새·귀뚜라미·물·룸톤 | 합성 구현. | Assets/_Project/Scripts/Gameplay/AmbientBeds.cs:5-12 |

## 정해야 할 것

- [x] **CC-BY 소리를 크레딧 화면에 올려 쓸 것인가** (확정) → CC0만 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: CC0만 / CC-BY 허용(크레딧 화면에 표기)  · 추천: CC0만
  - 근거: AUDIO.md는 Freesound에서 CC-BY를 쓰지 않는다. 크레딧 화면(CreditsScreen)은 이미 CC BY 4.0 차량으로 쓰고 있어 기술적으로는 가능. 준환 결정.
- [ ] **Sonniss 번들 사용** (미정) 
  - 선택지: 사람이 번들 약관 읽고 다운로드 / 사용 안 함  · 추천: 사람이 약관 확인 뒤 사용
  - 근거: 상업 가능·표기 불필요지만 7.47 GB, 원본 재배포 금지, AI 학습 금지 조항. 규칙 3상 에이전트는 다운로드·약관 동의 불가.
- [x] **AudioMixer 도입(그룹·리버브·덕킹)** (확정) → 도입 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 도입(파일 구조 변경) / 코드 필터만  · 추천: 도입
  - 근거: 잔향 존과 우선순위가 믹서 없이는 어렵다. 파일 구조 변경이므로 준환 승인 필요.
- [ ] **HRTF/공간화 플러그인** (미정) 
  - 선택지: Unity 내장 확인 뒤 결정 / 외부 플러그인 추가  · 추천: Unity 내장 확인 뒤 결정
  - 근거: 의존성 추가는 먼저 묻는다(전역 규칙). 현재 내장 3D 패닝만 사용.
- [x] **동시 발음 상한** (확정) → 32 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 32 / 48 / 64  · 추천: 32
  - 근거: docs/AUDIO.md 예시 32. 50명 동시 사격 시 우선순위 규칙과 함께 측정 뒤 확정.
- [x] **표면 태그 확장 범위** (확정) → 14종 표 [준환 위임 일괄 확정 2026-11-16: 추천안 적용 — 검토 후 변경 가능]
  - 선택지: 14종 표(제안) / +3종(Gravel, Asphalt, Tile)만  · 추천: 14종 표
  - 근거: 06 문서는 +3종을 최소로 권장. 재질 12분류와 맞추려면 14종이 자연스럽다.


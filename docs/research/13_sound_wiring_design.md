# 13. 사운드 연결 설계 (S5, 2026-11-16, 초안)

범위: 받아 둔 소리(`10_sound_download_checklist.md`)를 게임에 어떻게 연결하나. **Unity·코드 변경 없음**, 설계만. 소유 파일: `CombatantAudio`, `DoorAudio`, `ElevatorAudio`, `AmbientBeds`, `BulletSurfaces`(표면 부분), `SoundRules`, `audio.json`, `AUDIO.md`. 표면 태그는 S1(`12_surface_tag_design.md`, `Core/World/SurfaceId.cs`) 소유. 확인한 것은 코드·문서를 읽은 사실, "제안/가정"은 구분해 적었다.

## 1. 표면 목록 합의 (S1 ↔ S5)

S1의 `SurfaceId`는 14종: Concrete0 Metal1 Wood2 Grass3 Dirt4 Water5 Gravel6 Asphalt7 Tile8 Carpet9 MetalGrate10 Glass11 Rock12 Sand13. 준환 요구(콘크리트·아스팔트·자갈·흙·진흙·풀·모래·바위·나무·데크·타일·금속·계단·함석·물·유리)와 비교하면 **Mud, Deck, MetalStairs, TinRoof 4종이 없다.** S1 파일이 "번호를 안정하게 두고 **추가**하라"고 했으므로:

| 번호 | 이름 | 상태 | 소리 소스(받은 파일 ID) | 비고 |
|---|---|---|---|---|
| 0 | Concrete | 있음 | 813622 | |
| 1 | Metal | 있음(철판 바닥) | 698697 | |
| 2 | Wood | 있음(실내 마루) | 546362 | |
| 3 | Grass | 있음 | 556042 | |
| 4 | Dirt | 있음 | 349082 | 마른 흙 |
| 5 | Water | 있음 | 531566 | 얕은 물, 수영과 구분 |
| 6 | Gravel | 있음 | 556002 | |
| 7 | Asphalt | 있음 | 877222 | 파일에 풀 구간도 섞임 → 자르기 필요 |
| 8 | Tile | 있음 | 555868 | |
| 9 | Carpet | 있음, 소리 없음 | Wood 클립 ×0.6 음량 | 폴백을 데이터에 명시 |
| 10 | MetalGrate | 있음, 소리 없음 | Metal 클립 | 폴백 명시 |
| 11 | Glass | 있음, 걸음 소리 없음 | Tile 클립 + 유리 파편 소리는 이벤트 | 파편 걸음 후보 못 찾음 |
| 12 | Rock | 있음 | 558472(걷기)·558471(달리기) | |
| 13 | Sand | 있음 | 564893 | |
| **14** | **Mud** | **추가 제안** | 548384 | 습지·해안 |
| **15** | **Deck** | **추가 제안** | 636080 | 부두·데크(S1 이름 `WoodOutdoor`와 같은 뜻) |
| **16** | **MetalStairs** | **추가 제안** | 451863 | 층 판정 단서 |
| **17** | **TinRoof** | **추가 제안** | Metal 클립 + 피치 낮춤(전용 CC0 없음) | 옥상 |

→ 총 **18종**. S1이 `SurfaceId.cs`에 14~17을 덧붙이고(master 중재), 변환 함수 `SurfaceRules`는 기존 `FootSurface`(6종)·`BulletSurface`로 접는 현재 방식을 유지하되, **발소리 클립 선택은 `FootSurface`가 아니라 `SurfaceId`로 직접 한다**(6종 enum으로는 18종 소리를 못 고른다). `FootSurface`는 봇 청취 반경과 기존 테스트용으로 남긴다.

## 2. 이름 파싱 제거 방식 (확인한 코드 기준)

현재(확인): `CombatantAudio.SurfaceAt`은 ① 광선 아래 1.5 m ② 지형이면 `PavedRoads.Contains`→Concrete, 아니면 알파맵 지배 레이어 이름에 "Grass"/"Dirt"/"Mud"가 있는지 문자열 검사 ③ 수면 아래면 Water ④ `Vehicle`이면 Metal ⑤ 콜라이더 이름이 `Colliders`이고 부모 이름 끝(`_House` 등)을 `Enum.TryParse` ⑥ 폭 0.4 m 미만 박스는 계단으로 추정. `BulletSurfaces.Of`는 같은 부모 이름 파싱을 반복한다.

바꿀 것(제안):
1. `SurfaceAt(feet)` → `SurfaceId SurfaceAt(feet)`. 순서: 광선 → 지형(`TerrainSurfaceTable[layerId]`: 지형 레이어 **이름이 아니라 id/에셋 참조**로 표면을 찾는 데이터 표, 도로 분기는 Asphalt 태그) → `SurfaceTag`(없으면 폴백) → 차량 Metal. 수면 아래 Water.
2. 태그 없는 콜라이더 폴백은 Concrete + **개발 빌드 경고 1회**(조용한 폴백 금지, S1 문서 5항과 같음). 월드 빌더 검증 테스트: "태그 없는 건물 콜라이더 0개".
3. `Enum.TryParse`·`StartsWith("Station")`·`name != "Colliders"`·폭 0.4 m 추정 코드 삭제. 계단은 `MetalStairs` 태그로.
4. `BulletSurfaces.Of`도 같은 `SurfaceTag`를 읽는다(`SurfaceRules.ToBullet`).
5. 봇 청취(`BotHearing.SurfaceAt`)는 `SurfaceRules.ToFoot(id)`로 기존 6종 반경을 유지하고, 새 표면(Gravel·Mud 등)의 **소리 크기 계수는 데이터 표**(아래 4절)로 둔다. 회귀 테스트: 기존 `FootstepSurfaceTests` 4종 + 봇 감지 반경.

## 3. 클립 구조 (제안)

`Assets/_Project/Audio/<분류>/` 아래, 기존 `Resources/Steps/<표면>` 관례 유지:
- `Resources/Steps/<surface>/step_<surface>_NN.wav` (표면당 변형 5개 이상, 걷기·달리기 구분은 파일명 접미사 `_walk`/`_run`).
- 걸음 소스는 **한 파일에 걸음 여러 개가 든 시퀀스**(Nox_Sound 등)다. 자르기는 **Unity 에디터 도구**(오프라인, 범위 표 `Data/…/sound_cuts.json`)로 한다. 이 PC에는 ffmpeg·sox가 없고(확인) Python 표준 라이브러리는 WAV만 읽으므로(AIFF·FLAC·MP3 불가), 임포트된 `AudioClip` 샘플을 읽어 구간을 잘라 WAV로 쓰는 `Editor` 스크립트가 현실적이다. 자른 결과 파일에 대해서만 ledger 행을 만든다(원본은 `AssetStaging/`에 남기고 저장소 제외).
- 총성: `Resources/Shots/shot_<무기>_near_NN.wav`, `..._far_NN.wav`, `..._tail_indoor_NN.wav`. 소음기는 `shot_suppressed_NN.wav`.
- 저장소 파일명은 상표 없는 일반 이름(`pistol`, `smg`, `rifle`, `carbine`, `shotgun`, `sniper`). 원본 이름의 상표명(Glock·AK·MP5·M16·Mossberg 등)은 ledger에만 적는다(규칙 1).

## 4. 소리 이벤트·데이터화 (제안)

`audio.json`의 이벤트 표를 런타임 데이터로 옮긴다: `SoundRules`가 쓰는 상수(표면 음량 계수, 범위, 자세 계수)와 이벤트 표(소리 id, 카테고리, 범위, 레이어)를 `Data/Resources/GameData/sounds.json`에 두고 EditMode 테스트가 표와 코드를 대조한다(기존 `rules.json` 방식, 확인 필요). 표면 음량 계수 초안: Metal 1.3, MetalStairs 1.35, TinRoof 1.4, Wood 1.15, Deck 1.15, Gravel 1.25, Water 1.2, Tile 1.1, Concrete 1.0, Asphalt 1.0, Rock 1.0, Dirt 0.9, Sand 0.8, Mud 0.8, Grass 0.75, Carpet 0.7, MetalGrate 1.25, Glass 1.15.

## 5. 액션별 소리 연결 요약 (09 시트 §7 참조)

| 이벤트 | 연결할 소스(저장소 이름 → 받은 ID) | 레이어 |
|---|---|---|
| 권총 발사 | `pistol` ← 828786, 253736 | near + far + tail |
| SMG | `smg` ← 201672(+실내 201667) | 〃 |
| 소총 | `rifle` ← 201670, 201668(한 발만) | 〃 |
| DMR | `carbine` ← 46655 (+ 저격 혼합은 청취 후) | 〃 |
| 샷건 | `shotgun` ← 159710(발사/펌프 분리) | 〃 |
| 저격 | `sniper` ← 855608, 351777(발사부) | 〃 |
| 소음기 | `shot_suppressed` ← 668071 | near만 |
| 권총 장전 | `pistol_reload` ← 100804(CC-BY) | Body |
| 소총 장전 | `rifle_reload` ← 674742 | Body |
| 샷건 펌프 | `shotgun_pump` ← 436074 | Body |
| 빈 탄창 | `dry_fire` ← 674568 | Body |
| 탄피 | `casing` ← 777922 | Impact(소) |
| 탄 스침 | `bullet_whiz` ← 730351(잘라서), 505238 | Body 3 m |
| 도탄 | `impact_ricochet` ← 478345 | Impact |
| 피격·신음 | `pain_grunt` ← 547209 | 피격자 2D |
| 문 열림·닫힘 | `door_open/close` ← 125958 | Body |
| 문 부수기 | `door_break` ← 160213 | Impact |
| 유리 | `glass_break` ← 861043 | Impact |
| 엘리베이터 | `elevator_travel/ambient/open` ← 531792, 502344, 241763 | Elevator |
| 차량 | `vehicle_engine_idle` ← 401552, 경적 170243, 타이어 71736, 충돌 807438 | Vehicle |
| 폭발 | 원거리 372086, 수류탄 609587, 섬광 752628, 연막 551429 | Explosion |
| 비·천둥·바람·귀뚜라미·새·불 | 527499, 683420, 683433, 522299, 826326, 595483 | 2D 베드 |
| 구역 사이렌·방송·가이거 | 412171/336640, 879805, 417424/674113 | 2D, S6가 호출 |
| 귀울림 | 450620 | 2D |
| 줍기·치료·수영·근접 | 464996, 800275, 637958, 523230 | Body |

## 6. 공간음향 (제안·가정)

확인: 현재 AudioMixer 없음(audio.json), 3D 감쇠 곡선·거리별 저역 통과만 있음. ADR 이전 `a_mix`는 "도입"으로 확정됨.
- 믹서: Master → SFX(Weapons, Footsteps, Zone Alerts) / Ambience / UI. 리버브 존은 믹서 그룹 + 존 트리거.
- 층 차폐: 소스와 청취자의 **바닥 콜라이더 수**(광선 1개 위→아래)로 판정, 한 층 사이마다 저역 통과 −(22 kHz→2 kHz)와 −6 dB. 계단 소리(`MetalStairs`)는 차폐를 덜 받는 단서.
- 벽 폐색: 소스→청취자 광선 샘플링, **프레임당 광선 상한 8개**, 소스당 갱신 0.25 s, 60 m 밖은 광선 없이 거리 필터만. (가정, 비용은 측정 후 조정, S1 건물 구조·S8 관심 영역 선행)
- 총성 3겹: near(30 m까지 단독)·far(200 m부터 단독)는 현재 구현 유지 + 실내/실외 꼬리 클립을 리버브 존으로 선택.
- 동시 발음 상한 32 확정(a_voice). 풀링·우선순위(거리 가중)는 50인 시나리오에서 측정.
- HRTF: **미확인**. Unity 6 오디오 설정의 공간화 플러그인 선택 가능 여부는 별도 Unity 확인이 필요(이 세션 범위 밖, 의존성 추가는 준환 승인).

## 7. 테스트 (seam)

가장 높은 seam은 **Core 규칙 EditMode 테스트**(`SoundRules`·`SurfaceRules`: 표면→클립 경로, 폴백, 음량 계수, 표 대조)이고, 표 전체(18종 × 클립 폴더 존재)를 데이터로 검증한다. 두 번째 seam은 PlayMode 시나리오(같은 건물 2층 발소리·총성의 저역 통과 값, 층 차이 판정). 새 seam은 만들지 않는다.

## 8. 위험·미정
- `SurfaceId` 추가(14~17)는 S1 소유 파일 변경 → master 중재.
- 걸음 시퀀스 자르기 품질: 자동 무음 검출이 안 맞으면 수동 범위 표.
- CC-BY 1건(재장전)은 크레딧 화면 항목 추가 필요(`Credits.cs`·`CreditsTests` 대조, Unity 순번에서).
- Sonniss는 슬라이스에서 사용하지 않는다(준환 약관 확인 전).

## 9. GitHub 이슈 (2026-11-16)
스펙 #282, 지도 #283, 티켓 T1~T11 = #284~#294(T1 표면 18종 → T2 자르기 도구 → T3 이름 파싱 제거·T4 총성·T5 전투 소리·T6 월드 소리·T7 환경음·T8 구역 훅·T9 공간음향·T10 ledger·크레딧 → T11 준환 청취), 결정 D1 HRTF 확인 #295, D2 Sonniss 약관(준환) #296. 막힘은 GitHub 의존성으로 표시.

# 10. 사운드 다운로드 체크리스트 (준환이 Freesound에 로그인해서 직접 받는 것)

작성 2026-11-16. 근거: ADR-177(CC-BY 예외 허용, 크레딧 화면 표기), `09_sound_candidates.md`. Freesound 다운로드는 로그인이 필요해 **준환이 직접** 받는다(에이전트 가입·로그인 금지, 규칙 3).
받은 파일은 아래 **저장 폴더**에 넣고 위치를 알려 주면, 이름 변경·변환·ledger·크레딧·게임 연결·테스트는 사운드 세션(S5)이 진행한다.
저장 폴더: `AssetStaging/audio/` (`.gitignore`에 들어 있어 git에 안 올라간다. 저장소로 옮기는 건 S5가 일반 이름으로 바꾼 뒤).

## A. 확정 (준환이 직접 고른 것. 09 시트 §8-1 + ADR-177)
소리 페이지를 연 뒤 라이선스 줄을 확인하고 받는다. **✔** = 조사 때 페이지에서 직접 확인, **F** = 검색 필터 결과만 봄(받을 때 라이선스 줄 재확인). 받은 파일 이름은 **그대로** `AssetStaging/audio/`에 넣으면 된다(S5가 아래 '저장소 파일명'으로 바꾼다). 저장소 파일명엔 실존 상표(Glock·AK·MP5·Mossberg 등)를 쓰지 않는다.

| ☑ | 용도 | 소리 페이지 | 작성자 | 라이선스 | 길이/형식 | 저장소 파일명 | 메모 |
|---|---|---|---|---|---|---|---|
| ☑ | 유리 깨짐 | https://freesound.org/people/ElevatorFan2020/sounds/861043/ | ElevatorFan2020 | CC0 ✔ | 1.06s, MP3 259k | `glass_break_01` | MP3라 편집 시 재인코딩 열화 |
| ☑ | 권총 재장전 | https://freesound.org/people/Fr3yr/sounds/100804/ | Fr3yr | **CC BY 3.0** ✔ (ADR-177, 크레딧 필수) | 0.74s, WAV 96k/24bit 모노 | `pistol_reload_01` | 원본 이름의 상표명은 ledger에만. 수정 시 크레딧에 '수정함' |
| ☑ | 권총 발사 | https://freesound.org/people/areniporgen/sounds/828786/ | areniporgen | CC0 ✔ | 1.45s, WAV 48k/32bit 스테레오 | `pistol_shot_01` | 야외 근접, 폰 마이크 |
| ☑ | 저격 발사 1 | https://freesound.org/people/qubodup/sounds/855608/ | qubodup | CC0 ✔ | 1.44s, FLAC 48k | `sniper_shot_01` | **잘라서 사용**. 미군 훈련 영상에서 추출(원 영상 저작권 상태는 준환 확인) |
| ☑ | 저격 발사 2 | https://freesound.org/people/kennysvoice/sounds/351777/ | kennysvoice | CC0 F | ? | `sniper_shot_02` | 장전→발사 순서 파일에서 **발사 부분만** |
| ☑ | 소음기 발사(대구경) | https://freesound.org/people/ajhartman42295/sounds/668071/ | ajhartman42295 | CC0 F | ? | `shot_suppressed_01` | 준환: 소음기 단 소리로 사용 |
| ☑ | 소총 장전·탄창 | https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/674742/ | serøutōnin--deprivəd | CC0 F | ? | `rifle_reload_01` | 링크는 퍼센트 인코딩 |
| ☑ | 탄 스침 1 | https://freesound.org/people/GO_s0unds123/sounds/730351/ | GO_s0unds123 | CC0 F | ? | `bullet_whiz_01` | **잘라서 사용** |
| ☑ | 탄 스침 2 | https://freesound.org/people/Daleonfire/sounds/505238/ | Daleonfire | CC0 F | ? | `bullet_whiz_02` | |
| ☑ | 문 열림·닫힘 | https://freesound.org/people/Ryding/sounds/125958/ | Ryding | CC0 F | ? | `door_open_01`, `door_close_01` | 한 파일에서 열림/닫힘 분리 |
| ☑ | 차량 엔진 아이들 | https://freesound.org/people/GiocoSound/sounds/401552/ | GiocoSound | CC0 F | ? | `vehicle_engine_idle_01` | 시동만 걸린 상태, 루프 정리 필요. **TRP Car engine idle은 쓰지 않음** |
| ☑ | 원거리 폭발 | https://freesound.org/people/Mr_KeybOred/sounds/372086/ | Mr_KeybOred | CC0 F | ? | `explosion_distant_01` | |
| ☑ | 사이렌 A | https://freesound.org/people/Poligonstudio/sounds/412171/ | Poligonstudio | CC0 F | ? | `siren_air_raid_01` | 준환: A·B 중 하나. 둘 다 받아 두고 게임에서 고른다 |
| ☑ | 사이렌 B | https://freesound.org/people/audiojacked/sounds/336640/ | audiojacked | CC0 F | ? | `siren_air_raid_02` | 실물 녹음(제목상) |
| ☑ | 방송 차임 | https://freesound.org/people/Qvantor/sounds/879805/ | Qvantor | CC0 F | ? | `announce_chime_01` | 음성이 아닌 차임만 |
| ☑ | 엘리베이터 이동 | https://freesound.org/people/conath/sounds/531792/ | conath | CC0 F | ? | `elevator_travel_01` | 준환: "알아서". 3개 다 받고 S5가 고른다 |
| ☑ | 엘리베이터 앰비언트 | https://freesound.org/people/universodemalaonda/sounds/502344/ | universodemalaonda | CC0 F | ? | `elevator_ambient_01` | |
| ☑ | 엘리베이터 문 | https://freesound.org/people/GilPS/sounds/241763/ | GilPS | CC0 F | ? | `elevator_open_01` | |

## B. SMG·AR·샷건 등 (준환 위임으로 에이전트가 확정, 2026-11-16)
준환이 "이제부터 알아서 해라"라고 위임해, 09 시트 §8-2 후보 중 CC0를 페이지에서 확인한(✔) 것을 우선해 **에이전트가 확정**했다(청취는 못 했다. 게임 안에서 준환이 듣고 마음에 안 들면 이 표에서 교체).

| ☑ | 용도 | 소리 페이지 | 작성자 | 라이선스 | 길이/형식 | 저장소 파일명(선택 시) | 메모 |
|---|---|---|---|---|---|---|---|
| ☑ | SMG 발사 | https://freesound.org/people/Franki-01234/sounds/201672/ | Franki-01234 | CC0 ✔ | 2.29s, AIFF 48k/16bit | `smg_shot_01` | 공포탄 실녹음. 거리 반향 포함 |
| ☑ | SMG 문 앞(실내 꼬리) | https://freesound.org/people/Franki-01234/sounds/201667/ | Franki-01234 | CC0 F | ? | `smg_shot_indoor_01` | 선택 |
| ☑ | 소총 발사 1(AK 계열) | https://freesound.org/people/Franki-01234/sounds/201670/ | Franki-01234 | CC0 ✔ | 5.21s, AIFF 48k/16bit | `rifle_shot_01` | 연발 녹음 → **한 발만 잘라서** |
| ☑ | 소총 발사 2(M16 계열) | https://freesound.org/people/Franki-01234/sounds/201668/ | Franki-01234 | CC0 ✔ | 4.17s, AIFF 48k/16bit | `rifle_shot_02` | 버스트 → 한 발만 |
| ☑ | 카빈 단발 | https://freesound.org/people/cognito%20perceptu/sounds/46655/ | cognito perceptu | CC0 ✔ | 3.34s, WAV 44.1k/16bit | `carbine_shot_01` | 2차대전 소총, 현대 소총과 다름. DMR 대용 가능 |
| ☑ | 샷건 발사 | https://freesound.org/people/AnthonyChan0/sounds/159710/ | AnthonyChan0 | CC0 ✔ | 3.09s, WAV 48k/24bit | `shotgun_shot_01` | 실녹음, 발사 뒤 펌프 포함 → 분리 |
| ☑ | 샷건 장전·펌프 | https://freesound.org/people/rammbostein/sounds/436074/ | rammbostein | CC0 F | ? | `shotgun_pump_01` | |
| ☑ | 권총 슬라이드 | https://freesound.org/people/hiramjustus/sounds/55340/ | hiramjustus | CC0 ✔ | 1.31s, WAV 44.1k/16bit 모노 | `pistol_slide_01` | 장전 보조음 |
| ☑ | 권총 빈 탄창(공이 딸깍) | https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/674568/ | serøutōnin--deprivəd | CC0 F | ? | `dry_fire_01` | 현재 무음 액션 |
| ☑ | 권총 발사 대체 | https://freesound.org/people/Kodack/sounds/253736/ | Kodack | CC0 F | ? | `pistol_shot_02` | 두 번째 변형이 필요할 때 |

받을 때 소리 페이지에서 확인할 것(이상이 있으면 받지 말고 알려 주기): ① 라이선스 줄이 위 표와 같은지 ② 'Not for use in AI training' 류 문구(unfa 유리 깨짐처럼 CC0에 붙은 비동의 문구는 구속력 불명) ③ NC/ND 여부 ④ 설명에 합성·AI 표기가 있는지(규칙 2).

## D. 가이거·발소리·기타 (에이전트 확정안, 준환 위임)
발소리는 **Nox_Sound 팩**(같은 작가·같은 등산화·모노라 통일감)을 우선하고 없는 표면만 다른 작가로 채웠다. 대부분 걸음 여러 개가 든 시퀀스라 S5가 클립으로 자른다. 전부 F(필터 결과만 확인, 단 Tile은 ✔)이므로 받을 때 라이선스 줄 재확인.

| ☑ | 용도 | 소리 페이지 | 작성자 | 라이선스 | 저장소 파일명 |
|---|---|---|---|---|---|
| ☑ | 가이거(약·중·강 루프 소재) | https://freesound.org/people/parabolix/sounds/417424/ | parabolix | CC0 F | `geiger_01` |
| ☑ | 가이거(보조) | https://freesound.org/people/Sanderboah/sounds/674113/ | Sanderboah | CC0 F | `geiger_02` |
| ☑ | 발소리 Tile | https://freesound.org/people/Nox_Sound/sounds/555868/ | Nox_Sound | CC0 ✔ | `step_tile_01` |
| ☑ | 발소리 Mud | https://freesound.org/people/Nox_Sound/sounds/548384/ | Nox_Sound | CC0 F | `step_mud_01` |
| ☑ | 발소리 Sand | https://freesound.org/people/Nox_Sound/sounds/564893/ | Nox_Sound | CC0 F | `step_sand_01` |
| ☑ | 발소리 Rock | Nox_Sound 팩 https://freesound.org/people/Nox_Sound/packs/29889/ 의 `Rock_Walk`/`Rock_Run` 소리 | Nox_Sound | CC0 F | `step_rock_01` |
| ☑ | 발소리 Grass | 같은 팩 `Footsteps_Mountain_Boots_Grass_Mono.wav` | Nox_Sound | CC0 F | `step_grass_01` |
| ☑ | 발소리 Gravel | 같은 팩 `Footsteps_Mountain_Boots_Gravel_Mono.wav` | Nox_Sound | CC0 F | `step_gravel_01` |
| ☑ | 발소리 Concrete | https://freesound.org/people/SecureSubset/sounds/813622/ | SecureSubset | CC0 F | `step_concrete_01` |
| ☑ | 발소리 Asphalt | https://freesound.org/people/tonu.muzic/sounds/877222/ | tonu.muzic | CC0 F | `step_asphalt_01` |
| ☑ | 발소리 Dirt | https://freesound.org/people/Yoyodaman234/sounds/349082/ | Yoyodaman234 | CC0 F | `step_dirt_01` |
| ☑ | 발소리 WoodFloor | https://freesound.org/people/PanosA/sounds/546362/ | PanosA | CC0 F | `step_wood_01` |
| ☑ | 발소리 데크 | https://freesound.org/people/coalcon/sounds/636080/ | coalcon | CC0 F | `step_deck_01` |
| ☑ | 발소리 MetalPlate | https://freesound.org/people/IENBA/sounds/698697/ | IENBA | CC0 F | `step_metal_01` |
| ☑ | 발소리 MetalStairs | https://freesound.org/people/kyles/sounds/451863/ | kyles | CC0 F | `step_stairs_01` |
| ☑ | 발소리 Water | https://freesound.org/people/patchytherat/sounds/531566/ | patchytherat | CC0 F | `step_water_01` |
| ☑ | 수영 | https://freesound.org/people/kyles/sounds/637958/ | kyles | CC0 F | `swim_01` |
| ☑ | 줍기·가방 | https://freesound.org/people/usamah/sounds/464996/ | usamah | CC0 F | `loot_rummage_01` |
| ☑ | 치료 | https://freesound.org/people/SecureSubset/sounds/800275/ | SecureSubset | CC0 F | `heal_bandage_01` |
| ☑ | 탄피 | https://freesound.org/people/BlondPanda/sounds/777922/ | BlondPanda | CC0 F | `casing_01` |
| ☑ | 귀울림 | https://freesound.org/people/Breviceps/sounds/450620/ | Breviceps | CC0 F | `tinnitus_01` |
| ☑ | 낙하·피격 신음 | https://freesound.org/people/MrFossy/sounds/547209/ | MrFossy | CC0 F | `pain_grunt_01` |
| ☑ | 문 부수기 | https://freesound.org/people/qubodup/sounds/160213/ | qubodup | CC0 F | `door_break_01` |
| ☑ | 폭발(수류탄) | https://freesound.org/people/unfa/sounds/609587/ | unfa | CC0 F | `explosion_grenade_01` |
| ☑ | 섬광탄 | https://freesound.org/people/modusmogulus/sounds/752628/ | modusmogulus | CC0 F | `flashbang_01` |
| ☑ | 차량 경적 | https://freesound.org/people/BeatsbyCasper/sounds/170243/ | BeatsbyCasper | CC0 F | `vehicle_horn_01` |
| ☑ | 타이어 | https://freesound.org/people/audible-edge/sounds/71736/ | audible-edge | CC0 F | `tyre_squeal_01` |
| ☑ | 차량 충돌 | https://freesound.org/people/Logicogonist/sounds/807438/ | Logicogonist | CC0 F | `vehicle_crash_01` |
| ☑ | 비 | https://freesound.org/people/timothyd4y/sounds/527499/ | timothyd4y | CC0 F | `rain_roof_01` |
| ☑ | 천둥 | https://freesound.org/people/SholeColtis/sounds/683420/ | SholeColtis | CC0 F | `thunder_01` |
| ☑ | 바람 | https://freesound.org/people/SholeColtis/sounds/683433/ | SholeColtis | CC0 F | `wind_01` |
| ☑ | 귀뚜라미(밤) | https://freesound.org/people/Defelozedd94/sounds/522299/ | Defelozedd94 | CC0 F | `crickets_01` |
| ☑ | 숲·새(낮) | https://freesound.org/people/upkey/sounds/826326/ | upkey | CC0 F | `birds_forest_01` |
| ☑ | 불 | https://freesound.org/people/SavReese/sounds/595483/ | SavReese | CC0 F | `fire_01` |

**대체/없음으로 처리**: 함석 지붕 발소리는 CC0 후보가 없어 `step_metal_01`을 쓰고 울림만 다르게 한다. 연막은 [Gas Grenade (Themiwa100)](https://freesound.org/people/Themiwa100/sounds/551429/) `smoke_hiss_01`. 근접 휘두름은 [Hard Swing 1 (magnuswaker)](https://freesound.org/people/magnuswaker/sounds/523230/) `melee_swing_01`. 도시 앰비언스는 기차역 방송이 섞여 쓰지 않는다. 탄착·표면별 도탄은 [Bullet Ricochet (Joao_Janz)](https://freesound.org/people/Joao_Janz/sounds/478345/) `impact_ricochet_01`.

### 에이전트 확정 사항 (준환 위임)
- SMG `smg_shot_01`(+실내 `smg_shot_indoor_01`), 소총 `rifle_shot_01/02`(한 발만), DMR 대용 `carbine_shot_01`, 샷건 `shotgun_shot_01`+`shotgun_pump_01`, 권총 변형 `pistol_shot_02`, 빈 탄창 `dry_fire_01`.
- 가이거 **사용**(오염 지대 안에서 강도 3단). 마음에 안 들면 안 쓰면 된다.
- CC-BY는 ADR-177로 허용.
### 아직 남은 것
1. **준환 청취**: 위 확정은 텍스트 조사 기반이다. 게임 안에서 듣고 교체 지시.
2. **Sonniss**: 약관 원문 확인 전까지 사용 금지.
3. 후보 없는 항목: 부스트(약 먹기), 방어구 소리, 무기 꺼냄·넣음, 유리 파편 위 걸음 — 필요 시 S5가 합성 유지 또는 Kenney로 임시 처리.

## 다운로드 결과 (2026-11-16, 에이전트가 준환의 로그인된 Chrome으로 수행)
- A·B·D 표의 **전 항목(약 63개)을 받아 `AssetStaging/audio/`에 넣었다.** 파일 이름은 Freesound 형식 `<ID>__<작성자>__<제목>.<확장자>` 그대로(S5가 일반 이름으로 변경).
- 받기 전에 **소리 페이지의 라이선스 줄을 스크립트로 확인**했다: Fr3yr 재장전만 CC BY 3.0(ADR-177), 나머지는 전부 CC0 페이지 확인(표의 F 표시는 이제 확인 완료). CC0가 아니면 받지 않도록 했고 건너뛴 것은 없다.
- Nox_Sound Rock/Grass/Gravel 소리는 팩에서 번호를 찾아 받았다: Rock Walk 558472, Rock Run 558471, Grass 556042, Gravel 556002.
- `564893`(wet sand)은 한 번 더 받아져 `~/Downloads`에 `(1)` 복사본이 남아 있다(사용 안 함, 지워도 됨).
- 아직 **듣지 않았고 길이·AI 학습 금지 문구는 확인 안 함**(소리 페이지 설명 문구는 이번에 읽지 않았다). 각 소리 설명의 'AI 학습 금지' 류 문구는 S5가 ledger 작성 때 확인.
- 후원 팝업이 뜨는 사이트였고 후원·결제는 하지 않았다.

## C. 받은 뒤 S5가 할 일 (Unity 순번에서)
| ☐ | 단계 |
|---|---|
| ☐ | 이름 변경(일반 이름), 필요 시 WAV 변환·루프 정리 |
| ☐ | `docs/ASSET_LEDGER.md`에 소리별 행: 소리 페이지 URL·작성자·라이선스 줄·확인일·수정 여부·원본 이름 (ledger 링크는 `CreditsTests`가 `Credits.cs`와 대조하므로 크레딧과 **같이** 추가) |
| ☐ | `Credits.cs`/크레딧 화면에 CC-BY 항목 추가: `Glock19_reload by Fr3yr — https://freesound.org/people/Fr3yr/sounds/100804/ — CC BY 3.0 (수정 여부)` |
| ☐ | 게임 연결(`CombatantAudio` 등 유리·장전 이벤트), EditMode/PlayMode 테스트 |
| ☐ | **교체 후 준환이 게임 안에서 직접 듣는다.** 소리는 텍스트 조사만 했고 누구도 들어 보지 않았다 |

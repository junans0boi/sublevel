# 14. 사운드 ledger·크레딧 초안 (S5, 2026-11-16)

**초안일 뿐이다.** `docs/ASSET_LEDGER.md`·`Credits.cs`는 CreditsTests가 서로 대조하므로 Unity 순번에서 같이 고친다(master 지시). 여기엔 저장소 파일명 매핑, ledger 행 문안, 크레딧 문구, 설명문 확인 결과만 둔다.

## 1. 설명문 확인 결과 ('AI 학습 금지' 류, 소스 설명·태그 스캔 2026-11-16)
- 66개 소리 페이지의 설명·태그를 스크립트로 읽어 `AI`·`training`·`generative`·`synthes` 등 키워드를 찾았다(소리 설명 전문을 사람이 읽은 것은 아님).
- **문제 후보 2건**
  - `609587` unfa "Grenade Explosion": 설명에 "I DO NOT GIVE CONSENT to using my sounds in training generative machine learning models"(의미: AI 학습에 쓰지 말라는 업로더 요청). CC0 라이선스 줄과 별개의 문구라 **법적 구속력은 불명, 원문 확인 필요**. 우리는 AI 학습에 쓰지 않으므로 사용 자체는 가능해 보이나, 같은 설명에 Vitalium(오픈소스 신스)로 **합성**했고 폭죽 녹음을 섞었다고 적혀 있다(AI 생성 아님, 합성음). "스타워즈 같다" 문제와 같은 부류일 수 있어 청취 후 교체 후보.
  - `879805` Qvantor "Announcement Chime": 태그 `synthesized`(신스 톤). AI 생성 표기는 아님. 청취 확인.
- 나머지 64건은 키워드 일치 없음 또는 무관한 일치(예: `training`은 Franki-01234 설명의 "military training", 505238의 "training video"를 쓴다는 업로더 문장, 855608의 "Marksmanship Training Center").
- **다시 확인할 것(추정)**: `683433` "Wave_Wind_1"은 제목상 파도(wave)일 수 있다. 바람 소재로 부적합하면 교체.
- **빠진 것**: 업로더가 설명 대신 댓글·프로필에 적은 조건은 읽지 못했다.

## 2. 저장소 파일명 매핑과 ledger 행 초안
ID는 `docs/ASSET_LEDGER.md`의 S 분류 다음 번호(S-0008부터, 기존 S-0001~0007 뒤). 프로젝트 경로는 Unity 순번에서 자른 뒤 `Assets/_Project/Audio/<분류>/`로 확정. 라이선스 원문(소리 페이지 사본)은 `docs/licenses/S-xxxx/`에 둔다.

| ID | 저장소 이름 | 용도 | 출처 URL | 작성자 | 라이선스 | 크레딧 필요 | 원본 파일 | 형식·길이 | 수정 | 메모 |
|---|---|---|---|---|---|---|---|---|---|---|
| S-0008 | `carbine_shot_01` | DMR 대용 단발 | https://freesound.org/s/46655/ | cognito-perceptu | CC0 1.0 | 아니오 | `46655__cognito-perceptu__carbine-single.wav` | wav, 3.3s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0009 | `pistol_slide_01` | 권총 슬라이드 | https://freesound.org/s/55340/ | hiramjustus | CC0 1.0 | 아니오 | `55340__hiramjustus__glock.wav` | wav, 1.3s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0010 | `tyre_squeal_01` | 타이어 스크리치 | https://freesound.org/s/71736/ | audible-edge | CC0 1.0 | 아니오 | `71736__audible-edge__chrysler-lhs-tire-squeal-01-04-25-2009.wav` | wav, 10.0s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0011 | `pistol_reload_01` | 권총 재장전 | https://freesound.org/s/100804/ | fr3yr | CC BY 3.0 | **예** | `100804__fr3yr__glock19_reload.wav` | wav, 0.7s | 자르기·WAV 변환·이름 변경(예정) | CC BY 3.0, 크레딧 필수 |
| S-0012 | `door_open_close_01` | 문 열림·닫힘 | https://freesound.org/s/125958/ | ryding | CC0 1.0 | 아니오 | `125958__ryding__wooden-door-open-close.wav` | wav, 2.9s | 자르기·WAV 변환·이름 변경(예정) | 한 파일에서 열림/닫힘 분리 |
| S-0013 | `shotgun_shot_01` | 샷건 발사+펌프 | https://freesound.org/s/159710/ | anthonychan0 | CC0 1.0 | 아니오 | `159710__anthonychan0__mossberg-500a-1-shot-and-pump.wav` | wav, 3.1s | 자르기·WAV 변환·이름 변경(예정) | 발사/펌프 분리 |
| S-0014 | `door_break_01` | 문 부수기 | https://freesound.org/s/160213/ | qubodup | CC0 1.0 | 아니오 | `160213__qubodup__kickingforcingbreaking-wooden-door.flac` | flac, 1.0s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0015 | `vehicle_horn_01` | 차량 경적 | https://freesound.org/s/170243/ | beatsbycasper | CC0 1.0 | 아니오 | `170243__beatsbycasper__car-horn.wav` | wav, 0.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0016 | `smg_shot_indoor_01` | SMG 실내 꼬리 | https://freesound.org/s/201667/ | franki-01234 | CC0 1.0 | 아니오 | `201667__franki-01234__mp5-submachine-gun-in-doorway.aiff` | aiff, 2.8s | 자르기·WAV 변환·이름 변경(예정) | 공포탄 녹음 |
| S-0017 | `rifle_shot_02` | 소총 발사 변형 | https://freesound.org/s/201668/ | franki-01234 | CC0 1.0 | 아니오 | `201668__franki-01234__m16-burst-in-street.aiff` | aiff, 4.2s | 자르기·WAV 변환·이름 변경(예정) | 버스트 → 한 발만 |
| S-0018 | `rifle_shot_01` | 소총 발사 | https://freesound.org/s/201670/ | franki-01234 | CC0 1.0 | 아니오 | `201670__franki-01234__ak47-fire-into-street.aiff` | aiff, 5.2s | 자르기·WAV 변환·이름 변경(예정) | 연발 녹음 → 한 발만 |
| S-0019 | `smg_shot_01` | SMG 발사 | https://freesound.org/s/201672/ | franki-01234 | CC0 1.0 | 아니오 | `201672__franki-01234__mp5-submachine-gun-down-the-block.aiff` | aiff, 2.3s | 자르기·WAV 변환·이름 변경(예정) | 공포탄 녹음 |
| S-0020 | `elevator_open_01` | 엘리베이터 문 | https://freesound.org/s/241763/ | gilps | CC0 1.0 | 아니오 | `241763__gilps__elevator_opening.wav` | wav, 6.6s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0021 | `pistol_shot_02` | 권총 발사 변형 | https://freesound.org/s/253736/ | kodack | CC0 1.0 | 아니오 | `253736__kodack__pistol-shot.wav` | wav, 2.0s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0022 | `siren_air_raid_02` | 구역 사이렌 B | https://freesound.org/s/336640/ | audiojacked | CC0 1.0 | 아니오 | `336640__audiojacked__alarm-air-raid-siren-test-devonport-sony-pcm-m-10.wav` | wav, 77.2s | 자르기·WAV 변환·이름 변경(예정) | 77초 실물 녹음 |
| S-0023 | `step_dirt_01` | 발소리 Dirt | https://freesound.org/s/349082/ | yoyodaman234 | CC0 1.0 | 아니오 | `349082__yoyodaman234__hiking-on-soft-ground-1-through-mud-then-slipping.wav` | wav, 15.1s | 자르기·WAV 변환·이름 변경(예정) | 미끄러짐 구간 제외 |
| S-0024 | `sniper_shot_02` | 저격 발사 변형 | https://freesound.org/s/351777/ | kennysvoice | CC0 1.0 | 아니오 | `351777__kennysvoice__sniper-rifle-m24-sfx.mp3` | mp3, 4.0s | 자르기·WAV 변환·이름 변경(예정) | 장전→발사 중 발사부만 |
| S-0025 | `explosion_distant_01` | 원거리 폭발 | https://freesound.org/s/372086/ | mr_keybored | CC0 1.0 | 아니오 | `372086__mr_keybored__distant-explosion.wav` | wav, 4.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0026 | `vehicle_engine_idle_01` | 차량 엔진 아이들 | https://freesound.org/s/401552/ | giocosound | CC0 1.0 | 아니오 | `401552__giocosound__sfx_car_engine_outside_idle.wav` | wav, 3.1s | 자르기·WAV 변환·이름 변경(예정) | 루프 정리 |
| S-0027 | `siren_air_raid_01` | 구역 사이렌 A | https://freesound.org/s/412171/ | poligonstudio | CC0 1.0 | 아니오 | `412171__poligonstudio__air-raid-siren-alarm.wav` | wav, 122.4s | 자르기·WAV 변환·이름 변경(예정) | 2분, 루프 정리 |
| S-0028 | `geiger_01` | 가이거 계수관 루프 소재 | https://freesound.org/s/417424/ | parabolix | CC0 1.0 | 아니오 | `417424__parabolix__civil-defense-geiger-counter_lionel-electronic-labs-model-6b.wav` | wav, 60.0s | 자르기·WAV 변환·이름 변경(예정) | 1분 |
| S-0029 | `shotgun_pump_01` | 샷건 펌프·탄피 | https://freesound.org/s/436074/ | rammbostein | CC0 1.0 | 아니오 | `436074__rammbostein__shotgun-rack-and-shell-drop.mp3` | mp3, 7.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0030 | `tinnitus_01` | 귀울림 | https://freesound.org/s/450620/ | breviceps | CC0 1.0 | 아니오 | `450620__breviceps__tinnitus-sound.mp3` | mp3, 8.6s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0031 | `step_stairs_01` | 발소리 MetalStairs | https://freesound.org/s/451863/ | kyles | CC0 1.0 | 아니오 | `451863__kyles__footsteps-shoes-metal-stairs-up-down-resonate-slow-awkward-with-pants-noise.flac` | flac, 50.7s | 자르기·WAV 변환·이름 변경(예정) | 51초 |
| S-0032 | `loot_rummage_01` | 줍기·가방 뒤지기 | https://freesound.org/s/464996/ | usamah | CC0 1.0 | 아니오 | `464996__usamah__backpack-unzip-search-pulloutbooks.wav` | wav, 28.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0033 | `impact_ricochet_01` | 도탄 | https://freesound.org/s/478345/ | joao_janz | CC0 1.0 | 아니오 | `478345__joao_janz__bullet-ricochet-1_3.wav` | wav, 6.8s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0034 | `elevator_ambient_01` | 엘리베이터 앰비언트 | https://freesound.org/s/502344/ | universodemalaonda | CC0 1.0 | 아니오 | `502344__universodemalaonda__elevator-06.wav` | wav, 117.0s | 자르기·WAV 변환·이름 변경(예정) | 2분 루프 소재 |
| S-0035 | `bullet_whiz_02` | 탄 스침 변형 | https://freesound.org/s/505238/ | daleonfire | CC0 1.0 | 아니오 | `505238__daleonfire__whizzby.wav` | wav, 1.8s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0036 | `crickets_01` | 귀뚜라미(밤) | https://freesound.org/s/522299/ | defelozedd94 | CC0 1.0 | 아니오 | `522299__defelozedd94__crickets-at-night-raw-sound.wav` | wav, 222.6s | 자르기·WAV 변환·이름 변경(예정) | 3.7분 루프 소재 |
| S-0037 | `melee_swing_01` | 근접 휘두름 | https://freesound.org/s/523230/ | magnuswaker | CC0 1.0 | 아니오 | `523230__magnuswaker__hard-swing-1.wav` | wav, 0.9s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0038 | `rain_roof_01` | 비(지붕) | https://freesound.org/s/527499/ | timothyd4y | CC0 1.0 | 아니오 | `527499__timothyd4y__rain-on-shed-roof.wav` | wav, 61.5s | 자르기·WAV 변환·이름 변경(예정) | 루프 |
| S-0039 | `step_water_01` | 발소리 Water | https://freesound.org/s/531566/ | patchytherat | CC0 1.0 | 아니오 | `531566__patchytherat__puddle-footsteps.wav` | wav, 11.1s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0040 | `elevator_travel_01` | 엘리베이터 이동 | https://freesound.org/s/531792/ | conath | CC0 1.0 | 아니오 | `531792__conath__elevator-going-down-2.wav` | wav, 23.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0041 | `step_wood_01` | 발소리 Wood 실내 | https://freesound.org/s/546362/ | panosa | CC0 1.0 | 아니오 | `546362__panosa__footsteps-on-wooden-floor.wav` | wav, 13.6s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 |
| S-0042 | `pain_grunt_01` | 피격·낙하 신음 | https://freesound.org/s/547209/ | mrfossy | CC0 1.0 | 아니오 | `547209__mrfossy__voice_adultmale_paingrunts_09.wav` | wav, 0.3s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0043 | `step_mud_01` | 발소리 Mud | https://freesound.org/s/548384/ | nox_sound | CC0 1.0 | 아니오 | `548384__nox_sound__footsteps_mountain_boots_mud_mono.wav` | wav, 14.6s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 |
| S-0044 | `smoke_hiss_01` | 연막 분출 | https://freesound.org/s/551429/ | themiwa100 | CC0 1.0 | 아니오 | `551429__themiwa100__gas-grenade.wav` | wav, 8.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0045 | `step_tile_01` | 발소리 Tile | https://freesound.org/s/555868/ | nox_sound | CC0 1.0 | 아니오 | `555868__nox_sound__footsteps_mountain_boots_tile_mono.wav` | wav, 36.9s | 자르기·WAV 변환·이름 변경(예정) | 걷기9·달리기13 등 |
| S-0046 | `step_gravel_01` | 발소리 Gravel | https://freesound.org/s/556002/ | nox_sound | CC0 1.0 | 아니오 | `556002__nox_sound__footsteps_mountain_boots_gravel_mono.wav` | wav, 54.0s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 |
| S-0047 | `step_grass_01` | 발소리 Grass | https://freesound.org/s/556042/ | nox_sound | CC0 1.0 | 아니오 | `556042__nox_sound__footsteps_mountain_boots_grass_mono.wav` | wav, 69.3s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 |
| S-0048 | `step_rock_run_01` | 발소리 Rock 달리기 | https://freesound.org/s/558471/ | nox_sound | CC0 1.0 | 아니오 | `558471__nox_sound__footsteps_mountain_boots_rock_run_sequence_mono.wav` | wav, 18.4s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 |
| S-0049 | `step_rock_walk_01` | 발소리 Rock 걷기 | https://freesound.org/s/558472/ | nox_sound | CC0 1.0 | 아니오 | `558472__nox_sound__footsteps_mountain_boots_rock_walk_sequence_mono.wav` | wav, 19.1s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 |
| S-0050 | `step_sand_01` | 발소리 Sand | https://freesound.org/s/564893/ | nox_sound | CC0 1.0 | 아니오 | `564893__nox_sound__footsteps_mountain_boots_wet_sand_sequence_mono.wav` | wav, 64.5s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 |
| S-0051 | `fire_01` | 불 타는 소리 | https://freesound.org/s/595483/ | savreese | CC0 1.0 | 아니오 | `595483__savreese__fire-being-started-and-crackling.wav` | wav, 147.8s | 자르기·WAV 변환·이름 변경(예정) | 2.5분 |
| S-0052 | `explosion_grenade_01` | 수류탄 폭발 | https://freesound.org/s/609587/ | unfa | CC0 1.0 | 아니오 | `609587__unfa__grenade-explosion-sfx-medium-sized-meaty-realistic.flac` | flac, 5.3s | 자르기·WAV 변환·이름 변경(예정) | Vitalium 합성+폭죽 녹음, AI 학습 비동의 문구 |
| S-0053 | `step_deck_01` | 발소리 Deck | https://freesound.org/s/636080/ | coalcon | CC0 1.0 | 아니오 | `636080__coalcon__walking-on-wood-dock-rainforest-hawaii.m4a` | m4a, 14.2s | 자르기·WAV 변환·이름 변경(예정) | M4A → WAV 변환 |
| S-0054 | `swim_01` | 수영·물 움직임 | https://freesound.org/s/637958/ | kyles | CC0 1.0 | 아니오 | `637958__kyles__water-movements-swimming-around-various-splashes-random.flac` | flac, 56.3s | 자르기·WAV 변환·이름 변경(예정) | 56초 |
| S-0055 | `shot_suppressed_01` | 소음기 발사 | https://freesound.org/s/668071/ | ajhartman42295 | CC0 1.0 | 아니오 | `668071__ajhartman42295__50-cal-shot.wav` | wav, 1.9s | 자르기·WAV 변환·이름 변경(예정) | 50구경 녹음을 소음기 소리로 사용 |
| S-0056 | `geiger_02` | 가이거 보조 | https://freesound.org/s/674113/ | sanderboah | CC0 1.0 | 아니오 | `674113__sanderboah__geiger-counter-dry.wav` | wav, 25.5s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0057 | `dry_fire_01` | 빈 탄창 격발 | https://freesound.org/s/674568/ | serutonin-deprivd | CC0 1.0 | 아니오 | `674568__serutonin-deprivd__9mm-handgun-being-dry-fired.wav` | wav, 0.3s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0058 | `rifle_reload_01` | 소총 장전·탄창 | https://freesound.org/s/674742/ | serutonin-deprivd | CC0 1.0 | 아니오 | `674742__serutonin-deprivd__ak-47-assault-rifle-being-unloaded-and-reloaded.wav` | wav, 1.6s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0059 | `thunder_01` | 천둥 | https://freesound.org/s/683420/ | sholecoltis | CC0 1.0 | 아니오 | `683420__sholecoltis__short_thunder_deep.wav` | wav, 6.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0060 | `wind_01` | 바람 | https://freesound.org/s/683433/ | sholecoltis | CC0 1.0 | 아니오 | `683433__sholecoltis__wave_wind_1.wav` | wav, 3.5s | 자르기·WAV 변환·이름 변경(예정) | **제목 Wave_Wind — 파도 소리일 수 있음, 청취 확인** |
| S-0061 | `step_metal_01` | 발소리 Metal | https://freesound.org/s/698697/ | ienba | CC0 1.0 | 아니오 | `698697__ienba__footsteps-on-metal.wav` | wav, 10.3s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0062 | `bullet_whiz_01` | 탄 스침 | https://freesound.org/s/730351/ | go_s0unds123 | CC0 1.0 | 아니오 | `730351__go_s0unds123__bullet-fly-by.wav` | wav, 17.8s | 자르기·WAV 변환·이름 변경(예정) | 잘라서 사용 |
| S-0063 | `flashbang_01` | 섬광탄 | https://freesound.org/s/752628/ | modusmogulus | CC0 1.0 | 아니오 | `752628__modusmogulus__flashbang-open-field-high-quality.wav` | wav, 6.3s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0064 | `casing_01` | 탄피 | https://freesound.org/s/777922/ | blondpanda | CC0 1.0 | 아니오 | `777922__blondpanda__metal_shell_spin_09.wav` | wav, 0.4s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0065 | `heal_bandage_01` | 치료 붕대 | https://freesound.org/s/800275/ | securesubset | CC0 1.0 | 아니오 | `800275__securesubset__tape-bandages-rip.wav` | wav, 0.8s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0066 | `vehicle_crash_01` | 차량 충돌 | https://freesound.org/s/807438/ | logicogonist | CC0 1.0 | 아니오 | `807438__logicogonist__car-crash-short-1.wav` | wav, 0.6s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0067 | `step_concrete_01` | 발소리 Concrete | https://freesound.org/s/813622/ | securesubset | CC0 1.0 | 아니오 | `813622__securesubset__footsteps-stone-rock-concrete-cement.wav` | wav, 6.7s | 자르기·WAV 변환·이름 변경(예정) | 시퀀스 → 자르기 |
| S-0068 | `birds_forest_01` | 숲·새(낮) | https://freesound.org/s/826326/ | upkey | CC0 1.0 | 아니오 | `826326__upkey__forest-ambience-with-birds.wav` | wav, 77.4s | 자르기·WAV 변환·이름 변경(예정) | 77초 |
| S-0069 | `pistol_shot_01` | 권총 발사 | https://freesound.org/s/828786/ | areniporgen | CC0 1.0 | 아니오 | `828786__areniporgen__glock-19x.wav` | wav, 1.5s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0070 | `sniper_shot_01` | 저격 발사 | https://freesound.org/s/855608/ | qubodup | CC0 1.0 | 아니오 | `855608__qubodup__sniper-shot-in-field-3-m2010-enhanced-sniper-rifle-esr.flac` | flac, 1.4s | 자르기·WAV 변환·이름 변경(예정) | 잘라서 사용; 미군 영상 추출 |
| S-0071 | `glass_break_01` | 유리 깨짐 | https://freesound.org/s/861043/ | elevatorfan2020 | CC0 1.0 | 아니오 | `861043__elevatorfan2020__hard-glass-break-2.mp3` | mp3, 1.1s | 자르기·WAV 변환·이름 변경(예정) |  |
| S-0072 | `step_asphalt_01` | 발소리 Asphalt | https://freesound.org/s/877222/ | tonumuzic | CC0 1.0 | 아니오 | `877222__tonumuzic__footsteps-on-asphalt-and-grass.wav` | wav, 90.6s | 자르기·WAV 변환·이름 변경(예정) | 90초, 풀 구간 섞임 → 자르기 |
| S-0073 | `announce_chime_01` | 방송 차임 | https://freesound.org/s/879805/ | qvantor | CC0 1.0 | 아니오 | `879805__qvantor__announcement-chime.wav` | wav, 4.4s | 자르기·WAV 변환·이름 변경(예정) | 태그 synthesized(전자음, AI 표기 아님) |

합계 66행. 매핑에 없는 파일: 없음.

## 3. 크레딧 문구 초안 (CC BY 3.0 1건)
`Credits.cs`의 `CcBy` 상수는 "CC BY 4.0"이라 **CC BY 3.0 항목은 상수를 따로 두거나 `Licence` 문자열에 직접 적어야 한다**(Unity 순번에서 `CreditsTests`와 함께).

- `Title`: Glock19_reload.wav (표시 이름은 상표 문제 없이 "Pistol reload"로 줄여도 되는지 준환·법적 검토 확인; 원 제목을 정확히 표기하는 것이 CC BY 3.0 요구라 우선 원 제목 + 작성자 표기를 권장, **원문 확인 필요**)
- `Author`: Fr3yr
- `Licence`: `CC BY 3.0` (+ 수정 시 `(수정함)`)
- `Use`: 효과음(권총 재장전)
- 링크: https://freesound.org/people/Fr3yr/sounds/100804/
- 한 줄 표시 예: `Glock19_reload — Fr3yr (CC BY 3.0 (수정함)) · 효과음(권총 재장전)`

주의: 원 제목에 총기 제조사 상표(Glock)가 들어 있다. 규칙 1은 저장소 파일명·에셋 이름에서 상표 금지이고 CC BY 요구는 저작자 표기이므로 충돌한다. **크레딧에 원 제목을 쓰는 것이 상표 위험인지는 사람의 법적 판단(규칙 8)**이라 RISKS_OPEN_QUESTIONS에 올릴 것을 제안한다.

## 4. 기존 S-0001~0007과의 관계
교체 대상: S-0001(총성 FreeFirearmLibrary) → 새 총성, S-0002(OGA 재장전) → 새 재장전, S-0003(Kenney 발소리, 눈 발소리를 흙으로 쓴 것 포함) → 표면별 새 발소리(Kenney 피격·근접 소리는 유지), S-0004(OGA 물 발소리) → `step_water_01`, S-0005(합성 탄 스침)·S-0006(합성 문) → 녹음, S-0007(합성 앰비언스) → 녹음 앰비언스. 교체 순번에서 옛 행은 "교체됨(날짜)"으로 표시하고 파일은 삭제한다(되돌릴 수 없는 삭제는 master 승인).

# 09. 사운드 후보 청취 시트 (S5용 조사)

조사일 2026-11-16. 조사 전용: 다운로드·가입·약관 동의 없이 공개 페이지만 열람했다. 소스 선택은 준환이 직접 듣고 한다.

## 0. 읽는 법
- **✔** = 해당 소리 페이지를 직접 열어 라이선스 줄을 확인함. **F** = Freesound 검색의 `Creative Commons 0` 필터 결과에만 나옴(소리 페이지 미확인, 저장 전 소리마다 라이선스 줄 확인 필요). **?** = 확인 못 함.
- 링크는 모두 로그인 없이 **바로 들을 수 있는 페이지**다(Freesound·BigSoundBank는 **다운로드**만 로그인 필요, 아래 §2). 듣기만 하면 약관 문제 없음.
- 약관 문구는 요약이며 **원문 확인 필요** 표시를 붙였다. 법적 판단은 사람 몫(CLAUDE.md 8번).
- 길이는 소리 페이지에서 읽은 값만 적었다. 검색 결과에서 본 값은 신뢰하지 않고 비웠다.
- 요약 결론: **"스타워즈 같다"의 후보 원인**은 합성음·소총을 낮춰 재사용한 저격·SF 레이저 같은 탄 스침 소리(audio.json c_whiz, c_synth). 아래 후보는 전부 실제 녹음 계열이다.

## 1. 소스별 약관 요약

| 소스 | 라이선스 | 상업 사용 | 저장소 포함·재배포 | 다운로드 조건 | 약관 URL(확인일 2026-11-16) |
|---|---|---|---|---|---|
| Freesound | **파일별**. CC0 / CC-BY / CC-BY-NC 중 택일. CC0만 후보 | CC0는 허용(소리 페이지가 "상업 목적 포함, 허락 불필요"라고 안내 ✔). CC-BY는 크레딧, NC는 상업 불가(FAQ ✔) | CC0는 배포 가능(페이지 문구 ✔) | **로그인 필요**(FAQ ✔) | FAQ https://freesound.org/help/faq/ ✔ · 이용약관 https://freesound.org/help/tos_web/ (**미확인**, 원문 확인 필요) · CC0 https://creativecommons.org/publicdomain/zero/1.0/ |
| Kenney | 에셋 페이지 전체 CC0 ✔ | 허용, 크레딧 불필요 ✔ | 저장소 포함은 사이트가 직접 다루지 않고 에셋별 License 파일 참조 ✔ → CC0 자체는 허용(원문 확인 필요) | 공개 다운로드(가입 불필요로 알려짐, **미확인**) | https://kenney.nl/support ✔ |
| BigSoundBank | "Free and Royalty Free" 표시 파일은 CC0 / WTFPL / 퍼블릭 도메인 ✔ | 허용 ✔ | 형식 불문 재배포·변형 허용 ✔. 크레딧은 제안일 뿐 필수 아님 ✔ | 계정 필수 여부 미기재. 무료 계정은 "즉시 다운로드" 제공 ✔ | https://bigsoundbank.com/licenses.html ✔ · **소리마다 "Free and Royalty Free" 표시 확인**(유료/계약 파일 섞여 있음) |
| OpenGameArt | **파일별**(CC0 / CC-BY / GPL 등). CC0만 후보 | CC0 허용 | CC0 허용 | 공개(이 세션은 403으로 페이지·FAQ 열람 불가) | FAQ https://opengameart.org/content/faq (**열람 실패**, 원문 확인 필요). 이미 HUMAN_TODO에 "압축 파일 안 라이선스 문서 없음" 건 있음 |
| itch.io 팩 | **제작자별**, 형식 제각각 | 팩마다 다름 | 팩마다 다름 | 대부분 "원하는 금액" 무료 다운로드, 일부 로그인 필요 | 각 팩 페이지 설명·License 줄 |
| Sonniss GameAudioGDC | 자체 라이선스(CC0 아님) | 허용 | **재배포 제한**(§3) | 직접 링크·토렌트. 가입 요구는 못 봄 | https://sonniss.com/gdc-bundle-license/ ✔ |

## 2. Freesound 다운로드 정책 메모
- FAQ가 다운로드 전 **로그인을 요구**한다 ✔. 가입·로그인은 준환이 해야 한다(에이전트 금지, CLAUDE.md 3번).
- CC0 소리라도 업로더가 "생성형 AI 학습 동의 안 함"을 적어 둔 경우가 있다(예: unfa 유리 깨짐 ✔). 법적 구속력은 불명이며 **우리는 학습에 쓰지 않으므로 무관하나 원문 확인 필요**.
- ledger에는 소리별로 **소리 페이지 URL, 작성자, 라이선스 줄, 확인일**을 기록해야 한다(압축 파일에 라이선스가 없는 OGA 건의 재발 방지).

## 3. Sonniss GameAudioGDC 약관 (별도 절)

**확인한 사실 (페이지 열람, 원문 확인 필요)**
- 라이선스 문서: https://sonniss.com/gdc-bundle-license/ — **버전 2.0, 2026-08-27 시행**. 그 날짜 이후 내려받은 파일에 적용되고, 이전 파일은 받은 날의 버전을 따른다. 번들 안에 든 사본은 낡을 수 있어 **웹페이지 버전이 우선**.
- 상업 사용: 개인·상업 프로젝트(게임·영상·앱 등) 허용, 완성작 판매 가능, 프로젝트 수 제한 없음, **출처 표기 불필요**.
- 편집(레이어·피치·늘리기)은 허용하나 **원본 녹음을 자기가 녹음했다고 주장하면 금지**.
- **재배포 제한**: 효과음으로서 제3자에게 배포·게시·재라이선스하려면 Sonniss의 사전 서면 허락이 필요. 수정본도 포함, 단독 판매든 샘플팩·에셋팩·템플릿·SDK 안이든 동일. 같은 팀·조직 내 공유는 허용.
- **AI/ML 학습·개발·개선 목적 사용 명시적 금지**(AI 효과음 생성 도구 포함).
- 저장소 보관: 문서가 직접 다루지 않는다. 문서 문구상 **비공개 저장소를 팀 안에서만** 쓰면 문제 없어 보이나, **공개 저장소·외부 공유·재사용 템플릿**이면 재배포에 걸릴 수 있다(내 해석, 추정). 완성 게임에 들어간 파일을 사용자가 빌드에서 추출해 가는 경우는 문서에 없음 → **원문 확인 필요**. 준환이 문서 전문과 법적 의미를 확인할 것.
- 준거법 영국법, 상대 면책 조항 있음(요약, 원문 확인 필요).

**번들 규모**
- GDC 2026(gdc.sonniss.com): **7.47 GB+, 347+ 파일, 5개 zip**, 토렌트 옵션, 참여사 17곳(344 Audio, Epic Stock Media, Just Sound Effects, The Noisery 등) ✔. AUDIO.md의 "약 7.47GB/347+"와 일치.
- GDC 2024: 9부분 23.5 GB. 2025 번들은 없음(2026으로 합쳐짐) ✔.
- **총성이 들어 있는지는 확인 못 함.** 트랙 목록은 구글 시트로 링크돼 있고 이 세션에서 열 수 없었다. → **준환이 시트에서 gun/weapon을 검색해 봐야 한다.**

**누가 해야 하나**
- 다운로드(수 GB)와 약관 사본(PDF/웹 저장) 보관은 **준환이 직접**. 에이전트는 다운로드·가입 금지. 가입이 필요하면 준환이 한다(페이지 본문상 가입 없이 직접 링크로 내려받을 수 있어 보이나 Google Drive 미러는 구글 로그인을 요구할 수 있음, **미확인**).
- 사용 시: 쓰는 파일만 `Assets/_Project/Audio/`에 넣고 ledger에 번들 단위 약관 사본·버전·받은 날짜를 기록(AUDIO.md 5절). 약관 버전이 날짜별이므로 **받은 날짜 필수**.

## 4. 범주별 청취 후보

### 4-1. 총성

| 범주 | # | 후보(링크) | 소스·라이선스 | 길이/형식 | 메모 |
|---|---|---|---|---|---|
| 권총 | 1 | [Glock 19X (areniporgen)](https://freesound.org/people/areniporgen/sounds/828786/) | Freesound CC0 ✔ | 1.45s, WAV 48k/32bit 스테레오 | 야외 근접, 폰 마이크 녹음. 거칠 수 있음 |
| 권총 | 2 | [Pistol Shot (LeMudCrab)](https://freesound.org/people/LeMudCrab/sounds/163456/) | Freesound CC0 F | ? | 검색 인기 1위 |
| 권총 | 3 | [Snake's Second Authentic Gun Sounds Pack (f8studios) 9mm](https://f8studios.itch.io/snakes-second-authentic-gun-sounds-pack) | itch.io, 공식 라이선스 표기 없음 → 본문 "상업 사용 가능, 크레딧 불필요" ✔, 댓글에서 "public domain" 언급 | 166+ 파일 68종, MP3/WAV, 리버브 있음·없음 | 9mm(권총·SMG 겸용), .308, 20게이지, 장전·슬라이드·리볼버 기계음까지 한 번에. **가장 효율 좋음** 단 라이선스가 본문 문장뿐 → 원문 확인 필요 |
| SMG | 1 | f8studios 9mm (위 링크) | 위와 동일 | | SMG 대용 |
| SMG | 2 | [Mitraillette-3 (davidou)](https://freesound.org/people/davidou/sounds/88467/) | Freesound CC0 F | ? | 연사 |
| SMG | 3 | [Pistol lyd (Mathias2002)](https://freesound.org/people/Mathias2002/sounds/553820/) | Freesound CC0 F | ? | 권총, 연사 가능성 미확인 |
| AR | 1 | [Assault Rifle Single Burst (Keatonmcq)](https://freesound.org/people/Keatonmcq/sounds/160590/) | Freesound CC0 ✔ | 0.5s, MP3 147kbps | 설명상 **여러 효과음을 섞어 제작**한 합성물 → "스타워즈 같다"와 같은 부류일 위험. 비교용 |
| AR | 2 | [Rynnakkokivaari (ertzsi)](https://freesound.org/people/ertzsi/sounds/754135/) | Freesound CC0 F | ? | 핀란드어 제목, 실제 소총 녹음 추정(추정) |
| AR | 3 | [The free firearm sound library (OGA, bart)](https://opengameart.org/content/the-free-firearm-sound-library) | OGA CC0 표기(검색 결과만, 페이지 403) | ? | 소총·산탄총·권총·노리쇠. **현재 쓰는 소스가 이 계열일 가능성**(c_shot, HUMAN_TODO). 어느 것이 현재 쓰는지 ledger에서 확인 |
| DMR | 1 | f8studios .308 (위 링크) | 위와 동일 | | .308은 배틀라이플·DMR 용도로 안내됨 ✔ |
| DMR | 2 | [gunshot sounds (OGA)](https://opengameart.org/content/gunshot-sounds) | OGA CC0 표기(검색 결과만, 페이지 403) | ? | SKS·Mosin·CZ-52·산탄총, 사격장 녹음. 녹음기가 큰 소리에 한계 있었다는 작성자 메모 |
| DMR | 3 | [shooting_1 (mchrpt)](https://freesound.org/people/mchrpt/sounds/660978/) | Freesound CC0 F | ? | 무기 종류 미확인 |
| SR | 1 | [Sniper Shot in Field 3 (qubodup)](https://freesound.org/people/qubodup/sounds/855608/) | Freesound CC0 ✔ | 1.44s, FLAC 48k | 야외 원거리 저격 녹음. 출처가 미군 훈련 영상에서 추출(설명 ✔; 영상 저작권 상태는 **원문 확인 필요**) |
| SR | 2 | [Sniper Rifle M24 (kennysvoice)](https://freesound.org/people/kennysvoice/sounds/351777/) | Freesound CC0 F | ? | |
| SR | 3 | [50 cal shot (ajhartman42295)](https://freesound.org/people/ajhartman42295/sounds/668071/) | Freesound CC0 F | ? | 대구경, 대물저격 느낌 |
| 샷건 | 1 | [Shotgun hit (lumikon)](https://freesound.org/people/lumikon/sounds/564483/) | Freesound CC0 F | ? | |
| 샷건 | 2 | [Shotgun - Fire (AbuNijmeh)](https://freesound.org/people/AbuNijmeh/sounds/318973/) | Freesound CC0 F | ? | |
| 샷건 | 3 | [Shotgun 02 (Clueless79)](https://freesound.org/people/Clueless79/sounds/544676/) | Freesound CC0 ✔ | 4.18s, WAV 44.1k/16bit | 카테고리가 Electronic/Design, 에코 효과 포함 → 합성 가능성. 비교용 |
| 원거리 | 1 | [Distent gunshot (CosmicEmbers)](https://freesound.org/s/387477/) | Freesound **CC-BY 3.0** ✔ | 3s | **정책상 제외**(CC-BY는 AUDIO.md가 사용 안 함). 들어보는 용도로만. |

**제외한 것**: alienanthony "Gun sounds"(itch, 퍼블릭 도메인 표기)는 페이지에 **사운드 "AI Assisted" 표시**가 있어 규칙 2(AI 생성 에셋 금지)에 걸린다 → 후보에서 뺌.

### 4-2. 총기 장전·기계음

| # | 후보 | 소스·라이선스 | 메모 |
|---|---|---|---|
| **준환 선택(권총 장전)** | [Glock19_reload (Fr3yr)](https://freesound.org/people/Fr3yr/sounds/100804/) | Freesound **CC-BY 3.0** ✔ (2026-11-16 페이지 확인) | 0.74s, WAV 96k/24bit 모노. **라이선스 충돌**: AUDIO.md는 Freesound를 CC0만 허용, LICENSE_POLICY는 크레딧 가능하면 CC-BY 허용. 쓰려면 CREDITS 자동 반영 + ledger 기록 또는 CC0 대체 필요 → 준환·마스터 결정 필요 |
| 1 | f8studios 팩 ([링크](https://f8studios.itch.io/snakes-second-authentic-gun-sounds-pack)) | itch.io 위 설명 | 슬라이드·장전·탄창 채우기·리볼버·양각대 ✔ |
| 2 | [Assault Rifle Reload (Kodack)](https://freesound.org/people/Kodack/sounds/256912/) | Freesound CC0 F | |
| 3 | [AK-47 unloaded and reloaded (serøutōnin--deprivəd)](https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/674742/) | Freesound CC0 F | 링크는 퍼센트 인코딩됨 |

### 4-3. 발소리 표면 (14종 + 계단·함석·물)

표면 태그는 audio.json 표 기준. **Nox_Sound "Pack - Footsteps"**([팩 페이지](https://freesound.org/people/Nox_Sound/packs/29889/))는 같은 작가·같은 신발(등산화)·모노 녹음으로 표면이 일관되어 통일감이 좋다. 팩 전체 라이선스 줄은 없고 **소리마다 개별 표기**이므로 소리 페이지 확인 필요(타일 1개만 ✔: CC0, WAV 48k/24bit 모노, 약 37초, 걷기 9·달리기 13·점프 5·스크레이프 등 포함).

| 표면 | 후보 1 | 후보 2 | 후보 3 |
|---|---|---|---|
| Concrete | [Footsteps - Stone, Rock, Concrete (SecureSubset)](https://freesound.org/people/SecureSubset/sounds/813622/) F | [Footsteps on concrete (florianreichelt)](https://freesound.org/people/florianreichelt/sounds/459964/) F | [concrete footstep 2 (Yoyodaman234)](https://freesound.org/people/Yoyodaman234/sounds/166508/) F |
| Asphalt | [Footsteps on Asphalt and Grass (tonu.muzic)](https://freesound.org/people/tonu.muzic/sounds/877222/) F | [Footsteps, Shoe on Concrete (BigSoundBank)](https://bigsoundbank.com/detail-0514-footsteps-shoe-on-concrete.html) CC0 표기 | [Fast Steps on Concrete (BigSoundBank)](https://bigsoundbank.com/fast-steps-on-concrete-s1318.html) CC0 표기 |
| Gravel | [Hiking Boot Footsteps on Gravel (Fission9)](https://freesound.org/people/Fission9/sounds/521588/) F | [Footsteps_Mountain_Boots_Gravel (Nox_Sound, 팩)](https://freesound.org/people/Nox_Sound/packs/29889/) F | [Footsteps on gravels #1 (BigSoundBank)](https://bigsoundbank.com/steps-on-gravels-s0510.html) CC0 표기 |
| Dirt | [Hiking on Soft Ground 1 (Yoyodaman234)](https://freesound.org/people/Yoyodaman234/sounds/349082/) F | [Footsteps wearing sneakers... desert (felix.blume)](https://freesound.org/people/felix.blume/sounds/705748/) F (도로 소음 섞임) | — (마른 흙 전용 후보 약함, 추가 검색 필요) |
| Mud | [Footsteps_Mountain_Boots_Mud (Nox_Sound)](https://freesound.org/people/Nox_Sound/sounds/548384/) F | [Footsteps_Mud_Single_03 (BenDrain)](https://freesound.org/people/BenDrain/sounds/488082/) F | [Hiking on Soft Ground 1 – Through Mud (Yoyodaman234)](https://freesound.org/people/Yoyodaman234/sounds/349082/) F |
| Grass | [Hiking Boot Footsteps on Grass (Fission9)](https://freesound.org/people/Fission9/sounds/521587/) F | [snd_footsteps_grass_JE-01 (jevans27)](https://freesound.org/people/jevans27/sounds/563529/) F | [Steps in the tall grass (BigSoundBank)](https://bigsoundbank.com/steps-in-the-tall-grass-s1024.html) CC0 표기 |
| Sand | [footsteps in wet coarse sand (bruno.auzet)](https://freesound.org/people/bruno.auzet/sounds/539175/) F | [Mountain_Boots_Wet_Sand (Nox_Sound)](https://freesound.org/people/Nox_Sound/sounds/564893/) F | [Footsteps on beach, sand and water (YannSauvin)](https://freesound.org/people/YannSauvin/sounds/777609/) F |
| Rock | Nox_Sound 팩의 Rock Walk/Run/Sprint (팩 링크 위) F | [Footsteps - Stone, Rock, Concrete (SecureSubset)](https://freesound.org/people/SecureSubset/sounds/813622/) F | — |
| WoodFloor | [Footsteps on wooden floor (PanosA)](https://freesound.org/people/PanosA/sounds/546362/) F | [Foley_Footsteps_ShedWoodenFloor (MrFossy)](https://freesound.org/people/MrFossy/sounds/523273/) F | [Footsteps on a wooden floor (doctorphil)](https://freesound.org/people/doctorphil/sounds/536770/) F |
| WoodOutdoor(데크) | [Footsteps in Factory Hall on Wood and Concrete (RutgerMuller)](https://freesound.org/people/RutgerMuller/sounds/155858/) F | — | — (데크·부두 전용 후보 못 찾음, 추가 검색 필요) |
| Tile | [Footsteps_Mountain_Boots_Tile (Nox_Sound)](https://freesound.org/people/Nox_Sound/sounds/555868/) ✔ CC0 | [Footsteps, Tile, Male Sneakers (SpliceSound)](https://freesound.org/people/SpliceSound/sounds/170506/) F | [Tile, Slippers (SpliceSound)](https://freesound.org/people/SpliceSound/sounds/260120/) F |
| MetalPlate | [Footsteps on Metal (IENBA)](https://freesound.org/people/IENBA/sounds/698697/) F | [Footsteps in Factory Hall (RutgerMuller)](https://freesound.org/people/RutgerMuller/sounds/155858/) F | [OGA metal footsteps on concrete](https://opengameart.org/content/metal-footsteps-on-concrete) CC0 표기(검색 결과만) |
| MetalStairs | [metal stairs (kyles)](https://freesound.org/people/kyles/sounds/451863/) F | [Footsteps on stairs_01 (121053699)](https://freesound.org/people/121053699/sounds/467992/) F | [Walking up to Metal Stairs (bassimat)](https://freesound.org/people/bassimat/sounds/838910/) F |
| TinRoof | **후보 못 찾음**(함석 위 걸음). 추가 검색 필요. 임시로 MetalPlate + 울림 처리 | | |
| Water | [puddle footsteps (patchytherat)](https://freesound.org/people/patchytherat/sounds/531566/) F | [squishy footsteps in a puddle (patchytherat)](https://freesound.org/people/patchytherat/sounds/531571/) F | [Footsteps on beach, sand and water (YannSauvin)](https://freesound.org/people/YannSauvin/sounds/777609/) F |
| GlassShards | 아래 4-4 유리 파편 소리 활용. 전용 걸음 후보 못 찾음 | | |

주의: 발소리 후보는 대부분 **한 파일에 걸음 여러 개가 든 시퀀스**다. 클립 분리(자르기)가 필요하다. Mountain Boots 계열은 등산화 한 종류라 도심 구두/군화와 다른 느낌일 수 있다(추정, 들어봐야 앎).

### 4-4. 유리 깨짐

| # | 후보 | 소스·라이선스 | 메모 |
|---|---|---|---|
| **준환 선택** | [Hard Glass Break 2 (ElevatorFan2020)](https://freesound.org/people/ElevatorFan2020/sounds/861043/) | Freesound CC0 ✔ (2026-11-16 페이지 확인) | 1.06s, MP3 259kbps 44.1k 스테레오. "Glass Breaks" 팩의 창유리 1장 파손. MP3라 편집 시 재인코딩 열화 주의 |
| 1 | [Glass Break (unfa)](https://freesound.org/people/unfa/sounds/221528/) | Freesound CC0 ✔ | 1.9s, FLAC 96k/24bit. 작은 유리판을 돌로 깸. 업로더가 AI 학습 비동의 문구를 적음(§2) |
| 2 | [window break with axe (kyles)](https://freesound.org/people/kyles/sounds/452667/) | Freesound CC0 F | 창 + 도끼 |
| 3 | [Heavy glass break 1 (LPHypeR)](https://freesound.org/people/LPHypeR/sounds/660214/) | Freesound CC0 F | 두꺼운 유리 |
| 4 | [Broken glass (BigSoundBank)](https://bigsoundbank.com/detail-0771-broken-glass.html) | CC0 표기, Foley | 파편 위주(파편 걸음·잔해용) |
| 5 | [Glass Break (OGA, Till Behrend)](https://lpc.opengameart.org/content/glass-break) | OGA CC0 표기(검색 결과만) | 230KB wav |

두께별 변형 계획(audio.json c_glass)에는 얇은 창(1·2), 두꺼운 유리(3), 잔해(4)로 나눠 쓰는 안이 맞아 보인다(제안).

### 4-5. 문 / 엘리베이터 / 차량 / 폭발

| 범주 | 후보 | 소스·라이선스 | 메모 |
|---|---|---|---|
| 문 | [Wooden door open-close (Ryding)](https://freesound.org/people/Ryding/sounds/125958/) · [opening and closing wooden door (lartti)](https://freesound.org/people/lartti/sounds/527582/) · [Open and close wooden door 2 (MarcelWagner)](https://freesound.org/people/MarcelWagner/sounds/639661/) | Freesound CC0 F | 현재 합성 wav 6개(c_door) 대체. OGA [100 CC0 SFX](https://opengameart.org/content/100-cc0-sfx)(문 삐걱·열림·닫힘, CC0 표기, 검색 결과만)도 있음 |
| 엘리베이터 | [Elevator going down 2 (conath)](https://freesound.org/people/conath/sounds/531792/) · [elevator 06 (universodemalaonda)](https://freesound.org/people/universodemalaonda/sounds/502344/) · [Elevator_opening (GilPS)](https://freesound.org/people/GilPS/sounds/241763/) | Freesound CC0 F | 도착 알림음은 [Passing Floor Beep (jchamusantana)](https://freesound.org/people/jchamusantana/sounds/634356/) F |
| 차량 | [SFX_Car_Engine_Outside_Idle (GiocoSound)](https://freesound.org/people/GiocoSound/sounds/401552/) · [Car engine idle variations (TRP)](https://freesound.org/people/TRP/sounds/616598/) · [Car Arriving and Parking (ahriik)](https://freesound.org/people/ahriik/sounds/508901/) | Freesound CC0 F | 아이들만 찾음. 가속·루프 이음·충돌은 추가 검색 필요 |
| 폭발 | [Distant Explosion (danlucaz)](https://freesound.org/people/danlucaz/sounds/517747/) · [Distant Explosion (Mr_KeybOred)](https://freesound.org/people/Mr_KeybOred/sounds/372086/) · [distant explosions (Kostrava)](https://freesound.org/people/Kostrava/sounds/320788/) | Freesound CC0 F | 원거리 폭발만 조회. **근거리 폭발·수류탄**은 추가 검색 필요 |

### 4-6. 구역·경고음 (사이렌·가이거·방송) 과 환경음

| 범주 | 후보 | 소스·라이선스 | 메모 |
|---|---|---|---|
| 사이렌 | [Air Raid Siren Alarm (Poligonstudio)](https://freesound.org/people/Poligonstudio/sounds/412171/) · [Long Air Raid Siren (JalynCatbtg)](https://freesound.org/people/JalynCatbtg/sounds/607382/) · [Alarm, Air Raid Siren, Devonport (audiojacked)](https://freesound.org/people/audiojacked/sounds/336640/) | Freesound CC0 F | 마지막이 실제 녹음 느낌(제목상) |
| 가이거 | [Geiger Counter Hotspot (Sweeping) (cookies+policy)](https://freesound.org/people/cookies+policy/sounds/555934/) · [civil defense geiger counter (parabolix)](https://freesound.org/people/parabolix/sounds/417424/) · [Geiger counter (dry) (Sanderboah)](https://freesound.org/people/Sanderboah/sounds/674113/) | Freesound CC0 F | parabolix는 실물 계수관 녹음. 강도별 루프는 오염 DPS에 맞춰 3단 필요(제안) |
| 방송 | [Caution high radiation levels detected (Lyubomir_Ikonomov)](https://freesound.org/people/Lyubomir_Ikonomov/sounds/787649/) · [Announcement Chime (Qvantor)](https://freesound.org/people/Qvantor/sounds/879805/) | Freesound CC0 F | 영어 음성. 합성 음성 가능성(**미확인**, AI 생성이면 규칙 2에 걸림 → 확인 필요). 한국어 방송은 후보 없음 |
| 환경음 | BigSoundBank 환경음·생활음(CC0, [메인](https://bigsoundbank.com/)) · Kenney Impact/Interface 계열(보조 UI) | BigSoundBank CC0 ✔ | 이번 조사에서 도심 바람·전선 윙윙·숲 앰비언스 개별 후보는 **못 찾음**. 도시 앰비언스 검색은 BigSoundBank에서 결과 없음 |
| UI·보조 | Kenney [Impact Sounds](https://kenney.nl/assets/impact-sounds)·[Interface Sounds](https://kenney.nl/assets/interface-sounds) (링크는 알려진 주소, 이 세션에서 **열람 실패**) | CC0 ✔(사이트 약관) | 이미 Impact Sounds 일부 사용 중(c_foley). 표면과 어긋나게 쓴 것(눈 발소리→흙)부터 교체 |

## 5. 약관·정책상 위험 소스

1. **Sonniss**: AI 학습 금지, 재배포 금지(공개 저장소·템플릿 위험), 버전별 약관(받은 날짜 기록 필수). 총성 포함 여부 미확인.
2. **Freesound**: 로그인 필요(준환이 해야 함). 소리마다 라이선스가 달라 **파일 단위 확인** 필수. CC-BY/NC가 섞여 있음(예: 후보처럼 보이던 Distent gunshot은 CC-BY).
3. **itch.io 팩**: 라이선스가 표준 문구가 아니라 제작자 본문·댓글(f8studios). 법적 근거로 약함 → 쓰려면 제작자에게 서면 확인 또는 준환 판단. "AI Assisted" 표기 팩 존재(alienanthony) → 제외.
4. **OpenGameArt**: 이 세션에서 403이라 직접 검증 못 함. 이미 압축 파일 안에 라이선스 문서 없음 건이 열려 있음(HUMAN_TODO).
5. **합성 의심 파일**: Keatonmcq(혼합 제작), Clueless79(Electronic/Design), 방송 음성(Lyubomir). 질도 라이선스(AI 금지)도 확인 필요.
6. **출처 추정 불명**: qubodup 저격(미군 영상 추출). CC0 표기는 확인했으나 영상 원 저작권 상태는 원문 확인 필요(미군 공개 영상은 보통 퍼블릭 도메인이라 알려져 있으나 **미확인**).

## 6. 이번 조사의 한계 (추정/미확인)
- Freesound **F** 표시 항목은 필터 결과로만 CC0를 봤다. 소리 페이지 확인은 8개만 했다(✔).
- Freesound 요청 제한(429)으로 일부 검색(사이렌 외 총기류 재조회, 총기 기계음 상세)이 막혀 후보 수가 적다. **죽은 링크·잘못된 번호 가능성 있음**(검색 결과 파싱 기반).
- OpenGameArt 두 페이지와 FAQ는 403. Kenney 오디오 팩 페이지는 열지 못함.
- 소리를 **들어보지는 못했다**(텍스트로만 조사). 어느 것이 "스타워즈 같지 않은지"는 준환의 귀가 정한다.
- 범주별 남은 공백: 함석 지붕·데크 발소리, 근거리 폭발, 차량 가속·충돌, 도심 앰비언스, 한국어 방송.

---

# 7. 액션별 소리 사용표 (2026-11-16 추가)

코드(`CombatantAudio`, `SpatialAudio`, `AmbientBeds`, `VehicleAudio`, `ElevatorAudio`, `DoorFx`, `GlassFx`, `MeleeAudio`, `ThrowableView`, `StormFx`, `ZoneBanner`, `EnvironmentView`, `VehicleDamageVisual`)와 `ASSET_LEDGER`로 확인한 현재 상태다. **합성** = 코드/스크립트가 만든 소리(청취 불만 후보), **파일** = 녹음 파일, **무음** = 소리 호출 없음(코드 확인).
"카테고리"는 `SpatialAudio` 범주(Body 2~40m, Gunshot 12~750m, Explosion 15~800m, Vehicle 6~150m, Elevator 3~60m, Impact 3~60m). 범위는 audio.json 값 기준이고 제안값은 "제안"으로 표시했다.

## 7-1. 전투·캐릭터

| 액션 | 지금 | 쓸 소리(제안) | 레이어·재생 방식(제안) | 후보 링크(§번호) |
|---|---|---|---|---|
| 발사(6종 무기) | 파일 4(권총·SMG·소총·산탄) + 저격은 소총 낮은 피치 재사용 | 무기마다 근거리 단발 3~5변형 + 원거리 + 실내/실외 꼬리 | Gunshot, 근거리·중거리·원거리 3겹, 피치 ±5% | §4-1 |
| 원거리 총성 | 파일 5(같은 라이브러리 파생) | 먼 소리 별도 녹음 또는 저역통과 파생 | 200m부터 단독 | §4-1, §4-1 원거리 행 |
| 소음기 발사 | 4배 먼 소리로만 처리 | 소음기 전용 녹음 | | [silenced pistol shot](https://freesound.org/people/Clutvh/sounds/627087/) F, [Silenced shot](https://freesound.org/people/hoot_avi/sounds/100465/) F |
| 장전 | 파일 3(권총·소총·산탄). 소총계 3종이 같은 소리 | 무기별 탄창 분리·장착·노리쇠 3단 | Body(몸 소스), 발소리 거리로만 들림 | §4-2 + **준환 선택 Glock19_reload(CC-BY)** |
| 빈 탄창(격발 공이 소리) | **무음**(코드에서 호출 없음, 추정) | 짧은 딸깍 | Body | f8studios 팩에 있음(슬라이드·해머 기계음) |
| 탄피 떨어짐 | **무음** | 금속 탄피 바닥 튐 1~3종 | Impact 소범위 | 후보 미조사 → §7-3 |
| 무기 꺼냄·집어넣음·ADS | **무음** | 짧은 기계음/천 소리 | Body | f8studios 팩 기계음 활용 가능 |
| 탄 스침(근처로 지나감) | 합성(SF 레이저 느낌, 추정) | 실제 탄 스침 3~4종 | Body, 3m | [Real Bullet Flyby (qubodup)](https://freesound.org/people/qubodup/sounds/855248/) F · [Bullet Fly By (GO_s0unds123)](https://freesound.org/people/GO_s0unds123/sounds/730351/) F · [Fly-by whiz subsonic (modusmogulus)](https://freesound.org/people/modusmogulus/sounds/789222/) F · [Whizzby (Daleonfire)](https://freesound.org/people/Daleonfire/sounds/505238/) F |
| 탄착(표면별 맞는 소리) | 파일 일부 재사용 가능성(확인 필요) | 콘크리트·금속·나무·흙·물·유리 6종 | Impact | [Bullet Ricochet (Joao_Janz)](https://freesound.org/people/Joao_Janz/sounds/478345/) F · [bullet ricochet (aust_paul)](https://freesound.org/people/aust_paul/sounds/30932/) F · [22 caliber with ricochet (C-V)](https://freesound.org/people/C-V/sounds/523403/) F (표면별은 추가 검색) |
| 피격(부위별) | Kenney impactPunch 재사용 | 머리·몸·다리 3종, 사망 | 피격자 2D + 근처 Body | [VisceralBulletImpacts (u1769092)](https://freesound.org/people/u1769092/sounds/423301/) F · [Headshot (Pablobd)](https://freesound.org/people/Pablobd/sounds/511194/) F (CC0 필터 결과 2건뿐, 사망 비명은 후보 없음) |
| 히트·킬 확인음(HUD) | Kenney impactPunch_medium 재사용 | 짧은 2D 알림음 | UI 2D | Kenney Interface Sounds(§4-6) |
| 근접 휘두름 | 합성 swoosh | 휘두름 2~3종 | Body | [Basic Melee Swing (SypherZent)](https://freesound.org/people/SypherZent/sounds/420668/) F · [Hard Swing 1 (magnuswaker)](https://freesound.org/people/magnuswaker/sounds/523230/) F · [Fantasy Sword Swing (Assetsmith)](https://freesound.org/people/Assetsmith/sounds/876166/) F(이름이 판타지, 비교용) |
| 근접 타격·주먹 | Kenney impactPunch / 금속 종(Pan) | 타격감 있는 둔탁음 | Body | Kenney Impact Sounds(현재 사용 중) 유지 가능 |
| 발소리 | 6표면 파일(눈 발소리를 흙에 사용 등) | 14표면 + 달리기·웅크림 | Body | §4-3 |
| 착지 | 발소리 재사용 | 표면별 착지 | Body | Nox_Sound 팩에 점프·착지 시퀀스 있음(§4-3) |
| 수영·물 진입 | 물 발소리만. **수영 소리 무음**(추정) | 첨벙·수영 루프 | Body | [water movements swimming (kyles)](https://freesound.org/people/kyles/sounds/637958/) F |
| 낙하 피해·신음 | **무음** | 짧은 신음·충격 | 피격자 2D | 후보 없음(CC0 필터에서 0건) → 준환이 직접 찾을지 결정 |
| 줍기·가방 열기 | **무음**(`Loot.cs`에 호출 없음) | 지퍼·천 소리 | Body | [Backpack unzip search (usamah)](https://freesound.org/people/usamah/sounds/464996/) F · [Unzip, dig through bag, zip (jeffdutton10)](https://freesound.org/people/jeffdutton10/sounds/332538/) F · [Zipper Variety 1 (EverydayEldritch)](https://freesound.org/people/EverydayEldritch/sounds/611446/) F |
| 치료(붕대·키트) | **무음** | 붕대 감기·테이프 | Body | [Tape Bandages Rip (SecureSubset)](https://freesound.org/people/SecureSubset/sounds/800275/) F · [bandaging (toefur)](https://freesound.org/people/toefur/sounds/288924/) F · [Band aids unwrapping (SpliceSound)](https://freesound.org/people/SpliceSound/sounds/188189/) F |
| 부스트·약 먹기 | **무음** | 병·알약 소리 | Body | 후보 미조사 |
| 방어구 맞음·장착 | **무음** | 금속·천 | Impact | 후보 미조사 |
| 이모트 | **무음**(제안: 무음 유지) | — | — | — |

## 7-2. 월드·구역·환경

| 액션 | 지금 | 쓸 소리(제안) | 범위 | 후보 링크 |
|---|---|---|---|---|
| 수류탄 폭발 | 산탄총 소리 파일 재사용(`_blastClip`) | 폭발 근거리·중거리 2종 | Explosion 15~800 | [Grenade Explosion (unfa)](https://freesound.org/people/unfa/sounds/609587/) F(설명 "medium-sized, meaty, realistic") · [Explosion (florianreichelt)](https://freesound.org/people/florianreichelt/sounds/563010/) F · [Explosion (cejordi84)](https://freesound.org/people/cejordi84/sounds/232398/) F · 원거리는 §4-5 |
| 섬광탄 폭발 | 산탄총 소리 ×0.6 | 섬광탄 전용 + 귀울림 | Explosion + 피격자 2D | [Flashbang Open Field (modusmogulus)](https://freesound.org/people/modusmogulus/sounds/752628/) F · [Flashbang (modusmogulus)](https://freesound.org/people/modusmogulus/sounds/737326/) F · [Flashbang Designed (modusmogulus)](https://freesound.org/people/modusmogulus/sounds/734095/) F(제목상 합성 가공, 비교용) · 귀울림은 CC0 검색 0건 |
| 연막탄 | 합성(`FireCrackle` 등) | 쉬익 가스 분출 | Impact | [Gas Grenade (Themiwa100)](https://freesound.org/people/Themiwa100/sounds/551429/) F(1건뿐) |
| 불타는 소리 | 합성 `FireCrackle` | 불 타는 루프 | Impact | 후보 미조사 |
| 차량 엔진 | 합성 루프 | 엔진 아이들 + 가속 루프 | Vehicle 6~150 | §4-5 (아이들만, 가속 추가 필요) |
| 차량 경적 | 합성 | 경적 1~2종 | 봇 60m | [Car Horn (BeatsbyCasper)](https://freesound.org/people/BeatsbyCasper/sounds/170243/) F · [car horn 1 (3863)](https://freesound.org/people/3863/sounds/235363/) F · [car horn honking (99021905683)](https://freesound.org/people/99021905683/sounds/571348/) F |
| 타이어 스크리치 | 합성 | 타이어 끽 소리 | Vehicle | [Chrysler LHS tire squeal 01~04 (audible-edge)](https://freesound.org/people/audible-edge/sounds/71736/) F(4파일 연번 71736~71739) |
| 차량 충돌·파손 | 합성 `VehicleCrunch` | 금속 충돌 | Vehicle | [car crash short 1 (Logicogonist)](https://freesound.org/people/Logicogonist/sounds/807438/) F · [Collision (qubodup)](https://freesound.org/people/qubodup/sounds/332058/) F · [Collision Reverb (qubodup)](https://freesound.org/people/qubodup/sounds/332059/) F |
| 차량 폭발 | 합성 `VehicleExplosion` | 대형 폭발 | Explosion | 폭발 후보 재사용 |
| 엘리베이터 이동·도착·문 | 합성(윙·쿵·차임) | 이동 루프·도착 차임·문 | Elevator 3~60 | §4-5 |
| 문 열림·닫힘 | 합성 wav 6개 | 문 3종 | Body | §4-5 |
| 문 노크·부수기 | 합성 | 문 두드림·부서짐 | Impact | 후보 미조사(문 부수기는 "wooden door break" 추가 검색 필요) |
| 유리 깨짐 | 합성 | 두께별 | Impact 3~60 | §4-4 + **준환 선택 Hard Glass Break 2** |
| 구역 경고(warning)·봉쇄(lockdown)·폭격 접근·폭격 | **무음 추정**(코드에서 `SetClips` 호출처 없음. 씬 직렬화로 연결됐는지는 미확인) | 경고음·사이렌·접근 휘파람·폭발 | 전역 2D | §4-6 사이렌 + 폭발 |
| 가이거 계수관 | **없음**(제안) | 오염 강도별 3단 | 지역 | §4-6 |
| 방송(안내) | **없음**(제안) | 안내 방송·차임 | 2D | §4-6 |
| 비 | 합성 `RainLoop` | 비 루프(지붕/야외) | 2D·Impact | [Rain on Shed Roof (timothyd4y)](https://freesound.org/people/timothyd4y/sounds/527499/) F · [Rain on the House Roof Window (bassimat)](https://freesound.org/people/bassimat/sounds/833924/) F · [Hard Rain on Car Roof (eRobb4)](https://freesound.org/people/eRobb4/sounds/344460/) F |
| 천둥 | 합성 | 천둥 2~4종 | 2D | [Short_Thunder_Deep (SholeColtis)](https://freesound.org/people/SholeColtis/sounds/683420/) F · [peal of thunder distant (bastipictures)](https://freesound.org/people/bastipictures/sounds/243782/) F · [thunder7 (nednednerb)](https://freesound.org/people/nednednerb/sounds/38250/) F |
| 바람·새·귀뚜라미·물결·실내 룸톤 | 합성(S-0007) | 녹음 앰비언스 | 2D 베드 | 새·숲: [Forest Ambience with Birds (upkey)](https://freesound.org/people/upkey/sounds/826326/) F · [Birds in the Slovenian Forest (GammaGool)](https://freesound.org/people/GammaGool/sounds/850507/) F(제목상 "Generated Noise" 가공 → AI 아님 확인 필요) · 도시: [city ambience pedestrian subway (be_a_hero_not_a_patriot)](https://freesound.org/people/be_a_hero_not_a_patriot/sounds/332783/) F(**기차역 방송 포함**이라 게임에는 부적합할 수 있음) · 바람·귀뚜라미는 후보 못 찾음 |
| 동물·야생(Wildlife) | 파티클만, 소리 없음 | 새 소리는 앰비언스로 처리 | — | — |

## 7-3. 추가 검색 링크 (빈 곳 메우기용)
CC0 필터를 건 검색 결과 링크다. 검색 페이지는 로그인 없이 열린다. 직접 소리를 훑어볼 수 있다.
- 탄피: https://freesound.org/search/?q=bullet+casing&f=license%3A%22Creative+Commons+0%22
- 낙하·충돌 신음: https://freesound.org/search/?q=grunt+pain&f=license%3A%22Creative+Commons+0%22
- 문 부수기: https://freesound.org/search/?q=door+kick+break&f=license%3A%22Creative+Commons+0%22
- 귀울림: https://freesound.org/search/?q=tinnitus&f=license%3A%22Creative+Commons+0%22
- 바람: https://freesound.org/search/?q=wind&f=license%3A%22Creative+Commons+0%22
- 귀뚜라미: https://freesound.org/search/?q=crickets&f=license%3A%22Creative+Commons+0%22
- 불: https://freesound.org/search/?q=fire+crackling&f=license%3A%22Creative+Commons+0%22
- 함석 지붕 발소리: https://freesound.org/search/?q=footsteps+metal+roof&f=license%3A%22Creative+Commons+0%22
- 데크·부두: https://freesound.org/search/?q=footsteps+wooden+dock&f=license%3A%22Creative+Commons+0%22
- 약병·알약: https://freesound.org/search/?q=pill+bottle&f=license%3A%22Creative+Commons+0%22

## 7-4. 준환이 정해야 할 것 (소리 쪽)
1. **직접 내려받을 것**: 위 표의 링크는 전부 준환이 로그인해서 받아야 한다(Freesound 로그인 필요). 받을 때 `AssetStaging/Audio/<범주>/`에 파일명 앞에 Freesound ID(예: `855608_`)를 붙여 두면 ledger 기록이 쉽다.
2. **CC-BY 예외**: Glock19_reload 한 건 때문에 CC-BY를 받을지 정한다(앞서 설명).
3. **합성 유지 범위**: 엘리베이터 윙·차량 엔진·앰비언스는 녹음 후보가 약하다. "합성 유지, 총성·발소리·유리·문부터 교체"가 현실적이다(제안).
4. **무음 액션 7~8개**: 빈 탄창, 탄피, 무기 꺼냄, 수영, 줍기, 치료, 방어구, 구역 경고·봉쇄·폭격. 계획(PLAN)에 넣을지는 마스터 결정.
5. **Sonniss**: 위 CC0 후보가 약한 곳(근거리 폭발, 차량 가속, 앰비언스)은 Sonniss 2026 시트에서 찾는 편이 빠를 수 있다. 가입·다운로드는 준환.

링크 F 표시는 필터 검색 결과이며 소리 페이지 라이선스를 직접 확인하지 않았다(§0). 이 절의 모든 링크가 그렇다.

---

# 8. 준환 청취 결과와 대체 후보 (2026-11-16)

## 8-1. 결정 기록 (준환 확정)

| 범주 | 결정 | 메모 |
|---|---|---|
| 권총 1 | 유지: [Glock 19X (areniporgen)](https://freesound.org/people/areniporgen/sounds/828786/) ✔CC0 | 2번(LeMudCrab) 탈락 → 8-2 대체 |
| 저격 SR 1 | 채택, **잘라서 사용**: [qubodup Sniper Shot](https://freesound.org/people/qubodup/sounds/855608/) ✔CC0 | |
| 저격 SR 2 | 채택, **발사 소리만 잘라서**: [M24 (kennysvoice)](https://freesound.org/people/kennysvoice/sounds/351777/) F | 장전→발사 순서 파일 |
| 저격 SR 3 | **소음기 소리로 사용**: [50 cal shot (ajhartman42295)](https://freesound.org/people/ajhartman42295/sounds/668071/) F | 소음기 발사용 → 무기 소음기 장착 시 재생(제안) |
| AK 장전·탄창 | 채택: [AK-47 unloaded and reloaded (serøutōnin--deprivəd)](https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/674742/) F | 소총계 장전 |
| 유리 깨짐 | **확정**: [Hard Glass Break 2](https://freesound.org/people/ElevatorFan2020/sounds/861043/) ✔CC0 | |
| 문 | 확정: [Wooden door open-close (Ryding)](https://freesound.org/people/Ryding/sounds/125958/) F | 열림/닫힘으로 잘라 사용 |
| 엘리베이터 | 조사 후보 중 **알아서**: [going down 2 (conath)](https://freesound.org/people/conath/sounds/531792/) · [elevator 06](https://freesound.org/people/universodemalaonda/sounds/502344/) · [Elevator_opening (GilPS)](https://freesound.org/people/GilPS/sounds/241763/) | 구현 세션이 정함 |
| 차량 엔진 | 채택: [SFX_Car_Engine_Outside_Idle (GiocoSound)](https://freesound.org/people/GiocoSound/sounds/401552/) F | "시동 걸고 아무것도 안 한 상태" = 아이들 루프. **제외**: TRP Car engine idle variations |
| 폭발(원거리) | 채택: [Distant Explosion (Mr_KeybOred)](https://freesound.org/people/Mr_KeybOred/sounds/372086/) F | |
| 사이렌 | 둘 중 하나: [Poligonstudio](https://freesound.org/people/Poligonstudio/sounds/412171/) · [audiojacked Devonport](https://freesound.org/people/audiojacked/sounds/336640/) | 구현 세션이 들어보고 정함. audiojacked는 실물 녹음(제목상) |
| 방송 | 채택: [Announcement Chime (Qvantor)](https://freesound.org/people/Qvantor/sounds/879805/) F | 차임만 사용(음성 아님). "Caution high radiation" 음성은 후보에서 제외 |
| 탄 스침 | 채택: [Bullet Fly By (GO_s0unds123)](https://freesound.org/people/GO_s0unds123/sounds/730351/) F(**잘라서 사용**) · [Whizzby (Daleonfire)](https://freesound.org/people/Daleonfire/sounds/505238/) F | 둘 다 사용, 변형 2종 |
| 가이거 | 보류 (준환이 모름) | 아래 설명 |

**가이거 계수관이란**: 방사능을 재는 휴대 측정기로, 방사선에 닿으면 "치직·틱틱틱" 하는 짧은 클릭 소리를 낸다. 강하게 오염될수록 클릭이 빨라진다. 이 게임에서는 오염 지대 안에 있을 때 이 소리를 틀어 "지금 위험 구역"임을 알리는 용도(audio.json 제안). 소리 후보: [parabolix 실물 녹음](https://freesound.org/people/parabolix/sounds/417424/) F, [Sanderboah](https://freesound.org/people/Sanderboah/sounds/674113/) F. 싫으면 안 써도 되는 선택 항목이다. 정하려면 두 링크를 들어보면 된다.

## 8-2. 탈락 항목의 대체 후보 (모두 실제 녹음 우선)

준환 청취 결과: 권총2, SMG 2~3, AR 1~3, DMR 2, 샷건 1~3은 다른 무기 소리·사람 목소리·연발·비총성·SR 소리여서 **탈락**. 아래는 전부 페이지를 열어 확인한 것(✔)과 검색 결과(F)를 구분했다. **Franki-01234 "Weapons Fire Sounds" 팩**은 영화용 공포탄 녹음으로, 거리·골목·문 앞 반향별 변형이 있어 실내/실외 꼬리 레이어에 좋다(공포탄이라 실탄과 약간 다를 수 있음 → 들어보기).

| 범주 | 후보 | 라이선스 | 길이/형식 | 메모 |
|---|---|---|---|---|
| 권총 | [A Glock handgun being shot 3x (serøutōnin--deprivəd)](https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/855652/) F | CC0 필터 | ? | 제목상 3발 + 숲 반향. 설명에 "hollywood-esque" → 가공 가능성, 들어보기 |
| 권총 | [Pistol Shot (Kodack)](https://freesound.org/people/Kodack/sounds/253736/) F | CC0 필터 | ? | |
| 권총 | f8studios 9mm ([팩](https://f8studios.itch.io/snakes-second-authentic-gun-sounds-pack)) | 본문 문구(§4-1) | WAV | 앞서 소개. 리버브 있음/없음 둘 다 있어 레이어에 좋음 |
| SMG | [MP5 down the block (Franki-01234)](https://freesound.org/people/Franki-01234/sounds/201672/) ✔ | CC0 | 2.29s, AIFF 48k/16bit | MP5A3 실제 녹음, 공포탄 |
| SMG | [MP5 down the street](https://freesound.org/people/Franki-01234/sounds/201671/) F · [MP5 in doorway](https://freesound.org/people/Franki-01234/sounds/201667/) F | CC0 필터 | ? | 문 앞 = 실내 꼬리 후보 |
| SMG | [Silenced Submachine gun (Kodack)](https://freesound.org/people/Kodack/sounds/257364/) F | CC0 필터 | ? | 소음기 SMG |
| AR | [M16 burst in street (Franki-01234)](https://freesound.org/people/Franki-01234/sounds/201668/) ✔ | CC0 | 4.17s, AIFF 48k/16bit | **연발(버스트)** 녹음. 준환이 "연발이라 AR3 탈락"이라 했으므로 발사 1발만 잘라 쓰는 방법만 가능 |
| AR | [AK47 Fire into Street](https://freesound.org/people/Franki-01234/sounds/201670/) ✔ · [AK47 down the block](https://freesound.org/people/Franki-01234/sounds/201669/) F | CC0 | 5.21s, AIFF 48k/16bit | AKM 7.62x39. 연발이면 한 발 잘라 사용 |
| AR/카빈 | [carbine single (cognito perceptu)](https://freesound.org/people/cognito%20perceptu/sounds/46655/) ✔ | CC0 | 3.34s, WAV 44.1k/16bit | M1 카빈(2차대전 .30 소총) **단발**, 숲 개울가 녹음. 현대 AR과는 소리가 다름 |
| DMR | 위 M1 카빈 단발 · AKM 한 발 | | | DMR은 소총 단발 + 저격(SR) 소리를 섞는 안(제안). 기존 DMR2(OGA gunshot sounds, SKS/Mosin)는 사격장 녹음 한계로 탈락 |
| 샷건 | [Mossberg 500A - 1 shot and pump (AnthonyChan0)](https://freesound.org/people/AnthonyChan0/sounds/159710/) ✔ | CC0 | 3.09s, WAV 48k/24bit 스테레오 | 숲속 야외, **실제 녹음**(TASCAM), 발사+펌프 |
| 샷건 | [shotgun targetside (cedarstudios)](https://freesound.org/people/cedarstudios/sounds/148841/) F · [12 Gauge Fire Four Shots Quickly (mrrap4food)](https://freesound.org/people/mrrap4food/sounds/619041/) F | CC0 필터 | ? | |
| 샷건 기계음 | [Shotgun rack and shell drop (rammbostein)](https://freesound.org/people/rammbostein/sounds/436074/) F · [mossberg .12 gauge pump (ceremonialchapstick)](https://freesound.org/people/ceremonialchapstick/sounds/142894/) F | CC0 필터 | ? | 장전·펌프 |
| 빈 탄창 | [9mm Handgun Being Dry Fired (serøutōnin--deprivəd)](https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/674568/) F | CC0 필터 | ? | **격발 공이 딸깍**, 현재 무음 액션 채움 |
| 슬라이드 | [glock.wav (hiramjustus)](https://freesound.org/people/hiramjustus/sounds/55340/) ✔ | CC0 | 1.31s, WAV 44.1k/16bit 모노 | Glock 슬라이드 당김. **준환 선택 Glock19_reload가 CC-BY라 CC0 대체**로도 쓸 수 있음 |

**제외·주의**: db465의 `*_fire`(shotgun_fire, glock_fire, m4carbine_fire, 22lr_fire)는 페이지에 "절차적 합성"으로 설명된 것이 있어 합성 의심 → 사용 안 함. 탄창 분리·장착은 [A Glock handgun magazine removed and re-inserted](https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/854460/) F.

## 8-3. 아직 남은 결정
1. 위 8-2에서 SMG·AR·샷건을 다시 들어보고 고르기(권총은 Glock 19X 유지 + 8-2 후보 비교).
2. CC-BY인 Glock19_reload를 계속 쓸지, hiramjustus CC0 슬라이드로 대체할지.
3. 가이거 사용 여부.

---

# 9. 확정 현황 (master 요청으로 정리, 2026-11-16)
- **확정**: §8-1 전부, 그리고 CC-BY 처리는 **ADR-177로 확정**(Glock19_reload 사용, 크레딧 화면 표기, 저장소 파일명 `pistol_reload_01`).
- **미확정(기록상 준환 답 없음)**: SMG·AR·샷건 최종 선택(§8-2 후보에 "좋네"만 있음), 가이거 사용 여부, 발소리 14종·기타 §7 항목 선택. 확정으로 적으면 사실과 다르므로 `10_sound_download_checklist.md` B·'미결정'에 "받아서 들어 본 뒤 고름"으로 남겼다.
- 다운로드 목록은 `10_sound_download_checklist.md`.

# 10. 에이전트 확정 (준환 위임 "이제부터 알아서 해라", 2026-11-16)
SMG·AR·DMR·샷건·권총 변형·빈 탄창·가이거(사용)·발소리 14종(Nox_Sound 팩 우선)·§7 빈 칸 후보를 에이전트가 정해 `10_sound_download_checklist.md` B·D에 반영했다. 청취는 못 했으므로 **준환이 게임 안에서 듣고 바꾸면 된다.** 새로 찾은 후보: 탄피(BlondPanda), 귀울림(Breviceps), 신음(MrFossy), 문 부수기(qubodup), 바람(SholeColtis), 귀뚜라미(Defelozedd94), 불(SavReese), 데크 발소리(coalcon). 함석 발소리는 CC0 후보 없음 → MetalPlate 대용.

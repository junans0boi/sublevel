# ASSET_LEDGER — 에셋 대장 (모든 에셋 필수 기록)

규칙: 행이 없는 에셋은 커밋 금지. 번들은 번들 단위 + 사용 파일 목록. 라이선스 원문/스크린샷은 `docs/licenses/<id>/`에 보관.

| ID | 분류 | 이름 | 출처 URL | 라이선스 | 크레딧 필요 | 수정 여부 | 프로젝트 경로 | 사용 구역/용도 | 확인일 | 확인자 |
|---|---|---|---|---|---|---|---|---|---|---|
| (예시) A-0001 | 텍스처 | Concrete 034 | https://ambientcg.com/... | CC0 | 아니오 | 해상도 축소 | Assets/_Project/Art/Materials/... | D 바닥 | 2026-MM-DD | agent |
| A-0001 | 텍스처(PBR) | Concrete 034 (1K) | https://ambientcg.com/view?id=Concrete034 | CC0 1.0 | 아니오 | 파일명 변경, 러프니스→스무스니스 마스크 패킹(`_mask.png`) | Assets/_Project/Art/Materials/Concrete034 | 테스트 맵 벽 | 2026-11-12 | agent |
| A-0002 | 텍스처(PBR) | Painted Metal 004 (1K) | https://ambientcg.com/view?id=PaintedMetal004 | CC0 1.0 | 아니오 | 파일명 변경, 메탈릭·스무스니스 마스크 패킹, 컬러맵 채도 45%·명도 50%로 낮춤 | Assets/_Project/Art/Materials/PaintedMetal004 | 테스트 맵 강조색 소품(엄폐물) | 2026-11-12 | agent |
| A-0003 | 텍스처(PBR) | Metal Plates 006 (1K) | https://ambientcg.com/view?id=MetalPlates006 | CC0 1.0 | 아니오 | 파일명 변경, 메탈릭·스무스니스 마스크 패킹 | Assets/_Project/Art/Materials/MetalPlates006 | 테스트 맵 엄폐물·벽 | 2026-11-12 | agent |
| A-0004 | HDRI | Overcast Soil (Pure Sky) 2K | https://polyhaven.com/a/overcast_soil_puresky | CC0 1.0 | 아니오 | 아니오(임포트 시 큐브맵 변환) | Assets/_Project/Art/HDRI/OvercastSoilPuresky | 테스트 맵 하늘·환경광 | 2026-11-12 | agent |
| A-0005 | 텍스처(PBR) | Concrete 015 (1K) | https://ambientcg.com/view?id=Concrete015 | CC0 1.0 | 아니오 | 파일명 변경, 러프니스→스무스니스 마스크 패킹(`_mask.png`) | Assets/_Project/Art/Materials/Concrete015 | 테스트 맵 바닥 | 2026-11-12 | agent |
| A-0006 | 텍스처(PBR) | Metal 032 (1K) | https://ambientcg.com/view?id=Metal032 | CC0 1.0 | 아니오 | 파일명 변경, 메탈릭·스무스니스 마스크 패킹 | Assets/_Project/Art/Materials/Metal032 | 테스트 맵 벽·구조물 | 2026-11-12 | agent |
| A-0007 | 텍스처(PBR) | Ground 037 (1K) | https://ambientcg.com/view?id=Ground037 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Ground037 | 야외 맵 — 지형 레이어: 풀(이끼 섞인 자연 초지) | 2026-11-13 | agent |
| A-0008 | 텍스처(PBR) | Grass 003 (1K) | https://ambientcg.com/view?id=Grass003 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Grass003 | 야외 맵 — 지형 레이어: 풀 대안(짙은 짧은 풀, 절차 생성형) | 2026-11-13 | agent |
| A-0009 | 텍스처(PBR) | Ground 081 (1K) | https://ambientcg.com/view?id=Ground081 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Ground081 | 야외 맵 — 지형 레이어: 흙·자갈 길 | 2026-11-13 | agent |
| A-0010 | 텍스처(PBR) | Rock 051 (1K) | https://ambientcg.com/view?id=Rock051 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao, metallic. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Rock051 | 야외 맵 — 지형 레이어: 바위·절벽 | 2026-11-13 | agent |
| A-0011 | 텍스처(PBR) | Ground 054 (1K) | https://ambientcg.com/view?id=Ground054 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Ground054 | 야외 맵 — 지형 레이어: 젖은 모래·진흙(습지·해안) | 2026-11-13 | agent |
| A-0012 | 텍스처(PBR) | Asphalt 015 (1K) | https://ambientcg.com/view?id=Asphalt015 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Asphalt015 | 야외 맵 — 도로 아스팔트 띠 메시(차선 없음) | 2026-11-13 | agent |
| A-0013 | 텍스처(PBR) | Bricks 097 (1K) | https://ambientcg.com/view?id=Bricks097 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Bricks097 | 야외 맵 — 건물 벽: 낡은 붉은 벽돌(공장·마을) | 2026-11-13 | agent |
| A-0014 | 텍스처(PBR) | Painted Plaster 017 (1K) | https://ambientcg.com/view?id=PaintedPlaster017 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/PaintedPlaster017 | 야외 맵 — 건물 벽: 흰 회반죽(색은 머티리얼에서 조정) | 2026-11-13 | agent |
| A-0015 | 텍스처(PBR) | Corrugated Steel 005 (1K) | https://ambientcg.com/view?id=CorrugatedSteel005 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao, metallic. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/CorrugatedSteel005 | 야외 맵 — 창고 벽·지붕, 컨테이너 골함석(색은 머티리얼에서 조정) | 2026-11-13 | agent |
| A-0016 | 텍스처(PBR) | Roofing Tiles 014B (1K) | https://ambientcg.com/view?id=RoofingTiles014B | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/RoofingTiles014B | 야외 맵 — 마을 주택 지붕 기와 | 2026-11-13 | agent |
| A-0017 | 텍스처(PBR) | Planks 039 (1K) | https://ambientcg.com/view?id=Planks039 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Planks039 | 야외 맵 — 나무 바닥·오두막 벽·나무 상자 | 2026-11-13 | agent |
| A-0018 | 텍스처(PBR) | Concrete 047A (1K) | https://ambientcg.com/view?id=Concrete047A | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Concrete047A | 야외 맵 — 건물 바닥·승강장 콘크리트 | 2026-11-13 | agent |
| A-0019 | 텍스처(PBR) | Metal 022 (1K) | https://ambientcg.com/view?id=Metal022 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, metallic. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/Metal022 | 야외 맵 — 녹슨 금속 소품(드럼통·탱크·배관) | 2026-11-13 | agent |
| A-0020 | 텍스처(PBR) | Painted Metal 014 (1K) | https://ambientcg.com/view?id=PaintedMetal014 | CC0 1.0 | 아니오 | 파일명 변경만(픽셀 그대로). 사용 맵: color, normal, roughness, ao, metallic. 마스크 패킹은 에디터 코드가 수행 | Assets/_Project/Art/Materials/PaintedMetal014 | 야외 맵 — 칠 벗겨진 금속 소품(컨테이너·설비) | 2026-11-13 | agent |
| A-0021 | 모델(바위) | Poly Haven Rock 07 (FBX + 1K JPG) | https://polyhaven.com/a/rock_07 | CC0 1.0 | 아니오 | 파일명 변경만. FBX 그대로, 텍스처는 JPG 3장(color/normal/arm) | Assets/_Project/Art/Models/Rock07 | 야외 맵 — 엄폐용 바위. LOD0~3 내장(LOD0 약 14.8k tris) | 2026-11-13 | agent |
| A-0022 | 모델(바위) | Poly Haven Rock 09 (FBX + 1K JPG) | https://polyhaven.com/a/rock_09 | CC0 1.0 | 아니오 | 파일명 변경만. FBX 그대로, 텍스처는 JPG 3장(color/normal/arm) | Assets/_Project/Art/Models/Rock09 | 야외 맵 — 엄폐용 바위. LOD0~3 내장(LOD0 약 12.4k tris) | 2026-11-13 | agent |
| A-0023 | 모델(바위) | Poly Haven Rock Moss Set 01 (FBX + 1K JPG) | https://polyhaven.com/a/rock_moss_set_01 | CC0 1.0 | 아니오 | 파일명 변경만. FBX 그대로, 텍스처는 JPG 3장(color/normal/arm) | Assets/_Project/Art/Models/RockMossSet01 | 야외 맵 — 엄폐용 바위. 이끼 낀 바위 6개(각 5k~16.5k tris), LOD 없음 | 2026-11-13 | agent |
| W-0001 | 무기 모델 | Quaternius Ultimate Guns Pack — 사용 파일: Pistol_1.fbx, SubmachineGun_1.fbx, Bullpup_1.fbx, Shotgun_1.fbx | https://quaternius.com/packs/ultimategun.html | CC0 1.0 | 아니오 | 아니오. 상표·각인·텍스처 없음(단색 머티리얼), 게임 내 명칭은 오리지널(ADR-032) | Assets/_Project/Art/Weapons/QuaterniusUltimateGuns | 무기 외형 | 2026-11-12 | agent |
| C-0001 | 캐릭터+애니메이션 | Quaternius Ultimate Modular Men Pack — 사용 파일: Swat.fbx (애니메이션 24개 내장) | https://quaternius.com/packs/ultimatemodularcharacters.html | CC0 1.0 | 아니오 | 모델 파일은 그대로. 임포트 시 노멀 재계산(75°), 머티리얼은 프로젝트 머티리얼로 대체 | Assets/_Project/Art/Characters/QuaterniusModularMen | 플레이어·더미 외형 | 2026-11-12 | agent |
| S-0001 | 효과음(총성) | The Free Firearm Sound Library — 사용: 근거리 단발 녹음 4개(권총·기관단총·소총·산탄총)에서 잘라낸 shot_pistol/smg/rifle/shotgun.wav | https://opengameart.org/content/the-free-firearm-sound-library | CC0 1.0 (OpenGameArt 페이지 표기. 압축 파일 안에는 라이선스 문서 없음) | 아니오 | 단발 구간 자르기, 16bit 44.1kHz 모노 변환, 음량 정규화, 파일명 변경(원본 폴더의 실총 명칭은 프로젝트에 넣지 않음) | Assets/_Project/Audio/Weapons/FreeFirearmLibrary | 총성 4종 | 2026-11-12 | agent |
| S-0002 | 효과음(재장전) | Gun reload sounds (SpringySpringo) — gunreload1, assaultriflereload1_0, shotguncock_0 | https://opengameart.org/content/gun-reload-sounds | CC0 1.0 | 아니오 | 파일명 변경 | Assets/_Project/Audio/Weapons/OgaGunReload | 재장전 | 2026-11-12 | agent |
| S-0003 | 효과음(발소리·피격) | Kenney Impact Sounds 1.0 — footstep_concrete_000~004, impactPunch_medium_000~002 | https://kenney.nl/assets/impact-sounds | CC0 1.0 | 아니오 | 아니오 | Assets/_Project/Audio/Foley/KenneyImpactSounds | 발소리(콘크리트), 피격 | 2026-11-12 | agent |

| F-0001 | 폰트 | Noto Sans KR Regular (Noto Sans CJK KR subset OTF) | https://github.com/notofonts/noto-cjk/tree/main/Sans/SubsetOTF/KR | SIL Open Font License 1.1 | 아니오(라이선스 사본 동봉) | 파일명 변경(NotoSansKR-Regular.otf → NotoSansKR.otf), 폰트 자체는 수정 없음 | Assets/_Project/Art/Fonts/NotoSansKR | 게임 UI 전체(한글) | 2026-11-13 | agent |
자체 제작(외부 출처 없음, Art/Audio 밖에 둠 — ADR-027): `Assets/_Project/Prefabs/M1_5/Textures/FabricNoise.png`은 `CharacterKit`이 코드(고정 시드 값 노이즈)로 만든 텍스처다. AI 생성물 아님.

## 분류 코드
A=환경/머티리얼, W=무기, C=캐릭터, N=애니메이션, V=차량, S=사운드, M=음악, F=폰트, U=UI, P=코드 패키지

## 검증 스크립트 요구 (M0-7)
`Assets/_Project/Editor/AssetValidator`가 `Assets/_Project/Art|Audio` 하위 폴더가 ledger에 존재하는지 검사하고, 없으면 빌드 실패.

## 코드 패키지 (P)
| ID | 분류 | 이름 | 출처 URL | 라이선스 | 크레딧 필요 | 수정 여부 | 프로젝트 경로 | 사용 구역/용도 | 확인일 | 확인자 |
|---|---|---|---|---|---|---|---|---|---|---|
| P-0001 | 코드 패키지 | Fish-Net 4.7.3 | https://github.com/FirstGearGames/FishNet | 커스텀(무료·로열티프리·게임 사용 허용, 경쟁 네트워킹 제품 금지). **needs-license-review** | 확인 필요 | 아니오 | Packages/manifest.json (git, 태그 4.7.3) | 넷코드 | 2026-11-12 | agent |
| P-0002 | 코드 패키지 | Universal RP 17.3.0 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 렌더링 | 2026-11-12 | agent |
| P-0003 | 코드 패키지 | Input System 1.20.1 | Unity Package Manager | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 입력 | 2026-11-12 | agent |
| P-0004 | 코드 패키지 | Cinemachine 3.1.4 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 카메라 | 2026-11-12 | agent |
| P-0005 | 코드 패키지 | Test Framework 1.6.0 | Unity Package Manager (번들) | Unity Companion/Package License | 아니오 | 아니오 | Packages/manifest.json | 테스트 | 2026-11-12 | agent |

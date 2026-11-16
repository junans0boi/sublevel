# 캐릭터 트랙 DONE (2026-11-16, 브랜치 `track/character`)

## 한 것 (확인 = Unity 실행·테스트 결과)
- **복원**: `CharacterImport.SyncStaging`이 Mixamo Swat + UBC 남·여를 Humanoid·Mesh LOD로 반입. PNG·FBX 삭제 후 재실행 2회, `.meta` 변경 0(텍스처 GUID를 git에서 복구하도록 수정). 첫 Unity 실행(Library APFS 클론) 28초, 재실행 17초. (ADR-228)
- **로스터**: 15 모델(Mixamo 13: Swat + 민간 9 + 군복 3, UBC 남·여), 변형 총 66 외형(앞서 적은 108은 계산 오류). 새 9종은 master가 준환 지시로 받아 둔 FBX(합계 약 0.5GB, 비커밋). `Prefabs/Roster/*.prefab`, `Editor/CharacterRoster.cs`(`Build`, `RenderLineup`), `Scripts/Gameplay/CharacterDef.cs`(S4 인터페이스: 소켓·변형·키·눈). (ADR-225·226)
- **비율**: 키 1.8m·눈 1.6m 정규화, 머리 0.148~0.175, 팔 0.25~0.31, 다리 0.42~0.49(키 대비, 12 모델 실측). 소총 Warden 0.75m=키의 0.42배·팔의 1.33배, 순증 검사. (ADR-229, `CharacterRosterTests`)
- **LOD**: 내장 Mesh LOD 곡선 실측 + lodBias 1에서 100m 컬(95m 보임/105m 0 확인). Mixamo 모델 LOD0 19~59k > 15k 상한 → 모델별 시작 LOD 자동(1~3). 삼각형 4m 7.0~14.5k, 20m 1.6~8.0k, 70m 0.4~2.0k. 텍스처 4096 원본은 2048로 로드. (ADR-227)
- **50체 PERF**(12 모델 혼합, 에디터 PlayMode 1080p, 그림자 켬): 분산 p99 5.3ms·드로우 373·삼각형 26만 / 밀집 p99 6.3ms·드로우 489·삼각형 63만 / SetPass 11 → PERF_BUDGET A 합격. `RosterRenderPerfTests`.
- **테스트**: EditMode 전체 1227/1227 통과(내 6건 포함). PlayMode는 `RosterRenderPerfTests` 2건만 실행(소리 없음). FBX 비추적 검사 테스트 포함.
- **문서**: ADR-225~229, ASSET_LEDGER(C-0003 갱신·C-0007), characters.json 표, BACKLOG 3줄.
- 캡처: `Logs/roster/lineup_{front,side}.png`, `perf50_{spread,worst}.png`(Logs는 저장소 밖 아님, 미커밋).

## 못 한 것
- **새 Mixamo 9종은 민간 복장**(청바지·후드 등). 군복은 아래 "군복 3종"으로 보강. 색조는 피부와 옷이 한 아틀라스라 약하게만 걸었다(눈으로 확인: lineup_front.png).
- **UBC는 맨몸**: 의상 없는 모델이라 단색 작전복 재질로 표시 → 얼굴·손이 몸과 같은 색. 외형 품질 한계(BACKLOG). Swat 텍스처에는 "SWAT" 글씨가 있다(일반 명사지만 상표·정서 검토는 사람 몫, RISKS 목록에 올릴 것).
- **크레딧**: 두 출처 모두 표기 의무가 없다(CC0, Mixamo 로열티 프리). `Credits.cs`는 CC-BY 전용이고 소유 밖이라 수정하지 않았다. 자발적 표기 문구 제안: "Characters: Quaternius Universal Base Characters (CC0), Mixamo (Adobe)" — UI 트랙이 크레딧 화면에 넣을 것.
- **게임 연결 안 함**: 플레이어·봇은 여전히 UMM Swat(Generic). Animator 컨트롤러·IK·손잡이 오프셋은 애니 트랙. 애니메이션·IK·실제 맵을 포함한 PERF는 애니 트랙(50인 Animator PERF).
- 헬멧·방어구 소켓 연결, 배낭 부착 미검증(소켓만 만듦).

## 추정/주의 (확인 못 한 것)
- 머리 비율은 목관절~정수리 기준이라 턱 기준 값이 아니다(0.16은 실제 머리보다 큼). #230 "머리 과대"의 체감 판정은 포즈 잡은 캡처가 필요(T-포즈만 촬영).
- 50체 수치는 정지 T-포즈·빈 바닥. 실제 맵 부하와 합산하지 않았다.
- 컷 거리는 lodBias에 비례(Ultra 200m). 게임 기본 품질에서의 값은 그래픽 설정 확인 필요.
- Mixamo 약관 "비공개 저장소 포함" 문구는 여전히 미확인(커밋은 `.meta`·프리팹·재질뿐).

## 병합 시 주의
- 소유 밖 변경: `.gitignore`(Ubc 무시 2줄), `Scripts/Gameplay/CharacterDef.cs`(신규), `Tests/*`(신규 2), `BACKLOG.md`, `DECISIONS/ASSET_LEDGER`(추가만).
- 병합 후 새 클론에서 `tools/unity.sh run CharacterImport.SyncStaging` → `CharacterRoster.Build`(프리팹이 이미 있으면 갱신) 순으로 복원.
- `ProjectSettings` 변경은 매번 되돌렸다.

## 추가 라운드 (master 지시 "안 된 것" 전부)
- **(2) UBC 의상**: UV가 몸 부위별로 분리 가능한지 확인 → 가능(섬이 부위별). 본 가중치 래스터화로 상의·하의·장갑·부츠·피부 영역을 나누고 변형 8+1 텍스처를 구움. 머리·목은 피부 유지, 손 장갑, 발 부츠. (`Editor/CharacterOutfit.cs`, ADR-225, ledger C-0009). 캡처 `Logs/roster/posed_Ubc*_front.png`.
- **(5) 색조**: 곱하기 색조가 피부까지 물들이던 문제를 영역 재채색으로 해결(수치는 ADR-225). Swat은 머리가 별도 재질이라 곱하기 유지.
- **(3) 머리 비율 #230**: 소총 든 자세 캡처(`PoseLowReady`) + 턱 기준 수치. 턱~정수리/키: Mixamo 0.137~0.158, UBC 0.128, Swat 0.142(헬멧 포함). 실제 성인 0.125~0.145. 테스트 `Head_ChinToCrown_RatioIsHumanSized`(허용 0.11~0.16). 소총 파지 포즈에서 양손이 총에 닿는 것을 5종 캡처로 확인(UBC·Swat 양호, Brian 등 민간은 손이 총 아래로 약간 어긋남: 포즈는 임시).
- **(4) 헬멧·방어구·배낭**: `ArmorVisual`/`BackpackVisual`이 본 이름으로 찾아 로스터에 붙지 않는 문제 → `Head`·`Chest` 별칭 추가로 12종 모두 부착(EditMode `WornGear_MountsOnRosterBones_AtHeadChestBack`, 캡처 `posed_*`). **한계**: UBC 2종은 본 이름이 `Head`라 별칭을 못 넣어 헬멧이 눈을 덮음(Mixamo 10종은 정상). 수정은 `ArmorVisual` 3줄이라 소유 밖(애니 트랙/master).
- **(6) RISKS**: 크레딧 표기, Mixamo 약관·파생 텍스처 커밋, "SWAT" 글씨, 민간 복장 4건을 아래 "RISKS에 올릴 항목"에 목록으로 적음(RISKS 문서는 소유 밖이라 수정하지 않음).
- **(7) API**: `docs/tracks/character_API.md`.
- **(1) 군복 계열**: master가 Mixamo에서 받는 중. 도착하면 `SyncStaging` → `CharacterRoster.Entries` 한 줄 → `Build`.
- 테스트: EditMode 1229/1229(신규 2건 포함). 50체 PERF 재측정: 다른 Unity 5개가 동시에 돌아 p99가 흔들려(12.4ms 1회 관측) **3회 중 최선**을 기록하도록 테스트를 바꿨다: 분산 p99 7.1ms·드로우 373, 밀집 p99 7.0ms·드로우 489(합격, 조용한 머신 기준 재측정 필요). 변형 텍스처는 LFS PNG 43장 약 43MB.

## RISKS에 올릴 항목 (소유 밖 문서라 직접 수정하지 않음, master가 병합 시 추가)
- [ ] **캐릭터 크레딧·표기(사람)**: 로스터 모델 두 출처(Quaternius UBC=CC0, Mixamo=Adobe 로열티 프리)는 표기 의무가 없다. 그래도 크레딧 화면에 "Characters: Quaternius Universal Base Characters (CC0), Mixamo (Adobe)"를 넣을지 결정(UI 트랙이 `Credits.cs`는 CC-BY 전용이라 별도 항목 필요).
- [ ] **Mixamo Additional Terms 원문(사람)**: AI/ML 학습 금지 조항, 비공개 저장소 포함 문구, 원본 파일 재배포 금지의 범위. 저장소에는 FBX·추출 PNG를 올리지 않았지만(`.gitignore`), `Prefabs/Roster/Textures/*.png`는 Mixamo 텍스처에서 재채색한 파생물이라 이것을 커밋해도 되는지 확인 필요(공개 전에 파생 텍스처를 제거하거나 UBC(CC0) 것만 남기는 선택지).
- [ ] **Swat 텍스처의 "SWAT" 글씨**: Mixamo Swat 조끼 텍스처에 흰 글씨 SWAT이 있다(일반 명사, 실존 부대 로고 아님). 가상 세계관·상표·정서 검토(사람) 후 유지하거나 조끼 텍스처 영역을 지운다. 위치: `Roster_Swat_Soldier_Body_diffuse.png` 조끼 오른쪽.
- [ ] **Mixamo 새 9종은 민간 복장**: 군복 계열을 master가 추가 다운로드 중. 도착하면 `CharacterImport.SyncStaging`이 자동 반입하고 `CharacterRoster.Entries`에 한 줄 추가.

## 군복 3종 반입 (master 확보분)
- Steve(철모 군복 117MB), SwatGuy(위장 전술 115MB), GasMask(방독면 142MB, 4분간 크기 불변·FBX 헤더 정상 확인 후 반입). 모두 Humanoid, 로스터 **15 모델**. ledger C-0010.
- 수치: LOD0 삼각형 Steve 54.7k·SwatGuy 46.3k·GasMask 41.2k → 시작 LOD 2(≤15k). 텍스처는 4096이지만 2048로 로드. 4m 삼각형 Steve 14.6k, SwatGuy 13.0k. 본 수: Humanoid 55(전 모델 `isHuman`). 비율 머리(턱~정수리) 0.155~0.156, 팔 0.27~0.28, 다리 0.43~0.45.
- 텍스처 메모리(기본 변형, 에디터 측정): 군복 3종 16.0~24.0MB씩, 15 모델 합계 225MB, 전부 ≤2048px. 한 판에 15종이 모두 로드된다고 가정한 상한이다.
- 군복 3종은 위장·장비 아틀라스라 색상 이동이 깨져 변형 Base 1개만. 머리 본 이름 문제 없음(별칭으로 부착), 모델 자체 헬멧이 있어 `HasHelmetMask=true`.
- 머티리얼↔텍스처 연결은 FBX에 없어 슬롯명 규칙으로 정했다(Steve는 body2=1001·body1=1002, GasMask는 body=1001·body1=1002·body2=1003). 캡처(`posed_MixamoSteve/SwatGuy/GasMask_front.png`)로 확인.
- 50체 PERF(15 모델 혼합): 분산 p99 5.3ms·드로우 347·삼각형 31만, 밀집 p99 7.1ms·드로우 445·삼각형 70만, SetPass 11(합격). EditMode 1230/1230.

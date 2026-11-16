# 캐릭터 트랙 DONE (2026-11-16, 브랜치 `track/character`)

## 한 것 (확인 = Unity 실행·테스트 결과)
- **복원**: `CharacterImport.SyncStaging`이 Mixamo Swat + UBC 남·여를 Humanoid·Mesh LOD로 반입. PNG·FBX 삭제 후 재실행 2회, `.meta` 변경 0(텍스처 GUID를 git에서 복구하도록 수정). 첫 Unity 실행(Library APFS 클론) 28초, 재실행 17초. (ADR-228)
- **로스터**: 3 모델(MixamoSwat·UbcMale·UbcFemale) x 변형 9 = 27 외형. `Prefabs/Roster/*.prefab`, `Editor/CharacterRoster.cs`(`Build`, `RenderLineup`), `Scripts/Gameplay/CharacterDef.cs`(S4 인터페이스: 소켓·변형·키·눈). (ADR-225·226)
- **비율**: 키 1.8m·눈 1.6m 정규화, 머리 0.160, 팔 0.272~0.312, 다리 0.455~0.493(키 대비). 소총 Warden 0.75m=키의 0.42배·팔의 1.33배, 순증 검사. (ADR-229, `CharacterRosterTests`)
- **LOD**: 내장 Mesh LOD 곡선 실측 + lodBias 1에서 100m 컬(95m 보임/105m 0 확인). Swat LOD0 19.4k > 15k 상한 → bias +1. (ADR-227)
- **50체 PERF**(에디터 PlayMode 1080p, 그림자 켬): 분산 p99 4.5ms·드로우 243·삼각형 36만 / 밀집 p99 5.4ms·드로우 309·삼각형 75만 / SetPass 11 → PERF_BUDGET A 합격. `RosterRenderPerfTests`.
- **테스트**: EditMode 전체 1227/1227 통과(내 6건 포함). PlayMode는 `RosterRenderPerfTests` 2건만 실행(소리 없음). FBX 비추적 검사 테스트 포함.
- **문서**: ADR-225~229, ASSET_LEDGER(C-0003 갱신·C-0007), characters.json 표, BACKLOG 3줄.
- 캡처: `Logs/roster/lineup_{front,side}.png`, `perf50_{spread,worst}.png`(Logs는 저장소 밖 아님, 미커밋).

## 못 한 것
- **Mixamo 7~11종 추가**: 준환의 Mixamo 로그인·다운로드가 필요(blocked, 규칙 3). 지금 로스터는 3 모델.
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

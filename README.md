# SUBLEVEL (서브레벨)

**STEADY TO VIVID STUDIO**가 만드는 PC(macOS · Windows) 배틀로얄 게임입니다.
거대한 폐쇄 시설을 모티브로 한 가상의 섬에서, 참가자들은 **엘리베이터를 타고 무작위 지역에 투입**되어 파밍과 교전을 하고, **지역이 하나씩 닫히며** 좁혀지는 섬에서 **마지막 한 명(한 팀)** 이 될 때까지 살아남아야 합니다.

> 개발 중입니다. 일반 공개 배포는 아직 하지 않으며, 테스트 빌드는 로그인한 테스터에게만 [런처](#관련-저장소)로 배포합니다.
> 세계관·지명·무기 이름은 모두 오리지널입니다(실존 상표·브랜드·작품의 고유명사를 쓰지 않습니다).

- 웹사이트: <https://sublevel.steady2vivid.kro.kr>
- 기획 개요: [`docs/SPEC.md`](docs/SPEC.md) · 상세 정의서: [`docs/bible/`](docs/bible/)

![섬 전경](docs/tracks/map_mood_captures/after_air_wide.jpg)

## 게임 한눈에

| 항목 | 내용 |
|---|---|
| 모드 | 솔로 / 스쿼드(4인) |
| 인원 | 판당 80명(사람 최대 64 + 봇). 사람이 적으면 서버 쪽 봇 AI가 채움 |
| 맵 | 2.4 × 2.0 km 섬, 6개 지역, 마을 17곳, 건물 약 400채. 1980년대 한국 + 열대가 섞인 가상 세계 |
| 투입 | 승강장 20곳 × 승강기 3기. 매치 시작 때 서버가 참가자를 무작위 지역의 엘리베이터에 배정, 문이 열리면 6초 스폰 보호 |
| 압박 | 6개 지역 중 5개가 순서대로 닫히고 마지막 하나가 최종 지역(처음에는 숨김) |
| 탈출 | 희귀 **열쇠**로 승강장 엘리베이터를 움직여 아직 안전한 지역으로 이동(팀당 최대 2회, 누구나 함께 탈 수 있음) |
| 부활 | 스쿼드 전용 **칩 부활**: 사망하면 칩이 떨어지고, 팀원이 칩을 회수해 승강장에서 부활 |
| 판 길이 | 목표 20~25분 |

자세한 규칙은 [`docs/SPEC.md`](docs/SPEC.md)에 있고, 수치는 플레이테스트로 계속 조정합니다.

## 스크린샷

모두 Unity에서 직접 캡처한 실제 게임 화면입니다(AI 생성 이미지 없음).

| | |
|---|---|
| ![스쿼드 로비](docs/bible/img/ui/iter7_lobby_squad_1920x1080_after.png) | ![외형 선택](docs/bible/img/ui/iter7_char_1920x1080_after.png) |
| 스쿼드 로비: 엘리베이터 안에서 출발 대기 | 외형: 캐릭터 15종, 의상 색 변형 |
| ![청람읍권 마을](docs/tracks/map_mood_captures/after_air1_R1.jpg) | ![지상 시점](docs/tracks/map_mood_captures/after_eye2.jpg) |
| 청람읍권(R1): 섬 중심의 시가지 | 지상 시점: 숲과 마을 |

## 현재 들어가 있는 것

- **이동·시점**: 3인칭/1인칭 전환, 견착·정조준, Q/E 기울이기, 앉기·엎드리기·점프·매달리기, 수영
- **전투**: 권총·SMG·AR·DMR·저격·샷건 계열(전부 오리지널 이름), 조준경 8종, 단발/연사, 탄도, 헤드샷, 반동·저지력, 근접 무기, 파편·연막 수류탄
- **생존**: 헬멧·조끼 3단계, 가방 용량, 회복 아이템, 기절·수혈, 낙하 피해
- **차량**: 승용차·스포츠카·오토바이, 연료
- **매치**: 지역 경고 → 봉쇄 진행, 열쇠 엘리베이터, 스쿼드 칩 부활, 킬 로그, 관전·결과 화면
- **봇**: 서버 쪽 AI(이동·파밍·교전·엄폐·회복·열쇠 사용), 난이도별
- **멀티플레이**: 서버 권위 구조(Fish-Net), 전용 서버 빌드, 매칭 전 대기 섬, 지연 보상
- **UI**: 한글 UI, 로비(솔로/스쿼드), 외형, 훈련장, 설정, 미니맵·전체 지도, 인벤토리, 크레딧 화면

진행 상황은 [`docs/PROGRESS.md`](docs/PROGRESS.md), 결정 기록은 [`docs/DECISIONS.md`](docs/DECISIONS.md)(ADR)에 있습니다.

## 기술 스택

| 영역 | 사용 |
|---|---|
| 엔진 | Unity **6000.3.28f1** (6.3 LTS), URP 17.3 |
| 네트워크 | Fish-Net 4.7.3 (서버 권위, 전용 서버) |
| 입력·카메라 | Input System 1.20, Cinemachine 3.1 |
| 테스트 | Unity Test Framework 1.6 (EditMode · PlayMode) |
| 개발 방식 | Unity Editor를 커맨드라인(`-batchmode`)으로만 실행. 씬·프리팹·재질·월드는 Editor 스크립트가 코드로 생성 |
| 대상 | macOS 12+ (유니버설), Windows 64비트 |

## 빌드·실행

### 준비
- macOS(Apple Silicon 권장), Unity Hub + Unity **6000.3.x**(Mac/Windows Build Support IL2CPP 모듈)
- `git-lfs`(이미지·모델·씬은 LFS) — 클론 전에 `git lfs install`
- 자세한 환경: [`docs/SETUP.md`](docs/SETUP.md), 명령 모음: [`docs/UNITY_CLI.md`](docs/UNITY_CLI.md)

> **주의**: Mixamo 캐릭터 원본 FBX와 일부 애니메이션 원본은 약관상 저장소에 넣지 않습니다(ADR-192, ADR-228). `.meta`·프리팹만 커밋되어 있고, 원본은 개발자 로컬의 `AssetStaging/`에서 `CharacterImport.SyncStaging`이 복원합니다. 따라서 이 저장소만 클론해서는 전체 캐릭터 로스터가 포함된 빌드를 만들 수 없습니다.

### 명령
모든 Unity 작업은 래퍼 [`tools/unity.sh`](tools/unity.sh)를 거칩니다(설치된 6000.3.x 중 최신을 자동 선택, `UNITY_PATH`로 덮어쓰기).

```bash
tools/unity.sh test editmode            # EditMode 테스트 → Logs/test-editmode.xml
tools/unity.sh test playmode            # PlayMode 테스트 → Logs/test-playmode.xml
tools/unity.sh build mac                # Build/mac/Sublevel.app
tools/unity.sh build win                # Build/win/Sublevel.exe
tools/unity.sh build server             # 전용 서버(macOS) / server-win: Windows
tools/unity.sh run <Class.Method>       # Editor 스크립트 실행(예: CharacterImport.SyncStaging)
```

- 클라이언트 릴리스 패키징(zip + sha256 + manifest): [`tools/release.sh`](tools/release.sh), 절차는 [`docs/RELEASE.md`](docs/RELEASE.md)
- 전용 서버 배포: [`docs/DEDICATED_SERVER.md`](docs/DEDICATED_SERVER.md), [`docs/DEPLOY_GAME_SERVER.md`](docs/DEPLOY_GAME_SERVER.md)
- 성능 예산(머지 기준): [`docs/PERF_BUDGET.md`](docs/PERF_BUDGET.md)

## 프로젝트 구조

```
Assets/_Project/
  Scripts/Core/      순수 규칙(데미지·탄도·오염·봇 판단·월드 생성 규칙 등) — EditMode 테스트 대상
  Scripts/Gameplay/  화면·소리·HUD·입력 등 MonoBehaviour
  Scripts/Net/       Fish-Net 네트워크(전투원·매치·로비·대기 섬)
  Scripts/AI/        서버 쪽 봇
  Editor/            씬·프리팹·월드·캐릭터를 코드로 만드는 빌더, BuildScript
  Tests/             EditMode / PlayMode
  Scenes/            Bootstrap, MainMenu, World, TrainingRange, TestBox 등(생성물)
  Art/ Audio/ Prefabs/ Data/ Shaders/ Settings/
docs/                기획(SPEC·bible), 결정(DECISIONS), 진행(PROGRESS), 에셋 대장, 릴리스·배포 문서
tools/               unity.sh, release.sh, 측정·사운드·지도 보조 스크립트
placeholder/         임시 에셋(릴리스 전 제거)
```

## 관련 저장소

| 저장소 | 내용 | 공개 |
|---|---|---|
| `junans0boi/sublevel` (이 저장소) | 게임 클라이언트·전용 서버(Unity) | 공개 |
| `junans0boi/sublevel-web` | 공식 웹사이트(Next.js 정적 사이트), 배포 스크립트 | 비공개 |
| `junans0boi/sublevel-backend` | 계정(구글 로그인)·닉네임·판 결과·친구·파티·매칭·런처 다운로드 API | 비공개 |
| `junans0boi/sublevel-launcher` | 데스크톱 런처(Tauri 2): 로그인 → 게임 다운로드·검증·설치 → 실행 | 비공개 |

흐름: 웹사이트/런처에서 구글 로그인 → 백엔드가 세션과 게임 토큰 발급 → 런처가 서명된 URL로 게임 zip을 받아 sha256 검증 후 설치 → 게임이 토큰으로 매칭·게임 서버에 접속 → 게임 서버가 판 결과를 백엔드에 기록.

## 로드맵과 현재 위치

| 단계 | 내용 | 상태 |
|---|---|---|
| M0 | Unity CLI 헤드리스 파이프라인, 성능 기준선 | 완료 |
| M1 · M1.5 · M1.6 | 코어 루프(이동·사격·루팅), 에셋 도입, 게임 감각 | 완료 |
| M2 · M3 | 첫 구역·봇 AI·엘리베이터 투입, 봉쇄 시스템 | 완료 후 새 맵(6지역 섬)으로 재구성 |
| M4 · M5 | 여러 지역 풀 매치, 싱글 완성판 | 진행 중(플레이 피드백 반영) |
| M6 | 멀티 전환: 전용 서버, 지연 보상, 실제 다중 클라이언트 매치 | 진행 중(클로즈드 테스트 준비) |

최근(2026-11): 대기 섬(번지·승강기), 문·차량·애니메이션 수정, 지역 종료 방식(가스 상승·폭격) 결정, 총기별 프로필. 세부는 [`docs/ROADMAP.md`](docs/ROADMAP.md), [`docs/PROGRESS.md`](docs/PROGRESS.md).

## 릴리스

- `v*` 태그를 올리면 GitHub Actions([`.github/workflows/release.yml`](.github/workflows/release.yml))가 태그 메시지와 이전 태그 이후 커밋 목록으로 GitHub Release를 만듭니다.
- 게임 클라이언트 zip은 지금 **GitHub Release에 올리지 않습니다**(로그인 테스터에게 런처로만 배포). 올릴 수 있는 조건과 방법은 [`docs/RELEASE.md`](docs/RELEASE.md) "GitHub Releases"를 보세요.

## 라이선스와 크레딧

**코드**: 이 저장소의 코드에는 아직 오픈소스 라이선스를 붙이지 않았습니다. 별도 표기가 없으면 모든 권리는 STEADY TO VIVID STUDIO에 있습니다.

**에셋**: 모든 외부 에셋은 출처·라이선스·수정 여부를 [`docs/ASSET_LEDGER.md`](docs/ASSET_LEDGER.md)에 기록하고, 허용 기준은 [`docs/LICENSE_POLICY.md`](docs/LICENSE_POLICY.md)를 따릅니다.

| 출처 | 라이선스 | 쓰는 곳 |
|---|---|---|
| [ambientCG](https://ambientcg.com), [Poly Haven](https://polyhaven.com) | CC0 | 지형·건물 재질, 하늘(HDRI) |
| [Quaternius](https://quaternius.com) (Universal Base Characters·Animation Library, Ultimate Guns 등), [Poly Pizza](https://poly.pizza) 일부 모델 | CC0 | 캐릭터 2종, 맨손 애니메이션, 총기·소품 모델 |
| [Freesound](https://freesound.org) 녹음 대부분, [OpenGameArt](https://opengameart.org), [Kenney](https://kenney.nl) | CC0 | 총성·발소리·환경음 등 |
| Mixamo (Adobe) | Mixamo 약관(게임 내 사용 가능, 원본 파일 재배포 금지) | 캐릭터 13종, 소총·이동 애니메이션. 원본 파일은 저장소·배포물에 따로 넣지 않음 |
| Noto Sans KR | SIL Open Font License 1.1 | UI 글꼴 |
| Fish-Net | 개발사 라이선스(무료·로열티 프리) | 네트워크 |
| Unity 패키지(URP·Input System·Cinemachine·Test Framework) | Unity Companion License | 엔진 패키지 |
| 자체 제작 | STEADY TO VIVID STUDIO | UI 아이콘, 코드로 합성한 효과음·환경음, 코드 생성 월드·건물 |

### 크레딧(CC BY)
다음 에셋은 CC BY 라이선스로 사용하며, 게임 메인 메뉴의 **크레딧** 화면에도 같은 내용이 나옵니다(`Assets/_Project/Scripts/Core/Credits.cs`).

| 작품 | 작가 | 라이선스 | 용도 |
|---|---|---|---|
| [Fairheaven LT '80 - Low Poly Model](https://sketchfab.com/3d-models/fairheaven-lt-80-low-poly-model-e2678da920cc4be68dbc193727919ffb) | DanielZhabotinsky | CC BY 4.0 | 차량(세단) |
| [Fairheaven SW '84 - Low Poly Model](https://sketchfab.com/3d-models/fairheaven-sw-84-low-poly-model-aa0becb6e854422596cac6b21bf79787) | DanielZhabotinsky | CC BY 4.0 | 차량(왜건) |
| [Lightbody Lifted '85 - Low Poly Model](https://sketchfab.com/3d-models/lightbody-lifted-85-low-poly-model-67beae18c3d24be68f9c8f0ec382d8e3) | DanielZhabotinsky | CC BY 4.0 | 차량(픽업) |
| [Shvan '92 - Low Poly Model](https://sketchfab.com/3d-models/shvan-92-low-poly-model-09d718c9cf72401b8534d265a06a803f) | DanielZhabotinsky | CC BY 4.0 | 차량(밴) |
| [Fictional supercar - V12 Goblin](https://sketchfab.com/3d-models/fictional-supercar-v12-goblin-0a20e49ad5774d778567cb5c3f345786) | ollitei | CC BY 4.0 (수정함) | 차량(스포츠카) |
| [Mid poly dirt bike](https://sketchfab.com/3d-models/mid-poly-dirt-bike-3009c80ec6cc47fcaad27cf96f2a0822) | danilloribeiro | CC BY 4.0 | 오토바이(오프로드) |
| [Motorcycle](https://sketchfab.com/3d-models/motorcycle-38404e2077ca4b209cd2f1db30541b94) | animanyarty | CC BY 4.0 | 오토바이(로드) |
| [Generic passenger car pack](https://sketchfab.com/3d-models/generic-passenger-car-pack-20f9af9b8a404d5cb022ac6fe87f21f5) | comrade1280 | CC BY 4.0 | 주차·폐차 소품 |
| [Glock19_reload](https://freesound.org/s/100804/) | Fr3yr | CC BY 3.0 (수정함) | 권총 재장전 소리 |

라이선스 원문: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).

---

© STEADY TO VIVID STUDIO. 문의·버그는 GitHub Issues로 받습니다.

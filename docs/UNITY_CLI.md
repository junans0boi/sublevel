# UNITY_CLI — Unity Editor 커맨드라인으로 구현하기

> "unity cli" = **Unity Editor를 `-batchmode`로 실행하는 커맨드라인**으로 해석했다. 특정 래퍼 도구를 쓰기로 했다면 DECISIONS.md에 교체 기록.

## 핵심 원칙
1. **GUI 클릭 금지, 전부 코드로.** 씬/프리팹/머티리얼/설정은 `Assets/_Project/Editor/` 아래 C# Editor 스크립트(`static` 메서드)로 생성·수정한다.
2. 모든 실행은 `tools/unity.sh`를 거친다. 로그는 `Logs/*.log`, 결과는 종료 코드 + 로그 마지막 줄로 판단.
3. 같은 프로젝트에 Unity 인스턴스를 **동시에 둘 이상 열지 않는다**(락 파일 충돌). 순차 실행.
4. 컴파일 에러가 있으면 `-executeMethod`가 실행되지 않는다 → 먼저 로그에서 `error CS`를 grep 하고 고친다.

## 기본 명령 형태
```bash
UNITY="/Applications/Unity/Hub/Editor/<6000.3.x>/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PWD" \
  -executeMethod Namespace.Class.Method \
  -logFile Logs/run.log \
  -accept-apiupdate
```
| 인자 | 용도 |
|---|---|
| `-batchmode -nographics` | 창 없이 실행 (그래픽 필요한 작업은 `-nographics` 제외) |
| `-quit` | 메서드 끝나면 종료 (비동기 대기가 필요하면 코드에서 `EditorApplication.Exit(code)` 호출) |
| `-projectPath` | 프로젝트 루트 |
| `-executeMethod` | Editor 폴더의 public static 메서드 |
| `-logFile` | 로그 경로(`-`면 stdout) |
| `-buildTarget StandaloneOSX / StandaloneWindows64` | 타깃 전환 |
| `-runTests -testPlatform EditMode\|PlayMode -testResults <xml>` | 테스트 실행 (`-quit` 쓰지 않음) |
| `-testFilter "<이름>"` / `-testCategory` | 부분 테스트 |
| `-createProject <path>` | 새 프로젝트 생성 |
| `-accept-apiupdate` | API 업데이터 자동 허용 |
| `-serial/-username/-password`, `-returnlicense` | 헤드리스 라이선스 활성화/반납 |

## 표준 작업 레시피
- **프로젝트 설정**: `Setup.ConfigureProject` — URP 에셋 연결, Player 설정(회사명 STEADY TO VIVID STUDIO, 아이콘 placeholder), 스크립팅 백엔드, 해상도 옵션, 입력 시스템 활성화.
- **씬 생성**: `SceneBuilder.BuildZoneD` 등 — `EditorSceneManager.NewScene` → 오브젝트/프리팹 인스턴스 배치 → `SaveScene`. 레벨은 **데이터(ScriptableObject/JSON) + 생성 스크립트**로 재현 가능하게 만든다.
- **에셋 임포트 설정**: `AssetPostprocessor` 또는 `AssetImporter`로 텍스처(KTX2/압축, 최대 해상도 1K~2K), 모델(LOD, 읽기 비활성) 일괄 설정.
- **빌드**: `BuildScript.BuildMac`, `BuildScript.BuildWin` — `BuildPipeline.BuildPlayer`, 결과 `Build/mac`, `Build/win`. 실패 시 `EditorApplication.Exit(1)`.
- **테스트**: EditMode(순수 로직: 존 상태머신, 데미지, 루팅 테이블), PlayMode(씬 스모크: 로드, 스폰, 엘리베이터 투입, 존 폐쇄 진행). 모든 게임 로직은 **테스트 가능한 순수 C# 클래스**와 얇은 MonoBehaviour로 분리.
- **스크린샷/프로파일**: PlayMode 테스트나 `-batchmode`(그래픽 필요 시 nographics 제외)에서 `ScreenCapture`/프레임타임 로그를 파일로 저장해 PERF_BUDGET 검증.
- **헤드리스 서버 빌드**(M6): Dedicated Server 타깃, `-batchmode -nographics`로 실행.

## tools/unity.sh 서브커맨드
`create` · `run <Class.Method>` · `test editmode|playmode [filter]` · `build mac|win` · `log` (마지막 에러 요약) · `license-return`.

## 트러블슈팅
| 증상 | 조치 |
|---|---|
| "project already open" | 다른 Unity 프로세스 종료(`pkill -f Unity`), `Temp/UnityLockfile` 확인 |
| 라이선스 오류/활성화 요구 | 사람 개입 필요(`blocked`). 또는 `-serial` 방식 (개인 라이선스는 GUI 활성화 필요할 수 있음) |
| executeMethod가 안 돈다 | 컴파일 에러 확인, 메서드가 `public static`인지, 스크립트가 `Editor` 폴더인지 |
| 로그가 비어 있음 | `-logFile` 경로 폴더 존재 확인 |
| 패키지 해석 실패 | `Packages/manifest.json` URL/버전 확인, 네트워크 허용 목록 확인 |
| 첫 임포트가 매우 느림 | 정상. Library 폴더 유지, 순차 작업 |
| 에디터 GUI로만 가능한 작업 | 코드로 대체 불가 시 `blocked` + 사유 (M0에서 이런 목록을 `RISKS_OPEN_QUESTIONS.md`에 누적) |

## M0 증명 항목 (이게 실패하면 Godot 4 폴백 검토)
1. 헤드리스로 프로젝트 생성·설정·씬 생성 가능
2. EditMode/PlayMode 테스트 헤드리스 통과
3. mac/win 빌드 헤드리스 성공
4. 에이전트가 스크린샷/프레임타임을 파일로 얻어 스스로 검증 가능

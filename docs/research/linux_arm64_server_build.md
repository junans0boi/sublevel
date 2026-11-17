# Linux ARM64 전용 서버 빌드 검증 (#381, ADR-295·296)

결론: **Unity 6000.3.28f1에서 되고, ARM64 ELF가 나온다.** UUM-122045(Bee architecture 미구현) 오류는 나지 않았다. 단 Mono는 안 되고 **IL2CPP만** 된다.

## 환경
- 호스트: MacBook M1(arm64), Unity 6000.3.28f1, 서버 대상: Oracle Always Free ARM(Ubuntu 24.04 aarch64).
- 설치한 모듈(Unity Hub CLI `install-modules --version 6000.3.28f1 -m ...`, 다운로드는 준환이 허락):

| 모듈 id | 이름 | 다운로드 | 설치 크기 |
|---|---|---|---|
| `linux-mono` | Linux Build Support (Mono) | 약 82 MB | 약 218 MB |
| `linux-server` | Linux Dedicated Server Build Support | 약 225 MB | 약 600 MB |
| `linux-il2cpp` | Linux Build Support (IL2CPP) | 약 82 MB | 약 217 MB |

  경로 `/Applications/Unity/Hub/Editor/6000.3.28f1/PlaybackEngines/LinuxStandaloneSupport`(합계 976 MB). **ARM64 서버에는 `linux-server` + `linux-il2cpp`가 필요**하다(`Variations/`에 `linuxarm64_server_*_il2cpp`만 있고 arm64 Mono 변형은 없다). `linux-mono`는 x86_64용이라 안 깔아도 된다.
- 패키지(Unity가 첫 빌드 때 자동으로 `Packages/manifest.json`에 추가): `com.unity.sdk.linux-arm64 1.1.1`(sysroot), `com.unity.sdk.linux-x86_64 1.1.1`, `com.unity.toolchain.macos-arm64-linux 1.1.1`(크로스 컴파일 clang). IL2CPP가 Mac에서 Linux용 C++를 컴파일하는 데 필요.

## 결과
- `tools/unity.sh build server-linux-arm64` → `Build Finished, Result: Success`, 약 3분(IL2CPP). 로그 `Logs/build-server-linux-arm64.log`.
- 산출물 `Build/server-linux-arm64/`(커밋 안 함): 총 약 1.8 GB 중 **실제로 올릴 것은 약 340 MB**.

| 항목 | 크기 | 비고 |
|---|---|---|
| `SublevelServer` | 8 KB | `ELF 64-bit LSB pie executable, ARM aarch64`, 인터프리터 `/lib/ld-linux-aarch64.so.1` |
| `GameAssembly.so` | 97 MB | IL2CPP 결과, aarch64 |
| `UnityPlayer.so` | 34 MB | aarch64 |
| `SublevelServer_Data/` | 205 MB | 에셋·씬(서버 서브타깃이라 그래픽 셰이더 등은 빠짐) |
| `SublevelServer_Data/Plugins/lib_burst_generated.so` | — | Burst, aarch64 |
| `libdecor-0.so.0`, `libdecor-cairo.so` | 120 KB | **x86-64**(Unity가 그대로 복사). 창 장식용 선택 라이브러리라 `-batchmode -nographics`에선 안 쓰므로 올리지 않아도 됨(확인은 #383에서) |
| `SublevelServer_BackUpThisFolder_ButDontShipItWithYourGame/` | 1.5 GB | IL2CPP 중간물·디버그 심볼. **올리지 않는다** |

- Fish-Net 4.7.3(Tugboat 포함): 네이티브 플러그인 없음. 패키지 안의 바이너리는 `FishNet.CodeAnalysis*.dll`(에디터 전용 분석기)뿐이라 ARM64 문제 없음. 프로젝트 `Assets`에도 `.so/.dylib/.a` 없음. 빌드에 들어간 네이티브는 Unity 자체(`UnityPlayer.so`)와 Burst 생성물뿐이고 모두 aarch64.
- 실행은 하지 않았다(오라클에서 #383). 즉 "빌드되고 ARM64 ELF다"까지가 확인이고, 실제 기동·접속은 미확인.

## 시행착오 (재현용)
1. 처음엔 Mono로 `StandaloneBuildSubtarget.Server`만 지정 → 성공했지만 **x86-64**가 나왔다(아키텍처 미지정, 274 MB). 성공 로그만 보면 속기 쉬움 → 반드시 `file`로 확인.
2. `-architecture ARM64` 명령줄 인자와 `SetPlatformSettings("Standalone"|"LinuxStandalone", "Architecture", ...)`는 효과가 없었다(x86-64 그대로).
3. 효과가 있는 키: `EditorUserBuildSettings.SetPlatformSettings("Linux64", "Architecture", "ARM64")`(프로브로 확인: 잘못된 플랫폼 이름은 읽기도 빈 문자열, `Linux64`만 값을 정규화).
4. ARM64는 IL2CPP 전용이라 Mono를 지정하면 변형이 없다. `BuildScript.BuildServerLinuxArm64`는 `ScriptingImplementation.IL2CPP` + `NamedBuildTarget.Server`.

## 남은 위험 (오라클에서 #383이 확인)
- glibc: Unity는 `GLIBC` 최소 버전을 요구한다. Ubuntu 24.04(glibc 2.39)면 충분할 것으로 보지만 실행으로 확인 필요.
- 서버 서브타깃에서 빠진 것(렌더 전용 에셋)이 서버 로직(NavMesh, 월드 메시 `World_Meshes.bytes` 등)을 깨는지는 실행해 봐야 안다. 현재 Mac 서버는 서브타깃이 Player(일반 빌드)이므로 동작이 다를 수 있다.
- 실패 시 대안(이번엔 필요 없었음): x86_64 서버 빌드를 `qemu-user`/box64로 에뮬레이션(느림, 비권장), 오라클 AMD 마이크로(1/8 OCPU, 1 GB — 80인 서버엔 부족).
